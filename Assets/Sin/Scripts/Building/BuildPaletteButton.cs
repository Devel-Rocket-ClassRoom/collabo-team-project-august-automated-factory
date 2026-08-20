using Factory.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 팔레트 버튼 1개 = 기계 하나 선택(고스트 배치 모드) 또는 벨트 모드 전환(machineDef가 비어있으면 벨트).
    public class BuildPaletteButton : MonoBehaviour
    {
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private MachineDef machineDef;
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (machineDef != null)
            {
                machineTool.SelectMachine(machineDef);
                router.SetMode(BuildInputRouter.Mode.PlaceMachine);
            }
            else
            {
                machineTool.CancelPlacement();
                router.SetMode(BuildInputRouter.Mode.Belt);
            }
        }
    }
}
