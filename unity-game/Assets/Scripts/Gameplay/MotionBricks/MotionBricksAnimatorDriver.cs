using UnityEngine;

namespace VanguardZero.Animation
{
    /// <summary>
    /// Drives the MotionBricks-generated Unity AnimatorController (`VanguardOperator.controller`)
    /// with MotionBricks-inspired inertial velocity smoothing, 2D directional strafe blend-tree
    /// parameterization, stride-speed playback scaling, turn-in-place detection, and tactical
    /// action triggers (Slide, JumpVault, HitReact, Death).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksAnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private TacticalUpperBodyAimIK upperBodyAimIK;
        [SerializeField] private MotionBricksFootIK footIK;

        [Header("MotionBricks Velocity & Blend Tuning")]
        [Tooltip("Reference max sprint speed (m/s) that maps to MoveZ = 1.0 in the 2D BlendTree.")]
        [SerializeField] private float referenceSprintSpeed = 4.8f;

        [Tooltip("Reference tactical walk speed (m/s) that maps to MoveZ = 0.5.")]
        [SerializeField] private float referenceWalkSpeed = 2.0f;

        [Tooltip("Reference lateral strafe speed (m/s) that maps to |MoveX| = 0.6.")]
        [SerializeField] private float referenceStrafeSpeed = 2.1f;

        [Tooltip("Reference crouch move speed (m/s) that maps to |Move| = 0.5 in crouch blend tree.")]
        [SerializeField] private float referenceCrouchSpeed = 1.5f;

        [Tooltip("Inertial smoothing time (seconds) for directional velocity changes.")]
        [SerializeField] private float velocitySmoothTime = 0.10f;

        [Tooltip("Minimum yaw delta (degrees) while stationary to trigger a 90-deg turn-in-place brick.")]
        [SerializeField] private float turnInPlaceThresholdDeg = 55f;

        [SerializeField] private float turnInPlaceCooldown = 0.45f;

        // Cached Animator parameter hashes
        private static readonly int HashMoveX = Animator.StringToHash("MoveX");
        private static readonly int HashMoveZ = Animator.StringToHash("MoveZ");
        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashLocomotionRate = Animator.StringToHash("LocomotionSpeedMult");
        private static readonly int HashIsCrouching = Animator.StringToHash("IsCrouching");
        private static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int HashIsSliding = Animator.StringToHash("IsSliding");
        private static readonly int HashIsDead = Animator.StringToHash("IsDead");
        private static readonly int HashJumpVault = Animator.StringToHash("JumpVault");
        private static readonly int HashTurnLeft = Animator.StringToHash("TurnLeft90");
        private static readonly int HashTurnRight = Animator.StringToHash("TurnRight90");
        private static readonly int HashHitReact = Animator.StringToHash("HitReact");

        private MotionBrickDriveState _currentState;
        private Vector2 _smoothedMove;
        private Vector2 _moveVelRef;
        private float _lastStationaryYaw;
        private float _turnCooldownTimer;
        private bool _wasDead;
        private bool _initializedYaw;

        public MotionBrickDriveState CurrentState => _currentState;

        private void Awake()
        {
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>();
            if (upperBodyAimIK == null)
                upperBodyAimIK = GetComponentInChildren<TacticalUpperBodyAimIK>();
            if (footIK == null)
                footIK = GetComponentInChildren<MotionBricksFootIK>();
        }

        /// <summary>
        /// Feed latest locomotion/combat state from PlayerController, NetworkPlayerBridge, or AI Bot.
        /// </summary>
        public void SetDriveState(in MotionBrickDriveState state)
        {
            _currentState = state;
            if (!_initializedYaw)
            {
                _lastStationaryYaw = state.CharacterYawDegrees;
                _initializedYaw = true;
            }
        }

        /// <summary>
        /// Convenience overload for direct scalar wiring without constructing a struct.
        /// </summary>
        public void SetLocomotionState(
            Vector3 worldVelocity,
            float aimPitchDeg,
            bool isGrounded,
            bool isCrouching,
            bool isSprinting,
            bool isSliding,
            bool isADS,
            bool isReloading,
            bool isDead,
            int weaponSlot)
        {
            var style = weaponSlot == 1
                ? WeaponHoldStyle.Shotgun
                : (weaponSlot == 2 ? WeaponHoldStyle.Pistol : WeaponHoldStyle.AssaultRifle);

            SetDriveState(new MotionBrickDriveState
            {
                WorldVelocity = worldVelocity,
                AimPitchDegrees = aimPitchDeg,
                CharacterYawDegrees = transform.eulerAngles.y,
                IsGrounded = isGrounded,
                IsCrouching = isCrouching,
                IsSprinting = isSprinting,
                IsSliding = isSliding,
                IsAimingDownSights = isADS,
                IsReloading = isReloading,
                IsDead = isDead,
                WeaponStyle = style,
            });
        }

        public void TriggerJumpVault()
        {
            if (targetAnimator != null && !_currentState.IsDead)
                targetAnimator.SetTrigger(HashJumpVault);
        }

        public void TriggerHitReact(Vector3 localImpactDirection)
        {
            if (targetAnimator != null && !_currentState.IsDead)
                targetAnimator.SetTrigger(HashHitReact);
            if (upperBodyAimIK != null)
                upperBodyAimIK.AddHitFlinchImpulse(localImpactDirection);
        }

        public void TriggerWeaponRecoil(float pitchKickDeg = 3.2f, float backKickMeters = 0.035f)
        {
            if (upperBodyAimIK != null)
                upperBodyAimIK.AddRecoilImpulse(pitchKickDeg, backKickMeters);
        }

        private void Update()
        {
            if (targetAnimator == null)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            // Convert world planar velocity into character local space (+Z forward, +X right)
            Vector3 planarWorld = new Vector3(_currentState.WorldVelocity.x, 0f, _currentState.WorldVelocity.z);
            Vector3 localVel = transform.InverseTransformDirection(planarWorld);
            float planarSpeed = planarWorld.magnitude;

            // Map physical speed (m/s) into MotionBricks BlendTree normalized coordinates
            Vector2 targetBlend;
            float speedRateMult = 1f;

            if (_currentState.IsCrouching)
            {
                float denom = Mathf.Max(referenceCrouchSpeed, 0.25f);
                targetBlend.x = Mathf.Clamp((localVel.x / denom) * 0.5f, -0.55f, 0.55f);
                targetBlend.y = Mathf.Clamp((localVel.z / denom) * 0.5f, -0.55f, 0.55f);
                if (planarSpeed > 0.15f)
                    speedRateMult = Mathf.Clamp(planarSpeed / denom, 0.7f, 1.35f);
            }
            else
            {
                float xNorm = (localVel.x / Mathf.Max(referenceStrafeSpeed, 0.25f)) * 0.6f;
                float zNorm;
                if (localVel.z >= 0f)
                {
                    if (_currentState.IsSprinting || localVel.z > referenceWalkSpeed * 1.15f)
                    {
                        float t = Mathf.InverseLerp(referenceWalkSpeed, referenceSprintSpeed, localVel.z);
                        zNorm = Mathf.Lerp(0.5f, 1.0f, t);
                    }
                    else
                    {
                        zNorm = (localVel.z / Mathf.Max(referenceWalkSpeed, 0.25f)) * 0.5f;
                    }
                }
                else
                {
                    zNorm = (localVel.z / Mathf.Max(referenceWalkSpeed * 0.85f, 0.25f)) * 0.5f;
                }

                targetBlend = new Vector2(Mathf.Clamp(xNorm, -0.7f, 0.7f), Mathf.Clamp(zNorm, -0.6f, 1.05f));

                // Match playback rate to actual velocity so feet never skate on the ground
                if (planarSpeed > 0.2f)
                {
                    float refSpeed = _currentState.IsSprinting ? referenceSprintSpeed : referenceWalkSpeed;
                    speedRateMult = Mathf.Clamp(planarSpeed / Mathf.Max(refSpeed, 0.5f), 0.75f, 1.35f);
                    if (!_currentState.IsSprinting && planarSpeed > referenceWalkSpeed * 1.1f)
                        speedRateMult = Mathf.Clamp(planarSpeed / 3.2f, 0.85f, 1.25f);
                }
            }

            _smoothedMove = Vector2.SmoothDamp(_smoothedMove, targetBlend, ref _moveVelRef, velocitySmoothTime, Mathf.Infinity, dt);

            targetAnimator.SetFloat(HashMoveX, _smoothedMove.x);
            targetAnimator.SetFloat(HashMoveZ, _smoothedMove.y);
            targetAnimator.SetFloat(HashSpeed, planarSpeed);
            targetAnimator.SetFloat(HashLocomotionRate, speedRateMult);
            targetAnimator.SetBool(HashIsCrouching, _currentState.IsCrouching);
            targetAnimator.SetBool(HashIsGrounded, _currentState.IsGrounded);
            targetAnimator.SetBool(HashIsSliding, _currentState.IsSliding);
            targetAnimator.SetBool(HashIsDead, _currentState.IsDead);

            // Reset cleanly on respawn transition (Dead -> Alive)
            if (_wasDead && !_currentState.IsDead)
            {
                targetAnimator.Rebind();
                targetAnimator.Update(0f);
                _smoothedMove = Vector2.zero;
                _moveVelRef = Vector2.zero;
            }
            _wasDead = _currentState.IsDead;

            // Turn-in-place brick triggers when stationary and rotating beyond threshold
            if (_turnCooldownTimer > 0f)
                _turnCooldownTimer -= dt;

            if (!_currentState.IsDead && _currentState.IsGrounded && planarSpeed < 0.25f)
            {
                float yawDelta = Mathf.DeltaAngle(_lastStationaryYaw, _currentState.CharacterYawDegrees);
                if (Mathf.Abs(yawDelta) >= turnInPlaceThresholdDeg && _turnCooldownTimer <= 0f)
                {
                    targetAnimator.SetTrigger(yawDelta < 0f ? HashTurnLeft : HashTurnRight);
                    _lastStationaryYaw = _currentState.CharacterYawDegrees;
                    _turnCooldownTimer = turnInPlaceCooldown;
                }
            }
            else
            {
                _lastStationaryYaw = _currentState.CharacterYawDegrees;
            }

            // Forward state to Upper-Body Aim-IK and Foot-IK companions
            if (upperBodyAimIK != null)
                upperBodyAimIK.SetCombatState(_currentState);
            if (footIK != null)
                footIK.SetLocomotionContext(_currentState.IsGrounded, _currentState.IsCrouching, _currentState.IsSprinting, _currentState.IsDead);
        }
    }
}
