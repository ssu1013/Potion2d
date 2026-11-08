#if UNITY_EDITOR
using System.Collections;
using System.IO;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 開発用の通しテスト。プロジェクト直下に「AutoTest.flag」というファイルがあるときだけ、再生開始時に
    /// タイトル → はじめから → お客さん来店 → 相談を聞く（力になりたい）→ 調合 → レシピ本（記録・名前を付ける）
    /// → 棚から取り出して分け与える → お礼 → …（火加減・焦げ・注ぎ戻し・在庫）… → 相談の続き
    /// （レシピ本から分け与える・合わない瓶は返ってくる・作り方を教える・セーブに残る）
    /// → 在庫の確認（在庫0は持てない・仕入れで買う・素材箱から持つ・使い切る・セーブ）→ ポーズ → クリア画面 → タイトル
    /// を自動で進め、各場面を AutoTestShots/ に保存して再生を止める。
    /// セーブは autotest.json に書くので、ふだんのセーブデータは消えない。（エディタ専用。製品版には含まれない）
    /// </summary>
    public class AtelierAutoTest : MonoBehaviour
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(Path.Combine(ProjectRoot, "AutoTest.flag"))) return;
            if (FindFirstObjectByType<BrewingStation>() == null) return;
            SaveSystem.Slot = "autotest";
            SaveSystem.Delete();
            new GameObject("AtelierAutoTest").AddComponent<AtelierAutoTest>();
        }

        private int shot;
        private string folder;
        private int errors;

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                Log("!! " + type + ": " + message);
            }
        }

        private IEnumerator Start()
        {
            folder = Path.Combine(ProjectRoot, "AutoTestShots");
            Directory.CreateDirectory(folder);
            foreach (string f in Directory.GetFiles(folder, "*.png")) File.Delete(f);
            File.Delete(Path.Combine(folder, "log.txt"));

            var controller = FindFirstObjectByType<FirstPersonController>();
            var player = FindFirstObjectByType<PlayerInteractor>();
            var station = FindFirstObjectByType<BrewingStation>();
            var quests = FindFirstObjectByType<QuestManager>();
            var progress = FindFirstObjectByType<PlayerProgress>();
            var stock = FindFirstObjectByType<IngredientInventory>();
            var potionInventory = FindFirstObjectByType<PotionInventory>();
            AtelierHUD hud = AtelierHUD.Instance;

            // ---- タイトル ----------------------------------------------------------
            yield return Wait(3.0f);
            yield return Shot("title");
            hud.OpenSettings();
            yield return Wait(0.6f);
            yield return Shot("title_settings");
            hud.CloseSettings();
            yield return Wait(0.3f);

            hud.Title.ForceNewGame();
            yield return Wait(2.6f);
            controller.InputEnabled = false;
            Log("モード: " + GameFlow.Instance.Mode);
            yield return Shot("gameplay_start");
            // 棚の素材は棚の Start で並ぶので、ゲームが始まってから集める
            IngredientSource[] sources = FindObjectsByType<IngredientSource>(FindObjectsSortMode.None);
            IngredientDatabase db = GameContext.Ingredients;
            Log($"素材カタログ: {(db != null ? db.Count : 0)} 種類 ／ 棚に並んだ素材: {sources.Length} 個 ／ 持っている素材: {stock.CountKinds(db)} 種類 ／ 所持金 {progress.Gold} G");

            // ---- お客さんの来店 ----------------------------------------------------
            controller.Teleport(new Vector3(0.2f, 0.02f, -1.2f));
            controller.LookAt(new Vector3(1.85f, 1.2f, -3.1f));
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Arriving && quests.CurrentCustomer != null &&
                                         quests.CurrentCustomer.transform.position.z > -3.6f, 20f);
            yield return Shot("customer_door");
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Offering, 20f);
            Customer customer = quests.CurrentCustomer;
            Log("来店: " + (customer != null ? customer.Quest.ClientFullName : "なし"));
            if (customer == null) yield break;

            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Wait(0.6f);
            Log("見ている対象: " + (player.Target != null ? player.Target.name : "なし"));
            yield return Shot("customer_counter");

            // ---- 作業台の上のレシピ本（調べると開く）-----------------------------------
            RecipeBookStand bookStand = FindFirstObjectByType<RecipeBookStand>();
            if (bookStand != null)
            {
                controller.Teleport(new Vector3(0.4f, 0.02f, 0.75f));
                controller.LookAt(Center(bookStand));
                yield return Wait(0.5f);
                Log($"[レシピ本] 作業台の本を見る：{(player.Target != null ? player.Target.GetDisplayName(player) : "なし")}（{(player.Target != null ? player.Target.GetSubtitle(player) : "")}）／ 案内 {(player.Options.Count > 0 ? player.Options[0].label : "なし")}");
                if (!(player.Target is RecipeBookStand)) Debug.LogError("[自動テスト] 作業台の上のレシピ本を調べられない");
                yield return Shot("book_stand");
            }
            else Debug.LogError("[自動テスト] 作業台の上にレシピ本（RecipeBookStand）がない（シーンを作り直してください）");
            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Wait(0.4f);

            // ---- 相談を聞く（力になりたい → 探求してくる）--------------------------------
            CheckWords("会話の前");
            quests.TalkTo(customer, player);
            yield return Wait(0.5f);
            yield return Shot("dialogue_typing");
            yield return ReadDialogueChoosing(hud, "力になりたい", "探求してくる");
            yield return Wait(0.5f);
            Log("相談の状態: " + quests.Phase + " ／ レシピ本が開いていない: " + !hud.IsRecipeBookOpen);
            if (quests.Phase != CustomerPhase.Waiting) Debug.LogError("[自動テスト] 「力になりたい」と答えても、相談に乗っている状態にならない");
            yield return Shot("quest_accepted");
            CheckWords("相談メモ");

            // ---- 重さの計測（作業台を見る／カウンターのお客さんを見る）--------------------
            controller.Teleport(new Vector3(0f, 0.02f, 0.2f));
            controller.LookAt(Center(station) + Vector3.up * 0.2f);
            yield return Measure("作業台");
            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Measure("カウンター");
            controller.Teleport(new Vector3(0.4f, 0.02f, 0.9f));
            controller.LookAt(new Vector3(1.85f, 1.2f, -3.1f));
            yield return Measure("入口と外");

            // 画質ごとの比べ（作業台を見た状態）。終わったら元の設定に戻す
            int originalQuality = GameSettings.GraphicsQuality;
            controller.Teleport(new Vector3(0f, 0.02f, 0.2f));
            controller.LookAt(Center(station) + Vector3.up * 0.2f);
            for (int q = GameSettings.QualityLow; q <= GameSettings.QualityHigh; q++)
            {
                GameSettings.GraphicsQuality = q;
                yield return Measure("作業台・画質" + GameSettings.QualityNames[q]);
                if (q == GameSettings.QualityLow) yield return Shot("quality_low");
                if (q == GameSettings.QualityHigh) yield return Shot("quality_high");
            }
            // 調合台の道具の重さ（標準の画質で、トレイ・加熱台をそれぞれ隠したとき）
            GameSettings.GraphicsQuality = GameSettings.QualityStandard;
            BrewTray trayObject = FindFirstObjectByType<BrewTray>();
            BrewHeater heaterObject = FindFirstObjectByType<BrewHeater>();
            if (trayObject != null)
            {
                trayObject.gameObject.SetActive(false);
                yield return Measure("作業台・画質標準・トレイを隠す");
                trayObject.gameObject.SetActive(true);
            }
            if (heaterObject != null)
            {
                heaterObject.gameObject.SetActive(false);
                yield return Measure("作業台・画質標準・加熱台を隠す");
                heaterObject.gameObject.SetActive(true);
            }
            // 調合台の見た目（ぼかし）を確かめるため、ここからは「標準」で進める（最後に元に戻す）

            // ---- 調合台に向かう（カメラが寄る・カーソルで作業する）-----------------------
            brew = BrewMode.Instance;
            if (brew == null)
            {
                Debug.LogError("[自動テスト] BrewMode がシーンにありません（シーンを作り直してください）");
                yield break;
            }
            IngredientData waterData = db.Get("base_water");
            IngredientData herbData = db.Get("main_herb");
            float waterBefore = stock.Get(waterData);
            float herbBefore = stock.Get(herbData);
            Vector3 walkPosition = new Vector3(0f, 0.02f, 0.75f);
            controller.Teleport(walkPosition);
            controller.LookAt(Center(station));
            yield return Wait(0.5f);
            Log($"[調合台] フラスコの案内: {(player.Options.Count > 0 ? player.Options[0].label : "なし")}");
            yield return Shot("brew_prompt");
            Vector3 cameraBefore = brew.ViewCamera.transform.position;
            pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            brew.SetVirtualPointer(pointer, false);
            station.OnPress(player, KeyCode.E); // E キーと同じ
            yield return Wait(0.25f);
            yield return Shot("brew_entering");
            yield return WaitUntil(() => brew.IsReady, 3f);
            yield return Wait(0.3f);
            Log($"[調合台] 寄り終わった: {brew.IsReady} ／ カメラ {cameraBefore} → {brew.ViewCamera.transform.position}（FOV {brew.ViewCamera.fieldOfView:0}）／ カーソル固定 {Cursor.lockState} ／ ぼかし {(brew.ViewCamera.GetComponent<AtelierPostFX>() != null && brew.ViewCamera.GetComponent<AtelierPostFX>().DofActive)} ／ トレイ {brew.Tray.TotalCount} 個（{brew.Tray.PageCount} ページ）");
            if (!brew.IsReady) Debug.LogError("[自動テスト] 調合台に寄り終わらない");
            if (Cursor.lockState == CursorLockMode.Locked) Debug.LogError("[自動テスト] 調合台でカーソルが固定されたまま");
            yield return Shot("brew_view");
            yield return Measure("調合台");
            GameSettings.GraphicsQuality = GameSettings.QualityHigh;
            yield return Measure("調合台・画質" + GameSettings.QualityNames[GameSettings.QualityHigh]);
            GameSettings.GraphicsQuality = GameSettings.QualityLow;
            yield return Wait(0.4f);
            yield return Shot("brew_quality_low");
            GameSettings.GraphicsQuality = GameSettings.QualityStandard;
            yield return Wait(0.4f);

            // ---- 調合（水＋薬草×2）：トレイから取る → 傾けて注ぐ → 入れる → 円を描いて混ぜる ----
            yield return Hover(TrayItemScreen(waterData), 0.4f);
            Log($"[トレイ] 指している: {brew.Hovered} {brew.HoverTitle} ／ {brew.HoverSubtitle}");
            yield return Shot("brew_hover_tray");
            yield return PickFromTray(waterData);
            Log($"[トレイ] 水を手に取った: {(player.HeldIngredient == waterData)}");
            yield return Hover(TrayItemScreen(herbData) + new Vector2(0f, Screen.height * 0.12f), 0.3f);
            yield return Shot("brew_held_water");
            float poured = 0f;
            yield return PourUntil(100f, 3f, ml => poured = ml, "pouring");
            Log($"[注ぐ] 傾けて注いだ: {poured:0}ml（最後の傾き {brew.Tilt:0.00}）／ フラスコ {station.Flask.VolumeMl:0}ml");
            Log($"[在庫] 水（使い放題: {stock.IsUnlimited(waterData)}）: {IngredientInventory.Format(waterData, waterBefore)} → {IngredientInventory.Format(waterData, stock.Get(waterData))}");
            if (poured < 60f) Debug.LogError("[自動テスト] 傾けて注いでも、ほとんど入らない");
            yield return RightClick();
            Log($"[戻す] 右クリックで手の物をトレイに戻した: {(player.HeldIngredient == null && player.HeldPotion == null)}");

            yield return PickFromTray(herbData);
            yield return Hover(FlaskScreen(0.75f), 0.35f);
            Log($"[入れる] フラスコの上: {brew.OverFlask}");
            yield return Shot("held_herb");
            yield return Click(FlaskScreen(0.75f));
            yield return Wait(0.2f);
            yield return Shot("drop_piece");
            yield return Wait(0.42f);
            yield return Shot("drop_ripple");
            Log($"[演出] 落ちた音: {station.Effects.LastDropSound}（{station.Effects.DropSoundCount} 回）");
            if (station.Effects.DropSoundCount < 1) Debug.LogError("[自動テスト] 固体を落としても音が鳴らない");
            yield return Wait(0.2f);
            yield return Click(FlaskScreen(0.75f));
            yield return Wait(0.8f);
            yield return RightClick();
            Log($"[在庫] 薬草（使い放題: {stock.IsUnlimited(herbData)}）: {IngredientInventory.Format(herbData, herbBefore)} → {IngredientInventory.Format(herbData, stock.Get(herbData))}（入れた量 {Amount(station, herbData):0.##}）");
            if (Amount(station, herbData) < 1.5f) Debug.LogError("[自動テスト] クリックで薬草が2回分入っていない");
            Log($"[工程] 混ぜる前：溶け具合 {station.Flask.Snapshot().dissolvedFraction:P0} ／ {Describe(station.Preview)}");
            int tinkBefore = station.Effects.TinkCount;
            yield return StirCircle(4f, true, 1.6f, "brew_stirring");
            Log($"[演出] 混ぜ終わり：渦の強さ {station.Effects.SwirlStrength:0.00} ／ ガラス棒が当たった音 {station.Effects.TinkCount - tinkBefore} 回");
            if (station.Effects.TinkCount - tinkBefore < 4) Debug.LogError("[自動テスト] かき混ぜてもガラス棒の音がほとんど鳴らない");
            yield return Wait(0.5f);
            yield return Shot("swirl_after");
            Log($"[演出] 手を止めて 0.5 秒：渦の強さ {station.Effects.SwirlStrength:0.00}（だんだん弱まる）");
            yield return Wait(1.0f);
            Log($"[工程] マウスで時計回りに {station.Flask.TotalTurns:0.#}回混ぜた：溶け具合 {station.Flask.Snapshot().dissolvedFraction:P0} ／ {Describe(station.Preview)}");
            if (station.Flask.TotalTurns < 4f) Debug.LogError("[自動テスト] マウスで円を描いても、ほとんど混ざらない");
            yield return Hover(new Vector2(Screen.width * 0.5f, Screen.height * 0.92f), 0.3f);
            yield return Shot("filled");
            LiquidRecord bottled = station.BottleOne(); // 「瓶に詰める（F）」ボタンと同じ
            Log($"[瓶詰め] {Describe(bottled)}（残り {station.Flask.VolumeMl:0}ml）");
            yield return Wait(0.7f);
            yield return Shot("result_quest_match");
            hud.HideResult();
            LiquidRecord rest = station.BottleOne();
            Log($"[瓶詰め] 残りも詰めた：{Describe(rest)} ／ 同じ液体: {(rest != null && bottled != null && rest.id == bottled.id)} ／ 作った液体の数 {station.Alchemy.Liquids.Count}");
            yield return Wait(0.3f);
            hud.HideResult();

            // ---- レシピ本（調合台で R）：ページ・名前を付ける ---------------------------
            yield return RecipeBookTests(hud, station, potionInventory, bottled);
            yield return ExitBrew();
            Log($"[調合台] 離れた: {!brew.IsActive} ／ カメラが元に戻った: {(Vector3.Distance(brew.ViewCamera.transform.position, cameraBefore) < 0.05f)} ／ カーソル固定 {Cursor.lockState}");

            PotionStack shelfStack = bottled != null ? potionInventory.Find(bottled.id) : null;
            Log($"[棚] {(shelfStack != null ? shelfStack.potionName + " " + shelfStack.DescribeAmount() : "なし")} ／ 新着: {(shelfStack != null && shelfStack.isNew)}");

            // ---- 棚（Tab）から取り出す --------------------------------------------
            hud.ToggleShelf();
            yield return Wait(0.6f);
            IngredientBrowserUI shelf = hud.IngredientBrowser;
            Log($"[ポーション棚] 開いた: {hud.IsShelfOpen} ／ 自作の液体 {shelf.VisibleBottleCount} 種類");
            if (bottled != null) shelf.SelectById(bottled.id);
            yield return Wait(0.3f);
            yield return Shot("shelf_ui");
            shelf.PrimaryAction();
            yield return Wait(0.6f);
            Log($"手に持っている: {(player.HeldPotion != null ? $"{player.HeldPotion.potionName} {player.HeldPotion.ml:0}ml（満タン: {player.HeldPotion.IsFullBottle}）" : "なし")} ／ 棚に残り {(shelfStack != null ? shelfStack.ml : 0f):0}ml");

            // ---- 手渡す ----------------------------------------------------------
            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Wait(0.6f);
            yield return Shot("deliver_prompt");
            int goldBefore = progress.Gold;
            quests.TalkTo(customer, player);
            yield return Wait(1.2f);
            yield return Shot("dialogue_success");
            yield return ReadDialogue(hud, false);
            yield return Wait(0.6f);
            Log($"所持金: {goldBefore} → {progress.Gold}（解決 {progress.CompletedQuests} 件・分け与えた {progress.SharedCount}・教えた {progress.TaughtCount}）");
            if (bottled != null) Log($"[レシピ本] 力になった相談の記録: 分け与えた {string.Join("・", bottled.sharedWith)}");
            yield return Shot("reward");
            // 次のお客さんは、作り方を教えてもよい自動テスト用の相談者にする（来店は後半の「相談の続き」で使う）
            QuestData teachQuest = MakeTestQuest("q_autotest_teach", "テストA", true);
            QuestData shareQuest = MakeTestQuest("q_autotest_share", "テストB", false);
            quests.NextQuestOverride = teachQuest;

            // ---- 火加減（つまみをドラッグ）・早送り・沸騰・反時計回り ----------------------
            controller.Teleport(walkPosition);
            controller.LookAt(Center(station));
            yield return Wait(0.3f);
            yield return EnterBrew(player, station);
            yield return PickFromTray(waterData);
            yield return PourUntil(130f, 3f, null, null);
            yield return RightClick();
            yield return Hover(KnobScreen(), 0.35f);
            Log($"[つまみ] 指している: {brew.Hovered} ／ {brew.HoverTitle}");
            yield return DragKnob(1f);
            Log($"[火加減] つまみを右へドラッグ：{ProcessStep.HeatLabel(station.Flask.HeatLevel)}（{station.Flask.HeatLevel:0.00}）／ 加熱台の表示 {station.Heater.Level:0.00}");
            if (station.Flask.HeatLevel < 0.95f) Debug.LogError("[自動テスト] つまみをドラッグしても強火にならない");
            yield return Wait(0.6f);
            yield return Shot("brew_knob");
            float beforeMl = station.Flask.VolumeMl;
            float simBefore = station.Flask.Time;
            brew.SetFastForward(true); // 「早送り（Space）」ボタンと同じ
            float realStart = Time.unscaledTime;
            yield return WaitUntil(() => station.Flask.Temperature >= 75f, 4f);
            yield return Shot("simmer");
            Log($"[演出] {station.Flask.Temperature:0}℃：音 {station.Effects.DescribeAudio()} ／ 液面の泡 {station.Effects.BoilVisual:0.00}");
            yield return Wait(Mathf.Max(0.2f, 4.2f - (Time.unscaledTime - realStart)));
            Log($"[早送り] 実時間 4.2 秒でフラスコの時間が {station.Flask.Time - simBefore:0.0} 秒進んだ（×{brew.FastForwardSpeed:0}）");
            Log($"[火加減] {station.Flask.Temperature:0}℃ ／ 沸騰 {station.Flask.IsBoiling} ／ 液量 {beforeMl:0} → {station.Flask.VolumeMl:0}ml（沸騰すると減る）");
            if (!station.Flask.IsBoiling) Debug.LogError("[自動テスト] 強火で（早送りで）16秒ほどたっても沸騰しない");
            yield return Shot("boiling");
            Log($"[演出] 沸騰中：音 {station.Effects.DescribeAudio()} ／ 液面の泡 {station.Effects.BoilVisual:0.00}");
            if (!station.Effects.DescribeAudio().Contains("ぐつぐつ")) Debug.LogError("[自動テスト] 沸騰しているのに、ぐつぐつ音が鳴っていない");
            yield return Measure("調合台・沸騰中");
            brew.SetFastForward(false);
            yield return DragKnob(0f);
            Log($"[火加減] つまみを左へ戻した：{ProcessStep.HeatLabel(station.Flask.HeatLevel)}");
            yield return StirCircle(2f, false, 1.6f, null);
            Log($"[工程] マウスで反時計回りに混ぜた（合計 {station.Flask.TotalTurns:0.#}回）");
            LiquidRecord hotWater = station.BottleOne();
            Log($"[瓶詰め] お湯：{Describe(hotWater)}");
            yield return Wait(0.6f);
            yield return Shot("result_unknown");
            hud.HideResult();
            station.BottleOne();
            hud.HideResult();
            string steps = bottled != null && bottled.recipes.Count > 0 ? string.Join(" → ", bottled.recipes[0].steps.ConvertAll(x => x.Describe())) : "なし";
            Log($"[工程の記録] {steps}");

            // ---- 少しの量を強火で煮詰めて焦がす（焦げる音・煙・液面の焦げつき）----------
            station.ResetFlask();
            yield return PickFromTray(waterData);
            yield return PourUntil(40f, 2f, null, null);
            yield return RightClick();
            yield return DragKnob(1f);
            station.SimSpeed = 8f;
            yield return WaitUntil(() => station.Flask.IsScorching, 10f);
            yield return Wait(1.2f);
            Log($"[演出] 煮詰めた：{station.Flask.VolumeMl:0}ml ／ 焦げ {station.Flask.Scorch:0.00} ／ 焦げている {station.Flask.IsScorching} ／ 音 {station.Effects.DescribeAudio()}");
            if (!station.Flask.IsScorching) Debug.LogError("[自動テスト] 少しの量を強火で煮詰めても焦げない");
            else if (!station.Effects.DescribeAudio().Contains("焦げる")) Debug.LogError("[自動テスト] 焦げているのに、焦げる音が鳴っていない");
            yield return Shot("scorch");
            station.SimSpeed = 1f;
            yield return DragKnob(0f);
            station.ResetFlask();
            yield return Wait(0.3f);

            // ---- 瓶詰めした液体をトレイ（自作）から取って、フラスコに注ぎ戻す（多段調合）----
            yield return PourBackTests(hud, player, station, potionInventory, bottled, waterData);
            yield return ExitBrew();

            // ---- 在庫・仕入れ・素材箱 ----------------------------------------------
            yield return StockTests(hud, player, station, stock, progress, db);

            // ---- ポーズ・クリア画面 ------------------------------------------------
            hud.OpenPause();
            yield return Wait(0.6f);
            yield return Shot("pause");
            hud.ClosePause();
            yield return Wait(0.3f);

            hud.ShowClear();
            yield return Wait(1.8f);
            yield return Shot("clear_screen");
            hud.CloseClearScreen();
            yield return Wait(0.3f);

            // ---- 相談の続き：レシピ本から分け与える（合わない瓶は返ってくる）→ 作り方を教える → 分け与える ----
            yield return ConsultationTests(hud, controller, player, quests, progress, potionInventory, station, bottled, teachQuest, shareQuest);

            GameSettings.GraphicsQuality = originalQuality;
            GameFlow.Instance.ReturnToTitle();
            yield return Wait(3.0f);
            yield return Shot("title_continue");

            Log($"自動テスト完了（エラー {errors} 件）");
            yield return Wait(0.5f);
            UnityEditor.EditorApplication.isPlaying = false;
        }

        /// <summary>
        /// 調合台で素材箱（I）を開くと、自作の液体も並んでいる → トレイの「自作」に並んだ瓶を取る → 傾けて注ぎ戻す（瓶が空になる）
        /// → トレイの水で薄める → 全部瓶に詰める（何本でも同じ液体・元の液体が記録される）
        /// </summary>
        private IEnumerator PourBackTests(AtelierHUD hud, PlayerInteractor player, BrewingStation station, PotionInventory potions,
                                          LiquidRecord healing, IngredientData waterData)
        {
            PotionStack stack = healing != null ? potions.Find(healing.id) : null;
            if (stack == null)
            {
                Debug.LogError("[AutoTest] 回復ポーションの瓶が棚にありません");
                yield break;
            }
            station.ResetFlask();
            hud.ToggleIngredientBox(); // 調合台で I キーと同じ
            yield return Wait(0.6f);
            IngredientBrowserUI box = hud.IngredientBrowser;
            Log($"[素材箱・すべて] 調合台で開いた: {hud.IsBrowserOpen} ／ 素材と自作の液体 {box.VisibleCount} 個 ／ うち自作の液体 {box.VisibleBottleCount} 種類");
            box.SelectById(healing.id);
            yield return Wait(0.3f);
            yield return Shot("box_with_liquids");
            hud.CloseIngredientBrowser();
            yield return Wait(0.3f);

            brew.Tray.SetTab(BrewTray.TabOwn);
            yield return Wait(0.3f);
            yield return Hover(TrayBottleScreen(healing.id), 0.35f);
            yield return Shot("brew_tray_own");
            yield return PickBottleFromTray(healing.id);
            float heldMl = player.HeldPotion != null ? player.HeldPotion.ml : 0f;
            Log($"[注ぎ戻し] トレイから取った瓶: {(player.HeldPotion != null ? player.HeldPotion.potionName : "なし")} {heldMl:0}ml");
            float poured = 0f;
            yield return PourUntil(9999f, 4f, ml => poured = ml, "pour_back");
            Log($"[注ぎ戻し] 傾けて {poured:0}ml 注いだ ／ フラスコ {station.Flask.VolumeMl:0}ml（瓶の中身 {heldMl:0}ml）／ 瓶は空になって消えた: {player.HeldPotion == null} ／ 棚に残り: {(potions.Find(healing.id) != null ? potions.Find(healing.id).ml : 0f):0}ml");
            if (player.HeldPotion != null) Debug.LogError("[自動テスト] 瓶を傾け続けても空にならない");

            // 水で薄めて、全部瓶に詰める
            brew.Tray.SetTab(BrewTray.TabLiquids);
            yield return Wait(0.2f);
            yield return PickFromTray(waterData);
            yield return PourUntil(230f, 4f, null, null);
            yield return RightClick();
            yield return StirCircle(2f, true, 1.6f, null);
            brew.Tray.SetTab(BrewTray.TabAll);
            float volume = station.Flask.VolumeMl;
            int bottlesBefore = potions.TotalCount;
            LiquidRecord mixed = station.BottleAll(); // 「全部詰める」ボタンと同じ
            yield return Wait(0.6f);
            yield return Shot("result_bottle_all");
            hud.HideResult();
            PotionStack mixedStack = mixed != null ? potions.Find(mixed.id) : null;
            Log($"[全部詰める] {volume:0}ml → {Describe(mixed)} ／ 棚 {(mixedStack != null ? mixedStack.DescribeAmount() : "なし")} ／ 瓶 {bottlesBefore} → {potions.TotalCount} 本 ／ フラスコ {station.Flask.VolumeMl:0}ml");
            Log($"[多段調合] 元の液体に回復ポーション: {(mixed != null && mixed.parentLiquidIds.Contains(healing.id))} ／ 使った素材: {(mixed != null ? string.Join(",", mixed.ingredientIds) : "")}");
            string mixedSteps = mixed != null && mixed.recipes.Count > 0 ? string.Join(" → ", mixed.recipes[0].steps.ConvertAll(x => x.Describe())) : "なし";
            Log($"[工程の記録] {mixedSteps}");
            yield return Wait(0.3f);
            yield return Shot("brew_after_bottle_all");
        }

        /// <summary>在庫0は持てない → 取引先帳で開拓する・取り寄せる → 素材箱から持つ → 使い切る → セーブに残る</summary>
        private IEnumerator StockTests(AtelierHUD hud, PlayerInteractor player, BrewingStation station, IngredientInventory stock,
                                       PlayerProgress progress, IngredientDatabase db)
        {
            IngredientData crystal = db.Get("cat_amplify_crystal");
            if (crystal == null)
            {
                Debug.LogError("[AutoTest] cat_amplify_crystal がありません");
                yield break;
            }
            stock.Consume(crystal, 999f);
            Log($"[在庫0] 水晶の在庫 {stock.Get(crystal):0.##} → 持てた: {player.HoldIngredient(crystal)}（false なら正しい）");
            yield return Wait(0.3f);

            // 取引先帳
            progress.AddGold(200);
            int goldBefore = progress.Gold;
            hud.OpenShop();
            yield return Wait(0.6f);
            IngredientBrowserUI browser = hud.IngredientBrowser;
            Log($"[取引先帳] 載っている素材: {browser.VisibleCount} 種類");
            yield return Measure("取引先帳");
            float t0 = Time.realtimeSinceStartup;
            browser.SetTab(2);
            browser.SetTab(0);
            Log($"[取引先帳] 種類タブの切り替え2回: {(Time.realtimeSinceStartup - t0) * 1000f:0.0} ms");

            // 開拓（使い放題の素材）
            IngredientData mist = db.Get("main_mist_flower");
            if (mist != null)
            {
                Log($"[開拓] 霧隠れの花：使い放題の素材 {IngredientInventory.IsInfiniteKind(mist)} ／ 取引済み {stock.IsUnlimited(mist)} ／ 持てる {stock.Has(mist)}");
                browser.SelectById(mist.IngredientId);
                yield return Wait(0.2f);
                yield return Shot("shop_unlock");
                int g0 = progress.Gold;
                browser.Buy();
                yield return Wait(0.3f);
                Log($"[開拓] 霧隠れの花を開拓：所持金 {g0} → {progress.Gold}（差 {g0 - progress.Gold}）／ 取引済み {stock.IsUnlimited(mist)} ／ もう一度押しても払わない：{TryBuyAgain(browser, progress)}");
            }

            // 取り寄せ（希少な素材）
            int g1 = progress.Gold;
            browser.SelectById(crystal.IngredientId);
            browser.SetQuantity(3);
            yield return Wait(0.2f);
            yield return Shot("shop_ui");
            browser.Buy();
            yield return Wait(0.3f);
            Log($"[取り寄せ] 水晶×3（{crystal.BuyPrice} G ずつ）: 所持金 {g1} → {progress.Gold}（差 {g1 - progress.Gold}）／ 在庫 {stock.Get(crystal):0.##}");
            browser.SetFilter("薬");
            yield return Wait(0.2f);
            Log($"[取引先帳] 「薬」で絞り込み: {browser.VisibleCount} 種類");
            yield return Shot("shop_search");
            browser.SetFilter("");
            hud.CloseIngredientBrowser();
            yield return Wait(0.3f);

            // 調合台で素材箱から持つ
            yield return EnterBrew(player, station);
            hud.ToggleIngredientBox();
            yield return Wait(0.6f);
            Log($"[素材箱] 使える素材と自作の液体: {browser.VisibleCount} 個（うち自作の液体 {browser.VisibleBottleCount} 種類）");
            browser.SelectById(crystal.IngredientId);
            yield return Wait(0.2f);
            yield return Shot("box_ui");
            browser.PrimaryAction();
            yield return Wait(0.5f);
            yield return Hover(FlaskScreen(0.75f), 0.35f);
            Log($"[素材箱] 手に持っている: {(player.HeldIngredient != null ? player.HeldIngredient.DisplayName : "なし")} ／ 箱は閉じた: {!hud.IsBrowserOpen}");
            yield return Shot("held_crystal");

            // 使い切る（3回入れると在庫0 → 手から消える）。テストなので、フラスコは空から始める
            station.ResetFlask();
            for (int i = 0; i < 4 && player.HeldIngredient != null; i++)
            {
                yield return Click(FlaskScreen(0.75f));
                yield return Wait(0.5f);
            }
            Log($"[使い切る] 在庫 {stock.Get(crystal):0.##} ／ 手に持っている: {(player.HeldIngredient != null ? player.HeldIngredient.DisplayName : "なし")} ／ フラスコ {Amount(station, crystal):0.##}");
            station.ResetFlask();
            yield return ExitBrew();

            // セーブに在庫が入る
            GameFlow.Instance.Save();
            SaveData data = SaveSystem.Load();
            float savedMl = 0f;
            foreach (PotionStack p in data.potions) savedMl += p.ml;
            Log($"[セーブ] 版 {data.version} ／ 取引済み {data.unlockedIngredients.Count} 種類 ／ 希少な素材の在庫 {data.ingredients.Count} 種類 ／ 所持金 {data.gold} G ／ 作った液体 {data.liquids.Count} 種類 ／ 棚の瓶 {data.potions.Count} 種類 {savedMl:0}ml");
        }

        // ------------------------------------------------------------------
        // 調合台の操作（仮のポインタでマウスの代わりをする）
        // ------------------------------------------------------------------

        private BrewMode brew;
        private Vector2 pointer;
        private bool pointerDown;

        private IEnumerator EnterBrew(PlayerInteractor player, BrewingStation station)
        {
            station.OnPress(player, KeyCode.E);
            yield return WaitUntil(() => brew.IsReady, 3f);
            pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            brew.SetVirtualPointer(pointer, false);
            yield return Wait(0.2f);
        }

        private IEnumerator ExitBrew()
        {
            brew.ClearVirtualPointer();
            pointerDown = false;
            brew.Exit();
            yield return WaitUntil(() => !brew.IsActive, 3f);
            yield return Wait(0.2f);
        }

        /// <summary>仮のポインタを動かす（seconds かけて、まっすぐ）</summary>
        private IEnumerator MoveTo(Vector2 to, float seconds)
        {
            Vector2 from = pointer;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                pointer = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                brew.SetVirtualPointer(pointer, pointerDown);
                yield return null;
            }
            pointer = to;
            brew.SetVirtualPointer(pointer, pointerDown);
            yield return null;
        }

        private IEnumerator Hover(Vector2 at, float settle)
        {
            yield return MoveTo(at, 0.2f);
            yield return Wait(settle);
        }

        private IEnumerator Click(Vector2 at)
        {
            yield return MoveTo(at, 0.15f);
            yield return null;
            pointerDown = true;
            brew.SetVirtualPointer(pointer, true);
            yield return null;
            yield return null;
            pointerDown = false;
            brew.SetVirtualPointer(pointer, false);
            yield return null;
        }

        private IEnumerator RightClick()
        {
            brew.SetVirtualPointer(pointer, false, true);
            yield return null;
            yield return null;
            brew.SetVirtualPointer(pointer, false, false);
            yield return null;
        }

        private Vector2 FlaskScreen(float height)
        {
            BrewingStation s = brew.Station;
            return brew.ScreenPoint(Vector3.Lerp(s.BottomWorld, s.MouthWorld, height));
        }

        private Vector2 KnobScreen()
        {
            return brew.ScreenPoint(brew.Station.Heater.Knob.position + brew.ViewCamera.transform.forward * -0.02f);
        }

        private BrewTrayItem TrayItem(System.Func<TrayEntry, bool> match)
        {
            foreach (BrewTrayItem item in brew.Tray.Items)
            {
                if (item != null && match(item.Entry)) return item;
            }
            return null;
        }

        private Vector2 ItemScreen(BrewTrayItem item)
        {
            if (item == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
            return brew.ScreenPoint(PrefabAnchors.RendererBounds(item.transform).center);
        }

        private Vector2 TrayItemScreen(IngredientData data)
        {
            return ItemScreen(TrayItem(e => e.ingredient == data));
        }

        private Vector2 TrayBottleScreen(string liquidId)
        {
            return ItemScreen(TrayItem(e => e.bottle != null && e.bottle.liquidId == liquidId));
        }

        /// <summary>トレイの素材をクリックして手に取る（今のページになければ、ページをめくって探す）</summary>
        private IEnumerator PickFromTray(IngredientData data)
        {
            yield return PickWhere(e => e.ingredient == data, data != null ? data.DisplayName : "?");
        }

        private IEnumerator PickBottleFromTray(string liquidId)
        {
            yield return PickWhere(e => e.bottle != null && e.bottle.liquidId == liquidId, liquidId);
        }

        private IEnumerator PickWhere(System.Func<TrayEntry, bool> match, string label)
        {
            BrewTray tray = brew.Tray;
            BrewTrayItem item = TrayItem(match);
            for (int page = 0; item == null && page < tray.PageCount; page++)
            {
                tray.SetPage(page);
                yield return null;
                item = TrayItem(match);
            }
            if (item == null)
            {
                Debug.LogError("[自動テスト] トレイに見つからない: " + label);
                yield break;
            }
            yield return Click(ItemScreen(item));
            yield return Wait(0.25f);
        }

        /// <summary>手に持った液体を、フラスコの上で左ボタンを押したまま下へ動かして傾けて注ぐ（targetMl 入るか、瓶が空になるまで）</summary>
        private IEnumerator PourUntil(float targetMl, float maxSeconds, System.Action<float> result, string shotName)
        {
            PlayerInteractor player = FindFirstObjectByType<PlayerInteractor>();
            Vector2 start = FlaskScreen(0.95f) + new Vector2(Screen.width * 0.03f, Screen.height * 0.05f);
            yield return Hover(start, 0.3f);
            if (!brew.OverFlask) Debug.LogError("[自動テスト] 液体を持ってフラスコの上に来ても、注ぐ構えにならない");
            pointerDown = true;
            brew.SetVirtualPointer(pointer, true);
            yield return null;
            yield return null;
            // 約 0.4 秒で、深く傾ける（いちばん深くの 8 割ほど）
            float pixels = 240f * Screen.height / 1080f * 0.75f;
            yield return MoveTo(pointer - new Vector2(0f, pixels), 0.4f);
            bool shot = shotName == null;
            float t = 0f;
            while (t < maxSeconds && brew.PouredThisTime < targetMl && (player.HeldIngredient != null || player.HeldPotion != null))
            {
                if (!shot && t > 0.3f)
                {
                    shot = true;
                    yield return Shot(shotName);
                    Log($"[演出] 注いでいる（{brew.PourRate:0} ml/秒）：音 {brew.Station.Effects.DescribeAudio()}");
                }
                t += Time.deltaTime;
                yield return null;
            }
            float poured = brew.PouredThisTime;
            pointerDown = false;
            brew.SetVirtualPointer(pointer, false);
            yield return null;
            yield return Wait(0.3f);
            if (result != null) result(poured);
        }

        /// <summary>手ぶらで、フラスコの上で左ボタンを押したまま円を描いてかき混ぜる</summary>
        private IEnumerator StirCircle(float seconds, bool clockwise, float turnsPerSecond, string shotName)
        {
            BrewingStation s = brew.Station;
            Vector3 center = Vector3.Lerp(s.BottomWorld, s.MouthWorld, 0.35f);
            Vector2 c = brew.ScreenPoint(center);
            float radius = Vector2.Distance(c, brew.ScreenPoint(center + brew.ViewCamera.transform.right * s.BottomRadius * 0.6f));
            // 画面の上では、時計回り＝角度が減る向き
            float sign = clockwise ? -1f : 1f;
            float angle = Mathf.PI * 0.5f;
            yield return Hover(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, 0.3f);
            pointerDown = true;
            brew.SetVirtualPointer(pointer, true);
            yield return null;
            yield return null;
            bool shot = shotName == null;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                angle += sign * turnsPerSecond * Mathf.PI * 2f * Time.deltaTime;
                pointer = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                brew.SetVirtualPointer(pointer, true);
                if (!shot && t > seconds * 0.4f)
                {
                    shot = true;
                    yield return Shot(shotName);
                }
                yield return null;
            }
            float turns = brew.StirTurnsThisTime;
            bool cw = brew.StirClockwise;
            pointerDown = false;
            brew.SetVirtualPointer(pointer, false);
            yield return null;
            Log($"[かき混ぜる] マウスで {(clockwise ? "時計回り" : "反時計回り")}に {seconds:0.#}秒 → {turns:0.0}回（向きの判定: {(cw ? "時計回り" : "反時計回り")}）");
            if (cw != clockwise) Debug.LogError("[自動テスト] 円を描いた向きと、かき混ぜた向きが合わない");
        }

        /// <summary>つまみを左ボタンでつかんで、右へ（上へ）ドラッグして火加減を target にする</summary>
        private IEnumerator DragKnob(float target)
        {
            BrewingStation s = brew.Station;
            yield return Hover(KnobScreen(), 0.2f);
            if (brew.Hovered != BrewMode.HoverKind.Knob) Debug.LogError("[自動テスト] つまみを指せない（" + brew.Hovered + "）");
            pointerDown = true;
            brew.SetVirtualPointer(pointer, true);
            yield return null;
            yield return null;
            float pixels = (target - s.Flask.HeatLevel) * 300f * Screen.height / 1080f * 1.15f;
            yield return MoveTo(pointer + new Vector2(pixels, 0f), 0.5f);
            yield return Wait(0.2f);
            pointerDown = false;
            brew.SetVirtualPointer(pointer, false);
            yield return null;
            yield return Wait(0.2f);
        }

        /// <summary>取引済みの素材でもう一度「開拓する」を押しても、お金が減らないか</summary>
        private static bool TryBuyAgain(IngredientBrowserUI browser, PlayerProgress progress)
        {
            int before = progress.Gold;
            browser.Buy();
            return progress.Gold == before;
        }

        private static float Amount(BrewingStation station, IngredientData data)
        {
            return station.Flask != null && data != null ? station.Flask.UnitsOf(data.IngredientId) : 0f;
        }

        private static string Describe(LiquidRecord r)
        {
            if (r == null) return "なし";
            return $"{r.DisplayName}［{r.kind}］★{r.stars} 回復 {r.Attributes[PotionAttribute.Healing]:0.0} 総効力 {r.TotalPotency:0.0}";
        }

        /// <summary>数秒間の平均で、描画の回数・影・メモリ確保・フレーム時間を記録する</summary>
        private IEnumerator Measure(string label)
        {
            yield return Wait(0.8f); // 見る向きを変えた直後は除く
            var gc = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            gc.enabled = true;
            int frames = 0;
            double ms = 0, worst = 0, batches = 0, setPass = 0, shadows = 0, tris = 0, allocs = 0;
            float end = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames++;
                double dt = Time.unscaledDeltaTime * 1000.0;
                ms += dt;
                if (dt > worst) worst = dt;
                batches += UnityEditor.UnityStats.batches;
                setPass += UnityEditor.UnityStats.setPassCalls;
                shadows += UnityEditor.UnityStats.shadowCasters;
                tris += UnityEditor.UnityStats.triangles;
                allocs += gc.sampleBlockCount;
            }
            gc.enabled = false;
            int n = Mathf.Max(1, frames);
            Log($"[計測:{label}] 平均 {ms / n:0.0} ms（最大 {worst:0.0}）／ 描画バッチ {batches / n:0} ／ SetPass {setPass / n:0} ／ 影を落とす物 {shadows / n:0} ／ 三角形 {tris / n / 1000:0.0}k ／ メモリ確保 {allocs / n:0.0} 回/フレーム");
        }

        /// <summary>レシピ本：調合台で R → 記録の数・ページの中身 → 名前を付ける（棚の瓶の名前も変わる）→ 種類で絞る → 閉じる</summary>
        private IEnumerator RecipeBookTests(AtelierHUD hud, BrewingStation station, PotionInventory potions, LiquidRecord healing)
        {
            LiquidRegistry registry = station.Alchemy.Liquids;
            Log($"[レシピ本] 記録 {registry.Count} 種類 ／ 新しいページ {registry.UnreadCount}");
            hud.ToggleRecipeBook(); // R キーと同じ
            yield return Wait(0.8f);
            RecipeBookUI book = hud.RecipeBook;
            Log($"[レシピ本] 開いた: {hud.IsRecipeBookOpen}（{book.CurrentMode}）／ 一覧 {book.VisibleCount} ページ ／ 調合台の操作は止まる: {hud.IsModalOpen} ／ カーソル固定 {Cursor.lockState}");
            if (!hud.IsRecipeBookOpen) Debug.LogError("[自動テスト] R でレシピ本が開かない");
            if (healing == null) yield break;
            book.SelectById(healing.id);
            yield return Wait(0.6f);
            Log($"[レシピ本] ページ: {book.DetailSummary}");
            Log($"[レシピ本] 相談の案内: {book.CheckLine}");
            if (healing.recipes.Count == 0) Debug.LogError("[自動テスト] 瓶に詰めた液体の作り方が記録されていない");
            yield return Shot("recipe_book");
            book.BeginRename();
            yield return Wait(0.4f);
            yield return Shot("recipe_book_rename");
            book.ConfirmRename("ミーナのすり傷薬");
            yield return Wait(0.5f);
            PotionStack stack = potions.Find(healing.id);
            Log($"[名前を付ける] 記録: {healing.DisplayName}（もとの名前 {healing.autoName}）／ 棚の瓶: {(stack != null ? stack.potionName : "なし")}");
            if (healing.DisplayName != "ミーナのすり傷薬" || stack == null || stack.potionName != healing.DisplayName)
                Debug.LogError("[自動テスト] 名前を付けても、記録か棚の瓶の名前が変わらない");
            yield return Shot("recipe_book_renamed");
            book.SetFilter(3);
            yield return Wait(0.3f);
            Log($"[レシピ本] 「未知の液体」に絞る: {book.VisibleCount} ページ");
            book.SetFilter(0);
            hud.ToggleRecipeBook();
            yield return Wait(0.4f);
            Log($"[レシピ本] 閉じた: {!hud.IsRecipeBookOpen} ／ 見たページの NEW が消えた: {!healing.unread} ／ 調合台に戻った: {BrewMode.Instance != null && BrewMode.Instance.IsActive}");
        }

        /// <summary>
        /// 相談の続き：
        ///   テストA（作り方を教えてもよい人）… 話す → 力になりたい → 棚の瓶を分け与える → 合わない瓶を渡すと返ってくる
        ///                                     → レシピ本で作り方を教える（瓶は減らない・お礼は少なめ・記録に残る）
        ///   テストB（教えられない人）       … 話す → 力になりたい → 棚の瓶を分け与える → 役立つ瓶を渡す（棚から1本減る）
        ///   最後にセーブして、記録（名前・相談の記録・数）が残っているかを見る
        /// </summary>
        private IEnumerator ConsultationTests(AtelierHUD hud, FirstPersonController controller, PlayerInteractor player, QuestManager quests,
                                              PlayerProgress progress, PotionInventory potions, BrewingStation station, LiquidRecord healing,
                                              QuestData teachQuest, QuestData shareQuest)
        {
            LiquidRegistry registry = station.Alchemy.Liquids;
            controller.LookAt(new Vector3(1.85f, 1.2f, -3.1f));
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Offering && quests.CurrentQuest == teachQuest, 40f);
            Customer customer = quests.CurrentCustomer;
            if (customer == null || quests.CurrentQuest != teachQuest)
            {
                Debug.LogError("[自動テスト] テスト用の相談者（テストA）が来ない");
                yield break;
            }
            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Wait(0.6f);
            Log("[相談] 来店: " + customer.Quest.ClientFullName + " ／ 案内: " + (player.Options.Count > 0 ? player.Options[0].label : "なし"));
            yield return Shot("next_customer");

            // 力になりたい → 棚の瓶を分け与える（レシピ本が「分け与える」で開く）
            quests.TalkTo(customer, player);
            yield return Wait(0.4f);
            yield return ReadDialogueChoosing(hud, "力になりたい", "分け与える");
            yield return Wait(0.6f);
            RecipeBookUI book = hud.RecipeBook;
            Log($"[分け与える] レシピ本: {hud.IsRecipeBookOpen}（{book.CurrentMode}）／ 満タンの瓶がある液体 {book.VisibleCount} 種類");
            if (!hud.IsRecipeBookOpen || book.CurrentMode != RecipeBookUI.Mode.Share) Debug.LogError("[自動テスト] 「棚の瓶を分け与える」でレシピ本が開かない");
            yield return Shot("book_share");
            CheckWords("レシピ本（分け与える）");

            // 合わない瓶（満タンの瓶があって、悩みに合わない液体）を渡そうとする
            LiquidRecord wrong = null;
            foreach (LiquidRecord r in registry.All)
            {
                PotionStack s = potions.Find(r.id);
                if (s != null && s.FullBottles >= 1 && !teachQuest.IsSatisfiedBy(r)) wrong = r;
            }
            if (wrong != null && book.SelectById(wrong.id))
            {
                float before = potions.Find(wrong.id).ml;
                yield return Wait(0.3f);
                Log($"[分け与える] 合わない瓶を選ぶ：{wrong.DisplayName} ／ 案内「{book.CheckLine}」");
                book.PrimaryAction();
                yield return Wait(0.6f);
                Log($"[分け与える] 渡そうとした → 会話: {hud.Dialogue.IsOpen}「{hud.Dialogue.CurrentPageText}」／ 棚の瓶 {before:0} → {(potions.Find(wrong.id) != null ? potions.Find(wrong.id).ml : 0f):0}ml（減らない）／ 相談の状態 {quests.Phase}");
                yield return Shot("share_wrong");
            }
            else
            {
                Log("[分け与える] 合わない満タンの瓶がないので、やめる");
                hud.CloseRecipeBook();
                yield return Wait(0.4f);
                quests.TalkTo(customer, player);
                yield return Wait(0.4f);
            }

            // 作り方を教える
            yield return ReadDialogueChoosing(hud, "作り方を教える");
            yield return Wait(0.6f);
            Log($"[教える] レシピ本: {hud.IsRecipeBookOpen}（{book.CurrentMode}）／ 作り方がある液体 {book.VisibleCount} 種類");
            if (!hud.IsRecipeBookOpen || book.CurrentMode != RecipeBookUI.Mode.Teach) Debug.LogError("[自動テスト] 「作り方を教える」でレシピ本が開かない");
            if (healing != null) book.SelectById(healing.id);
            yield return Wait(0.5f);
            Log($"[教える] 選んだ：{(book.SelectedRecord != null ? book.SelectedRecord.DisplayName : "なし")} ／ 案内「{book.CheckLine}」／ ボタン {book.ActionInteractable}");
            yield return Shot("book_teach");
            int goldBefore = progress.Gold;
            PotionStack healStock = healing != null ? potions.Find(healing.id) : null;
            float stockBefore = healStock != null ? healStock.ml : 0f;
            book.PrimaryAction();
            yield return Wait(0.8f);
            yield return Shot("teach_success");
            yield return ReadDialogue(hud, false);
            yield return Wait(0.5f);
            healStock = healing != null ? potions.Find(healing.id) : null;
            Log($"[教える] 所持金 {goldBefore} → {progress.Gold}（お礼 {progress.Gold - goldBefore} G。分け与えると {teachQuest.RewardGold} G）／ 棚の瓶 {stockBefore:0} → {(healStock != null ? healStock.ml : 0f):0}ml ／ 教えた {progress.TaughtCount} 件 ／ 記録: 教えた相手 {(healing != null ? string.Join("・", healing.taughtTo) : "")}");
            if (progress.TaughtCount < 1 || healing == null || !healing.taughtTo.Contains("テストA")) Debug.LogError("[自動テスト] 作り方を教えても、相談が解決しない（または記録に残らない）");

            // テストB：レシピ本から役立つ瓶を分け与える（棚から1本減る）
            quests.NextQuestOverride = shareQuest;
            if (healing != null && (potions.Find(healing.id) == null || potions.Find(healing.id).FullBottles < 1))
            {
                potions.AddBottled(healing, 100f); // 前の作業で使い切っているので、テスト用に1本足す
                Log("[分け与える] テスト用に、回復の瓶を棚へ1本足した");
            }
            controller.LookAt(new Vector3(1.85f, 1.2f, -3.1f));
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Offering && quests.CurrentQuest == shareQuest, 40f);
            customer = quests.CurrentCustomer;
            if (customer == null || quests.CurrentQuest != shareQuest)
            {
                Debug.LogError("[自動テスト] テスト用の相談者（テストB）が来ない");
                yield break;
            }
            controller.Teleport(new Vector3(-0.1f, 0.02f, -1.25f));
            controller.LookAt(customer.transform.position + Vector3.up * 0.95f);
            yield return Wait(0.6f);
            quests.TalkTo(customer, player);
            yield return Wait(0.4f);
            yield return ReadDialogueChoosing(hud, "力になりたい", "分け与える");
            yield return Wait(0.6f);
            if (healing != null) book.SelectById(healing.id);
            yield return Wait(0.4f);
            healStock = healing != null ? potions.Find(healing.id) : null;
            stockBefore = healStock != null ? healStock.ml : 0f;
            goldBefore = progress.Gold;
            Log($"[分け与える] テストB：{(book.SelectedRecord != null ? book.SelectedRecord.DisplayName : "なし")} ／ 案内「{book.CheckLine}」");
            book.PrimaryAction();
            yield return Wait(0.8f);
            yield return Shot("share_success");
            yield return ReadDialogue(hud, false);
            yield return Wait(0.5f);
            healStock = healing != null ? potions.Find(healing.id) : null;
            Log($"[分け与える] 所持金 {goldBefore} → {progress.Gold} ／ 棚の瓶 {stockBefore:0} → {(healStock != null ? healStock.ml : 0f):0}ml（1本＝100ml 減る）／ 分け与えた {progress.SharedCount} 件 ／ 記録: {(healing != null ? string.Join("・", healing.sharedWith) : "")}");
            if (healing == null || !healing.sharedWith.Contains("テストB") || stockBefore - (healStock != null ? healStock.ml : 0f) < 99f)
                Debug.LogError("[自動テスト] レシピ本から瓶を分け与えても、棚の瓶が減らない（または記録に残らない）");

            // 最後にレシピ本を読む（系譜・相談の記録）→ セーブに残っているか
            hud.ToggleRecipeBook();
            yield return Wait(0.6f);
            if (healing != null) book.SelectById(healing.id);
            yield return Wait(0.5f);
            var children = new System.Collections.Generic.List<LiquidRecord>();
            if (healing != null) registry.CollectChildren(healing.id, children);
            Log($"[レシピ本] 記録 {registry.Count} 種類 ／ 「{(healing != null ? healing.DisplayName : "")}」から生まれた液体 {children.Count} 種類 ／ 作り方 {(healing != null ? healing.recipes.Count : 0)} 通り");
            yield return Shot("recipe_book_final");
            CheckWords("レシピ本（読む）");
            yield return Measure("レシピ本を開いている");
            hud.ToggleRecipeBook();
            yield return Wait(0.3f);
            GameFlow.Instance.Save();
            SaveData saved = SaveSystem.Load();
            LiquidRecord savedHealing = null;
            if (saved != null && healing != null) savedHealing = saved.liquids.Find(x => x.id == healing.id);
            Log($"[セーブ] 版 {(saved != null ? saved.version : 0)} ／ 名前 {(savedHealing != null ? savedHealing.customName : "なし")} ／ 分け与えた {(saved != null ? saved.sharedCount : 0)}・教えた {(saved != null ? saved.taughtCount : 0)} ／ 記録の相談 {(savedHealing != null ? string.Join("・", savedHealing.sharedWith) + "／" + string.Join("・", savedHealing.taughtTo) : "")}");
            if (savedHealing == null || savedHealing.customName != "ミーナのすり傷薬" || saved.taughtCount < 1) Debug.LogError("[自動テスト] 付けた名前・相談の記録がセーブに残っていない");
        }

        /// <summary>自動テスト用の相談者（回復がいちばん強い液体で解決する）</summary>
        private static QuestData MakeTestQuest(string id, string name, bool acceptsRecipe)
        {
            var q = ScriptableObject.CreateInstance<QuestData>();
            q.name = id;
            q.EditorSetup(id, name, "自動テストの相談者", new CustomerLook { cloth = new Color(0.4f, 0.5f, 0.75f) },
                new[] { "（自動テスト）小さな切り傷が、なかなか治らなくて……。" },
                new[] { "ありがとう、助かります！" },
                new[] { "まだ少しずきずきします……。" },
                new[] { "治った！ ありがとう！" },
                new[] { "うーん、これでは治らないみたいです。" },
                new[] { "そうですか……。" },
                "回復の力がいちばん強い液体（回復 15 以上）", "回復の力がある液体ならよさそう", null,
                new[]
                {
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.AtLeast, 15f),
                },
                0, 100, false, "小さな切り傷が、なかなか治らない", acceptsRecipe,
                acceptsRecipe ? new[] { "なるほど、そうやって作るんですね！ 自分でもやってみます。" } : null, 3);
            return q;
        }

        /// <summary>会話を読み進め、選択肢が出たら picks の言葉を含むものを順に選ぶ（選択肢が尽きたら最後の言葉で探す）</summary>
        private IEnumerator ReadDialogueChoosing(AtelierHUD hud, params string[] picks)
        {
            int guard = 0;
            int next = 0;
            while (hud.Dialogue.IsOpen && guard++ < 60)
            {
                if (hud.Dialogue.IsShowingChoices)
                {
                    System.Collections.Generic.IReadOnlyList<string> choices = hud.Dialogue.Choices;
                    string want = picks.Length > 0 ? picks[Mathf.Min(next, picks.Length - 1)] : "";
                    int index = 0;
                    for (int i = 0; choices != null && i < choices.Count; i++)
                    {
                        if (choices[i].Contains(want)) { index = i; break; }
                    }
                    Log($"[会話] 選択肢: {(choices != null ? string.Join(" ／ ", choices) : "")} → 「{(choices != null && index < choices.Count ? choices[index] : "")}」");
                    yield return Shot("dialogue_choices");
                    hud.Dialogue.Choose(index);
                    next++;
                    yield return Wait(0.6f);
                    if (next >= picks.Length) yield break; // 最後の選択をしたら、続きは呼んだ側で見る
                    continue;
                }
                hud.Dialogue.Advance();
                yield return Wait(0.3f);
            }
        }

        /// <summary>画面に出ている文字に「依頼」「納品」「クエスト」が残っていないか（お使いからの脱却）</summary>
        private static void CheckWords(string where)
        {
            string[] banned = { "依頼", "納品", "クエスト", "引き受け" };
            int found = 0;
            foreach (UnityEngine.UI.Text t in FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || string.IsNullOrEmpty(t.text) || !t.isActiveAndEnabled) continue;
                foreach (string word in banned)
                {
                    if (!t.text.Contains(word)) continue;
                    found++;
                    Debug.LogError($"[自動テスト] 画面に「{word}」が残っている（{where}）：{t.text}");
                }
            }
            Log($"[言い換え] {where}：「依頼・納品・クエスト」は画面に {found} 件");
        }

        private IEnumerator ReadDialogue(AtelierHUD hud, bool accept)
        {
            int guard = 0;
            while (hud.Dialogue.IsOpen && guard++ < 60)
            {
                if (hud.Dialogue.IsShowingChoices)
                {
                    yield return Shot("dialogue_choices");
                    hud.Dialogue.Choose(accept ? 0 : 1);
                    yield return Wait(0.6f);
                    if (hud.Dialogue.IsOpen) yield return Shot("dialogue_after_choice");
                    continue;
                }
                hud.Dialogue.Advance();
                yield return Wait(0.3f);
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float timeout)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeout)
                {
                    Log("時間切れで待つのをやめた");
                    yield break;
                }
                yield return null;
            }
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            shot++;
            string path = Path.Combine(folder, $"{shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(path, 2);
            yield return null;
        }

        private static IngredientSource Find(IngredientSource[] sources, string id)
        {
            foreach (IngredientSource s in sources)
            {
                if (s.Ingredient != null && s.Ingredient.IngredientId == id) return s;
            }
            Debug.LogError("[AutoTest] 素材が見つかりません: " + id);
            return null;
        }

        private static Vector3 Center(Component c)
        {
            var col = c.GetComponent<Collider>();
            return col != null ? col.bounds.center : c.transform.position;
        }

        private static void Log(string message)
        {
            Debug.Log("[AutoTest] " + message);
            string dir = Path.Combine(ProjectRoot, "AutoTestShots");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "log.txt"), message + "\n");
        }
    }
}
#endif
