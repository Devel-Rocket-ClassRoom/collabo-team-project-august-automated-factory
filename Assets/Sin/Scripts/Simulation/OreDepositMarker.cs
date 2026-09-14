using UnityEngine;

namespace Factory.Simulation
{
    // 씬에 직접 놓는 광물 노드 마커. OreDepositSpawner.cs 코드를 열어 좌표를 손으로 적어넣는
    // 대신, 이 컴포넌트를 씬에 배치하고 Scene 뷰에서 위치를 옮긴 뒤 Inspector에서 depositId만
    // 골라주면 게임 시작 시 그 칸에 자동 등록된다.
    //
    // 쓰는 법: 빈 GameObject 만들고 이 컴포넌트 추가 → Scene 뷰에서 원하는 칸 위로 드래그 →
    // depositId에 Assets/Sin/Resources/GameData/OreDeposits 안 애셋의 depositId 문자열 입력
    // (예: "CopperOreDeposit"). 우클릭 메뉴의 "칸 중앙에 맞추기"로 정확히 칸 중앙에 스냅 가능.
    public class OreDepositMarker : MonoBehaviour
    {
        [Tooltip("Assets/Sin/Resources/GameData/OreDeposits 안 OreDepositDef 애셋의 depositId와 정확히 같은 문자열.")]
        public string depositId;

        public Vector2Int Cell => GridUtility.WorldToCell(transform.position);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 center = GridUtility.CellToWorldCenter(Cell, 0.05f);
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(center, new Vector3(0.95f, 0.1f, 0.95f));
            UnityEditor.Handles.Label(center + Vector3.up * 0.4f,
                string.IsNullOrEmpty(depositId) ? "(depositId 비어있음)" : depositId);
        }

        [ContextMenu("칸 중앙에 맞추기")]
        private void SnapToCell()
        {
            transform.position = GridUtility.CellToWorldCenter(Cell, 0f);
        }
#endif
    }
}
