using Factory.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Seo.UI
{
    public sealed class BgmSettingsPanel : MonoBehaviour
    {
        private static BgmSettingsPanel instance;
        private static int lastClosedFrame = -1;
        private Slider volumeSlider;
        private TMP_Text volumeLabel;
        private GameObject previousSelection;
        private bool preferencesChanged;

        public static void Show()
        {
            if (instance == null)
            {
                var root = new GameObject("BgmSettingsCanvas", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                instance = root.AddComponent<BgmSettingsPanel>();
                instance.Build();
                root.SetActive(false);
            }

            if (instance.gameObject.activeSelf) return;
            instance.previousSelection = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            instance.gameObject.SetActive(true);
            var manager = BgmManager.Instance;
            instance.volumeSlider.interactable = manager != null;
            float volume = manager != null && !manager.IsMuted ? manager.MasterVolume : 0f;
            instance.volumeSlider.SetValueWithoutNotify(Mathf.RoundToInt(volume * 100f));
            instance.UpdateVolumeLabel(instance.volumeSlider.value);
            instance.volumeSlider.Select();
        }

        public void Close()
        {
            lastClosedFrame = Time.frameCount;
            gameObject.SetActive(false);
            if (EventSystem.current != null && previousSelection != null
                && previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(previousSelection);
            previousSelection = null;
        }

        public static bool TryHandleBack()
        {
            // Both this panel and the in-game HUD listen for Escape. Consume the
            // closing frame as well, regardless of which Update runs first.
            if (instance != null && instance.gameObject.activeSelf)
            {
                instance.Close();
                return true;
            }
            return lastClosedFrame == Time.frameCount;
        }

        private void Build()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = SeoUIFactory.LandscapeReference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // A full-screen raycast target prevents clicks reaching the menu or world.
            var backdrop = SeoUIFactory.CreatePanel(transform, "Backdrop", Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0.015f, 0.025f, 0.82f));
            backdrop.sprite = null;

            var panel = SeoUIFactory.CreatePanel(transform, "SettingsPanel", Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.zero, new Vector2(740f, 390f));
            var title = SeoUIFactory.CreateTMPText(panel.transform, "Title", "설정", 32,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(title.rectTransform, -290f, 132f, 140f, 52f);
            title.color = SeoUITheme.Current.Primary;

            var label = SeoUIFactory.CreateTMPText(panel.transform, "BgmLabel", "배경음악", 25);
            Place(label.rectTransform, -225f, 46f, 210f, 44f);
            volumeLabel = SeoUIFactory.CreateTMPText(panel.transform, "Volume", "", 25,
                TextAnchor.MiddleRight, FontStyle.Bold);
            Place(volumeLabel.rectTransform, 260f, 46f, 140f, 44f);

            BuildSlider(panel.transform);

            var hint = SeoUIFactory.CreateTMPText(panel.transform, "Hint",
                "변경한 볼륨은 자동으로 저장됩니다", 18, TextAnchor.MiddleCenter);
            Place(hint.rectTransform, 0f, -72f, 660f, 36f);
            hint.color = SeoUITheme.Current.Muted;

            var close = SeoUIFactory.CreateTMPButton(panel.transform, "Close", "닫기", Close);
            Place(close.GetComponent<RectTransform>(), 0f, -135f, 180f, 56f);

            // Keep keyboard/controller focus inside the modal while it is open.
            volumeSlider.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = close,
                selectOnDown = close
            };
            close.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = volumeSlider,
                selectOnDown = volumeSlider
            };
        }

        private void BuildSlider(Transform parent)
        {
            var root = new GameObject("BgmVolumeSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
            root.transform.SetParent(parent, false);
            Place(root.GetComponent<RectTransform>(), 0f, -14f, 640f, 56f);
            root.GetComponent<Image>().color = Color.clear;

            var track = SeoUIFactory.CreatePanel(root.transform, "Track", Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.zero, new Vector2(612f, 10f), SeoUITheme.Current.Secondary);
            track.sprite = null;
            track.raycastTarget = false;

            var fill = SeoUIFactory.CreatePanel(track.transform, "Fill", Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, SeoUITheme.Current.Primary);
            fill.sprite = null;
            fill.raycastTarget = false;

            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(root.transform, false);
            Place(handleArea, 0f, 0f, 612f, 56f);
            var handle = SeoUIFactory.CreatePanel(handleArea, "Handle", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(28f, 42f), Color.white);
            handle.rectTransform.pivot = Vector2.one * 0.5f;
            handle.sprite = null;

            volumeSlider = root.GetComponent<Slider>();
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 100f;
            volumeSlider.wholeNumbers = true;
            volumeSlider.direction = Slider.Direction.LeftToRight;
            volumeSlider.fillRect = fill.rectTransform;
            volumeSlider.handleRect = handle.rectTransform;
            volumeSlider.targetGraphic = handle;
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        private void SetVolume(float value)
        {
            var manager = BgmManager.Instance;
            if (manager == null) return;
            manager.SetVolume(value / 100f);
            // Moving the slider also recovers from a previously saved mute setting.
            if (manager.IsMuted) manager.SetMuted(false);
            preferencesChanged = true;
            UpdateVolumeLabel(value);
        }

        private void UpdateVolumeLabel(float value)
        {
            volumeLabel.text = value <= 0f ? "음소거" : $"{Mathf.RoundToInt(value)}%";
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                TryHandleBack();
        }

        private void OnDisable() => SavePreferences();

        private void OnApplicationPause(bool paused)
        {
            if (paused) SavePreferences();
        }

        private void SavePreferences()
        {
            if (!preferencesChanged) return;
            PlayerPrefs.Save();
            preferencesChanged = false;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            SeoUIFactory.SetRect(rect, Vector2.one * 0.5f, Vector2.one * 0.5f,
                Vector2.one * 0.5f, new Vector2(x, y), new Vector2(width, height));
        }
    }
}
