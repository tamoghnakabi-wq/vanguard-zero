using UnityEngine;

namespace VanguardZero.Core
{
    /// <summary>
    /// Tags world surfaces and props for realistic impact sparks, bullet decals, and footstep audio.
    /// Surface indices match VanguardZero.Networking.HitSurfaceType:
    /// 0=Concrete, 1=Metal, 2=Flesh, 3=Armor, 4=Dirt.
    /// </summary>
    public class VanguardSurfaceTag : MonoBehaviour
    {
        public enum SurfaceKind : byte
        {
            Concrete = 0,
            Metal = 1,
            Flesh = 2,
            Armor = 3,
            Dirt = 4
        }

        [SerializeField] private SurfaceKind surface = SurfaceKind.Concrete;

        public SurfaceKind Surface
        {
            get => surface;
            set => surface = value;
        }

        public static SurfaceKind ResolveFromCollider(Collider col)
        {
            if (col == null) return SurfaceKind.Concrete;
            if (col.TryGetComponent<VanguardSurfaceTag>(out var tag))
                return tag.Surface;
            var parentTag = col.GetComponentInParent<VanguardSurfaceTag>();
            if (parentTag != null)
                return parentTag.Surface;
            return SurfaceKind.Concrete;
        }
    }
}
