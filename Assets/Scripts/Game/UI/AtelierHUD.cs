using PotionFrontier.Alchemy;
using PotionFrontier.Game2D;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 調合机の上に重ねる画面（シーンの AtelierHUD に付ける）。1画面版なので、ほかの画面へは移らない。
    /// タイトル・ロード画面・ポーズ・設定・レシピ本・素材ノート・お知らせ・新しい素材が届いたお知らせ・画面のフェードをまとめて持つ。
    /// 本（レシピ本・素材ノート）は机の上に広げるように出し、後ろは少しだけ暗くする（ランプの火のゆらぎ・環境音はそのまま）。
    /// 調合机の操作の画面（BrewHUD2D）はこれとは別の Canvas。
    ///
    /// 絵を差し替えるところ：「UI の絵」欄の UISkin（Assets/Art2D/UI/UISkin.asset）に、羊皮紙・木の板・ボタン・手紙などの絵と
    /// フォントを入れる。空欄はコードで描いた仮の絵・OS の日本語フォントになる。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AtelierHUD : MonoBehaviour
    {
        public static AtelierHUD Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private BrewInput2D brewInput;

        [Header("UI の絵")]
        [SerializeField, Tooltip("UI の絵とフォント（空欄はコードで描いた仮の絵・OS のフォント）")] private UISkin skin;

        [Header("ロード画面の長さ（秒。合計で 2〜3 秒が目安）")]
        [SerializeField] private LoadingTiming loadingTiming = new LoadingTiming();

        private RectTransform root;
        private RecipeBookUI recipeBook;
        private IngredientNotebookUI notebook;
        private TitleScreenUI title;
        private PauseMenuUI pause;
        private LoadingScreenUI loading;
        private SettingsPanel settings;
        private ArrivalNoticeUI arrival;

        private CanvasGroup gameplayGroup;
        private GameObject deskNav;
        private RectTransform toastPanel;
        private Text toastText;
        private CanvasGroup toastGroup;
        private float toastTimer;
        private int lastModalCloseFrame = -1;

        public ScreenFader Fader { get; private set; }
        public RecipeBookUI RecipeBook => recipeBook;
        /// <summary>タイトル画面（自動テストが「はじめから」を押す）</summary>
        public TitleScreenUI Title => title;
        /// <summary>新しい素材が届いたお知らせ（自動テストが確かめる）</summary>
        public ArrivalNoticeUI Arrival => arrival;
        public bool IsSettingsOpen => settings != null && settings.IsOpen;
        public bool IsRecipeBookOpen => recipeBook != null && recipeBook.IsOpen;
        public bool IsNotebookOpen => notebook != null && notebook.IsOpen;
        public bool IsPauseOpen => pause != null && pause.IsOpen;
        public bool IsLoadingOpen => loading != null && loading.IsOpen;
        /// <summary>ロード画面（GameFlow が、タイトルから遊びはじめるときに出す）</summary>
        public LoadingScreenUI Loading => loading;
        /// <summary>素材ノート（自動テストが開いて確かめる）</summary>
        public IngredientNotebookUI Notebook => notebook;
        /// <summary>いま出ているお知らせの文（自動テスト用）</summary>
        public string ToastText => toastTimer > 0f && toastText != null ? toastText.text : "";

        /// <summary>何かのウィンドウ（タイトル含む）を開いていて、調合机の操作を止めるべきとき</summary>
        public bool IsModalOpen =>
            IsRecipeBookOpen || IsNotebookOpen || IsPauseOpen || IsSettingsOpen || (title != null && title.IsOpen) || IsLoadingOpen;

        /// <summary>最後にウィンドウを閉じたフレーム（閉じたクリック・キーで、そのまま別の操作が起きないようにする）</summary>
        public int LastModalCloseFrame => lastModalCloseFrame;

        /// <summary>このフレームはプレイヤーの操作を受け付けない（ウィンドウ表示中・閉じた直後）</summary>
        public bool BlocksPlayerActions => IsModalOpen || LastModalCloseFrame == Time.frameCount;

        private void Awake()
        {
            Instance = this;
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (brewInput == null) brewInput = FindFirstObjectByType<BrewInput2D>();
            UIFactory.Skin = skin;
            EnsureEventSystem();
            Build();
        }

        private void Start()
        {
            // レシピ本で名前を付けたら、トレイの瓶・手に持っている瓶の名前も合わせる
            if (alchemy != null) alchemy.Liquids.Renamed += OnLiquidRenamed;
            // 新しい素材が届いたら、右下に小さな手紙を出す
            if (ingredients != null) ingredients.Arrived += OnIngredientArrived;
        }

        private void OnDestroy()
        {
            if (alchemy != null) alchemy.Liquids.Renamed -= OnLiquidRenamed;
            if (ingredients != null) ingredients.Arrived -= OnIngredientArrived;
            if (Instance == this) Instance = null;
        }

        private void OnLiquidRenamed(LiquidRecord record)
        {
            if (record == null) return;
            if (inventory != null) inventory.RefreshFrom(record);
            PotionStack held = brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null;
            if (held != null && (held.liquidId == record.id || held.potionId == record.id)) held.potionName = record.DisplayName;
        }

        private void OnIngredientArrived(IngredientData data)
        {
            if (data == null || arrival == null) return;
            GameFlow flow = GameFlow.Instance;
            if (flow != null && flow.Mode != GameMode.Playing) return;
            arrival.Show(data);
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

            loading.Tick(dt);

            // 一番手前のウィンドウだけがキー入力を受け取る
            bool frameFree = LastModalCloseFrame != Time.frameCount;
            if (settings.IsOpen) settings.Tick(dt, allowInput && frameFree);
            else if (pause.IsOpen) pause.Tick(dt, allowInput && frameFree);
            else if (recipeBook.IsOpen) recipeBook.Tick(dt, allowInput && frameFree);
            else if (notebook.IsOpen)
            {
                notebook.Tick(dt, allowInput && frameFree);
                if (allowInput && frameFree && Input.GetKeyDown(KeyCode.M)) CloseNotebook();
            }
            title.Tick(dt, allowInput && frameFree && !settings.IsOpen && !loading.IsOpen);

            // R でレシピ本を開く（閉じるのは Esc・閉じるボタン。検索欄で r を打っても閉じないように）
            if (playing && allowInput && frameFree && !IsModalOpen && Input.GetKeyDown(KeyCode.R)) OpenRecipeBook();
            // M で素材ノート
            // （閉じたのと同じフレームで、また開かないように、閉じたフレームかをここで調べ直す）
            if (playing && allowInput && LastModalCloseFrame != Time.frameCount && !IsModalOpen && Input.GetKeyDown(KeyCode.M)) OpenNotebook();

            UpdateGameplay(dt, playing);
        }

        private void HandleEscape(bool playing)
        {
            if (settings.IsOpen) settings.Close();
            else if (loading.IsOpen) { }
            else if (title.IsOpen && title.IsConfirmOpen) title.CloseConfirm();
            else if (pause.IsOpen) ClosePause();
            else if (recipeBook.IsOpen)
            {
                // 名前を打っている途中なら、名前を付けるのをやめるだけ
                if (recipeBook.IsRenaming) recipeBook.CancelRename();
                else CloseRecipeBook();
            }
            else if (notebook.IsOpen) CloseNotebook();
            else if (title.IsOpen) { }
            else if (playing && brewInput != null && brewInput.Held != null)
            {
                // 持っている物があれば、まずトレイに戻す → もう一度でひとやすみ
                brewInput.ReturnHeld();
                MarkClosed();
            }
            else if (playing) OpenPause();
        }

        private void UpdateGameplay(float dt, bool playing)
        {
            bool visible = playing && !IsModalOpen;
            gameplayGroup.alpha = Mathf.MoveTowards(gameplayGroup.alpha, visible ? 1f : 0f, dt * 8f);
            gameplayGroup.blocksRaycasts = visible;
            if (deskNav.activeSelf != visible) deskNav.SetActive(visible);

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                toastGroup.alpha = Mathf.Clamp01(toastTimer / 0.4f);
            }
            // 届いたお知らせは、本を開いていても静かに出しておく（タイトルでは消す）
            if (playing) arrival.Tick(dt);
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

        public void OpenRecipeBook()
        {
            if (recipeBook.IsOpen) return;
            if (notebook.IsOpen) notebook.Close();
            recipeBook.Open();
        }

        public void ToggleRecipeBook()
        {
            if (recipeBook.IsOpen) CloseRecipeBook();
            else if (!IsModalOpen || notebook.IsOpen) OpenRecipeBook();
        }

        public void CloseRecipeBook()
        {
            if (!recipeBook.IsOpen) return;
            recipeBook.Close();
            MarkClosed();
        }

        public void OpenNotebook(IngredientData focus = null)
        {
            if (notebook.IsOpen) return;
            if (recipeBook.IsOpen) recipeBook.Close(false);
            if (IsModalOpen) return;
            notebook.Open(focus);
        }

        public void CloseNotebook()
        {
            if (!notebook.IsOpen) return;
            notebook.Close();
            MarkClosed();
        }

        public void OpenSettings()
        {
            settings.Open(null);
        }

        public void CloseSettings()
        {
            if (settings != null && settings.IsOpen) settings.Close();
        }

        public void ShowTitle(bool hasSave)
        {
            if (recipeBook.IsOpen) recipeBook.Close(false);
            if (notebook.IsOpen) notebook.Close();
            pause.Close();
            loading.Close();
            if (settings.IsOpen) settings.Close();
            arrival.Clear();
            toastTimer = 0f;
            toastGroup.alpha = 0f;
            gameplayGroup.alpha = 0f;
            title.Show(hasSave, hasSave ? SaveSystem.Load() : null);
        }

        public void ShowGameplay()
        {
            title.Hide();
            MarkClosed();
        }

        public void OpenPause()
        {
            GameFlow flow = GameFlow.Instance;
            if (flow == null || flow.Mode != GameMode.Playing || pause.IsOpen) return;
            flow.Pause();
            pause.Open(Summary());
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

        /// <summary>ポーズに出す、探求の進み具合</summary>
        private string Summary()
        {
            GameFlow flow = GameFlow.Instance;
            IngredientDatabase db = GameContext.Ingredients;
            int points = flow != null ? flow.DiscoveryPoints : 0;
            int liquids = alchemy != null ? alchemy.Liquids.Count : 0;
            int onDesk = ingredients != null ? ingredients.CountKinds(db) : 0;
            int total = db != null ? db.Count : onDesk;
            int bottles = inventory != null ? inventory.TotalCount : 0;
            string line1 = $"発見　<b>{points}</b>　／　見つけた液体　<b>{liquids} 種類</b>";
            string line2 = $"机の素材　<b>{onDesk} / {total}</b>　／　トレイの瓶　<b>{bottles} 本</b>";
            if (flow != null && flow.BrewCount > 0) line2 += $"\n<size=22><color=#7A5A38>これまでに瓶に詰めた回数：{flow.BrewCount}</color></size>";
            string next = "";
            int remaining = 0;
            IngredientData coming = flow != null ? flow.NextArrival(out remaining) : null;
            if (coming != null) next = $"\n<size=22><color=#7A5A38>次の素材が届くまで：発見あと {remaining}</color></size>";
            else if (total > 0) next = "\n<size=22><color=#7A5A38>すべての素材が机にそろった</color></size>";
            return line1 + "\n" + line2 + next;
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
            canvas.sortingOrder = 20; // 調合机の操作の画面（BrewHUD）より手前
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            // 画面の比率が 16:9 でないとき（ウルトラワイド・Steam Deck など）も、UI を絵と同じ 16:9 の範囲に収める
            root = canvasGo.AddComponent<ViewportFit2D>().SafeRoot;

            // 奥から：プレイ中の UI → レシピ本・素材ノート → お知らせ → タイトル → ロード画面 → ポーズ → 設定
            RectTransform gameplay = UIFactory.CreateRect("Gameplay", root);
            UIFactory.Stretch(gameplay);
            gameplayGroup = gameplay.gameObject.AddComponent<CanvasGroup>();
            gameplayGroup.alpha = 0f;
            BuildDeskNav(gameplay);

            recipeBook = new RecipeBookUI(root, () => alchemy, inventory, CloseRecipeBook);
            // 素材ノート（発見の記録）
            notebook = new IngredientNotebookUI(root, ingredients, () => alchemy, CloseNotebook);

            RectTransform toastLayer = UIFactory.CreateRect("ToastLayer", root);
            UIFactory.Stretch(toastLayer);
            BuildToast(toastLayer);
            arrival = new ArrivalNoticeUI(toastLayer);

            title = new TitleScreenUI(root,
                () => { if (GameFlow.Instance != null) GameFlow.Instance.StartNewGame(); },
                () => { if (GameFlow.Instance != null) GameFlow.Instance.ContinueGame(); },
                OpenSettings,
                () => { if (GameFlow.Instance != null) GameFlow.Instance.QuitGame(); });
            loading = new LoadingScreenUI(root, loadingTiming);
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

        /// <summary>机の左下のボタン（レシピ本・素材ノート）</summary>
        private void BuildDeskNav(RectTransform parent)
        {
            RectTransform nav = UIFactory.CreateRect("DeskNav", parent);
            UIFactory.Vertical(nav.gameObject, new RectOffset(0, 0, 0, 0), 10f, TextAnchor.LowerLeft);
            UIFactory.Place(nav, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 50f), new Vector2(250f, 150f));
            UIWidgets.WoodButton(nav, "レシピ本 R", 24, new Vector2(240f, 60f), ToggleRecipeBook);
            UIWidgets.WoodButton(nav, "素材ノート M", 24, new Vector2(240f, 60f), () => OpenNotebook());
            deskNav = nav.gameObject;
            deskNav.SetActive(false);
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
