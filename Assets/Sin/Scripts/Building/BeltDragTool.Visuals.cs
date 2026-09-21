using System.Collections.Generic;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 순수 지오메트리 계산과 실제 스트립/코너 오브젝트 생성 — 배선 데이터(BeltSegment 등)는
    // 안 건드리고, 주어진 좌표로 무엇을 만들지만 담당한다.
    public partial class BeltDragTool
    {
        // 세그먼트 하나가 정확히 자기 칸 안(경계~반대쪽 경계)에 들어차도록 진입/이탈 지점을
        // 계산한다. 진입/이탈 방향이 다르면(코너) 꺾이는 지점(bend)을 반환해서 두 조각으로 나눠 그린다.
        private static void ComputeCellSpan(List<Vector2Int> path, int k, out Vector3 entry, out Vector3 exit, out Vector3? bend)
        {
            Vector3 center = GridUtility.CellToWorldCenter(path[k], 0f); // 벨트는 바닥에 붙는다(납작 Quad)
            Vector3? inDir = k > 0 ? DirWorld(path[k] - path[k - 1]) : (Vector3?)null;
            Vector3? outDir = k < path.Count - 1 ? DirWorld(path[k + 1] - path[k]) : (Vector3?)null;

            if (inDir.HasValue && outDir.HasValue && inDir.Value != outDir.Value)
            {
                entry = center - inDir.Value * 0.5f;
                exit = center + outDir.Value * 0.5f;
                bend = center;
                return;
            }

            Vector3 dir = inDir ?? outDir ?? Vector3.forward;
            entry = center - dir * 0.5f;
            exit = center + dir * 0.5f;
            bend = null;
        }

        private static Vector3 DirWorld(Vector2Int step) => new Vector3(step.x, 0f, step.y);

        private void SpawnCommittedVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId)
        {
            var root = new GameObject($"Belt_{segmentId}");

            // 아이템이 벨트 면(beltSurfaceY) 위에 얹혀 굴러가는 것처럼 반지름만큼 띄운다.
            Vector3 itemHeightOffset = Vector3.up * (beltSurfaceY + 0.01f + itemVisualRadius);

            var startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(root.transform);
            startAnchor.position = from + itemHeightOffset;

            var endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(root.transform);
            endAnchor.position = to + itemHeightOffset;

            // 코너면 꺾이는 지점도 앵커로 만들어 BeltItemRenderer에 넘긴다 — 안 그러면 아이템이
            // start->end를 그냥 직선(대각선)으로 가로질러서, 코너를 안 따라가고 벽을 뚫는 것처럼
            // 보인다. 진입 절반/이탈 절반 두 구간으로 나눠 가게 하려면 이 중간점이 필요하다.
            Transform bendAnchor = null;
            if (bend.HasValue)
            {
                bendAnchor = new GameObject("Bend").transform;
                bendAnchor.SetParent(root.transform);
                bendAnchor.position = bend.Value + itemHeightOffset;

                if (cornerPrefab != null)
                {
                    SpawnBeltCorner(bend.Value, bend.Value - from, to - bend.Value, committedColor, root.transform, keepMaterial: true);
                }
                else
                {
                    // 폴백: 진입 절반 + 이탈 절반 두 조각으로 나눠 그려 칸 전체를 덮는다.
                    BuildVisuals.CreateStrip(from, bend.Value, committedThickness, committedColor, root.transform, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: beltSurfaceY);
                    BuildVisuals.CreateStrip(bend.Value, to, committedThickness, committedColor, root.transform, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: beltSurfaceY);
                }
            }
            else
            {
                BuildVisuals.CreateStrip(from, to, committedThickness, committedColor, root.transform, prefab: stripPrefab, keepPrefabMaterial: true, flatSurfaceY: beltSurfaceY);
            }

            var itemRenderer = root.AddComponent<BeltItemRenderer>();
            itemRenderer.Initialize(driver, segmentId, startAnchor, endAnchor, itemVisualPrefab, bendAnchor);
        }
        // 여기부터
        // 세이브 로드도 최초 배치와 완전히 같은 프리팹/높이/아이템 렌더러 경로를 사용한다.
        public void SpawnRestoredVisual(Vector3 from, Vector3 to, Vector3? bend, int segmentId)
        {
            SpawnCommittedVisual(from, to, bend, segmentId);
        }
        //여기까지
        // 코너 칸 중심에 코너 프리팹을 놓고 Y축으로만 돌린다(프리팹의 눕힌 자세는 유지).
        //
        // 우회전 프리팹 기준: 진입 = 아래(-Z) 변에서 위로(+Z) 들어와, 오른쪽(+X) 변으로 나간다.
        //  - 실제 진입 흐름방향(fin)에 프리팹 로컬 +Z 를 맞추면(yaw) 네 방향 우회전이 다 커버됨.
        //  - 좌회전은 거울상이라 회전만으론 화살표가 뒤집힘 → 좌회전 전용 프리팹(cornerLeftPrefab)을 쓴다.
        //    없으면 대칭 타일로 보고 우회전 프리팹을 두 변 사잇방향에 맞춰 돌린다(화살표 없는 코너용 폴백).
        private GameObject SpawnBeltCorner(Vector3 cellCenter, Vector3 dIn, Vector3 dOut, Color tint, Transform parent, bool keepMaterial)
        {
            Vector3 fin = new Vector3(dIn.x, 0f, dIn.z).normalized;
            Vector3 fout = new Vector3(dOut.x, 0f, dOut.z).normalized;
            bool leftTurn = Vector3.Cross(fin, fout).y < 0f;

            GameObject prefab = (leftTurn && cornerLeftPrefab != null) ? cornerLeftPrefab : cornerPrefab;
            var go = Instantiate(prefab, parent);
            go.transform.position = new Vector3(cellCenter.x, beltSurfaceY, cellCenter.z);

            // 이음새를 덮게 코너를 살짝 키운다(Quad 평면 축 = 로컬 X/Y, Z 는 노멀이라 유지).
            Vector3 baseScale = go.transform.localScale;
            go.transform.localScale = new Vector3(baseScale.x * cornerScale, baseScale.y * cornerScale, baseScale.z);

            float yaw;
            if (leftTurn && cornerLeftPrefab == null)
            {
                // 폴백: 대칭 타일 가정 — 두 변 사잇방향(우회전 프리팹은 135°)에 맞춤.
                Vector3 bisector = fout - fin;
                yaw = Mathf.Atan2(bisector.x, bisector.z) * Mathf.Rad2Deg - 135f;
            }
            else
            {
                // 진입 흐름방향(fin)에 로컬 +Z 를 맞춘다. 우/좌 전용 프리팹 둘 다 이 규칙.
                yaw = Mathf.Atan2(fin.x, fin.z) * Mathf.Rad2Deg;
            }
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;

            if (!keepMaterial) BuildVisuals.TintPreserveShape(go, tint);
            return go;
        }
    }
}
