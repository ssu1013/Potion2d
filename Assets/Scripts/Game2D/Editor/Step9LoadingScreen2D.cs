using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step9：ロード画面（タイトルから遊びはじめるときに出る1枚絵）の仮の絵を作って、UISkin につなぐ
    /// （何度実行してもよい。すでにあるファイル・入っている欄には触らない）。
    ///   Assets/CustomAssets/Images/UI/Loading/loading_background.png … 画面いっぱいの1枚絵（1920×1080）
    /// 自分のイラストに替えるときは、同じ名前の PNG で上書きするだけでよい（このメニューを実行する必要はない）。
    /// </summary>
    public static class Step9LoadingScreen2D
    {
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";

        [MenuItem("Tools/Potion Frontier 2D/Step9：ロード画面（仮の絵を作ってつなぐ）", priority = 19)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step9", "再生を止めてから実行してください。", "OK");
                return;
            }
            var log = new StringBuilder();
            string path = CustomAssetPaths.UILoading + "/loading_background.png";
            bool isNew = !File.Exists(path);
            Sprite background = P.Ensure(CustomAssetPaths.UILoading, "loading_background", 1920, 1080, BackgroundArt);
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
            else if (skin.loadingBackground == null && background != null)
            {
                skin.loadingBackground = background;
                EditorUtility.SetDirty(skin);
                log.AppendLine("・UISkin の「ロード画面」欄につないだ");
            }
            else log.AppendLine("・UISkin の「ロード画面」欄は、もうつながっている");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Step7CustomAssets2D.Report(log);
            Debug.Log("[Step9] ロード画面\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step9.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step9：ロード画面", log.ToString(), "OK");
        }

        /// <summary>仮の絵：深い夜の色。下の方に、ランプのようなかすかな暖かい明かりと、上の方にわずかな星</summary>
        private static Color BackgroundArt(float x, float y)
        {
            float v = Mathf.InverseLerp(-540f, 540f, y);
            Color night = Color.Lerp(new Color(0.045f, 0.03f, 0.03f, 1f), new Color(0.03f, 0.04f, 0.075f, 1f), v);
            float dx = x / 900f, dy = (y + 620f) / 520f;
            float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            night += new Color(0.16f, 0.085f, 0.03f, 0f) * (glow * glow);
            float edge = Mathf.Clamp01((Mathf.Abs(x) - 560f) / 400f);
            night *= 1f - 0.45f * edge * edge;
            float cx = Mathf.Floor(x / 48f), cy = Mathf.Floor(y / 48f);
            float h = Mathf.Abs(Mathf.Sin(cx * 127.1f + cy * 311.7f) * 43758.5453f);
            h -= Mathf.Floor(h);
            if (h > 0.93f && y > 60f)
            {
                float h2 = Mathf.Abs(Mathf.Sin(cx * 269.5f + cy * 183.3f) * 24634.63f);
                h2 -= Mathf.Floor(h2);
                float sx = (cx + 0.2f + 0.6f * h2) * 48f, sy = (cy + 0.2f + 0.6f * (1f - h2)) * 48f;
                float d = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                float star = Mathf.Clamp01(1.6f - d) * (0.25f + 0.5f * (h - 0.93f) / 0.07f) * Mathf.Clamp01((y - 60f) / 300f);
                night += new Color(0.8f, 0.85f, 1f, 0f) * star;
            }
            night.a = 1f;
            return night;
        }
    }
}
