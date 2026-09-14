using System.Collections.Generic;
using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Rendering
{
    // 시뮬레이션 데이터(BeltSegment.Items)를 읽어 풀링된 오브젝트로 화면에 그린다.
    // 시뮬레이션 배열 자체는 건드리지 않으므로 틱 루프의 GC Alloc 0 유지에 영향을 주지 않는다.
    //
    // 자원별로 실제 모델(Addressables, ResourceRuntime.PrefabName)이 있으면 그걸 얹고, 없는
    // 자원(아직 매핑 안 된 것)은 예전처럼 구+색 폴백을 쓴다. 풀 슬롯은 재사용되며 프레임마다
    // 다른 자원을 표시할 수 있으니, 슬롯에 물린 자원이 바뀔 때만 모델을 다시 얹는다(매 프레임
    // Addressables 로드/파괴를 반복하지 않도록).
    public class BeltItemRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private int segmentId;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private GameObject itemVisualPrefab; // 자원별 모델이 없을 때의 폴백(구) 모양
        // 코너 세그먼트일 때만 값 있음 — 꺾이는 지점. 이게 없으면(직선) start->end 직선 이동.
        [SerializeField] private Transform bendPoint;

        private sealed class Slot
        {
            public Transform Root;
            public int ResourceId = -1;
            public Renderer FallbackRenderer;
            public bool HasModel; // true면 실제 자원 모델을 얹은 상태 — 폴백은 로드될 때까지의 임시 표시일 뿐, 매 프레임 다시 칠할 필요 없음
        }

        private readonly List<Slot> pool = new List<Slot>();
        private BeltSegment segment;

        // 런타임에 벨트를 놓는 건설 도구가 에디터 SerializedObject 없이 직접 배선할 때 쓴다.
        public void Initialize(SimulationDriver driver, int segmentId, Transform startPoint, Transform endPoint, GameObject itemVisualPrefab = null, Transform bendPoint = null)
        {
            this.driver = driver;
            this.segmentId = segmentId;
            this.startPoint = startPoint;
            this.endPoint = endPoint;
            if (itemVisualPrefab != null) this.itemVisualPrefab = itemVisualPrefab;
            this.bendPoint = bendPoint;
            segment = null;
        }

        private void LateUpdate()
        {
            if (segment == null)
            {
                segment = FindSegment();
                if (segment == null) return;
            }

            var database = driver.World.Database;
            EnsurePoolSize(segment.Items.Count);

            for (int i = 0; i < segment.Items.Count; i++)
            {
                var item = segment.Items[i];
                float t = segment.Length <= 0f ? 0f : item.Position / segment.Length;
                var slot = pool[i];

                Vector3 position;
                Vector3 direction;
                if (bendPoint != null)
                {
                    // 코너: start/bend/end를 2차 베지어 곡선의 세 점으로 써서 부드럽게 돈다.
                    // (직선 두 개를 이어 붙이면 꺾이는 지점이 뾰족한 직각이 돼서 안 좋음 —
                    // 베지어는 양 끝(t=0, t=1)에서 각각 직선 구간과 접선이 자연스럽게 이어진다.)
                    Vector3 p0 = startPoint.position, p1 = bendPoint.position, p2 = endPoint.position;
                    float u = 1f - t;
                    position = u * u * p0 + 2f * u * t * p1 + t * t * p2;
                    direction = 2f * u * (p1 - p0) + 2f * t * (p2 - p1); // 곡선의 접선(진행 방향)
                }
                else
                {
                    position = Vector3.Lerp(startPoint.position, endPoint.position, t);
                    direction = endPoint.position - startPoint.position;
                }

                slot.Root.position = position;
                // 진행 방향으로 회전시킨다 — 안 그러면(특히 실제 자원 모델이 얹힌 뒤로는) 방향
                // 없이 원래 배리언트 자세 그대로 고정돼서 벨트를 따라가는 느낌이 안 난다.
                if (direction.sqrMagnitude > 0.0001f) slot.Root.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                slot.Root.gameObject.SetActive(true);

                if (slot.ResourceId != item.ResourceId) MountResource(slot, item.ResourceId, database);
                // 폴백(구)을 쓰는 슬롯만 매 프레임 다시 칠한다 — 실제 모델은 자기 고유 재질을
                // 그대로 쓰고 색으로 구분할 필요가 없다.
                if (!slot.HasModel) BuildVisuals.Colorize(slot.FallbackRenderer, database.Resources[item.ResourceId].Color);
            }

            for (int i = segment.Items.Count; i < pool.Count; i++)
            {
                pool[i].Root.gameObject.SetActive(false);
            }
        }

        private BeltSegment FindSegment()
        {
            if (driver == null || driver.World == null) return null;
            var segments = driver.World.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveSegment 참고).
                if (segments[i].Id == segmentId) return segments[i];
            }
            return null;
        }

        private void EnsurePoolSize(int count)
        {
            while (pool.Count < count)
            {
                var root = new GameObject("BeltItem").transform;
                root.SetParent(transform, false);
                pool.Add(new Slot { Root = root });
            }
        }

        // 슬롯에 표시할 자원이 바뀌었을 때만 호출된다. 기존 표시물을 지우고, 그 자원에 매핑된
        // Addressables 모델이 있으면 얹고(로드 전/실패 시엔 폴백 유지), 없으면 폴백만 남긴다.
        private void MountResource(Slot slot, int resourceId, GameDatabase database)
        {
            for (int i = slot.Root.childCount - 1; i >= 0; i--) Destroy(slot.Root.GetChild(i).gameObject);
            slot.ResourceId = resourceId;
            slot.HasModel = false;

            GameObject fallback = itemVisualPrefab != null ? Instantiate(itemVisualPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallback.transform.SetParent(slot.Root, false);
            if (itemVisualPrefab == null)
            {
                fallback.transform.localScale = Vector3.one * 0.25f;
                var collider = fallback.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
            slot.FallbackRenderer = fallback.GetComponent<Renderer>();

            string prefabName = database.Resources[resourceId].PrefabName;
            if (!string.IsNullOrEmpty(prefabName))
            {
                slot.HasModel = true;
                var mount = new GameObject("Model");
                mount.transform.SetParent(slot.Root, false);
                // alignToGround: false — 슬롯 루트가 이미 벨트 위 원하는 높이를 매 프레임 그대로
                // 따라가므로, 기계처럼 "지면(y=0)까지 내리는" 보정을 하면 오히려 파묻힌다.
                mount.AddComponent<AddressableModelMount>().Mount(prefabName, fallback, alignToGround: false);
            }
        }
    }
}
