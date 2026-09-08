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
        private Text powerText;
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
            powerText.text = data.Power;
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
            rootRt.anchoredPosition = new Vector2(-28f, 12f);
            rootRt.sizeDelta = new Vector2(520f, 620f);
            SeoUIFactory.ApplyPanel(root.GetComponent<Image>());

            var panel = root.AddComponent<MachineInfoPanel>();
            panel.accentBar = CreateImage(root.transform, "AccentBar", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(7f, 0f));
            panel.accentBar.rectTransform.pivot = new Vector2(0f, 0.5f);
            panel.titleText = CreateText(root.transform, "Title", new Vector2(26f, -20f), new Vector2(330f, 44f), 30, FontStyle.Bold);

            var statusCard = CreateImage(root.transform, "StatusCard", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(150f, 40f));
            statusCard.rectTransform.pivot = new Vector2(1f, 1f);
            statusCard.color = new Color(0f, 0.46f, 0.58f, 0.72f);
            panel.statusText = CreateText(statusCard.transform, "Status", Vector2.zero, Vector2.zero, 18, FontStyle.Bold);
            panel.statusText.rectTransform.anchorMin = Vector2.zero;
            panel.statusText.rectTransform.anchorMax = Vector2.one;
            panel.statusText.rectTransform.offsetMin = new Vector2(8f, 2f);
            panel.statusText.rectTransform.offsetMax = new Vector2(-8f, -2f);
            panel.statusText.alignment = TextAnchor.MiddleCenter;

            panel.powerText = CreateText(root.transform, "Power", new Vector2(26f, -66f), new Vector2(330f, 28f), 18, FontStyle.Bold);
            panel.powerText.color = SeoUITheme.Current.Warning;
            panel.recipeText = CreateText(root.transform, "Recipe", new Vector2(26f, -98f), new Vector2(460f, 44f), 22, FontStyle.Bold);

            var inputCard = CreateImage(root.transform, "InputCard", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -152f), new Vector2(222f, 142f));
            inputCard.color = new Color(0.02f, 0.16f, 0.24f, 0.94f);
            panel.inputText = CreateText(inputCard.transform, "Input", new Vector2(16f, -14f), new Vector2(190f, 112f), 19);

            var outputCard = CreateImage(root.transform, "OutputCard", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(268f, -152f), new Vector2(226f, 142f));
            outputCard.color = new Color(0.2f, 0.1f, 0.03f, 0.94f);
            panel.outputText = CreateText(outputCard.transform, "Output", new Vector2(16f, -14f), new Vector2(194f, 112f), 19);

            panel.progressText = CreateText(root.transform, "Progress", new Vector2(24f, -310f), new Vector2(470f, 32f), 18, FontStyle.Bold);
            panel.portsText = CreateText(root.transform, "Ports", new Vector2(24f, -390f), new Vector2(470f, 70f), 18);

            var progressBackground = CreateImage(root.transform, "ProgressBackground", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -350f), new Vector2(470f, 16f));
            progressBackground.color = new Color(1f, 1f, 1f, 0.12f);
            panel.progressFill = CreateImage(progressBackground.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillRt = panel.progressFill.rectTransform;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            panel.progressFill.type = Image.Type.Filled;
            panel.progressFill.fillMethod = Image.FillMethod.Horizontal;

            // 기존 HUD의 회전/확정 버튼과 시각적으로 섞이지 않도록 패널 내부에 독립된
            // 하단 액션 영역을 두고, 기능별 색상과 충분한 버튼 간격을 적용한다.
            var actionFooter = CreateImage(
                root.transform,
                "ActionFooter",
                Vector2.zero,
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, 100f));
            actionFooter.color = new Color(0.035f, 0.045f, 0.06f, 0.98f);

            var footerLine = CreateImage(
                actionFooter.transform,
                "TopLine",
                new Vector2(0f, 1f),
                Vector2.one,
                Vector2.zero,
                new Vector2(0f, 2f));
            footerLine.color = new Color(1f, 1f, 1f, 0.14f);

            panel.recipeButton = CreateButton(
                actionFooter.transform,
                "RecipeButton",
                "레시피 설정",
                new Vector2(24f, 20f),
                new Vector2(310f, 58f),
                false,
                new Color(0.1f, 0.38f, 0.62f, 1f));
            var closeButton = CreateButton(
                actionFooter.transform,
                "CloseButton",
                "닫기",
                new Vector2(-24f, 20f),
                new Vector2(132f, 58f),
                true,
                new Color(0.38f, 0.16f, 0.18f, 1f));
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

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size,
            bool rightAnchored,
            Color backgroundColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rightAnchored ? new Vector2(1f, 0f) : Vector2.zero;
            rt.anchorMax = rt.anchorMin;
            rt.pivot = rightAnchored ? new Vector2(1f, 0f) : Vector2.zero;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = backgroundColor;

            var text = CreateText(go.transform, "Label", Vector2.zero, size, 20, FontStyle.Bold);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta = Vector2.zero;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            var button = go.GetComponent<Button>();
            SeoUIFactory.ApplyButton(button, backgroundColor);
            return button;
        }
    }
}
