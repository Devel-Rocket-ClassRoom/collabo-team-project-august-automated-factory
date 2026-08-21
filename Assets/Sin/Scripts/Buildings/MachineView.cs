using Factory.Simulation;
using Factory.UI;
using UnityEngine;

namespace Factory.Buildings
{
    public enum MachineInstanceKind
    {
        Miner,
        Processor,
    }

    // 그리드에 배치된 기계 하나의 얇은 뷰 셸. 실제 상태는 SimulationWorld의 배열이 갖고 있고,
    // 이 컴포넌트는 그 인스턴스를 씬 오브젝트/탭 입력과 연결하는 역할만 한다.
    public class MachineView : MonoBehaviour, IInteractable
    {
        [SerializeField] private MachineInstanceKind kind;
        [SerializeField] private int instanceIndex;
        [SerializeField] private SimulationDriver driver;

        public void Initialize(MachineInstanceKind kind, int instanceIndex, SimulationDriver driver)
        {
            this.kind = kind;
            this.instanceIndex = instanceIndex;
            this.driver = driver;
        }

        public void OnTap()
        {
            if (driver == null || driver.World == null) return;

            switch (kind)
            {
                case MachineInstanceKind.Miner:
                    var miner = driver.World.Miners[instanceIndex];
                    Debug.Log($"[MachineView] Miner buffered output: {miner.BufferedOutput}");
                    break;
                case MachineInstanceKind.Processor:
                    var processor = driver.World.Processors[instanceIndex];
                    // 코어(UniversalPorts)는 레시피 개념이 없는 순수 저장소라 선택 UI를 안 연다.
                    if (processor.UniversalPorts)
                    {
                        Debug.Log($"[MachineView] Core storage — buffer 합계는 InputBuffer 참고");
                        break;
                    }
                    var category = driver.World.Database.Machines[processor.MachineId].Category;
                    RecipeSelectionPanel.Instance?.Open(instanceIndex, category);
                    break;
            }
        }
    }
}
