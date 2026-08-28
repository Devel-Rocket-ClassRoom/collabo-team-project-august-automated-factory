using System;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 기계 하나의 현재 상태를 표시하는 순수 View. 데이터 계산은 MachineInfoPresenter가 담당한다.
    public sealed class MachineInfoPanel : UIPanelBase
    {
        private Text titleText;
        private Text statusText;
        private Text recipeText;
        private Text inputText;
        private Text outputText;
        private Text progressText;
        private Text portsText;
        private Image accentBar;
        private Image progressFill;
        private Button recipeButton;

        public event Action CloseRequested;
        public event Action RecipeRequested;

        public void Render(in MachineInfoViewData data)
        {
            titleText.text = data.Title;
            statusText.text = "상태 · " + data.Status;
            recipeText.text = "레시피 · " + data.Recipe;
            inputText.text = data.Input;
            outputText.text = data.Output;
            progressText.text = data.Progress;
            portsText.text = data.Ports;
            accentBar.color = data.AccentColor;
            progressFill.color = data.AccentColor;
            progressFill.fillAmount = data.Progress01;
            recipeButton.gameObject.SetActive(data.CanSelectRecipe);
        }

        public static MachineInfoPanel CreateRuntime(Transform parent)
        {
            var root = new GameObject("MachineInfoPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);

            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(1f, 0.5f);
            rootRt.anchorMax = new Vector2(1f, 0.5f);
            rootRt.pivot = new Vector2(1f, 0.5f);
            rootRt.anchoredPosition = new Vector2(-32f, 30f);
            rootRt.sizeDelta = new Vector2(430f, 590f);
            root.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.1f, 0.96f);

            var panel = root.AddComponent<MachineInfoPanel>();
            panel.accentBar = CreateImage(root.transform, "AccentBar", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(8f, 0f));
            panel.accentBar.rectTransform.pivot = new Vector2(0f, 0.5f);
            panel.titleText = CreateText(root.transform, "Title", new Vector2(24f, -22f), new Vector2(320f, 46f), 30, FontStyle.Bold);
            panel.statusText = CreateText(root.transform, "Status", new Vector2(24f, -78f), new Vector2(370f, 34f), 20);
            panel.recipeText = CreateText(root.transform, "Recipe", new Vector2(24f, -128f), new Vector2(370f, 46f), 22, FontStyle.Bold);
            panel.inputText = CreateText(root.transform, "Input", new Vector2(24f, -188f), new Vector2(180f, 120f), 19);
            panel.outputText = CreateText(root.transform, "Output", new Vector2(220f, -188f), new Vector2(185f, 120f), 19);
            panel.progressText = CreateText(root.transform, "Progress", new Vector2(24f, -326f), new Vector2(370f, 32f), 18);
            panel.portsText = CreateText(root.transform, "Ports", new Vector2(24f, -400f), new Vector2(370f, 74f), 19);

            var progressBackground = CreateImage(root.transform, "ProgressBackground", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -365f), new Vector2(370f, 18f));
            progressBackground.color = new Color(1f, 1f, 1f, 0.12f);
            panel.progressFill = CreateImage(progressBackground.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillRt = panel.progressFill.rectTransform;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            panel.progressFill.type = Image.Type.Filled;
            panel.progressFill.fillMethod = Image.FillMethod.Horizontal;

            var closeButton = CreateButton(root.transform, "CloseButton", "닫기", new Vector2(-118f, 22f), new Vector2(94f, 54f), true);
            panel.recipeButton = CreateButton(root.transform, "RecipeButton", "레시피 변경", new Vector2(24f, 22f), new Vector2(210f, 54f), false);
            closeButton.onClick.AddListener(() => panel.CloseRequested?.Invoke());
            panel.recipeButton.onClick.AddListener(() => panel.RecipeRequested?.Invoke());

            panel.Close();
            return panel;
        }

        private static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size, bool rightAnchored)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rightAnchored ? new Vector2(1f, 0f) : Vector2.zero;
            rt.anchorMax = rt.anchorMin;
            rt.pivot = rightAnchored ? new Vector2(1f, 0f) : Vector2.zero;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.24f, 1f);

            var text = CreateText(go.transform, "Label", Vector2.zero, size, 20, FontStyle.Bold);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta = Vector2.zero;
            text.alignment = TextAnchor.MiddleCenter;
            return go.GetComponent<Button>();
        }
    }
}
