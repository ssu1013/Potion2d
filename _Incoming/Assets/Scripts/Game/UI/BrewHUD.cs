using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 調合台に向かっているときの画面（BrewMode と一緒に出る）。フラスコとトレイの邪魔をしないよう、ふちに寄せて置く。
    ///   ・画面のふちを暗くする（どの画質でも、フラスコとトレイが浮かび上がって見える）
    ///   ・下：トレイの分類（すべて／液体／主素材／触媒／自作）とページ、素材箱を開くボタン
    ///   ・右：瓶に詰める・全部詰める・早送り・離れる のボタン
    ///   ・右下：今できる操作の案内（手に持っている物で変わる。小さく、半透明）
    ///   ・左下：これまでの工程（最近の数行）
    ///   ・カーソルのそば：指している物の名前と説明
    ///   ・フラスコのそば：注いでいる量と傾き、かき混ぜた向きと回数。つまみのそば：火加減
    /// </summary>
    public sealed class BrewHUD
    {
        private readonly RectTransform root;
        private readonly CanvasGroup group;
        private readonly PlayerInteractor player;
        private readonly IngredientInventory ingredients;
        private readonly System.Action openBox;
        private readonly System.Action openBook;

        private readonly Image vignette;
        private readonly Text fastLabel;
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly Text pageLabel;
        private readonly Button pagePrev, pageNext;
        private readonly Button bottleOne, bottleAll, fastButton;
        private readonly Text bottleAllLabel, fastButtonLabel;
        private readonly RectTransform guidePanel;
        private readonly Text guideText;
        private readonly Text logText;
        private readonly RectTransform logPanel;
        private readonly RectTransform tooltip;
        private readonly Text tooltipTitle, tooltipBody;
        private readonly RectTransform pourMeter;
        private readonly Image pourFill;
        private readonly Text pourText;
        private readonly RectTransform stirMeter;
        private readonly Text stirText;
        private readonly RectTransform heatGauge;
        private readonly Image heatFill;
        private readonly Text heatText;
        private readonly Image virtualCursor;

        private string shownGuide, shownLog, shownTooltip, shownTooltipBody, shownHeat;
        private int shownTab = -1, shownPage = -1, shownPageCount = -1, shownAll = -1;
        private bool shownFast;
        // 文字を作り直すかどうかの目印（毎フレーム文字列を作らないように）
        private object guideHeld;
        private int guideAmount = int.MinValue;
        private int logCount = -1, logLastAmount = int.MinValue;
        private ProcessStepType logLastType;
        private int heatKey = int.MinValue, pourKey = int.MinValue, stirKey = int.MinValue;
        private float pourAlpha, stirAlpha;
        private readonly StringBuilder sb = new StringBuilder();

        public BrewHUD(RectTransform parent, PlayerInteractor player, IngredientInventory ingredients, System.Action openBox, System.Action openBook = null)
        {
            this.player = player;
            this.ingredients = ingredients;
            this.openBox = openBox;
            this.openBook = openBook;

            root = UIFactory.CreateRect("BrewHUD", parent);
            UIFactory.Stretch(root);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            vignette = UIFactory.CreateImage(root, "Vignette", UIWidgets.Vignette(), new Color(1f, 1f, 1f, 0.75f));
            UIFactory.Stretch(vignette.rectTransform, -120f, -120f, -80f, -80f);

            // 上：早送りの表示
            fastLabel = UIFactory.Label(root, "Fast", "", 30, UIColors.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Place(fastLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(600f, 40f));

            // 下：トレイの分類とページ
            Image bar = UIFactory.CreateImage(root, "TrayBar", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.78f), new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            UIFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1000f, 64f));
            UIFactory.Horizontal(bar.gameObject, new RectOffset(14, 14, 8, 8), 8f, TextAnchor.MiddleCenter);
            UIFactory.FitContent(bar.gameObject, true, false);
            for (int i = 0; i < BrewTray.TabLabels.Length; i++)
            {
                int tab = i;
                tabButtons.Add(UIWidgets.WoodButton(bar.transform, BrewTray.TabLabels[i], 22, new Vector2(104f, 46f), () => SetTab(tab)));
            }
            Spacer(bar.transform, 10f);
            pagePrev = UIWidgets.WoodButton(bar.transform, "◀", 22, new Vector2(48f, 46f), () => Page(-1));
            pageLabel = UIFactory.Label(bar.transform, "Page", "1/1", 22, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Size(pageLabel.gameObject, 64f, 40f);
            pageNext = UIWidgets.WoodButton(bar.transform, "▶", 22, new Vector2(48f, 46f), () => Page(1));
            Spacer(bar.transform, 10f);
            UIWidgets.WoodButton(bar.transform, "素材箱（I）", 22, new Vector2(150f, 46f), () => { if (this.openBox != null) this.openBox(); });

            // 右：作業のボタン
            Image side = UIFactory.CreateImage(root, "Actions", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.7f), new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            UIFactory.Place(side.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, -40f), new Vector2(240f, 300f));
            UIFactory.Vertical(side.gameObject, new RectOffset(12, 12, 12, 12), 8f, TextAnchor.UpperCenter);
            UIFactory.FitContent(side.gameObject, true, true);
            bottleOne = UIWidgets.WoodButton(side.transform, "瓶に詰める（F）", 22, new Vector2(216f, 52f), () => Station(s => s.BottleOne()));
            bottleAll = UIWidgets.WoodButton(side.transform, "全部詰める", 22, new Vector2(216f, 52f), () => Station(s => s.BottleAll()));
            bottleAllLabel = bottleAll.GetComponentInChildren<Text>();
            fastButton = UIWidgets.WoodButton(side.transform, "早送り（Space）", 22, new Vector2(216f, 52f), () => { if (BrewMode.Instance != null) BrewMode.Instance.ToggleFastForward(); });
            fastButtonLabel = fastButton.GetComponentInChildren<Text>();
            if (openBook != null) UIWidgets.WoodButton(side.transform, "レシピ本（R）", 22, new Vector2(216f, 52f), () => this.openBook());
            UIWidgets.WoodButton(side.transform, "離れる（Esc）", 22, new Vector2(216f, 52f), () => { if (BrewMode.Instance != null) BrewMode.Instance.Exit(); });

            // 右下：操作の案内（小さく、半透明）
            Image guide = UIFactory.CreateImage(root, "Guide", UIFactory.RoundedRect(new Color(0.06f, 0.04f, 0.03f, 0.62f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            guidePanel = guide.rectTransform;
            UIFactory.Place(guidePanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 96f), new Vector2(420f, 100f));
            UIFactory.Vertical(guide.gameObject, new RectOffset(16, 16, 10, 12), 2f);
            UIFactory.FitContent(guide.gameObject, true, true);
            guideText = UIFactory.Label(guidePanel, "Text", "", 19, UIColors.CreamDim, TextAnchor.UpperLeft);
            guideText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 左下：これまでの工程
            Image log = UIFactory.CreateImage(root, "Log", UIFactory.RoundedRect(new Color(0.06f, 0.04f, 0.03f, 0.62f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            logPanel = log.rectTransform;
            UIFactory.Place(logPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 96f), new Vector2(420f, 100f));
            UIFactory.Vertical(log.gameObject, new RectOffset(16, 16, 10, 12), 2f);
            UIFactory.FitContent(log.gameObject, true, true);
            logText = UIFactory.Label(logPanel, "Text", "", 19, UIColors.CreamDim, TextAnchor.UpperLeft);
            logText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // カーソルのそばの説明
            Image tip = UIFactory.CreateImage(root, "Tooltip", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.9f), UIColors.ParchmentEdge, 10, 2), Color.white);
            tooltip = tip.rectTransform;
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.pivot = new Vector2(0f, 1f);
            UIFactory.Vertical(tip.gameObject, new RectOffset(14, 16, 8, 10), 2f);
            UIFactory.FitContent(tip.gameObject, true, true);
            tooltipTitle = UIFactory.Label(tooltip, "Title", "", 23, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            tooltipTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            tooltipBody = UIFactory.Label(tooltip, "Body", "", 18, UIColors.CreamDim, TextAnchor.MiddleLeft);
            tooltipBody.horizontalOverflow = HorizontalWrapMode.Overflow;
            tooltip.gameObject.SetActive(false);

            // 注いでいる量と傾き（フラスコの口の左）
            Image pour = UIFactory.CreateImage(root, "PourMeter", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.82f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            pourMeter = pour.rectTransform;
            pourMeter.anchorMin = pourMeter.anchorMax = new Vector2(0.5f, 0.5f);
            pourMeter.pivot = new Vector2(1f, 0.5f);
            pourMeter.sizeDelta = new Vector2(250f, 92f);
            Image pourBack = UIFactory.CreateImage(pourMeter, "Bar", UIFactory.RoundedRect(new Color(0.15f, 0.1f, 0.07f, 1f), UIColors.CreamDim, 6, 2), Color.white);
            UIFactory.Place(pourBack.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(20f, 72f));
            pourFill = UIFactory.CreateImage(pourBack.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 4, 0), UIColors.Gold);
            UIFactory.Stretch(pourFill.rectTransform, 3f, 3f, 3f, 3f);
            pourFill.type = Image.Type.Filled;
            pourFill.fillMethod = Image.FillMethod.Vertical;
            pourText = UIFactory.Label(pourMeter, "Text", "", 21, UIColors.Cream, TextAnchor.MiddleRight, FontStyle.Bold, true);
            UIFactory.Stretch(pourText.rectTransform, 12f, 42f, 6f, 6f);
            pourMeter.gameObject.AddComponent<CanvasGroup>().alpha = 0f;

            // かき混ぜた向きと回数（フラスコの右）
            Image stir = UIFactory.CreateImage(root, "StirMeter", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.82f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            stirMeter = stir.rectTransform;
            stirMeter.anchorMin = stirMeter.anchorMax = new Vector2(0.5f, 0.5f);
            stirMeter.pivot = new Vector2(0f, 0.5f);
            stirMeter.sizeDelta = new Vector2(240f, 70f);
            stirText = UIFactory.Label(stirMeter, "Text", "", 21, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            UIFactory.Stretch(stirText.rectTransform, 14f, 10f, 4f, 4f);
            stirMeter.gameObject.AddComponent<CanvasGroup>().alpha = 0f;

            // 火加減（つまみの右）
            Image heat = UIFactory.CreateImage(root, "HeatGauge", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.72f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            heatGauge = heat.rectTransform;
            heatGauge.anchorMin = heatGauge.anchorMax = new Vector2(0.5f, 0.5f);
            heatGauge.pivot = new Vector2(0f, 0.5f);
            heatGauge.sizeDelta = new Vector2(190f, 58f);
            heatText = UIFactory.Label(heatGauge, "Text", "", 20, UIColors.Cream, TextAnchor.UpperLeft, FontStyle.Bold, true);
            UIFactory.Stretch(heatText.rectTransform, 12f, 10f, 6f, 24f);
            heatText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Image heatBack = UIFactory.CreateImage(heatGauge, "Bar", UIFactory.RoundedRect(new Color(0.15f, 0.1f, 0.07f, 1f), new Color(0f, 0f, 0f, 0f), 5, 0), Color.white);
            UIFactory.Place(heatBack.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 9f), new Vector2(166f, 12f));
            heatFill = UIFactory.CreateImage(heatBack.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 5, 0), new Color(1f, 0.55f, 0.2f));
            UIFactory.Stretch(heatFill.rectTransform);
            heatFill.type = Image.Type.Filled;
            heatFill.fillMethod = Image.FillMethod.Horizontal;

            // 自動テストの仮のポインタ（本物のマウスカーソルは画面の撮影に写らないので、その代わり）
            virtualCursor = UIFactory.CreateImage(root, "VirtualCursor", UIWidgets.Knob(), new Color(1f, 1f, 1f, 0.9f));
            virtualCursor.rectTransform.anchorMin = virtualCursor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            virtualCursor.rectTransform.sizeDelta = new Vector2(22f, 22f);
            virtualCursor.gameObject.SetActive(false);
        }

        public bool Visible => group.alpha > 0.01f;

        private static void Spacer(Transform parent, float width)
        {
            RectTransform s = UIFactory.CreateRect("Spacer", parent);
            UIFactory.Size(s.gameObject, width, 10f);
        }

        private static void Station(System.Action<BrewingStation> action)
        {
            BrewMode mode = BrewMode.Instance;
            if (mode != null && mode.IsReady && mode.Station != null) action(mode.Station);
        }

        private static void SetTab(int tab)
        {
            BrewMode mode = BrewMode.Instance;
            if (mode != null && mode.Tray != null) mode.Tray.SetTab(tab);
        }

        private static void Page(int delta)
        {
            BrewMode mode = BrewMode.Instance;
            if (mode != null && mode.Tray != null) mode.Tray.SetPage(mode.Tray.Page + delta);
        }

        /// <summary>毎フレーム（AtelierHUD から）。modal はウィンドウ（素材箱・会話など）を開いているとき</summary>
        public void Tick(float dt, bool modal, bool resultShowing)
        {
            BrewMode mode = BrewMode.Instance;
            float blend = mode != null && mode.IsActive && !modal ? mode.Blend : 0f;
            float target = resultShowing ? blend * 0.35f : blend;
            group.alpha = Mathf.MoveTowards(group.alpha, target, dt * 6f);
            bool interactive = mode != null && mode.IsReady && !modal && !resultShowing;
            group.blocksRaycasts = interactive;
            group.interactable = interactive;
            root.gameObject.SetActive(group.alpha > 0.001f || blend > 0f);
            if (!root.gameObject.activeSelf || mode == null || mode.Station == null) return;

            BrewingStation station = mode.Station;
            FlaskSimulation flask = station.Flask;

            if (shownFast != mode.FastForward)
            {
                shownFast = mode.FastForward;
                fastLabel.text = mode.FastForward ? $"▶▶ 早送り ×{mode.FastForwardSpeed:0}" : "";
                fastButtonLabel.text = mode.FastForward ? "早送りをやめる" : "早送り（Space）";
            }

            UpdateTray(mode.Tray);

            int all = station.BottlesForAll();
            if (all != shownAll)
            {
                shownAll = all;
                bottleAllLabel.text = all > 1 ? $"全部詰める（{all}本）" : "全部詰める";
            }
            bool canBottle = station.HasLiquid;
            bottleOne.interactable = canBottle;
            bottleAll.interactable = all > 1;

            UpdateGuide(mode);
            UpdateLog(flask);
            UpdateTooltip(mode);
            UpdateMeters(mode, station, dt);
            UpdateHeat(mode, station);

            bool showVirtual = mode.UsingVirtualPointer;
            if (virtualCursor.gameObject.activeSelf != showVirtual) virtualCursor.gameObject.SetActive(showVirtual);
            if (showVirtual) virtualCursor.rectTransform.anchoredPosition = ToCanvas(mode.PointerPosition);
        }

        private void UpdateTray(BrewTray tray)
        {
            if (tray == null) return;
            if (tray.Tab != shownTab)
            {
                shownTab = tray.Tab;
                for (int i = 0; i < tabButtons.Count; i++)
                {
                    bool selected = i == tray.Tab;
                    tabButtons[i].image.color = selected ? new Color(1f, 0.86f, 0.45f) : new Color(0.78f, 0.72f, 0.64f);
                    Text label = tabButtons[i].GetComponentInChildren<Text>();
                    if (label != null) label.color = selected ? Color.white : UIColors.CreamDim;
                }
            }
            if (tray.Page != shownPage || tray.PageCount != shownPageCount)
            {
                shownPage = tray.Page;
                shownPageCount = tray.PageCount;
                pageLabel.text = $"{tray.Page + 1}/{tray.PageCount}";
                pagePrev.interactable = tray.Page > 0;
                pageNext.interactable = tray.Page < tray.PageCount - 1;
            }
        }

        private void UpdateGuide(BrewMode mode)
        {
            IngredientData held = player != null ? player.HeldIngredient : null;
            PotionStack bottle = player != null ? player.HeldPotion : null;
            object heldNow = bottle != null ? (object)bottle : held;
            int heldAmount = bottle != null ? Mathf.RoundToInt(bottle.ml) : held != null && ingredients != null ? Mathf.RoundToInt(ingredients.Get(held) * 100f) : 0;
            if (ReferenceEquals(heldNow, guideHeld) && heldAmount == guideAmount && shownGuide != null) return;
            guideHeld = heldNow;
            guideAmount = heldAmount;
            sb.Clear();
            if (bottle != null)
            {
                sb.Append("<color=#F4C95D>手に持っている：</color><b>").Append(bottle.potionName).Append("</b> ").Append(bottle.ml.ToString("0")).Append("ml\n");
                sb.Append("フラスコの上で <b>左ボタンを押したまま下へ</b>：傾けて注ぐ\n（深く傾けるほど速い。ホイールでも傾く）\n");
                sb.Append("右クリック／Q：トレイに戻す");
            }
            else if (held != null)
            {
                string amount = ingredients != null ? IngredientInventory.Format(held, ingredients.Get(held)) : "";
                sb.Append("<color=#F4C95D>手に持っている：</color><b>").Append(held.DisplayName).Append("</b> ").Append(amount == "∞" ? "使い放題" : amount).Append('\n');
                if (held.IsPoured) sb.Append("フラスコの上で <b>左ボタンを押したまま下へ</b>：傾けて注ぐ\n（深く傾けるほど速い。ホイールでも傾く）\n");
                else sb.Append("フラスコの上で <b>クリック</b>：1回分入れる\n（押したままで続けて入れる）\n");
                sb.Append("右クリック／Q：トレイに戻す");
            }
            else
            {
                sb.Append("<b>クリック</b>：トレイの素材・自作の瓶を手に取る\n");
                sb.Append("フラスコの上で <b>左ドラッグして円を描く</b>：かき混ぜる\n");
                sb.Append("つまみを <b>左ドラッグ／ホイール</b>：火加減\n");
                sb.Append("C／V 混ぜる　H 火加減　F 瓶に詰める\n");
                sb.Append("Space 早送り　E／Esc 離れる");
            }
            SetText(guideText, ref shownGuide, sb.ToString());
        }

        private void UpdateLog(FlaskSimulation flask)
        {
            int count = flask != null ? flask.Log.Count : 0;
            ProcessStep last = count > 0 ? flask.Log[count - 1] : null;
            int lastAmount = last != null ? Mathf.RoundToInt(last.amount * 10f) + (last.clockwise ? 1 : 0) * 7919 : 0;
            ProcessStepType lastType = last != null ? last.type : default;
            if (count == logCount && lastAmount == logLastAmount && lastType == logLastType && shownLog != null) return;
            logCount = count;
            logLastAmount = lastAmount;
            logLastType = lastType;
            sb.Clear();
            sb.Append("<color=#F4C95D>◆</color> <b>これまでの工程</b>");
            if (flask == null || flask.Log.Count == 0)
            {
                sb.Append("\n<color=#BFAE8E>（まだ何もしていない）</color>");
            }
            else
            {
                IReadOnlyList<ProcessStep> log = flask.Log;
                int start = Mathf.Max(0, log.Count - 6);
                if (start > 0) sb.Append("\n<color=#8A7A60>…ほか ").Append(start).Append(" 工程</color>");
                for (int i = start; i < log.Count; i++)
                {
                    sb.Append('\n').Append(i + 1).Append(". ").Append(log[i].Describe());
                }
            }
            SetText(logText, ref shownLog, sb.ToString());
        }

        private void UpdateTooltip(BrewMode mode)
        {
            bool show = mode.IsReady && mode.Current == BrewMode.Activity.None && !string.IsNullOrEmpty(mode.HoverTitle);
            if (tooltip.gameObject.activeSelf != show) tooltip.gameObject.SetActive(show);
            if (!show) return;
            if (!ReferenceEquals(mode.HoverTitle, shownTooltip) || !ReferenceEquals(mode.HoverSubtitle, shownTooltipBody))
            {
                shownTooltip = mode.HoverTitle;
                shownTooltipBody = mode.HoverSubtitle;
                tooltipTitle.text = mode.HoverTitle;
                tooltipBody.text = mode.HoverSubtitle ?? "";
                tooltipBody.gameObject.SetActive(!string.IsNullOrEmpty(mode.HoverSubtitle));
            }
            Vector2 p = ToCanvas(mode.PointerPosition) + new Vector2(26f, -20f);
            // 画面の右・下からはみ出さないように
            Vector2 half = root.rect.size * 0.5f;
            Vector2 size = tooltip.rect.size;
            if (p.x + size.x > half.x - 12f) p.x = ToCanvas(mode.PointerPosition).x - 26f - size.x;
            if (p.y - size.y < -half.y + 12f) p.y = -half.y + 12f + size.y;
            tooltip.anchoredPosition = p;
        }

        private void UpdateMeters(BrewMode mode, BrewingStation station, float dt)
        {
            bool pouring = mode.Current == BrewMode.Activity.Pouring;
            pourAlpha = Mathf.MoveTowards(pourAlpha, pouring ? 1f : 0f, dt * (pouring ? 8f : 2.5f));
            pourMeter.GetComponent<CanvasGroup>().alpha = pourAlpha;
            pourMeter.gameObject.SetActive(pourAlpha > 0.001f);
            if (pourAlpha > 0.001f)
            {
                pourFill.fillAmount = mode.Tilt;
                int key = Mathf.RoundToInt(mode.PouredThisTime) * 1000 + Mathf.RoundToInt(mode.PourRate);
                if (key != pourKey)
                {
                    pourKey = key;
                    pourText.text = mode.PourRate > 0.5f
                    ? $"+{mode.PouredThisTime:0}ml\n<size=17><color=#E9C46A>{mode.PourRate:0} ml/秒</color></size>"
                    : $"+{mode.PouredThisTime:0}ml\n<size=17><color=#BFAE8E>下へ動かして傾ける</color></size>";
                }
                pourMeter.anchoredPosition = ToCanvas(mode.ScreenPoint(station.MouthWorld)) + new Vector2(-70f, 10f);
            }

            bool stirring = mode.Current == BrewMode.Activity.Stirring;
            stirAlpha = Mathf.MoveTowards(stirAlpha, stirring ? 1f : 0f, dt * (stirring ? 8f : 2f));
            stirMeter.GetComponent<CanvasGroup>().alpha = stirAlpha;
            stirMeter.gameObject.SetActive(stirAlpha > 0.001f);
            if (stirAlpha > 0.001f)
            {
                int total = Mathf.RoundToInt(station.Flask != null ? station.Flask.TotalTurns : 0f);
                int key = Mathf.RoundToInt(mode.StirTurnsThisTime * 10f) * 4 + (mode.StirTurnsThisTime < 0.05f ? 0 : mode.StirClockwise ? 1 : 2) + total * 100000;
                if (key != stirKey)
                {
                    stirKey = key;
                    string dir = mode.StirTurnsThisTime < 0.05f ? "円を描いて混ぜる" : mode.StirClockwise ? "↻ 時計回り" : "↺ 反時計回り";
                    stirText.text = $"{dir}\n<size=17><color=#E9C46A>{mode.StirTurnsThisTime:0.0} 回</color>　<color=#BFAE8E>合計 {total} 回</color></size>";
                }
                Vector3 side = Vector3.Lerp(station.BottomWorld, station.MouthWorld, 0.45f) + mode.ViewCamera.transform.right * (station.BottomRadius + 0.06f);
                stirMeter.anchoredPosition = ToCanvas(mode.ScreenPoint(side));
            }
        }

        private void UpdateHeat(BrewMode mode, BrewingStation station)
        {
            BrewHeater heater = station.Heater;
            bool show = heater != null && heater.Knob != null && station.Flask != null;
            if (heatGauge.gameObject.activeSelf != show) heatGauge.gameObject.SetActive(show);
            if (!show) return;
            float level = station.Flask.HeatLevel;
            bool active = mode.Hovered == BrewMode.HoverKind.Knob || mode.Current == BrewMode.Activity.Dialing;
            int temperature = Mathf.RoundToInt(station.Flask.Temperature);
            int key = Mathf.RoundToInt(level * 100f) * 1000 + temperature * 2 + (active ? 1 : 0);
            if (key != heatKey)
            {
                heatKey = key;
                string text = $"{(active ? "<color=#F4C95D>" : "")}火加減：{ProcessStep.HeatLabel(level)}{(active ? "</color>" : "")}　<color=#E9C46A>{temperature}℃</color>";
                SetText(heatText, ref shownHeat, text);
            }
            heatFill.fillAmount = level;
            heatGauge.anchoredPosition = ToCanvas(mode.ScreenPoint(heater.Knob.position)) + new Vector2(38f, 0f);
        }

        private Vector2 ToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            return local;
        }

        private static void SetText(Text label, ref string shown, string value)
        {
            if (shown == value) return;
            shown = value;
            label.text = value;
        }
    }
}
