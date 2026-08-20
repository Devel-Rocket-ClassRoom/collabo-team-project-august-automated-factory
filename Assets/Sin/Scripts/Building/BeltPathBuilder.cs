using System.Collections.Generic;
using UnityEngine;

namespace Factory.Building
{
    // 손가락 드래그로 들어오는 원시 셀 시퀀스를 "한 칸씩 이어지는 정직교 경로"로 바꾸는
    // 순수 로직. MonoBehaviour와 분리해서 실제 입력 없이도 유닛 테스트할 수 있게 한다.
    public static class BeltPathBuilder
    {
        // rawCells(드래그 중 지나온 원시 셀들)를 순서대로 Extend에 흘려 최종 정직교 경로를 만든다.
        // 테스트/오프라인 검증용 진입점 — 실제 드래그 툴은 프레임마다 Extend를 직접 호출한다.
        public static List<Vector2Int> BuildOrthogonalPath(IReadOnlyList<Vector2Int> rawCells)
        {
            var path = new List<Vector2Int>();
            for (int i = 0; i < rawCells.Count; i++)
            {
                Extend(path, rawCells[i]);
            }
            return path;
        }

        // path를 제자리에서 갱신한다: nextCell을 이어붙이거나(인접), 대각선으로 튀었으면
        // 우세 축 방향으로 먼저 한 칸 이어서 자동으로 코너를 만들거나, 이미 지나온 칸으로
        // 되짚어오면 그 지점까지 경로를 잘라낸다(드래그 중 취소).
        public static void Extend(List<Vector2Int> path, Vector2Int nextCell)
        {
            if (path.Count == 0)
            {
                path.Add(nextCell);
                return;
            }

            if (path[path.Count - 1] == nextCell) return; // 같은 칸에 머무름

            int backtrackIndex = path.IndexOf(nextCell);
            if (backtrackIndex >= 0)
            {
                path.RemoveRange(backtrackIndex + 1, path.Count - backtrackIndex - 1);
                return;
            }

            Vector2Int current = path[path.Count - 1];
            Vector2Int delta = nextCell - current;

            while (delta != Vector2Int.zero)
            {
                Vector2Int step = DominantAxisStep(delta);
                current += step;
                path.Add(current);
                delta -= step;
            }
        }

        private static Vector2Int DominantAxisStep(Vector2Int delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return new Vector2Int((int)Mathf.Sign(delta.x), 0);
            }
            return new Vector2Int(0, (int)Mathf.Sign(delta.y));
        }
    }
}
