using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>ゲームで使う音の一覧（アセット1つにまとめ、AudioManager が参照する）</summary>
    [CreateAssetMenu(fileName = "SoundLibrary", menuName = "Potion Frontier/サウンドライブラリ (Sound Library)", order = 10)]
    public class SoundLibrary : ScriptableObject
    {
        [Header("BGM・環境音")]
        public AudioClip bgmTitle;
        public AudioClip bgmAtelier;
        public AudioClip ambientRoom;
        public AudioClip ambientCauldron;

        [Header("UI")]
        public AudioClip uiClick;
        public AudioClip uiHover;
        public AudioClip uiOpen;
        public AudioClip uiClose;
        public AudioClip page;
        public AudioClip startGame;
        [Tooltip("日付のアイキャッチで、日付が浮かび上がるときの静かな音（se_date.wav）")] public AudioClip dateCard;

        [Header("調合")]
        public AudioClip pickup;
        public AudioClip putBack;
        public AudioClip pourLoop;
        public AudioClip plop;
        public AudioClip brewSuccess;
        public AudioClip brewSpecial;
        public AudioClip brewFail;
        public AudioClip discard;
        public AudioClip warning;
        public AudioClip shelfPlace;

        [Header("調合台の音（操作に合わせて鳴り方が変わる）")]
        [Tooltip("固体が液体に落ちる音（ランダムに選ぶ）")] public AudioClip[] dropSolid = new AudioClip[0];
        [Tooltip("粉を振り入れる音（ランダムに選ぶ）")] public AudioClip[] dropPowder = new AudioClip[0];
        [Tooltip("細く注ぐ音（ループ。速く注ぐときは「注ぐ音」と重ねる）")] public AudioClip pourThinLoop;
        [Tooltip("ガラス棒で液体を回す音（ループ。回す速さで高さと大きさが変わる）")] public AudioClip stirLoop;
        [Tooltip("ガラス棒がフラスコに当たる音（ランダムに選ぶ。回す速さで間隔と高さが変わる）")] public AudioClip[] stirTink = new AudioClip[0];
        [Tooltip("温まってきたときの、ぽこぽこという音（ループ）")] public AudioClip simmerLoop;
        [Tooltip("沸騰しているときの、ぐつぐつという音（ループ）")] public AudioClip boilLoop;
        [Tooltip("煮詰まって焦げているときの音（ループ）")] public AudioClip scorchLoop;
        [Tooltip("加熱台の炎の音（ループ。火加減で大きさが変わる）")] public AudioClip burnerLoop;
        [Tooltip("火をつけたときの音")] public AudioClip ignite;
        [Tooltip("火加減のつまみのカチカチ")] public AudioClip knobTick;
        [Tooltip("瓶に詰めて栓をする音")] public AudioClip corkPop;

        [Header("調合の手ごたえ（空ならコードで作った音）")]
        [Tooltip("瓶詰めの結果で ★ が1つ付くたびの「ちりん」（★の数だけ高くなる）")] public AudioClip starChime;
        [Tooltip("新しい液体を発見したときの音")] public AudioClip discovery;
        [Tooltip("新しい素材が調合机に届いたときの、手紙が届いたような控えめな音（se_letter.wav）")] public AudioClip ingredientArrived;
        [Tooltip("反応が起きた瞬間の音")] public AudioClip reactionSting;
        [Tooltip("焦げはじめた・目標から外れたなどの、やさしい注意の音")] public AudioClip softWarning;

        [Header("夜の環境音（空ならコードで作った静かな音）")]
        [Tooltip("ランプの火が静かに燃える音（ループ。調合机で、ずっと小さく鳴る）")] public AudioClip lampFireLoop;
        [Tooltip("お湯が温まると、泡でフラスコが台に当たる「コト」（いくつか入れるとランダムに選ぶ）")] public AudioClip[] kotoKnock = new AudioClip[0];
        [Tooltip("沸きはじめたフラスコのガラスが、軽く触れ合う「ちん」（空ならガラス棒の音を高く小さく使う）")] public AudioClip[] glassTouch = new AudioClip[0];
    }
}
