using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 기계 배치 중 방향(Facing)을 90도씩 돌린다. 3D 화살표 표식이 화면에서 잘 안 보일 수
    // 있어서(특히 배치 전 고스트 단계), 현재 방향을 글자로도 같이 보여준다.
    public class RotatePlacementButton : MonoBehaviour
    {
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private Button button;
        [SerializeField] private Text facingLabel;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => machineTool.RotateFacing());
        }

        private void Update()
        {
            if (facingLabel == null || machineTool == null) return;
            // 채굴기는 입출력 포트가 없어서(원격 전송) 방향 표시가 의미 없다 — 제련로 등
            // 포트가 있는 기계를 놓을 때만 보여준다.
            facingLabel.text = machineTool.IsPlacing && machineTool.CurrentMachineUsesPorts
                ? $"출력 방향\n{FacingArrow(machineTool.CurrentFacing)}"
                : string.Empty;
        }

        private static string FacingArrow(Vector2Int facing)
        {
            if (facing == new Vector2Int(1, 0)) return "▶ 동";
            if (facing == new Vector2Int(-1, 0)) return "◀ 서";
            if (facing == new Vector2Int(0, 1)) return "▲ 북";
            if (facing == new Vector2Int(0, -1)) return "▼ 남";
            return "?";
        }
    }
}
