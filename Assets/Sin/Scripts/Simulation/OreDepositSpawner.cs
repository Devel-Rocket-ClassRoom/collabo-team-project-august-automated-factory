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

        // 최초 테스트용 고정 배치 몇 개(철 2군데 + 석탄, 합성기가 서로 다른 두 자원을 받는 걸
        // 확인하려고 둠). 이후 광맥은 여기 코드를 고치는 대신 OreDepositMarker를 씬에 놓는
        // 것을 권장 — 재컴파일 없이 Scene 뷰에서 바로 배치/이동 가능.
        private static readonly (Vector2Int cell, string depositId)[] FixedDeposits =
        {
            (new Vector2Int(4, 3), "IronOreDeposit"),
            (new Vector2Int(-4, 3), "CoalDeposit"),
            (new Vector2Int(-4, 5), "CopperOreDeposit"),
            (new Vector2Int(6, 4), "GoldOreDeposit"),
            (new Vector2Int(2, 6), "QuartzOreDeposit"),
            (new Vector2Int(-2, 6), "UraniumOreDeposit"),
        };

        private void Start()
        {
            if (driver == null || driver.World == null) return;

            var db = driver.World.Database;
            var grid = driver.World.Grid;

            for (int i = 0; i < FixedDeposits.Length; i++)
            {
                var (cell, depositId) = FixedDeposits[i];
                TrySpawn(cell, depositId, db, grid);
            }

            // 씬에 직접 놓은 마커(OreDepositMarker)도 등록한다 — 코드 수정/재컴파일 없이
            // Scene 뷰에서 오브젝트 옮기고 depositId만 고르면 되는 더 쉬운 배치 방법.
            var markers = FindObjectsByType<OreDepositMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                TrySpawn(markers[i].Cell, markers[i].depositId, db, grid);
            }
        }

        private void TrySpawn(Vector2Int cell, string depositId, GameDatabase db, WorldGrid grid)
        {
            if (string.IsNullOrEmpty(depositId)) return;
            if (!db.TryGetOreDepositId(depositId, out int depositRuntimeId)) return;
            if (grid.TryGetOreDeposit(cell, out _)) return; // 이미 있으면(겹치는 마커, 재실행 등) 건너뜀

            grid.RegisterOreDeposit(cell, depositRuntimeId);
            SpawnVisual(cell, db.OreDeposits[depositRuntimeId]);
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
