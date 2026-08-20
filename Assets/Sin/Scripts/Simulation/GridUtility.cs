using UnityEngine;

namespace Factory.Simulation
{
    // XZ 평면 위 셀 좌표 <-> 월드 좌표 변환. 셀 크기 1, WorldGrid의 청크 좌표 규약과 동일한 기준을 쓴다.
    public static class GridUtility
    {
        public const float CellSize = 1f;

        // 화면 위쪽(지평선 근처)을 클릭하면 카메라 광선이 바닥 평면과 거의 평행해져서
        // 교차 거리가 수백~수천 유닛으로 튀는 수치 불안정 문제가 생긴다. 이 거리보다 먼
        // 교차점은 건설 입력으로 쓰지 않는다 (카메라 프레이밍상 정상적인 클릭이면 이 안에 들어옴).
        public const float MaxBuildRaycastDistance = 60f;

        public static Vector2Int WorldToCell(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPosition.x / CellSize),
                Mathf.FloorToInt(worldPosition.z / CellSize));
        }

        // 건설 입력용 공용 레이캐스트: 평면과 교차하더라도 너무 멀면(지평선 근처 클릭 등) 무효 처리한다.
        public static bool TryRaycastToCell(Ray ray, Plane groundPlane, out Vector2Int cell)
        {
            cell = default;
            if (!groundPlane.Raycast(ray, out float distance)) return false;
            if (distance > MaxBuildRaycastDistance) return false;

            cell = WorldToCell(ray.GetPoint(distance));
            return true;
        }

        public static Vector3 CellToWorldCenter(Vector2Int cell, float y = 0f)
        {
            return new Vector3(
                (cell.x + 0.5f) * CellSize,
                y,
                (cell.y + 0.5f) * CellSize);
        }
    }
}
