using System;
using PotionFrontier.Alchemy;
using PotionFrontier.Game2D;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 2D 版アトリエの画面（シーンの AtelierHUD に付ける）。タイトル・ポーズ・設定・クリア・会話・レシピ本・取引先帳・相談メモ・お知らせ・
    /// 部屋を行き来する「＜」「＞」ボタン・
    /// 画面のフェードをまとめて持つ（中身は 3D 版から移した DialogueUI・RecipeBookUI・QuestTrackerUI などをそのまま使う）。
    /// 調合台の画面（BrewHUD2D）はこれとは別の Canvas で、調合台に向かっている間だけ出る。
    ///
    /// 絵を差し替えるところ：「UI の絵」欄の UISkin（Assets/Art2D/UI/UISkin.asset）に、羊皮紙・木の板・ボタン・コインなどの絵と
    /// フォントを入れる。空欄はコードで描いた仮の絵・OS の日本語フォントになる。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AtelierHUD : MonoBehaviour
    {
        public static AtelierHUD Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private PlayerProgress progress;
        [SerializeField] private QuestManager quests;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private BrewInput2D brewInput;
        [SerializeField] private AtelierView2D view;

        [Header("UI の絵")]
        [SerializeField, Tooltip("UI の絵とフォント（空欄はコードで描いた仮の絵・OS のフォント）")] private UISkin skin;

        private RectTransform root;
        private DialogueUI dialogue;
        private RecipeBookUI recipeBook;
        private IngredientBrowserUI ledger;
        private QuestTrackerUI tracker;
        private TitleScreenUI title;
        private PauseMenuUI pause;
        private ClearScreenUI clear;
        private SettingsPanel settings;

        private CanvasGroup gameplayGroup;
        private RectTransform trackerArea;
        private GameObject brewNav;
        private RoomArrow leftArrow;
        private RoomArrow rightArrow;

        /// <summary>部屋の左右の「＜」「＞」（大きな透明のボタン。行き先の名前も出す）</summary>
        private sealed class RoomArrow
        {
            public GameObject root;
            public Image back;
            public Text label;
            public float hover;
            public bool pointerInside;
        }
        private RectTransform toastPanel;
        private Text toastText;
        private CanvasGroup toastGroup;
        private float toastTimer;
        private RectTransform tipPanel;
        private Text tipText;
        private Text customerMark;
        private float markTime;
        private int lastModalCloseFrame = -1;

        public ScreenFader Fader { get; private set; }
        public RecipeBookUI RecipeBook => recipeBook;
        /// <summary>会話の窓（自動テストが読み進める）</summary>
        public DialogueUI Dialogue => dialogue;
        /// <summary>タイトル画面（自動テストが「はじめから」を押す）</summary>
        public TitleScreenUI Title => title;
        public bool IsSettingsOpen => settings != null && settings.IsOpen;
        public bool IsRecipeBookOpen => recipeBook != null && recipeBook.IsOpen;
        public bool IsLedgerOpen => ledger != null && ledger.IsOpen;

        /// <summary>何かのウィンドウ（タイトル含む）を開いていて、部屋や調合台の操作を止めるべきとき</summary>
        public bool IsModalOpen =>
            IsRecipeBookOpen || IsLedgerOpen || (dialogue != null && dialogue.IsOpen) || (pause != null && pause.IsOpen) ||
            (settings != null && settings.IsOpen) || (title != null && title.IsOpen) || (clear != null && clear.IsOpen);

        /// <summary>最後にウィンドウを閉じたフレーム（閉じたクリック・キーで、そのまま別の操作が起きないようにする）</summary>
        public int LastModalCloseFrame => Mathf.Max(lastModalCloseFrame, dialogue != null ? dialogue.ClosedFrame : -1);

        /// <summary>このフレームはプレイヤーの操作を受け付けない（ウィンドウ表示中・閉じた直後）</summary>
        public bool BlocksPlayerActions => IsModalOpen || LastModalCloseFrame == Time.frameCount;

        private void Awake()
        {
            Instance = this;
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            if (quests == null) quests = FindFirstObjectByType<QuestManager>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (brewInput == null) brewInput = FindFirstObjectByType<BrewInput2D>();
            if (view == null) view = FindFirstObjectByType<AtelierView2D>();
            UIFactory.Skin = skin;
            EnsureEventSystem();
            Build();
        }

        private void Start()
        {
            // レシピ本で名前を付けたら、棚の瓶・手に持っている瓶の名前も合わせる
            if (alchemy != null) alchemy.Liquids.Renamed += OnLiquidRenamed;
        }

        private void OnDestroy()
        {
            if (alchemy != null) alchemy.Liquids.Renamed -= OnLiquidRenamed;
            if (Instance == this) Instance = null;
        }

        private void OnLiquidRenamed(LiquidRecord record)
        {
            if (record == null) return;
            if (inventory != null) inventory.RefreshFrom(record);
            PotionStack held = brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null;
            if (held != null && (held.liquidId == record.id || held.potionId == record.id)) held.potionName = record.DisplayName;
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------

        private void Update()
        {
            // 再生開始直後は1フレームが数秒になることがあるので、アニメーションが飛ばないよう上限を付ける
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            GameFlow flow = GameFlow.Instance;
            bool playing = flow == null || flow.Mode == GameMode.Playing;
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
            else if (ledger.IsOpen) ledger.Tick(dt, allowInput && frameFree);
            title.Tick(dt, allowInput && frameFree && !settings.IsOpen);

            // R でレシピ本を開く（閉じるのは Esc・閉じるボタン。検索欄で r を打っても閉じないように）
            bool transitioning = view != null && view.Transitioning;
            if (playing && allowInput && frameFree && !IsModalOpen && !transitioning && Input.GetKeyDown(KeyCode.R)) OpenRecipeBook();

            UpdateGameplay(dt, playing);
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
            else if (ledger.IsOpen) CloseLedger();
            else if (dialogue.IsOpen || clear.IsOpen || title.IsOpen) { }
            else if (playing && view != null && view.InBrew)
            {
                // 調合台では：持っている物を戻す → もう一度で部屋に戻る
                if (view.Transitioning) return;
                if (brewInput != null && brewInput.Held != null) brewInput.ReturnHeld();
                else view.ExitBrew();
                MarkClosed();
            }
            else if (playing) OpenPause();
        }

        private void UpdateGameplay(float dt, bool playing)
        {
            bool visible = playing && !IsModalOpen;
            gameplayGroup.alpha = Mathf.MoveTowards(gameplayGroup.alpha, visible ? 1f : 0f, dt * 8f);
            gameplayGroup.blocksRaycasts = visible;
            bool inBrew = view != null && view.InBrew;
            if (brewNav.activeSelf != (inBrew && visible)) brewNav.SetActive(inBrew && visible);
            // 調合台では、右上のボタン（瓶に詰める など）の下に相談メモを出す
            float top = inBrew ? 236f : 0f;
            if (!Mathf.Approximately(trackerArea.offsetMax.y, -top)) UIFactory.Stretch(trackerArea, 0f, 0f, top, 0f);

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                toastGroup.alpha = Mathf.Clamp01(toastTimer / 0.4f);
            }
            if (tracker != null) tracker.Tick(dt);
            UpdateRoomTip(visible && !inBrew);
            UpdateCustomerMark(dt, visible && !inBrew);
            bool arrows = visible && !inBrew && view != null && !view.Transitioning;
            UpdateArrow(leftArrow, -1, arrows, dt);
            UpdateArrow(rightArrow, 1, arrows, dt);
        }

        private void UpdateArrow(RoomArrow arrow, int direction, bool show, float dt)
        {
            bool on = show && view.CanMove(direction);
            if (arrow.root.activeSelf != on) arrow.root.SetActive(on);
            if (!on) return;
            string text = (direction < 0 ? "＜\n" : "＞\n") + "<size=22>" + view.NeighborName(direction) + "</size>";
            if (arrow.label.text != text) arrow.label.text = text;
            // ふだんはほとんど透明。カーソルを乗せると、ふわっと浮かぶ
            arrow.hover = Mathf.MoveTowards(arrow.hover, arrow.pointerInside ? 1f : 0f, dt * 6f);
            arrow.back.color = new Color(0.08f, 0.05f, 0.03f, Mathf.Lerp(0.06f, 0.42f, arrow.hover));
            Color c = UIColors.Cream;
            c.a = Mathf.Lerp(0.45f, 1f, arrow.hover);
            arrow.label.color = c;
        }

        private void UpdateRoomTip(bool show)
        {
            show = show && view != null && !view.Transitioning && !string.IsNullOrEmpty(view.HoverTitle);
            if (tipPanel.gameObject.activeSelf != show) tipPanel.gameObject.SetActive(show);
            if (!show) return;
            string text = string.IsNullOrEmpty(view.HoverSubtitle) ? view.HoverTitle : view.HoverTitle + "\n<size=18>" + view.HoverSubtitle + "</size>";
            if (tipText.text != text) tipText.text = text;
            Vector2 p = Pointer2D.Position;
            bool left = p.x > Screen.width * 0.7f;
            tipPanel.pivot = new Vector2(left ? 1f : 0f, 1f);
            tipPanel.position = new Vector3(p.x + (left ? -24f : 24f), p.y - 24f, 0f);
        }

        /// <summary>カウンターのお客さんの頭の上に「！」（相談したそう）・「…」（待っている）を出す</summary>
        private void UpdateCustomerMark(float dt, bool show)
        {
            Customer2D customer = quests != null ? quests.CurrentCustomer : null;
            string mark = "";
            if (show && customer != null)
            {
                switch (quests.Phase)
                {
                    case CustomerPhase.Offering: mark = "！"; break;
                    case CustomerPhase.Waiting: mark = "…"; break;
                }
                if (customer.Mood == Customer2D.MoodKind.Happy) mark = "♪";
                else if (customer.Mood == Customer2D.MoodKind.Sad) mark = "…";
            }
            Camera cam = Camera.main;
            bool on = !string.IsNullOrEmpty(mark) && cam != null;
            if (customerMark.gameObject.activeSelf != on) customerMark.gameObject.SetActive(on);
            if (!on) return;
            markTime += dt;
            if (customerMark.text != mark) customerMark.text = mark;
            Vector3 sp = cam.WorldToScreenPoint(customer.HeadWorld);
            customerMark.rectTransform.position = new Vector3(sp.x, sp.y + 30f + Mathf.Sin(markTime * 4f) * 6f, 0f);
        }

        // ------------------------------------------------------------------
        // 呼び出し
        // ------------------------------------------------------------------

        public void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            toastText.text = message;
            toastTimer = 3.5f;
            toastGroup.alpha = 1f;
        }

        public void ShowDialogue(DialogueRequest request)
        {
            if (recipeBook.IsOpen) recipeBook.Close(false);
            dialogue.Open(request);
        }

        public void OpenRecipeBook()
        {
            if (recipeBook.IsOpen) return;
            recipeBook.Open(RecipeBookUI.Mode.Read);
        }

        public void ToggleRecipeBook()
        {
            if (recipeBook.IsOpen) CloseRecipeBook();
            else if (!IsModalOpen) OpenRecipeBook();
        }

        /// <summary>相談に乗るとき：分け与える瓶／教える作り方を選ぶためにレシピ本を開く</summary>
        public void OpenRecipeBookFor(RecipeBookUI.Mode mode, QuestData consultation, Action<LiquidRecord> pick)
        {
            if (recipeBook.IsOpen) recipeBook.Close(false);
            recipeBook.Open(mode, consultation, pick);
        }

        public void CloseRecipeBook()
        {
            if (!recipeBook.IsOpen) return;
            recipeBook.Close();
            MarkClosed();
        }

        // ------------------------------------------------------------------
        // 取引先帳（入手ルートの開拓・取り寄せ）
        // ------------------------------------------------------------------

        public void OpenLedger()
        {
            if (IsModalOpen) return;
            ledger.Open(IngredientBrowserUI.Mode.Shop, 0);
        }

        public void CloseLedger()
        {
            if (!ledger.IsOpen) return;
            ledger.Close();
            MarkClosed();
        }

        /// <summary>
        /// 開拓する（使い放題の素材：一度払えば、ずっと使い放題）／取り寄せる（希少な素材：数を決めて買う）。
        /// 実績（解決した相談の数）が足りない取引先とは、まだ取引できない
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

        public void ShowClear()
        {
            clear.Open(progress);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.brewSpecial, 0.9f);
        }

        public void OpenSettings()
        {
            settings.Open(null);
        }

        public void CloseSettings()
        {
            if (settings != null && settings.IsOpen) settings.Close();
        }

        private void CloseClear()
        {
            clear.Close();
            MarkClosed();
            ShowToast("これからも探求を続けて、町の人の相談に乗っていこう！");
        }

        public void ShowTitle(bool hasSave)
        {
            if (recipeBook.IsOpen) recipeBook.Close(false);
            if (ledger.IsOpen) ledger.Close();
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
            MarkClosed();
        }

        public void OpenPause()
        {
            GameFlow flow = GameFlow.Instance;
            if (flow == null || flow.Mode != GameMode.Playing || pause.IsOpen) return;
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

        private void MarkClosed()
        {
            lastModalCloseFrame = Time.frameCount;
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
            canvas.sortingOrder = 20; // 調合台の画面（BrewHUD）より手前
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            // 画面の比率が 16:9 でないとき（ウルトラワイド・Steam Deck など）も、UI を絵と同じ 16:9 の範囲に収める
            root = canvasGo.AddComponent<ViewportFit2D>().SafeRoot;

            // 奥から：プレイ中の UI → 会話 → レシピ本 → お知らせ → クリア → タイトル → ポーズ → 設定
            RectTransform gameplay = UIFactory.CreateRect("Gameplay", root);
            UIFactory.Stretch(gameplay);
            gameplayGroup = gameplay.gameObject.AddComponent<CanvasGroup>();
            gameplayGroup.alpha = 0f;

            trackerArea = UIFactory.CreateRect("TrackerArea", gameplay);
            UIFactory.Stretch(trackerArea);
            tracker = new QuestTrackerUI(trackerArea, quests, progress,
                () => brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null);
            // 相談メモは見るだけ（下の部屋・調合台のクリックをさえぎらない）
            foreach (Graphic g in trackerArea.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            BuildBrewNav(gameplay);
            leftArrow = BuildArrow(gameplay, -1);
            rightArrow = BuildArrow(gameplay, 1);
            BuildRoomTip(gameplay);
            customerMark = UIFactory.Label(gameplay, "CustomerMark", "", 52, UIColors.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Place(customerMark.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(80f, 70f));
            customerMark.raycastTarget = false;
            customerMark.gameObject.SetActive(false);

            dialogue = new DialogueUI(root);
            recipeBook = new RecipeBookUI(root, () => alchemy, inventory,
                () => quests != null && quests.Phase == CustomerPhase.Waiting ? quests.CurrentQuest : null,
                () => quests != null ? quests.TeachRewardRate : 0.7f, CloseRecipeBook);
            // 取引先帳（3D 版の素材箱・取引先帳の画面を、取引先帳としてだけ使う）
            ledger = new IngredientBrowserUI(root, ingredients, inventory, progress,
                () => alchemy != null ? alchemy.Settings : null,
                id => alchemy != null ? alchemy.Liquids.Get(id) : null,
                _ => null, CloseLedger, _ => { }, _ => { }, TradeIngredient);

            RectTransform toastLayer = UIFactory.CreateRect("ToastLayer", root);
            UIFactory.Stretch(toastLayer);
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

            // 画面の部品ごとに Canvas を分ける（動く部品が変わっても、ほかの部品まで作り直さずに済む）
            foreach (Transform child in root)
            {
                child.gameObject.AddComponent<Canvas>();
                if (child != toastLayer) child.gameObject.AddComponent<GraphicRaycaster>();
            }

            Fader = new ScreenFader(transform);
        }

        /// <summary>調合台にいる間の、左下のボタン（レシピ本・部屋に戻る）</summary>
        private void BuildBrewNav(RectTransform parent)
        {
            RectTransform nav = UIFactory.CreateRect("BrewNav", parent);
            UIFactory.Place(nav, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 50f), new Vector2(250f, 150f));
            UIFactory.Vertical(nav.gameObject, new RectOffset(0, 0, 0, 0), 10f, TextAnchor.LowerLeft);
            UIWidgets.WoodButton(nav, "レシピ本 R", 24, new Vector2(240f, 60f), ToggleRecipeBook);
            UIWidgets.WoodButton(nav, "部屋に戻る Esc", 24, new Vector2(240f, 60f), () =>
            {
                if (view != null && view.InBrew && !view.Transitioning)
                {
                    if (brewInput != null) brewInput.ReturnHeld();
                    view.ExitBrew();
                }
            });
            brewNav = nav.gameObject;
            brewNav.SetActive(false);
        }

        /// <summary>部屋の左右の端の「＜」「＞」（高さいっぱいの、ほとんど透明なボタン）</summary>
        private RoomArrow BuildArrow(RectTransform parent, int direction)
        {
            var arrow = new RoomArrow();
            Image back = UIFactory.CreateImage(parent, direction < 0 ? "RoomArrowLeft" : "RoomArrowRight",
                UIFactory.RoundedRect(Color.white, new Color(0f, 0f, 0f, 0f), 24, 0), new Color(0.08f, 0.05f, 0.03f, 0.06f));
            Vector2 anchor = new Vector2(direction < 0 ? 0f : 1f, 0.5f);
            UIFactory.Place(back.rectTransform, anchor, anchor, new Vector2(direction < 0 ? 16f : -16f, 40f), new Vector2(130f, 560f));
            back.raycastTarget = true;
            var button = back.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                if (view != null) view.MoveStop(direction);
            });
            var trigger = back.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => arrow.pointerInside = true);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => arrow.pointerInside = false);
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
            Text label = UIFactory.Label(back.rectTransform, "Label", "", 72, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(label.rectTransform);
            label.raycastTarget = false;
            label.supportRichText = true;
            arrow.root = back.gameObject;
            arrow.back = back;
            arrow.label = label;
            arrow.root.SetActive(false);
            return arrow;
        }

        /// <summary>部屋で指している物の名前（カーソルのそば）</summary>
        private void BuildRoomTip(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "RoomTip", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            tipPanel = panel.rectTransform;
            UIFactory.Place(tipPanel, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(420f, 80f));
            panel.raycastTarget = false;
            tipText = UIFactory.Label(tipPanel, "Text", "", 24, UIColors.Cream, TextAnchor.UpperLeft);
            UIFactory.Stretch(tipText.rectTransform, 16f, 16f, 10f, 8f);
            tipText.raycastTarget = false;
            tipPanel.gameObject.SetActive(false);
        }

        private void BuildToast(RectTransform parent)
        {
            Image panel = UIFactory.CreateImage(parent, "Toast", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 14, 0), Color.white);
            toastPanel = panel.rectTransform;
            UIFactory.Place(toastPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1000f, 60f));
            panel.raycastTarget = false;
            toastText = UIFactory.Label(toastPanel, "Text", "", 26, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(toastText.rectTransform, 20f, 20f, 6f, 6f);
            toastText.raycastTarget = false;
            toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastGroup.alpha = 0f;
        }
    }
}
