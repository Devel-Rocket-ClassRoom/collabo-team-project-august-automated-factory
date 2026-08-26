using Factory.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 팔레트 버튼 1개 = 기계 하나 선택(고스트 배치 모드) 또는 벨트 모드 전환.
    // 벨트 버튼인지는 machineDef가 비어있는지가 아니라 isBeltButton으로 명시적으로 구분한다 —
    // 예전엔 machineDef==null이면 그냥 벨트 모드로 넘어갔는데, 그러면 데이터 시딩을 깜빡해서
    // 기계 버튼에 MachineDef가 안 붙은 경우(예: 구리채굴기)에도 조용히 "벨트"처럼 동작해서
    // 원인을 알기 어려웠다.
    public class BuildPaletteButton : MonoBehaviour
    {
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private MachineDef machineDef;
        [SerializeField] private bool isBeltButton;
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
        [SerializeField] private Color selectedColor = new Color(0.25f, 0.75f, 0.35f, 0.95f);

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
            if (background == null) background = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (router == null) return;
            router.ModeChanged += HandleModeChanged;
            HandleModeChanged(router.CurrentMode); // 이 오브젝트가 나중에 활성화된 경우에도 현재 상태를 바로 반영.
        }

        private void OnDisable()
        {
            if (router != null) router.ModeChanged -= HandleModeChanged;
        }

        // 지금 이 버튼이 대표하는 도구/기계가 실제로 선택돼 있는지 색으로 보여준다 — 안 그러면
        // (실제로 겪은 문제) 벨트 모드가 계속 켜져 있는데도 겉보기엔 아무 표시가 없어서 왜
        // 기계 탭이 안 먹히는지 알 길이 없다.
        private void HandleModeChanged(BuildInputRouter.Mode mode)
        {
            if (background == null) return;

            bool selected = isBeltButton
                ? mode == BuildInputRouter.Mode.Belt
                : mode == BuildInputRouter.Mode.PlaceMachine && machineDef != null && machineTool.SelectedMachine == machineDef;

            background.color = selected ? selectedColor : normalColor;
        }

        private void HandleClick()
        {
            if (isBeltButton)
            {
                // 이미 벨트 모드면 다시 눌러서 해제(None으로) — 벨트 모드는 배치처럼 확정
                // 버튼으로 자동 해제되는 계기가 없어서, 안 그러면 한 번 벨트를 고르고 나면
                // 도구를 계속 쥔 채로 남아 기계를 눌러도(레시피 확인 등) 항상 빌드 제스처로만
                // 먹혀서 다시는 기계를 탭할 수 없게 된다.
                if (router.CurrentMode == BuildInputRouter.Mode.Belt)
                {
                    router.SetMode(BuildInputRouter.Mode.None);
                    return;
                }

                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.Belt);
                return;
            }

            if (machineDef == null)
            {
                Debug.LogError($"[BuildPaletteButton] '{name}'에 MachineDef가 연결되어 있지 않습니다 — " +
                    "Tools > Factory Prototype > Seed Sample Game Data 실행 후 Build Tech Tree Scene을 다시 실행하세요.");
                return;
            }

            // 이미 이 기계를 배치 모드로 고른 상태면 다시 눌러서 해제 — 벨트 버튼과 동일한 이유.
            if (router.CurrentMode == BuildInputRouter.Mode.PlaceMachine && machineTool.SelectedMachine == machineDef)
            {
                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.None);
                return;
            }

            machineTool.SelectMachine(machineDef);
            router.SetMode(BuildInputRouter.Mode.PlaceMachine);
        }
    }
}
