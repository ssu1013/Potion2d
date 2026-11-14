using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D 調合画面の文字（BrewHUD の Prefab のルートに付ける）。画面に出す文字は、できるだけ少なくしてある：
    ///   ・上の真ん中：フラスコの中身の名前（液体があるときだけ。その下に分類と ★）
    ///   ・カーソルのそば：指している素材の名前と、素材ノートで見つけた性質（素材トレイ・空き瓶を指したときだけ）
    ///   ・上の真ん中の少し下：短いお知らせ（数秒で消える）
    /// 操作の説明・工程・見立て・ボタンは出さない（つまみ・空き瓶・素材トレイなど、机の上の物を直接さわって遊ぶ）。
    /// 見た目は Prefab の中の Image・Text を直接いじってよい。
    /// 日本語のフォントは「フォント」欄に入れる。空なら UISkin のフォント → Windows の日本語フォント（Yu Gothic UI など）を使う
    /// （OS のフォントは製品版では使えないので、Noto Sans JP・M PLUS Rounded などの商用可のフォントに差し替える）。
    /// </summary>
    public class BrewHUD2D : MonoBehaviour
    {
        /// <summary>Step11（画面の断捨離）の配置の版。これより古い Prefab なら、メニュー「Step11」で新しくする</summary>
        public const int MinimalLayout = 11;

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private BrewSession2D session;
        [SerializeField] private BrewInput2D input;

        [Header("フォント")]
        [SerializeField, Tooltip("日本語のフォント（空なら UISkin のフォント → それもなければ OS のフォント）")] private Font font;
        [SerializeField, Tooltip("太字の文字に使うフォント（空なら UISkin の太字 → それもなければ上のフォントを太らせる）")] private Font boldFont;
        [SerializeField] private string[] osFontNames = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Hiragino Sans" };
        [SerializeField, HideInInspector, Tooltip("画面の配置の版（Step11 のメニューで 11 になる）")] private int layoutVersion;

        /// <summary>画面の配置の版（11 = Step11 の配置：見立て・工程・ボタンなし）</summary>
        public int LayoutVersion => layoutVersion;

        [Header("文字")]
        [SerializeField, Tooltip("フラスコの中身の名前（液体があるときだけ出る）")] private Text liquidTitle;
        [SerializeField, Tooltip("分類と ★（液体があるときだけ出る）")] private Text liquidDetail;
        [SerializeField, Tooltip("名前の後ろの板（液体があるときだけ出る。空なら名前の親の Image）")] private Image infoBackground;
        [SerializeField, Tooltip("カーソルのそばの説明")] private RectTransform tooltipPanel;
        [SerializeField] private Text tooltipText;
        [SerializeField, Tooltip("短いお知らせ")] private Text toast;

        private float infoTimer;
        private float toastTimer;
        private float toastAge;
        private Canvas canvas;
        private bool infoBackgroundSearched;
        private CanvasGroup tooltipGroup;
        private string tooltipTitle;
        private string tooltipSubtitle;
        private const float ToastSeconds = 3f;
        private const float ToastFadeIn = 0.15f;
        private const float ToastFadeOut = 0.5f;

        private void Awake()
        {
            // 画面の比率が 16:9 でないときも、UI を絵と同じ範囲に収める（子を「Safe」の下へ移す）
            if (GetComponent<Canvas>() != null && GetComponent<ViewportFit2D>() == null) gameObject.AddComponent<ViewportFit2D>();
            // 調合台の手ごたえ（温度計・演出・結果カード）。Prefab になければ付ける
            if (GetComponent<BrewFeedback2D>() == null) gameObject.AddComponent<BrewFeedback2D>();

            UISkin skin = UIFactory.Skin;
            if (font == null && skin != null) font = skin.font;
            if (boldFont == null && skin != null) boldFont = skin.fontBold;
            if (font == null) font = Font.CreateDynamicFontFromOSFont(osFontNames, 20);
            if (font != null)
            {
                foreach (Text t in GetComponentsInChildren<Text>(true))
                {
                    if (boldFont != null && t.fontStyle == FontStyle.Bold)
                    {
                        t.font = boldFont;
                        t.fontStyle = FontStyle.Normal;
                    }
                    else t.font = font;
                }
            }
        }

        private void Start()
        {
            if (session == null) session = FindFirstObjectByType<BrewSession2D>();
            if (input == null) input = FindFirstObjectByType<BrewInput2D>();
            if (session != null)
            {
                session.Message += ShowToast;
                session.Bottled += OnBottled;
            }
            if (toast != null) toast.text = "";
            if (tooltipPanel != null)
            {
                // 説明の札は、ふわっと出す（クリックは邪魔しない）
                tooltipGroup = tooltipPanel.GetComponent<CanvasGroup>();
                if (tooltipGroup == null) tooltipGroup = tooltipPanel.gameObject.AddComponent<CanvasGroup>();
                tooltipGroup.blocksRaycasts = false;
                tooltipGroup.interactable = false;
                tooltipPanel.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (session == null) return;
            session.Message -= ShowToast;
            session.Bottled -= OnBottled;
        }

        private void Update()
        {
            if (session == null) return;
            // タイトルの間は隠す（1画面なので、遊んでいる間はずっと出ている）
            if (canvas == null) canvas = GetComponent<Canvas>();
            GameFlow flow = GameFlow.Instance;
            bool show = flow == null || flow.Mode == GameMode.Playing;
            if (canvas != null && canvas.enabled != show) canvas.enabled = show;
            if (!show) return;
            float dt = Time.deltaTime;
            infoTimer -= dt;
            if (infoTimer <= 0f)
            {
                infoTimer = 0.2f;
                RefreshInfo();
            }
            UpdateTooltip();
            if (toast != null && toastTimer > 0f)
            {
                toastTimer -= dt;
                toastAge += dt;
                Color c = toast.color;
                // すっと現れて、ゆっくり消える
                c.a = Mathf.Clamp01(Mathf.Min(toastAge / ToastFadeIn, toastTimer / ToastFadeOut));
                toast.color = c;
            }
        }

        public void ShowToast(string text)
        {
            if (toast == null) return;
            // 出ている途中に次のお知らせが来たときは、消えずにそのまま文字だけ変える（ちらつかないように）
            bool visible = toastTimer > 0f && toast.color.a > 0.5f;
            toast.text = text;
            toastAge = visible ? ToastFadeIn : 0f;
            toastTimer = ToastSeconds;
            Color c = toast.color;
            c.a = visible ? 1f : 0f;
            toast.color = c;
        }

        private void OnBottled(LiquidRecord record, bool isNew, float ml, int bottles)
        {
            if (record == null) return;
            // 結果カードを出すときは、文字のお知らせは出さない
            BrewFeedback2D feedback = GetComponent<BrewFeedback2D>();
            if (feedback != null && feedback.ShowsBottleResult) return;
            string head = isNew ? "新しい液体を発見！　" : "";
            ShowToast($"{head}{record.DisplayName}を {Mathf.RoundToInt(ml)}ml（{bottles}本）瓶に詰めた");
        }

        /// <summary>フラスコの中身の名前（液体がないとき・空っぽのときは何も出さない）</summary>
        private void RefreshInfo()
        {
            LiquidRecord preview = session.Flask != null ? session.Preview : null;
            string title = preview != null ? preview.DisplayName : "";
            string detail = "";
            if (preview != null)
            {
                detail = KindLabel(preview.kind);
                if (preview.stars > 0) detail += "　" + new string('★', Mathf.Clamp(preview.stars, 0, 5));
            }
            if (liquidTitle != null && liquidTitle.text != title) liquidTitle.text = title;
            if (liquidDetail != null && liquidDetail.text != detail) liquidDetail.text = detail;
            // 名前がないときは、後ろの板も出さない（空っぽの黒い板が残らないように）
            if (infoBackground == null && !infoBackgroundSearched && liquidTitle != null)
            {
                infoBackgroundSearched = true;
                Transform parent = liquidTitle.transform.parent;
                if (parent != null && parent != transform && parent.GetComponent<Canvas>() == null) infoBackground = parent.GetComponent<Image>();
            }
            bool showBack = title.Length > 0;
            if (infoBackground != null && infoBackground.enabled != showBack) infoBackground.enabled = showBack;
        }

        private void UpdateTooltip()
        {
            if (tooltipPanel == null || tooltipText == null || input == null) return;
            bool show = input.Current == BrewInput2D.Activity.None && !string.IsNullOrEmpty(input.HoverTitle);
            if (tooltipPanel.gameObject.activeSelf != show)
            {
                tooltipPanel.gameObject.SetActive(show);
                if (show && tooltipGroup != null) tooltipGroup.alpha = 0f;
            }
            if (!show) return;
            if (tooltipGroup != null && tooltipGroup.alpha < 1f) tooltipGroup.alpha = Mathf.MoveTowards(tooltipGroup.alpha, 1f, Time.unscaledDeltaTime * 10f);
            // 指している物が変わったときだけ、文字を作り直す（BrewInput2D も、変わったときだけ新しい文字にする）
            if (!ReferenceEquals(input.HoverTitle, tooltipTitle) || !ReferenceEquals(input.HoverSubtitle, tooltipSubtitle))
            {
                tooltipTitle = input.HoverTitle;
                tooltipSubtitle = input.HoverSubtitle;
                string text = string.IsNullOrEmpty(tooltipSubtitle) ? tooltipTitle : tooltipTitle + "\n<size=16>" + tooltipSubtitle + "</size>";
                tooltipText.text = text;
                // 素材ノートの性質の行が増えても、枠からはみ出さないように高さを合わせる
                float h = Mathf.Max(30f, tooltipText.preferredHeight);
                tooltipText.rectTransform.sizeDelta = new Vector2(tooltipText.rectTransform.sizeDelta.x, h);
                tooltipPanel.sizeDelta = new Vector2(tooltipPanel.sizeDelta.x, h + 14f);
            }
            Vector2 p = input.PointerScreen;
            // 画面の右端（素材トレイ）・上端からはみ出さないように、右側ではカーソルの左下に出す
            bool left = p.x > Screen.width * 0.6f;
            tooltipPanel.pivot = new Vector2(left ? 1f : 0f, 1f);
            tooltipPanel.position = new Vector3(p.x + (left ? -24f : 24f), p.y - 24f, 0f);
        }

        private static string KindLabel(LiquidKind kind)
        {
            switch (kind)
            {
                case LiquidKind.Potion: return "ポーション";
                case LiquidKind.Intermediate: return "中間液体";
                default: return "未知の液体";
            }
        }
    }
}
