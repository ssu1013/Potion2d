using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面のUI一式（実行時にコードで組み立てる）。
    /// ・プレイ中：照準・操作案内・フラスコの状態・手に持っている物・通知・調合結果・所持金・相談メモ
    /// ・ウィンドウ：ポーション棚・素材箱／取引先帳・レシピ本・会話・ポーズ・設定・クリア画面
    /// ・タイトル画面と暗転（フェード）
    /// Esc の扱い（閉じる／ポーズ）とマウスカーソルの固定もここでまとめて管理する。
    /// </summary>
    public class AtelierHUD : MonoBehaviour
    {
        public static AtelierHUD Instance { get; private set; }

        [SerializeField] private PotionInventory inventory;
        [SerializeField] private BrewingStation station;
        [SerializeField] private PlayerInteractor player;
        [SerializeField] private QuestManager quests;
        [SerializeField] private PlayerProgress progress;
        [SerializeField, Tooltip("素材の在庫（空なら起動時にシーンから探す）")] private IngredientInventory ingredients;
        [SerializeField, Tooltip("属性バーの最大値（この値でバーが満タン）")] private float attributeBarMax = 100f;

        private RectTransform root;
        private CanvasGroup gameplayGroup;
        private CanvasGroup toastLayerGroup;
        private Canvas gameplayCanvas;
        private Canvas toastCanvas;

        // 表示中の内容（同じなら文字を作り直さない）
        private object shownHeldObject;
        private float shownHeldMl = -1f;
        private float shownCrosshair = -1f;

        // 照準
        private Image crosshair;
        private float crosshairPulse;

        // 操作案内
        private RectTransform promptPanel;
        private Text promptTitle;
        private Text promptSubtitle;
        private readonly List<PromptRow> promptRows = new List<PromptRow>();

        // フラスコ
        private Text flaskAmountText;
        private Image flaskBarFill;
        private Text flaskContentsText;
        private RectTransform attributeList;
        private readonly List<AttributeRow> attributeRows = new List<AttributeRow>();

        // 手に持っている物
        private GameObject heldPanel;
        private Text heldText;

        // 通知
        private CanvasGroup toastGroup;
        private Text toastText;
        private float toastTimer;

        // 調合結果
        private GameObject resultRoot;
        private RectTransform resultCard;
        private Image resultIcon;
        private Image resultGlow;
        private Text resultBanner;
        private Text resultName;
        private Text resultStars;
        private Text resultBody;
        private Text resultFooter;
        private float resultTimer;
        private float resultAppear;
        private float resultAge;

        // ウィンドウ類
        private IngredientBrowserUI ingredientUI;
        private RecipeBookUI recipeBook;
        private DialogueUI dialogue;
        private TitleScreenUI title;
        private PauseMenuUI pause;
        private SettingsPanel settings;
        private ClearScreenUI clear;
        private QuestTrackerUI tracker;

        private int lastModalCloseFrame = -1;
        private bool? cursorLocked;

        // 調合台
        private BrewHUD brewHUD;
        private GameObject controlsPanel;
        private bool brewMode;

        public ScreenFader Fader { get; private set; }
        public DialogueUI Dialogue => dialogue;
        public IngredientBrowserUI IngredientBrowser => ingredientUI;
        /// <summary>ポーション棚（素材箱の「自作の液体」タブ）を見ている</summary>
        public bool IsShelfOpen => ingredientUI != null && ingredientUI.IsShowingLiquids;
        public bool IsBrowserOpen => ingredientUI != null && ingredientUI.IsOpen;
        public RecipeBookUI RecipeBook => recipeBook;
        public bool IsRecipeBookOpen => recipeBook != null && recipeBook.IsOpen;
        public TitleScreenUI Title => title;

        /// <summary>何かのウィンドウ（タイトル含む）を開いていて、移動や調べる操作を止めるべきとき</summary>
        public bool IsModalOpen =>
            IsBrowserOpen || IsRecipeBookOpen || (dialogue != null && dialogue.IsOpen) || (pause != null && pause.IsOpen) ||
            (settings != null && settings.IsOpen) || (title != null && title.IsOpen) || (clear != null && clear.IsOpen);

        /// <summary>棚・素材箱以外のウィンドウ（レシピ本も）を開いているとき（Tab や I で開けない）</summary>
        public bool IsDialogueOrMenuOpen =>
            IsRecipeBookOpen || (dialogue != null && dialogue.IsOpen) || (pause != null && pause.IsOpen) || (settings != null && settings.IsOpen) ||
            (title != null && title.IsOpen) || (clear != null && clear.IsOpen);

        public bool IsResultShowing => resultRoot != null && resultRoot.activeSelf;

        /// <summary>調合台に向かっている（カーソルで作業する。歩く・見回す操作は止める）</summary>
        public bool BrewModeActive => brewMode;

        /// <summary>最後にウィンドウを閉じたフレーム（閉じたキー入力で、そのまま別の操作が起きないようにする）</summary>
        public int LastModalCloseFrame => Mathf.Max(lastModalCloseFrame, dialogue != null ? dialogue.ClosedFrame : -1);

        /// <summary>このフレームはプレイヤーの操作を受け付けない（ウィンドウ表示中・閉じた直後・結果表示中）</summary>
        public bool BlocksPlayerActions => IsModalOpen || IsResultShowing || LastModalCloseFrame == Time.frameCount;

        private struct PromptRow
        {
            public GameObject root;
            public Text key;
            public Text label;
        }

        private struct AttributeRow
        {
            public GameObject root;
            public Text name;
            public Image fill;
            public Text value;
        }

        public void Configure(PotionInventory inventory, BrewingStation station, PlayerInteractor player, QuestManager quests, PlayerProgress progress)
        {
            this.inventory = inventory;
            this.station = station;
            this.player = player;
            this.quests = quests;
            this.progress = progress;
        }

        private void Awake()
        {
            Instance = this;
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            EnsureEventSystem();
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (station != null) station.ContentsChanged += RefreshFlask;
        }

        private void OnDisable()
        {
            if (station != null) station.ContentsChanged -= RefreshFlask;
        }

        private void Start()
        {
            DynamicGI.UpdateEnvironment(); // 環境光（Trilight）を確実に反映
            RefreshFlask();
            // レシピ本で名前を付けたら、棚の瓶・手に持っている瓶の名前も合わせる
            if (station != null && station.Alchemy != null) station.Alchemy.Liquids.Renamed += OnLiquidRenamed;
        }

        private void OnLiquidRenamed(LiquidRecord record)
        {
            if (record == null) return;
            if (inventory != null) inventory.RefreshFrom(record);
            PotionStack held = player != null ? player.HeldPotion : null;
            if (held != null && (held.liquidId == record.id || held.potionId == record.id))
            {
                held.potionName = record.DisplayName;
                shownHeldObject = null; // 手に持っている物の表示を作り直す
            }
        }

        private void Update()
        {
            // 再生開始直後は1フレームが数秒になることがあるので、アニメーションが飛ばないよう上限を付ける
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool playing = GameFlow.Instance == null || GameFlow.Instance.Mode == GameMode.Playing;
            bool allowInput = Fader == null || Fader.Alpha < 0.5f;

            // ボタンをクリックすると「選択中」になり、Enter/Space で二重に押されてしまうので選択を外す
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<Button>() != null)
            {
                es.SetSelectedGameObject(null);
            }

            if (allowInput && Input.GetKeyDown(KeyCode.Escape)) HandleEscape(playing);

            // 一番手前のウィンドウだけがキー入力を受け取る
            bool frameFree = LastModalCloseFrame != Time.frameCount;
            if (settings.IsOpen) settings.Tick(dt, allowInput && frameFree);
            else if (pause.IsOpen) pause.Tick(dt, allowInput && frameFree);
            else if (clear.IsOpen) clear.Tick(dt, allowInput && frameFree);
            else if (dialogue.IsOpen) dialogue.Tick();
            else if (recipeBook.IsOpen) recipeBook.Tick(dt, allowInput && frameFree);
            else if (ingredientUI.IsOpen) ingredientUI.Tick(dt, allowInput && frameFree);
            title.Tick(dt, allowInput && frameFree && !settings.IsOpen);

            UpdateCursor(playing);
            UpdateGameplay(dt, playing);
            if (brewHUD != null) brewHUD.Tick(dt, IsModalOpen || !playing, IsResultShowing);
        }

        private void HandleEscape(bool playing)
        {
            if (settings.IsOpen) settings.Close();
            else if (title.IsOpen && title.IsConfirmOpen) title.CloseConfirm();
            else if (pause.IsOpen) ClosePause();
            else if (recipeBook.IsOpen)
            {
                // 名前を打っている途中なら、名前を付けるのをやめるだけ
                if (recipeBook.IsRenaming) recipeBook.CancelRename();
                else CloseRecipeBook();
            }
            else if (ingredientUI.IsOpen) CloseIngredientBrowser();
            else if (dialogue.IsOpen || clear.IsOpen || title.IsOpen) { }
            else if (IsResultShowing) { HideResult(); MarkClosed(); }
            else if (brewMode && BrewMode.Instance != null) { BrewMode.Instance.Exit(); MarkClosed(); }
            else if (playing) OpenPause();
        }

        private void UpdateCursor(bool playing)
        {
            // 調合台ではカーソルで直接作業するので、カーソルを出しておく
            bool wantLocked = playing && !IsModalOpen && !brewMode;
            if (cursorLocked == wantLocked) return;
            cursorLocked = wantLocked;
            FirstPersonController.SetCursorLocked(wantLocked);
        }

        private void UpdateGameplay(float dt, bool playing)
        {
            bool visible = playing && !IsModalOpen;
            gameplayGroup.alpha = Mathf.MoveTowards(gameplayGroup.alpha, visible ? 1f : 0f, dt * 8f);
            bool toastVisible = playing && !dialogue.IsOpen && !title.IsOpen && !pause.IsOpen && !clear.IsOpen;
            toastLayerGroup.alpha = Mathf.MoveTowards(toastLayerGroup.alpha, toastVisible ? 1f : 0f, dt * 8f);
            // 見えていない間は描画そのものを止める
            gameplayCanvas.enabled = gameplayGroup.alpha > 0.001f;
            toastCanvas.enabled = toastLayerGroup.alpha > 0.001f && toastGroup.alpha > 0.001f;

            // 通知のフェード
            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                toastGroup.alpha = Mathf.Clamp01(toastTimer / 0.4f);
            }

            // 結果カード（E／クリックで閉じる）
            if (IsResultShowing)
            {
                resultAge += dt;
                resultAppear = Mathf.Min(1f, resultAppear + dt * 5f);
                float e = 1f - Mathf.Pow(1f - resultAppear, 3f);
                float overshoot = Mathf.Sin(resultAppear * Mathf.PI) * 0.06f;
                float s = Mathf.Lerp(0.8f, 1f, e) + overshoot;
                resultCard.localScale = new Vector3(s, s, 1f);
                resultGlow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, resultAge * 20f);
                resultTimer -= dt;
                bool dismiss = resultAge > 0.35f && !IsModalOpen &&
                               (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0));
                if (resultTimer <= 0f || dismiss)
                {
                    HideResult();
                    if (dismiss) MarkClosed();
                }
            }

            if (tracker != null) tracker.Tick(dt);
            if (!visible) return;

            // 照準：何かを見ているときは金色で少し大きく
            bool targeting = player != null && player.Target != null;
            crosshairPulse = Mathf.MoveTowards(crosshairPulse, targeting ? 1f : 0f, dt * 6f);
            if (!Mathf.Approximately(crosshairPulse, shownCrosshair))
            {
                shownCrosshair = crosshairPulse;
                crosshair.color = Color.Lerp(new Color(1f, 1f, 1f, 0.8f), UIColors.Gold, crosshairPulse);
                float size = Mathf.Lerp(60f, 76f, crosshairPulse);
                crosshair.rectTransform.sizeDelta = new Vector2(size, size);
            }

            // 手に持っている物（変わったときだけ文字を作り直す）。調合台では調合台の画面が表示する
            if (brewMode) return;
            IngredientData held = player != null ? player.HeldIngredient : null;
            PotionStack heldPotion = player != null ? player.HeldPotion : null;
            object current = heldPotion != null ? (object)heldPotion : held;
            // 注いで瓶の中身が減ったときも表示を変える
            if (!ReferenceEquals(current, shownHeldObject) || (heldPotion != null && !Mathf.Approximately(Mathf.Round(heldPotion.ml), shownHeldMl)))
            {
                shownHeldObject = current;
                heldPanel.SetActive(current != null);
                if (heldPotion != null)
                {
                    shownHeldMl = Mathf.Round(heldPotion.ml);
                    string stars = heldPotion.stars > 0 ? $" <color=#F4C95D>{BrewResult.StarText(heldPotion.stars)}</color>" : "";
                    heldText.text = $"手に持っている：<b>{heldPotion.potionName}</b>{stars}　{heldPotion.ml:0}ml　<color=#D9C7A3>[Q] 棚に戻す</color>";
                }
                else if (held != null)
                {
                    heldText.text = $"手に持っている：<b>{held.DisplayName}</b>　<color=#D9C7A3>[Q] 戻す</color>";
                }
            }
        }

        private void MarkClosed()
        {
            lastModalCloseFrame = Time.frameCount;
        }

        // ------------------------------------------------------------------
        // プレイ中の表示（外から呼ぶ）
        // ------------------------------------------------------------------

        /// <summary>調合台に向かう／離れる（BrewMode が呼ぶ）。照準・操作案内・手に持っている物の表示を隠し、調合台の画面を出す</summary>
        public void SetBrewMode(bool on)
        {
            if (brewMode == on) return;
            brewMode = on;
            crosshair.gameObject.SetActive(!on);
            if (controlsPanel != null) controlsPanel.SetActive(!on);
            promptPanel.gameObject.SetActive(false);
            heldPanel.SetActive(false);
            shownHeldObject = null;
            shownHeldMl = -1f;
            cursorLocked = null; // 次のフレームでカーソルの固定をやり直す
        }

        public void SetPrompt(Interactable target, string title, string subtitle, IReadOnlyList<InteractionOption> options)
        {
            bool show = target != null && !string.IsNullOrEmpty(title) && !IsResultShowing && !IsModalOpen && !brewMode;
            promptPanel.gameObject.SetActive(show);
            if (!show) return;

            promptTitle.text = title;
            promptSubtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            promptSubtitle.text = subtitle ?? "";

            for (int i = 0; i < promptRows.Count; i++)
            {
                bool active = options != null && i < options.Count;
                promptRows[i].root.SetActive(active);
                if (!active) continue;
                promptRows[i].key.text = options[i].keyLabel;
                promptRows[i].label.text = options[i].label;
            }
        }

        public void ShowToast(string message)
        {
            toastText.text = message;
            toastTimer = 3.6f;
            toastGroup.alpha = 1f;
        }

        /// <summary>瓶詰めした結果のカード（失敗作＝未知の液体も「発見」として見せる）</summary>
        public void ShowResult(LiquidRecord record, bool isNew, bool stored, float bottledMl, int bottles, PotionStack stack)
        {
            resultRoot.SetActive(true);
            resultAppear = 0f;
            resultAge = 0f;
            resultTimer = 8f;

            bool special = record.IsSpecial;
            string kindLabel = record.kind == LiquidKind.Potion ? "ポーション" : record.kind == LiquidKind.Intermediate ? "中間液体" : "未知の液体";
            if (special) resultBanner.text = isNew ? "特別なポーションを発見！" : "特別なポーションができた！";
            else resultBanner.text = isNew ? $"新しい{kindLabel}を発見！" : $"{kindLabel}を瓶に詰めた";
            resultIcon.sprite = UIFactory.PotionIcon(record.color);
            Color glow = special ? new Color(1f, 0.85f, 0.4f, 0.7f)
                       : record.kind == LiquidKind.Unknown ? new Color(0.5f, 0.45f, 0.4f, 0.35f)
                       : new Color(record.color.r, record.color.g, record.color.b, 0.55f);
            resultGlow.color = glow;
            resultName.text = record.DisplayName;
            resultStars.text = record.stars > 0 ? BrewResult.StarText(record.stars) : "";
            resultStars.gameObject.SetActive(record.stars > 0);

            var sb = new StringBuilder();
            foreach (string effect in record.effects) sb.Append("・").Append(effect).Append('\n');
            if (special || record.effects.Count == 0) sb.Append(record.description).Append('\n');
            foreach (LiquidTrait t in record.traits)
            {
                if (t.strength < 0.25f || station == null || station.Alchemy == null) continue;
                sb.Append("<color=#7A5A3A>特性：").Append(station.Alchemy.Settings.GetTrait(t.id).displayName).Append("</color>  ");
            }
            resultBody.text = sb.ToString().TrimEnd();

            string amount = bottles > 1 ? $"{bottles}本（{bottledMl:0}ml）" : $"{bottledMl:0}ml";
            if (stored && stack != null) resultFooter.text = $"{amount}を瓶に詰めて棚にしまった（棚に計{stack.ml:0}ml）";
            else resultFooter.text = $"{amount}を瓶に詰めた";
            if (isNew) resultFooter.text += "\n<color=#2E5F8A>レシピ本（R）に新しいページが増えた。次の調合の材料にも使える</color>";
            if (station != null && station.HasLiquid) resultFooter.text += $"\nフラスコにはまだ {station.Flask.VolumeMl:0}ml 残っている";
            else if (station != null && station.Flask != null && !station.Flask.IsEmpty) resultFooter.text += "\n<color=#8A6A45>溶け残った素材はフラスコの底に残り、次の調合で溶ける</color>";

            // 相談の条件に合うなら教える
            if (quests != null && quests.Phase == CustomerPhase.Waiting && quests.CurrentQuest != null && quests.CurrentQuest.IsSatisfiedBy(record))
            {
                resultFooter.text += $"\n<color=#2E7024><b>○ {quests.CurrentQuest.ClientName}さんの悩みに役立ちそう！</b></color>";
            }
            resultFooter.text += "\n<size=18><color=#8A6A45>E／クリックで閉じる</color></size>";
        }

        public void HideResult()
        {
            if (resultRoot != null) resultRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // ポーション棚
        // ------------------------------------------------------------------

        /// <summary>ポーション棚を開く（素材箱と同じ一覧の「自作の液体」タブ）</summary>
        public void OpenShelf()
        {
            OpenIngredientBrowser(IngredientBrowserUI.Mode.Box, IngredientBrowserUI.LiquidTab);
        }

        public void CloseShelf()
        {
            CloseIngredientBrowser();
        }

        /// <summary>[Tab] キー：ポーション棚を開く／閉じる（素材箱を開いているときは「自作の液体」タブに切り替える）</summary>
        public void ToggleShelf()
        {
            if (ingredientUI == null || ingredientUI.IsTyping) return;
            if (ingredientUI.IsShowingLiquids) CloseIngredientBrowser();
            else if (ingredientUI.IsOpen && ingredientUI.CurrentMode == IngredientBrowserUI.Mode.Box) ingredientUI.SetTab(IngredientBrowserUI.LiquidTab);
            else OpenShelf();
        }

        private void TakeFromShelf(PotionStack stack)
        {
            if (player != null && player.HoldPotion(inventory, stack)) CloseIngredientBrowser();
        }

        /// <summary>今の相談に役立ちそうな瓶なら、相談した人の名前（でなければ null）</summary>
        private string ConsultationFor(PotionStack stack)
        {
            if (quests == null || quests.Phase != CustomerPhase.Waiting || quests.CurrentQuest == null) return null;
            return quests.CurrentQuest.IsSatisfiedBy(stack) ? quests.CurrentQuest.ClientName : null;
        }

        // ------------------------------------------------------------------
        // レシピ本
        // ------------------------------------------------------------------

        /// <summary>[R] キー：レシピ本を開く／閉じる（相談に乗っている途中・文字を打っているとき・取引先帳では何もしない）</summary>
        public void ToggleRecipeBook()
        {
            if (recipeBook == null || recipeBook.IsTyping || (ingredientUI != null && ingredientUI.IsTyping)) return;
            if (recipeBook.IsOpen)
            {
                if (recipeBook.CurrentMode == RecipeBookUI.Mode.Read) CloseRecipeBook();
                return;
            }
            OpenRecipeBook();
        }

        /// <summary>レシピ本を開いて読む（素材箱・ポーション棚を開いていたら閉じて開く。取引先帳では R は数を変えるキーなので開かない）</summary>
        public void OpenRecipeBook()
        {
            if (recipeBook == null || recipeBook.IsOpen) return;
            if ((dialogue != null && dialogue.IsOpen) || pause.IsOpen || settings.IsOpen || title.IsOpen || clear.IsOpen) return;
            if (ingredientUI.IsOpen)
            {
                if (ingredientUI.CurrentMode == IngredientBrowserUI.Mode.Shop) return;
                ingredientUI.Close();
            }
            HideResult();
            recipeBook.Open(RecipeBookUI.Mode.Read);
        }

        /// <summary>相談に乗るためにレシピ本を開く（分け与える瓶／教える作り方を選ぶ。やめたら pick に null）</summary>
        public void OpenRecipeBookFor(RecipeBookUI.Mode mode, QuestData consultation, System.Action<LiquidRecord> pick)
        {
            if (recipeBook == null) return;
            if (ingredientUI.IsOpen) ingredientUI.Close();
            if (recipeBook.IsOpen) recipeBook.Close(false);
            HideResult();
            recipeBook.Open(mode, consultation, pick);
        }

        public void CloseRecipeBook()
        {
            if (recipeBook == null || !recipeBook.IsOpen) return;
            recipeBook.Close();
            MarkClosed();
        }

        // ------------------------------------------------------------------
        // 素材箱・仕入れ
        // ------------------------------------------------------------------

        /// <summary>素材箱を「すべて」タブで開く（前にポーション棚のタブで閉じていても、素材箱として開く）</summary>
        public void OpenIngredientBox()
        {
            OpenIngredientBrowser(IngredientBrowserUI.Mode.Box, 0);
        }

        public void OpenShop()
        {
            OpenIngredientBrowser(IngredientBrowserUI.Mode.Shop);
        }

        private void OpenIngredientBrowser(IngredientBrowserUI.Mode mode, int tab = -1)
        {
            if (ingredientUI == null || IsDialogueOrMenuOpen) return;
            if (ingredientUI.IsOpen && ingredientUI.CurrentMode == mode)
            {
                if (tab >= 0) ingredientUI.SetTab(tab);
                return;
            }
            HideResult();
            ingredientUI.Open(mode, tab);
        }

        public void CloseIngredientBrowser()
        {
            if (ingredientUI == null || !ingredientUI.IsOpen) return;
            ingredientUI.Close();
            MarkClosed();
        }

        /// <summary>[I] キー：素材箱を開く／閉じる（検索欄に文字を打っているときは何もしない）</summary>
        public void ToggleIngredientBox()
        {
            if (ingredientUI == null || ingredientUI.IsTyping) return;
            if (ingredientUI.IsOpen) CloseIngredientBrowser();
            else OpenIngredientBrowser(IngredientBrowserUI.Mode.Box, 0);
        }

        private void TakeFromBox(IngredientData data)
        {
            if (player != null && player.HoldIngredient(data)) CloseIngredientBrowser();
        }

        /// <summary>
        /// 取引先帳：使い放題の素材は入手ルートを開拓する（一度払えばずっと使い放題）。
        /// 希少な素材は 数 × 価格 で取り寄せる（使った分だけ減る在庫が増える）
        /// </summary>
        private bool TradeIngredient(IngredientData data, int quantity)
        {
            if (data == null || quantity <= 0 || ingredients == null || progress == null) return false;
            SoundLibrary lib = AudioManager.Lib;
            if (data.RequiredReputation > progress.CompletedQuests && !ingredients.IsUnlocked(data))
            {
                if (lib != null) AudioManager.Se(lib.warning, 0.8f);
                ShowToast($"相談を {data.RequiredReputation} 件解決すると、取引してもらえる");
                return false;
            }

            bool infinite = IngredientInventory.IsInfiniteKind(data);
            if (infinite && ingredients.IsUnlimited(data)) return false;
            GameBalance balance = GameContext.Balance;
            int cost = infinite ? data.UnlockCost(balance != null ? balance.unlockPriceMultiplier : 8) : data.BuyPrice * quantity;
            if (cost <= 0) return false;
            if (!progress.TrySpendGold(cost))
            {
                if (lib != null) AudioManager.Se(lib.warning, 0.8f);
                ShowToast($"お金が足りません（{cost} G 必要）");
                return false;
            }
            if (infinite)
            {
                ingredients.Unlock(data);
                if (lib != null) AudioManager.Se(lib.questComplete, 0.7f);
                ShowToast($"「{data.DisplayName}」の入手ルートを開拓した！ これからは使い放題（-{cost} G）");
            }
            else
            {
                float amount = quantity * data.AmountPerUse;
                ingredients.Add(data, amount);
                if (lib != null) AudioManager.Se(lib.coins, 0.8f);
                ShowToast($"「{data.DisplayName}」を {IngredientInventory.Format(data, amount).TrimStart('×')} 取り寄せた（-{cost} G）");
            }
            if (GameFlow.Instance != null) GameFlow.Instance.Save();
            return true;
        }

        // ------------------------------------------------------------------
        // 会話・クリア
        // ------------------------------------------------------------------

        public void ShowDialogue(DialogueRequest request)
        {
            HideResult();
            if (ingredientUI.IsOpen) ingredientUI.Close();
            if (recipeBook.IsOpen) recipeBook.Close(false);
            dialogue.Open(request);
        }

        public void ShowClear()
        {
            HideResult();
            clear.Open(progress);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.brewSpecial, 0.9f);
        }

        /// <summary>設定画面を開く（自動テスト用にも公開）</summary>
        public void OpenSettings()
        {
            settings.Open(null);
        }

        public void CloseSettings()
        {
            settings.Close();
        }

        /// <summary>クリア画面を閉じる（自動テスト用）</summary>
        public void CloseClearScreen()
        {
            if (clear.IsOpen) CloseClear();
        }

        private void CloseClear()
        {
            clear.Close();
            MarkClosed();
            ShowToast("これからも探求を続けて、町の人の相談に乗っていこう！");
        }

        // ------------------------------------------------------------------
        // タイトル・ポーズ
        // ------------------------------------------------------------------

        public void ShowTitle(bool hasSave)
        {
            HideResult();
            if (ingredientUI.IsOpen) ingredientUI.Close();
            if (recipeBook.IsOpen) recipeBook.Close(false);
            dialogue.ForceClose();
            pause.Close();
            clear.Close();
            if (settings.IsOpen) settings.Close();
            toastTimer = 0f;
            toastGroup.alpha = 0f;
            gameplayGroup.alpha = 0f;
            title.Show(hasSave, hasSave ? SaveSystem.Load() : null);
        }

        public void ShowGameplay()
        {
            title.Hide();
            if (tracker != null) tracker.SnapGold();
            RefreshFlask();
            MarkClosed();
        }

        public void OpenPause()
        {
            GameFlow flow = GameFlow.Instance;
            if (flow == null || flow.Mode != GameMode.Playing || pause.IsOpen) return;
            HideResult();
            flow.Pause();
            pause.Open(progress, inventory);
        }

        public void ClosePause()
        {
            if (!pause.IsOpen) return;
            pause.Close();
            if (GameFlow.Instance != null) GameFlow.Instance.Resume();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.7f);
            MarkClosed();
        }

        // ------------------------------------------------------------------
        // フラスコ表示
        // ------------------------------------------------------------------

        private void RefreshFlask()
        {
            if (station == null || flaskAmountText == null) return;
            FlaskSimulation flask = station.Flask;
            LiquidRecord preview = station.Preview;
            // 溶け残りだけのときは 0ml（液体はない）と表示する
            float volume = station.HasLiquid ? flask.VolumeMl : 0f;
            flaskAmountText.text = $"{volume:0} / {station.CapacityMl:0} ml";
            flaskBarFill.fillAmount = Mathf.Clamp01(volume / station.CapacityMl);
            Color c = preview != null ? preview.color : UIColors.CreamDim;
            c.a = 1f;
            flaskBarFill.color = volume > 0.5f ? c : UIColors.CreamDim;

            var sb = new StringBuilder();
            if (flask == null || flask.IsEmpty)
            {
                sb.Append("<color=#BFAE8E>（空っぽ）</color>");
            }
            else
            {
                if (preview != null)
                {
                    string kind = preview.kind == LiquidKind.Potion ? "ポーション" : preview.kind == LiquidKind.Intermediate ? "中間液体" : "未知の液体";
                    sb.Append("<b>").Append(preview.DisplayName).Append("</b>  <color=#BFAE8E>").Append(kind).Append("</color>\n");
                }
                LiquidState state = flask.Snapshot();
                if (preview == null)
                {
                    // 溶け残りだけ：何が残っているかと、次に液体を注ぐと溶けることを伝える
                    sb.Append("<color=#BFAE8E>溶け残り：");
                    bool first = true;
                    foreach (LiquidState.Source src in state.sources)
                    {
                        if (src.undissolvedUnits < 0.01f) continue;
                        sb.Append(first ? "" : "・").Append(src.name).Append(' ').Append(ProcessStep.FormatUnits(src.undissolvedUnits));
                        first = false;
                    }
                    sb.Append("（液体を注ぐと溶ける）</color>\n");
                    sb.Append("<color=#E9C46A>").Append(Mathf.RoundToInt(flask.Temperature)).Append("℃</color> ").Append(ProcessStep.HeatLabel(flask.HeatLevel));
                }
                else
                {
                    sb.Append("<color=#E9C46A>").Append(Mathf.RoundToInt(flask.Temperature)).Append("℃</color> ")
                      .Append(ProcessStep.HeatLabel(flask.HeatLevel)).Append(flask.IsBoiling ? "・沸騰中" : "")
                      .Append("　溶け具合 <color=#E9C46A>").Append(Mathf.RoundToInt(state.dissolvedFraction * 100f)).Append("%</color>")
                      .Append("　混ぜた ").Append(Mathf.RoundToInt(flask.TotalTurns)).Append("回");
                }
            }
            flaskContentsText.text = sb.ToString().TrimEnd();

            // 属性バー（100ml あたりの濃さ）
            AlchemyManager alchemy = station.Alchemy;
            List<PotionAttribute> ranked = new List<PotionAttribute>();
            AttributeVector vector = null;
            if (alchemy != null && preview != null)
            {
                vector = preview.Attributes;
                ranked = vector.GetRankedAttributes();
            }

            while (attributeRows.Count < ranked.Count) attributeRows.Add(CreateAttributeRow(attributeList));
            for (int i = 0; i < attributeRows.Count; i++)
            {
                bool active = i < ranked.Count;
                attributeRows[i].root.SetActive(active);
                if (!active) continue;

                PotionAttribute a = ranked[i];
                AttributeInfo info = alchemy.Settings.GetInfo(a);
                float value = vector[a];
                attributeRows[i].name.text = info.displayName;
                attributeRows[i].value.text = value.ToString("0");
                attributeRows[i].fill.fillAmount = Mathf.Clamp01(value / attributeBarMax);
                Color ac = info.color;
                ac.a = 1f;
                attributeRows[i].fill.color = ac;
            }
            attributeList.gameObject.SetActive(ranked.Count > 0);
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private void Build()
        {
            var canvasGo = new GameObject("AtelierCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)canvasGo.transform;

            // 奥から：プレイ中のUI → 会話 → 棚 → 素材箱 → 通知 → クリア → タイトル → ポーズ → 設定
            RectTransform gameplay = UIFactory.CreateRect("Gameplay", root);
            UIFactory.Stretch(gameplay);
            gameplayGroup = gameplay.gameObject.AddComponent<CanvasGroup>();
            gameplayGroup.blocksRaycasts = false;
            gameplayGroup.interactable = false;
            gameplayGroup.alpha = 0f;

            BuildCrosshair(gameplay);
            BuildPrompt(gameplay);
            BuildFlaskPanel(gameplay);
            BuildHeldPanel(gameplay);
            BuildControlsHelp(gameplay);
            tracker = new QuestTrackerUI(gameplay, quests, progress, player);
            BuildResultCard(gameplay);

            // 調合台の画面（ボタンを押せるように、プレイ中のUIとは別の入れ物にする）
            brewHUD = new BrewHUD(root, player, ingredients, OpenIngredientBox, ToggleRecipeBook);

            dialogue = new DialogueUI(root);
            ingredientUI = new IngredientBrowserUI(root, ingredients, inventory, progress,
                () => station != null && station.Alchemy != null ? station.Alchemy.Settings : null,
                id => station != null && station.Alchemy != null ? station.Alchemy.Liquids.Get(id) : null,
                ConsultationFor, CloseIngredientBrowser, TakeFromBox, TakeFromShelf, TradeIngredient);
            recipeBook = new RecipeBookUI(root, () => station != null ? station.Alchemy : null, inventory,
                () => quests != null && quests.Phase == CustomerPhase.Waiting ? quests.CurrentQuest : null,
                () => quests != null ? quests.TeachRewardRate : 0.7f, CloseRecipeBook);

            // 通知は棚・素材箱のウィンドウより手前に出す（仕入れで買ったときの知らせが見えるように）
            RectTransform toastLayer = UIFactory.CreateRect("ToastLayer", root);
            UIFactory.Stretch(toastLayer);
            toastLayerGroup = toastLayer.gameObject.AddComponent<CanvasGroup>();
            toastLayerGroup.blocksRaycasts = false;
            toastLayerGroup.alpha = 0f;
            BuildToast(toastLayer);
            clear = new ClearScreenUI(root, CloseClear);
            title = new TitleScreenUI(root,
                () => { if (GameFlow.Instance != null) GameFlow.Instance.StartNewGame(); },
                () => { if (GameFlow.Instance != null) GameFlow.Instance.ContinueGame(); },
                OpenSettings,
                () => { if (GameFlow.Instance != null) GameFlow.Instance.QuitGame(); });
            pause = new PauseMenuUI(root,
                ClosePause,
                OpenSettings,
                () =>
                {
                    pause.Close();
                    if (GameFlow.Instance != null) GameFlow.Instance.ReturnToTitle();
                },
                () => { if (GameFlow.Instance != null) GameFlow.Instance.QuitGame(); });
            settings = new SettingsPanel(root);

            // 画面の部品ごとに Canvas を分ける。動く部品（タイトルの光の粒・会話の文字送りなど）が
            // 変わったときに、ほかの部品まで作り直さずに済む
            foreach (Transform child in root)
            {
                var sub = child.gameObject.AddComponent<Canvas>();
                if (child != gameplay && child != toastLayer) child.gameObject.AddComponent<GraphicRaycaster>();
                if (child == gameplay) gameplayCanvas = sub;
                if (child == toastLayer) toastCanvas = sub;
            }

            Fader = new ScreenFader(transform);
        }

        private void BuildCrosshair(RectTransform parent)
        {
            crosshair = UIFactory.CreateImage(parent, "Crosshair", UIFactory.Crosshair(), Color.white);
            UIFactory.Place(crosshair.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        }

        private void BuildPrompt(RectTransform parent)
        {
            // 右下は明るい小物（瓶・本）の上に重なることがあるので、背景は濃いめにする
            Image panel = UIFactory.CreateImage(parent, "Prompt", UIFactory.RoundedRect(new Color(0.07f, 0.05f, 0.035f, 0.88f), new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            promptPanel = panel.rectTransform;
            // 画面の右下（操作のヘルプのすぐ上）に出す。画面の真ん中に出すと、見ている物（フラスコなど）が隠れて見づらいため
            UIFactory.Place(promptPanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 150f), new Vector2(300f, 100f));
            UIFactory.Vertical(panel.gameObject, new RectOffset(20, 24, 12, 14), 4f);
            UIFactory.FitContent(panel.gameObject, true, true);

            promptTitle = UIFactory.Label(promptPanel, "Title", "", 28, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            promptTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            promptSubtitle = UIFactory.Label(promptPanel, "Subtitle", "", 19, new Color(0.93f, 0.86f, 0.72f), TextAnchor.MiddleLeft);
            promptSubtitle.horizontalOverflow = HorizontalWrapMode.Overflow;

            for (int i = 0; i < 7; i++)
            {
                RectTransform row = UIFactory.CreateRect("Row" + i, promptPanel);
                UIFactory.Horizontal(row.gameObject, new RectOffset(0, 0, 3, 0), 10f);
                Text key = UIFactory.KeyCap(row, "E", 19);
                Text label = UIFactory.Label(row, "Label", "", 23, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Normal, true);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                promptRows.Add(new PromptRow { root = row.gameObject, key = key, label = label });
            }
            promptPanel.gameObject.SetActive(false);
        }

        private void BuildFlaskPanel(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "FlaskPanel", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            UIFactory.Place(panel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(500f, 200f));
            UIFactory.Vertical(panel.gameObject, new RectOffset(22, 22, 16, 18), 8f).childForceExpandWidth = true;
            UIFactory.FitContent(panel.gameObject, false, true);

            UIFactory.Label(panel.transform, "Title", "<color=#F4C95D>◆</color> 調合フラスコ", 28, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold, true);

            // 容量バー
            RectTransform barRow = UIFactory.CreateRect("Capacity", panel.transform);
            UIFactory.Horizontal(barRow.gameObject, new RectOffset(0, 0, 0, 0), 12f);
            Image barBack = UIFactory.CreateImage(barRow, "Bar", UIFactory.RoundedRect(new Color(0.1f, 0.07f, 0.05f, 0.9f), UIColors.CreamDim, 8, 2), Color.white);
            UIFactory.Size(barBack.gameObject, 260f, 20f);
            flaskBarFill = UIFactory.CreateImage(barBack.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 6, 0), Color.white);
            UIFactory.Stretch(flaskBarFill.rectTransform, 3f, 3f, 3f, 3f);
            flaskBarFill.type = Image.Type.Filled;
            flaskBarFill.fillMethod = Image.FillMethod.Horizontal;
            flaskAmountText = UIFactory.Label(barRow, "Amount", "0 / 600 ml", 22, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            flaskAmountText.horizontalOverflow = HorizontalWrapMode.Overflow;

            flaskContentsText = UIFactory.Label(panel.transform, "Contents", "", 22, UIColors.Cream, TextAnchor.UpperLeft);

            attributeList = UIFactory.CreateRect("Attributes", panel.transform);
            UIFactory.Vertical(attributeList.gameObject, new RectOffset(0, 0, 6, 0), 4f);
        }

        private AttributeRow CreateAttributeRow(Transform parent)
        {
            RectTransform row = UIFactory.CreateRect("Attribute", parent);
            UIFactory.Horizontal(row.gameObject, new RectOffset(0, 0, 0, 0), 10f);

            Text name = UIFactory.Label(row, "Name", "", 20, UIColors.CreamDim, TextAnchor.MiddleLeft);
            UIFactory.Size(name.gameObject, 90f, 24f);

            Image back = UIFactory.CreateImage(row, "Bar", UIFactory.RoundedRect(new Color(0.1f, 0.07f, 0.05f, 0.9f), new Color(0f, 0f, 0f, 0f), 6, 0), Color.white);
            UIFactory.Size(back.gameObject, 210f, 14f);
            Image fill = UIFactory.CreateImage(back.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 6, 0), Color.white);
            UIFactory.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            Text value = UIFactory.Label(row, "Value", "", 20, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(value.gameObject, 50f, 24f);
            return new AttributeRow { root = row.gameObject, name = name, fill = fill, value = value };
        }

        private void BuildHeldPanel(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "HeldPanel", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            UIFactory.Place(panel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(300f, 54f));
            UIFactory.Horizontal(panel.gameObject, new RectOffset(20, 22, 10, 10), 0f);
            UIFactory.FitContent(panel.gameObject, true, true);
            heldText = UIFactory.Label(panel.transform, "Held", "", 24, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            heldText.horizontalOverflow = HorizontalWrapMode.Overflow;
            heldPanel = panel.gameObject;
            heldPanel.SetActive(false);
        }

        private void BuildControlsHelp(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "Controls", UIFactory.RoundedRect(new Color(0.06f, 0.04f, 0.03f, 0.55f), new Color(0f, 0f, 0f, 0f), 10, 0), Color.white);
            controlsPanel = panel.gameObject;
            UIFactory.Place(panel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(300f, 100f));
            UIFactory.Vertical(panel.gameObject, new RectOffset(18, 18, 12, 12), 2f);
            UIFactory.FitContent(panel.gameObject, true, true);
            Text help = UIFactory.Label(panel.transform, "Help",
                "WASD 移動　マウス／矢印キー 視点　Shift 走る\n" +
                "E／左クリック 調べる・取る・話す\n" +
                "フラスコを調べると調合台に向かう\n" +
                "I 素材箱　Tab ポーション棚　R レシピ本　Q 戻す　Esc メニュー",
                18, UIColors.CreamDim, TextAnchor.UpperLeft);
            help.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildToast(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "Toast", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 14, 0), Color.white);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(400f, 60f));
            UIFactory.Horizontal(panel.gameObject, new RectOffset(28, 28, 12, 12), 0f, TextAnchor.MiddleCenter);
            UIFactory.FitContent(panel.gameObject, true, true);
            toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastGroup.alpha = 0f;
            toastText = UIFactory.Label(panel.transform, "Text", "", 26, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildResultCard(RectTransform parent)
        {
            RectTransform overlay = UIFactory.CreateRect("Result", parent);
            UIFactory.Stretch(overlay);
            resultRoot = overlay.gameObject;

            Image card = UIFactory.CreateImage(overlay, "Card", UIFactory.Parchment(), Color.white);
            resultCard = card.rectTransform;
            UIFactory.Place(resultCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(560f, 300f));
            UIFactory.Vertical(card.gameObject, new RectOffset(44, 44, 70, 34), 8f, TextAnchor.UpperCenter).childForceExpandWidth = true;
            UIFactory.FitContent(card.gameObject, false, true);

            // 上部のリボン
            Image ribbon = UIFactory.CreateImage(resultCard, "Ribbon", UIFactory.WoodPanel(), Color.white);
            ribbon.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Place(ribbon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(420f, 70f));
            resultBanner = UIFactory.Label(ribbon.transform, "Banner", "", 30, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(resultBanner.rectTransform);

            // アイコンの後ろの光（ゆっくり回る）
            RectTransform iconHolder = UIFactory.CreateRect("IconHolder", resultCard);
            UIFactory.Size(iconHolder.gameObject, 150f, 150f);
            resultGlow = UIFactory.CreateImage(iconHolder, "Glow", UIWidgets.Sparkle(), Color.white);
            UIFactory.Place(resultGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 300f));
            resultIcon = UIFactory.CreateImage(iconHolder, "Icon", null, Color.white);
            resultIcon.preserveAspect = true;
            UIFactory.Stretch(resultIcon.rectTransform);

            resultName = UIFactory.Label(resultCard, "Name", "", 38, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            resultStars = UIFactory.Label(resultCard, "Stars", "", 32, new Color(0.85f, 0.55f, 0.1f), TextAnchor.MiddleCenter);
            resultBody = UIFactory.Label(resultCard, "Body", "", 24, UIColors.Ink, TextAnchor.UpperCenter);
            resultFooter = UIFactory.Label(resultCard, "Footer", "", 20, UIColors.InkLight, TextAnchor.MiddleCenter);

            resultRoot.SetActive(false);
        }
    }
}
