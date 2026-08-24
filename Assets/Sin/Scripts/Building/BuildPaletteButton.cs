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

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (isBeltButton)
            {
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

            machineTool.SelectMachine(machineDef);
            router.SetMode(BuildInputRouter.Mode.PlaceMachine);
        }
    }
}
