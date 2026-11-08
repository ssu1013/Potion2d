using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 2D のお客さん（QuestManager が来店のたびに作る）。相談データ（QuestData）の「立ち絵」があればそれを、
    /// なければ「見た目」（肌・髪・服の色、髪型、帽子、背丈）から仮の立ち絵をコードで描く。会話の顔も同じように作る。
    /// 立ち絵は足元が原点。クリックの当たり判定（BoxCollider2D）付き。
    /// </summary>
    public class Customer2D : MonoBehaviour
    {
        public enum MoodKind { None, Waiting, Happy, Sad }

        private const float Height = 2.6f; // ワールド単位（背丈 1.0 のとき）

        private static readonly Dictionary<string, Sprite> bodyCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> faceCache = new Dictionary<string, Texture2D>();

        private SpriteRenderer body;
        private float bob;
        private bool walking;
        private float facing = 1f;

        public QuestData Quest { get; private set; }
        public MoodKind Mood { get; private set; }
        /// <summary>会話の顔（DialogueRequest.portrait に渡す）</summary>
        public Texture Portrait { get; private set; }
        /// <summary>頭の上（「！」などの印を出す所）</summary>
        public Vector3 HeadWorld => transform.position + Vector3.up * (Height * HeightScale + 0.15f);

        private float HeightScale => Quest != null && Quest.Look != null ? Mathf.Clamp(Quest.Look.height, 0.75f, 1.2f) : 1f;

        public static Customer2D Create(QuestData quest, Vector3 position, Transform parent)
        {
            var go = new GameObject("Customer_" + (quest != null ? quest.ClientName : "?"));
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            var c = go.AddComponent<Customer2D>();
            c.Init(quest);
            return c;
        }

        private void Init(QuestData quest)
        {
            Quest = quest;
            var art = new GameObject("Art");
            art.transform.SetParent(transform, false);
            body = art.AddComponent<SpriteRenderer>();
            body.sortingLayerName = "Room";
            body.sortingOrder = 40;

            Sprite sprite = quest != null && quest.Portrait != null ? quest.Portrait : BodySprite(quest);
            body.sprite = sprite;
            // 足元を原点に、背丈に合わせた大きさにそろえる
            float h = Height * HeightScale;
            float w = Quest != null && Quest.Look != null ? Mathf.Clamp(Quest.Look.width, 0.8f, 1.3f) : 1f;
            Bounds b = sprite != null ? sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
            float s = h / Mathf.Max(0.01f, b.size.y);
            art.transform.localScale = new Vector3(s * w, s, 1f);
            art.transform.localPosition = new Vector3(-b.center.x * s * w, -b.min.y * s, 0f);

            var hit = gameObject.AddComponent<BoxCollider2D>();
            hit.size = new Vector2(Mathf.Max(0.8f, b.size.x * s * w), h);
            hit.offset = new Vector2(0f, h * 0.5f);

            Portrait = quest != null && quest.Portrait != null ? quest.Portrait.texture : FaceTexture(quest);
        }

        public void SetMood(MoodKind mood)
        {
            Mood = mood;
        }

        /// <summary>x 座標 targetX まで歩く（地面の高さはそのまま）</summary>
        public IEnumerator WalkTo(float targetX, float speed)
        {
            walking = true;
            while (this != null && Mathf.Abs(transform.position.x - targetX) > 0.02f)
            {
                Vector3 p = transform.position;
                facing = Mathf.Sign(targetX - p.x);
                p.x = Mathf.MoveTowards(p.x, targetX, speed * Time.deltaTime);
                transform.position = p;
                yield return null;
            }
            walking = false;
        }

        private void Update()
        {
            if (body == null) return;
            // 歩くときは弾む・止まっているときは小さく息をする
            bob += Time.deltaTime * (walking ? 10f : 2f);
            float y = walking ? Mathf.Abs(Mathf.Sin(bob)) * 0.08f : Mathf.Sin(bob) * 0.015f;
            body.flipX = facing < 0f;
            Transform t = body.transform;
            t.localPosition = new Vector3(t.localPosition.x, BaseY + y, 0f);
        }

        private float baseY = float.NaN;
        private float BaseY
        {
            get
            {
                if (float.IsNaN(baseY) && body != null) baseY = body.transform.localPosition.y;
                return float.IsNaN(baseY) ? 0f : baseY;
            }
        }

        // ------------------------------------------------------------------
        // 仮の絵（見た目の色から描く）
        // ------------------------------------------------------------------

        private static string Key(QuestData q) => q != null ? q.QuestId : "";

        private static Sprite BodySprite(QuestData q)
        {
            string key = Key(q);
            if (bodyCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            const int w = 160, h = 280;
            Texture2D tex = Draw(q != null ? q.Look : new CustomerLook(), w, h, false);
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0f), 100f);
            sprite.name = "CustomerBody_" + key;
            bodyCache[key] = sprite;
            return sprite;
        }

        private static Texture2D FaceTexture(QuestData q)
        {
            string key = Key(q);
            if (faceCache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
            Texture2D tex = Draw(q != null ? q.Look : new CustomerLook(), 192, 192, true);
            faceCache[key] = tex;
            return tex;
        }

        /// <summary>3頭身のちびキャラ（portrait = true なら顔のアップ）。座標は下が 0、上が 1</summary>
        private static Texture2D Draw(CustomerLook look, int w, int h, bool portrait)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            // 顔のアップは、頭のまわりだけを大きく描く
            float zoom = portrait ? 2.3f : 1f;
            float cy = portrait ? 0.72f : 0f;
            Color skin = look.skin, hair = look.hair, cloth = look.cloth, accent = look.accent;
            Color line = new Color(0.25f, 0.16f, 0.1f, 1f);
            Color bg = portrait ? new Color(0.93f, 0.86f, 0.7f, 1f) : new Color(0f, 0f, 0f, 0f);
            float aspect = (float)w / h;
            float pxSize = 1f / h / zoom;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 正規化した座標：横は -0.5〜0.5（縦の比で）、縦は 0〜1
                    float u = ((x + 0.5f) / w - 0.5f) * aspect / zoom;
                    float v = (y + 0.5f) / h / zoom + cy - (portrait ? 0.5f / zoom : 0f);
                    Color c = bg;

                    // 体（服）：台形っぽい丸い四角
                    float bodyD = RoundBox(u, v - 0.24f, 0.2f - (v - 0.05f) * 0.08f, 0.22f, 0.07f);
                    c = Paint(c, cloth, bodyD, pxSize);
                    // 襟・帯（差し色）
                    c = Paint(c, accent, RoundBox(u, v - 0.43f, 0.12f, 0.025f, 0.02f), pxSize);
                    // 足
                    c = Paint(c, Darker(cloth, 0.6f), RoundBox(u - 0.08f, v - 0.03f, 0.05f, 0.04f, 0.02f), pxSize);
                    c = Paint(c, Darker(cloth, 0.6f), RoundBox(u + 0.08f, v - 0.03f, 0.05f, 0.04f, 0.02f), pxSize);
                    // 手
                    c = Paint(c, skin, Circle(u - 0.22f, v - 0.25f, 0.045f), pxSize);
                    c = Paint(c, skin, Circle(u + 0.22f, v - 0.25f, 0.045f), pxSize);

                    // 髪（後ろ）
                    float headY = 0.68f;
                    switch (look.hairStyle)
                    {
                        case HairStyle.Long:
                            c = Paint(c, hair, RoundBox(u, v - (headY - 0.12f), 0.2f, 0.2f, 0.12f), pxSize);
                            break;
                        case HairStyle.TwinTails:
                            c = Paint(c, hair, Circle(u - 0.22f, v - (headY - 0.08f), 0.08f), pxSize);
                            c = Paint(c, hair, Circle(u + 0.22f, v - (headY - 0.08f), 0.08f), pxSize);
                            break;
                        case HairStyle.Bun:
                            c = Paint(c, hair, Circle(u, v - (headY + 0.2f), 0.07f), pxSize);
                            break;
                    }
                    // 頭
                    float headD = Circle(u, v - headY, 0.19f);
                    c = Paint(c, skin, headD, pxSize);
                    // 髪（前）
                    if (look.hairStyle != HairStyle.Bald)
                    {
                        float fringe = Mathf.Max(Circle(u, v - (headY + 0.04f), 0.195f), -(v - (headY + 0.07f + (look.hairStyle == HairStyle.Bob ? -0.03f : 0f))));
                        c = Paint(c, hair, fringe, pxSize);
                        if (look.hairStyle == HairStyle.Bob || look.hairStyle == HairStyle.Long)
                        {
                            c = Paint(c, hair, RoundBox(u - 0.17f, v - (headY - 0.02f), 0.04f, 0.1f, 0.04f), pxSize);
                            c = Paint(c, hair, RoundBox(u + 0.17f, v - (headY - 0.02f), 0.04f, 0.1f, 0.04f), pxSize);
                        }
                    }
                    // 目・口・ほっぺ
                    c = Paint(c, line, Circle(u - 0.07f, v - (headY - 0.01f), 0.022f), pxSize);
                    c = Paint(c, line, Circle(u + 0.07f, v - (headY - 0.01f), 0.022f), pxSize);
                    c = Paint(c, new Color(1f, 0.6f, 0.55f, 0.5f), Circle(u - 0.12f, v - (headY - 0.06f), 0.03f), pxSize);
                    c = Paint(c, new Color(1f, 0.6f, 0.55f, 0.5f), Circle(u + 0.12f, v - (headY - 0.06f), 0.03f), pxSize);
                    c = Paint(c, line, RoundBox(u, v - (headY - 0.08f), 0.025f, 0.006f, 0.006f), pxSize);
                    if (look.beard) c = Paint(c, hair, RoundBox(u, v - (headY - 0.13f), 0.11f, 0.05f, 0.05f), pxSize);

                    // 帽子
                    switch (look.hatStyle)
                    {
                        case HatStyle.Cap:
                        case HatStyle.Beret:
                            c = Paint(c, accent, RoundBox(u, v - (headY + 0.16f), 0.18f, 0.06f, 0.06f), pxSize);
                            break;
                        case HatStyle.StrawHat:
                            c = Paint(c, new Color(0.92f, 0.8f, 0.45f, 1f), RoundBox(u, v - (headY + 0.12f), 0.27f, 0.025f, 0.02f), pxSize);
                            c = Paint(c, new Color(0.92f, 0.8f, 0.45f, 1f), RoundBox(u, v - (headY + 0.18f), 0.15f, 0.06f, 0.05f), pxSize);
                            break;
                        case HatStyle.Pointy:
                            float cone = Mathf.Max(Mathf.Abs(u) - (headY + 0.3f - v) * 0.6f, -(v - (headY + 0.12f)));
                            c = Paint(c, accent, Mathf.Max(cone, v - (headY + 0.3f)), pxSize);
                            break;
                        case HatStyle.Scarf:
                        case HatStyle.Bandana:
                        case HatStyle.Hood:
                            c = Paint(c, accent, Mathf.Max(Circle(u, v - (headY + 0.05f), 0.215f), -(v - (headY + 0.09f))), pxSize);
                            break;
                    }
                    // 輪郭（頭・体）
                    float outline = Mathf.Min(Mathf.Abs(headD), Mathf.Abs(bodyD));
                    c = Color.Lerp(c, line, Mathf.Clamp01(1f - outline / (pxSize * 2f)) * 0.7f * (c.a > 0.01f ? 1f : 0f));
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Color Paint(Color under, Color color, float d, float pxSize)
        {
            float a = Mathf.Clamp01(0.5f - d / pxSize) * color.a;
            if (a <= 0f) return under;
            float outA = a + under.a * (1f - a);
            if (outA <= 1e-5f) return under;
            Color c = (color * a + under * under.a * (1f - a)) / outA;
            c.a = outA;
            return c;
        }

        private static Color Darker(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static float Circle(float x, float y, float r) => Mathf.Sqrt(x * x + y * y) - r;

        private static float RoundBox(float x, float y, float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x) - hx + radius;
            float qy = Mathf.Abs(y) - hy + radius;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }
}
