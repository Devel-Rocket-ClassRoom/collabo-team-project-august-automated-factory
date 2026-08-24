using System.Collections.Generic;
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

        // anchor(최소 모서리 칸) 기준 footprint(가로x세로, 칸 단위) 전체가 차지하는 칸 목록.
        public static List<Vector2Int> GetFootprintCells(Vector2Int anchor, Vector2Int footprint)
        {
            var cells = new List<Vector2Int>(footprint.x * footprint.y);
            for (int dx = 0; dx < footprint.x; dx++)
            {
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    cells.Add(new Vector2Int(anchor.x + dx, anchor.y + dy));
                }
            }
            return cells;
        }

        public static Vector3 GetFootprintCenter(Vector2Int anchor, Vector2Int footprint, float y = 0f)
        {
            return new Vector3(
                anchor.x * CellSize + footprint.x * CellSize * 0.5f,
                y,
                anchor.y * CellSize + footprint.y * CellSize * 0.5f);
        }

        // anchor+footprint가 차지하는 블록 기준으로, Facing 방향의 "앞면"(출력) 또는
        // 그 반대인 "뒷면"(입력)에 해당하는 칸 목록을 반환한다. footprint가 1x1이면 항상
        // 기존 단일 셀(cell±Facing)과 정확히 같은 결과(원소 1개)를 낸다 — 회귀 없음의 근거.
        // 정사각형이 아닌 footprint를 90도 회전해서 놓는 경우(가로세로 스왑)는 다루지 않는다
        // (지금 쓰는 footprint는 전부 정사각형 — 1x1 또는 2x2).
        public static List<Vector2Int> GetPortCells(Vector2Int anchor, Vector2Int footprint, Vector2Int facing, bool isOutputSide)
        {
            int sign = isOutputSide ? 1 : -1;
            var cells = new List<Vector2Int>();

            if (facing.x != 0)
            {
                int x = facing.x > 0
                    ? (sign > 0 ? anchor.x + footprint.x : anchor.x - 1)
                    : (sign > 0 ? anchor.x - 1 : anchor.x + footprint.x);
                for (int dy = 0; dy < footprint.y; dy++)
                {
                    cells.Add(new Vector2Int(x, anchor.y + dy));
                }
            }
            else if (facing.y != 0)
            {
                int y = facing.y > 0
                    ? (sign > 0 ? anchor.y + footprint.y : anchor.y - 1)
                    : (sign > 0 ? anchor.y - 1 : anchor.y + footprint.y);
                for (int dx = 0; dx < footprint.x; dx++)
                {
                    cells.Add(new Vector2Int(anchor.x + dx, y));
                }
            }

            return cells;
        }
    }
}
