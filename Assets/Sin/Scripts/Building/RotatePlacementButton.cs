using UnityEngine;
using UnityEngine.UI;

namespace Factory.Building
{
    // 기계 배치 중 방향(Facing)을 90도씩 돌린다. 방향 표시는 화면 글자가 아니라 기계 위에
    // 영구적으로 붙어있는 작은 화살표(OutputArrow, PrefabBuilder 참고) 하나로만 보여준다.
    public class RotatePlacementButton : MonoBehaviour
    {
        [SerializeField] private MachineGhostTool machineTool;
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => machineTool.RotateFacing());
        }
    }
}
