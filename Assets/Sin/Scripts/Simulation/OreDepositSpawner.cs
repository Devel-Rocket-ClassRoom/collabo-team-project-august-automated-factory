using Factory.Building;
using Factory.Data;
using Factory.Rendering;
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
            Vector3 worldPos = GridUtility.CellToWorldCenter(cell, 0f);
            var res = driver.World.Database.Resources[deposit.ResourceId];

            var root = new GameObject($"OreDeposit_{deposit.Key}");
            root.transform.position = worldPos;

            // 폴백: 자원 색 얇은 판. Addressables 모델(SM_*_Node)이 로드되면 숨겨진다.
            // oreDepositVisualPrefab(SceneBootstrapper가 넣어주던 판 프리팹)은 이제 안 쓴다 — Addressables 우선.
            GameObject placeholder = oreDepositVisualPrefab != null
                ? Instantiate(oreDepositVisualPrefab, worldPos, Quaternion.identity, root.transform)
                : BuildVisuals.CreateBox(worldPos, new Vector3(0.95f, 0.08f, 0.95f), res.Color, root.transform, withCollider: false);
            placeholder.name = "Placeholder";
            BuildVisuals.Colorize(placeholder, res.Color);

            // 광맥 모델은 resourceId 로 조회한다(컨벤션) — AddressablesSetup 의 Deposits 표.
            root.AddComponent<AddressableModelMount>().Mount("Prefab_Deposit_" + res.Key, placeholder);
        }
    }
}
