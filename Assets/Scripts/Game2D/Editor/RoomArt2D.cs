using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// アトリエの部屋の仮の絵（1920×1080 を3枚つないで横長の部屋にする）。すでにあるファイルには触らない。
    /// room_left（入口とカウンター）・room_center（作業台と窓）・room_right（瓶の棚とレシピ本の台）。
    /// 自分の絵に替えるときは、同じ名前・同じ大きさの PNG で上書きする（3枚の端がつながるように描く）。
    /// </summary>
    public static class RoomArt2D
    {
        public sealed class Set
        {
            public Sprite left, center, right;
        }

        private static readonly Color WallA = new Color(0.36f, 0.25f, 0.18f, 1f);
        private static readonly Color WallSeam = new Color(0.18f, 0.12f, 0.08f, 1f);
        private static readonly Color Floor = new Color(0.42f, 0.29f, 0.18f, 1f);
        private static readonly Color Wood = new Color(0.5f, 0.34f, 0.2f, 1f);
        private static readonly Color WoodDark = new Color(0.28f, 0.18f, 0.11f, 1f);

        public static Set EnsureRoom(string dir)
        {
            var s = new Set();
            s.left = P.Ensure(dir, "room_left", 1920, 1080, Left);
            s.center = P.Ensure(dir, "room_center", 1920, 1080, Center);
            s.right = P.Ensure(dir, "room_right", 1920, 1080, Right);
            return s;
        }

        /// <summary>壁（縦の板）と床。panelX は 3 枚の中での横の位置（板の継ぎ目をそろえる）</summary>
        private static Color Wall(float x, float y, float panelX)
        {
            float gx = x + 960f + panelX;
            float plank = Mathf.Repeat(gx, 160f);
            float seam = Mathf.Clamp01(1f - Mathf.Abs(plank - 1f) / 2.5f);
            float tone = 0.02f * Mathf.Sin(gx * 0.013f) + 0.015f * Mathf.Sin(y * 0.02f + gx * 0.001f);
            Color wall = new Color(WallA.r + tone, WallA.g + tone, WallA.b + tone * 0.5f, 1f);
            wall = Color.Lerp(wall, WallSeam, seam * 0.7f);
            // 腰板
            if (y < -200f && y > -360f) wall = Color.Lerp(wall, WoodDark, 0.35f);
            // 床（下）
            if (y < -360f)
            {
                float board = Mathf.Repeat(gx * 0.5f + y * 0.3f, 120f);
                Color f = Floor;
                f = Color.Lerp(f, WoodDark, Mathf.Clamp01(1f - Mathf.Abs(board - 1f) / 2f) * 0.5f);
                float depth = Mathf.Clamp01((-360f - y) / 180f);
                wall = Color.Lerp(f, f * 0.7f, depth);
                wall.a = 1f;
            }
            return wall;
        }

        private static Color Left(float x, float y)
        {
            Color c = Wall(x, y, 0f);
            // 入口の扉（左）
            float door = P.Box(x, y, -640f, -60f, 150f, 300f, 12f);
            if (door < 0f)
            {
                c = Color.Lerp(new Color(0.34f, 0.2f, 0.11f, 1f), WoodDark, Mathf.Repeat(x + 2000f, 60f) < 3f ? 0.6f : 0f);
                float window = P.Box(x, y, -640f, 120f, 70f, 70f, 30f);
                if (window < 0f) c = new Color(0.16f, 0.22f, 0.4f, 1f);
                float knob = P.Circle(x, y, -530f, -80f, 10f);
                c = Color.Lerp(c, new Color(0.85f, 0.7f, 0.35f, 1f), P.Fill(knob));
            }
            c = Color.Lerp(c, WoodDark, P.Stroke(door, 14f));
            // 看板
            float sign = P.Box(x, y, -640f, 340f, 120f, 36f, 8f);
            c = Color.Lerp(c, new Color(0.62f, 0.45f, 0.26f, 1f), P.Fill(sign));
            // カウンター（右）
            float top = P.Box(x, y, 520f, -130f, 400f, 18f, 6f);
            float front = P.Box(x, y, 520f, -260f, 380f, 120f, 6f);
            c = Color.Lerp(c, Wood * 0.85f, P.Fill(front));
            if (front < 0f && Mathf.Repeat(x + 2000f, 95f) < 3f) c = Color.Lerp(c, WoodDark, 0.5f);
            c = Color.Lerp(c, Wood * 1.15f, P.Fill(top));
            // 呼び鈴
            float bell = P.Min(P.Circle(x, y, 300f, -92f, 22f), P.Box(x, y, 300f, -110f, 30f, 6f, 3f));
            c = Color.Lerp(c, new Color(0.85f, 0.7f, 0.35f, 1f), P.Fill(bell));
            // 窓（上の右）
            float win = P.Box(x, y, 420f, 260f, 150f, 130f, 10f);
            if (win < 0f) c = Mathf.Min(Mathf.Abs(x - 420f), Mathf.Abs(y - 260f)) < 6f ? WoodDark : new Color(0.13f, 0.18f, 0.36f, 1f);
            c = Color.Lerp(c, WoodDark, P.Stroke(win, 14f));
            c.a = 1f;
            return c;
        }

        private static Color Center(float x, float y)
        {
            Color c = Wall(x, y, 1920f);
            // 窓（右上）と月
            float win = P.Box(x, y, 580f, 260f, 170f, 190f, 10f);
            if (win < 0f)
            {
                c = Mathf.Min(Mathf.Abs(x - 580f), Mathf.Abs(y - 260f)) < 6f ? WoodDark
                    : Color.Lerp(new Color(0.1f, 0.14f, 0.3f, 1f), new Color(0.22f, 0.3f, 0.5f, 1f), Mathf.Clamp01((y - 70f) / 380f));
                float moon = P.Circle(x, y, 650f, 360f, 34f);
                c = Color.Lerp(c, new Color(0.95f, 0.95f, 0.85f, 1f), P.Fill(moon));
            }
            c = Color.Lerp(c, WoodDark, P.Stroke(win, 16f));
            // 棚と壺（左上）
            float shelf = P.Box(x, y, -560f, 230f, 260f, 10f, 3f);
            c = Color.Lerp(c, new Color(0.3f, 0.2f, 0.12f, 1f), P.Fill(shelf));
            for (int i = 0; i < 4; i++)
            {
                float jar = P.Box(x, y, -740f + i * 120f, 290f, 34f, 48f, 14f);
                c = Color.Lerp(c, new Color(0.55f, 0.6f, 0.62f, 0.9f), P.Fill(jar) * 0.8f);
            }
            // 作業台（下）
            if (y < -300f && y > -560f)
            {
                Color table = Wood;
                float g = 0.04f * Mathf.Sin(x * 0.02f + Mathf.Sin(y * 0.05f) * 3f);
                table.r += g; table.g += g * 0.7f;
                if (y < -330f) table = Color.Lerp(table, WoodDark, 0.55f);
                c = table;
            }
            // 加熱台・フラスコ・トレイ（調合台の小さな絵）
            float heater = P.Box(x, y, 0f, -280f, 170f, 22f, 8f);
            c = Color.Lerp(c, new Color(0.24f, 0.21f, 0.2f, 1f), P.Fill(heater));
            float flask = P.Min(P.Circle(x, y, 0f, -170f, 80f), P.Box(x, y, 0f, -60f, 24f, 50f, 5f));
            c = Color.Lerp(c, new Color(0.82f, 0.92f, 1f, 1f), P.Fill(flask) * 0.18f);
            c = Color.Lerp(c, new Color(0.86f, 0.93f, 1f, 1f), P.Stroke(flask, 4f) * 0.9f);
            float knob = P.Circle(x, y, 230f, -280f, 32f);
            c = Color.Lerp(c, new Color(0.8f, 0.62f, 0.34f, 1f), P.Fill(knob));
            float tray = P.Box(x, y, 0f, -420f, 640f, 30f, 8f);
            c = Color.Lerp(c, new Color(0.45f, 0.3f, 0.18f, 1f), P.Fill(tray));
            c.a = 1f;
            return c;
        }

        private static Color Right(float x, float y)
        {
            Color c = Wall(x, y, 3840f);
            // 瓶の棚（3段）
            float frame = P.Box(x, y, -300f, 20f, 520f, 330f, 8f);
            c = Color.Lerp(c, WoodDark * 1.2f, P.Fill(frame) * 0.55f);
            c = Color.Lerp(c, WoodDark, P.Stroke(frame, 18f));
            for (int i = 0; i < 3; i++)
            {
                float board = P.Box(x, y, -300f, 250f - i * 250f, 510f, 12f, 3f);
                c = Color.Lerp(c, Wood, P.Fill(board));
            }
            // レシピ本の台（右）と開いた本
            float stand = P.Min(P.Box(x, y, 550f, -260f, 30f, 120f, 6f), P.Box(x, y, 550f, -380f, 90f, 14f, 6f));
            c = Color.Lerp(c, Wood * 0.9f, P.Fill(stand));
            float desk = P.Box(x, y, 550f, -120f, 130f, 22f, 6f);
            c = Color.Lerp(c, Wood, P.Fill(desk));
            float book = P.Min(P.Box(x, y, 500f, -82f, 62f, 16f, 6f), P.Box(x, y, 600f, -82f, 62f, 16f, 6f));
            c = Color.Lerp(c, new Color(0.95f, 0.88f, 0.72f, 1f), P.Fill(book));
            float cover = P.Box(x, y, 550f, -98f, 130f, 6f, 3f);
            c = Color.Lerp(c, new Color(0.45f, 0.16f, 0.12f, 1f), P.Fill(cover));
            // 小さな窓（右上）
            float win = P.Box(x, y, 620f, 300f, 100f, 110f, 40f);
            if (win < 0f) c = new Color(0.13f, 0.18f, 0.36f, 1f);
            c = Color.Lerp(c, WoodDark, P.Stroke(win, 12f));
            c.a = 1f;
            return c;
        }
    }
}
