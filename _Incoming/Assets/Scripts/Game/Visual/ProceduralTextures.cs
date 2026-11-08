using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 手描き風のテクスチャをコードで作る（木の床板・石壁・木材・窓の空）。
    /// 本物のテクスチャが用意できたら、マテリアルの Texture を差し替えるだけでよい。
    /// </summary>
    public static class ProceduralTextures
    {
        /// <summary>横向きの木の板（床・棚・机）</summary>
        public static Texture2D WoodPlanks(int size, Color baseColor, int plankCount, int seed)
        {
            var random = new System.Random(seed);
            var tex = NewTexture(size, size, "WoodPlanks");
            var pixels = new Color32[size * size];

            var tones = new float[plankCount + 1];
            var offsets = new float[plankCount + 1];
            for (int i = 0; i < tones.Length; i++)
            {
                tones[i] = 0.82f + (float)random.NextDouble() * 0.3f;
                offsets[i] = (float)random.NextDouble() * 100f;
            }

            for (int y = 0; y < size; y++)
            {
                // 割り算の余りが出ても継ぎ目がずれないよう、行の範囲を整数で割り振る
                int plank = y * plankCount / size;
                int plankStart = plank * size / plankCount;
                int plankHeight = (plank + 1) * size / plankCount - plankStart;
                int yIn = y - plankStart;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)yIn / plankHeight;
                    float grain = Mathf.Sin((u * 9f + Mathf.Sin(u * 13f + offsets[plank]) * 0.35f + v * 1.5f) * Mathf.PI * 2f);
                    float grain2 = Mathf.PerlinNoise(u * 24f + offsets[plank], v * 3f);
                    float shade = tones[plank] * (0.92f + grain * 0.04f + (grain2 - 0.5f) * 0.12f);

                    // 板の継ぎ目（暗い線）と、板の上端の明るい縁
                    if (yIn < 2) shade *= 0.35f;
                    else if (yIn < 4) shade *= 1.12f;

                    // 板の縦の継ぎ目（板ごとに位置をずらす）
                    int seam = (int)(offsets[plank] * 7f) % size;
                    if (Mathf.Abs(x - seam) < 2) shade *= 0.4f;

                    pixels[y * size + x] = (Color32)Scale(baseColor, shade);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>石積みの壁</summary>
        public static Texture2D StoneBricks(int size, Color baseColor, int seed)
        {
            var random = new System.Random(seed);
            var tex = NewTexture(size, size, "StoneBricks");
            var pixels = new Color32[size * size];
            const int rows = 5;
            const int columns = 3;
            const int mortar = 3;

            var tones = new float[rows * (columns + 1)];
            for (int i = 0; i < tones.Length; i++) tones[i] = 0.72f + (float)random.NextDouble() * 0.28f;

            for (int y = 0; y < size; y++)
            {
                int row = y * rows / size;
                int rowStart = row * size / rows;
                int brickH = (row + 1) * size / rows - rowStart;
                int yIn = y - rowStart;
                int shift = (row % 2 == 0) ? 0 : size / columns / 2;
                for (int x = 0; x < size; x++)
                {
                    int xs = (x + shift) % size;
                    int col = xs * columns / size;
                    int colStart = col * size / columns;
                    int brickW = (col + 1) * size / columns - colStart;
                    int xIn = xs - colStart;
                    float shade = tones[row * (columns + 1) + col];

                    float n = Mathf.PerlinNoise(x * 0.09f + row * 3.1f, y * 0.09f + col * 7.3f);
                    shade *= 0.9f + (n - 0.5f) * 0.25f;

                    int edge = Mathf.Min(Mathf.Min(xIn, brickW - 1 - xIn), Mathf.Min(yIn, brickH - 1 - yIn));
                    if (edge < mortar) shade = 0.32f;                 // 目地
                    else if (edge < mortar + 3)                       // 角を少し暗く（立体感）
                    {
                        bool topOrLeft = (yIn > brickH / 2) || (xIn < brickW / 2);
                        shade *= topOrLeft ? 1.08f : 0.8f;
                    }

                    pixels[y * size + x] = (Color32)Scale(baseColor, shade);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>窓の外の空と木々（ぼかした縦グラデーション）</summary>
        public static Texture2D WindowSky(int width, int height)
        {
            var tex = NewTexture(width, height, "WindowSky");
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            var skyTop = new Color(0.55f, 0.78f, 0.95f);
            var skyBottom = new Color(0.92f, 0.95f, 0.85f);
            var tree = new Color(0.36f, 0.62f, 0.38f);
            var treeDark = new Color(0.25f, 0.48f, 0.3f);

            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / width;
                    Color c = Color.Lerp(skyBottom, skyTop, v);
                    float treeLine = 0.35f + Mathf.PerlinNoise(u * 6f, 0.5f) * 0.3f;
                    if (v < treeLine)
                    {
                        float n = Mathf.PerlinNoise(u * 18f, v * 18f);
                        c = Color.Lerp(treeDark, tree, n);
                    }
                    pixels[y * width + x] = (Color32)c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------
        // 調合の粒（湯気・泡・しぶき・粉）。白で描き、色は粒ごとに付ける
        // ------------------------------------------------------------------

        /// <summary>
        /// 湯気・煙のかたまり（2×2 のコマ。粒ごとに違う形を選ぶ）。
        /// ふちがやわらかく、中にむらのある、もやもやした形
        /// </summary>
        public static Texture2D SteamPuffs(int cellSize, int seed)
        {
            int size = cellSize * 2;
            var tex = NewTexture(size, size, "SteamPuffs");
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            var random = new System.Random(seed);
            for (int cell = 0; cell < 4; cell++)
            {
                int ox = (cell % 2) * cellSize;
                int oy = (cell / 2) * cellSize;
                float sx = (float)random.NextDouble() * 50f;
                float sy = (float)random.NextDouble() * 50f;
                // 少しいびつな丸（中心をずらしたかたまりを3つ重ねる）
                var blobs = new Vector3[3];
                for (int b = 0; b < blobs.Length; b++)
                {
                    blobs[b] = new Vector3(((float)random.NextDouble() - 0.5f) * 0.28f, ((float)random.NextDouble() - 0.5f) * 0.28f, 0.24f + (float)random.NextDouble() * 0.12f);
                }
                for (int y = 0; y < cellSize; y++)
                {
                    for (int x = 0; x < cellSize; x++)
                    {
                        float u = (x + 0.5f) / cellSize - 0.5f;
                        float v = (y + 0.5f) / cellSize - 0.5f;
                        float shape = 0f;
                        foreach (Vector3 b in blobs)
                        {
                            float d = Mathf.Sqrt((u - b.x) * (u - b.x) + (v - b.y) * (v - b.y)) / b.z;
                            shape = Mathf.Max(shape, Mathf.Clamp01(1f - d));
                        }
                        float n = Mathf.PerlinNoise(sx + u * 5f, sy + v * 5f) * 0.6f + Mathf.PerlinNoise(sx * 2f + u * 13f, sy * 2f + v * 13f) * 0.4f;
                        float a = Mathf.SmoothStep(0f, 1f, shape) * Mathf.Lerp(0.45f, 1f, n);
                        // 外側 4 画素は必ず透明（コマの境目がにじまないように）
                        int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(cellSize - 1 - x, cellSize - 1 - y));
                        if (edge < 4) a = 0f;
                        pixels[(oy + y) * size + ox + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>泡（透明な丸に、明るい縁と光の点）</summary>
        public static Texture2D Bubble(int size)
        {
            var tex = NewTexture(size, size, "Bubble");
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.14f);
                    float inside = d < 0.82f ? 0.12f : 0f;
                    float hx = u + 0.32f, hy = v - 0.34f;
                    float glint = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.2f);
                    float a = Mathf.Clamp01(ring * 0.9f + inside + glint);
                    if (d > 0.97f) a = 0f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>しぶき・粉の粒（ふちのはっきりした丸）</summary>
        public static Texture2D Droplet(int size)
        {
            var tex = NewTexture(size, size, "Droplet");
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01((0.92f - d) / 0.12f);
                    // 左上を少し明るく（つや）
                    float hx = u + 0.3f, hy = v - 0.3f;
                    float light = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.45f);
                    byte c = (byte)(Mathf.Lerp(0.82f, 1f, light) * 255f);
                    pixels[y * size + x] = new Color32(c, c, c, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
        }

        private static Color Scale(Color c, float k)
        {
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
        }
    }
}
