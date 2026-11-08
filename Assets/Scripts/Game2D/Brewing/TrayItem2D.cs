using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合トレイに並ぶ1つの物（TrayItem の Prefab のルートに付ける）。絵は並ぶ物で自動で入れ替わる
    /// （素材データのアイコン、なければ BrewArt2D の容器の絵）。クリックで手に持つ（BrewInput2D が判定する）。
    /// </summary>
    public class TrayItem2D : MonoBehaviour
    {
        [SerializeField, Tooltip("容器の絵（自動で入れ替わる）")] private SpriteRenderer body;
        [SerializeField, Tooltip("中身（白い形を色で塗る。自動で入れ替わる）")] private SpriteRenderer content;
        [SerializeField, Tooltip("Body と Content をまとめた子（大きさをそろえるのに使う）")] private Transform art;
        [SerializeField, Tooltip("クリックの当たり判定（絵の大きさに合わせて自動で変わる）")] private BoxCollider2D hitBox;
        [SerializeField, Min(0.1f), Tooltip("トレイに並べたときの高さ（ワールド単位）")] private float height = 0.95f;
        [SerializeField, Min(0.1f)] private float maxWidth = 1.05f;

        private Vector3 baseScale = Vector3.one;
        private float highlight;
        private bool hovered;

        public TrayEntry2D Entry { get; private set; }

        private void Awake()
        {
            baseScale = transform.localScale;
        }

        public void Set(TrayEntry2D entry, BrewArt2D artSet)
        {
            Entry = entry;
            Sprite bodySprite = null, contentSprite = null;
            Color color = Color.white;
            if (artSet != null) artSet.Resolve(entry.ingredient, entry.potion, out bodySprite, out contentSprite, out color);
            if (body != null)
            {
                body.sprite = bodySprite;
                body.enabled = bodySprite != null;
            }
            if (content != null)
            {
                content.sprite = contentSprite;
                color.a = 0.92f;
                content.color = color;
                content.enabled = contentSprite != null;
            }
            Vector2 size = ArtFit2D.Fit(art, bodySprite != null ? bodySprite : contentSprite, height, maxWidth);
            if (hitBox != null)
            {
                hitBox.size = new Vector2(Mathf.Max(0.4f, size.x), Mathf.Max(0.4f, size.y));
                hitBox.offset = Vector2.zero;
            }
        }

        public void SetHovered(bool on)
        {
            hovered = on;
        }

        private void Update()
        {
            highlight = Mathf.MoveTowards(highlight, hovered ? 1f : 0f, Time.deltaTime * 8f);
            transform.localScale = baseScale * (1f + highlight * 0.1f);
        }
    }
}
