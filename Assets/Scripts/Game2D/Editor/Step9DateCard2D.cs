using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step9：日付のアイキャッチ（タイトルから遊びはじめるときに、暗い画面に日付が浮かぶ）の仮の絵と音を作って、
    /// UISkin・SoundLibrary につなぐ（何度実行してもよい。すでにあるファイル・入っている欄には触らない）。
    ///   Assets/CustomAssets/Images/UI/DateCard/date_background.png … 画面いっぱいの背景（1920×1080）
    ///   Assets/CustomAssets/Images/UI/DateCard/date_ornament.png   … 日付の下の飾りの線（512×32）
    ///   Assets/CustomAssets/Sounds/SE/UI/se_date.wav               … 日付が浮かび上がるときの音
    /// 自分のイラストに替えるときは、同じ名前の PNG で上書きするだけでよい。
    /// </summary>
    public static class Step9DateCard2D
    {
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string SoundLibraryPath = "Assets/Audio/SoundLibrary.asset";

        [MenuItem("Tools/Potion Frontier 2D/Step9：日付のアイキャッチ（絵と音を作る）", priority = 19)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step9", "再生を止めてから実行してください。", "OK");
                return;
            }
            var log = new StringBuilder();
            string dir = CustomAssetPaths.UIDateCard;
            Sprite background = Make(dir, "date_background", 1920, 1080, BackgroundArt, log);
            Sprite ornament = Make(dir, "date_ornament", 512, 32, OrnamentArt, log);
            string wav = CustomAssetPaths.SeUI + "/se_date.wav";
            bool newWav = !File.Exists(wav);
            AudioClip sound = Step8SingleScreen2D.WriteWav(CustomAssetPaths.SeUI, "se_date", SfxSynth.DateBell);
            log.AppendLine($"・音：{(newWav ? "作った" : "そのまま")}（{wav}）");

            int linked = 0;
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin != null)
            {
                if (skin.dateBackground == null && background != null) { skin.dateBackground = background; linked++; }
                if (skin.dateOrnament == null && ornament != null) { skin.dateOrnament = ornament; linked++; }
                EditorUtility.SetDirty(skin);
            }
            else log.AppendLine("！ " + SkinPath + " がない");
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            if (lib != null)
            {
                if (lib.dateCard == null && sound != null) { lib.dateCard = sound; linked++; }
                EditorUtility.SetDirty(lib);
            }
            else log.AppendLine("！ " + SoundLibraryPath + " がない");
            log.AppendLine($"・つないだ欄：{linked}（UISkin の「日付のアイキャッチ」・SoundLibrary の dateCard）");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Step7CustomAssets2D.Report(log);
            Debug.Log("[Step9] 日付のアイキャッチ\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step9.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step9：日付のアイキャッチ", log.ToString(), "OK");
        }

        private static Sprite Make(string dir, string name, int w, int h, System.Func<float, float, Color> art, StringBuilder log)
        {
            string path = $"{dir}/{name}.png";
            bool isNew = !File.Exists(path);
            Sprite s = P.Ensure(dir, name, w, h, art);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && !Art2DImportRules.TryParse(ti.userData, out _) &&
                Art2DImportRules.TryReadPngSize(path, out int pw, out int ph))
            {
                // 元の大きさを覚える（大きく描き直して上書きしても、出る大きさがそろう）。UI の絵なので、にじまないように圧縮しない
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.userData = Art2DImportRules.Format(pw, ph, ti.spritePixelsPerUnit, ti.spriteBorder);
                ti.SaveAndReimport();
                s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            log.AppendLine($"・絵：{(isNew ? "作った" : "そのまま")}（{path}）");
            return s;
        }

        /// <summary>仮の背景：深い夜の色。下の方に、ランプのようなかすかな暖かい明かりと、上の方にわずかな星</summary>
        private static Color BackgroundArt(float x, float y)
        {
            float v = Mathf.InverseLerp(-540f, 540f, y);
            Color night = Color.Lerp(new Color(0.045f, 0.03f, 0.03f, 1f), new Color(0.03f, 0.04f, 0.075f, 1f), v);
            // 下の真ん中の、暖かい明かり
            float dx = x / 900f, dy = (y + 620f) / 520f;
            float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            night += new Color(0.16f, 0.085f, 0.03f, 0f) * (glow * glow);
            // ふちを少し暗く
            float edge = Mathf.Clamp01((Mathf.Abs(x) - 560f) / 400f);
            night *= 1f - 0.45f * edge * edge;
            // 星（上の方だけ、ごくわずか）
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

        /// <summary>仮の飾り：両端が消える細い線と、真ん中の小さなひし形</summary>
        private static Color OrnamentArt(float x, float y)
        {
            var cream = new Color(0.86f, 0.78f, 0.64f, 1f);
            float fade = Mathf.Clamp01(1f - Mathf.Abs(x) / 250f);
            float line = Mathf.Clamp01(1.2f - Mathf.Abs(y)) * fade * 0.7f * (Mathf.Abs(x) > 20f ? 1f : 0f);
            float diamond = P.Fill(P.Diamond(x, y, 0f, 0f, 9f, 9f));
            float inner = P.Fill(P.Diamond(x, y, 0f, 0f, 4.5f, 4.5f));
            float a = Mathf.Max(line, diamond * 0.9f - inner * 0.6f);
            return new Color(cream.r, cream.g, cream.b, a);
        }
    }
}
