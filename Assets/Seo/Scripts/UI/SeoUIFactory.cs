using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Seo.UI
{
    public static class SeoUIFactory
    {
        public static readonly Vector2 LandscapeReference = new Vector2(1920f, 1080f);

        public static Image CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            ApplyPanel(image, color);
            return image;
        }

        public static Text CreateText(Transform parent, string name, string value, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = SeoUITheme.Current.Text;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, UnityAction action,
            Color? tint = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            ApplyButton(button, tint);
            CreateText(go.transform, "Label", label, 19, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        public static void ApplyPanel(Image image, Color? color = null)
        {
            if (image == null) return;
            var theme = SeoUITheme.Current;
            image.sprite = theme.PanelSprite;
            image.type = theme.PanelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color ?? theme.ScreenPanel;
        }

        public static void ApplyButton(Button button, Color? tint = null)
        {
            if (button == null) return;
            var theme = SeoUITheme.Current;
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = theme.ButtonSprite;
                image.type = theme.ButtonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = tint ?? Color.white;
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.highlightedSprite = theme.ButtonPressedSprite;
            state.pressedSprite = theme.ButtonPressedSprite;
            state.selectedSprite = theme.ButtonPressedSprite;
            button.spriteState = state;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.color = theme.Text;
                label.fontStyle = FontStyle.Bold;
                label.raycastTarget = false;
            }
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            if (rt == null) return;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
        }
    }
}
