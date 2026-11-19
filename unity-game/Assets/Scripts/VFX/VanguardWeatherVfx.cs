using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Rain-swept dusk precipitation and moody atmospheric mist motes that track the active camera.
    /// Gives BLACKSITE-09 unmistakable hyper-realistic tactical atmosphere in every frame.
    /// </summary>
    public class VanguardWeatherVfx : MonoBehaviour
    {
        [SerializeField] private float rainHeight = 18f;
        [SerializeField] private Vector3 rainBoxSize = new Vector3(44f, 6f, 44f);
        [SerializeField] private int rainEmissionRate = 320;
        [SerializeField] private int dustEmissionRate = 18;

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
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = VanguardPalette.SlateCool;

            // Rain streaks
            var rainGo = new GameObject("BlacksiteRain");
            rainGo.transform.SetParent(transform, false);
            _rainPs = rainGo.AddComponent<ParticleSystem>();
            var main = _rainPs.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.45f;
            main.startSpeed = 22f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = VanguardPalette.WithAlpha(VanguardPalette.SlateCool, 0.34f);
            main.maxParticles = 900;

            var emission = _rainPs.emission;
            emission.rateOverTime = rainEmissionRate;

            var shape = _rainPs.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rainBoxSize;
            rainGo.transform.localRotation = Quaternion.Euler(96f, 18f, 0f); // Slight wind-blown angle downward

            var rend = rainGo.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 14f;
            rend.velocityScale = 0.04f;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Atmospheric windblown mist/dust motes catching floodlights
            var mistGo = new GameObject("BlacksiteMistMotes");
            mistGo.transform.SetParent(transform, false);
            mistGo.transform.localPosition = new Vector3(0f, -rainHeight * 0.65f, 0f);
            _mistPs = mistGo.AddComponent<ParticleSystem>();
            var mMain = _mistPs.main;
            mMain.loop = true;
            mMain.playOnAwake = true;
            mMain.simulationSpace = ParticleSystemSimulationSpace.World;
            mMain.startLifetime = 6.5f;
            mMain.startSpeed = 0.65f;
            mMain.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            mMain.startColor = VanguardPalette.WithAlpha(VanguardPalette.MoonKeyLight, 0.18f);
            mMain.maxParticles = 140;

            var mEm = _mistPs.emission;
            mEm.rateOverTime = dustEmissionRate;

            var mShape = _mistPs.shape;
            mShape.shapeType = ParticleSystemShapeType.Box;
            mShape.scale = new Vector3(36f, 8f, 36f);

            var mCol = _mistPs.colorOverLifetime;
            mCol.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(VanguardPalette.MoonKeyLight, 0f), new GradientColorKey(VanguardPalette.SlateCool, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.22f, 0.35f), new GradientAlphaKey(0f, 1f) });
            mCol.color = grad;

            var mRend = mistGo.GetComponent<ParticleSystemRenderer>();
            mRend.sharedMaterial = mat;
            mRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _rainPs.Play();
            _mistPs.Play();
        }
    }
}
