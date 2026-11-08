using System;
using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    public enum GameMode { Title, Playing }

    /// <summary>
    /// ゲーム全体の流れ（タイトル ⇄ プレイ、ポーズ、セーブ／ロード）。
    /// タイトル画面ではアトリエをゆっくり映すカメラに切り替え、プレイヤー操作を止める。
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        [SerializeField] private Camera titleCamera;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private PlayerProgress progress;
        [SerializeField] private QuestManager quests;
        [SerializeField] private BrewingStation station;
        [SerializeField] private Transform playerStart;
        [SerializeField, Tooltip("素材の在庫（空なら起動時にシーンから探す）")] private IngredientInventory ingredients;

        private bool transitioning;
        private AlchemyManager alchemy;
        private List<IngredientStockEntry> pendingLegacyFlask;
        private FlaskSaveData pendingFlask;

        public GameMode Mode { get; private set; } = GameMode.Title;
        public bool IsPaused { get; private set; }
        public PlayerProgress Progress => progress;

        public event Action<GameMode> ModeChanged;

        public void Configure(Camera titleCamera, Camera playerCamera, FirstPersonController player, PlayerInteractor interactor,
                              PotionInventory inventory, PlayerProgress progress, QuestManager quests, BrewingStation station, Transform playerStart)
        {
            this.titleCamera = titleCamera;
            this.playerCamera = playerCamera;
            this.player = player;
            this.interactor = interactor;
            this.inventory = inventory;
            this.progress = progress;
            this.quests = quests;
            this.station = station;
            this.playerStart = playerStart;
        }

        private void Awake()
        {
            Instance = this;
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            alchemy = FindFirstObjectByType<AlchemyManager>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            yield return null; // HUD・AudioManager の準備を待つ
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
            progress.ResetAll(balance != null ? balance.startingGold : 0);
            inventory.Clear();
            if (ingredients != null) ingredients.ResetToStarter(balance);
            if (alchemy != null) alchemy.Liquids.Clear();
            pendingFlask = null;
            pendingLegacyFlask = null;
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
            progress.ReadFrom(data);
            inventory.Import(data.potions);
            if (ingredients != null)
            {
                // 版1（素材の在庫がなかった頃）のセーブには、最初の素材を渡す
                // 版2・3は在庫の量だけ（使い放題の素材は、在庫があれば取引済みにする）
                if (data.version < 2) ingredients.ResetToStarter(GameContext.Balance);
                else ingredients.Import(data.ingredients, data.version >= 4 ? data.unlockedIngredients : null);
            }
            if (alchemy != null)
            {
                alchemy.Liquids.Import(data.version >= 3 ? data.liquids : null);
                // フェーズ4までに作ったポーションにも液体の記録を作る（注ぎ戻し・相談の判定に使う）
                inventory.EnsureRecords(alchemy.Liquids);
            }
            pendingFlask = data.version >= 3 && data.flaskState != null && data.flaskState.version > 0 ? data.flaskState : null;
            pendingLegacyFlask = data.version < 3 ? data.flask : null;
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

        public void Save()
        {
            if (Mode != GameMode.Playing || progress == null) return;
            var data = new SaveData();
            progress.WriteTo(data);
            data.potions = inventory.Export();
            if (interactor != null && interactor.HeldPotion != null) data.potions.Add(interactor.HeldPotion.CloneWith(interactor.HeldPotion.ml));
            if (ingredients != null)
            {
                data.ingredients = ingredients.Export();
                data.unlockedIngredients = ingredients.ExportUnlocked();
            }
            if (station != null) data.flaskState = station.ExportFlask();
            if (alchemy != null) data.liquids = alchemy.Liquids.Export();
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

            SetCameras(false);
            if (playerStart != null)
            {
                player.Teleport(playerStart.position);
                player.LookAt(playerStart.position + playerStart.forward * 3f + Vector3.up * 1.2f);
            }
            interactor.ClearHeld();
            if (pendingFlask != null) station.ImportFlask(pendingFlask);
            else if (pendingLegacyFlask != null && pendingLegacyFlask.Count > 0) station.ImportLegacy(pendingLegacyFlask);
            else station.ResetFlask();
            pendingFlask = null;
            pendingLegacyFlask = null;
            player.InputEnabled = true;
            interactor.enabled = true;
            Mode = GameMode.Playing;
            if (ModeChanged != null) ModeChanged(Mode);
            if (lib != null)
            {
                AudioManager.PlayBgm(lib.bgmAtelier, 2f);
                AudioManager.PlayAmbient(lib.ambientRoom);
            }
            quests.StartFlow();

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
            quests.StopFlow();
            interactor.ClearHeld();
            interactor.enabled = false;
            player.InputEnabled = false;
            SetCameras(true);
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

        private void SetCameras(bool title)
        {
            if (titleCamera != null)
            {
                titleCamera.enabled = title;
                var listener = titleCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = title;
                var rig = titleCamera.GetComponent<TitleCameraRig>();
                if (rig != null) rig.enabled = title;
            }
            if (playerCamera != null)
            {
                playerCamera.enabled = !title;
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = !title;
            }
        }
    }
}
