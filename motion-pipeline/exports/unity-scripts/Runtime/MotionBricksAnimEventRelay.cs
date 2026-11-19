using UnityEngine;

namespace VanguardZero.Animation
{
    /// <summary>
    /// Lightweight AnimationEvent receiver auto-attached to the child Animator GameObject
    /// whenever <see cref="MotionBricksFootIK"/> lives on a parent character root, ensuring
    /// Unity's `OnMotionBricksFootstep` AnimationEvents never log missing-receiver errors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionBricksAnimEventRelay : MonoBehaviour
    {
        private MotionBricksFootIK _target;

        public void Bind(MotionBricksFootIK target)
        {
            _target = target;
        }

        public void OnMotionBricksFootstep(string footSide)
        {
            if (_target != null)
                _target.OnMotionBricksFootstep(footSide);
        }
    }
}
