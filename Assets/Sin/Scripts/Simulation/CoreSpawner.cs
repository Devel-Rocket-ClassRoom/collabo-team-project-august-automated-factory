using Factory.Buildings;
using UnityEngine;

namespace Factory.Simulation
{
    // 게임 시작 시 미리 놓여있는 중심 저장 거점("코어")을 만든다. 플레이어가 짓는 게 아니라
    // 처음부터 존재하는 마인더스트리류 코어 개념. 레시피를 배정하지 않은 ProcessorInstance라
    // 아무 자원이나 받아서 버퍼에 쌓아두기만 하고 아무것도 생산하지 않는다 — 별도 인스턴스
    // 타입 없이 기존 ProcessorInstance를 그대로 재사용하고, UniversalPorts만 켠다(4면 입출력).
    // 2x2 칸을 차지하며, 앵커를 (-1,-1)로 둬서 블록 중심이 정확히 월드 원점에 오게 한다
    // (카메라 초기 프레이밍이 원점 중심이라 자연스럽게 화면 가운데 놓임).
    public class CoreSpawner : MonoBehaviour
    {
        private static readonly Vector2Int Anchor = new Vector2Int(-1, -1);
        private static readonly Vector2Int Footprint = new Vector2Int(2, 2);

        [SerializeField] private SimulationDriver driver;
        [SerializeField] private GameObject corePrefab;

        private void Start()
        {
            if (driver == null || driver.World == null) return;

            var db = driver.World.Database;
            if (!db.TryGetMachineId("Core", out int machineId)) return;

            var grid = driver.World.Grid;
            var cells = GridUtility.GetFootprintCells(Anchor, Footprint);
            for (int i = 0; i < cells.Count; i++)
            {
                if (grid.IsOccupied(cells[i])) return; // 이미 있으면(재실행 등) 건너뜀
            }

            var core = new ProcessorInstance(db.ResourceCount) { MachineId = machineId, RecipeId = -1, UniversalPorts = true };
            int index = driver.World.AddProcessor(core);
            driver.World.CoreProcessorIndex = index; // 채굴기 원격 전송(MinerSystem)이 참조하는 대상
            grid.RegisterBuildingFootprint(cells, CellOccupantType.Processor, index);

            Vector3 worldPos = GridUtility.GetFootprintCenter(Anchor, Footprint, 0.75f);
            var go = corePrefab != null ? Instantiate(corePrefab, worldPos, Quaternion.identity) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Core";
            if (corePrefab == null) go.transform.position = worldPos;

            var view = go.GetComponent<MachineView>() ?? go.AddComponent<MachineView>();
            view.Initialize(MachineInstanceKind.Processor, index, driver);
        }
    }
}
