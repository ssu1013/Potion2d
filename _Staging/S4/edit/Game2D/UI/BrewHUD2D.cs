using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D 調合画面の文字とボタン（BrewHUD の Prefab のルートに付ける）。見た目は Prefab の中の Image・Text を直接いじってよい
    /// （パネルの絵・ボタンの絵は Image の Source Image に、自分で描いた絵を入れる）。
    /// 日本語のフォントは「フォント」欄に入れる。空なら Windows の日本語フォント（Yu Gothic UI など）を使う
    /// （OS のフォントは製品版では使えないので、Noto Sans JP・M PLUS Rounded などの商用可のフォントに差し替える）。
    /// </summary>
    public class BrewHUD2D : MonoBehaviour
    {
        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private BrewSession2D session;
        [SerializeField] private BrewInput2D input;
        [SerializeField] private BrewTray2D tray;

        [Header("フォント")]
        [SerializeField, Tooltip("日本語のフォント（空なら UISkin のフォント → それもなければ OS のフォント）")] private Font font;
        [SerializeField, Tooltip("太字の文字に使うフォント（空なら UISkin の太字 → それもなければ上のフォントを太らせる）")] private Font boldFont;
        [SerializeField] private string[] osFontNames = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Hiragino Sans" };
        [SerializeField, HideInInspector, Tooltip("画面の配置の版（Step4 のメニューで 4 になる）")] private int layoutVersion;

        /// <summary>画面の配置の版（4 = Step4 の配置：見立てカード・温度計と重ならない）</summary>
        public int LayoutVersion => layoutVersion;

        [Header("文字")]
        [SerializeField, Tooltip("フラスコの中身の名前")] private Text liquidTitle;
        [SerializeField, Tooltip("分類・量・温度・属性")] private Text liquidDetail;
        [SerializeField, Tooltip("これまでの工程（最近の数行）")] private Text processLog;
        [SerializeField, Tooltip("カーソルのそばの説明")] private RectTransform tooltipPanel;
        [SerializeField] private Text tooltipText;
        [SerializeField, Tooltip("短いお知らせ")] private Text toast;
        [SerializeField, Tooltip("注いだ量・かき混ぜた回数（作業中だけ）")] private Text activityText;
        [SerializeField, Min(1)] private int logLines = 6;

        [Header("ボタン")]
        [SerializeField] private Button bottleOneButton;
        [SerializeField] private Button bottleAllButton;
        [SerializeField] private Text bottleAllLabel;
        [SerializeField] private Button fastForwardButton;
        [SerializeField] private Text fastForwardLabel;
        [SerializeField] private Button pagePrevButton;
        [SerializeField] private Button pageNextButton;
        [SerializeField] private Text pageLabel;

        private readonly StringBuilder sb = new StringBuilder(256);
        private float infoTimer;
        private float toastTimer;

        private void Awake()
        {
            // 画面の比率が 16:9 でないときも、UI を絵と同じ範囲に収める（子を「Safe」の下へ移す）
            if (GetComponent<Canvas>() != null && GetComponent<ViewportFit2D>() == null) gameObject.AddComponent<ViewportFit2D>();
            // 調合台の手ごたえ（見立てカード・温度計・演出）。Prefab になければ付ける
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
            if (tray == null) tray = FindFirstObjectByType<BrewTray2D>();
            if (session != null)
            {
                session.Message += ShowToast;
                session.Bottled += OnBottled;
            }
            Bind(bottleOneButton, () => { if (session != null) session.BottleOne(); });
            Bind(bottleAllButton, () => { if (session != null) session.BottleAll(); });
            Bind(fastForwardButton, () => { if (session != null) session.FastForward = !session.FastForward; });
            Bind(pagePrevButton, () => { if (tray != null) tray.ChangePage(-1); });
            Bind(pageNextButton, () => { if (tray != null) tray.ChangePage(1); });
            if (toast != null) toast.text = "";
            if (tooltipPanel != null) tooltipPanel.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (session == null) return;
            session.Message -= ShowToast;
            session.Bottled -= OnBottled;
        }

        private Canvas canvas;

        private void Update()
        {
            if (session == null) return;
            // アトリエの部屋にいる間（調合台に向かっていないとき）は隠す
            if (canvas == null) canvas = GetComponent<Canvas>();
            AtelierView2D view = AtelierView2D.Current;
            GameFlow flow = GameFlow.Instance;
            bool show = (view == null || view.InBrew) && (flow == null || flow.Mode == GameMode.Playing);
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
            UpdateActivity();
            if (toast != null && toastTimer > 0f)
            {
                toastTimer -= dt;
                Color c = toast.color;
                c.a = Mathf.Clamp01(toastTimer / 0.5f);
                toast.color = c;
            }
        }

        /// <summary>ボタンに動きをつなぐ。押したあとは選択を外す（Space・Enter で同じボタンがもう一度押されないように）</summary>
        private static void Bind(Button button, UnityAction action)
        {
            if (button == null) return;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(action);
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });
        }

        public void ShowToast(string text)
        {
            if (toast == null) return;
            toast.text = text;
            Color c = toast.color;
            c.a = 1f;
            toast.color = c;
            toastTimer = 3f;
        }

        private void OnBottled(LiquidRecord record, bool isNew, float ml, int bottles)
        {
            if (record == null) return;
            // 結果カードを出すときは、下の文字のお知らせは出さない
            BrewFeedback2D feedback = GetComponent<BrewFeedback2D>();
            if (feedback != null && feedback.ShowsBottleResult) return;
            string head = isNew ? "新しい液体を発見！　" : "";
            ShowToast($"{head}{record.DisplayName}を {Mathf.RoundToInt(ml)}ml（{bottles}本）瓶に詰めた");
        }

        private void RefreshInfo()
        {
            FlaskSimulation sim = session.Flask;
            LiquidRecord preview = session.Preview;
            AlchemySettings settings = sim != null ? sim.Settings : null;

            if (liquidTitle != null)
            {
                if (sim == null) liquidTitle.text = "（フラスコの準備ができていません）";
                else if (sim.IsEmpty) liquidTitle.text = "フラスコは空っぽ";
                else if (preview == null) liquidTitle.text = "溶け残りだけ（液体を注ぐと溶ける）";
                else liquidTitle.text = preview.DisplayName;
            }

            if (liquidDetail != null && sim != null)
            {
                sb.Length = 0;
                if (preview != null)
                {
                    sb.Append(KindLabel(preview.kind));
                    if (preview.stars > 0) sb.Append("　").Append(new string('★', Mathf.Clamp(preview.stars, 0, 5)));
                    sb.Append('\n');
                }
                sb.Append($"量 {Mathf.RoundToInt(sim.VolumeMl)} ／ {Mathf.RoundToInt(session.CapacityMl)} ml\n");
                sb.Append($"温度 {Mathf.RoundToInt(sim.Temperature)}℃　火加減 {ProcessStep.HeatLabel(sim.HeatLevel)}");
                if (sim.IsBoiling) sb.Append("（沸騰）");
                if (session.FastForward) sb.Append("　早送り中");
                // 属性の値は、見立てカード（BrewFeedback2D）があればそちらに出す
                if (preview != null && settings != null && GetComponent<BrewFeedback2D>() == null)
                {
                    AttributeVector attributes = preview.Attributes;
                    List<PotionAttribute> ranked = attributes.GetRankedAttributes();
                    int shown = 0;
                    foreach (PotionAttribute a in ranked)
                    {
                        float v = attributes[a];
                        if (v < 0.5f) break;
                        sb.Append(shown == 0 ? "\n" : "　");
                        sb.Append(settings.GetDisplayName(a)).Append(' ').Append(v.ToString("0.0"));
                        if (++shown >= 3) break;
                    }
                }
                liquidDetail.text = sb.ToString();
            }

            if (processLog != null && sim != null)
            {
                sb.Length = 0;
                IReadOnlyList<ProcessStep> log = sim.Log;
                int start = Mathf.Max(0, log.Count - logLines);
                for (int i = start; i < log.Count; i++)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(i + 1).Append(". ").Append(log[i].Describe());
                }
                processLog.text = sb.Length > 0 ? sb.ToString() : "（まだ何もしていない）";
            }

            if (bottleAllLabel != null)
            {
                int n = session.BottlesForAll();
                bottleAllLabel.text = n > 1 ? $"全部詰める（{n}本） Shift+F" : "全部詰める Shift+F";
            }
            if (fastForwardLabel != null) fastForwardLabel.text = session.FastForward ? "早送り中 Space" : "早送り Space";
            if (pageLabel != null && tray != null) pageLabel.text = $"{tray.Page + 1} / {tray.PageCount}";
        }

        private void UpdateTooltip()
        {
            if (tooltipPanel == null || tooltipText == null || input == null) return;
            bool show = input.Current == BrewInput2D.Activity.None && !string.IsNullOrEmpty(input.HoverTitle);
            if (tooltipPanel.gameObject.activeSelf != show) tooltipPanel.gameObject.SetActive(show);
            if (!show) return;
            string text = string.IsNullOrEmpty(input.HoverSubtitle) ? input.HoverTitle : input.HoverTitle + "\n<size=16>" + input.HoverSubtitle + "</size>";
            if (tooltipText.text != text) tooltipText.text = text;
            Vector2 p = input.PointerScreen;
            // 画面の右端・上端からはみ出さないように、カーソルの左下に出す
            bool left = p.x > Screen.width * 0.7f;
            tooltipPanel.pivot = new Vector2(left ? 1f : 0f, 1f);
            tooltipPanel.position = new Vector3(p.x + (left ? -24f : 24f), p.y - 24f, 0f);
        }

        private void UpdateActivity()
        {
            if (activityText == null || input == null) return;
            string text = "";
            switch (input.Current)
            {
                case BrewInput2D.Activity.Pouring:
                    text = $"注いだ量 {Mathf.RoundToInt(input.PouredThisTime)}ml　傾き {Mathf.RoundToInt(input.Tilt * 100f)}%　{Mathf.RoundToInt(input.PourRate)}ml/秒";
                    break;
                case BrewInput2D.Activity.Stirring:
                    text = $"{(input.StirClockwise ? "時計回り" : "反時計回り")}に {input.StirTurnsThisTime:0.0}回";
                    break;
                case BrewInput2D.Activity.Dialing:
                    text = $"火加減 {ProcessStep.HeatLabel(session.HeatLevel)}（{Mathf.RoundToInt(session.HeatLevel * 100f)}%）";
                    break;
            }
            if (activityText.text != text) activityText.text = text;
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
