using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 素材トレイのいちばん下の「空き瓶」（Tray の Prefab の子「BottleStation」に付ける）。
    /// クリックすると、フラスコの中身を全部瓶に詰める（BrewInput2D が判定して BrewSession2D.BottleAll を呼ぶ）。
    /// ボタンや文字は使わない：フラスコに液体があるときは、瓶の中にその色がうっすら見えて、ゆっくり明るくなったり暗くなったりする。
    /// 液体がないときは、瓶が暗く沈む。
    ///
    /// 絵を差し替えるところ：
    ///   Tile … 下地のマス（CustomAssets/Images/Workbench/tray_tile.png）
    ///   Art/Body・Art/Content … 瓶（空なら調合画面の絵の一覧 BrewArt2D の「自作の液体の瓶」= CustomAssets/Images/Containers/potion_bottle.png）
    /// </summary>
    public class BottleStation2D : MonoBehaviour
    {
        [SerializeField, Tooltip("下地のマス")] private SpriteRenderer tile;
        [SerializeField, Tooltip("瓶の絵（空なら BrewArt2D の自作の液体の瓶）")] private SpriteRenderer body;
        [SerializeField, Tooltip("瓶の中身（白い形。フラスコの液体の色で塗られる）")] private SpriteRenderer content;
        [SerializeField, Tooltip("Body と Content をまとめた子（大きさをそろえるのに使う）")] private Transform art;
        [SerializeField, Min(0.1f), Tooltip("瓶の絵の高さ（ワールド単位）")] private float height = 0.8f;

        [Header("色")]
        [SerializeField] private Color tileColor = new Color(0.9f, 0.82f, 0.68f, 1f);
        [SerializeField] private Color tileHoverColor = new Color(1f, 0.93f, 0.72f, 1f);
        [SerializeField, Tooltip("液体がないとき（詰められないとき）の暗さ")] private Color idleTint = new Color(0.55f, 0.55f, 0.6f, 0.7f);

        private BrewSession2D session;
        private Flask2D flask;
        private Vector3 baseScale = Vector3.one;
        private bool hovered;
        private float highlight;
        private float ready;
        private float time;
        private bool artReady;

        /// <summary>今、瓶に詰められる（フラスコに液体がある）</summary>
        public bool Ready => session != null && session.HasLiquid;

        private void Awake()
        {
            baseScale = transform.localScale;
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            flask = FindFirstObjectByType<Flask2D>();
            SetupArt();
        }

        private void SetupArt()
        {
            if (artReady || session == null) return;
            BrewArt2D set = session.Art;
            if (set != null)
            {
                if (body != null && body.sprite == null) body.sprite = set.potionBottle.body;
                if (content != null && content.sprite == null) content.sprite = set.potionBottle.content;
            }
            Sprite reference = body != null && body.sprite != null ? body.sprite : (content != null ? content.sprite : null);
            if (art != null && reference != null) ArtFit2D.Fit(art, reference, height, height);
            artReady = true;
        }

        public void SetHovered(bool on)
        {
            hovered = on;
        }

        private void Update()
        {
            if (!artReady) SetupArt();
            float dt = Time.deltaTime;
            time += dt;
            highlight = Mathf.MoveTowards(highlight, hovered ? 1f : 0f, dt * 8f);
            ready = Mathf.MoveTowards(ready, Ready ? 1f : 0f, dt * 3f);
            transform.localScale = baseScale * (1f + highlight * 0.08f);

            // 詰められるときは、ゆっくり息をするように明るくなる
            float breathe = 0.5f + 0.5f * Mathf.Sin(time * 2.2f);
            Color lit = Color.Lerp(idleTint, Color.white, ready);
            if (tile != null) tile.color = Color.Lerp(tileColor, tileHoverColor, Mathf.Max(highlight, ready * breathe * 0.5f)) * new Color(lit.r, lit.g, lit.b, 1f);
            if (body != null) body.color = lit;
            if (content != null)
            {
                Color c = flask != null ? flask.ShownColor : Color.white;
                c.a = ready * (0.45f + 0.25f * breathe);
                content.color = c;
                content.enabled = content.sprite != null && c.a > 0.01f;
            }
        }
    }
}
