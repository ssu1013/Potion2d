using System;
using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game2D;
using UnityEngine;

namespace PotionFrontier.Game
{
    public enum GameMode { Title, Playing }

    /// <summary>
    /// ゲーム全体の流れ（1画面版）：タイトル ⇄ 調合机、ポーズ、セーブ／ロード。シーンの GameSystems に付ける。
    /// 画面は1つだけ。タイトルも本も、同じ調合机の上に重ねて出す（カメラの移動はない）。
    /// タイトルから遊びはじめるときだけ、1枚絵のロード画面（LoadingScreenUI。2〜3秒）をはさむ。
    /// 発見が進むと、新しい素材が自動で机に届く（DiscoveryProgress）。
    /// クリアや終わりはない：伝説の特効薬を作ったあとも、いつまでも調合を続けられる。
    /// セーブは自動（瓶に詰めたとき・素材が届いたとき・タイトルに戻るとき・終了するとき・
    /// ウィンドウが裏に回ったとき・遊んでいる間の一定時間ごと）。強制終了や電源断でも、失うのは最後の数十秒だけ。
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private BrewSession2D session;
        [SerializeField] private BrewInput2D brewInput;
        [SerializeField] private AtelierView2D view;

        [Header("自動セーブ")]
        [SerializeField, Min(0f), Tooltip("遊んでいる間、この秒数ごとに自動でセーブする（0 = しない）。ほかに、瓶に詰めたとき・素材が届いたとき・ウィンドウが裏に回ったとき・終了するときにもセーブする")]
        private float autosaveSeconds = 60f;

        private bool transitioning;
        private bool checkArrivals;
        private bool quitting;
        private FlaskSaveData pendingFlask;
        private float autosaveTimer;
        private float lastSaveTime = -999f;
        private float lastSaveWarning = -999f;
        /// <summary>読み込みで困ったことがあったときに、調合机に着いてから出すお知らせ</summary>
        private string loadNotice;

        public GameMode Mode { get; private set; } = GameMode.Title;
        public bool IsPaused { get; private set; }
        /// <summary>これまでに瓶に詰めた回数</summary>
        public int BrewCount { get; private set; }

        public event Action<GameMode> ModeChanged;

        /// <summary>今の発見のポイント（性質1・反応2・新しい液体1）</summary>
        public int DiscoveryPoints => DiscoveryProgress.Points(GameContext.Ingredients, alchemy != null ? alchemy.Liquids : null);

        /// <summary>次に届く素材と、それまでの残りのポイント（すべて届いていれば null）</summary>
        public IngredientData NextArrival(out int remaining)
        {
            return DiscoveryProgress.NextArrival(GameContext.Ingredients, ingredients, DiscoveryPoints, out remaining);
        }

        private void Awake()
        {
            Instance = this;
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (session == null) session = FindFirstObjectByType<BrewSession2D>();
            if (brewInput == null) brewInput = FindFirstObjectByType<BrewInput2D>();
            if (view == null) view = FindFirstObjectByType<AtelierView2D>();
        }

        private void OnEnable()
        {
            DiscoveryJournal.Changed += OnDiscovery;
            if (alchemy != null)
            {
                alchemy.Liquids.Discovered += OnLiquidDiscovered;
                alchemy.Bottled += OnBottled;
            }
        }

        private void OnDisable()
        {
            DiscoveryJournal.Changed -= OnDiscovery;
            if (alchemy != null)
            {
                alchemy.Liquids.Discovered -= OnLiquidDiscovered;
                alchemy.Bottled -= OnBottled;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            yield return null; // 画面（AtelierHUD）と音（AudioManager）の準備を待つ
            EnterTitleImmediate();
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) yield return hud.Fader.FadeIn(1.2f);
        }

        private void LateUpdate()
        {
            TickAutosave();
            // 発見のあと、同じフレームにまとめて届ける（性質と液体が同時に増えても、お知らせは1回ずつ）
            if (!checkArrivals) return;
            checkArrivals = false;
            if (Mode != GameMode.Playing || transitioning) return;
            List<IngredientData> arrived = DiscoveryProgress.Deliver(GameContext.Ingredients, ingredients, DiscoveryPoints);
            if (arrived.Count > 0) Save();
        }

        /// <summary>遊んでいる間の自動セーブ（ポーズ中・切り替えの途中は数えない）</summary>
        private void TickAutosave()
        {
            if (autosaveSeconds <= 0f || Mode != GameMode.Playing || transitioning || IsPaused) return;
            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer >= autosaveSeconds) Save();
        }

        private void OnDiscovery() => checkArrivals = true;
        private void OnLiquidDiscovered(LiquidRecord record) => checkArrivals = true;

        /// <summary>瓶に詰めた回数を数える（どんな薬を作っても、ゲームは止まらない）</summary>
        private void OnBottled(LiquidRecord record, bool isNew)
        {
            if (record == null || Mode != GameMode.Playing) return;
            BrewCount++;
        }

        // ------------------------------------------------------------------
        // タイトル ⇄ 調合机
        // ------------------------------------------------------------------

        /// <summary>はじめから遊ぶ（それまでのセーブは SaveSystem が .prev.json に1つだけ残す）。notice は調合机に着いてから出すお知らせ</summary>
        public void StartNewGame() => StartNewGame(null);

        private void StartNewGame(string notice)
        {
            if (transitioning) return;
            loadNotice = notice;
            SaveSystem.Delete();
            if (inventory != null) inventory.Clear();
            if (ingredients != null) ingredients.ResetToStarter(GameContext.Balance);
            if (alchemy != null) alchemy.Liquids.Clear();
            DiscoveryJournal.Clear();
            BrewCount = 0;
            pendingFlask = null;
            StartCoroutine(BeginPlay("静かな夜の調合机へ、ようこそ。発見が進むと、新しい素材が届く"));
        }

        public void ContinueGame()
        {
            if (transitioning) return;
            SaveData data = SaveSystem.Load();
            if (data == null)
            {
                // ファイルはあるのに読めなかった（壊れていた）ときは、そのことを知らせる（元のファイルは SaveSystem が残している）
                StartNewGame(SaveSystem.Exists ? "セーブデータを読み込めなかったので、はじめから始めます（元のファイルは残してあります）" : null);
                return;
            }
            loadNotice = null;
            try
            {
                Import(data);
            }
            catch (Exception e)
            {
                // 思わぬ形のセーブでも、遊べるところまでは読み込んで続ける。元のファイルは、上書きする前に控えを残す
                Debug.LogException(e, this);
                SaveSystem.KeepCopy("loadfail");
                loadNotice = "セーブデータの一部を読み込めませんでした（元のファイルは残してあります）";
            }
            BrewCount = Mathf.Max(0, data.brewCount);
            pendingFlask = data.version >= 3 && data.flaskState != null && data.flaskState.version > 0 ? data.flaskState : null;
            StartCoroutine(BeginPlay($"おかえりなさい（{data.savedAt} のつづき）"));
        }

        /// <summary>セーブの中身を、在庫・液体の記録・素材ノートに読み込む（古い版は足りない物を補う）</summary>
        private void Import(SaveData data)
        {
            if (inventory != null) inventory.Import(data.potions);
            if (ingredients != null)
            {
                if (data.version < 2) ingredients.ResetToStarter(GameContext.Balance);
                else ingredients.Import(data.version >= 4 ? data.unlockedIngredients : null, data.version < 7 ? data.ingredients : null);
                // 古いセーブでも、はじめから机にある素材は必ずある
                GameBalance balance = GameContext.Balance;
                if (balance != null) foreach (IngredientAmount e in balance.startingIngredients) if (e != null && e.ingredient != null) ingredients.Unlock(e.ingredient, false);
            }
            if (alchemy != null)
            {
                alchemy.Liquids.Import(data.version >= 3 ? data.liquids : null);
                if (inventory != null) inventory.EnsureRecords(alchemy.Liquids);
            }
            // 素材ノート（版6から）。古いセーブでは、作った液体の素材を「使ったことがある」にしておく
            DiscoveryJournal.Import(data.version >= 6 ? data.notes : null, data.version >= 6 ? data.knownReactions : null);
            if (data.version < 6 && alchemy != null)
            {
                IngredientDatabase db = GameContext.Ingredients;
                foreach (LiquidRecord r in alchemy.Liquids.All)
                {
                    foreach (string id in r.ingredientIds) DiscoveryJournal.Learn(db != null ? db.Get(id) : null, IngredientFact.Used);
                }
            }
            // 発見のポイントで届くはずの素材は、お知らせなしで机に並べておく（届く時期を調整したとき・古いセーブ）
            DiscoveryProgress.Deliver(GameContext.Ingredients, ingredients, DiscoveryPoints, false);
        }

        public void ReturnToTitle()
        {
            if (transitioning) return;
            Save();
            EnterTitleImmediate();
        }

        public void Pause()
        {
            if (Mode != GameMode.Playing || IsPaused) return;
            IsPaused = true;
            Time.timeScale = 0f;
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = 1f;
        }

        public void QuitGame()
        {
            // 連打しても、セーブと終了は1回だけ
            if (quitting) return;
            quitting = true;
            Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>今の状態をセーブする（プレイ中だけ）。失敗しても遊びは止めず、画面に小さく知らせる</summary>
        public void Save()
        {
            if (Mode != GameMode.Playing) return;
            autosaveTimer = 0f;
            lastSaveTime = Time.unscaledTime;
            SaveData data;
            try
            {
                data = Collect();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                NotifySaveProblem();
                return;
            }
            if (!SaveSystem.Save(data)) NotifySaveProblem();
        }

        /// <summary>セーブに書く中身を集める</summary>
        private SaveData Collect()
        {
            var data = new SaveData { brewCount = BrewCount };
            data.potions = inventory != null ? inventory.Export() : new List<PotionStack>();
            // 手に持っている瓶は、トレイに戻したことにして保存する
            PotionStack held = brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null;
            if (held != null && held.ml >= PotionStack.EmptyMl) data.potions.Add(held.CloneWith(held.ml));
            if (ingredients != null) data.unlockedIngredients = ingredients.ExportUnlocked();
            if (session != null) data.flaskState = session.ExportFlask();
            if (alchemy != null) data.liquids = alchemy.Liquids.Export();
            data.notes = DiscoveryJournal.ExportNotes();
            data.knownReactions = DiscoveryJournal.ExportReactions();
            return data;
        }

        /// <summary>セーブできなかったことを知らせる（何度も失敗しても、1分に1回まで）</summary>
        private void NotifySaveProblem()
        {
            if (Time.unscaledTime - lastSaveWarning < 60f) return;
            lastSaveWarning = Time.unscaledTime;
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast("セーブできませんでした（ディスクの空きや、保存先のフォルダを確かめてください）");
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            Save();
            GameSettings.Save();
        }

        /// <summary>ウィンドウが裏に回った（Alt+Tab・最小化）：その場でセーブする（裏に回ったまま PC を落とされても大丈夫なように）</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SaveInBackground();
        }

        /// <summary>OS にアプリを止められる直前（スリープなど）</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveInBackground();
        }

        private void SaveInBackground()
        {
            GameSettings.Save();
            // フォーカスの出入りが続いても、書くのは1秒に1回まで
            if (quitting || Time.unscaledTime - lastSaveTime < 1f) return;
            Save();
        }

        /// <summary>
        /// タイトルから遊びはじめる：ロゴとボタンがすっと消え、タイトルの1枚絵がロード画面の1枚絵（右下に Loading...）へ
        /// 静かに入れ替わり、2〜3秒で調合机が現れる。絵が出ている間に、後ろでタイトルを片付けてフラスコを用意する。環境音は途切れない。
        /// ロード画面をはさまない設定（AtelierHUD の「タイトル → 調合机の切り替え」）なら、タイトルの1枚絵そのものがゆっくり消えて調合机が現れる
        /// </summary>
        private IEnumerator BeginPlay(string welcome)
        {
            transitioning = true;
            AtelierHUD hud = AtelierHUD.Instance;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.startGame, 0.45f);
            TitleScreenUI title = hud != null ? hud.Title : null;
            LoadingScreenUI card = hud != null && hud.UseLoadingScreen ? hud.Loading : null;
            if (card != null)
            {
                // タイトルの文字とボタンが消え、1枚絵はロード画面の絵が上から静かに覆う
                if (title != null) title.BeginLeave(false, 0f);
                // タイトルの曲から調合机の曲へ、ロード画面の間にゆっくり入れ替える（環境音はそのまま）
                if (lib != null)
                {
                    AudioManager.PlayBgm(lib.bgmAtelier, 2.5f);
                    AudioManager.PlayAmbient(lib.ambientRoom);
                }
                card.Show();
                while (card.IsOpen && !card.Covered) yield return null;
            }

            // ロード画面（またはタイトル）の絵が出ている間に、机の上を用意する
            if (brewInput != null) brewInput.ReturnHeld();
            if (session != null)
            {
                if (pendingFlask != null) session.ImportFlask(pendingFlask);
                else session.ResetFlask();
            }
            pendingFlask = null;
            if (view != null) view.Place();

            if (card != null)
            {
                // タイトルは、ロード画面の絵の後ろで片付ける
                if (hud != null) hud.ShowGameplay();
                // ロード画面が消えはじめるのを待つ
                while (card.IsOpen && !card.Revealing) yield return null;
            }
            else if (title != null && title.IsOpen)
            {
                // ロード画面をはさまない：タイトルの1枚絵がゆっくり消えて、後ろの調合机が見えてくる
                if (lib != null)
                {
                    AudioManager.PlayBgm(lib.bgmAtelier, 2.5f);
                    AudioManager.PlayAmbient(lib.ambientRoom);
                }
                title.BeginLeave(true, hud != null ? hud.TitleFadeSeconds : 1.2f);
            }

            Mode = GameMode.Playing;
            if (ModeChanged != null) ModeChanged(Mode);
            if (lib != null)
            {
                AudioManager.PlayBgm(lib.bgmAtelier, 2f);
                AudioManager.PlayAmbient(lib.ambientRoom);
            }
            if (card != null) while (card.IsOpen) yield return null;
            else if (title != null) while (title.IsOpen && !title.LeaveFinished) yield return null;
            if (hud != null) hud.ShowGameplay();
            if (hud != null) hud.ShowToast(string.IsNullOrEmpty(loadNotice) ? welcome : loadNotice);
            loadNotice = null;
            autosaveTimer = 0f;
            transitioning = false;
            checkArrivals = true;
        }

        private void EnterTitleImmediate()
        {
            Resume();
            if (brewInput != null) brewInput.ReturnHeld();
            if (view != null) view.Place();
            Mode = GameMode.Title;
            if (ModeChanged != null) ModeChanged(Mode);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null)
            {
                AudioManager.PlayBgm(lib.bgmTitle, 1.5f);
                AudioManager.PlayAmbient(lib.ambientRoom);
            }
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowTitle(SaveSystem.Exists);
        }
    }
}
