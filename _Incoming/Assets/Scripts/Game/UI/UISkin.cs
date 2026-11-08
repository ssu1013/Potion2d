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

        [Header("枠・パネル（9スライス推奨）")]
        public Sprite parchment;
        public Sprite woodPanel;
        [Tooltip("ボタン（空なら木の板）")] public Sprite button;
        [Tooltip("棚・素材箱のマス")] public Sprite slot;
        [Tooltip("選んでいるマス")] public Sprite slotSelected;

        [Header("アイコン・小物")]
        public Sprite crosshair;
        public Sprite coin;
        [Tooltip("スライダーのつまみ")] public Sprite knob;
        public Sprite vignette;
        public Sprite sparkle;
        [Tooltip("ぼんやり光る丸（光の粒など）")] public Sprite softDot;
    }
}
