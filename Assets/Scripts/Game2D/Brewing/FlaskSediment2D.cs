using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 溶け残り（Step4-B）：入れた固体・粉は、液面からゆっくり沈んで、溶けきるまでフラスコの底に見える。
    /// 溶けるほど小さく・少なくなり、溶けるときに素材の色の筋がふわっと立ちのぼる（かき混ぜるほどたくさん）。
    /// かき混ぜている間は、渦に巻き上げられて底で踊る。
    /// FlaskEffects2D が起動時に自分に付ける。絵は BrewArt2D の「落とす欠片（dropPiece）」と「湯気（steam。色の筋に使う）」。
    /// </summary>
    public class FlaskSediment2D : MonoBehaviour
    {
        [SerializeField, Range(1, 4), Tooltip("固体1回分（1単位）を、底に何個の欠片で見せるか")] private int piecesPerUnit = 2;
        [SerializeField, Range(4, 48), Tooltip("底に見せる欠片の数の上限")] private int maxPieces = 28;
        [SerializeField, Min(0.05f), Tooltip("欠片の大きさ（ワールド単位）")] private float pieceSize = 0.24f;
        [SerializeField, Range(0f, 0.5f), Tooltip("底に散らばる幅（液体の幅に対する割合）")] private float spread = 0.2f;
        [SerializeField, Range(0f, 3f), Tooltip("溶けるときの色の筋の多さ")] private float inkAmount = 1f;
        [SerializeField, Min(0.1f), Tooltip("液面から底まで沈むのにかかる秒数")] private float sinkSeconds = 0.7f;

        private sealed class Track
        {
            public float seen;
            public float lastUndissolved;
        }

        private BrewSession2D session;
        private Flask2D flask;
        private FlaskEffects2D effects;
        private BrewArt2D art;
        private ParticleSystem ink;
        private readonly List<FlaskSimulation.UndissolvedPiece> left = new List<FlaskSimulation.UndissolvedPiece>();
        private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        private readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        private readonly List<int> stale = new List<int>();
        private readonly List<Vector3> inkFrom = new List<Vector3>();
        private float pollTimer;
        private float inkAccum;
        private Color inkColor = Color.white;
        private Transform root;

        /// <summary>今、底に見えている欠片の数（自動テスト用）</summary>
        public int VisiblePieces { get; private set; }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            flask = GetComponent<Flask2D>() != null ? GetComponent<Flask2D>() : FindFirstObjectByType<Flask2D>();
            effects = GetComponent<FlaskEffects2D>();
            art = session != null ? session.Art : null;
            root = new GameObject("Sediment").transform;
            root.SetParent(transform, false);
            if (effects != null) ink = effects.CreateExtraSystem("Ink", art != null ? art.steam : null, "Liquid", 2, true, -0.02f, 240, true, 0.35f, 1.6f);
        }

        private void LateUpdate()
        {
            if (session == null || flask == null || session.Flask == null) return;
            FlaskSimulation sim = session.Flask;
            float dt = Time.deltaTime;

            pollTimer -= dt;
            if (pollTimer <= 0f)
            {
                pollTimer = 0.1f;
                sim.GetUndissolved(left);
                UpdateTracks();
            }
            Layout();
            EmitInk(dt);
        }

        /// <summary>溶けた量（前に見たときとの差）から、色の筋の出る量を決める</summary>
        private void UpdateTracks()
        {
            float dissolved = 0f;
            Color mixed = Color.black;
            foreach (FlaskSimulation.UndissolvedPiece p in left)
            {
                if (!tracks.TryGetValue(p.order, out Track t))
                {
                    tracks[p.order] = t = new Track { seen = Time.time, lastUndissolved = p.undissolved };
                    continue;
                }
                float d = t.lastUndissolved - p.undissolved;
                t.lastUndissolved = p.undissolved;
                if (d <= 0f) continue;
                dissolved += d;
                mixed += p.color * d;
            }
            // 溶けきって一覧から消えた分も、最後に少し筋を出す
            stale.Clear();
            foreach (KeyValuePair<int, Track> kv in tracks)
            {
                bool present = false;
                foreach (FlaskSimulation.UndissolvedPiece p in left) if (p.order == kv.Key) { present = true; break; }
                if (!present) stale.Add(kv.Key);
            }
            foreach (int key in stale)
            {
                dissolved += tracks[key].lastUndissolved;
                tracks.Remove(key);
            }
            if (dissolved > 0f && session.HasLiquid)
            {
                if (mixed.maxColorComponent > 0f) inkColor = mixed / Mathf.Max(0.0001f, dissolved);
                inkAccum += dissolved * 45f * inkAmount;
            }
        }

        private void Layout()
        {
            Vector3 bottom = flask.BottomWorld;
            Vector3 surface = flask.SurfaceWorld;
            float width = flask.LiquidWidth * spread;
            float swirl = effects != null ? effects.SwirlStrength : 0f;
            float time = Time.time;
            int used = 0;
            inkFrom.Clear();
            foreach (FlaskSimulation.UndissolvedPiece p in left)
            {
                if (!tracks.TryGetValue(p.order, out Track t)) continue;
                float age = time - t.seen;
                if (age < 0.3f) continue; // 落ちてくる途中（Flask2D の欠片が液面に着くまで）
                float frac = Mathf.Clamp01(p.undissolved / Mathf.Max(0.001f, p.units));
                int count = Mathf.Clamp(Mathf.CeilToInt(p.units * piecesPerUnit * (p.powder ? 1.5f : 1f)), 1, 6);
                int visible = Mathf.Clamp(Mathf.CeilToInt(count * frac), 1, count);
                float size = pieceSize * (p.powder ? 0.55f : 1f) * Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(frac));
                float sink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - 0.3f) / sinkSeconds));
                // 素材ごとの欠片の絵があれば、それをそのままの色で（なければ共通の欠片を素材の色で塗る）
                Sprite own = p.ingredient != null ? p.ingredient.PieceSprite : null;
                Color color = own != null ? Color.white : Color.Lerp(p.color, Color.black, 0.12f);
                color.a = 1f;
                for (int k = 0; k < visible && used < maxPieces; k++)
                {
                    int seed = p.order * 97 + k * 13;
                    float x = (Hash01(seed) - 0.5f) * 2f * width;
                    float rest = bottom.y + size * 0.35f + Mathf.Abs(x) * 0.35f + Hash01(seed + 5) * 0.04f;
                    float phase = Hash01(seed + 9) * 6.28f;
                    // かき混ぜると渦に巻き上げられて、底で踊る
                    x += Mathf.Sin(time * (2.2f + Hash01(seed + 3)) + phase) * width * 0.6f * swirl;
                    float lift = Mathf.Abs(Mathf.Sin(time * 1.7f + phase)) * 0.25f * swirl;
                    float y = Mathf.Lerp(surface.y, rest + lift, sink);
                    SpriteRenderer sr = Get(used++);
                    Sprite sprite = own != null ? own : (art != null ? art.dropPiece : null);
                    if (sr.sprite != sprite) sr.sprite = sprite;
                    sr.transform.position = new Vector3(bottom.x + x, y, 0f);
                    sr.transform.localScale = Vector3.one * Scale(size, sprite);
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, Hash01(seed + 1) * 360f + time * 120f * swirl);
                    sr.color = color;
                    if (frac < 0.999f) inkFrom.Add(sr.transform.position);
                }
            }
            for (int i = used; i < pool.Count; i++)
            {
                if (pool[i].enabled) pool[i].enabled = false;
            }
            VisiblePieces = used;
        }

        private void EmitInk(float dt)
        {
            if (ink == null || inkAccum < 1f) return;
            if (inkFrom.Count == 0)
            {
                inkAccum = 0f;
                return;
            }
            float swirl = effects != null ? effects.SwirlStrength : 0f;
            Color c = Color.Lerp(inkColor, Color.white, 0.1f);
            c.a = 0.32f;
            int n = Mathf.Min(8, Mathf.FloorToInt(inkAccum));
            inkAccum -= n;
            inkAccum = Mathf.Min(inkAccum, 20f);
            for (int i = 0; i < n; i++)
            {
                Vector3 from = inkFrom[Random.Range(0, inkFrom.Count)];
                var v = new Vector3(Random.Range(-0.15f, 0.15f) + swirl * Random.Range(-0.6f, 0.6f), Random.Range(0.15f, 0.45f), 0f);
                FlaskEffects2D.EmitParticle(ink, from, v, Random.Range(0.2f, 0.35f), Random.Range(1.4f, 2.4f), c);
            }
        }

        private SpriteRenderer Get(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Piece_" + pool.Count);
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = art != null ? art.dropPiece : null;
                sr.sortingLayerName = "Liquid";
                sr.sortingOrder = 2;
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                sr.enabled = false;
                pool.Add(sr);
            }
            SpriteRenderer r = pool[index];
            if (!r.enabled) r.enabled = true;
            return r;
        }

        private static float Scale(float worldSize, Sprite s)
        {
            return s != null ? worldSize / Mathf.Max(0.01f, Mathf.Max(s.bounds.size.x, s.bounds.size.y)) : worldSize;
        }

        private static float Hash01(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
