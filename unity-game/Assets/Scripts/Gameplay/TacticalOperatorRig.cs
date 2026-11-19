using System.Collections.Generic;
using UnityEngine;
using VanguardZero.Animation;
using VanguardZero.Core;
using VanguardZero.Networking;

namespace VanguardZero.Gameplay
{
    /// <summary>
    /// Third-person visual operator controller for remote players and tactical AI bots.
    /// Supports both imported Blender TacticalOperator.fbx Humanoid bones AND articulated
    /// tactical armor hierarchy, plus Animator / procedural gait, upper-body Aim IK,
    /// crouch compression, team visor emissive tint, and weapon mount switching.
    /// </summary>
    public class TacticalOperatorRig : MonoBehaviour
    {
        [Header("Bone / Hierarchy Transforms")]
        [SerializeField] private Transform hips;
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform leftLowerLeg;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField] private Transform rightLowerLeg;
        [SerializeField] private Transform rightHandWeaponMount;
        [SerializeField] private Transform headEyePoint;

        [Header("Team Identification Renderers")]
        [SerializeField] private List<Renderer> teamVisorRenderers = new List<Renderer>();
        [SerializeField] private List<Renderer> teamArmorRenderers = new List<Renderer>();
        [SerializeField] private List<GameObject> weaponModels3P = new List<GameObject>();

        private Animator _animator;
        private MotionBricksAnimatorDriver _motionDriver;
        private MotionBricksFootIK _footIk;
        private Vector3 _targetPos;
        private float _targetYaw;
        private float _targetPitch;
        private Vector3 _currentVelocity;
        private PlayerStanceFlags _stance = PlayerStanceFlags.Grounded;
        private WeaponId _activeWeapon = WeaponId.VX47_AssaultRifle;
        private TeamId _team = TeamId.Alpha;
        private bool _isAlive = true;
        private bool _isHiddenForLocalFirstPerson;

        private float _gaitCycle;
        private float _stepAudioTimer;
        private float _crouchBlend;
        private float _baseHipsY = 0.96f;
        private bool _poseInitialized;

        public Transform MuzzleTransform { get; private set; }
        public Transform EyeTransform => headEyePoint != null ? headEyePoint : (head != null ? head : transform);

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _motionDriver = GetComponentInChildren<MotionBricksAnimatorDriver>();
            _footIk = GetComponentInChildren<MotionBricksFootIK>();
            if (_footIk != null) _footIk.OnFootstep += HandleNeuralFootstep;
            AutoBindHierarchy();
            if (hips != null) _baseHipsY = hips.localPosition.y > 0.1f ? hips.localPosition.y : 0.96f;
        }

        private void OnDestroy()
        {
            if (_footIk != null) _footIk.OnFootstep -= HandleNeuralFootstep;
        }

        private void HandleNeuralFootstep(MotionBricksFootstepPayload ev)
        {
            if (_isHiddenForLocalFirstPerson) return;
            string prefix = (ev.SurfaceTag != null && ev.SurfaceTag.IndexOf("metal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ? "footstep_metal"
                : "footstep_concrete";
            float vol = ev.IsCrouching ? 0.22f : (ev.IsSprinting ? 0.48f : 0.34f);
            VanguardAudioManager.Instance?.PlayRandom3D(prefix, 4, ev.WorldPosition, AudioBus.Foley, vol, 0.08f, 24f);
        }

        public void AutoBindHierarchy()
        {
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                string n = t.name;
                if (hips == null && (n == "Hips" || n == "Pelvis")) hips = t;
                else if (spine == null && n == "Spine") spine = t;
                else if (chest == null && (n == "Chest" || n == "UpperChest")) chest = t;
                else if (head == null && n == "Head") head = t;
                else if (leftUpperArm == null && (n == "LeftUpperArm" || n == "UpperArm_L")) leftUpperArm = t;
                else if (rightUpperArm == null && (n == "RightUpperArm" || n == "UpperArm_R")) rightUpperArm = t;
                else if (leftUpperLeg == null && (n == "LeftUpperLeg" || n == "UpperLeg_L")) leftUpperLeg = t;
                else if (leftLowerLeg == null && (n == "LeftLowerLeg" || n == "LowerLeg_L")) leftLowerLeg = t;
                else if (rightUpperLeg == null && (n == "RightUpperLeg" || n == "UpperLeg_R")) rightUpperLeg = t;
                else if (rightLowerLeg == null && (n == "RightLowerLeg" || n == "LowerLeg_R")) rightLowerLeg = t;
                else if (rightHandWeaponMount == null && n.Contains("RightHandWeaponMount")) rightHandWeaponMount = t;
                else if (headEyePoint == null && n.Contains("HeadEyePoint")) headEyePoint = t;
                else if (MuzzleTransform == null && n.Contains("MuzzlePoint")) MuzzleTransform = t;
            }

            if (teamVisorRenderers.Count == 0 || teamArmorRenderers.Count == 0)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("Visor") || r.name.Contains("Badge"))
                        teamVisorRenderers.Add(r);
                    else if (r.name.Contains("Carrier") || r.name.Contains("Chest") || r.name.Contains("Torso"))
                        teamArmorRenderers.Add(r);
                }
            }
        }

        public void SetHiddenForLocalFirstPerson(bool hide)
        {
            _isHiddenForLocalFirstPerson = hide;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = hide
                    ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                    : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        public void ApplyTeamVisuals(TeamId team)
        {
            _team = team;
            Color visorColor = VanguardPalette.GetTeamColor((int)team) * 2.2f;
            Color armorColor = VanguardPalette.GetTeamArmorTint((int)team);

            var propBlock = new MaterialPropertyBlock();
            foreach (var r in teamVisorRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(propBlock);
                propBlock.SetColor("_BaseColor", visorColor);
                propBlock.SetColor("_EmissionColor", visorColor);
                r.SetPropertyBlock(propBlock);
            }

            var armorBlock = new MaterialPropertyBlock();
            foreach (var r in teamArmorRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(armorBlock);
                armorBlock.SetColor("_BaseColor", armorColor);
                r.SetPropertyBlock(armorBlock);
            }
        }

        public void SetAliveState(bool alive)
        {
            _isAlive = alive;
            if (_motionDriver != null) return;
            if (!alive)
            {
                // Tactical collapse pose
                if (hips != null)
                {
                    hips.localPosition = new Vector3(0f, 0.24f, 0f);
                    hips.localRotation = Quaternion.Euler(82f, 12f, 18f);
                }
            }
            else
            {
                if (hips != null)
                {
                    hips.localPosition = new Vector3(0f, _baseHipsY, 0f);
                    hips.localRotation = Quaternion.identity;
                }
            }
        }

        public void ApplyPoseSnapshot(in PlayerPoseSnapshot pose, bool snapImmediate = false)
        {
            _targetPos = pose.Position;
            _targetYaw = pose.Yaw;
            _targetPitch = pose.Pitch;
            _currentVelocity = pose.Velocity;
            _stance = pose.Stance;

            if (_activeWeapon != pose.ActiveWeapon)
            {
                _activeWeapon = pose.ActiveWeapon;
                UpdateEquippedWeapon3P(_activeWeapon);
            }

            if (snapImmediate || !_poseInitialized)
            {
                _poseInitialized = true;
                transform.position = _targetPos;
                transform.rotation = Quaternion.Euler(0f, _targetYaw, 0f);
            }
        }

        private void UpdateEquippedWeapon3P(WeaponId weapon)
        {
            for (int i = 0; i < weaponModels3P.Count; i++)
            {
                if (weaponModels3P[i] != null)
                    weaponModels3P[i].SetActive(i == (int)weapon);
            }
        }

        private void LateUpdate()
        {
            if (!_isAlive) return;

            float dt = Time.deltaTime;
            float speed = new Vector3(_currentVelocity.x, 0f, _currentVelocity.z).magnitude;
            float speedNorm = Mathf.Clamp01(speed / 6.5f);
            bool crouching = (_stance & PlayerStanceFlags.Crouching) != 0 || (_stance & PlayerStanceFlags.Sliding) != 0;

            _crouchBlend = Mathf.MoveTowards(_crouchBlend, crouching ? 1f : 0f, dt * 7f);

            // Feed NVIDIA MotionBricks AnimatorDriver if present (drives 2D BlendTrees + Upper-Body Aim IK + Neural Foot IK!)
            if (_motionDriver != null)
            {
                var drive = new MotionBrickDriveState
                {
                    WorldVelocity = _currentVelocity,
                    AimPitchDegrees = _targetPitch,
                    CharacterYawDegrees = _targetYaw,
                    IsGrounded = (_stance & PlayerStanceFlags.Grounded) != 0,
                    IsCrouching = crouching,
                    IsSprinting = (_stance & PlayerStanceFlags.Sprinting) != 0,
                    IsSliding = (_stance & PlayerStanceFlags.Sliding) != 0,
                    IsAimingDownSights = (_stance & PlayerStanceFlags.AimingDownSights) != 0,
                    IsReloading = (_stance & PlayerStanceFlags.Reloading) != 0,
                    IsDead = !_isAlive,
                    WeaponStyle = (WeaponHoldStyle)(byte)_activeWeapon
                };
                _motionDriver.SetDriveState(in drive);
                return;
            }

            // Procedural fallback when MotionBricksAnimatorDriver is not attached
            float stepFreq = Mathf.Lerp(1.8f, 3.3f, speedNorm);
            if (speedNorm > 0.04f)
            {
                _gaitCycle += dt * stepFreq * Mathf.PI * 2f;
                _stepAudioTimer += dt * stepFreq;
                if (_stepAudioTimer >= 0.5f && (_stance & PlayerStanceFlags.Grounded) != 0)
                {
                    _stepAudioTimer = 0f;
                    VanguardAudioManager.Instance?.PlayRandom3D("footstep_concrete", 4, transform.position, AudioBus.Foley, 0.35f, 0.07f, 24f);
                }
            }
            else
            {
                _gaitCycle = Mathf.MoveTowards(_gaitCycle, 0f, dt * 4f);
            }

            float breathe = Mathf.Sin(Time.time * 1.9f) * 0.012f;
            float bob = Mathf.Sin(_gaitCycle * 2f) * 0.038f * speedNorm;

            if (hips != null)
            {
                float targetY = Mathf.Lerp(_baseHipsY, _baseHipsY - 0.36f, _crouchBlend) + bob + breathe;
                hips.localPosition = new Vector3(0f, targetY, 0f);
                float roll = Mathf.Sin(_gaitCycle) * 2.4f * speedNorm;
                float leanForward = Mathf.Lerp(4f, 14f, speedNorm) + (_crouchBlend * 12f);
                hips.localRotation = Quaternion.Euler(leanForward, 0f, roll);
            }

            // Upper-body aim pitch distribution across Spine & Chest
            float clampedPitch = Mathf.Clamp(_targetPitch, -55f, 55f);
            if (spine != null)
                spine.localRotation = Quaternion.Euler(clampedPitch * 0.45f, 0f, 0f);
            if (chest != null)
                chest.localRotation = Quaternion.Euler(clampedPitch * 0.55f, 0f, 0f);
            if (head != null)
                head.localRotation = Quaternion.Euler(clampedPitch * 0.25f, 0f, 0f);

            // Procedural leg stride when no Animator overrides legs
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                float legSwing = Mathf.Sin(_gaitCycle) * 34f * speedNorm;
                float kneeCrouch = _crouchBlend * 42f;

                if (leftUpperLeg != null)
                    leftUpperLeg.localRotation = Quaternion.Euler(legSwing - kneeCrouch * 0.6f, 0f, 0f);
                if (leftLowerLeg != null)
                    leftLowerLeg.localRotation = Quaternion.Euler(Mathf.Max(0f, -legSwing * 0.7f) + kneeCrouch, 0f, 0f);

                if (rightUpperLeg != null)
                    rightUpperLeg.localRotation = Quaternion.Euler(-legSwing - kneeCrouch * 0.6f, 0f, 0f);
                if (rightLowerLeg != null)
                    rightLowerLeg.localRotation = Quaternion.Euler(Mathf.Max(0f, legSwing * 0.7f) + kneeCrouch, 0f, 0f);
            }
        }
    }
}
