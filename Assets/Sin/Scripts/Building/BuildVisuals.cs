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

            go.transform.SetParent(parent, true);
            go.transform.position = mid;
            go.transform.rotation = Quaternion.LookRotation(to - from);
            go.transform.localScale = new Vector3(thickness, thickness * 0.4f, length);

            Colorize(go, color);
            return go;
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

        public static void Colorize(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
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
            if (materialCache.TryGetValue(color, out var cached)) return cached;

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
