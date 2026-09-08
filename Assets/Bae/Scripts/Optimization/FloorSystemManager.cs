using UnityEngine;

namespace Optimization
{
    [RequireComponent(typeof(FloorObjectPool))]
    [RequireComponent(typeof(FloorChunkManager))]
    [RequireComponent(typeof(FloorViewportCuller))]
    public class FloorSystemManager : MonoBehaviour
    {
        [Header("Required Setup")]
        [Tooltip("반복해서 생성할 바닥 프리팹을 넣어주세요.")]
        public GameObject floorPrefab;
        [Tooltip("추적할 메인 카메라 (비워두면 Camera.main을 자동 탐색합니다)")]
        public Camera targetCamera;

        [Header("Grid Settings (그리드 설정)")]
        public float tileSize = 1f;
        public int chunkSize = 16;
        public int mapWidth = 2500;
        public int mapLength = 2500;
        public Vector3 globalOffset = new Vector3(0.5f, 0f, 0.5f);

        [Header("Performance Settings (모바일 최적화)")]
        public float viewRadius = 45f;
        public float updateInterval = 0.05f;
        [Tooltip("1프레임당 그릴 바닥의 개수입니다. 수치를 올리면 렉이 발생하지만 렌더링이 빨라집니다.")]
        public int tilesPerFrame = 150;

        private FloorObjectPool pool;
        private FloorChunkManager chunkManager;
        private FloorViewportCuller viewportCuller;

        private void Start()
        {
            if (floorPrefab == null)
            {
                Debug.LogError("[FloorSystemManager] 바닥 프리팹(floorPrefab)이 할당되지 않았습니다!");
                return;
            }

            pool = GetComponent<FloorObjectPool>();
            chunkManager = GetComponent<FloorChunkManager>();
            viewportCuller = GetComponent<FloorViewportCuller>();

            // Auto-find camera
            if (targetCamera == null) targetCamera = Camera.main;

            // Sync settings to components
            chunkManager.tileSize = tileSize;
            chunkManager.chunkSize = chunkSize;
            chunkManager.mapWidth = mapWidth;
            chunkManager.mapLength = mapLength;
            chunkManager.globalOffset = globalOffset;
            chunkManager.tilesPerFrame = tilesPerFrame;

            viewportCuller.viewCamera = targetCamera;
            viewportCuller.chunkManager = chunkManager;
            viewportCuller.viewRadius = viewRadius;
            viewportCuller.updateInterval = updateInterval;

            // Initialize Pool intelligently based on radius and chunk size
            int visibleChunks = Mathf.CeilToInt(viewRadius / (chunkSize * tileSize)) * 2;
            int estimatedVisibleCells = (visibleChunks * visibleChunks) * (chunkSize * chunkSize);
            pool.Initialize(floorPrefab, Mathf.Min(estimatedVisibleCells, 10000));

            // Start the system
            chunkManager.Initialize(pool);
            viewportCuller.SetCullingEnabled(true);
        }
    }
}
