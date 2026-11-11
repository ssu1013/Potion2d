using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 絵の取り込み設定を自動で決める（Unity を開くだけでよく、Inspector を触る必要はない）。
    ///   ・はじめて取り込む画像（Assets/CustomAssets/Images・Assets/Art2D）：スプライト・100 ピクセル = 1 ワールド単位・ミップマップなし
    ///   ・差し替え用の画像（Assets/CustomAssets/Images）：同じ名前の PNG で上書きしたとき、元と大きさ（ピクセル）が違っても、
    ///     ゲームの中での大きさ・9スライスのふちの太さが変わらないように、ピクセル数の比で自動で合わせる。
    ///     （元の大きさは .meta の userData に「potion2d:design=幅x高さ;ppu=…;border=…」として覚えている。今ある絵には書き込み済み。
    ///     新しい名前で足した絵には無いので、そのときはふつうの取り込み（100 ピクセル = 1 単位）になる）
    /// </summary>
    public class Art2DImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/Art2D/";
        public const string CustomRoot = "Assets/CustomAssets/Images/";
        private const string Tag = "potion2d:";

        private void OnPreprocessTexture()
        {
            bool custom = assetPath.StartsWith(CustomRoot);
            if (!custom && !assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.importSettingsMissing)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 4096;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
            }
            if (custom) KeepDesignSize(importer);
        }

        /// <summary>上書きした絵の大きさが元と違えば、ゲームの中での大きさがそろうように ppu・ふちを合わせる</summary>
        private void KeepDesignSize(TextureImporter importer)
        {
            if (!TryParse(importer.userData, out Design d)) return;
            if (!TryReadPngSize(assetPath, out int width, out int height) || width <= 0) return;
            float k = width / (float)d.width;
            float ppu = d.ppu * k;
            Vector4 border = d.border * k;
            border = new Vector4(Mathf.Round(border.x), Mathf.Round(border.y), Mathf.Round(border.z), Mathf.Round(border.w));
            if (Mathf.Abs(importer.spritePixelsPerUnit - ppu) > 0.01f) importer.spritePixelsPerUnit = ppu;
            if (importer.spriteBorder != border) importer.spriteBorder = border;
            // 大きく描いた絵が縮んでぼやけないように、取り込める最大の大きさも合わせる
            int longest = Mathf.Max(width, height);
            if (importer.maxTextureSize < longest) importer.maxTextureSize = Mathf.Min(8192, Mathf.NextPowerOfTwo(longest));
        }

        // ------------------------------------------------------------------

        public struct Design
        {
            public int width, height;
            public float ppu;
            public Vector4 border;
        }

        public static string Format(int width, int height, float ppu, Vector4 border)
        {
            var c = CultureInfo.InvariantCulture;
            return $"{Tag}design={width}x{height};ppu={ppu.ToString("0.###", c)};border={border.x.ToString(c)},{border.y.ToString(c)},{border.z.ToString(c)},{border.w.ToString(c)}";
        }

        public static bool TryParse(string userData, out Design d)
        {
            d = default;
            if (string.IsNullOrEmpty(userData) || !userData.StartsWith(Tag)) return false;
            var c = CultureInfo.InvariantCulture;
            foreach (string part in userData.Substring(Tag.Length).Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "design":
                        string[] wh = kv[1].Split('x');
                        if (wh.Length == 2)
                        {
                            int.TryParse(wh[0], NumberStyles.Integer, c, out d.width);
                            int.TryParse(wh[1], NumberStyles.Integer, c, out d.height);
                        }
                        break;
                    case "ppu":
                        float.TryParse(kv[1], NumberStyles.Float, c, out d.ppu);
                        break;
                    case "border":
                        string[] b = kv[1].Split(',');
                        if (b.Length == 4)
                        {
                            float.TryParse(b[0], NumberStyles.Float, c, out float x);
                            float.TryParse(b[1], NumberStyles.Float, c, out float y);
                            float.TryParse(b[2], NumberStyles.Float, c, out float z);
                            float.TryParse(b[3], NumberStyles.Float, c, out float w);
                            d.border = new Vector4(x, y, z, w);
                        }
                        break;
                }
            }
            return d.width > 0 && d.height > 0 && d.ppu > 0f;
        }

        /// <summary>PNG の大きさをファイルの頭から読む（取り込む前に知るため）</summary>
        public static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    var head = new byte[24];
                    if (fs.Read(head, 0, 24) < 24) return false;
                    if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47) return false; // PNG でない
                    width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                    height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
