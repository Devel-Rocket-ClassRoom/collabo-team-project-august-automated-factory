using System.Collections.Generic;
using UnityEngine;

namespace Factory.Building
{
    // 에셋 없는 프로토타입용 런타임 시각 헬퍼. 건설 도구들이 프리뷰/확정 지오메트리를
    // 만들 때 공용으로 쓴다. 색상별로 머티리얼을 캐싱해서 재사용한다 — 안 그러면 고스트를
    // 매 프레임 다시 칠할 때마다(Colorize) 새 Material을 계속 만들어내게 된다.
    public static class BuildVisuals
    {
        private static readonly Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();

        public static GameObject CreateStrip(Vector3 from, Vector3 to, float thickness, Color color, Transform parent, bool withCollider = false, GameObject prefab = null)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (!withCollider) Object.Destroy(go.GetComponent<Collider>());
            }

            Vector3 mid = (from + to) * 0.5f;
            float length = Mathf.Max(Vector3.Distance(from, to), 0.001f);
            Quaternion rotation = Quaternion.LookRotation(to - from);

            go.transform.SetParent(parent, true);
            go.transform.position = mid;
            go.transform.rotation = rotation;
            go.transform.localScale = new Vector3(thickness, thickness * 0.4f, length);

            Colorize(go, color);

            // 방향 화살표는 스트립(go)의 자식으로 붙이되, 월드 좌표/스케일을 먼저 확정한 뒤
            // worldPositionStays=true로 재부모화한다 — 스트립은 (thickness, thickness*0.4,
            // length)로 비균일 스케일돼 있어서 그냥 자식으로 붙이면 화살표가 길이에 따라
            // 늘어나거나 찌그러지는데, 이렇게 하면 유니티가 로컬 스케일을 알아서 보정해줘서
            // 항상 일정한 절대 크기로 보이면서도 스트립이 파괴될 때 같이 정리된다.
            AttachDirectionArrow(mid + Vector3.up * (thickness * 0.4f * 0.5f + 0.03f), rotation, go.transform);

            return go;
        }

        private static Mesh beltArrowMesh;

        private static void AttachDirectionArrow(Vector3 position, Quaternion rotation, Transform stripTransform)
        {
            var arrowGO = new GameObject("DirectionArrow", typeof(MeshFilter), typeof(MeshRenderer));
            arrowGO.transform.position = position;
            arrowGO.transform.rotation = rotation;
            arrowGO.transform.localScale = Vector3.one * 0.6f;
            arrowGO.transform.SetParent(stripTransform, true);

            if (beltArrowMesh == null)
            {
                beltArrowMesh = new Mesh { name = "BeltDirectionArrow" };
                var vertices = new[]
                {
                    new Vector3(0f, 0f, 0.34f),
                    new Vector3(-0.16f, 0f, 0.06f),
                    new Vector3(0.16f, 0f, 0.06f),
                };
                var triangles = new[] { 0, 1, 2, 0, 2, 1 };
                var normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                beltArrowMesh.vertices = vertices;
                beltArrowMesh.triangles = triangles;
                beltArrowMesh.normals = normals;
                beltArrowMesh.RecalculateBounds();
            }

            arrowGO.GetComponent<MeshFilter>().sharedMesh = beltArrowMesh;
            Colorize(arrowGO, Color.black);
        }

        public static GameObject CreateBox(Vector3 position, Vector3 scale, Color color, Transform parent, bool withCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!withCollider) Object.Destroy(go.GetComponent<Collider>());

            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = scale;

            Colorize(go, color);
            return go;
        }

        public static void Colorize(GameObject go, Color color) => Colorize(go.GetComponent<Renderer>(), color);

        // GetComponent 없이 이미 갖고 있는 Renderer 참조로 바로 칠한다 — 매 프레임 여러 번
        // 호출되는 곳(예: BeltItemRenderer)에서 GetComponent 비용을 반복하지 않기 위함.
        public static void Colorize(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.sharedMaterial = GetOrCreateMaterial(color);
        }

        // 텍스처를 타일링해서 까는 전용 머티리얼 (바닥 격자 등). 색상 캐시 대상이 아니다 —
        // 텍스처마다 보통 하나씩만 쓰이므로 굳이 캐싱할 필요가 없다.
        public static Material CreateTiledMaterial(Texture2D texture, Vector2 tiling)
        {
            var mat = new Material(FindDefaultShader());
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            return mat;
        }

        private static Shader FindDefaultShader() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        private static Material GetOrCreateMaterial(Color color)
        {
            // "죽은 참조" 방어: Enter Play Mode Options에서 도메인 리로드를 꺼두면(흔한 성능
            // 설정), 이 static 캐시는 플레이 세션 사이에도 안 비워진다. 그런데 여기 든
            // Material은 Play 모드에서 만들어진 거라 Play를 멈추면 실제로는 파괴된다 — 그러면
            // 캐시엔 "파괴된 오브젝트를 가리키는" 참조만 남는데, 유니티의 오버로드된 ==
            // 덕분에 그 값은 여전히 null과 같다고 비교되지만 캐시 안에는 그대로 남아있어서,
            // 다음 플레이 세션에서 재사용하면 렌더러에 죽은 머티리얼이 배정돼 에러도 없이
            // 조용히 안 보이게 된다(실제로 겪은 버그: 벨트 위 아이템이 아무 표시 없이 사라짐).
            // cached != null로 죽은 참조를 걸러내고 다시 만들어서 스스로 복구되게 한다.
            if (materialCache.TryGetValue(color, out var cached) && cached != null) return cached;

            var mat = new Material(FindDefaultShader()) { color = color };

            if (color.a < 1f)
            {
                // URP Lit을 스크립트로 Transparent 서페이스로 전환할 때 필요한 최소 설정.
                mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                mat.SetFloat("_Blend", 0f); // Alpha blend
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            materialCache[color] = mat;
            return mat;
        }
    }
}
