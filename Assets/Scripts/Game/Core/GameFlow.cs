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
    /// 画面は1つだけ。タイトルも本も、同じ調合机の上に重ねて出す（暗転・カメラの移動はない）。
    /// 発見が進むと、新しい素材が自動で机に届く（DiscoveryProgress）。伝説の特効薬を初めて作るとクリア。
    /// セーブは自動（瓶に詰めたとき・素材が届いたとき・タイトルに戻るとき・終了するとき）。
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public const string FinalPotionId = "special_legendary_cure";

        public static GameFlow Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private BrewSession2D session;
        [SerializeField] private BrewInput2D brewInput;
        [SerializeField] private AtelierView2D view;

        private bool transitioning;
        private bool checkArrivals;
        private FlaskSaveData pendingFlask;

        public GameMode Mode { get; private set; } = GameMode.Title;
        public bool IsPaused { get; private set; }
        /// <summary>伝説の特効薬を作った</summary>
        public bool GameCleared { get; private set; }

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
            // 発見のあと、同じフレームにまとめて届ける（性質と液体が同時に増えても、お知らせは1回ずつ）
            if (!checkArrivals) return;
            checkArrivals = false;
            if (Mode != GameMode.Playing || transitioning) return;
            List<IngredientData> arrived = DiscoveryProgress.Deliver(GameContext.Ingredients, ingredients, DiscoveryPoints);
            if (arrived.Count > 0) Save();
        }

        private void OnDiscovery() => checkArrivals = true;
        private void OnLiquidDiscovered(LiquidRecord record) => checkArrivals = true;

        private void OnBottled(LiquidRecord record, bool isNew)
        {
            if (record == null || GameCleared || Mode != GameMode.Playing || record.specialPotionId != FinalPotionId) return;
            GameCleared = true;
            Save();
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowClear();
        }

        // ------------------------------------------------------------------
        // タイトル ⇄ 調合机
        // ------------------------------------------------------------------

        public void StartNewGame()
        {
            if (transitioning) return;
            SaveSystem.Delete();
            if (inventory != null) inventory.Clear();
            if (ingredients != null) ingredients.ResetToStarter(GameContext.Balance);
            if (alchemy != null) alchemy.Liquids.Clear();
            DiscoveryJournal.Clear();
            GameCleared = false;
            pendingFlask = null;
            StartCoroutine(BeginPlay("静かな夜の調合机へ、ようこそ。発見が進むと、新しい素材が届く"));
        }

        public void ContinueGame()
        {
            if (transitioning) return;
            SaveData data = SaveSystem.Load();
            if (data == null)
            {
                StartNewGame();
                return;
            }
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
            GameCleared = data.gameCleared;
            pendingFlask = data.version >= 3 && data.flaskState != null && data.flaskState.version > 0 ? data.flaskState : null;
            StartCoroutine(BeginPlay($"おかえりなさい（{data.savedAt} のつづき）"));
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
            AudioManager.SetPourLoop(false);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = 1f;
        }

        public void QuitGame()
        {
            Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>今の状態をセーブする（プレイ中だけ）</summary>
        public void Save()
        {
            if (Mode != GameMode.Playing) return;
            var data = new SaveData { gameCleared = GameCleared };
            data.potions = inventory != null ? inventory.Export() : new List<PotionStack>();
            // 手に持っている瓶は、トレイに戻したことにして保存する
            PotionStack held = brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null;
            if (held != null && held.ml >= PotionStack.EmptyMl) data.potions.Add(held.CloneWith(held.ml));
            if (ingredients != null) data.unlockedIngredients = ingredients.ExportUnlocked();
            if (session != null) data.flaskState = session.ExportFlask();
            if (alchemy != null) data.liquids = alchemy.Liquids.Export();
            data.notes = DiscoveryJournal.ExportNotes();
            data.knownReactions = DiscoveryJournal.ExportReactions();
            SaveSystem.Save(data);
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        /// <summary>タイトルの紙がすっと消えて、そのまま同じ机で遊びはじめる（暗転しない）</summary>
        private IEnumerator BeginPlay(string welcome)
        {
            transitioning = true;
            AtelierHUD hud = AtelierHUD.Instance;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.startGame, 0.6f);
            if (brewInput != null) brewInput.ReturnHeld();
            if (session != null)
            {
                if (pendingFlask != null) session.ImportFlask(pendingFlask);
                else session.ResetFlask();
            }
            pendingFlask = null;
            if (view != null) view.Place();
            Mode = GameMode.Playing;
            if (ModeChanged != null) ModeChanged(Mode);
            if (lib != null)
            {
                AudioManager.PlayBgm(lib.bgmAtelier, 2f);
                AudioManager.PlayAmbient(lib.ambientRoom);
            }
            if (hud != null)
            {
                hud.ShowGameplay();
                yield return new WaitForSecondsRealtime(0.4f);
                hud.ShowToast(welcome);
            }
            transitioning = false;
            checkArrivals = true;
        }

        private void EnterTitleImmediate()
        {
            Resume();
            if (brewInput != null) brewInput.ReturnHeld();
            AudioManager.SetPourLoop(false);
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
