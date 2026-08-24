using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 기계 고스트 배치를 확정한다. 터치 릴리즈만으로 바로 놓이지 않도록 별도 버튼을 거치게 해서
    // 모바일 오조작(잘못 짚은 채로 손을 뗐을 때 원치 않는 배치)을 줄인다.
    public class ConfirmPlacementButton : MonoBehaviour
    {
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (!machineTool.Confirm()) return;

            // 벨트 버튼이 따로 있는데 기계를 놓자마자 벨트 모드로 자동 전환되면 오히려
            // 헷갈린다는 피드백에 따라, 확정 후엔 아무 모드도 아닌 상태(None)로 돌아간다 —
            // None이면 한 손가락 드래그가 카메라 팬으로 동작하니 놀고 있는 상태도 아니다.
            if (router != null) router.SetMode(BuildInputRouter.Mode.None);
        }
    }
}
