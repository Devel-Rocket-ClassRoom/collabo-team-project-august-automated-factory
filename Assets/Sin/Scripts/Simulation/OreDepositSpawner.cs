using Factory.Building;
using Factory.Data;
using UnityEngine;

namespace Factory.Simulation
{
    // 게임 시작 시 미리 깔려있는 광물 노드들을 배치한다. 절차적 지형 생성은 스코프 밖이라
    // 코어 주변 고정 좌표에 몇 개만 둔다 — 채굴기는 이제 하나뿐이고, 이 노드 위에 지어야만
    // 그 노드가 정한 자원/속도/산출량을 물려받는다(MachineGhostTool.Confirm() 참고).
    public class OreDepositSpawner : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private GameObject oreDepositVisualPrefab;

        // CopperOreDeposit은 뺐다 — "CopperOre" 아이템이 아직 Bae님 데이터에 없어서(팀에서
        // 실제 아이템으로 추가하면 다시 넣으면 됨). 대신 석탄(Coal, 실제 아이템으로 이미 있음)
        // 노드를 둬서 합성기가 진짜 서로 다른 두 자원(철 주괴 + 석탄)을 받는 걸 테스트할 수 있게 함.
        private static readonly (Vector2Int cell, string depositId)[] FixedDeposits =
        {
            (new Vector2Int(4, 3), "IronOreDeposit"),
            (new Vector2Int(4, 5), "IronOreDeposit"),
            (new Vector2Int(-4, 3), "CoalDeposit"),
        };

        private void Start()
        {
            if (driver == null || driver.World == null) return;

            var db = driver.World.Database;
            var grid = driver.World.Grid;

            for (int i = 0; i < FixedDeposits.Length; i++)
            {
                var (cell, depositId) = FixedDeposits[i];
                if (!db.TryGetOreDepositId(depositId, out int depositRuntimeId)) continue;
                if (grid.TryGetOreDeposit(cell, out _)) continue; // 이미 있으면(재실행 등) 건너뜀

                grid.RegisterOreDeposit(cell, depositRuntimeId);
                SpawnVisual(cell, db.OreDeposits[depositRuntimeId]);
            }
        }

        private void SpawnVisual(Vector2Int cell, OreDepositRuntime deposit)
        {
            Vector3 worldPos = GridUtility.CellToWorldCenter(cell, 0.02f); // 바닥에 거의 붙게(지면 겹침 방지)
            GameObject go = oreDepositVisualPrefab != null
                ? Instantiate(oreDepositVisualPrefab, worldPos, Quaternion.identity)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (oreDepositVisualPrefab == null)
            {
                Destroy(go.GetComponent<Collider>());
                go.transform.position = worldPos;
                go.transform.localScale = new Vector3(0.95f, 0.05f, 0.95f);
            }

            go.name = $"OreDeposit_{deposit.Key}";
            BuildVisuals.Colorize(go, driver.World.Database.Resources[deposit.ResourceId].Color);
        }
    }
}
