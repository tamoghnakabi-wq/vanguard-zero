using System.Collections.Generic;
using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Pooled AAA tactical VFX manager: HDR tracers, surface impact sparks/dust,
    /// bullet decals, muzzle flash bursts, and ejected brass shell casings.
    /// Zero Instantiate/Destroy per shot during gameplay.
    /// </summary>
    public class VanguardVfxManager : MonoBehaviour
    {
        public static VanguardVfxManager Instance { get; private set; }

        private readonly List<TracerBeamEffect> _tracers = new List<TracerBeamEffect>(32);
        private readonly List<ShellCasingProjectile> _shells = new List<ShellCasingProjectile>(24);
        private readonly List<Transform> _decals = new List<Transform>(48);
        private int _tracerCursor;
        private int _shellCursor;
        private int _decalCursor;

        private ParticleSystem _concreteImpactPs;
        private ParticleSystem _metalSparkPs;
        private ParticleSystem _fleshImpactPs;
        private ParticleSystem _muzzleSmokePs;

        private Material _tracerMat;
        private Material _particleMat;
        private Material _decalMat;
        private Material _brassMat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InitializeMaterials();
            BuildPools();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static VanguardVfxManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("VanguardVfxManager");
            return go.AddComponent<VanguardVfxManager>();
        }

        private void InitializeMaterials()
        {
            var spriteShader = Shader.Find("Sprites/Default");
            var urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit") ?? spriteShader;

            _tracerMat = new Material(urpUnlit);
            _tracerMat.color = VanguardPalette.TracerGold * 2.4f; // HDR above bloom threshold

            _particleMat = new Material(spriteShader);
            _particleMat.color = VanguardPalette.HudLight;

            _decalMat = new Material(urpUnlit);
            _decalMat.color = VanguardPalette.WithAlpha(VanguardPalette.ShadowVoid, 0.92f);

            _brassMat = new Material(urpLit);
            _brassMat.SetColor("_BaseColor", VanguardPalette.BrassCasing);
            if (_brassMat.HasProperty("_Metallic")) _brassMat.SetFloat("_Metallic", 0.9f);
            if (_brassMat.HasProperty("_Smoothness")) _brassMat.SetFloat("_Smoothness", 0.78f);
        }

        private void BuildPools()
        {
            // Tracers
            for (int i = 0; i < 32; i++)
            {
                var go = new GameObject($"Tracer_{i:00}");
                go.transform.SetParent(transform, false);
                var tracer = go.AddComponent<TracerBeamEffect>();
                tracer.Initialize(_tracerMat);
                go.SetActive(false);
                _tracers.Add(tracer);
            }

            // Shell Casings
            for (int i = 0; i < 24; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = $"BrassShell_{i:00}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.011f, 0.022f, 0.011f);
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                var rend = go.GetComponent<Renderer>();
                rend.sharedMaterial = _brassMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var proj = go.AddComponent<ShellCasingProjectile>();
                go.SetActive(false);
                _shells.Add(proj);
            }

            // Bullet Decals
            for (int i = 0; i < 48; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"BulletDecal_{i:00}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.11f, 0.11f, 1f);
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                var rend = go.GetComponent<Renderer>();
                rend.sharedMaterial = _decalMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _decals.Add(go.transform);
            }

            // Reusable ParticleSystems via Emit()
            _concreteImpactPs = CreateBurstParticleSystem("PS_ConcreteImpact", VanguardPalette.DustConcrete, 0.06f, 0.16f, 1.5f, 4.5f, 0.55f, 1.1f);
            _metalSparkPs = CreateBurstParticleSystem("PS_MetalSparks", VanguardPalette.SparkHot * 1.9f, 0.025f, 0.06f, 4.0f, 9.5f, 0.35f, 1.8f);
            _fleshImpactPs = CreateBurstParticleSystem("PS_FleshImpact", VanguardPalette.AccentCrimson, 0.05f, 0.12f, 1.2f, 3.4f, 0.32f, 0.7f);
            _muzzleSmokePs = CreateBurstParticleSystem("PS_MuzzleBurst", VanguardPalette.AccentAmber * 1.8f, 0.04f, 0.10f, 2.0f, 5.5f, 0.16f, 0.1f);
        }

        private ParticleSystem CreateBurstParticleSystem(
            string psName, Color color, float minSize, float maxSize,
            float minSpeed, float maxSpeed, float lifetime, float gravityMod)
        {
            var go = new GameObject(psName);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.gravityModifier = gravityMod;
            main.maxParticles = 256;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.03f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = _particleMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return ps;
        }

        public void SpawnTracer(Vector3 muzzlePos, Vector3 endPos, bool isShotgun = false)
        {
            if (_tracers.Count == 0) return;
            var tracer = _tracers[_tracerCursor];
            _tracerCursor = (_tracerCursor + 1) % _tracers.Count;
            tracer.Fire(muzzlePos, endPos, isShotgun ? 0.018f : 0.026f, isShotgun ? 0.065f : 0.085f, VanguardPalette.TracerGold);
        }

        public void SpawnMuzzleBurst(Vector3 pos, Vector3 forward)
        {
            if (_muzzleSmokePs == null) return;
            _muzzleSmokePs.transform.position = pos;
            _muzzleSmokePs.transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward);
            _muzzleSmokePs.Emit(7);
        }

        public void SpawnShellCasing(Vector3 ejectPos, Vector3 ejectRight, Vector3 ejectUp, Vector3 carrierVel)
        {
            if (_shells.Count == 0) return;
            var shell = _shells[_shellCursor];
            _shellCursor = (_shellCursor + 1) % _shells.Count;

            Vector3 vel = carrierVel
                + ejectRight * UnityEngine.Random.Range(1.8f, 2.8f)
                + ejectUp * UnityEngine.Random.Range(1.2f, 2.1f);
            float floorY = ejectPos.y - 1.35f;
            if (Physics.Raycast(ejectPos, Vector3.down, out var hit, 6f))
                floorY = hit.point.y + 0.015f;

            shell.Launch(ejectPos, Quaternion.LookRotation(ejectRight), vel, floorY);
        }

        public void SpawnImpact(Vector3 point, Vector3 normal, VanguardSurfaceTag.SurfaceKind surface)
        {
            Quaternion rot = Quaternion.LookRotation(normal.sqrMagnitude > 0.001f ? normal : Vector3.up);

            switch (surface)
            {
                case VanguardSurfaceTag.SurfaceKind.Metal:
                case VanguardSurfaceTag.SurfaceKind.Armor:
                    _metalSparkPs.transform.SetPositionAndRotation(point + normal * 0.02f, rot);
                    _metalSparkPs.Emit(14);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_metal", 2, point, AudioBus.Impacts, 0.72f, 0.09f, 40f);
                    SpawnDecal(point, normal, 0.09f);
                    break;

                case VanguardSurfaceTag.SurfaceKind.Flesh:
                    _fleshImpactPs.transform.SetPositionAndRotation(point + normal * 0.02f, rot);
                    _fleshImpactPs.Emit(10);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_flesh", 1, point, AudioBus.Impacts, 0.78f, 0.08f, 35f);
                    break;

                default:
                    _concreteImpactPs.transform.SetPositionAndRotation(point + normal * 0.02f, rot);
                    _concreteImpactPs.Emit(12);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_concrete", 2, point, AudioBus.Impacts, 0.68f, 0.09f, 40f);
                    SpawnDecal(point, normal, 0.12f);
                    break;
            }
        }

        private void SpawnDecal(Vector3 point, Vector3 normal, float size)
        {
            if (_decals.Count == 0) return;
            var decal = _decals[_decalCursor];
            _decalCursor = (_decalCursor + 1) % _decals.Count;
            decal.position = point + normal * 0.009f;
            decal.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            decal.localScale = new Vector3(size, size, 1f);
            decal.gameObject.SetActive(true);
        }
    }
}
