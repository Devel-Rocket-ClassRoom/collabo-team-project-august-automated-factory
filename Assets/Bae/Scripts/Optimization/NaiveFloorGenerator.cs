using UnityEngine;

namespace Optimization
{
    public class NaiveFloorGenerator : MonoBehaviour
    {
        [Header("최적화 적용 전 (Before) 테스트용")]
        public GameObject floorPrefab;
        public int mapWidth = 120;
        public int mapLength = 120;
        public float tileSize = 1f;

        private void Start()
        {
            if (floorPrefab == null || floorPrefab.name.Contains("Manager"))
            {
                Debug.LogWarning("바닥 프리팹이 비어있거나 매니저가 할당되었습니다. 임시로 기본 Quad를 사용합니다.");
                floorPrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                floorPrefab.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            StartCoroutine(GenerateSlowly());
        }

        private System.Collections.IEnumerator GenerateSlowly()
        {
            float offsetX = mapWidth * tileSize / 2f;
            float offsetZ = mapLength * tileSize / 2f;
            Vector3 globalOffset = new Vector3(0.5f, 0f, 0.5f);
            
            int count = 0;

            for (int x = 0; x < mapWidth; x++)
            {
                for (int z = 0; z < mapLength; z++)
                {
                    Vector3 pos = new Vector3(x * tileSize - offsetX, 0, z * tileSize - offsetZ) + globalOffset;
                    Instantiate(floorPrefab, pos, Quaternion.identity, transform);
                    
                    count++;
                    // 500개 생성마다 1프레임 대기 (에디터 뻗음 방지)
                    if (count >= 500)
                    {
                        count = 0;
                        yield return null;
                    }
                }
            }
            
            Debug.Log($"무식한 방식(Naive)으로 {mapWidth * mapLength}개의 바닥 생성 완료! 이제 프로파일러 캡처하세요.");
        }
    }
}
