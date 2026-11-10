using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step12：タイトル画面の1枚絵（後ろの調合机を隠す、タイトル専用の背景）の仮の絵を作って、UISkin につなぐ
    /// （何度実行してもよい。すでにあるファイル・入っている欄には触らない）。
    ///   Assets/CustomAssets/Images/UI/Title/title_background.png … 画面いっぱいの1枚絵（1920×1080）
    /// 自分のイラストに替えるときは、同じ名前の PNG で上書きするだけでよい（このメニューを実行する必要はない）。
    /// </summary>
    public static class Step12TitleScreen2D
    {
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";

        [MenuItem("Tools/Potion Frontier 2D/Step12：タイトルの1枚絵（仮の絵を作ってつなぐ）", priority = 22)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step12", "再生を止めてから実行してください。", "OK");
                return;
            }
            var log = new StringBuilder();
            string path = CustomAssetPaths.UITitle + "/title_background.png";
            bool isNew = !File.Exists(path);
            Sprite background = P.Ensure(CustomAssetPaths.UITitle, "title_background", 1920, 1080, BackgroundArt);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && !Art2DImportRules.TryParse(ti.userData, out _) &&
                Art2DImportRules.TryReadPngSize(path, out int pw, out int ph))
            {
                // 元の大きさを覚える（大きく描き直して上書きしても、画面いっぱいに出る）。にじまないように圧縮しない
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.userData = Art2DImportRules.Format(pw, ph, ti.spritePixelsPerUnit, ti.spriteBorder);
                ti.SaveAndReimport();
                background = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            log.AppendLine($"・絵：{(isNew ? "作った" : "そのまま")}（{path}）");

            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin == null) log.AppendLine("！ " + SkinPath + " がない");
            else if (skin.titleBackground == null && background != null)
            {
                skin.titleBackground = background;
                EditorUtility.SetDirty(skin);
                log.AppendLine("・UISkin の「タイトル画面」欄につないだ（タイトルの間は、後ろの調合机・素材トレイが見えなくなる）");
            }
            else log.AppendLine("・UISkin の「タイトル画面」欄は、もうつながっている");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Step7CustomAssets2D.Report(log);
            Debug.Log("[Step12] タイトルの1枚絵\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step12.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step12：タイトルの1枚絵", log.ToString(), "OK");
        }

        /// <summary>
        /// 仮の絵：月夜の丘。上は深い藍色の空と星、左上に月、下に重なる丘のシルエットと、灯りのともる小さな工房。
        /// ロゴ（上の真ん中）とボタン（真ん中から下）が読みやすいよう、真ん中は静かな色にしてある
        /// </summary>
        private static Color BackgroundArt(float x, float y)
        {
            // 空：上は深い藍、地平線に向かって少し明るく、ほんのり暖かく
            float v = Mathf.InverseLerp(-260f, 540f, y);
            Color sky = Color.Lerp(new Color(0.16f, 0.11f, 0.16f, 1f), new Color(0.03f, 0.04f, 0.1f, 1f), Mathf.Pow(v, 0.8f));

            // 月（左上）と、そのまわりのぼんやりした光
            const float mx = -600f, my = 445f, mr = 52f;
            float md = Mathf.Sqrt((x - mx) * (x - mx) + (y - my) * (y - my));
            float halo = Mathf.Clamp01(1f - md / 420f);
            sky += new Color(0.1f, 0.11f, 0.16f, 0f) * (halo * halo);
            float moon = Mathf.Clamp01(mr + 0.5f - md);
            float crater = Mathf.Clamp01(1f - Mathf.Sqrt((x - mx - 16f) * (x - mx - 16f) + (y - my - 12f) * (y - my - 12f)) / 18f) * 0.08f;
            sky = Color.Lerp(sky, new Color(1f, 0.96f, 0.84f, 1f) - new Color(crater, crater, crater, 0f), moon);

            // 星
            float cx = Mathf.Floor(x / 44f), cy = Mathf.Floor(y / 44f);
            float h = Mathf.Abs(Mathf.Sin(cx * 127.1f + cy * 311.7f) * 43758.5453f);
            h -= Mathf.Floor(h);
            if (h > 0.9f && y > -120f && md > mr + 20f)
            {
                float h2 = Mathf.Abs(Mathf.Sin(cx * 269.5f + cy * 183.3f) * 24634.63f);
                h2 -= Mathf.Floor(h2);
                float sx = (cx + 0.2f + 0.6f * h2) * 44f, sy = (cy + 0.2f + 0.6f * (1f - h2)) * 44f;
                float d = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                float star = Mathf.Clamp01(1.7f - d) * (0.3f + 0.6f * (h - 0.9f) / 0.1f) * Mathf.Clamp01((y + 120f) / 360f);
                sky += new Color(0.85f, 0.88f, 1f, 0f) * star;
            }

            // 遠くの丘・近くの丘（シルエット）
            float far = -250f + 55f * Mathf.Sin(x * 0.0042f + 1.1f) + 24f * Mathf.Sin(x * 0.011f + 0.4f);
            float near = -360f + 48f * Mathf.Sin(x * 0.0058f + 2.6f) + 18f * Mathf.Sin(x * 0.017f + 1.3f);
            Color farColor = new Color(0.075f, 0.065f, 0.11f, 1f) + new Color(0.03f, 0.02f, 0.02f, 0f) * Mathf.Clamp01((y + 420f) / 200f);
            Color c = Color.Lerp(sky, farColor, Mathf.Clamp01(far - y + 0.5f));

            // 近くの丘の上の、小さな工房（右寄り。窓に灯り）
            const float hx = 560f;
            float baseY = -360f + 48f * Mathf.Sin(hx * 0.0058f + 2.6f) + 18f * Mathf.Sin(hx * 0.017f + 1.3f);
            bool house = Mathf.Abs(x - hx) < 70f && y < baseY + 80f && y > baseY - 20f;
            bool roof = y >= baseY + 80f && y < baseY + 80f + (90f - Mathf.Abs(x - hx)) * 0.62f && Mathf.Abs(x - hx) < 90f;
            bool chimney = x > hx + 30f && x < hx + 50f && y > baseY + 80f && y < baseY + 150f;
            Color silhouette = new Color(0.03f, 0.025f, 0.04f, 1f);
            float nearK = house || roof || chimney ? 1f : Mathf.Clamp01(near - y + 0.5f);
            c = Color.Lerp(c, silhouette, nearK);

            // 窓の灯りと、まわりのにじみ
            float wx = hx - 18f, wy = baseY + 40f;
            bool window = Mathf.Abs(x - wx) < 15f && Mathf.Abs(y - wy) < 17f;
            if (window) c = Mathf.Abs(x - wx) < 1.5f || Mathf.Abs(y - wy) < 1.5f ? silhouette : new Color(1f, 0.72f, 0.36f, 1f);
            float wd = Mathf.Sqrt((x - wx) * (x - wx) + (y - wy) * (y - wy));
            float glow = Mathf.Clamp01(1f - wd / 170f);
            if (!window) c += new Color(0.32f, 0.17f, 0.06f, 0f) * (glow * glow * glow);

            // ふちを少し暗く
            float ex = Mathf.Abs(x) / 960f, ey = Mathf.Abs(y) / 540f;
            float edge = Mathf.Clamp01((ex * ex + ey * ey - 0.55f) / 0.9f);
            c *= 1f - 0.35f * edge;
            c.a = 1f;
            return c;
        }
    }
}
