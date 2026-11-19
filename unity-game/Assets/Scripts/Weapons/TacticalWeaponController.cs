using System;
using UnityEngine;
using VanguardZero.Core;
using VanguardZero.Networking;
using VanguardZero.VFX;

namespace VanguardZero.Weapons
{
    /// <summary>
    /// Manages local and remote weapon inventory (VX-47, M890, M19), magazine/reserve ammo,
    /// reload choreography, client-side immediate hitscan + tracer presentation, and
    /// authoritative server shot submission via IPlayerCombatAuthority.
    /// </summary>
    public class TacticalWeaponController : MonoBehaviour
    {
        private readonly int[] _magAmmo = new int[3];
        private readonly int[] _reserveAmmo = new int[3];

        private WeaponId _currentWeapon = WeaponId.VX47_AssaultRifle;
        private IPlayerCombatAuthority _combatAuthority;
        private TacticalWeaponRig _weaponRig;
        private Camera _fpCamera;

        private float _nextFireTime;
        private bool _isReloading;
        private float _reloadTimer;
        private float _reloadDuration;
        private int _reloadAudioStage;
        private uint _shotSequence;
        private float _dynamicSpreadBonus;
        private bool _wasAds;

        public WeaponId CurrentWeapon => _currentWeapon;
        public bool IsReloading => _isReloading;
        public float ReloadNormalized => _isReloading && _reloadDuration > 0.01f ? Mathf.Clamp01(_reloadTimer / _reloadDuration) : 0f;
        public int CurrentMagAmmo => _magAmmo[(int)_currentWeapon];
        public int CurrentReserveAmmo => _reserveAmmo[(int)_currentWeapon];
        public float CurrentSpreadDegrees { get; private set; } = 1.2f;

        public event Action<WeaponId, int, int> OnWeaponAmmoChanged;
        public event Action<HitConfirmEvent> OnLocalHitConfirmed;
        public event Action<float, float> OnRecoilCameraKick;

        public void Initialize(IPlayerCombatAuthority authority, TacticalWeaponRig rig, Camera fpCam)
        {
            if (_combatAuthority != null)
            {
                _combatAuthority.OnShotVisualReceived -= HandleShotVisualReceived;
                _combatAuthority.OnHitConfirmed -= HandleHitConfirmed;
                _combatAuthority.OnAliveStateChanged -= HandleAliveStateChanged;
            }

            _combatAuthority = authority;
            _weaponRig = rig;
            _fpCamera = fpCam;

            ResetLoadoutAmmo();

            if (_combatAuthority != null)
            {
                _combatAuthority.OnShotVisualReceived += HandleShotVisualReceived;
                _combatAuthority.OnHitConfirmed += HandleHitConfirmed;
                _combatAuthority.OnAliveStateChanged += HandleAliveStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (_combatAuthority != null)
            {
                _combatAuthority.OnShotVisualReceived -= HandleShotVisualReceived;
                _combatAuthority.OnHitConfirmed -= HandleHitConfirmed;
                _combatAuthority.OnAliveStateChanged -= HandleAliveStateChanged;
            }
        }

        public void ResetLoadoutAmmo()
        {
            for (int i = 0; i < 3; i++)
            {
                var spec = VanguardNetServices.GetWeaponSpec((WeaponId)i);
                _magAmmo[i] = spec.MagazineSize;
                _reserveAmmo[i] = spec.MagazineSize * 5;
            }
            _isReloading = false;
            _reloadTimer = 0f;
            _dynamicSpreadBonus = 0f;
            NotifyAmmoChanged();
        }

        public void SwitchWeapon(WeaponId newWeapon)
        {
            if (_currentWeapon == newWeapon) return;
            _currentWeapon = newWeapon;
            _isReloading = false;
            _reloadTimer = 0f;
            _nextFireTime = Time.time + 0.22f;

            _weaponRig?.SetActiveWeaponModel(_currentWeapon);
            _combatAuthority?.SubmitWeaponSwitch(_currentWeapon);
            VanguardAudioManager.Instance?.Play2D("weapon_equip", AudioBus.Weapons, 0.65f, 0.04f);
            NotifyAmmoChanged();
        }

        public void StartReload()
        {
            if (_isReloading) return;
            int idx = (int)_currentWeapon;
            var spec = VanguardNetServices.GetWeaponSpec(_currentWeapon);
            if (_magAmmo[idx] >= spec.MagazineSize || _reserveAmmo[idx] <= 0) return;

            _isReloading = true;
            _reloadTimer = 0f;
            _reloadDuration = spec.ReloadDurationSeconds;
            _reloadAudioStage = 0;

            _combatAuthority?.SubmitReloadStart();

            string startClip = _currentWeapon switch
            {
                WeaponId.VX47_AssaultRifle => "vx47_reload_magout",
                WeaponId.M890_TacticalShotgun => "m890_shell_insert",
                _ => "m19_reload"
            };
            VanguardAudioManager.Instance?.Play2D(startClip, AudioBus.Weapons, 0.8f, 0.02f);
        }

        public void TickWeapons(
            float dt,
            bool fireHeld,
            bool firePressedThisFrame,
            bool reloadPressed,
            bool isAds,
            bool isSprinting,
            Vector3 velocity)
        {
            if (isAds != _wasAds)
            {
                _wasAds = isAds;
                VanguardAudioManager.Instance?.Play2D(isAds ? "weapon_ads_in" : "weapon_ads_out", AudioBus.Weapons, 0.45f, 0.03f);
            }

            var spec = VanguardNetServices.GetWeaponSpec(_currentWeapon);
            float movePenalty = Mathf.Clamp01(new Vector3(velocity.x, 0f, velocity.z).magnitude / 6.5f) * 1.45f;
            float adsMult = isAds ? 0.32f : 1.0f;
            _dynamicSpreadBonus = Mathf.MoveTowards(_dynamicSpreadBonus, 0f, dt * 5.2f);
            CurrentSpreadDegrees = (spec.SpreadDegrees + movePenalty + _dynamicSpreadBonus) * adsMult;

            if (_isReloading)
            {
                _reloadTimer += dt;
                float norm = ReloadNormalized;

                if (_currentWeapon == WeaponId.VX47_AssaultRifle)
                {
                    if (_reloadAudioStage == 0 && norm >= 0.48f)
                    {
                        _reloadAudioStage = 1;
                        VanguardAudioManager.Instance?.Play2D("vx47_reload_magin", AudioBus.Weapons, 0.82f, 0.02f);
                    }
                    else if (_reloadAudioStage == 1 && norm >= 0.80f)
                    {
                        _reloadAudioStage = 2;
                        VanguardAudioManager.Instance?.Play2D("vx47_reload_bolt", AudioBus.Weapons, 0.85f, 0.02f);
                    }
                }
                else if (_currentWeapon == WeaponId.M890_TacticalShotgun)
                {
                    if (_reloadAudioStage == 0 && norm >= 0.45f)
                    {
                        _reloadAudioStage = 1;
                        VanguardAudioManager.Instance?.Play2D("m890_shell_insert", AudioBus.Weapons, 0.82f, 0.04f);
                    }
                    else if (_reloadAudioStage == 1 && norm >= 0.78f)
                    {
                        _reloadAudioStage = 2;
                        VanguardAudioManager.Instance?.Play2D("m890_pump", AudioBus.Weapons, 0.88f, 0.02f);
                    }
                }

                if (_reloadTimer >= _reloadDuration)
                {
                    CompleteReload();
                }
                return;
            }

            if (reloadPressed)
            {
                StartReload();
                return;
            }

            bool wantsToShoot = _currentWeapon == WeaponId.VX47_AssaultRifle ? fireHeld : firePressedThisFrame;
            if (wantsToShoot && Time.time >= _nextFireTime)
            {
                TryFireCurrentWeapon(spec, isAds, velocity);
            }
        }

        private void CompleteReload()
        {
            _isReloading = false;
            int idx = (int)_currentWeapon;
            var spec = VanguardNetServices.GetWeaponSpec(_currentWeapon);
            int needed = spec.MagazineSize - _magAmmo[idx];
            int take = Mathf.Min(needed, _reserveAmmo[idx]);
            _magAmmo[idx] += take;
            _reserveAmmo[idx] -= take;
            NotifyAmmoChanged();
        }

        public bool TryFireCurrentWeapon(WeaponBallisticsSpec spec, bool isAds, Vector3 carrierVelocity)
        {
            int idx = (int)_currentWeapon;
            if (_magAmmo[idx] <= 0)
            {
                _nextFireTime = Time.time + 0.25f;
                VanguardAudioManager.Instance?.Play2D("weapon_dryfire", AudioBus.Weapons, 0.75f, 0.03f);
                if (_reserveAmmo[idx] > 0) StartReload();
                return false;
            }

            _magAmmo[idx]--;
            _nextFireTime = Time.time + spec.FireIntervalSeconds;
            _shotSequence++;
            _dynamicSpreadBonus = Mathf.Min(3.5f, _dynamicSpreadBonus + (isAds ? 0.28f : 0.55f));
            NotifyAmmoChanged();

            switch (_currentWeapon)
            {
                case WeaponId.VX47_AssaultRifle:
                    VanguardAudioManager.Instance?.PlayRandom2D("vx47_fire", 3, AudioBus.Weapons, 0.92f, 0.035f);
                    break;
                case WeaponId.M890_TacticalShotgun:
                    VanguardAudioManager.Instance?.PlayRandom2D("m890_fire", 2, AudioBus.Weapons, 0.98f, 0.03f);
                    break;
                case WeaponId.M19_TacticalPistol:
                    VanguardAudioManager.Instance?.PlayRandom2D("m19_fire", 2, AudioBus.Weapons, 0.88f, 0.04f);
                    break;
            }

            float pitchKick = 0.8f;
            float yawKick = 0f;
            if (_weaponRig != null)
            {
                _weaponRig.TriggerFirePresentation(_currentWeapon, carrierVelocity, out pitchKick, out yawKick);
            }
            OnRecoilCameraKick?.Invoke(pitchKick, yawKick);

            Transform aimTf = _fpCamera != null ? _fpCamera.transform : transform;
            Vector3 aimOrigin = aimTf.position;
            Vector3 aimDir = ApplySpread(aimTf.forward, _currentWeapon == WeaponId.M890_TacticalShotgun ? 0.35f : CurrentSpreadDegrees * 0.35f);
            Vector3 muzzlePos = _weaponRig != null ? _weaponRig.GetCurrentMuzzleWorldPosition(aimTf) : (aimOrigin + aimTf.forward * 0.6f);

            RenderLocalShotTracers(muzzlePos, aimOrigin, aimDir, spec);

            var fireInput = new WeaponFireInput
            {
                Weapon = _currentWeapon,
                MuzzlePosition = muzzlePos,
                AimOrigin = aimOrigin,
                AimDirection = aimDir,
                ShotSequence = _shotSequence,
                ClientTimeSeconds = Time.time
            };
            _combatAuthority?.SubmitWeaponFire(in fireInput);

            if (_magAmmo[idx] == 0 && _reserveAmmo[idx] > 0)
            {
                StartReload();
            }

            return true;
        }

        private void RenderLocalShotTracers(Vector3 muzzlePos, Vector3 aimOrigin, Vector3 aimDir, WeaponBallisticsSpec spec)
        {
            int localPlayerLayer = LayerMask.NameToLayer("LocalPlayer");
            int viewModelLayer = LayerMask.NameToLayer("ViewModel");
            int mask = ~0;
            if (localPlayerLayer >= 0) mask &= ~(1 << localPlayerLayer);
            if (viewModelLayer >= 0) mask &= ~(1 << viewModelLayer);

            int pellets = Mathf.Max(1, spec.PelletsPerShot);
            for (int p = 0; p < pellets; p++)
            {
                Vector3 dir = p == 0 ? aimDir : ApplySpread(aimDir, spec.SpreadDegrees);
                if (Physics.Raycast(aimOrigin, dir, out RaycastHit hit, spec.MaxRange, mask, QueryTriggerInteraction.Ignore))
                {
                    VanguardVfxManager.Instance?.SpawnTracer(muzzlePos, hit.point, pellets > 1);
                    var surf = VanguardSurfaceTag.ResolveFromCollider(hit.collider);
                    VanguardVfxManager.Instance?.SpawnImpact(hit.point, hit.normal, surf);
                }
                else
                {
                    VanguardVfxManager.Instance?.SpawnTracer(muzzlePos, aimOrigin + dir * spec.MaxRange, pellets > 1);
                }
            }
        }

        private static Vector3 ApplySpread(Vector3 forward, float spreadDegrees)
        {
            if (spreadDegrees <= 0.001f) return forward.normalized;
            float rad = spreadDegrees * Mathf.Deg2Rad;
            Vector2 disk = UnityEngine.Random.insideUnitCircle * Mathf.Tan(rad);
            Quaternion basis = Quaternion.LookRotation(forward);
            return (forward + basis * new Vector3(disk.x, disk.y, 0f)).normalized;
        }

        private void HandleShotVisualReceived(WeaponShotVisualEvent ev)
        {
            if (ev.IsLocalShooter) return;

            string prefix = ev.Weapon switch
            {
                WeaponId.M890_TacticalShotgun => "m890_fire",
                WeaponId.M19_TacticalPistol => "m19_fire",
                _ => "vx47_fire"
            };
            int variations = ev.Weapon == WeaponId.VX47_AssaultRifle ? 3 : 2;
            VanguardAudioManager.Instance?.PlayRandom3D(prefix, variations, ev.MuzzlePosition, AudioBus.Weapons, 0.88f, 0.04f, 95f);

            VanguardVfxManager.Instance?.SpawnMuzzleBurst(ev.MuzzlePosition, (ev.HitPoint - ev.MuzzlePosition).normalized);
            VanguardVfxManager.Instance?.SpawnTracer(ev.MuzzlePosition, ev.HitPoint, ev.Weapon == WeaponId.M890_TacticalShotgun);

            if (ev.DidHit)
            {
                VanguardVfxManager.Instance?.SpawnImpact(
                    ev.HitPoint,
                    ev.HitNormal,
                    (VanguardSurfaceTag.SurfaceKind)ev.Surface);
            }
        }

        private void HandleHitConfirmed(HitConfirmEvent hit)
        {
            if (hit.IsKill)
            {
                VanguardAudioManager.Instance?.Play2D("kill_confirm", AudioBus.UI, 0.92f, 0.0f);
                if (hit.IsHeadshot)
                    VanguardAudioManager.Instance?.Play2D("vo_headshot", AudioBus.Voice, 0.85f, 0.0f);
            }
            else if (hit.IsHeadshot)
            {
                VanguardAudioManager.Instance?.Play2D("hitmarker_headshot", AudioBus.UI, 0.90f, 0.02f);
            }
            else
            {
                VanguardAudioManager.Instance?.Play2D("hitmarker_body", AudioBus.UI, 0.82f, 0.03f);
            }

            OnLocalHitConfirmed?.Invoke(hit);
        }

        private void HandleAliveStateChanged(bool alive)
        {
            if (alive)
            {
                ResetLoadoutAmmo();
            }
        }

        private void NotifyAmmoChanged()
        {
            int idx = (int)_currentWeapon;
            OnWeaponAmmoChanged?.Invoke(_currentWeapon, _magAmmo[idx], _reserveAmmo[idx]);
        }
    }
}
