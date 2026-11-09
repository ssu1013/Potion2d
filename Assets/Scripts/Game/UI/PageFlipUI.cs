using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 紙が1枚めくれていく演出（図鑑の「新発見！」ページ・素材ノートのページ送り）。
    /// めくりたい物（結果カード・ページ・カードの並び）の上に紙を1枚かぶせ、とじ目を軸に裏返して、下の中身を見せる。
    /// 紙の絵は UISkin の page_turn.png（Assets/CustomAssets/Images/UI/Discovery/）。空なら page.png → 羊皮紙。
    /// </summary>
    public sealed class PageFlipUI
    {
        public const float DefaultSeconds = 0.6f;
        private const float FadeSeconds = 0.18f;

        private readonly RectTransform leaf;
        private readonly Image paper;
        private readonly Image shade;
        private float age = -1f;
        private float seconds = DefaultSeconds;
        private bool rightToLeft = true;

        /// <summary>めくっている最中か</summary>
        public bool Playing => age >= 0f;
        /// <summary>これまでにめくった回数（自動テスト用）</summary>
        public int PlayCount { get; private set; }
        /// <summary>0（めくりはじめ）〜1（めくり終わり）。めくっていないときは 1</summary>
        public float Progress => age < 0f ? 1f : Mathf.Clamp01(age / seconds);

        public PageFlipUI(RectTransform target, string name = "PageTurn")
        {
            leaf = UIFactory.CreateRect(name, target);
            UIFactory.Stretch(leaf);
            paper = UIFactory.CreateImage(leaf, "Paper", LeafSprite(), Color.white);
            UIFactory.Stretch(paper.rectTransform);
            // めくれて立っているあいだの陰（真横になるほど暗く、裏側は少し暗い）
            shade = UIFactory.CreateImage(leaf, "Shade", paper.sprite, new Color(0.16f, 0.09f, 0.04f, 0f));
            UIFactory.Stretch(shade.rectTransform);
            leaf.gameObject.SetActive(false);
        }

        /// <summary>めくる。rightToLeft = 右から左へ（本を読み進める向き）。false なら左から右へ（戻る）</summary>
        public void Play(bool rightToLeft = true, float seconds = DefaultSeconds)
        {
            this.rightToLeft = rightToLeft;
            this.seconds = Mathf.Max(0.1f, seconds);
            age = 0f;
            PlayCount++;
            Sprite s = LeafSprite();
            if (s != paper.sprite)
            {
                paper.sprite = s;
                shade.sprite = s;
                paper.type = shade.type = s != null && s.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            }
            // とじ目（回る軸）：右から左へめくるなら左の辺
            leaf.pivot = new Vector2(rightToLeft ? 0f : 1f, 0.5f);
            leaf.gameObject.SetActive(true);
            leaf.SetAsLastSibling();
            Apply();
        }

        public void Stop()
        {
            age = -1f;
            leaf.localScale = Vector3.one;
            leaf.localEulerAngles = Vector3.zero;
            leaf.gameObject.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (age < 0f) return;
            age += dt;
            if (age >= seconds + FadeSeconds)
            {
                Stop();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            float u = Mathf.Clamp01(age / seconds);
            float e = u * u * (3f - 2f * u);               // ゆっくり持ち上げて、さっと倒す
            float sx = Mathf.Cos(e * Mathf.PI);             // 1（かぶさっている）→ -1（向こう側に倒れた）
            float lift = Mathf.Sin(e * Mathf.PI);           // 立ち上がっている度合い
            leaf.localScale = new Vector3(sx, 1f + 0.06f * lift, 1f);
            leaf.localEulerAngles = new Vector3(0f, 0f, (rightToLeft ? 2.5f : -2.5f) * lift);

            bool back = sx < 0f;
            float alpha = 1f;
            if (back) alpha = Mathf.Clamp01(1f - (-sx - 0.35f) / 0.65f);   // 向こう側に倒れながら消える
            if (age > seconds) alpha *= Mathf.Clamp01(1f - (age - seconds) / FadeSeconds);
            float dark = 0.38f * (1f - Mathf.Abs(sx)) + (back ? 0.14f : 0f);
            paper.color = new Color(1f, 1f, 1f, alpha);
            shade.color = new Color(0.16f, 0.09f, 0.04f, dark * alpha);
        }

        private static Sprite LeafSprite()
        {
            UISkin skin = UIFactory.Skin;
            if (skin != null)
            {
                if (skin.pageTurn != null) return skin.pageTurn;
                if (skin.discoveryPage != null) return skin.discoveryPage;
            }
            return UIFactory.Parchment();
        }
    }
}
