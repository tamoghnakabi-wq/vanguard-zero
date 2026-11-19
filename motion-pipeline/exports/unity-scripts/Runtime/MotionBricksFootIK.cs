using System;
using UnityEngine;

namespace VanguardZero.Animation
{
    /// <summary>
    /// Grounding Foot-IK & Surface-Aware Footstep Event Router driven by NVIDIA MotionBricks'
    /// neural foot-contact curves (`LeftFootContact`, `RightFootContact`) baked into each clip.
    /// Eliminates foot skating/clipping on BLACKSITE-09 concrete steps, catwalks, and ramps,
    /// and emits <see cref="OnFootstep"/> events synced to exact neural heel/toe strikes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksFootIK : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private Transform hipsBone;
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform leftLowerLeg;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField] private Transform rightLowerLeg;
        [SerializeField] private Transform rightFoot;

        [Header("Ground Raycast Settings")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float rayOriginHeight = 0.55f;
        [SerializeField] private float rayDistance = 1.15f;
        [SerializeField] private float footSoleOffset = 0.08f;
        [SerializeField] private float maxPelvisDrop = 0.28f;
        [SerializeField] private float pelvisSmoothSpeed = 14f;

        private static readonly int HashLeftContact = Animator.StringToHash("LeftFootContact");
        private static readonly int HashRightContact = Animator.StringToHash("RightFootContact");

        public event Action<MotionBricksFootstepPayload> OnFootstep;

        private bool _isGrounded = true;
        private bool _isCrouching;
        private bool _isSprinting;
        private bool _isDead;
        private float _pelvisOffsetY;
        private bool _bonesResolved;
        private readonly RaycastHit[] _raycastHits = new RaycastHit[16];

        private void Awake()
        {
            ResolveBones();
            EnsureAnimationEventRelay();
        }

        private void EnsureAnimationEventRelay()
        {
            if (targetAnimator == null || targetAnimator.gameObject == gameObject)
                return;
            var relay = targetAnimator.gameObject.GetComponent<MotionBricksAnimEventRelay>();
            if (relay == null)
                relay = targetAnimator.gameObject.AddComponent<MotionBricksAnimEventRelay>();
            relay.Bind(this);
        }

        public void ResolveBones()
        {
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>();

            if (targetAnimator != null && targetAnimator.isHuman)
            {
                hipsBone = hipsBone != null ? hipsBone : targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
                leftUpperLeg = leftUpperLeg != null ? leftUpperLeg : targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                leftLowerLeg = leftLowerLeg != null ? leftLowerLeg : targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                leftFoot = leftFoot != null ? leftFoot : targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightUpperLeg = rightUpperLeg != null ? rightUpperLeg : targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                rightLowerLeg = rightLowerLeg != null ? rightLowerLeg : targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                rightFoot = rightFoot != null ? rightFoot : targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            }

            hipsBone = hipsBone != null ? hipsBone : FindDeepChild(transform, "Hips");
            leftUpperLeg = leftUpperLeg != null ? leftUpperLeg : FindDeepChild(transform, "LeftUpperLeg");
            leftLowerLeg = leftLowerLeg != null ? leftLowerLeg : FindDeepChild(transform, "LeftLowerLeg");
            leftFoot = leftFoot != null ? leftFoot : FindDeepChild(transform, "LeftFoot");
            rightUpperLeg = rightUpperLeg != null ? rightUpperLeg : FindDeepChild(transform, "RightUpperLeg");
            rightLowerLeg = rightLowerLeg != null ? rightLowerLeg : FindDeepChild(transform, "RightLowerLeg");
            rightFoot = rightFoot != null ? rightFoot : FindDeepChild(transform, "RightFoot");

            _bonesResolved = hipsBone != null && leftFoot != null && rightFoot != null;
        }

        public void SetLocomotionContext(bool isGrounded, bool isCrouching, bool isSprinting, bool isDead)
        {
            _isGrounded = isGrounded;
            _isCrouching = isCrouching;
            _isSprinting = isSprinting;
            _isDead = isDead;
        }

        /// <summary>
        /// AnimationEvent receiver baked into every MotionBricks AnimationClip at neural foot strikes.
        /// </summary>
        public void OnMotionBricksFootstep(string footSide)
        {
            if (_isDead || !_isGrounded)
                return;

            bool isLeft = string.Equals(footSide, "Left", StringComparison.OrdinalIgnoreCase);
            Transform foot = isLeft ? leftFoot : rightFoot;
            Vector3 probePos = foot != null ? foot.position : transform.position;
            string surfaceTag = "Concrete";
            Vector3 normal = Vector3.up;
            Vector3 hitPos = probePos;

            if (TryRaycastGroundExcludingSelf(probePos + Vector3.up * 0.35f, 0.9f, out RaycastHit hit))
            {
                hitPos = hit.point;
                normal = hit.normal;
                surfaceTag = ResolveSurfaceTag(hit.collider);
            }

            OnFootstep?.Invoke(new MotionBricksFootstepPayload
            {
                IsLeftFoot = isLeft,
                WorldPosition = hitPos,
                SurfaceNormal = normal,
                SurfaceTag = surfaceTag,
                Intensity = _isSprinting ? 1.0f : (_isCrouching ? 0.45f : 0.78f),
                IsCrouching = _isCrouching,
                IsSprinting = _isSprinting,
            });
        }

        private void LateUpdate()
        {
            if (_isDead || !_isGrounded || targetAnimator == null)
                return;

            if (!_bonesResolved)
                ResolveBones();
            if (!_bonesResolved)
                return;

            float leftWeight = Mathf.Clamp01(targetAnimator.GetFloat(HashLeftContact));
            float rightWeight = Mathf.Clamp01(targetAnimator.GetFloat(HashRightContact));

            bool hitL = ProbeFoot(leftFoot.position, out Vector3 targetL, out Vector3 normalL);
            bool hitR = ProbeFoot(rightFoot.position, out Vector3 targetR, out Vector3 normalR);

            // Lower pelvis slightly on uneven stairs/ramps so planted foot never over-stretches
            float desiredPelvisOffset = 0f;
            if (hitL && leftWeight > 0.25f)
                desiredPelvisOffset = Mathf.Min(desiredPelvisOffset, targetL.y - leftFoot.position.y);
            if (hitR && rightWeight > 0.25f)
                desiredPelvisOffset = Mathf.Min(desiredPelvisOffset, targetR.y - rightFoot.position.y);
            desiredPelvisOffset = Mathf.Clamp(desiredPelvisOffset, -maxPelvisDrop, 0.05f);

            _pelvisOffsetY = Mathf.Lerp(_pelvisOffsetY, desiredPelvisOffset, 1f - Mathf.Exp(-pelvisSmoothSpeed * Time.deltaTime));
            hipsBone.position += Vector3.up * _pelvisOffsetY;

            // Apply Two-Bone Foot IK weighted by MotionBricks' neural contact confidence
            if (hitL && leftWeight > 0.05f && leftUpperLeg != null && leftLowerLeg != null)
            {
                Vector3 kneeHint = leftLowerLeg.position + transform.forward * 0.45f;
                TacticalUpperBodyAimIK.SolveTwoBoneIK(leftUpperLeg, leftLowerLeg, leftFoot, targetL, kneeHint, leftWeight * 0.85f);
                AlignFootToNormal(leftFoot, normalL, leftWeight * 0.75f);
            }
            if (hitR && rightWeight > 0.05f && rightUpperLeg != null && rightLowerLeg != null)
            {
                Vector3 kneeHint = rightLowerLeg.position + transform.forward * 0.45f;
                TacticalUpperBodyAimIK.SolveTwoBoneIK(rightUpperLeg, rightLowerLeg, rightFoot, targetR, kneeHint, rightWeight * 0.85f);
                AlignFootToNormal(rightFoot, normalR, rightWeight * 0.75f);
            }
        }

        private bool ProbeFoot(Vector3 footPos, out Vector3 targetPos, out Vector3 surfaceNormal)
        {
            Vector3 origin = new Vector3(footPos.x, transform.position.y + rayOriginHeight, footPos.z);
            if (TryRaycastGroundExcludingSelf(origin, rayDistance, out RaycastHit hit))
            {
                targetPos = hit.point + hit.normal * footSoleOffset;
                surfaceNormal = hit.normal;
                return true;
            }
            targetPos = footPos;
            surfaceNormal = Vector3.up;
            return false;
        }

        private bool TryRaycastGroundExcludingSelf(Vector3 origin, float distance, out RaycastHit closestHit)
        {
            closestHit = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _raycastHits, distance, groundLayers, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            Transform selfRoot = transform.root;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _raycastHits[i];
                if (h.collider == null)
                    continue;
                Transform ht = h.collider.transform;
                if (ht == transform || ht.IsChildOf(transform) || (selfRoot != null && (ht == selfRoot || ht.IsChildOf(selfRoot))))
                    continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    closestHit = h;
                    found = true;
                }
            }
            return found;
        }

        private static void AlignFootToNormal(Transform foot, Vector3 surfaceNormal, float weight)
        {
            Quaternion align = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            foot.rotation = Quaternion.Slerp(foot.rotation, align * foot.rotation, Mathf.Clamp01(weight));
        }

        private static string ResolveSurfaceTag(Collider col)
        {
            if (col == null)
                return "Concrete";
            string nameLower = col.name.ToLowerInvariant();
            if (nameLower.Contains("metal") || nameLower.Contains("catwalk") || nameLower.Contains("container") || nameLower.Contains("grate") || nameLower.Contains("steel"))
                return "Metal";
            if (!string.IsNullOrEmpty(col.tag) && col.tag != "Untagged")
                return col.tag;
            return "Concrete";
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
