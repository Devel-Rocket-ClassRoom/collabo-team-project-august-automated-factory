using UnityEngine;
using UnityEngine.UI;

namespace Choi.SaveLoad
{
    /// <summary>발전기/전선/송신탑 배치와 공장 전체 SAVE/LOAD를 확인하는 런타임 패널입니다.</summary>
    public sealed class FactoryPowerPanel : MonoBehaviour
    {
        private PowerSaveManager saveManager;
        private PowerGridSystem powerGrid;
        private PowerBuildController powerBuild;
        private Text statusText;
        private string saveMessage = "저장 준비됨";

        private void Start()
        {
            saveManager = GetComponent<PowerSaveManager>();
            powerGrid = GetComponent<PowerGridSystem>();
            powerBuild = GetComponent<PowerBuildController>();
            BuildPanel();
        }

        private void Update()
        {
            if (statusText == null || powerGrid == null || powerBuild == null) return;
            statusText.text =
                $"발전 {powerGrid.AvailablePower} / 사용 {powerGrid.UsedPower} / 요구 {powerGrid.RequestedPower}\n" +
                $"가동 기계 {powerGrid.PoweredMachineCount}/{powerGrid.TotalMachineCount} · 작동 송신탑 {powerGrid.ActiveTowerCount}\n" +
                $"모드: {ModeLabel(powerBuild.Mode)} · {powerBuild.LastMessage}\n{saveMessage}";
        }

        private void BuildPanel()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[FactoryPowerPanel] HUD Canvas를 찾지 못했습니다.");
                return;
            }

            Transform existing = canvas.transform.Find("FactoryPowerPanel");
            if (existing != null) Destroy(existing.gameObject);

            var panel = new GameObject("FactoryPowerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-18f, -18f);
            panelRect.sizeDelta = new Vector2(360f, 390f);
            panel.GetComponent<Image>().color = new Color(0.035f, 0.07f, 0.11f, 0.94f);

            CreateText(panel.transform, "Title", "POWER & FACTORY SAVE", new Vector2(0f, -15f), new Vector2(330f, 35f), 23);
            statusText = CreateText(panel.transform, "Status", string.Empty, new Vector2(0f, -52f), new Vector2(330f, 100f), 17);

            CreateButton(panel.transform, "GeneratorButton", "발전기 배치", new Vector2(-88f, -160f),
                () => powerBuild.SetMode(PowerBuildMode.Generator));
            CreateButton(panel.transform, "CableButton", "전선 배치", new Vector2(88f, -160f),
                () => powerBuild.SetMode(PowerBuildMode.Cable));
            CreateButton(panel.transform, "TowerButton", "송신탑 배치", new Vector2(-88f, -208f),
                () => powerBuild.SetMode(PowerBuildMode.TransmissionTower));
            CreateButton(panel.transform, "RemovePowerButton", "전력 철거", new Vector2(88f, -208f),
                () => powerBuild.SetMode(PowerBuildMode.Remove));
            CreateButton(panel.transform, "CancelPowerButton", "배치 종료", new Vector2(0f, -256f),
                () => powerBuild.SetMode(PowerBuildMode.None));
            CreateButton(panel.transform, "SaveFactoryButton", "SAVE", new Vector2(-88f, -316f), SaveFactory);
            CreateButton(panel.transform, "LoadFactoryButton", "LOAD", new Vector2(88f, -316f), LoadFactory);
        }

        private void SaveFactory()
        {
            if (saveManager == null) return;
            saveManager.Save();
            saveMessage = "공장 전체 SAVE 완료";
        }

        private void LoadFactory()
        {
            if (saveManager == null) return;
            saveMessage = saveManager.Load() ? "공장 전체 LOAD 완료" : "저장 파일이 없습니다";
        }

        private static string ModeLabel(PowerBuildMode mode)
        {
            switch (mode)
            {
                case PowerBuildMode.Generator: return "발전기";
                case PowerBuildMode.Cable: return "전선";
                case PowerBuildMode.TransmissionTower: return "송신탑";
                case PowerBuildMode.Remove: return "전력 철거";
                default: return "없음";
            }
        }

        private static void CreateButton(Transform parent, string name, string label, Vector2 position,
            UnityEngine.Events.UnityAction action)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(160f, 40f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.11f, 0.43f, 0.68f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            Text text = CreateText(buttonObject.transform, "Label", label, Vector2.zero, Vector2.zero, 18);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }
    }
}
