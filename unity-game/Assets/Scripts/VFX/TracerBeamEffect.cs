using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.VFX
{
    /// <summary>
    /// Pooled HDR tracer beam connecting muzzle to impact point.
    /// </summary>
    public class TracerBeamEffect : MonoBehaviour
    {
        private LineRenderer _line;
        private float _age;
        private float _lifetime = 0.09f;
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

        public void Fire(Vector3 from, Vector3 to, float width = 0.024f, float lifetime = 0.085f, Color? tint = null)
        {
            if (_line == null) Awake();
            _lifetime = Mathf.Max(0.03f, lifetime);
            _age = 0f;

            _startColor = tint ?? VanguardPalette.TracerGold;
            _endColor = VanguardPalette.WithAlpha(_startColor, 0.15f);

            _line.startWidth = width;
            _line.endWidth = width * 0.45f;
            _line.SetPosition(0, from);
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
            _line.startColor = VanguardPalette.WithAlpha(_startColor, fade);
            _line.endColor = VanguardPalette.WithAlpha(_endColor, fade * 0.35f);
        }
    }
}
