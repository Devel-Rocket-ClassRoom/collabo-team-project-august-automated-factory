using System.Collections.Generic;
using UnityEngine;

namespace Optimization
{
    public class FloorViewportCuller : MonoBehaviour
    {
        public Camera viewCamera;
        public FloorChunkManager chunkManager;
        public float viewRadius = 45f; // Increased so generating happens OFF-SCREEN
        public float updateInterval = 0.05f; // Update much faster to catch up with camera

        private float timer = 0f;
        private HashSet<Vector2Int> currentlyVisibleChunks = new HashSet<Vector2Int>();
        private bool isCullingEnabled = false;

        public void SetCullingEnabled(bool enabled)
        {
            isCullingEnabled = enabled;
            if (!enabled)
            {
                chunkManager.UnloadAllChunks();
                currentlyVisibleChunks.Clear();
            }
            else
            {
                ForceUpdate();
            }
        }

        private void Update()
        {
            if (!isCullingEnabled) return;

            timer += Time.deltaTime;
            if (timer >= updateInterval)
            {
                timer = 0f;
                UpdateVisibleChunks();
            }
        }

        public void ForceUpdate()
        {
            if (!isCullingEnabled) return;
            UpdateVisibleChunks();
        }

        private void UpdateVisibleChunks()
        {
            if (viewCamera == null || chunkManager == null) return;

            // 1. Raycast to find where the camera is looking on the ground (Y=0)
            // If the camera is angled, the center of the screen is a better reference than the camera's raw position.
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 focusPoint = viewCamera.transform.position;
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                focusPoint = ray.GetPoint(enter);
            }

            // 2. Map offsets
            float offsetX = chunkManager.mapWidth * chunkManager.tileSize / 2f;
            float offsetZ = chunkManager.mapLength * chunkManager.tileSize / 2f;

            // 3. Focus position relative to grid origin (0,0 is bottom-left)
            float localX = focusPoint.x + offsetX;
            float localZ = focusPoint.z + offsetZ;

            // 4. Convert to chunk coordinates
            float chunkWorldSize = chunkManager.chunkSize * chunkManager.tileSize;
            int currentChunkX = Mathf.FloorToInt(localX / chunkWorldSize);
            int currentChunkZ = Mathf.FloorToInt(localZ / chunkWorldSize);

            int chunksVisibleInRadius = Mathf.CeilToInt(viewRadius / chunkWorldSize);

            HashSet<Vector2Int> newVisibleChunks = new HashSet<Vector2Int>();

            for (int x = currentChunkX - chunksVisibleInRadius; x <= currentChunkX + chunksVisibleInRadius; x++)
            {
                for (int z = currentChunkZ - chunksVisibleInRadius; z <= currentChunkZ + chunksVisibleInRadius; z++)
                {
                    Vector2Int chunkCoord = new Vector2Int(x, z);
                    
                    // Square culling (반듯한 정사각형 모양 유지)
                    newVisibleChunks.Add(chunkCoord);
                }
            }

            // 5. Unload chunks that are no longer visible
            List<Vector2Int> chunksToUnload = new List<Vector2Int>();
            foreach (var chunk in currentlyVisibleChunks)
            {
                if (!newVisibleChunks.Contains(chunk))
                {
                    chunkManager.UnloadChunk(chunk);
                    chunksToUnload.Add(chunk);
                }
            }
            foreach (var chunk in chunksToUnload)
            {
                currentlyVisibleChunks.Remove(chunk);
            }

            // 6. Load new visible chunks
            foreach (var chunk in newVisibleChunks)
            {
                if (!currentlyVisibleChunks.Contains(chunk))
                {
                    chunkManager.LoadChunk(chunk);
                    currentlyVisibleChunks.Add(chunk);
                }
            }
        }
    }
}
