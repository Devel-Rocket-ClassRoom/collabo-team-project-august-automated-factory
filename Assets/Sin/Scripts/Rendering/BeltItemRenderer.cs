using System.Collections.Generic;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Rendering
{
    // 시뮬레이션 데이터(BeltSegment.Items)를 읽어 풀링된 오브젝트로 화면에 그린다.
    // 시뮬레이션 배열 자체는 건드리지 않으므로 틱 루프의 GC Alloc 0 유지에 영향을 주지 않는다.
    public class BeltItemRenderer : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private int segmentId;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private GameObject itemVisualPrefab;

        private readonly List<Transform> pool = new List<Transform>();
        private readonly List<Renderer> poolRenderers = new List<Renderer>();
        private BeltSegment segment;

        // 런타임에 벨트를 놓는 건설 도구가 에디터 SerializedObject 없이 직접 배선할 때 쓴다.
        public void Initialize(SimulationDriver driver, int segmentId, Transform startPoint, Transform endPoint, GameObject itemVisualPrefab = null)
        {
            this.driver = driver;
            this.segmentId = segmentId;
            this.startPoint = startPoint;
            this.endPoint = endPoint;
            if (itemVisualPrefab != null) this.itemVisualPrefab = itemVisualPrefab;
            segment = null;
        }

        private void LateUpdate()
        {
            if (segment == null)
            {
                segment = FindSegment();
                if (segment == null) return;
            }

            EnsurePoolSize(segment.Items.Count);

            var database = driver.World.Database;
            for (int i = 0; i < segment.Items.Count; i++)
            {
                var item = segment.Items[i];
                float t = segment.Length <= 0f ? 0f : item.Position / segment.Length;
                pool[i].position = Vector3.Lerp(startPoint.position, endPoint.position, t);
                pool[i].gameObject.SetActive(true);
                // 자원별 프리팹은 없고(에셋 없는 프로토타입) 대신 색으로 구분한다 — 풀 슬롯이
                // 이전엔 다른 자원을 표시했을 수 있으니 매 프레임 다시 칠한다. Renderer는
                // EnsurePoolSize에서 이미 캐시해뒀으므로 매 프레임 GetComponent를 반복하지 않는다.
                BuildVisuals.Colorize(poolRenderers[i], database.Resources[item.ResourceId].Color);
            }

            for (int i = segment.Items.Count; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
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
                GameObject visual = itemVisualPrefab != null
                    ? Instantiate(itemVisualPrefab, transform)
                    : GameObject.CreatePrimitive(PrimitiveType.Sphere);

                if (itemVisualPrefab == null)
                {
                    visual.transform.SetParent(transform);
                    visual.transform.localScale = Vector3.one * 0.25f;

                    var collider = visual.GetComponent<Collider>();
                    if (collider != null) Object.Destroy(collider);
                }
                pool.Add(visual.transform);
                poolRenderers.Add(visual.GetComponent<Renderer>());
            }
        }
    }
}
