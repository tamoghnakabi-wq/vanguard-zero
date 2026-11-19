using UnityEngine;

namespace VanguardZero.Animation
{
    /// <summary>
    /// LateUpdate Upper-Body Tactical Aim-IK & Procedural Combat Layer for VANGUARD ZERO.
    /// Runs after the MotionBricks Animator so lower-body neural locomotion stays untouched while:
    /// - Distributing aim pitch cleanly across Spine / Chest / UpperChest / Neck / Head
    /// - Locking the LeftHand onto the active weapon's foregrip via analytic Two-Bone IK
    /// - Applying spring-damped weapon recoil impulse & directional ballistic hit-flinch additive
    /// - Smoothly blending Sprint-to-Fire, ADS tight shoulder tuck, and Reload upper-body motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TacticalUpperBodyAimIK : MonoBehaviour
    {
        [Header("Humanoid Bone References (Auto-Resolved if Left Null)")]
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform upperChest;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftLowerArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform rightLowerArm;
        [SerializeField] private Transform rightHand;

        [Header("Weapon Sockets")]
        [Tooltip("Optional LeftHand foregrip target transform on the currently equipped 3P weapon.")]
        [SerializeField] private Transform leftHandForegripTarget;
        [SerializeField] private Transform rightHandWeaponMount;

        [Header("Aim Pitch Distribution (Weights Sum ~1.0 Across Torso -> Head)")]
        [SerializeField] private float spinePitchWeight = 0.16f;
        [SerializeField] private float chestPitchWeight = 0.24f;
        [SerializeField] private float upperChestPitchWeight = 0.30f;
        [SerializeField] private float neckPitchWeight = 0.15f;
        [SerializeField] private float headPitchWeight = 0.15f;
        [SerializeField] private float maxAimPitchDeg = 72f;
        [SerializeField] private float aimSmoothSpeed = 16f;

        [Header("Two-Bone Left-Hand Foregrip IK")]
        [Range(0f, 1f)]
        [SerializeField] private float leftHandIKWeight = 0.92f;
        [SerializeField] private Vector3 leftElbowHintLocalOffset = new Vector3(-0.35f, -0.25f, -0.20f);

        [Header("Recoil & Hit-Flinch Spring")]
        [SerializeField] private float recoilSpringStiffness = 140f;
        [SerializeField] private float recoilSpringDamping = 18f;
        [SerializeField] private float flinchSpringStiffness = 110f;
        [SerializeField] private float flinchSpringDamping = 15f;

        private MotionBrickDriveState _state;
        private float _smoothedPitch;
        private float _adsWeight;
        private float _sprintLowerWeight;
        private float _reloadPhase;
        private float _recoilPitch;
        private float _recoilPitchVel;
        private Vector3 _flinchEuler;
        private Vector3 _flinchEulerVel;
        private bool _bonesResolved;

        private void Awake()
        {
            ResolveBones();
        }

        public void ResolveBones()
        {
            if (_bonesResolved && spine != null && rightHand != null)
                return;

            var animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                spine = spine != null ? spine : animator.GetBoneTransform(HumanBodyBones.Spine);
                chest = chest != null ? chest : animator.GetBoneTransform(HumanBodyBones.Chest);
                upperChest = upperChest != null ? upperChest : animator.GetBoneTransform(HumanBodyBones.UpperChest);
                neck = neck != null ? neck : animator.GetBoneTransform(HumanBodyBones.Neck);
                head = head != null ? head : animator.GetBoneTransform(HumanBodyBones.Head);
                leftUpperArm = leftUpperArm != null ? leftUpperArm : animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftLowerArm = leftLowerArm != null ? leftLowerArm : animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                leftHand = leftHand != null ? leftHand : animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightUpperArm = rightUpperArm != null ? rightUpperArm : animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLowerArm = rightLowerArm != null ? rightLowerArm : animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                rightHand = rightHand != null ? rightHand : animator.GetBoneTransform(HumanBodyBones.RightHand);
            }

            // Fallback by exact TacticalOperator.fbx contract names (works for Generic rigs too)
            spine = spine != null ? spine : FindDeepChild(transform, "Spine");
            chest = chest != null ? chest : FindDeepChild(transform, "Chest");
            upperChest = upperChest != null ? upperChest : FindDeepChild(transform, "UpperChest");
            neck = neck != null ? neck : FindDeepChild(transform, "Neck");
            head = head != null ? head : FindDeepChild(transform, "Head");
            leftUpperArm = leftUpperArm != null ? leftUpperArm : FindDeepChild(transform, "LeftUpperArm");
            leftLowerArm = leftLowerArm != null ? leftLowerArm : FindDeepChild(transform, "LeftLowerArm");
            leftHand = leftHand != null ? leftHand : FindDeepChild(transform, "LeftHand");
            rightUpperArm = rightUpperArm != null ? rightUpperArm : FindDeepChild(transform, "RightUpperArm");
            rightLowerArm = rightLowerArm != null ? rightLowerArm : FindDeepChild(transform, "RightLowerArm");
            rightHand = rightHand != null ? rightHand : FindDeepChild(transform, "RightHand");
            rightHandWeaponMount = rightHandWeaponMount != null ? rightHandWeaponMount : FindDeepChild(transform, "RightHandWeaponMount");

            _bonesResolved = spine != null || upperChest != null;
        }

        public void SetCombatState(in MotionBrickDriveState state)
        {
            _state = state;
        }

        public void SetLeftHandForegripTarget(Transform gripTarget)
        {
            leftHandForegripTarget = gripTarget;
        }

        public void AddRecoilImpulse(float pitchKickDeg, float backKickMeters)
        {
            _recoilPitchVel -= Mathf.Abs(pitchKickDeg) * 16f + Mathf.Abs(backKickMeters) * 80f;
            _recoilPitch = Mathf.Clamp(_recoilPitch - pitchKickDeg * 0.4f, -22f, 8f);
        }

        public void AddHitFlinchImpulse(Vector3 localImpactDirection)
        {
            Vector3 dir = localImpactDirection.sqrMagnitude > 0.001f ? localImpactDirection.normalized : Vector3.back;
            // Knock chest away from bullet direction with visceral twist
            _flinchEulerVel += new Vector3(dir.z * 210f, -dir.x * 160f, -dir.x * 130f);
        }

        private void LateUpdate()
        {
            if (_state.IsDead)
                return;

            if (!_bonesResolved)
                ResolveBones();

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            // Smooth combat pose weights
            float targetPitch = Mathf.Clamp(_state.AimPitchDegrees, -maxAimPitchDeg, maxAimPitchDeg);
            _smoothedPitch = Mathf.Lerp(_smoothedPitch, targetPitch, 1f - Mathf.Exp(-aimSmoothSpeed * dt));
            _adsWeight = Mathf.MoveTowards(_adsWeight, _state.IsAimingDownSights ? 1f : 0f, dt * 8f);
            _sprintLowerWeight = Mathf.MoveTowards(_sprintLowerWeight, (_state.IsSprinting && !_state.IsAimingDownSights) ? 1f : 0f, dt * 7f);

            if (_state.IsReloading)
                _reloadPhase = Mathf.Repeat(_reloadPhase + dt * 1.4f, 1f);
            else
                _reloadPhase = Mathf.MoveTowards(_reloadPhase, 0f, dt * 6f);

            // Step critically-damped recoil & hit-flinch springs
            StepSpring(ref _recoilPitch, ref _recoilPitchVel, recoilSpringStiffness, recoilSpringDamping, dt);
            StepSpring3(ref _flinchEuler, ref _flinchEulerVel, flinchSpringStiffness, flinchSpringDamping, dt);

            // 1. Distribute Aim Pitch + Recoil + Hit-Flinch around character's local right axis
            float totalPitch = _smoothedPitch + _recoilPitch + _flinchEuler.x;
            float totalYaw = _flinchEuler.y + (_adsWeight * 6f); // subtle tactical bladed stance during ADS
            float totalRoll = _flinchEuler.z;

            Vector3 charRight = transform.right;
            Vector3 charUp = transform.up;
            Vector3 charFwd = transform.forward;

            ApplyWorldDelta(spine, charRight, totalPitch * spinePitchWeight, charUp, totalYaw * 0.25f, charFwd, totalRoll * 0.2f);
            ApplyWorldDelta(chest, charRight, totalPitch * chestPitchWeight, charUp, totalYaw * 0.35f, charFwd, totalRoll * 0.4f);
            ApplyWorldDelta(upperChest, charRight, totalPitch * upperChestPitchWeight, charUp, totalYaw * 0.40f, charFwd, totalRoll * 0.4f);
            ApplyWorldDelta(neck, charRight, (_smoothedPitch + _flinchEuler.x * 0.4f) * neckPitchWeight, charUp, -totalYaw * 0.5f, charFwd, 0f);
            ApplyWorldDelta(head, charRight, (_smoothedPitch + _flinchEuler.x * 0.3f) * headPitchWeight, charUp, -totalYaw * 0.5f, charFwd, 0f);

            // 2. Subtle weapon-style & sprint-to-fire arm posture refinement
            if (rightUpperArm != null)
            {
                float styleLift = _state.WeaponStyle == WeaponHoldStyle.Pistol ? -8f : (_state.WeaponStyle == WeaponHoldStyle.Shotgun ? 4f : 0f);
                float adsTighten = _adsWeight * -6f;
                float sprintDrop = _sprintLowerWeight * 16f;
                float reloadDip = _state.IsReloading ? Mathf.Sin(_reloadPhase * Mathf.PI) * 18f : 0f;
                rightUpperArm.rotation = Quaternion.AngleAxis(styleLift + adsTighten + sprintDrop + reloadDip, charRight) * rightUpperArm.rotation;
            }

            // 3. Analytic Two-Bone Left-Hand Foregrip IK (keeps support hand locked onto rifle/shotgun foregrip)
            float effectiveIK = _state.IsReloading ? 0f : leftHandIKWeight * (1f - _sprintLowerWeight * 0.55f);
            if (effectiveIK > 0.01f && leftHandForegripTarget != null && leftUpperArm != null && leftLowerArm != null && leftHand != null)
            {
                Vector3 hintWorld = transform.TransformPoint(leftElbowHintLocalOffset) + (leftUpperArm.position - transform.position);
                SolveTwoBoneIK(leftUpperArm, leftLowerArm, leftHand, leftHandForegripTarget.position, hintWorld, effectiveIK);
                leftHand.rotation = Quaternion.Slerp(leftHand.rotation, leftHandForegripTarget.rotation, effectiveIK);
            }
        }

        private static void ApplyWorldDelta(Transform bone, Vector3 rAxis, float pitchDeg, Vector3 uAxis, float yawDeg, Vector3 fAxis, float rollDeg)
        {
            if (bone == null)
                return;
            Quaternion q = Quaternion.AngleAxis(yawDeg, uAxis)
                         * Quaternion.AngleAxis(pitchDeg, rAxis)
                         * Quaternion.AngleAxis(rollDeg, fAxis);
            bone.rotation = q * bone.rotation;
        }

        private static void StepSpring(ref float value, ref float vel, float stiffness, float damping, float dt)
        {
            float acc = -stiffness * value - damping * vel;
            vel += acc * dt;
            value += vel * dt;
            if (Mathf.Abs(value) < 0.001f && Mathf.Abs(vel) < 0.001f)
            {
                value = 0f;
                vel = 0f;
            }
        }

        private static void StepSpring3(ref Vector3 value, ref Vector3 vel, float stiffness, float damping, float dt)
        {
            Vector3 acc = -stiffness * value - damping * vel;
            vel += acc * dt;
            value += vel * dt;
            if (value.sqrMagnitude < 1e-6f && vel.sqrMagnitude < 1e-6f)
            {
                value = Vector3.zero;
                vel = Vector3.zero;
            }
        }

        /// <summary>
        /// Law-of-cosines analytic Two-Bone IK (ported from MotionBricks `animation/ik.py` design).
        /// </summary>
        public static void SolveTwoBoneIK(Transform root, Transform mid, Transform tip, Vector3 targetPos, Vector3 hintPos, float weight)
        {
            Vector3 a = root.position;
            Vector3 b = mid.position;
            Vector3 c = tip.position;
            Vector3 t = Vector3.Lerp(c, targetPos, Mathf.Clamp01(weight));

            float abLen = Vector3.Distance(a, b);
            float bcLen = Vector3.Distance(b, c);
            if (abLen < 1e-4f || bcLen < 1e-4f)
                return;

            Vector3 at = t - a;
            float maxReach = (abLen + bcLen) * 0.995f;
            float minReach = Mathf.Abs(abLen - bcLen) + 0.01f;
            float atLen = Mathf.Clamp(at.magnitude, minReach, maxReach);
            Vector3 atDir = at.sqrMagnitude > 1e-6f ? at.normalized : (c - a).normalized;

            Vector3 ac = c - a;
            Vector3 acDir = ac.sqrMagnitude > 1e-6f ? ac.normalized : atDir;

            // Compute desired interior angles using law of cosines
            float cosRootCurrent = Mathf.Clamp((abLen * abLen + ac.sqrMagnitude - bcLen * bcLen) / (2f * abLen * Mathf.Max(ac.magnitude, 1e-4f)), -1f, 1f);
            float cosRootTarget = Mathf.Clamp((abLen * abLen + atLen * atLen - bcLen * bcLen) / (2f * abLen * atLen), -1f, 1f);
            float cosMidCurrent = Mathf.Clamp((abLen * abLen + bcLen * bcLen - ac.sqrMagnitude) / (2f * abLen * bcLen), -1f, 1f);
            float cosMidTarget = Mathf.Clamp((abLen * abLen + bcLen * bcLen - atLen * atLen) / (2f * abLen * bcLen), -1f, 1f);

            // Plane normal defined by current bend or pole hint
            Vector3 bendPlane = Vector3.Cross(b - a, c - b);
            if (bendPlane.sqrMagnitude < 1e-6f)
                bendPlane = Vector3.Cross(hintPos - a, atDir);
            if (bendPlane.sqrMagnitude < 1e-6f)
                return;
            bendPlane.Normalize();

            // 1. Adjust elbow bend angle
            float midDeltaRad = Mathf.Acos(cosMidTarget) - Mathf.Acos(cosMidCurrent);
            mid.rotation = Quaternion.AngleAxis(-midDeltaRad * Mathf.Rad2Deg, bendPlane) * mid.rotation;

            // 2. Swing root bone so tip meets target
            Vector3 newAcDir = (tip.position - a).normalized;
            Quaternion swing = Quaternion.FromToRotation(newAcDir, atDir);
            root.rotation = swing * root.rotation;

            // 3. Twist around root->target axis toward pole hint for natural elbow/knee alignment
            Vector3 curMidProj = Vector3.ProjectOnPlane(mid.position - a, atDir);
            Vector3 hintProj = Vector3.ProjectOnPlane(hintPos - a, atDir);
            if (curMidProj.sqrMagnitude > 1e-5f && hintProj.sqrMagnitude > 1e-5f)
            {
                float twistAngle = Vector3.SignedAngle(curMidProj, hintProj, atDir);
                root.rotation = Quaternion.AngleAxis(twistAngle * 0.65f, atDir) * root.rotation;
            }
        }

        private static Transform FindDeepChild(Transform parent, string targetName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == targetName)
                    return child;
                Transform found = FindDeepChild(child, targetName);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
