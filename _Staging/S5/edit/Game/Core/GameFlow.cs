using System;
using System.Collections;
using PotionFrontier.Alchemy;
using PotionFrontier.Game2D;
using UnityEngine;

namespace PotionFrontier.Game
{
    public enum GameMode { Title, Playing }

    /// <summary>
    /// ゲーム全体の流れ（2D 版）：タイトル ⇄ プレイ、ポーズ、セーブ／ロード。シーンの GameSystems に付ける。
    /// セーブは自動（瓶に詰めたとき・相談が解決したとき・タイトルに戻るとき・終了するとき）。
    /// 中身：所持金・解決した相談・素材の在庫と取引中の素材・瓶の棚・作った液体（レシピ本）・フラスコの中身。
    /// （3D 版 GameFlow の流れをそのまま移し、カメラ・プレイヤーの部分を 2D のアトリエ（AtelierView2D）に替えた）
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private PlayerProgress progress;
        [SerializeField] private QuestManager quests;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private BrewSession2D session;
        [SerializeField] private BrewInput2D brewInput;
        [SerializeField] private AtelierView2D view;

        private bool transitioning;
        private FlaskSaveData pendingFlask;

        public GameMode Mode { get; private set; } = GameMode.Title;
        public bool IsPaused { get; private set; }
        public PlayerProgress Progress => progress;

        public event Action<GameMode> ModeChanged;

        private void Awake()
        {
            Instance = this;
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            if (quests == null) quests = FindFirstObjectByType<QuestManager>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (session == null) session = FindFirstObjectByType<BrewSession2D>();
            if (brewInput == null) brewInput = FindFirstObjectByType<BrewInput2D>();
            if (view == null) view = FindFirstObjectByType<AtelierView2D>();
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

        // ------------------------------------------------------------------
        // タイトル ⇄ プレイ
        // ------------------------------------------------------------------

        public void StartNewGame()
        {
            if (transitioning) return;
            SaveSystem.Delete();
            GameBalance balance = GameContext.Balance;
            if (progress != null) progress.ResetAll(balance != null ? balance.startingGold : 0);
            if (inventory != null) inventory.Clear();
            if (ingredients != null) ingredients.ResetToStarter(balance);
            if (alchemy != null) alchemy.Liquids.Clear();
            DiscoveryJournal.Clear();
            pendingFlask = null;
            StartCoroutine(BeginPlay("アトリエへようこそ！ 自由に調合を探求して、町の人の相談に乗ろう"));
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
            if (progress != null) progress.ReadFrom(data);
            if (inventory != null) inventory.Import(data.potions);
            if (ingredients != null)
            {
                if (data.version < 2) ingredients.ResetToStarter(GameContext.Balance);
                else ingredients.Import(data.ingredients, data.version >= 4 ? data.unlockedIngredients : null);
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
            pendingFlask = data.version >= 3 && data.flaskState != null && data.flaskState.version > 0 ? data.flaskState : null;
            StartCoroutine(BeginPlay($"おかえりなさい！（{data.savedAt} のつづき）"));
        }

        public void ReturnToTitle()
        {
            if (transitioning) return;
            Save();
            StartCoroutine(BackToTitle());
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
            if (Mode != GameMode.Playing || progress == null) return;
            var data = new SaveData();
            progress.WriteTo(data);
            data.potions = inventory != null ? inventory.Export() : new System.Collections.Generic.List<PotionStack>();
            // 手に持っている瓶は、棚に戻したことにして保存する
            PotionStack held = brewInput != null && brewInput.Held != null ? brewInput.Held.Potion : null;
            if (held != null && held.ml >= PotionStack.EmptyMl) data.potions.Add(held.CloneWith(held.ml));
            if (ingredients != null)
            {
                data.ingredients = ingredients.Export();
                data.unlockedIngredients = ingredients.ExportUnlocked();
            }
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

        private IEnumerator BeginPlay(string welcome)
        {
            transitioning = true;
            AtelierHUD hud = AtelierHUD.Instance;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.startGame);
            if (hud != null) yield return hud.Fader.FadeOut(0.8f);

            if (brewInput != null) brewInput.ReturnHeld();
            if (session != null)
            {
                if (pendingFlask != null) session.ImportFlask(pendingFlask);
                else session.ResetFlask();
            }
            pendingFlask = null;
            if (view != null) view.EnterRoomImmediate();
            Mode = GameMode.Playing;
            if (ModeChanged != null) ModeChanged(Mode);
            if (lib != null)
            {
                AudioManager.PlayBgm(lib.bgmAtelier, 2f);
                AudioManager.PlayAmbient(lib.ambientRoom);
            }
            if (quests != null) quests.StartFlow();

            if (hud != null)
            {
                hud.ShowGameplay();
                yield return hud.Fader.FadeIn(0.9f);
                hud.ShowToast(welcome);
            }
            transitioning = false;
        }

        private IEnumerator BackToTitle()
        {
            transitioning = true;
            AtelierHUD hud = AtelierHUD.Instance;
            Resume();
            if (hud != null) yield return hud.Fader.FadeOut(0.7f);
            EnterTitleImmediate();
            if (hud != null) yield return hud.Fader.FadeIn(0.9f);
            transitioning = false;
        }

        private void EnterTitleImmediate()
        {
            Resume();
            if (quests != null) quests.StopFlow();
            if (brewInput != null) brewInput.ReturnHeld();
            AudioManager.SetPourLoop(false);
            if (view != null) view.EnterTitle();
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
