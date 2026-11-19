using System;
using System.Collections;
using UnityEngine;

namespace VanguardZero.Core
{
    /// <summary>
    /// Easing curves and unscaled/scaled coroutine tween helpers for responsive UI and game feel.
    /// </summary>
    public static class Ease
    {
        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        public static float OutElastic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }

        public static float InOutSine(float t)
        {
            t = Mathf.Clamp01(t);
            return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
        }

        public static IEnumerator Tween(
            float duration,
            Action<float> apply,
            Func<float, float> ease = null,
            bool unscaled = true)
        {
            if (duration <= 0.0001f)
            {
                apply?.Invoke(1f);
                yield break;
            }

            ease ??= OutCubic;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                apply?.Invoke(ease(t));
                yield return null;
                elapsed += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            }

            apply?.Invoke(1f);
        }
    }
}
