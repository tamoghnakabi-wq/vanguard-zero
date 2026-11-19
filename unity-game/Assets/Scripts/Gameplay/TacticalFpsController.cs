using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using VanguardZero.Core;
using VanguardZero.Networking;
using VanguardZero.Weapons;

namespace VanguardZero.Gameplay
{
    /// <summary>
    /// AAA First-Person Tactical Controller for VANGUARD ZERO.
    /// Drives smooth walk/sprint/crouch/slide/jump/stamina, Q/E tactical lean, ADS FOV zoom,
    /// camera bob + recoil kick + surface-aware footsteps, and cleanly separates local FPS
    /// input from remote 3rd-person visual interpolation via IPlayerCombatAuthority.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TacticalFpsController : MonoBehaviour
    {
        public static TacticalFpsController LocalInstance { get; private set; }
        public static event Action<TacticalFpsController> OnLocalPlayerReady;
        public static event Action<int, int, Vector3> OnLocalVitalsUpdated;

        [Header("Movement Tuning")]
        [SerializeField] private float walkSpeed = 4.7f;
        [SerializeField] private float sprintSpeed = 7.3f;
        [SerializeField] private float crouchSpeed = 2.75f;
        [SerializeField] private float slideInitialSpeed = 9.8f;
        [SerializeField] private float slideDuration = 0.62f;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float gravity = -24.5f;
        [SerializeField] private float standingHeight = 1.82f;
        [SerializeField] private float crouchHeight = 1.15f;
        [SerializeField] private float standingEyeY = 1.64f;
        [SerializeField] private float crouchEyeY = 1.02f;

        [Header("References")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera fpCamera;
        [SerializeField] private TacticalWeaponRig weaponRig;
        [SerializeField] private TacticalWeaponController weaponController;
        [SerializeField] private TacticalOperatorRig operatorRig;

        private CharacterController _cc;
        private IPlayerCombatAuthority _authority;
        private AudioListener _audioListener;

        private float _yaw;
        private float _pitch;
        private float _verticalVelocity;
        private Vector3 _planarVelocity;
        private float _stamina = 100f;
        private bool _isCrouching;
        private bool _isSliding;
        private float _slideTimer;
        private Vector3 _slideDir;
        private float _leanCurrent;
        private float _headBobPhase;
        private float _stepAccum;
        private bool _wasGrounded = true;
        private float _cameraRoll;
        private float _damageFlinchRoll;
        private float _recoilPitchAccum;
        private bool _isPausedOrMenu;
        private bool _localConfigured;

        // Optional automated verification input override (zero effect during normal play)
        private bool _useSyntheticInput;
        private Vector2 _syntheticMove;
        private Vector2 _syntheticLook;
        private bool _syntheticFire;
        private bool _syntheticAds;
        private bool _syntheticSprint;
        private bool _syntheticCrouch;

        public IPlayerCombatAuthority Authority => _authority;
        public TacticalWeaponController Weapons => weaponController;
        public TacticalWeaponRig WeaponRig => weaponRig;
        public Camera FirstPersonCamera => fpCamera;
        public float Stamina => _stamina;
        public float MaxStamina => 100f;
        public bool IsAds { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching => _isCrouching || _isSliding;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _authority = GetComponent<IPlayerCombatAuthority>();
            if (weaponController == null) weaponController = GetComponent<TacticalWeaponController>();
            if (weaponRig == null) weaponRig = GetComponentInChildren<TacticalWeaponRig>(true);
            if (operatorRig == null) operatorRig = GetComponentInChildren<TacticalOperatorRig>(true);
            if (fpCamera == null) fpCamera = GetComponentInChildren<Camera>(true);
            if (cameraPivot == null && fpCamera != null) cameraPivot = fpCamera.transform.parent;
            if (fpCamera != null) _audioListener = fpCamera.GetComponent<AudioListener>();
        }

        private void Start()
        {
            BindAuthority();
        }

        private void OnDestroy()
        {
            UnbindAuthority();
            if (LocalInstance == this) LocalInstance = null;
        }

        private void BindAuthority()
        {
            if (_authority == null) _authority = GetComponent<IPlayerCombatAuthority>();
            if (_authority == null) return;

            _authority.OnAuthoritativeTeleport += HandleAuthoritativeTeleport;
            _authority.OnAliveStateChanged += HandleAliveStateChanged;
            _authority.OnTeamAssigned += HandleTeamAssigned;
            _authority.OnHealthArmorChanged += HandleHealthArmorChanged;
            _authority.OnRemotePoseReceived += HandleRemotePoseReceived;

            ConfigureLocalVsRemote(_authority.IsLocalPlayer);
            operatorRig?.ApplyTeamVisuals(_authority.Team);
        }

        private void UnbindAuthority()
        {
            if (_authority == null) return;
            _authority.OnAuthoritativeTeleport -= HandleAuthoritativeTeleport;
            _authority.OnAliveStateChanged -= HandleAliveStateChanged;
            _authority.OnTeamAssigned -= HandleTeamAssigned;
            _authority.OnHealthArmorChanged -= HandleHealthArmorChanged;
            _authority.OnRemotePoseReceived -= HandleRemotePoseReceived;
        }

        private void ConfigureLocalVsRemote(bool isLocal)
        {
            _localConfigured = true;
            if (isLocal)
            {
                LocalInstance = this;
                _yaw = transform.eulerAngles.y;

                // Disable any scene fallback camera so local FP camera is Camera.main
                foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (cam != fpCamera && cam.CompareTag("MainCamera"))
                    {
                        cam.gameObject.SetActive(false);
                    }
                }

                if (fpCamera != null)
                {
                    fpCamera.gameObject.SetActive(true);
                    fpCamera.tag = "MainCamera";
                    if (fpCamera.TryGetComponent<UniversalAdditionalCameraData>(out var uac))
                    {
                        uac.renderPostProcessing = true;
                        uac.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    }
                }

                if (_audioListener != null) _audioListener.enabled = true;
                if (weaponRig != null) weaponRig.gameObject.SetActive(true);
                if (operatorRig != null) operatorRig.SetHiddenForLocalFirstPerson(true);

                int localLayer = LayerMask.NameToLayer("LocalPlayer");
                if (localLayer >= 0) gameObject.layer = localLayer;

                if (weaponController != null)
                {
                    weaponController.Initialize(_authority, weaponRig, fpCamera);
                    weaponController.OnRecoilCameraKick += HandleRecoilCameraKick;
                }

                _cc.enabled = true;
                OnLocalPlayerReady?.Invoke(this);
            }
            else
            {
                if (fpCamera != null) fpCamera.gameObject.SetActive(false);
                if (_audioListener != null) _audioListener.enabled = false;
                if (weaponRig != null) weaponRig.gameObject.SetActive(false);
                if (operatorRig != null) operatorRig.SetHiddenForLocalFirstPerson(false);
                if (weaponController != null) weaponController.Initialize(_authority, null, null);
                _cc.enabled = false;
            }
        }

        public void SetMenuOrPauseLock(bool locked)
        {
            _isPausedOrMenu = locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        public void SetSyntheticInput(bool enabled, Vector2 move = default, Vector2 look = default, bool fire = false, bool ads = false, bool sprint = false, bool crouch = false)
        {
            _useSyntheticInput = enabled;
            _syntheticMove = move;
            _syntheticLook = look;
            _syntheticFire = fire;
            _syntheticAds = ads;
            _syntheticSprint = sprint;
            _syntheticCrouch = crouch;
        }

        private void Update()
        {
            if (_authority == null)
            {
                BindAuthority();
                return;
            }

            if (!_localConfigured)
            {
                ConfigureLocalVsRemote(_authority.IsLocalPlayer);
            }

            if (!_authority.IsLocalPlayer)
            {
                return;
            }

            if (!_authority.IsAlive)
            {
                VanguardAudioManager.Instance?.SetLowHealthHeartbeat(false);
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0.00001f) return;

            ReadInputs(
                out Vector2 moveInput,
                out Vector2 lookDelta,
                out bool sprintHeld,
                out bool crouchHeld,
                out bool crouchPressed,
                out bool jumpPressed,
                out bool fireHeld,
                out bool firePressed,
                out bool adsHeld,
                out bool reloadPressed,
                out float leanTarget);

            if (_isPausedOrMenu && !_useSyntheticInput)
            {
                moveInput = Vector2.zero;
                lookDelta = Vector2.zero;
                sprintHeld = false;
                fireHeld = false;
                firePressed = false;
                adsHeld = false;
                reloadPressed = false;
                jumpPressed = false;
                leanTarget = 0f;
            }

            TickLook(dt, lookDelta);
            TickMovement(dt, moveInput, sprintHeld, crouchHeld, crouchPressed, jumpPressed);
            TickCameraAndLean(dt, leanTarget, adsHeld);

            IsAds = adsHeld && !IsSprinting;
            weaponController?.TickWeapons(dt, fireHeld, firePressed, reloadPressed, IsAds, IsSprinting, _planarVelocity);
            weaponRig?.TickRig(
                dt,
                lookDelta,
                _planarVelocity,
                _cc.isGrounded,
                IsAds,
                IsSprinting,
                _isSliding,
                weaponController != null && weaponController.IsReloading,
                weaponController != null ? weaponController.ReloadNormalized : 0f);

            SubmitPoseToAuthority();
        }

        private void ReadInputs(
            out Vector2 move,
            out Vector2 look,
            out bool sprint,
            out bool crouch,
            out bool crouchDown,
            out bool jumpDown,
            out bool fireHeld,
            out bool fireDown,
            out bool ads,
            out bool reloadDown,
            out float lean)
        {
            if (_useSyntheticInput)
            {
                move = _syntheticMove;
                look = _syntheticLook;
                sprint = _syntheticSprint;
                crouch = _syntheticCrouch;
                crouchDown = false;
                jumpDown = false;
                fireHeld = _syntheticFire;
                fireDown = _syntheticFire;
                ads = _syntheticAds;
                reloadDown = false;
                lean = 0f;
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            float mx = 0f, my = 0f;
            if (kb != null)
            {
                if (kb.aKey.isPressed) mx -= 1f;
                if (kb.dKey.isPressed) mx += 1f;
                if (kb.wKey.isPressed) my += 1f;
                if (kb.sKey.isPressed) my -= 1f;

                sprint = kb.leftShiftKey.isPressed;
                crouch = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
                crouchDown = kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame;
                jumpDown = kb.spaceKey.wasPressedThisFrame;
                reloadDown = kb.rKey.wasPressedThisFrame;

                lean = 0f;
                if (kb.qKey.isPressed) lean -= 1f;
                if (kb.eKey.isPressed) lean += 1f;

                if (!_isPausedOrMenu)
                {
                    if (kb.digit1Key.wasPressedThisFrame) weaponController?.SwitchWeapon(WeaponId.VX47_AssaultRifle);
                    else if (kb.digit2Key.wasPressedThisFrame) weaponController?.SwitchWeapon(WeaponId.M890_TacticalShotgun);
                    else if (kb.digit3Key.wasPressedThisFrame) weaponController?.SwitchWeapon(WeaponId.M19_TacticalPistol);
                }
            }
            else
            {
                sprint = crouch = crouchDown = jumpDown = reloadDown = false;
                lean = 0f;
            }

            move = Vector2.ClampMagnitude(new Vector2(mx, my), 1f);

            if (mouse != null && !_isPausedOrMenu)
            {
                look = mouse.delta.ReadValue() * 0.085f;
                fireHeld = mouse.leftButton.isPressed;
                fireDown = mouse.leftButton.wasPressedThisFrame;
                ads = mouse.rightButton.isPressed;

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.1f && weaponController != null)
                {
                    int cur = (int)weaponController.CurrentWeapon;
                    int next = (cur + (scroll > 0 ? 1 : 2)) % 3;
                    weaponController.SwitchWeapon((WeaponId)next);
                }
            }
            else
            {
                look = Vector2.zero;
                fireHeld = fireDown = ads = false;
            }
        }

        private void TickLook(float dt, Vector2 lookDelta)
        {
            float sens = VanguardSettings.MouseSensitivity * (IsAds ? VanguardSettings.AdsSensitivityMultiplier : 1f);
            float ySign = VanguardSettings.InvertY ? 1f : -1f;

            _yaw += lookDelta.x * sens;
            _pitch = Mathf.Clamp(_pitch + lookDelta.y * sens * ySign, -84f, 84f);

            // Smooth recoil recovery
            if (_recoilPitchAccum > 0.01f)
            {
                float rec = Mathf.Min(_recoilPitchAccum, dt * 8.5f);
                _recoilPitchAccum -= rec;
                _pitch = Mathf.Clamp(_pitch + rec * 0.4f, -84f, 84f);
            }

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void TickMovement(float dt, Vector2 moveInput, bool sprintHeld, bool crouchHeld, bool crouchPressed, bool jumpPressed)
        {
            bool grounded = _cc.isGrounded;
            if (grounded && !_wasGrounded && _verticalVelocity < -5f)
            {
                VanguardAudioManager.Instance?.Play2D("land_heavy", AudioBus.Foley, 0.55f, 0.06f);
            }
            _wasGrounded = grounded;

            Vector3 wishDir = (transform.right * moveInput.x + transform.forward * moveInput.y);
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

            // Trigger tactical slide when pressing crouch while sprinting
            if (crouchPressed && grounded && IsSprinting && _stamina >= 18f && !_isSliding)
            {
                _isSliding = true;
                _slideTimer = slideDuration;
                _slideDir = wishDir.sqrMagnitude > 0.05f ? wishDir.normalized : transform.forward;
                _stamina = Mathf.Max(0f, _stamina - 18f);
                VanguardAudioManager.Instance?.Play2D("slide_tactical", AudioBus.Foley, 0.72f, 0.04f);
            }

            if (_isSliding)
            {
                _slideTimer -= dt;
                float t = Mathf.Clamp01(_slideTimer / slideDuration);
                float slideSpeed = Mathf.Lerp(crouchSpeed, slideInitialSpeed, Ease.OutCubic(t));
                _planarVelocity = _slideDir * slideSpeed;
                _isCrouching = true;
                if (_slideTimer <= 0f || !grounded)
                {
                    _isSliding = false;
                }
            }
            else
            {
                _isCrouching = crouchHeld;
                bool canSprint = sprintHeld && moveInput.y > 0.25f && !_isCrouching && !IsAds && _stamina > 4f;
                IsSprinting = canSprint && grounded;

                if (IsSprinting)
                    _stamina = Mathf.Max(0f, _stamina - 22f * dt);
                else
                    _stamina = Mathf.Min(100f, _stamina + 18f * dt);

                float targetSpeed = _isCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
                if (IsAds) targetSpeed *= 0.65f;

                Vector3 targetPlanar = wishDir * targetSpeed;
                float accel = grounded ? 16f : 4.5f;
                _planarVelocity = Vector3.Lerp(_planarVelocity, targetPlanar, Mathf.Clamp01(dt * accel));
            }

            if (grounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -2.2f;
                if (jumpPressed && _stamina >= 10f)
                {
                    _isSliding = false;
                    _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    _stamina = Mathf.Max(0f, _stamina - 10f);
                    VanguardAudioManager.Instance?.Play2D("jump_effort", AudioBus.Foley, 0.50f, 0.06f);
                }
            }
            else
            {
                _verticalVelocity += gravity * dt;
            }

            // Smooth CharacterController height for crouch/slide
            float targetHeight = IsCrouching ? crouchHeight : standingHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetHeight, dt * 11f);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);

            Vector3 motion = (_planarVelocity + Vector3.up * _verticalVelocity) * dt;
            _cc.Move(motion);

            // Surface-aware footsteps
            float speed = _planarVelocity.magnitude;
            if (grounded && !_isSliding && speed > 0.6f)
            {
                _stepAccum += speed * dt;
                float strideDist = IsSprinting ? 2.15f : (_isCrouching ? 1.45f : 1.78f);
                if (_stepAccum >= strideDist)
                {
                    _stepAccum = 0f;
                    PlayFootstepSound();
                }
            }
        }

        private void PlayFootstepSound()
        {
            string clipPrefix = "footstep_concrete";
            if (Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 1.0f))
            {
                var surf = VanguardSurfaceTag.ResolveFromCollider(hit.collider);
                if (surf == VanguardSurfaceTag.SurfaceKind.Metal)
                    clipPrefix = "footstep_metal";
            }
            float vol = _isCrouching ? 0.22f : (IsSprinting ? 0.58f : 0.40f);
            VanguardAudioManager.Instance?.PlayRandom2D(clipPrefix, 4, AudioBus.Foley, vol, 0.08f);
        }

        private void TickCameraAndLean(float dt, float leanTarget, bool adsHeld)
        {
            if (cameraPivot == null || fpCamera == null) return;

            // Check wall clearance before leaning
            float safeLean = leanTarget;
            if (Mathf.Abs(leanTarget) > 0.1f)
            {
                Vector3 origin = transform.position + Vector3.up * (IsCrouching ? crouchEyeY : standingEyeY);
                if (Physics.SphereCast(origin, 0.18f, transform.right * Mathf.Sign(leanTarget), out _, 0.42f))
                    safeLean = 0f;
            }
            _leanCurrent = Mathf.Lerp(_leanCurrent, safeLean, dt * 10f);

            float speedNorm = _cc.isGrounded ? Mathf.Clamp01(_planarVelocity.magnitude / sprintSpeed) : 0f;
            _headBobPhase += dt * Mathf.Lerp(2f, 11.2f, speedNorm);
            float bobY = Mathf.Sin(_headBobPhase * 2f) * 0.032f * speedNorm * (IsAds ? 0.25f : 1f);
            float bobX = Mathf.Cos(_headBobPhase) * 0.018f * speedNorm * (IsAds ? 0.25f : 1f);

            float targetEyeY = IsCrouching ? crouchEyeY : standingEyeY;
            Vector3 pivotLocal = cameraPivot.localPosition;
            pivotLocal.y = Mathf.Lerp(pivotLocal.y, targetEyeY + bobY, dt * 12f);
            pivotLocal.x = Mathf.Lerp(pivotLocal.x, _leanCurrent * 0.28f + bobX, dt * 12f);
            cameraPivot.localPosition = pivotLocal;

            _damageFlinchRoll = Mathf.Lerp(_damageFlinchRoll, 0f, dt * 10f);
            float slideRoll = _isSliding ? -5.5f : 0f;
            float targetRoll = (-_leanCurrent * 12.5f) + slideRoll + _damageFlinchRoll;
            _cameraRoll = Mathf.Lerp(_cameraRoll, targetRoll, dt * 12f);

            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, _cameraRoll);

            // Dynamic FOV (ADS zoom + Sprint widen)
            float baseFov = VanguardSettings.FieldOfView;
            float adsFov = weaponController != null && weaponController.CurrentWeapon == WeaponId.VX47_AssaultRifle
                ? baseFov - 20f
                : baseFov - 13f;
            float targetFov = adsHeld && !IsSprinting ? adsFov : (IsSprinting || _isSliding ? baseFov + 7f : baseFov);
            fpCamera.fieldOfView = Mathf.Lerp(fpCamera.fieldOfView, targetFov, dt * 10f);
        }

        private void SubmitPoseToAuthority()
        {
            PlayerStanceFlags flags = PlayerStanceFlags.None;
            if (_cc.isGrounded) flags |= PlayerStanceFlags.Grounded;
            if (IsSprinting) flags |= PlayerStanceFlags.Sprinting;
            if (_isCrouching) flags |= PlayerStanceFlags.Crouching;
            if (_isSliding) flags |= PlayerStanceFlags.Sliding;
            if (IsAds) flags |= PlayerStanceFlags.AimingDownSights;
            if (weaponController != null && weaponController.IsReloading) flags |= PlayerStanceFlags.Reloading;
            if (!_authority.IsAlive) flags |= PlayerStanceFlags.Dead;

            var pose = new PlayerPoseSnapshot
            {
                Position = transform.position,
                Yaw = _yaw,
                Pitch = _pitch,
                Velocity = _planarVelocity + Vector3.up * _verticalVelocity,
                Stance = flags,
                ActiveWeapon = weaponController != null ? weaponController.CurrentWeapon : WeaponId.VX47_AssaultRifle
            };

            _authority.SubmitLocalPose(in pose);
        }

        private void HandleRecoilCameraKick(float pitchKick, float yawKick)
        {
            _pitch = Mathf.Clamp(_pitch - pitchKick, -84f, 84f);
            _yaw += yawKick;
            _recoilPitchAccum = Mathf.Min(6f, _recoilPitchAccum + pitchKick);
        }

        private void HandleAuthoritativeTeleport(Vector3 pos, Quaternion rot)
        {
            bool wasEnabled = _cc.enabled;
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            _yaw = rot.eulerAngles.y;
            _pitch = 0f;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _isSliding = false;
            _stamina = 100f;
            _cc.enabled = wasEnabled;
        }

        private void HandleAliveStateChanged(bool alive)
        {
            operatorRig?.SetAliveState(alive);
            if (_authority != null && _authority.IsLocalPlayer)
            {
                if (weaponRig != null) weaponRig.gameObject.SetActive(alive);
                if (!alive)
                {
                    VanguardAudioManager.Instance?.Play2D("player_death", AudioBus.Foley, 0.85f, 0f);
                    VanguardAudioManager.Instance?.SetLowHealthHeartbeat(false);
                }
            }
        }

        private void HandleTeamAssigned(TeamId team)
        {
            operatorRig?.ApplyTeamVisuals(team);
        }

        private void HandleHealthArmorChanged(int health, int armor, Vector3 attackerOrigin)
        {
            if (_authority == null || !_authority.IsLocalPlayer) return;

            if (attackerOrigin != Vector3.zero)
            {
                _damageFlinchRoll = UnityEngine.Random.Range(-4.5f, 4.5f);
                VanguardAudioManager.Instance?.Play2D("player_hurt", AudioBus.Foley, 0.72f, 0.06f);
            }

            VanguardAudioManager.Instance?.SetLowHealthHeartbeat(health > 0 && health <= 30);
            OnLocalVitalsUpdated?.Invoke(health, armor, attackerOrigin);
        }

        private void HandleRemotePoseReceived(PlayerPoseSnapshot pose)
        {
            operatorRig?.ApplyPoseSnapshot(in pose);
        }
    }
}
