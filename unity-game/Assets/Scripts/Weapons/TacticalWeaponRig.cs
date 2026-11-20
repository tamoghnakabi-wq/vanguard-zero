using UnityEngine;
using VanguardZero.Core;
using VanguardZero.Networking;
using VanguardZero.VFX;

namespace VanguardZero.Weapons
{
    /// <summary>
    /// First-person viewmodel & procedural weapon animation rig for VX-47, M890, and M19.
    /// Drives spring-damper recoil, mouse sway, gait bob, ADS optic alignment,
    /// reload choreography, reciprocating bolt/slide, muzzle flash light, and brass ejection.
    /// </summary>
    public class TacticalWeaponRig : MonoBehaviour
    {
        [Header("Weapon Viewmodels (0=VX47, 1=M890, 2=M19)")]
        [SerializeField] private Transform[] weaponRoots = new Transform[3];
        [SerializeField] private Transform[] muzzlePoints = new Transform[3];
        [SerializeField] private Transform[] ejectPorts = new Transform[3];
        [SerializeField] private Transform[] movingBolts = new Transform[3];
        [SerializeField] private Transform[] magazines = new Transform[3];

        [Header("Pose Offsets")]
        [SerializeField] private Vector3 hipOffset = new Vector3(0.19f, -0.15f, 0.24f);
        [SerializeField] private Vector3 adsOffsetRifle = new Vector3(0.0f, -0.094f, 0.13f);
        [SerializeField] private Vector3 adsOffsetShotgun = new Vector3(0.0f, -0.072f, 0.16f);
        [SerializeField] private Vector3 adsOffsetPistol = new Vector3(0.0f, -0.064f, 0.24f);
        [SerializeField] private Vector3 sprintOffset = new Vector3(0.10f, -0.21f, 0.18f);
        [SerializeField] private Vector3 sprintEuler = new Vector3(14f, -34f, 18f);

        private Transform _fpArmsRoot;
        private WeaponId _activeWeapon = WeaponId.VX47_AssaultRifle;
        private float _adsBlend;
        private float _sprintBlend;
        private float _reloadBlend;
        private float _reloadProgress;
        private float _equipLerp = 1f;

        private Vector3 _recoilPos;
        private Vector3 _recoilPosVel;
        private Vector3 _recoilRot;
        private Vector3 _recoilRotVel;
        private float _boltBlowback;

        private Vector2 _swayLookSmooth;
        private float _bobPhase;
        private Light _muzzleLight;
        private Transform _muzzleFlashMesh;
        private float _muzzleFlashTimer;

        public float AdsBlend => _adsBlend;

        private void Awake()
        {
            // Enforce v2.0 FPS viewmodel offsets: gloved Crye sleeves + wrist GPS visibly grip weapon in hipfire, clean ADS
            hipOffset = new Vector3(0.19f, -0.15f, 0.24f);
            adsOffsetRifle = new Vector3(0.0f, -0.094f, 0.13f);
            adsOffsetShotgun = new Vector3(0.0f, -0.072f, 0.16f);
            adsOffsetPistol = new Vector3(0.0f, -0.064f, 0.24f);
            sprintOffset = new Vector3(0.10f, -0.21f, 0.18f);

            AutoDiscoverWeaponParts();
            EnsureMuzzleLight();
            SetActiveWeaponModel(_activeWeapon);
        }

        public void AutoDiscoverWeaponParts()
        {
            for (int i = 0; i < 3; i++)
            {
                if (weaponRoots[i] == null)
                {
                    var child = transform.Find($"WeaponModel_{i}");
                    if (child != null) weaponRoots[i] = child;
                }

                var root = weaponRoots[i];
                if (root == null) continue;

                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if (muzzlePoints[i] == null && n.Contains("MuzzlePoint")) muzzlePoints[i] = t;
                    else if (ejectPorts[i] == null && n.Contains("EjectPort")) ejectPorts[i] = t;
                    else if (movingBolts[i] == null && (n.Contains("Bolt") || n.Contains("Slide") || n.Contains("Pump") || n.Contains("Forend"))) movingBolts[i] = t;
                    else if (magazines[i] == null && (n.Contains("Magazine") || n.Contains("Mag"))) magazines[i] = t;
                }
            }
        }

        private void EnsureMuzzleLight()
        {
            _fpArmsRoot = transform.Find("FP_TacticalArms");

            var lightGo = transform.Find("MuzzleFlashLight");
            if (lightGo == null)
            {
                var go = new GameObject("MuzzleFlashLight");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0.15f, -0.08f, 0.85f);
                _muzzleLight = go.AddComponent<Light>();
                _muzzleLight.type = LightType.Point;
                _muzzleLight.color = VanguardPalette.AccentAmber;
                _muzzleLight.range = 7.5f;
                _muzzleLight.intensity = 0f;
                _muzzleLight.shadows = LightShadows.None;
            }
            else
            {
                _muzzleLight = lightGo.GetComponent<Light>();
            }

            var flashMesh = transform.Find("MuzzleFlashStar");
            if (flashMesh != null) Destroy(flashMesh.gameObject);

            // Crossed 2-plane transparent additive star muzzle flash using alpha-feathered PNG sprite (ZERO square edges!)
            var starRoot = new GameObject("MuzzleFlashStar");
            starRoot.transform.SetParent(transform, false);
            var flashTex = Resources.Load<Texture2D>("VFX/T_MuzzleFlash_Star");
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = VanguardVfxManager.CreateTransparentParticleMat(shader, flashTex, additive: true);
            mat.SetColor("_BaseColor", new Color(1.0f, 0.86f, 0.56f, 1.0f) * 1.9f);

            for (int p = 0; p < 2; p++)
            {
                var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                plane.name = $"FlashPlane_{p}";
                plane.transform.SetParent(starRoot.transform, false);
                plane.transform.localRotation = p == 0 ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                if (plane.TryGetComponent<Collider>(out var c)) Destroy(c);
                var r = plane.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _muzzleFlashMesh = starRoot.transform;
            _muzzleFlashMesh.gameObject.SetActive(false);
        }

        public void SetActiveWeaponModel(WeaponId weapon)
        {
            _activeWeapon = weapon;
            _equipLerp = 0f;
            int idx = (int)weapon;
            for (int i = 0; i < weaponRoots.Length; i++)
            {
                if (weaponRoots[i] != null)
                    weaponRoots[i].gameObject.SetActive(i == idx);
            }
        }

        public Vector3 GetCurrentMuzzleWorldPosition(Transform fallbackCamera)
        {
            int idx = (int)_activeWeapon;
            if (idx >= 0 && idx < muzzlePoints.Length && muzzlePoints[idx] != null)
                return muzzlePoints[idx].position;
            if (fallbackCamera != null)
                return fallbackCamera.position + fallbackCamera.forward * 0.65f + fallbackCamera.right * 0.14f - fallbackCamera.up * 0.10f;
            return transform.position + transform.forward * 0.65f;
        }

        public void TriggerFirePresentation(WeaponId weapon, Vector3 carrierVelocity, out float cameraPitchKick, out float cameraYawKick)
        {
            float adsFactor = Mathf.Lerp(1f, 0.58f, _adsBlend);

            switch (weapon)
            {
                case WeaponId.M890_TacticalShotgun:
                    _recoilPosVel += new Vector3(UnityEngine.Random.Range(-0.08f, 0.08f), 0.18f, -1.35f) * adsFactor;
                    _recoilRotVel += new Vector3(-115f, UnityEngine.Random.Range(-22f, 22f), UnityEngine.Random.Range(-28f, 28f)) * adsFactor;
                    cameraPitchKick = 3.2f * adsFactor;
                    cameraYawKick = UnityEngine.Random.Range(-0.85f, 0.85f) * adsFactor;
                    break;

                case WeaponId.M19_TacticalPistol:
                    _recoilPosVel += new Vector3(UnityEngine.Random.Range(-0.03f, 0.03f), 0.08f, -0.58f) * adsFactor;
                    _recoilRotVel += new Vector3(-62f, UnityEngine.Random.Range(-10f, 10f), UnityEngine.Random.Range(-14f, 14f)) * adsFactor;
                    cameraPitchKick = 1.15f * adsFactor;
                    cameraYawKick = UnityEngine.Random.Range(-0.35f, 0.35f) * adsFactor;
                    break;

                default:
                    _recoilPosVel += new Vector3(UnityEngine.Random.Range(-0.035f, 0.035f), 0.055f, -0.72f) * adsFactor;
                    _recoilRotVel += new Vector3(-46f, UnityEngine.Random.Range(-14f, 14f), UnityEngine.Random.Range(-16f, 16f)) * adsFactor;
                    cameraPitchKick = 0.82f * adsFactor;
                    cameraYawKick = UnityEngine.Random.Range(-0.32f, 0.32f) * adsFactor;
                    break;
            }

            _boltBlowback = 1f;
            _muzzleFlashTimer = 0.048f;

            Vector3 muzzlePos = GetCurrentMuzzleWorldPosition(transform);
            if (_muzzleLight != null)
            {
                _muzzleLight.transform.position = muzzlePos;
                _muzzleLight.intensity = 6.5f;
            }
            if (_muzzleFlashMesh != null)
            {
                _muzzleFlashMesh.position = muzzlePos + transform.forward * 0.06f;
                _muzzleFlashMesh.rotation = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                float s = weapon == WeaponId.M890_TacticalShotgun ? 0.28f : (weapon == WeaponId.M19_TacticalPistol ? 0.16f : 0.22f);
                _muzzleFlashMesh.localScale = new Vector3(s, s, s * 1.35f);
                _muzzleFlashMesh.gameObject.SetActive(true);
            }

            VanguardVfxManager.Instance?.SpawnMuzzleBurst(muzzlePos, transform.forward);

            int idx = (int)weapon;
            Vector3 ejectPos = (idx >= 0 && idx < ejectPorts.Length && ejectPorts[idx] != null)
                ? ejectPorts[idx].position
                : (muzzlePos - transform.forward * 0.28f + transform.right * 0.05f);
            VanguardVfxManager.Instance?.SpawnShellCasing(ejectPos, transform.right, transform.up, carrierVelocity);
        }

        public void TickRig(
            float dt,
            Vector2 lookDelta,
            Vector3 planarVelocity,
            bool isGrounded,
            bool isAds,
            bool isSprinting,
            bool isSliding,
            bool isReloading,
            float reloadNormalized)
        {
            _adsBlend = Mathf.MoveTowards(_adsBlend, (isAds && !isReloading && !isSprinting) ? 1f : 0f, dt * 8.5f);
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, (isSprinting && !isAds && !isReloading) ? 1f : 0f, dt * 6.5f);
            _reloadBlend = Mathf.MoveTowards(_reloadBlend, isReloading ? 1f : 0f, dt * 7.0f);
            _reloadProgress = reloadNormalized;
            _equipLerp = Mathf.MoveTowards(_equipLerp, 1f, dt * 4.5f);

            if (_muzzleFlashTimer > 0f)
            {
                _muzzleFlashTimer -= dt;
                if (_muzzleFlashTimer <= 0f)
                {
                    if (_muzzleLight != null) _muzzleLight.intensity = 0f;
                    if (_muzzleFlashMesh != null) _muzzleFlashMesh.gameObject.SetActive(false);
                }
            }

            const float posStiffness = 190f;
            const float posDamping = 22f;
            _recoilPosVel += (-_recoilPos * posStiffness - _recoilPosVel * posDamping) * dt;
            _recoilPos += _recoilPosVel * dt;

            const float rotStiffness = 220f;
            const float rotDamping = 24f;
            _recoilRotVel += (-_recoilRot * rotStiffness - _recoilRotVel * rotDamping) * dt;
            _recoilRot += _recoilRotVel * dt;

            _boltBlowback = Mathf.MoveTowards(_boltBlowback, 0f, dt * 16f);
            int wIdx = (int)_activeWeapon;
            if (wIdx >= 0 && wIdx < movingBolts.Length && movingBolts[wIdx] != null)
            {
                Vector3 boltLocal = movingBolts[wIdx].localPosition;
                boltLocal.z = -_boltBlowback * 0.055f;
                movingBolts[wIdx].localPosition = boltLocal;
            }

            if (wIdx >= 0 && wIdx < magazines.Length && magazines[wIdx] != null)
            {
                float magDrop = isReloading ? Mathf.Sin(Mathf.Clamp01(reloadNormalized) * Mathf.PI) * -0.14f : 0f;
                Vector3 magLocal = magazines[wIdx].localPosition;
                magLocal.y = -0.07f + magDrop;
                magazines[wIdx].localPosition = magLocal;
            }

            _swayLookSmooth = Vector2.Lerp(_swayLookSmooth, Vector2.ClampMagnitude(lookDelta, 14f), dt * 12f);
            float swayMult = Mathf.Lerp(1f, 0.25f, _adsBlend);

            float speed = planarVelocity.magnitude;
            float speedNorm = isGrounded ? Mathf.Clamp01(speed / 6.8f) : 0.1f;
            _bobPhase += dt * Mathf.Lerp(1.6f, 11.5f, speedNorm);

            float bobX = Mathf.Cos(_bobPhase) * 0.011f * speedNorm * swayMult;
            float bobY = Mathf.Sin(_bobPhase * 2f) * 0.009f * speedNorm * swayMult;
            float breatheY = Mathf.Sin(Time.time * 2.1f) * 0.0025f * swayMult;

            Vector3 targetAdsOffset = _activeWeapon switch
            {
                WeaponId.M890_TacticalShotgun => adsOffsetShotgun,
                WeaponId.M19_TacticalPistol => adsOffsetPistol,
                _ => adsOffsetRifle
            };

            Vector3 basePos = Vector3.Lerp(hipOffset, targetAdsOffset, Ease.InOutSine(_adsBlend));
            basePos = Vector3.Lerp(basePos, sprintOffset, Ease.InOutSine(_sprintBlend));

            float reloadWave = Mathf.Sin(_reloadProgress * Mathf.PI);
            Vector3 reloadPosOffset = new Vector3(-0.04f, -0.09f * reloadWave, -0.04f) * _reloadBlend;
            Vector3 reloadRotOffset = new Vector3(18f * reloadWave, 14f, -24f) * _reloadBlend;

            float equipDip = (1f - Ease.OutCubic(_equipLerp)) * -0.22f;
            float slideRoll = isSliding ? -14f : 0f;

            Vector3 swayPos = new Vector3(
                -_swayLookSmooth.x * 0.0022f * swayMult + bobX,
                -_swayLookSmooth.y * 0.0022f * swayMult + bobY + breatheY + equipDip,
                0f);

            Vector3 swayRot = new Vector3(
                _swayLookSmooth.y * 0.65f * swayMult,
                -_swayLookSmooth.x * 0.85f * swayMult,
                -_swayLookSmooth.x * 1.15f * swayMult + slideRoll);

            Vector3 finalPos = basePos + swayPos + reloadPosOffset + _recoilPos;
            Vector3 finalEuler = Vector3.Lerp(Vector3.zero, sprintEuler, Ease.InOutSine(_sprintBlend))
                                 + swayRot + reloadRotOffset + _recoilRot;

            if (wIdx >= 0 && wIdx < weaponRoots.Length && weaponRoots[wIdx] != null)
            {
                weaponRoots[wIdx].localPosition = finalPos;
                weaponRoots[wIdx].localRotation = Quaternion.Euler(finalEuler);
            }

            // Keep gloved first-person arms locked cleanly to the active weapon's pose & recoil!
            if (_fpArmsRoot != null)
            {
                _fpArmsRoot.localPosition = finalPos + new Vector3(0f, -0.008f, 0.01f);
                _fpArmsRoot.localRotation = Quaternion.Euler(finalEuler);
            }
        }
    }
}
