using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VanguardZero.Core;

namespace VanguardZero.UI
{
    /// <summary>
    /// Reusable uGUI + TextMeshPro builder styled exclusively with VanguardPalette.
    /// Guarantees crisp AAA tactical chrome, zero default Unity gray skins, and valid sprites
    /// for all Filled bars.
    /// </summary>
    public static class VanguardUiFactory
    {
        private static Sprite _solidSprite;

        public static Sprite SolidSprite
        {
            get
            {
                if (_solidSprite != null) return _solidSprite;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var cols = new Color[16];
                for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                tex.SetPixels(cols);
                tex.Apply(false, true);
                _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                _solidSprite.name = "VanguardSolidSprite";
                return _solidSprite;
            }
        }

        public static Canvas CreateHudCanvas(string name, int sortingOrder = 0)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        public static RectTransform CreateFullStretch(string name, Transform parent, float padLeft = 0f, float padBottom = 0f, float padRight = 0f, float padTop = 0f)
        {
            var rt = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.offsetMin = new Vector2(padLeft, padBottom);
            rt.offsetMax = new Vector2(-padRight, -padTop);
            return rt;
        }

        public static UnityEngine.UI.Image CreatePanel(
            string name,
            Transform parent,
            Color bgColor,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 pos,
            Vector2 size,
            bool withBorder = true,
            bool raycastTarget = false)
        {
            var rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = SolidSprite;
            img.color = bgColor;
            img.raycastTarget = raycastTarget;

            if (withBorder)
            {
                var outline = rt.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = VanguardPalette.HudBorder;
                outline.effectDistance = new Vector2(1f, -1f);
            }

            return img;
        }

        public static UnityEngine.UI.Image AddAccentStripe(Transform parent, Color accent, bool leftEdge = true, float thickness = 4f)
        {
            RectTransform rt = leftEdge
                ? CreateRect("AccentStripe", parent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(thickness, 0f))
                : CreateRect("AccentStripe", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, thickness));
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = SolidSprite;
            img.color = accent;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI CreateLabel(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal,
            float characterSpacing = 1.5f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.characterSpacing = characterSpacing;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        public static UnityEngine.UI.Button CreateTacticalButton(
            string name,
            Transform parent,
            string label,
            Color accentColor,
            UnityEngine.Events.UnityAction onClick,
            float height = 52f,
            float fontSize = 18f,
            bool primaryStyle = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.sprite = SolidSprite;
            bg.color = primaryStyle ? VanguardPalette.WithAlpha(accentColor, 0.92f) : VanguardPalette.HudButtonNormal;
            bg.raycastTarget = true;

            var outline = go.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = primaryStyle ? accentColor : VanguardPalette.HudBorder;
            outline.effectDistance = new Vector2(1f, -1f);

            if (!primaryStyle)
            {
                AddAccentStripe(go.transform, accentColor, true, 4f);
            }

            var btn = go.AddComponent<UnityEngine.UI.Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primaryStyle ? new Color(1.12f, 1.12f, 1.12f, 1f) : new Color(1.35f, 1.42f, 1.52f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var txt = CreateLabel(
                "Label",
                go.transform,
                label,
                fontSize,
                primaryStyle ? VanguardPalette.ShadowVoid : VanguardPalette.HudLight,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                1.0f);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(20f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);

            btn.onClick.AddListener(() =>
            {
                VanguardAudioManager.Instance?.Play2D("ui_click", AudioBus.UI, 0.7f, 0.03f);
                onClick?.Invoke();
            });

            return btn;
        }

        public static TMP_InputField CreateTacticalInput(
            string name,
            Transform parent,
            string placeholder,
            string initialValue = "",
            float height = 44f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.sprite = SolidSprite;
            bg.color = VanguardPalette.HudPanelDark;

            var outline = go.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = VanguardPalette.HudBorder;
            outline.effectDistance = new Vector2(1f, -1f);

            var textArea = CreateFullStretch("TextArea", go.transform, 14f, 4f, 14f, 4f);
            textArea.gameObject.AddComponent<RectMask2D>();

            var ph = CreateLabel("Placeholder", textArea, placeholder, 16f, VanguardPalette.HudMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Italic, 1f);
            ph.rectTransform.anchorMin = Vector2.zero;
            ph.rectTransform.anchorMax = Vector2.one;
            ph.rectTransform.offsetMin = Vector2.zero;
            ph.rectTransform.offsetMax = Vector2.zero;

            var txt = CreateLabel("Text", textArea, initialValue, 17f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1.5f);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero;
            txt.rectTransform.offsetMax = Vector2.zero;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = txt;
            input.placeholder = ph;
            input.text = initialValue;
            input.caretColor = VanguardPalette.AccentAmber;
            input.selectionColor = VanguardPalette.WithAlpha(VanguardPalette.AccentCyan, 0.35f);
            return input;
        }

        public static UnityEngine.UI.Slider CreateTacticalSlider(
            string name,
            Transform parent,
            string caption,
            float min,
            float max,
            float initial,
            System.Action<float> onValueChanged,
            out TextMeshProUGUI valueLabel)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 46f;
            le.minHeight = 46f;

            var cap = CreateLabel("Caption", row.transform, caption, 15f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1.5f);
            cap.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            cap.rectTransform.anchorMax = new Vector2(0.55f, 1f);
            cap.rectTransform.offsetMin = Vector2.zero;
            cap.rectTransform.offsetMax = Vector2.zero;

            valueLabel = CreateLabel("Value", row.transform, initial.ToString("0.00"), 15f, VanguardPalette.AccentCyan, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 1f);
            valueLabel.rectTransform.anchorMin = new Vector2(0.55f, 0.5f);
            valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueLabel.rectTransform.offsetMin = Vector2.zero;
            valueLabel.rectTransform.offsetMax = Vector2.zero;

            var sliderGo = new GameObject("Slider", typeof(RectTransform));
            sliderGo.transform.SetParent(row.transform, false);
            var srt = sliderGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 0.45f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;

            var bg = CreateFullStretch("Background", sliderGo.transform, 0f, 4f, 0f, 4f);
            var bgImg = bg.gameObject.AddComponent<UnityEngine.UI.Image>();
            bgImg.sprite = SolidSprite;
            bgImg.color = VanguardPalette.ConcreteTrim;

            var fillArea = CreateFullStretch("FillArea", sliderGo.transform, 0f, 4f, 0f, 4f);
            var fill = CreateFullStretch("Fill", fillArea);
            var fillImg = fill.gameObject.AddComponent<UnityEngine.UI.Image>();
            fillImg.sprite = SolidSprite;
            fillImg.color = VanguardPalette.AccentAmber;

            var handleArea = CreateFullStretch("HandleArea", sliderGo.transform, 6f, 0f, 6f, 0f);
            var handle = CreateRect("Handle", handleArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 0f));
            var handleImg = handle.gameObject.AddComponent<UnityEngine.UI.Image>();
            handleImg.sprite = SolidSprite;
            handleImg.color = VanguardPalette.HudLight;

            var slider = sliderGo.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;

            var capturedLabel = valueLabel;
            slider.onValueChanged.AddListener(v =>
            {
                if (capturedLabel != null) capturedLabel.text = max > 10f ? v.ToString("0") : v.ToString("0.00");
                onValueChanged?.Invoke(v);
            });
            if (capturedLabel != null) capturedLabel.text = max > 10f ? initial.ToString("0") : initial.ToString("0.00");

            return slider;
        }

        public static UnityEngine.UI.Image CreateFilledBar(string name, Transform parent, Color bgColor, Color fillColor, Vector2 size, out UnityEngine.UI.Image fillImage)
        {
            var bgRt = CreateRect(name, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, size);
            var bg = bgRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            bg.sprite = SolidSprite;
            bg.color = bgColor;
            bg.raycastTarget = false;

            var fillRt = CreateFullStretch("Fill", bgRt, 1f, 1f, 1f, 1f);
            fillImage = fillRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            fillImage.sprite = SolidSprite;
            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillAmount = 1f;
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            return bg;
        }
    }
}
