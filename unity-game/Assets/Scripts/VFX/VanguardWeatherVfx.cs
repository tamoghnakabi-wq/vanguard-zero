using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Photorealistic rain-swept dusk weather: delicate tapered alpha rain streaks,
    /// subtle low-lying drifting atmospheric fog sheets, and soft ground-level mist.
    /// Zero thick grey bars or square quads.
    /// </summary>
    public class VanguardWeatherVfx : MonoBehaviour
    {
        [SerializeField] private float rainHeight = 16f;
        [SerializeField] private Vector3 rainBoxSize = new Vector3(46f, 5f, 46f);
        [SerializeField] private int rainEmissionRate = 380;
        [SerializeField] private int fogSheetRate = 5;

        private ParticleSystem _rainPs;
        private ParticleSystem _mistPs;
        private Transform _followTarget;

        private void Awake()
        {
            BuildWeatherSystems();
        }

        private void LateUpdate()
        {
            if (_followTarget == null)
            {
                var cam = Camera.main;
                if (cam != null) _followTarget = cam.transform;
            }

            if (_followTarget != null)
            {
                Vector3 p = _followTarget.position;
                transform.position = new Vector3(p.x, Mathf.Max(0f, p.y) + rainHeight, p.z);
            }
        }

        private void BuildWeatherSystems()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var rainTex = Resources.Load<Texture2D>("VFX/T_RainDropStreak");
            var smokeTex = Resources.Load<Texture2D>("VFX/T_SoftSmoke");

            var rainMat = VanguardVfxManager.CreateTransparentParticleMat(shader, rainTex, additive: false);
            var mistMat = VanguardVfxManager.CreateTransparentParticleMat(shader, smokeTex, additive: false);

            // Delicate semi-transparent slanted rain streaks
            var rainGo = new GameObject("BlacksiteRain");
            rainGo.transform.SetParent(transform, false);
            _rainPs = rainGo.AddComponent<ParticleSystem>();
            var main = _rainPs.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.05f;
            main.startSpeed = 24f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.011f, 0.019f);
            main.startColor = new Color(0.76f, 0.85f, 0.96f, 0.20f);
            main.maxParticles = 900;

            var emission = _rainPs.emission;
            emission.rateOverTime = rainEmissionRate;

            var shape = _rainPs.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rainBoxSize;
            rainGo.transform.localRotation = Quaternion.Euler(97f, 16f, 0f);

            var rend = rainGo.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 7.5f;
            rend.velocityScale = 0.025f;
            rend.sharedMaterial = rainMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Soft low-drifting atmospheric rain-mist / fog clouds hugging the courtyard
            var mistGo = new GameObject("BlacksiteLowMist");
            mistGo.transform.SetParent(transform, false);
            mistGo.transform.localPosition = new Vector3(0f, -rainHeight + 2.4f, 0f);
            _mistPs = mistGo.AddComponent<ParticleSystem>();
            var mMain = _mistPs.main;
            mMain.loop = true;
            mMain.playOnAwake = true;
            mMain.simulationSpace = ParticleSystemSimulationSpace.World;
            mMain.startLifetime = 9.0f;
            mMain.startSpeed = 1.2f;
            mMain.startSize = new ParticleSystem.MinMaxCurve(6.5f, 12.5f);
            mMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            mMain.startColor = new Color(0.55f, 0.65f, 0.78f, 0.065f);
            mMain.maxParticles = 64;

            var mEm = _mistPs.emission;
            mEm.rateOverTime = fogSheetRate;

            var mShape = _mistPs.shape;
            mShape.shapeType = ParticleSystemShapeType.Box;
            mShape.scale = new Vector3(44f, 2.5f, 44f);
            mistGo.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);

            var mCol = _mistPs.colorOverLifetime;
            mCol.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.52f, 0.62f, 0.76f), 0f), new GradientColorKey(new Color(0.45f, 0.54f, 0.68f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.065f, 0.3f), new GradientAlphaKey(0.055f, 0.7f), new GradientAlphaKey(0f, 1f) });
            mCol.color = grad;

            var mRend = mistGo.GetComponent<ParticleSystemRenderer>();
            mRend.sharedMaterial = mistMat;
            mRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _rainPs.Play();
            _mistPs.Play();
        }
    }
}
