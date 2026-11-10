using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合台の手ごたえの UI で使う、コードで描く仮の絵（溶け具合の輪・丸）。
    /// 本物の絵は BrewFeedback2D の「絵」の欄に入れると、そちらが使われる（輪は CustomAssets/Images/UI/Brew/stir_ring.png）。
    /// </summary>
    public static class BrewUiSprites2D
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>輪（ぐるっと塗っていく溶け具合の輪）</summary>
        public static Sprite Ring()
        {
            const string key = "ring";
            if (TryGet(key, out Sprite s)) return s;
            const int size = 128;
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                    // 太さ 16 の輪（ふちはなめらかに）
                    float a = Mathf.Clamp01(Mathf.Min(r - (half - 20f), (half - 4f) - r) + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Store(key, Make(size, size, px, TextureWrapMode.Clamp), Vector4.zero);
        }

        /// <summary>まるい点（温度計の球・星の後ろの光など）</summary>
        public static Sprite Circle()
        {
            const string key = "circle";
            if (TryGet(key, out Sprite s)) return s;
            const int size = 64;
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(half - 1f - r + 0.5f));
                }
            }
            return Store(key, Make(size, size, px, TextureWrapMode.Clamp), Vector4.zero);
        }

        private static bool TryGet(string key, out Sprite sprite)
        {
            if (cache.TryGetValue(key, out sprite) && sprite != null) return true;
            sprite = null;
            return false;
        }

        private static Texture2D Make(int w, int h, Color[] px, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                name = "BrewUi",
            };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Sprite Store(string key, Texture2D tex, Vector4 border)
        {
            Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = "BrewUi_" + key;
            cache[key] = s;
            return s;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cache.Clear();
        }
    }
}
