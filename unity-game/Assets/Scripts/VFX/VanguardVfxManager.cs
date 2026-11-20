using System.Collections.Generic;
using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Pooled AAA tactical VFX manager using real alpha-feathered PNG sprites:
    /// thin ballistic tracers, wispy gunsmoke, glowing ricochet sparks, realistic bullet decals,
    /// and ejected brass shell casings. Zero square particles or GC allocations per shot.
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
        private Material _smokeMat;
        private Material _sparkMat;
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
            var particleUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");

            var smokeTex = Resources.Load<Texture2D>("VFX/T_SoftSmoke");
            var sparkTex = Resources.Load<Texture2D>("VFX/T_SparkStreak");
            var decalTex = Resources.Load<Texture2D>("VFX/T_BulletDecal");

            // Thin additive warm ballistic tracer material
            _tracerMat = CreateTransparentParticleMat(particleUnlit, null, additive: true);
            _tracerMat.SetColor("_BaseColor", VanguardPalette.TracerGold * 1.45f);

            // Soft alpha-blended smoke / dust puff material
            _smokeMat = CreateTransparentParticleMat(particleUnlit, smokeTex, additive: false);
            _smokeMat.SetColor("_BaseColor", Color.white);

            // Glowing additive hot ricochet spark material
            _sparkMat = CreateTransparentParticleMat(particleUnlit, sparkTex, additive: true);
            _sparkMat.SetColor("_BaseColor", VanguardPalette.SparkHot * 1.7f);

            // Alpha-blended realistic bullet hole crater decal (zero black squares!)
            _decalMat = CreateTransparentParticleMat(particleUnlit, decalTex, additive: false);
            _decalMat.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f, 0.92f));

            _brassMat = new Material(urpLit);
            _brassMat.SetColor("_BaseColor", VanguardPalette.BrassCasing);
            if (_brassMat.HasProperty("_Metallic")) _brassMat.SetFloat("_Metallic", 0.92f);
            if (_brassMat.HasProperty("_Smoothness")) _brassMat.SetFloat("_Smoothness", 0.80f);
        }

        public static Material CreateTransparentParticleMat(Shader shader, Texture2D mainTex, bool additive)
        {
            var mat = new Material(shader);
            if (mainTex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                mat.mainTexture = mainTex;
            }
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 2f : 0f); // 0=Alpha, 2=Additive
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) mat.EnableKeyword("_BLENDMODE_ADD");
            mat.renderQueue = 3000;
            return mat;
        }

        private void BuildPools()
        {
            for (int i = 0; i < 32; i++)
            {
                var go = new GameObject($"Tracer_{i:00}");
                go.transform.SetParent(transform, false);
                var tracer = go.AddComponent<TracerBeamEffect>();
                tracer.Initialize(_tracerMat);
                go.SetActive(false);
                _tracers.Add(tracer);
            }

            for (int i = 0; i < 24; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = $"BrassShell_{i:00}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.009f, 0.019f, 0.009f);
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                var rend = go.GetComponent<Renderer>();
                rend.sharedMaterial = _brassMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var proj = go.AddComponent<ShellCasingProjectile>();
                go.SetActive(false);
                _shells.Add(proj);
            }

            for (int i = 0; i < 48; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"BulletDecal_{i:00}";
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.14f, 0.14f, 1f);
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                var rend = go.GetComponent<Renderer>();
                rend.sharedMaterial = _decalMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _decals.Add(go.transform);
            }

            _concreteImpactPs = CreateBurstParticleSystem("PS_ConcreteImpact", _smokeMat, VanguardPalette.WithAlpha(VanguardPalette.DustConcrete, 0.65f), 0.14f, 0.34f, 0.8f, 3.0f, 0.65f, 0.35f, false);
            _metalSparkPs = CreateBurstParticleSystem("PS_MetalSparks", _sparkMat, VanguardPalette.SparkHot * 1.6f, 0.035f, 0.08f, 4.5f, 10.5f, 0.28f, 1.9f, true);
            _fleshImpactPs = CreateBurstParticleSystem("PS_FleshImpact", _smokeMat, VanguardPalette.WithAlpha(VanguardPalette.AccentCrimson, 0.72f), 0.10f, 0.24f, 0.9f, 2.6f, 0.36f, 0.55f, false);
            _muzzleSmokePs = CreateBurstParticleSystem("PS_MuzzleSmoke", _smokeMat, new Color(0.75f, 0.78f, 0.82f, 0.26f), 0.12f, 0.28f, 0.7f, 2.2f, 0.45f, -0.08f, false);
        }

        private ParticleSystem CreateBurstParticleSystem(
            string psName, Material mat, Color color, float minSize, float maxSize,
            float minSpeed, float maxSpeed, float lifetime, float gravityMod, bool stretched)
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
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.gravityModifier = gravityMod;
            main.maxParticles = 256;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = stretched ? 36f : 28f;
            shape.radius = 0.025f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = stretched
                ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f))
                : new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.85f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (stretched)
            {
                rend.renderMode = ParticleSystemRenderMode.Stretch;
                rend.lengthScale = 3.2f;
                rend.velocityScale = 0.03f;
            }

            return ps;
        }

        public void SpawnTracer(Vector3 muzzlePos, Vector3 endPos, bool isShotgun = false)
        {
            if (_tracers.Count == 0) return;
            var tracer = _tracers[_tracerCursor];
            _tracerCursor = (_tracerCursor + 1) % _tracers.Count;
            tracer.Fire(muzzlePos, endPos, isShotgun ? 0.009f : 0.013f, isShotgun ? 0.042f : 0.055f, VanguardPalette.TracerGold);
        }

        public void SpawnMuzzleBurst(Vector3 pos, Vector3 forward)
        {
            if (_muzzleSmokePs == null) return;
            _muzzleSmokePs.transform.position = pos;
            _muzzleSmokePs.transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward);
            _muzzleSmokePs.Emit(4);
        }

        public void SpawnShellCasing(Vector3 ejectPos, Vector3 ejectRight, Vector3 ejectUp, Vector3 carrierVel)
        {
            if (_shells.Count == 0) return;
            var shell = _shells[_shellCursor];
            _shellCursor = (_shellCursor + 1) % _shells.Count;

            Vector3 vel = carrierVel
                + ejectRight * UnityEngine.Random.Range(1.7f, 2.6f)
                + ejectUp * UnityEngine.Random.Range(1.1f, 1.9f);
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
                    _metalSparkPs.Emit(12);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_metal", 2, point, AudioBus.Impacts, 0.72f, 0.09f, 40f);
                    SpawnDecal(point, normal, 0.11f);
                    break;

                case VanguardSurfaceTag.SurfaceKind.Flesh:
                    _fleshImpactPs.transform.SetPositionAndRotation(point + normal * 0.02f, rot);
                    _fleshImpactPs.Emit(8);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_flesh", 1, point, AudioBus.Impacts, 0.78f, 0.08f, 35f);
                    break;

                default:
                    _concreteImpactPs.transform.SetPositionAndRotation(point + normal * 0.02f, rot);
                    _concreteImpactPs.Emit(9);
                    VanguardAudioManager.Instance?.PlayRandom3D("impact_concrete", 2, point, AudioBus.Impacts, 0.68f, 0.09f, 40f);
                    SpawnDecal(point, normal, 0.15f);
                    break;
            }
        }

        private void SpawnDecal(Vector3 point, Vector3 normal, float size)
        {
            if (_decals.Count == 0) return;
            var decal = _decals[_decalCursor];
            _decalCursor = (_decalCursor + 1) % _decals.Count;
            decal.position = point + normal * 0.008f;
            decal.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            decal.localScale = new Vector3(size, size, 1f);
            decal.gameObject.SetActive(true);
        }
    }
}
