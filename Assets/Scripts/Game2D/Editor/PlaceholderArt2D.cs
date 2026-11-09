using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 仮の絵（PNG）をコードで描いて Assets/CustomAssets/Images の下（場所は CustomAssetPaths の表）に保存する。すでにあるファイルには触らない。
    /// 自分の絵に替えるときは、同じ名前の PNG で上書きするだけでよい（大きさが違っても、ゲームの中での大きさは自動でそろう）。
    /// 大きさ（ピクセル）は 100 ピクセル = 1 ワールド単位。
    /// </summary>
    public static class PlaceholderArt2D
    {
        public sealed class Set
        {
            public Sprite background, flaskBack, flaskMask, flaskFront, liquidBody, liquidSurface;
            public Sprite heaterStand, flame, knobBase, knobDial, tray, stirRod, dropPiece, pourStream, lamp;
            public Sprite tallBottle, tallBottleContent, jar, jarContent, roundFlask, roundFlaskContent;
            public Sprite pouch, pouchContent, crystal, crystalContent, potionBottle, potionBottleContent;
            public Sprite fxSteam, fxBubble, fxDroplet, fxSwirl, fxRipple;
        }

        internal static readonly Color Glass = new Color(0.82f, 0.92f, 1f, 0.16f);
        internal static readonly Color GlassEdge = new Color(0.86f, 0.93f, 1f, 0.9f);
        internal static readonly Color Cork = new Color(0.55f, 0.38f, 0.24f, 1f);

        public static Set EnsureAll(string dir)
        {
            EnsureFolder(dir);
            var s = new Set();
            s.background = Ensure(dir, "brew_background", 1920, 1080, Background);
            s.flaskBack = Ensure(dir, "flask_back", 280, 340, (x, y) => GlassShape(FlaskShape(x, y)));
            s.flaskMask = Ensure(dir, "flask_mask", 280, 340, (x, y) => White(Fill(FlaskShape(x, y) + 7f)));
            s.flaskFront = Ensure(dir, "flask_front", 280, 340, FlaskFront);
            s.liquidBody = Ensure(dir, "liquid_body", 64, 64, (x, y) => Color.white);
            s.liquidSurface = Ensure(dir, "liquid_surface", 256, 40, (x, y) => White(EllipseFill(x, y, 124f, 17f)));
            s.heaterStand = Ensure(dir, "heater_stand", 360, 70, HeaterStand);
            s.flame = Ensure(dir, "flame", 100, 150, Flame);
            s.knobBase = Ensure(dir, "knob_base", 110, 110, (x, y) => Over(Paint(new Color(0.85f, 0.7f, 0.45f, 1f), Stroke(Circle(x, y, 0f, 0f, 49f), 4f)), Paint(new Color(0.2f, 0.17f, 0.15f, 1f), Fill(Circle(x, y, 0f, 0f, 50f)))));
            s.knobDial = Ensure(dir, "knob_dial", 90, 90, KnobDial);
            s.tray = Ensure(dir, "tray", 1300, 100, Tray);
            s.stirRod = Ensure(dir, "stir_rod", 24, 320, (x, y) => Over(Paint(new Color(1f, 1f, 1f, 0.6f), Fill(Box(x, y, -3f, 0f, 2f, 150f, 2f))), Paint(new Color(0.85f, 0.93f, 1f, 0.75f), Fill(Box(x, y, 0f, 0f, 7f, 155f, 7f)))));
            s.dropPiece = Ensure(dir, "drop_piece", 40, 40, (x, y) => White(Fill(Circle(x, y, 0f, 0f, 15f))));
            s.pourStream = Ensure(dir, "pour_stream", 24, 64, (x, y) => White(Fill(Box(x, y, 0f, 0f, 8f, 32f, 8f))));
            s.lamp = Ensure(dir, "lamp", 140, 220, Lamp);

            // 素材の容器（体＝ガラスなど、中身＝白い形）。どれも 140×200 のキャンバス
            s.tallBottle = Ensure(dir, "container_tall_bottle", 140, 200, (x, y) => Over(Paint(Cork, Fill(Box(x, y, 0f, 82f, 15f, 9f, 3f))), GlassShape(Min(Box(x, y, 0f, -20f, 38f, 62f, 14f), Box(x, y, 0f, 55f, 14f, 22f, 4f)))));
            s.tallBottleContent = Ensure(dir, "container_tall_bottle_content", 140, 200, (x, y) => White(Fill(Box(x, y, 0f, -30f, 32f, 50f, 11f))));
            s.jar = Ensure(dir, "container_jar", 140, 200, (x, y) => Over(Paint(Cork, Fill(Box(x, y, 0f, 62f, 50f, 8f, 4f))), GlassShape(Min(Box(x, y, 0f, -12f, 55f, 58f, 18f), Box(x, y, 0f, 50f, 46f, 8f, 4f)))));
            s.jarContent = Ensure(dir, "container_jar_content", 140, 200, (x, y) => White(Fill(Box(x, y, 0f, -20f, 48f, 44f, 14f))));
            s.roundFlask = Ensure(dir, "container_round_flask", 140, 200, (x, y) => Over(Paint(Cork, Fill(Box(x, y, 0f, 78f, 15f, 9f, 3f))), GlassShape(Min(Circle(x, y, 0f, -28f, 52f), Box(x, y, 0f, 40f, 13f, 32f, 4f)))));
            s.roundFlaskContent = Ensure(dir, "container_round_flask_content", 140, 200, (x, y) => White(Fill(Circle(x, y, 0f, -30f, 44f))));
            s.pouch = Ensure(dir, "container_pouch", 140, 200, Pouch);
            s.pouchContent = Ensure(dir, "container_pouch_content", 140, 200, (x, y) => White(Fill(Box(x, y, 0f, -12f, 40f, 12f, 4f))));
            s.crystal = Ensure(dir, "container_crystal", 140, 200, (x, y) => Paint(new Color(1f, 1f, 1f, 0.85f), Stroke(Diamond(x, y, 0f, -5f, 42f, 80f), 3f)));
            s.crystalContent = Ensure(dir, "container_crystal_content", 140, 200, (x, y) => Over(White(0.55f * Fill(Diamond(x, y, -12f, 8f, 14f, 40f))), Paint(new Color(0.85f, 0.85f, 0.85f, 1f), Fill(Diamond(x, y, 0f, -5f, 42f, 80f)))));
            s.potionBottle = Ensure(dir, "potion_bottle", 140, 200, (x, y) => Over(Paint(Cork, Fill(Box(x, y, 0f, 74f, 16f, 10f, 3f))), GlassShape(Min(Circle(x, y, 0f, -32f, 50f), Box(x, y, 0f, 32f, 14f, 34f, 4f)))));
            s.potionBottleContent = Ensure(dir, "potion_bottle_content", 140, 200, (x, y) => White(Fill(Circle(x, y, 0f, -34f, 42f))));

            // 演出（白で描く。色はコードで付ける）
            s.fxSteam = Ensure(dir, "fx_steam", 128, 128, SoftPuff);
            s.fxBubble = Ensure(dir, "fx_bubble", 48, 48, (x, y) => Over(White(Fill(Circle(x, y, -6f, 6f, 4f)) * 0.9f), White(Stroke(Circle(x, y, 0f, 0f, 19f), 3.5f) * 0.9f + Fill(Circle(x, y, 0f, 0f, 19f)) * 0.12f)));
            s.fxDroplet = Ensure(dir, "fx_droplet", 32, 32, (x, y) => White(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) / 13f) * 1.4f));
            s.fxSwirl = Ensure(dir, "fx_swirl", 256, 256, Swirl);
            s.fxRipple = Ensure(dir, "fx_ripple", 256, 256, (x, y) => White(Stroke(Circle(x, y, 0f, 0f, 112f), 9f) * 0.9f));
            return s;
        }

        /// <summary>湯気・煙：中心が濃く、ふちがふわっと消える丸（少しむらがある）</summary>
        internal static Color SoftPuff(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y) / 62f;
            float lumpy = 0.12f * Mathf.Sin(x * 0.11f + y * 0.07f) + 0.1f * Mathf.Sin(y * 0.13f - x * 0.05f);
            float a = Mathf.Clamp01(1f - r + lumpy * (1f - r));
            return White(a * a * 0.9f);
        }

        /// <summary>液面の渦：真上から見た、3本の腕の渦巻き（真ん中とふちは消える）</summary>
        internal static Color Swirl(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y) / 124f;
            if (r >= 1f) return White(0f);
            float theta = Mathf.Atan2(y, x);
            float arms = 0.5f + 0.5f * Mathf.Cos(3f * (theta + r * 5.5f));
            arms = Mathf.Pow(arms, 3f);
            float fade = Mathf.Clamp01(r * 3f) * Mathf.Clamp01((1f - r) * 4f);
            return White(arms * fade * 0.85f);
        }

        // ------------------------------------------------------------------
        // ファイル
        // ------------------------------------------------------------------

        internal static Sprite Ensure(string dir, string name, int width, int height, Func<float, float, Color> shader)
        {
            dir = CustomAssetPaths.FolderForImage(name, dir);
            EnsureFolder(dir);
            string path = $"{dir}/{name}.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color[width * height];
                for (int py = 0; py < height; py++)
                {
                    float y = py - height * 0.5f + 0.5f;
                    for (int px = 0; px < width; px++)
                    {
                        float x = px - width * 0.5f + 0.5f;
                        pixels[py * width + px] = shader(x, y);
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[2D 調合画面] {path} をスプライトとして読めませんでした（Texture Type を Sprite (2D and UI) にしてください）");
            return sprite;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------
        // 形（符号付き距離：内側がマイナス。単位はピクセル）
        // ------------------------------------------------------------------

        internal static float Circle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        internal static float Box(float x, float y, float cx, float cy, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x - cx) - hx + radius;
            float qy = Mathf.Abs(y - cy) - hy + radius;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        internal static float Diamond(float x, float y, float cx, float cy, float hx, float hy)
        {
            float v = Mathf.Abs(x - cx) / hx + Mathf.Abs(y - cy) / hy - 1f;
            return v * Mathf.Min(hx, hy) * 0.9f;
        }

        internal static float Min(float a, float b) => Mathf.Min(a, b);

        internal static float EllipseFill(float x, float y, float rx, float ry)
        {
            float v = (x * x) / (rx * rx) + (y * y) / (ry * ry);
            return Mathf.Clamp01((1f - v) * Mathf.Min(rx, ry) * 0.5f);
        }

        internal static float Fill(float d) => Mathf.Clamp01(0.5f - d);
        internal static float Stroke(float d, float width) => Mathf.Clamp01(0.5f - (Mathf.Abs(d) - width * 0.5f));

        internal static Color White(float alpha) => new Color(1f, 1f, 1f, alpha);

        internal static Color Paint(Color c, float coverage)
        {
            c.a *= coverage;
            return c;
        }

        /// <summary>top を bottom の上に重ねる</summary>
        internal static Color Over(Color top, Color bottom)
        {
            float a = top.a + bottom.a * (1f - top.a);
            if (a <= 1e-5f) return new Color(0f, 0f, 0f, 0f);
            Color c = (top * top.a + bottom * bottom.a * (1f - top.a)) / a;
            c.a = a;
            return c;
        }

        /// <summary>ガラス：うすい中身＋明るいふち</summary>
        internal static Color GlassShape(float d)
        {
            return Over(Paint(GlassEdge, Stroke(d, 4f)), Paint(Glass, Fill(d)));
        }

        // ------------------------------------------------------------------
        // 個別の絵
        // ------------------------------------------------------------------

        /// <summary>丸底フラスコの形（280×340 のキャンバス。口は上端から 3px 下）</summary>
        internal static float FlaskShape(float x, float y)
        {
            float body = Circle(x, y, 0f, -40f, 115f);
            float neck = Box(x, y, 0f, 95f, 34f, 72f, 6f);
            float lip = Box(x, y, 0f, 160f, 44f, 8f, 4f);
            return Min(Min(body, neck), lip);
        }

        internal static Color FlaskFront(float x, float y)
        {
            float d = FlaskShape(x, y);
            float inside = Fill(d);
            float streak = Fill(Box(x, y, -62f, -30f, 9f, 55f, 9f)) * 0.35f * inside;
            float dot = Fill(Circle(x, y, 58f, -95f, 10f)) * 0.25f * inside;
            float neckStreak = Fill(Box(x, y, -18f, 100f, 4f, 45f, 4f)) * 0.3f * inside;
            Color c = White(Mathf.Max(streak, Mathf.Max(dot, neckStreak)));
            return Over(Paint(new Color(1f, 1f, 1f, 0.55f), Stroke(d, 2f)), c);
        }

        internal static Color HeaterStand(float x, float y)
        {
            Color iron = Paint(new Color(0.24f, 0.21f, 0.2f, 1f), Fill(Box(x, y, 0f, -6f, 175f, 26f, 10f)));
            Color grate = Paint(new Color(0.42f, 0.36f, 0.32f, 1f), Fill(Box(x, y, 0f, 24f, 150f, 6f, 3f)));
            return Over(grate, iron);
        }

        internal static Color Flame(float x, float y)
        {
            float yy = y + 20f;
            float rx = 34f * Mathf.Clamp01(1f - Mathf.Max(0f, yy) / 85f);
            if (rx < 0.5f) return new Color(0f, 0f, 0f, 0f);
            float ry = yy < 0f ? 40f : 85f;
            float v = (x * x) / (rx * rx) + (yy * yy) / (ry * ry);
            float a = Mathf.Clamp01((1f - v) * 2.2f);
            float core = Mathf.Clamp01((0.45f - v) * 3f);
            Color outer = new Color(1f, 0.85f, 0.55f, a);
            return Over(White(core), outer);
        }

        internal static Color KnobDial(float x, float y)
        {
            Color brass = Paint(new Color(0.8f, 0.62f, 0.34f, 1f), Fill(Circle(x, y, 0f, 0f, 38f)));
            Color ring = Paint(new Color(0.55f, 0.4f, 0.2f, 1f), Stroke(Circle(x, y, 0f, 0f, 37f), 3f));
            Color notch = Paint(new Color(0.25f, 0.17f, 0.1f, 1f), Fill(Box(x, y, 0f, 22f, 5f, 14f, 3f)));
            return Over(notch, Over(ring, brass));
        }

        internal static Color Tray(float x, float y)
        {
            float d = Box(x, y, 0f, 0f, 645f, 45f, 12f);
            Color wood = Paint(new Color(0.45f, 0.3f, 0.18f, 1f), Fill(d));
            Color top = Paint(new Color(0.58f, 0.4f, 0.24f, 1f), Fill(Box(x, y, 0f, 30f, 640f, 12f, 8f)));
            float grain = 0.06f * Mathf.Sin(x * 0.05f + Mathf.Sin(y * 0.2f) * 2f);
            wood.r += grain; wood.g += grain * 0.7f;
            Color edge = Paint(new Color(0.25f, 0.16f, 0.1f, 1f), Stroke(d, 3f));
            return Over(edge, Over(top, wood));
        }

        internal static Color Pouch(float x, float y)
        {
            float d = Min(Box(x, y, 0f, -18f, 52f, 54f, 26f), Box(x, y, 0f, 48f, 24f, 12f, 6f));
            Color cloth = Paint(new Color(0.78f, 0.68f, 0.52f, 1f), Fill(d));
            Color tie = Paint(new Color(0.45f, 0.3f, 0.2f, 1f), Fill(Box(x, y, 0f, 36f, 30f, 4f, 2f)));
            Color edge = Paint(new Color(0.45f, 0.36f, 0.26f, 1f), Stroke(d, 3f));
            return Over(tie, Over(edge, cloth));
        }

        internal static Color Lamp(float x, float y)
        {
            float frame = Box(x, y, 0f, -10f, 46f, 70f, 8f);
            Color glass = Paint(new Color(1f, 0.85f, 0.6f, 0.25f), Fill(frame));
            Color metal = Paint(new Color(0.22f, 0.19f, 0.17f, 1f), Stroke(frame, 6f));
            Color cap = Paint(new Color(0.22f, 0.19f, 0.17f, 1f), Fill(Box(x, y, 0f, 70f, 32f, 10f, 4f)));
            Color foot = Paint(new Color(0.22f, 0.19f, 0.17f, 1f), Fill(Box(x, y, 0f, -84f, 40f, 8f, 3f)));
            Color handle = Paint(new Color(0.22f, 0.19f, 0.17f, 1f), Stroke(Circle(x, y, 0f, 92f, 14f), 4f));
            return Over(handle, Over(cap, Over(foot, Over(metal, glass))));
        }

        /// <summary>背景（1920×1080 = 画面いっぱい）：木の壁・窓・作業台の天板</summary>
        internal static Color Background(float x, float y)
        {
            // 壁（縦の板）
            float plank = Mathf.Repeat(x + 960f, 160f);
            float seam = Mathf.Clamp01(1f - Mathf.Abs(plank - 1f) / 2.5f);
            float tone = 0.02f * Mathf.Sin((x + 960f) * 0.013f) + 0.015f * Mathf.Sin(y * 0.02f + x * 0.001f);
            Color wall = new Color(0.36f + tone, 0.25f + tone, 0.18f + tone * 0.5f, 1f);
            wall = Color.Lerp(wall, new Color(0.18f, 0.12f, 0.08f, 1f), seam * 0.7f);

            // 窓（右上）
            float win = Box(x, y, 580f, 260f, 170f, 190f, 10f);
            if (win < 0f)
            {
                float bar = Mathf.Min(Mathf.Abs(x - 580f), Mathf.Abs(y - 260f));
                Color night = Color.Lerp(new Color(0.1f, 0.14f, 0.3f, 1f), new Color(0.22f, 0.3f, 0.5f, 1f), Mathf.Clamp01((y - 70f) / 380f));
                wall = bar < 6f ? new Color(0.2f, 0.13f, 0.09f, 1f) : night;
                float moon = Circle(x, y, 650f, 360f, 34f);
                if (moon < 0f) wall = Color.Lerp(wall, new Color(0.95f, 0.95f, 0.85f, 1f), Fill(moon));
            }
            float frame = Stroke(win, 16f);
            wall = Color.Lerp(wall, new Color(0.24f, 0.16f, 0.1f, 1f), frame);

            // 棚（左上）
            float shelf = Box(x, y, -560f, 250f, 260f, 10f, 3f);
            wall = Color.Lerp(wall, new Color(0.3f, 0.2f, 0.12f, 1f), Fill(shelf));

            // 作業台の天板と前板（下）
            if (y < -300f)
            {
                Color table = new Color(0.5f, 0.34f, 0.2f, 1f);
                float g = 0.04f * Mathf.Sin(x * 0.02f + Mathf.Sin(y * 0.05f) * 3f);
                table.r += g; table.g += g * 0.7f;
                if (y < -330f) table = Color.Lerp(table, new Color(0.3f, 0.2f, 0.12f, 1f), 0.6f);
                wall = table;
            }
            return wall;
        }
    }
}
