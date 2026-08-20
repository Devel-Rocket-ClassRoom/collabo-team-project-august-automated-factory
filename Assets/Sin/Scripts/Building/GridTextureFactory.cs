using UnityEngine;

namespace Factory.Building
{
    // 바닥에 깔 격자선 텍스처를 코드로 직접 만든다 (외부 이미지 파일 없이).
    // 타일 왼쪽/아래 가장자리에만 선을 그려두면, 타일링(반복)했을 때 모든 경계에
    // 자연스럽게 선이 생긴다 (이웃 타일의 왼쪽 선 = 이 타일의 오른쪽 경계선).
    public static class GridTextureFactory
    {
        public static Texture2D CreateGridLineTexture(int size, int lineThickness, Color fillColor, Color lineColor)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "ProceduralGridLine",
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x < lineThickness || y < lineThickness;
                    texture.SetPixel(x, y, onLine ? lineColor : fillColor);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
