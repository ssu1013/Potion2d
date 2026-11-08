using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step5（C：素材の性質の軸 ＋ D：発見の記録）のセットアップ。
    ///   ・新しい素材6つ・反応3つを、なければ作る（すでにある物は上書きしない）。シーンの AlchemyManager に反応を登録する
    ///   ・素材の仮の絵：Assets/Art2D/Ingredients/&lt;素材ID&gt;.png（256×256：トレイ・手に持つ・取引先帳・レシピ本・素材ノート）と
    ///     &lt;素材ID&gt;_piece.png（96×96：フラスコに落ちる欠片・底の溶け残り。液体にはない）
    ///   ・発見の仮の絵：Assets/Art2D/UI/Discovery/（page・card・unknown・stamp_*）→ UISkin に割り当て
    /// 仮の絵は、ファイルがないときだけ作る。同じ名前の PNG で上書きすれば、どこからも参照がそのままで絵だけ替わる。
    /// </summary>
    public static class Step5Discovery2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        public const string IngredientArtDir = "Assets/Art2D/Ingredients";
        public const string DiscoveryArtDir = "Assets/Art2D/UI/Discovery";
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private static readonly string[] Scenes = { "Assets/Scenes/Atelier2D.unity", "Assets/Scenes/Brew2D.unity" };

        private enum Shape { Bottle, Leaf, Flower, Petal, Crystal, Powder, Seed, Feather, Berry, Shell, Resin, Stone }

        [MenuItem(Menu + "Step5：素材と発見（新しい素材・素材の絵・発見の絵）", priority = 16)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step5", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new StringBuilder();
            string before = EditorSceneManager.GetActiveScene().path;

            AlchemySampleDataGenerator.SampleData d = AlchemySampleDataGenerator.CreateOrUpdateAssets();
            log.AppendLine("・新しい素材（なければ作った）：" + string.Join("、", Names(d.NewIngredients)));
            log.AppendLine("・新しい反応（なければ作った）：" + string.Join("、", ReactionNames(d.Reactions, 2)));

            EnsureIngredientArt(log);
            EnsureDiscoveryArt(log);
            RegisterReactions(d.Reactions, log);

            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(before) && File.Exists(before)) EditorSceneManager.OpenScene(before);
            log.AppendLine();
            log.AppendLine("絵を差し替えるときは、同じ名前の PNG で上書きするだけ（それぞれのフォルダの README.txt に一覧）。");
            Debug.Log("[Step5] " + log);
            EditorUtility.DisplayDialog("Step5：素材と発見", log.ToString(), "OK");
        }

        // ------------------------------------------------------------------
        // 素材の絵
        // ------------------------------------------------------------------

        private static void EnsureIngredientArt(StringBuilder log)
        {
            P.EnsureFolder(IngredientArtDir);
            List<IngredientData> all = IngredientCatalog.FindAllIngredients();
            int made = 0, assigned = 0;
            var readme = new StringBuilder();
            readme.AppendLine("素材の絵（同じ名前の PNG で上書きすると、ゲームのどこでも差し替わります）");
            readme.AppendLine("  <素材ID>.png       … 256×256 推奨。トレイ・手に持ったとき・取引先帳・レシピ本・素材ノートの絵（背景は透明）");
            readme.AppendLine("  <素材ID>_piece.png … 96×96 推奨。フラスコに入れたときに落ちる欠片・底に沈む溶け残り（液体の素材にはない）");
            readme.AppendLine("  ※ 絵は素材データ（Assets/GameData/Alchemy/Ingredients）の「アイコン」「欠片の絵」から参照しています");
            readme.AppendLine();
            foreach (IngredientData data in all)
            {
                if (data == null) continue;
                string id = data.IngredientId;
                Shape shape = ShapeFor(data);
                Color c = data.Color;
                c.a = 1f;
                if (!File.Exists($"{IngredientArtDir}/{id}.png")) made++;
                Sprite icon = P.Ensure(IngredientArtDir, id, 256, 256, (x, y) => Draw(shape, c, x, y, 1f));
                Sprite piece = null;
                if (!data.IsPoured)
                {
                    if (!File.Exists($"{IngredientArtDir}/{id}_piece.png")) made++;
                    piece = P.Ensure(IngredientArtDir, id + "_piece", 96, 96, (x, y) => Draw(shape, c, x, y, 256f / 96f));
                }
                if (data.EditorFillArt(icon, piece))
                {
                    EditorUtility.SetDirty(data);
                    assigned++;
                }
                readme.AppendLine($"{id}.png{(data.IsPoured ? "" : $" ／ {id}_piece.png")}　… {data.DisplayName}");
            }
            File.WriteAllText(IngredientArtDir + "/README.txt", readme.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(IngredientArtDir + "/README.txt");
            log.AppendLine($"・素材の絵：新しく作った {made} 枚、素材データに割り当てた {assigned} 件（{IngredientArtDir}）");
        }

        private static Shape ShapeFor(IngredientData d)
        {
            if (d.IsPoured || d.Category == IngredientCategory.BaseLiquid) return Shape.Bottle;
            if (d.HasTag("貝")) return Shape.Shell;
            if (d.HasTag("樹脂")) return Shape.Resin;
            if (d.HasTag("羽根")) return Shape.Feather;
            if (d.HasTag("種")) return Shape.Seed;
            if (d.HasTag("実")) return Shape.Berry;
            if (d.IngredientId.Contains("petal")) return Shape.Petal;
            if (d.HasTag("花")) return Shape.Flower;
            if (d.HasTag("草")) return Shape.Leaf;
            if (d.Form == IngredientForm.Powder) return Shape.Powder;
            if (d.Container == ContainerStyle.Crystal || d.HasTag("鉱石")) return Shape.Crystal;
            return Shape.Stone;
        }

        /// <summary>仮の素材の絵（x, y は 256×256 の真ん中を 0 とした画素。scale は小さい絵のときの拡大）</summary>
        private static Color Draw(Shape shape, Color c, float px, float py, float scale)
        {
            float x = px * scale, y = py * scale;
            Color dark = Color.Lerp(c, Color.black, 0.5f);
            Color light = Color.Lerp(c, Color.white, 0.45f);
            float d;
            Color body;
            switch (shape)
            {
                case Shape.Bottle:
                {
                    float glass = Mathf.Min(P.Box(x, y, 0f, -28f, 66f, 72f, 34f), P.Box(x, y, 0f, 58f, 22f, 30f, 6f));
                    float cork = P.Box(x, y, 0f, 92f, 26f, 14f, 5f);
                    float liquid = Mathf.Max(P.Box(x, y, 0f, -28f, 60f, 66f, 30f), y - 10f);
                    Color result = P.Paint(new Color(0.85f, 0.93f, 1f, 0.35f), P.Fill(glass));
                    result = P.Over(P.Paint(c, P.Fill(liquid)), result);
                    result = P.Over(P.Paint(new Color(0.95f, 0.9f, 0.75f, 1f), P.Fill(P.Box(x, y, 0f, -30f, 40f, 22f, 4f))), result);
                    result = P.Over(P.Paint(dark, P.Fill(P.Box(x, y, 0f, -30f, 26f, 4f, 2f))), result);
                    result = P.Over(P.Paint(new Color(1f, 1f, 1f, 0.55f), P.Fill(P.Box(x, y, -38f, -10f, 7f, 40f, 6f))), result);
                    result = P.Over(P.Paint(new Color(0.85f, 0.95f, 1f, 1f), P.Stroke(glass, 5f)), result);
                    result = P.Over(P.Paint(P.Cork, P.Fill(cork)), result);
                    return result;
                }
                case Shape.Leaf:
                {
                    Rot(x, y, 40f, out float u, out float v);
                    d = Ellipse(u, v, 100f, 46f);
                    body = Shade(c, light, u, v);
                    float rib = Mathf.Abs(v) < 3f && Mathf.Abs(u) < 92f ? 0.8f : 0f;
                    float veins = Mathf.Abs(Mathf.Repeat(u - Mathf.Abs(v) * 0.8f, 26f) - 13f) < 2f && Mathf.Abs(u) < 80f ? 0.5f : 0f;
                    Color leaf = P.Over(P.Paint(dark, Mathf.Max(rib, veins)), body);
                    float stem = P.Box(u, v, -112f, 0f, 22f, 4f, 3f);
                    return Outline(P.Over(P.Paint(dark, P.Fill(stem)), P.Paint(leaf, P.Fill(d))), dark, Mathf.Min(d, stem));
                }
                case Shape.Flower:
                {
                    Color result = new Color(0f, 0f, 0f, 0f);
                    float petals = 1e9f;
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f + 0.3f;
                        petals = Mathf.Min(petals, P.Circle(x, y, Mathf.Cos(a) * 52f, Mathf.Sin(a) * 52f, 44f));
                    }
                    result = P.Over(P.Paint(Shade(c, light, x, y), P.Fill(petals)), result);
                    result = Outline(result, dark, petals);
                    float center = P.Circle(x, y, 0f, 0f, 30f);
                    result = P.Over(P.Paint(new Color(1f, 0.85f, 0.35f, 1f), P.Fill(center)), result);
                    return P.Over(P.Paint(new Color(0.75f, 0.55f, 0.15f, 1f), P.Stroke(center, 4f)), result);
                }
                case Shape.Petal:
                {
                    Rot(x, y, -20f, out float u, out float v);
                    d = Mathf.Max(Ellipse(u, v + 10f, 66f, 104f), -P.Circle(u, v, 0f, 112f, 26f));
                    Color petal = Shade(c, light, u, v);
                    float vein = Mathf.Abs(u) < 2.5f && v < 70f ? 0.35f : 0f;
                    return Outline(P.Paint(P.Over(P.Paint(dark, vein), petal), P.Fill(d)), dark, d);
                }
                case Shape.Crystal:
                {
                    float hex = Mathf.Max(Mathf.Abs(x) * 0.866f + Mathf.Abs(y) * 0.5f, Mathf.Abs(y) * 0.62f) - 92f;
                    Color face = x < 0f ? light : (y > 0f ? c : Color.Lerp(c, dark, 0.35f));
                    float facet = Mathf.Max(Mathf.Abs(x) * 0.866f + Mathf.Abs(y) * 0.5f, Mathf.Abs(y) * 0.62f) - 52f;
                    Color result = P.Paint(face, P.Fill(hex));
                    result = P.Over(P.Paint(Color.Lerp(light, Color.white, 0.4f), P.Fill(facet) * 0.45f), result);
                    result = P.Over(P.Paint(new Color(1f, 1f, 1f, 0.8f), P.Fill(P.Circle(x, y, -34f, 36f, 10f))), result);
                    return Outline(result, dark, hex);
                }
                case Shape.Powder:
                {
                    d = Mathf.Max(Ellipse(x, y + 40f, 108f, 74f), -(y + 40f));
                    Color pile = Shade(c, light, x, y);
                    float speck = Mathf.PerlinNoise(x * 0.18f + 3f, y * 0.18f + 7f) > 0.68f ? 0.5f : 0f;
                    pile = P.Over(P.Paint(light, speck), pile);
                    Color result = Outline(P.Paint(pile, P.Fill(d)), dark, d);
                    for (int i = 0; i < 5; i++)
                    {
                        float sx = -70f + i * 35f, sy = 60f + (i % 2) * 22f;
                        result = P.Over(P.Paint(c, P.Fill(P.Circle(x, y, sx, sy, 7f))), result);
                    }
                    return result;
                }
                case Shape.Seed:
                {
                    Rot(x, y, 20f, out float u, out float v);
                    d = Ellipse(u, v + 10f, 54f, 84f);
                    Color seed = Shade(c, light, u, v);
                    float line = Mathf.Abs(u) < 3f && Mathf.Abs(v + 10f) < 70f ? 0.5f : 0f;
                    Color result = Outline(P.Paint(P.Over(P.Paint(dark, line), seed), P.Fill(d)), dark, d);
                    float sprout = Ellipse(u - 26f, v - 96f, 30f, 14f);
                    return Outline(P.Over(P.Paint(new Color(0.5f, 0.8f, 0.35f, 1f), P.Fill(sprout)), result), new Color(0.2f, 0.4f, 0.15f, 1f), Mathf.Min(sprout, d));
                }
                case Shape.Feather:
                {
                    Rot(x, y, 35f, out float u, out float v);
                    d = Ellipse(u, v, 36f, 112f);
                    float barbs = Mathf.Abs(Mathf.Repeat(v + Mathf.Abs(u) * 0.9f, 14f) - 7f) < 1.5f ? 0.35f : 0f;
                    Color feather = P.Over(P.Paint(dark, barbs), Shade(c, light, u, v));
                    float shaft = P.Box(u, v, 0f, -14f, 3f, 120f, 2f);
                    return Outline(P.Over(P.Paint(Color.Lerp(dark, Color.white, 0.4f), P.Fill(shaft)), P.Paint(feather, P.Fill(d))), dark, Mathf.Min(d, shaft));
                }
                case Shape.Berry:
                {
                    float b = Mathf.Min(P.Circle(x, y, -40f, -20f, 48f), Mathf.Min(P.Circle(x, y, 40f, -20f, 48f), P.Circle(x, y, 0f, 40f, 48f)));
                    Color result = Outline(P.Paint(Shade(c, light, x, y), P.Fill(b)), dark, b);
                    foreach (Vector2 h in new[] { new Vector2(-55f, -5f), new Vector2(25f, -5f), new Vector2(-15f, 55f) })
                        result = P.Over(P.Paint(new Color(1f, 1f, 1f, 0.7f), P.Fill(P.Circle(x, y, h.x, h.y, 9f))), result);
                    float leaf = Ellipse(x - 30f, y - 98f, 34f, 14f);
                    return Outline(P.Over(P.Paint(new Color(0.4f, 0.7f, 0.3f, 1f), P.Fill(leaf)), result), new Color(0.2f, 0.35f, 0.15f, 1f), Mathf.Min(leaf, b));
                }
                case Shape.Shell:
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    d = r - 96f;
                    float angle = Mathf.Atan2(y, x);
                    float spiral = Mathf.Repeat(angle / (Mathf.PI * 2f) + Mathf.Log(Mathf.Max(1f, r)) * 1.3f, 1f);
                    Color shell = Color.Lerp(light, c, spiral);
                    float line = spiral < 0.08f ? 0.6f : 0f;
                    return Outline(P.Paint(P.Over(P.Paint(dark, line), shell), P.Fill(d)), dark, d);
                }
                case Shape.Resin:
                {
                    // しずくの形：下の丸と、上へ細くなる先
                    float drop = P.Circle(x, y, 0f, -14f, 78f);
                    float t = Mathf.Clamp01((y + 14f) / 130f);
                    float w = 78f * Mathf.Pow(1f - t, 0.8f);
                    float tip = y >= -14f && y <= 116f ? Mathf.Abs(x) - w : 999f;
                    d = Mathf.Min(drop, tip);
                    Color resin = Shade(c, light, x, y);
                    Color result = Outline(P.Paint(resin, P.Fill(d)), dark, d);
                    return P.Over(P.Paint(new Color(1f, 1f, 1f, 0.75f), P.Fill(Ellipse(x + 30f, y - 30f, 14f, 24f))), result);
                }
                default:
                {
                    float wobble = (Mathf.PerlinNoise(Mathf.Atan2(y, x) * 1.5f + 2f, 3f) - 0.5f) * 24f;
                    d = Mathf.Sqrt(x * x + y * y * 1.3f) - 88f - wobble;
                    return Outline(P.Paint(Shade(c, light, x, y), P.Fill(d)), dark, d);
                }
            }
        }

        private static void Rot(float x, float y, float degrees, out float u, out float v)
        {
            float a = degrees * Mathf.Deg2Rad;
            u = x * Mathf.Cos(a) + y * Mathf.Sin(a);
            v = -x * Mathf.Sin(a) + y * Mathf.Cos(a);
        }

        /// <summary>楕円のだいたいの符号付き距離（画素）</summary>
        private static float Ellipse(float x, float y, float rx, float ry)
        {
            float k = Mathf.Sqrt((x * x) / (rx * rx) + (y * y) / (ry * ry));
            return (k - 1f) * Mathf.Min(rx, ry);
        }

        /// <summary>左上が明るい、ふんわりした塗り</summary>
        private static Color Shade(Color c, Color light, float x, float y)
        {
            float t = Mathf.Clamp01(0.5f + (-x + y) / 260f);
            return Color.Lerp(c, light, t * 0.7f);
        }

        private static Color Outline(Color fill, Color line, float d)
        {
            return P.Over(P.Paint(line, P.Stroke(d, 6f)), fill);
        }

        // ------------------------------------------------------------------
        // 発見の絵（ページ・カード・影絵・スタンプ）
        // ------------------------------------------------------------------

        private static void EnsureDiscoveryArt(StringBuilder log)
        {
            P.EnsureFolder(DiscoveryArtDir);
            int made = 0;
            Sprite page = Make("page", 256, 256, 56, PageArt, ref made);
            Sprite card = Make("card", 128, 144, 26, CardArt, ref made);
            Sprite unknown = Make("unknown", 128, 128, 0, UnknownArt, ref made);
            Sprite stampNew = Make("stamp_new", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.8f, 0.16f, 0.12f, 1f)), ref made);
            Sprite stampFact = Make("stamp_fact", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.82f, 0.3f, 0.12f, 1f)), ref made);
            Sprite stampReaction = Make("stamp_reaction", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.62f, 0.2f, 0.55f, 1f)), ref made);
            Sprite stampHelped = Make("stamp_helped", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.18f, 0.5f, 0.2f, 1f)), ref made);
            Sprite stampTaught = Make("stamp_taught", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.78f, 0.55f, 0.08f, 1f)), ref made);
            Sprite stampComplete = Make("stamp_complete", 160, 160, 0, (x, y) => StampArt(x, y, new Color(0.78f, 0.55f, 0.08f, 1f)), ref made);

            var readme = new StringBuilder();
            readme.AppendLine("発見・素材ノートの絵（同じ名前の PNG で上書きすると差し替わります。UISkin の「発見・素材ノート」から参照）");
            readme.AppendLine("  page.png            … 256×256。新しい反応のページ・「書き足した」お知らせの紙（9スライス：ふち 56）");
            readme.AppendLine("  card.png            … 128×144。素材ノートの素材カード（9スライス：ふち 26）");
            readme.AppendLine("  unknown.png         … 128×128。まだ出会っていない素材の影絵（白で描くと、暗い色で塗られる）");
            readme.AppendLine("  stamp_new.png       … 160×160。新しい液体を発見");
            readme.AppendLine("  stamp_fact.png      … 160×160。素材の性質を発見");
            readme.AppendLine("  stamp_reaction.png  … 160×160。新しい反応を発見");
            readme.AppendLine("  stamp_helped.png    … 160×160。レシピ本「役に立った」");
            readme.AppendLine("  stamp_taught.png    … 160×160。レシピ本「作り方を教えた」");
            readme.AppendLine("  stamp_complete.png  … 160×160。素材ノート「すべて発見」");
            readme.AppendLine("  ※ スタンプの上には文字（新発見 など）を重ねて出します。絵に文字を描き込んだら、UISkin の「スタンプに文字を重ねる」をオフに");
            File.WriteAllText(DiscoveryArtDir + "/README.txt", readme.ToString(), new UTF8Encoding(true));
            AssetDatabase.ImportAsset(DiscoveryArtDir + "/README.txt");

            UISkin skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            int assigned = 0;
            if (skin != null)
            {
                Undo.RecordObject(skin, "Step5 発見の絵");
                assigned += Fill(ref skin.discoveryPage, page);
                assigned += Fill(ref skin.notebookCard, card);
                assigned += Fill(ref skin.unknownMark, unknown);
                assigned += Fill(ref skin.stampNew, stampNew);
                assigned += Fill(ref skin.stampFact, stampFact);
                assigned += Fill(ref skin.stampReaction, stampReaction);
                assigned += Fill(ref skin.stampHelped, stampHelped);
                assigned += Fill(ref skin.stampTaught, stampTaught);
                assigned += Fill(ref skin.stampComplete, stampComplete);
                if (assigned > 0) EditorUtility.SetDirty(skin);
            }
            log.AppendLine($"・発見の絵：新しく作った {made} 枚、UISkin に割り当てた {assigned} 件（{DiscoveryArtDir}）");
        }

        private static Sprite Make(string name, int w, int h, int border, Func<float, float, Color> shader, ref int made)
        {
            string path = $"{DiscoveryArtDir}/{name}.png";
            bool isNew = !File.Exists(path);
            Sprite s = P.Ensure(DiscoveryArtDir, name, w, h, shader);
            if (isNew)
            {
                made++;
                if (border > 0 && AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.spriteBorder = new Vector4(border, border, border, border);
                    ti.SaveAndReimport();
                    s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
            }
            return s;
        }

        private static int Fill(ref Sprite field, Sprite value)
        {
            if (field != null || value == null) return 0;
            field = value;
            return 1;
        }

        private static Color PageArt(float x, float y)
        {
            float d = P.Box(x, y, 0f, 0f, 126f, 126f, 18f);
            float noise = Mathf.PerlinNoise(x * 0.05f + 10f, y * 0.05f + 4f);
            Color paper = Color.Lerp(new Color(0.98f, 0.94f, 0.82f, 1f), new Color(0.93f, 0.85f, 0.68f, 1f), noise * 0.6f);
            Color result = P.Paint(paper, P.Fill(d));
            result = P.Over(P.Paint(new Color(0.55f, 0.36f, 0.18f, 1f), P.Stroke(d, 6f)), result);
            float inner = P.Box(x, y, 0f, 0f, 108f, 108f, 10f);
            result = P.Over(P.Paint(new Color(0.72f, 0.52f, 0.25f, 0.8f), P.Stroke(inner, 2.5f)), result);
            foreach (Vector2 k in new[] { new Vector2(-108f, -108f), new Vector2(108f, -108f), new Vector2(-108f, 108f), new Vector2(108f, 108f) })
                result = P.Over(P.Paint(new Color(0.72f, 0.52f, 0.25f, 1f), P.Fill(P.Diamond(x, y, k.x, k.y, 9f, 9f))), result);
            return result;
        }

        private static Color CardArt(float x, float y)
        {
            float d = P.Box(x, y, 0f, 0f, 62f, 70f, 12f);
            Color wood = Color.Lerp(new Color(0.3f, 0.19f, 0.11f, 1f), new Color(0.36f, 0.23f, 0.13f, 1f), Mathf.PerlinNoise(x * 0.02f, y * 0.25f));
            Color result = P.Paint(wood, P.Fill(d));
            result = P.Over(P.Paint(new Color(0.78f, 0.6f, 0.32f, 1f), P.Stroke(d, 4f)), result);
            return result;
        }

        private static Color UnknownArt(float x, float y)
        {
            // 白い影絵（丸い形）に「？」の形の穴
            float blob = Mathf.Sqrt(x * x + y * y * 1.1f) - 54f;
            float ringR = Mathf.Sqrt(x * x + (y - 14f) * (y - 14f));
            float hook = Mathf.Abs(ringR - 17f) - 6f;
            if (y - 14f < -4f && x < 0f) hook = 99f;
            float stem = P.Box(x, y, 3f, -10f, 6f, 9f, 3f);
            float dot = P.Circle(x, y, 3f, -32f, 6.5f);
            float mark = Mathf.Min(hook, Mathf.Min(stem, dot));
            return new Color(1f, 1f, 1f, P.Fill(blob) * (1f - P.Fill(mark)));
        }

        private static Color StampArt(float x, float y, Color ink)
        {
            // 朱肉のスタンプ：二重の輪と、上下の小さな星（真ん中は文字のために空ける）。ふちは少しかすれる
            float r = Mathf.Sqrt(x * x + y * y);
            float outer = Mathf.Abs(r - 70f) - 5f;
            float inner = Mathf.Abs(r - 58f) - 2f;
            float stars = Mathf.Min(P.Diamond(x, y, 0f, 64f, 7f, 7f), P.Diamond(x, y, 0f, -64f, 7f, 7f));
            float a = Mathf.Max(P.Fill(Mathf.Min(outer, inner)), P.Fill(stars) * 0.9f);
            float grain = Mathf.PerlinNoise(x * 0.12f + 5f, y * 0.12f + 9f);
            a *= grain > 0.22f ? 1f : 0.35f;
            ink.a *= a * 0.92f;
            return ink;
        }

        // ------------------------------------------------------------------
        // 反応をシーンの AlchemyManager に登録
        // ------------------------------------------------------------------

        private static void RegisterReactions(ReactionData[] reactions, StringBuilder log)
        {
            foreach (string path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                AlchemyManager am = UnityEngine.Object.FindFirstObjectByType<AlchemyManager>();
                if (am == null) continue;
                var so = new SerializedObject(am);
                SerializedProperty list = so.FindProperty("reactions");
                var have = new HashSet<UnityEngine.Object>();
                for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
                int added = 0;
                foreach (ReactionData r in reactions)
                {
                    if (r == null || have.Contains(r)) continue;
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = r;
                    added++;
                }
                if (added > 0)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    log.AppendLine($"・{Path.GetFileNameWithoutExtension(path)} の AlchemyManager に反応を {added} つ登録した");
                }
            }
        }

        private static IEnumerable<string> Names(IEnumerable<IngredientData> list)
        {
            foreach (IngredientData d in list) if (d != null) yield return d.DisplayName;
        }

        private static IEnumerable<string> ReactionNames(ReactionData[] list, int skip)
        {
            for (int i = skip; i < list.Length; i++) if (list[i] != null) yield return list[i].DisplayName;
        }
    }
}
