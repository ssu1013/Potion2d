using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// UIの絵とフォント（Assets/Art/UI/UISkin.asset）。
    /// 空の欄は、コードで描いた仮の絵（UIFactory）が使われる。自作の絵を入れると、その部分だけ差し替わる。
    /// 枠の絵（羊皮紙・木の板・ボタンなど）は、スプライトエディタで「Border（9スライス）」を設定すると、
    /// どんな大きさのウィンドウでも角が伸びずにきれいに表示される。
    /// </summary>
    [CreateAssetMenu(fileName = "UISkin", menuName = "Potion Frontier/UIスキン (UI Skin)", order = 12)]
    public class UISkin : ScriptableObject
    {
        [Header("フォント")]
        [Tooltip("日本語が表示できるフォント（.ttf / .otf）。空ならOSのフォント（製品版では必ず指定する）")]
        public Font font;
        [Tooltip("太字に使うフォント（同じ書体の Bold など）。空なら上のフォントを太らせて表示する")]
        public Font fontBold;

        [Header("枠・パネル（Assets/CustomAssets/Images/UI/Common。同じ名前の PNG で上書きすると差し替わる）")]
        [Tooltip("parchment.png：羊皮紙（ウィンドウ・カードの紙）")] public Sprite parchment;
        [Tooltip("wood_panel.png：木の板（棚の枠など）")] public Sprite woodPanel;
        [Tooltip("ボタン（空なら木の板）")] public Sprite button;
        [Tooltip("棚・素材箱のマス")] public Sprite slot;
        [Tooltip("選んでいるマス")] public Sprite slotSelected;

        [Header("アイコン・小物（Assets/CustomAssets/Images/UI/Common）")]
        [Tooltip("照準（3D 版の名残り。2D 版では使っていない）")] public Sprite crosshair;
        [Tooltip("knob.png：スライダーのつまみ")] public Sprite knob;
        [Tooltip("vignette.png：タイトル画面のふちの暗がり")] public Sprite vignette;
        [Tooltip("sparkle.png：タイトルのロゴのきらめき")] public Sprite sparkle;
        [Tooltip("soft_dot.png：ぼんやり光る丸（光の粒・影など）")] public Sprite softDot;

        [Header("発見・素材ノート（Assets/CustomAssets/Images/UI/Discovery。同じ名前の PNG で上書きすると差し替わる）")]
        [Tooltip("page.png：新しい反応を見つけたときのページ・書き足したお知らせの紙（9スライス推奨）")] public Sprite discoveryPage;
        [Tooltip("page_turn.png：ページめくりで、めくれていく紙（左の辺がとじ目。9スライス推奨）")] public Sprite pageTurn;
        [Tooltip("card.png：素材ノートの素材カード（9スライス推奨）")] public Sprite notebookCard;
        [Tooltip("unknown.png：まだ出会っていない素材の影絵（白で描くと暗い色で塗られる）")] public Sprite unknownMark;
        [Tooltip("stamp_new.png：新しい液体を発見")] public Sprite stampNew;
        [Tooltip("stamp_fact.png：素材の性質を発見")] public Sprite stampFact;
        [Tooltip("stamp_reaction.png：新しい反応を発見")] public Sprite stampReaction;
        [Tooltip("stamp_complete.png：素材ノート「すべて発見」")] public Sprite stampComplete;
        [Tooltip("letter.png：新しい素材が調合机に届いたときの、小さなお知らせの手紙")] public Sprite letter;
        [Header("日付のアイキャッチ（Assets/CustomAssets/Images/UI/DateCard。同じ名前の PNG で上書きすると差し替わる）")]
        [Tooltip("date_background.png：日付が浮かぶ画面いっぱいの背景（1920×1080）。イラストに差し替えられる。空なら暗い単色")] public Sprite dateBackground;
        [Tooltip("date_ornament.png：日付の下の飾りの線（512×32）。空なら細い線")] public Sprite dateOrnament;
        [Range(0f, 0.9f), Tooltip("背景の絵の上に重ねる暗がりの濃さ（明るいイラストに差し替えたとき、日付が読みにくければ上げる）")] public float dateShade = 0.25f;

        [Header("そのほか")]
        [Tooltip("スタンプの上に文字（新発見・新しい反応 など）を重ねる。スタンプの絵に文字を描き込んだらオフにする")] public bool drawStampLabels = true;
    }
}
