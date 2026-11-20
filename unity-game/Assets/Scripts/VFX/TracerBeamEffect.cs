using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Pooled thin ballistic HDR tracer streak connecting muzzle to impact point.
    /// Fast, crisp, tapering warm-gold streak — zero thick lightsaber bars.
    /// </summary>
    public class TracerBeamEffect : MonoBehaviour
    {
        private LineRenderer _line;
        private float _age;
        private float _lifetime = 0.055f;
        private Color _startColor;
        private Color _endColor;

        private void Awake()
        {
            _line = gameObject.GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
        }

        public void Initialize(Material mat)
        {
            if (_line == null) Awake();
            _line.sharedMaterial = mat;
        }

        public void Fire(Vector3 from, Vector3 to, float width = 0.013f, float lifetime = 0.055f, Color? tint = null)
        {
            if (_line == null) Awake();
            _lifetime = Mathf.Max(0.028f, lifetime);
            _age = 0f;

            _startColor = VanguardPalette.WithAlpha(tint ?? VanguardPalette.TracerGold, 0.72f);
            _endColor = VanguardPalette.WithAlpha(_startColor, 0.08f);

            // Start tracer slightly ahead of muzzle so it never fills the camera near-plane
            Vector3 dir = (to - from);
            float dist = dir.magnitude;
            Vector3 startPos = dist > 2.2f ? from + (dir / dist) * 0.85f : from;

            _line.startWidth = width;
            _line.endWidth = width * 0.18f;
            _line.SetPosition(0, startPos);
            _line.SetPosition(1, to);
            _line.startColor = _startColor;
            _line.endColor = _endColor;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _lifetime);
            if (t >= 1f)
            {
                gameObject.SetActive(false);
                return;
            }

            float fade = 1f - Ease.OutCubic(t);
            _line.startColor = VanguardPalette.WithAlpha(_startColor, _startColor.a * fade);
            _line.endColor = VanguardPalette.WithAlpha(_endColor, _endColor.a * fade);
        }
    }
}
