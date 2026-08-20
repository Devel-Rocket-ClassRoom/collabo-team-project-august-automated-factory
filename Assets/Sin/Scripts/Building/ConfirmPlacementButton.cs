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

            // 기계를 놓은 다음 자연스러운 다음 동작은 벨트로 연결하는 것이라, 모드를 자동으로
            // 벨트로 넘겨준다 — 안 그러면 라우터가 계속 "기계 배치" 모드에 멈춰있어서, 다시
            // "벨트" 버튼을 누르지 않는 한 드래그해도 아무 반응이 없다.
            if (router != null) router.SetMode(BuildInputRouter.Mode.Belt);
        }
    }
}
