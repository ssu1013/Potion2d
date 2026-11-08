#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D 版の通しテスト（エディタ専用。製品版には含まれない）。
    /// メニュー「Tools/Potion Frontier 2D/仕上げ/④ 通しテストを実行」から始める。
    ///   タイトル → 設定 → はじめから → お客さんの来店 → お客さんをクリックして相談を聞く → 作業台をクリック
    ///   → トレイから水を取って傾けて注ぐ → 薬草を2回入れる → 円を描いてかき混ぜる → 「全部詰める」→ レシピ本
    ///   → 部屋に戻る → 棚・取引先帳 → お客さんに瓶を分け与える → ポーズ → 設定 → タイトルへ（セーブ）→ つづきから（ロードして同じか）
    /// を、マウスの代わりの「指」（Pointer2D）で実際に操作して確かめ、各場面を AutoTestShots2D/ に保存して再生を止める。
    /// セーブは autotest.json に書くので、ふだんのセーブデータは消えない。テスト中はマウスでゲーム画面を触らないこと。
    /// </summary>
    public class AtelierAutoTest2D : MonoBehaviour
    {
        public const string SessionKey = "PotionFrontier2D.AutoTest";
        private const float Watchdog = 300f;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string Folder => Path.Combine(ProjectRoot, "AutoTestShots2D");

        private int shot;
        private int errors;
        private int warnings;
        private float startTime;
        private bool finished;
        private LiquidRecord lastBottled;
        private bool ringSeen;

        private GameFlow flow;
        private AtelierHUD hud;
        private AtelierView2D view;
        private QuestManager quests;
        private PlayerProgress progress;
        private IngredientInventory stock;
        private PotionInventory potions;
        private BrewSession2D session;
        private BrewInput2D input;
        private BrewTray2D tray;
        private Flask2D flask;
        private Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!UnityEditor.SessionState.GetBool(SessionKey, false)) return;
            UnityEditor.SessionState.EraseBool(SessionKey);
            if (FindFirstObjectByType<AtelierView2D>() == null)
            {
                Debug.LogError("[自動テスト] アトリエのシーン（Assets/Scenes/Atelier2D.unity）で実行してください");
                return;
            }
            SaveSystem.Slot = "autotest";
            SaveSystem.Delete();
            new GameObject("AtelierAutoTest2D").AddComponent<AtelierAutoTest2D>();
        }

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
            if (message.StartsWith("[自動テスト]")) return; // 自分の記録（Log）は二重に数えない
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                Write("!! " + type + ": " + message + (type == LogType.Exception ? "\n" + stackTrace : ""));
            }
            else if (type == LogType.Warning)
            {
                warnings++;
                Write("（警告）" + message);
            }
        }

        private void Update()
        {
            if (finished || Time.realtimeSinceStartup - startTime < Watchdog) return;
            Fail($"{Watchdog:0} 秒たっても終わらないので、途中で止めた（どこかで止まっている）");
            StopAllCoroutines();
            StartCoroutine(Finish());
        }

        // ==================================================================
        // 本体
        // ==================================================================

        private IEnumerator Start()
        {
            startTime = Time.realtimeSinceStartup;
            Directory.CreateDirectory(Folder);
            foreach (string f in Directory.GetFiles(Folder, "*.png")) File.Delete(f);
            File.WriteAllText(Path.Combine(Folder, "log.txt"), "");
            Write($"2D 通しテスト {System.DateTime.Now:yyyy/MM/dd HH:mm:ss} ／ Unity {Application.unityVersion}");

            flow = GameFlow.Instance;
            hud = AtelierHUD.Instance;
            view = AtelierView2D.Current != null ? AtelierView2D.Current : FindFirstObjectByType<AtelierView2D>();
            quests = FindFirstObjectByType<QuestManager>();
            progress = FindFirstObjectByType<PlayerProgress>();
            stock = FindFirstObjectByType<IngredientInventory>();
            potions = FindFirstObjectByType<PotionInventory>();
            session = FindFirstObjectByType<BrewSession2D>();
            input = FindFirstObjectByType<BrewInput2D>();
            tray = FindFirstObjectByType<BrewTray2D>();
            flask = FindFirstObjectByType<Flask2D>();
            if (flow == null || hud == null || view == null || quests == null || session == null || input == null || flask == null)
            {
                Fail($"シーンに足りない物がある（GameFlow {flow != null} / AtelierHUD {hud != null} / AtelierView2D {view != null} / QuestManager {quests != null} / BrewSession2D {session != null} / BrewInput2D {input != null} / Flask2D {flask != null}）");
                yield return Finish();
                yield break;
            }
            session.Bottled += OnBottled;

            // ---- 画面の比率（計算だけ）・描く順番 -----------------------------------
            AspectMath();
            int workbench = SortingLayer.GetLayerValueFromName("Workbench");
            foreach (string layer in new[] { "Flask", "Liquid", "Items", "Held", "FX" })
            {
                Check(SortingLayer.NameToID(layer) != 0 && SortingLayer.GetLayerValueFromName(layer) > workbench,
                      $"描く順番「{layer}」が、部屋・作業台の絵より手前（番号 {SortingLayer.NameToID(layer)}）");
            }

            // ---- タイトル ----------------------------------------------------------
            yield return Wait(2.5f);
            cam = Camera.main;
            Log($"画面 {Screen.width}×{Screen.height} ／ 絵の範囲 {Fmt(CameraAspect2D.ViewRect)} ／ カメラ {(cam != null ? cam.name : "なし")}");
            Check(hud.Title != null && hud.Title.IsOpen, "タイトル画面が出ている");
            Check(flow.Mode == GameMode.Title, "モードがタイトル");
            CheckFonts("タイトル");
            yield return Shot("title");
            yield return Measure("タイトル");
            hud.OpenSettings();
            yield return Wait(0.6f);
            Check(hud.IsSettingsOpen, "タイトルから設定を開ける");
            yield return Shot("title_settings");
            hud.CloseSettings();
            yield return Wait(0.3f);

            // ---- はじめから --------------------------------------------------------
            hud.Title.ForceNewGame();
            yield return WaitUntil(() => flow.Mode == GameMode.Playing, 8f, "はじめから → プレイ中になる");
            yield return Wait(1.4f);
            Pointer2D.BeginSimulation(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            IngredientDatabase db = GameContext.Ingredients;
            IngredientData water = db != null ? db.Get("base_water") : null;
            IngredientData herb = db != null ? db.Get("main_herb") : null;
            Check(water != null && herb != null, "素材カタログに水と薬草がある");
            Log($"所持金 {progress.Gold} G ／ 素材 {(db != null ? db.Count : 0)} 種類のうち {(stock != null ? stock.CountKinds(db) : 0)} 種類を持っている ／ いる所 {AtelierView2D.StopNames[view.Stop]}");
            yield return Shot("room_start");
            yield return Measure("部屋");
            CheckWords("部屋");

            // ---- 部屋の移動（＜＞と同じ動き）----------------------------------------
            for (int i = 0; i < AtelierView2D.StopNames.Length; i++)
            {
                view.GoToStop(i);
                yield return WaitUntil(() => !view.Sliding, 4f, "部屋の移動が終わる");
                yield return Wait(0.2f);
                Check(view.Stop == i, $"「{AtelierView2D.StopNames[i]}」へ移動できる");
                yield return Shot("room_" + i);
            }

            // ---- お客さんの来店 ----------------------------------------------------
            view.GoToStop(0);
            yield return WaitUntil(() => !view.Sliding, 4f, "カウンターへ移動");
            quests.SpawnNow();
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Arriving, 6f, "お客さんが来る");
            yield return Wait(0.8f);
            yield return Shot("customer_arriving");
            yield return WaitUntil(() => quests.Phase == CustomerPhase.Offering, 25f, "お客さんがカウンターに着く");
            Customer2D customer = quests.CurrentCustomer;
            if (customer == null)
            {
                Fail("お客さんが来ない");
                yield return Finish();
                yield break;
            }
            QuestData quest = customer.Quest;
            Log($"来店：{quest.ClientFullName}（{quest.QuestId}）");
            yield return Wait(0.6f);

            // お客さんを指す → 名前が出る → クリックで話を聞く
            yield return Hover(ColliderScreen(customer), 0.3f);
            Check(view.HoverTitle == quest.ClientFullName, $"お客さんを指すと名前が出る（{view.HoverTitle}）");
            yield return Shot("customer_hover");
            yield return Click(ColliderScreen(customer));
            yield return Wait(0.4f);
            Check(hud.Dialogue.IsOpen, "お客さんをクリックすると会話が始まる");
            yield return Shot("dialogue");
            yield return ReadDialogue("力になりたい", "探求してくる");
            Check(quests.Phase == CustomerPhase.Waiting, "「力になりたい」で相談に乗っている状態になる");
            yield return Wait(0.4f);
            yield return Shot("quest_accepted");
            CheckWords("相談メモ");

            // ---- 作業台をクリック → 調合台 ----------------------------------------
            view.GoToStop(1);
            yield return WaitUntil(() => !view.Sliding, 4f, "作業台へ移動");
            RoomHotspot2D bench = FindHotspot(RoomHotspot2D.Kind.Workbench);
            Check(bench != null, "作業台のクリックする所がある");
            if (bench != null)
            {
                yield return Hover(SpotScreen(bench), 0.3f);
                Log($"[部屋] 指している：{view.HoverTitle}（{view.HoverSubtitle}）");
                yield return Shot("bench_hover");
                yield return Click(SpotScreen(bench));
            }
            else view.EnterBrew();
            yield return Wait(0.3f);
            yield return Shot("brew_entering");
            yield return WaitUntil(() => view.InBrew && !view.Transitioning, 5f, "調合台に寄り終わる");
            yield return Wait(0.4f);
            Check(view.InBrew, "作業台をクリックすると調合台に入る");
            BrewFeedback2D feedback = BrewFeedback2D.Current;
            Check(feedback != null, "調合台に手ごたえ（見立てカード・温度計）がある");
            BrewHUD2D brewHud = FindFirstObjectByType<BrewHUD2D>();
            if (brewHud != null && brewHud.LayoutVersion < 4) Warn("調合画面の配置が古いまま（メニュー「Step4：調合画面を新しくする」）");
            yield return Shot("brew_view");
            yield return Measure("調合台");

            // ---- 調合：水を注ぐ ----------------------------------------------------
            float waterBefore = stock.Get(water);
            TrayItem2D waterItem = FindTrayItem(water);
            if (waterItem != null)
            {
                SpriteRenderer body = waterItem.GetComponentInChildren<SpriteRenderer>();
                Check(body != null && body.sortingLayerName == "Items" && body.sortingLayerID != 0, $"トレイの素材が「Items」の順番で描かれる（{(body != null ? body.sortingLayerName : "なし")}）");
            }
            yield return PickFromTray(water);
            Check(input.Held != null && input.Held.Ingredient == water, "トレイの水をクリックすると手に持つ");
            yield return Hover(FlaskScreen(0.85f), 0.35f);
            Check(input.Hovered == BrewInput2D.HoverKind.Flask, "持ったままフラスコを指せる");
            yield return Shot("held_water");
            float poured = 0f;
            yield return Pour(100f, 5f, ml => poured = ml);
            Log($"[注ぐ] {poured:0}ml 注いだ（フラスコ {session.Flask.VolumeMl:0}ml）／ 水の在庫 {IngredientInventory.Format(water, waterBefore)} → {IngredientInventory.Format(water, stock.Get(water))}");
            Check(poured >= 60f, "押したまま下へ動かすと注げる");
            Pointer2D.RightClick();
            yield return Wait(0.2f);
            Check(input.Held == null, "右クリックで手の物を戻せる");

            // ---- 薬草を2回入れる ---------------------------------------------------
            yield return PickFromTray(herb);
            Check(input.Held != null && input.Held.Ingredient == herb, "トレイの薬草を持てる");
            yield return Hover(FlaskScreen(0.85f), 0.3f);
            yield return Click(FlaskScreen(0.85f));
            yield return Wait(0.25f);
            yield return Shot("drop_piece");
            yield return Wait(0.5f);
            yield return Click(FlaskScreen(0.85f));
            yield return Wait(0.8f);
            float herbUnits = session.Flask.UnitsOf(herb.IngredientId);
            Log($"[入れる] 薬草 {herbUnits:0.##} 回分");
            Check(herbUnits >= 1.5f, "クリック2回で薬草が2回分入る");
            Pointer2D.RightClick();
            yield return Wait(0.6f);
            FlaskSediment2D sediment = FindFirstObjectByType<FlaskSediment2D>();
            Check(sediment != null && sediment.VisiblePieces > 0, $"溶け残りの薬草が、フラスコの底に見える（{(sediment != null ? sediment.VisiblePieces : 0)} 個）");
            if (feedback != null)
            {
                float now = feedback.CurrentValue(PotionAttribute.Healing);
                float soon = feedback.ProjectedValue(PotionAttribute.Healing);
                Log($"[見立て] 混ぜる前：回復 今 {now:0.0} → 溶かしきると {soon:0.0} ／ 目標の線 {feedback.TargetRows} 本 ／ {feedback.Goal} ／ {feedback.Status.Replace('\n', ' ')}");
                Check(feedback.TargetRows >= 1, "見立てカードに、相談の目標の線が出る");
                Check(soon > now + 1f, "溶け残りの「これから増える分」が、今の値の先に予告される");
            }
            yield return Shot("meter_before_stir");

            // ---- 円を描いてかき混ぜる -----------------------------------------------
            float turnsBefore = session.Flask.TotalTurns;
            yield return Stir(4.5f, 1.5f);
            yield return Wait(0.6f);
            LiquidState state = session.Flask.Snapshot();
            Log($"[混ぜる] {session.Flask.TotalTurns - turnsBefore:0.#} 回転 ／ 溶け具合 {state.dissolvedFraction:P0} ／ {Describe(session.Preview)}");
            Check(session.Flask.TotalTurns - turnsBefore >= 3f, "マウスで円を描くとかき混ぜられる");
            yield return Shot("stirred");
            bool matches = session.Preview != null && quest.IsSatisfiedBy(session.Preview);
            Log($"[見立て] 今の液体は{quest.ClientName}さんの悩みに{(matches ? "合う" : "合わない")}");
            if (feedback != null)
            {
                Log($"[見立て] 混ぜたあと：回復 {feedback.CurrentValue(PotionAttribute.Healing):0.0} ／ {feedback.Goal} ／ {feedback.Status.Replace('\n', ' ')}");
                Check(ringSeen, "かき混ぜている間、カーソルのそばに溶け具合の輪が出る");
                if (matches) Check(feedback.Goal == BrewFeedback2D.GoalState.Met, "見立てカードが「悩みに合いそう」になる");
            }

            // ---- 瓶に詰める（「全部詰める」ボタン）------------------------------------
            lastBottled = null;
            Button bottleAll = FindButton("BottleAllButton");
            Check(bottleAll != null, "「全部詰める」ボタンがある");
            if (bottleAll != null) bottleAll.onClick.Invoke();
            else session.BottleAll();
            yield return Wait(1.1f);
            yield return Shot("bottled");
            LiquidRecord bottled = lastBottled;
            if (feedback != null)
            {
                Check(feedback.ResultVisible, "瓶詰めの結果カードが出る");
                yield return Wait(0.8f);
                Log($"[結果カード] ★ {feedback.ResultStarsShown} 個（記録は ★{(bottled != null ? bottled.stars : 0)}）");
                Check(bottled == null || feedback.ResultStarsShown == bottled.stars, "結果カードの ★ が1つずつ全部付く");
                feedback.PreviewReaction("テストの反応");
                yield return Wait(0.15f);
                Log($"[反応の演出] スロー中の時間の速さ {Time.timeScale:0.00}");
                yield return Shot("reaction_moment");
                yield return Wait(1.2f);
                Check(Mathf.Approximately(Time.timeScale, 1f), "反応のスローのあと、時間の速さが元に戻る");
            }
            Check(bottled != null, "ボタンで瓶に詰められる");
            PotionStack stack = bottled != null ? potions.Find(bottled.id) : null;
            Log($"[瓶詰め] {Describe(bottled)} ／ 棚 {(stack != null ? stack.DescribeAmount() : "なし")} ／ フラスコの残り {session.Flask.VolumeMl:0}ml");
            Check(stack != null && stack.FullBottles >= 1, "満タンの瓶が棚に入る");

            // 火加減（空のフラスコで、つまみ・炎の見た目だけ）
            session.SetHeat(0.6f);
            yield return Wait(0.8f);
            yield return Shot("heat");
            session.SetHeat(0f);

            // ---- レシピ本 ----------------------------------------------------------
            hud.OpenRecipeBook();
            yield return Wait(0.6f);
            Check(hud.IsRecipeBookOpen, "調合台でレシピ本を開ける");
            if (bottled != null) Check(hud.RecipeBook.SelectById(bottled.id), "作った液体がレシピ本に載っている");
            yield return Wait(0.3f);
            yield return Shot("recipe_book");
            Log("[レシピ本] " + hud.RecipeBook.DetailSummary);
            hud.CloseRecipeBook();
            yield return Wait(0.3f);

            // ---- 部屋に戻る → 棚・取引先帳 ------------------------------------------
            view.ExitBrew();
            yield return WaitUntil(() => !view.InBrew && !view.Transitioning, 5f, "部屋に戻る");
            yield return Wait(0.3f);
            Check(!view.InBrew, "調合台から部屋に戻れる");
            view.GoToStop(2);
            yield return WaitUntil(() => !view.Sliding, 4f, "棚へ移動");
            yield return Wait(0.3f);
            yield return Shot("shelf_with_bottle");
            view.GoToStop(0);
            yield return WaitUntil(() => !view.Sliding, 4f, "カウンターへ移動");
            RoomHotspot2D ledgerSpot = FindHotspot(RoomHotspot2D.Kind.Ledger);
            if (ledgerSpot != null) yield return Click(SpotScreen(ledgerSpot));
            else hud.OpenLedger();
            yield return Wait(0.6f);
            Check(hud.IsLedgerOpen, "カウンターの取引先帳をクリックすると開く");
            yield return Shot("ledger");
            hud.CloseLedger();
            yield return Wait(0.3f);

            // ---- 分け与える --------------------------------------------------------
            int goldBefore = progress.Gold;
            yield return Click(ColliderScreen(customer));
            yield return Wait(0.4f);
            yield return ReadDialogue("棚の瓶を分け与える");
            yield return WaitUntil(() => hud.IsRecipeBookOpen, 3f, "分け与える瓶を選ぶレシピ本が開く");
            PotionStack shareable = quests.FindShareable();
            Log($"[分け与える] 合う瓶：{(shareable != null ? shareable.potionName : "なし")}");
            if (bottled != null) hud.RecipeBook.SelectById(bottled.id);
            yield return Wait(0.3f);
            Log("[分け与える] " + hud.RecipeBook.CheckLine + " ／ ボタンが押せる: " + hud.RecipeBook.ActionInteractable);
            yield return Shot("share_book");
            hud.RecipeBook.PrimaryAction();
            yield return Wait(0.5f);
            yield return Shot("share_result");
            yield return ReadDialogue();
            yield return Wait(0.5f);
            if (matches)
            {
                Check(progress.Gold == goldBefore + quest.RewardGold, $"お礼を受け取る（{goldBefore} → {progress.Gold} G）");
                Check(progress.CompletedQuests >= 1 && progress.SharedCount >= 1, "解決した数・分け与えた数が増える");
            }
            else
            {
                Warn("作った液体が相談に合わなかったので、お礼の確認は飛ばした（調合の数値が変わった？）");
                yield return ReadDialogue("今は力になれない");
            }
            yield return WaitUntil(() => quests.Phase == CustomerPhase.None, 10f, "お客さんが帰る");
            yield return Shot("after_share");

            // ---- ポーズ・設定 ------------------------------------------------------
            hud.OpenPause();
            yield return Wait(0.5f);
            Check(flow.IsPaused && Time.timeScale == 0f, "Esc のポーズで時間が止まる");
            yield return Shot("pause");
            hud.ClosePause();
            yield return Wait(0.2f);
            Check(!flow.IsPaused && Time.timeScale == 1f, "ポーズを閉じると時間が動く");
            hud.OpenSettings();
            yield return Wait(0.5f);
            yield return Shot("settings");
            hud.CloseSettings();
            yield return Wait(0.3f);

            // ---- セーブ → タイトル → つづきから ----------------------------------------
            int gold = progress.Gold;
            int completed = progress.CompletedQuests;
            int liquids = session.Alchemy != null ? session.Alchemy.Liquids.Count : 0;
            float potionMl = TotalMl();
            float herbStock = stock.Get(herb);
            flow.ReturnToTitle();
            yield return WaitUntil(() => flow.Mode == GameMode.Title && hud.Title.IsOpen, 8f, "タイトルへ戻る");
            yield return Wait(1.2f);
            Check(SaveSystem.Exists, "タイトルへ戻るとセーブされる");
            yield return Shot("title_with_save");
            flow.ContinueGame();
            yield return WaitUntil(() => flow.Mode == GameMode.Playing, 8f, "つづきから → プレイ中になる");
            yield return Wait(1.5f);
            yield return Shot("continued");
            int liquidsAfter = session.Alchemy != null ? session.Alchemy.Liquids.Count : 0;
            Log($"[つづきから] 所持金 {gold} → {progress.Gold} ／ 解決 {completed} → {progress.CompletedQuests} ／ 液体 {liquids} → {liquidsAfter} 種類 ／ 瓶 {potionMl:0} → {TotalMl():0}ml ／ 薬草 {herbStock:0.##} → {stock.Get(herb):0.##}");
            Check(progress.Gold == gold && progress.CompletedQuests == completed, "所持金・解決した数がセーブから戻る");
            Check(liquidsAfter == liquids, "レシピ本の液体がセーブから戻る");
            Check(Mathf.Abs(TotalMl() - potionMl) < 0.6f, "棚の瓶がセーブから戻る");
            float herbAfter = stock.Get(herb);
            bool sameStock = (float.IsInfinity(herbAfter) && float.IsInfinity(herbStock)) || Mathf.Abs(herbAfter - herbStock) < 0.01f;
            Check(sameStock, "素材の在庫がセーブから戻る");
            CheckWords("つづきから");

            yield return Finish();
        }

        // ==================================================================
        // 操作（マウスの代わり）
        // ==================================================================

        private IEnumerator Hover(Vector2 at, float settle)
        {
            Pointer2D.Move(at);
            yield return Wait(settle);
        }

        private IEnumerator Click(Vector2 at)
        {
            Pointer2D.Move(at);
            yield return null;
            yield return null;
            Pointer2D.Press();
            yield return null;
            yield return null;
            Pointer2D.Release();
            yield return null;
        }

        private IEnumerator PickFromTray(IngredientData data)
        {
            if (data == null || tray == null) yield break;
            TrayItem2D item = null;
            for (int page = 0; page < tray.PageCount && item == null; page++)
            {
                item = FindTrayItem(data);
                if (item != null) break;
                tray.ChangePage(1);
                yield return Wait(0.2f);
            }
            if (item == null)
            {
                Fail($"トレイに「{data.DisplayName}」が見つからない");
                yield break;
            }
            yield return Hover(ColliderScreen(item), 0.25f);
            Log($"[トレイ] 指している：{input.HoverTitle}（{input.HoverSubtitle}）");
            yield return Click(ColliderScreen(item));
            yield return Wait(0.2f);
        }

        /// <summary>フラスコの上で押したまま、ゆっくり下へ動かして傾ける。target ml 入るか、時間切れまで</summary>
        private IEnumerator Pour(float targetMl, float maxSeconds, System.Action<float> result)
        {
            float start = session.Flask.VolumeMl;
            Vector2 p = FlaskScreen(0.85f);
            Pointer2D.Move(p);
            yield return null;
            Pointer2D.Press();
            yield return null;
            float t = 0f;
            float down = 300f * Screen.height / 1080f; // 傾け切る距離より少し多め
            bool shotTaken = false;
            while (t < maxSeconds && session.Flask.VolumeMl - start < targetMl)
            {
                t += Time.unscaledDeltaTime;
                Pointer2D.Move(p + Vector2.down * down * Mathf.Clamp01(t / 0.8f));
                if (!shotTaken && t > 0.9f)
                {
                    shotTaken = true;
                    Log($"[注ぐ] 傾き {input.Tilt:0.00} ／ {input.PourRate:0} ml/秒");
                    yield return Shot("pouring");
                }
                yield return null;
            }
            Pointer2D.Release();
            yield return Wait(0.3f);
            result(session.Flask.VolumeMl - start);
        }

        /// <summary>手ぶらでフラスコの上を押したまま、時計回りに円を描く</summary>
        private IEnumerator Stir(float seconds, float turnsPerSecond)
        {
            Vector2 center = WorldToScreen(Vector3.Lerp(flask.BottomWorld, flask.SurfaceWorld, 0.55f));
            float radius = 0.06f * Screen.height;
            float a = Mathf.PI * 0.5f;
            Pointer2D.Move(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            yield return Wait(0.2f);
            Check(input.Hovered == BrewInput2D.HoverKind.Flask, "手ぶらでフラスコを指せる");
            Pointer2D.Press();
            yield return null;
            float t = 0f;
            bool shotTaken = false;
            while (t < seconds)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                a -= turnsPerSecond * Mathf.PI * 2f * dt; // 画面の上が +Y なので、角度を減らすと時計回り
                Pointer2D.Move(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                if (BrewFeedback2D.Current != null && BrewFeedback2D.Current.RingVisible) ringSeen = true;
                if (!shotTaken && t > seconds * 0.5f)
                {
                    shotTaken = true;
                    yield return Shot("stirring");
                }
                yield return null;
            }
            Pointer2D.Release();
        }

        private IEnumerator ReadDialogue(params string[] picks)
        {
            var queue = new Queue<string>(picks);
            int guard = 0;
            while (hud.Dialogue.IsOpen && guard++ < 120)
            {
                if (hud.Dialogue.IsShowingChoices)
                {
                    if (queue.Count == 0)
                    {
                        Log("[会話] 選ぶものが残っていないので、ここで止める：" + string.Join(" ／ ", hud.Dialogue.Choices));
                        yield break;
                    }
                    string want = queue.Dequeue();
                    int index = -1;
                    IReadOnlyList<string> choices = hud.Dialogue.Choices;
                    for (int i = 0; choices != null && i < choices.Count; i++)
                    {
                        if (choices[i].Contains(want)) { index = i; break; }
                    }
                    if (index < 0)
                    {
                        Fail($"会話に「{want}」の選択肢がない（{string.Join(" ／ ", choices)}）");
                        yield break;
                    }
                    Log($"[会話] 「{choices[index]}」を選ぶ");
                    hud.Dialogue.Choose(index);
                    yield return Wait(0.4f);
                    continue;
                }
                hud.Dialogue.Advance();
                yield return Wait(0.12f);
            }
        }

        // ==================================================================
        // 確かめる・記録する
        // ==================================================================

        private void AspectMath()
        {
            // 横長は左右に帯、縦長（16:10・4:3）は上下に帯、16:9 とほぼ 16:9 は帯なし
            CheckRect(1920, 1080, new Rect(0f, 0f, 1f, 1f));
            CheckRect(1366, 768, new Rect(0f, 0f, 1f, 1f));
            CheckRect(3840, 2160, new Rect(0f, 0f, 1f, 1f));
            CheckRect(2560, 1080, new Rect(0.125f, 0f, 0.75f, 1f));
            CheckRect(1280, 800, new Rect(0f, 0.05f, 1f, 0.9f));
            CheckRect(1024, 768, new Rect(0f, 0.125f, 1f, 0.75f));
        }

        private void CheckRect(int w, int h, Rect expected)
        {
            Rect r = CameraAspect2D.Compute(w, h);
            bool ok = Mathf.Abs(r.x - expected.x) < 0.002f && Mathf.Abs(r.y - expected.y) < 0.002f &&
                      Mathf.Abs(r.width - expected.width) < 0.002f && Mathf.Abs(r.height - expected.height) < 0.002f;
            Check(ok, $"画面の比率 {w}×{h} → 絵の範囲 {Fmt(r)}");
        }

        private void CheckFonts(string where)
        {
            Font expected = UIFactory.Skin != null ? UIFactory.Skin.font : null;
            if (expected == null)
            {
                Warn($"[フォント] UISkin にフォントが入っていない（OS のフォントで表示中）：{where}");
                return;
            }
            Font bold = UIFactory.BoldFont;
            int total = 0, other = 0;
            var names = new List<string>();
            foreach (Text t in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                total++;
                if (t.font == expected || (bold != null && t.font == bold)) continue;
                other++;
                if (names.Count < 6) names.Add(t.name + "（" + (t.font != null ? t.font.name : "なし") + "）");
            }
            Log($"[フォント] {where}：文字 {total} 個のうち、ちがうフォント {other} 個 {string.Join("、", names)}");
            if (other > 0) Warn("[フォント] UISkin のフォントになっていない文字がある");
        }

        private void CheckWords(string where)
        {
            string[] banned = { "依頼", "納品", "クエスト", "引き受け", "WASD", "E キー", "Eキー", "Tab キー", "Tabキー" };
            int found = 0;
            foreach (Text t in FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || string.IsNullOrEmpty(t.text) || !t.isActiveAndEnabled) continue;
                foreach (string word in banned)
                {
                    if (!t.text.Contains(word)) continue;
                    found++;
                    Fail($"画面に「{word}」が残っている（{where}）：{t.text}");
                }
            }
            Log($"[言葉] {where}：使わない言葉・3D 版の操作の説明は {found} 件");
        }

        private IEnumerator Measure(string label)
        {
            float total = 0f, worst = 0f;
            int frames = 0;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                total += dt;
                worst = Mathf.Max(worst, dt);
                frames++;
            }
            float avg = frames > 0 ? total / frames : 0f;
            Log($"[重さ] {label}：平均 {avg * 1000f:0.0}ms（{(avg > 0f ? 1f / avg : 0f):0} fps）／ いちばん遅いフレーム {worst * 1000f:0.0}ms（エディタの中なので、製品版より重めに出る）");
        }

        private void Check(bool ok, string what)
        {
            if (ok) Log("○ " + what);
            else Fail(what);
        }

        private void Fail(string what)
        {
            errors++;
            Write("× " + what);
            Debug.LogError("[自動テスト] × " + what);
        }

        private void Warn(string what)
        {
            warnings++;
            Write("△ " + what);
            Debug.LogWarning("[自動テスト] △ " + what);
        }

        private static void Log(string message)
        {
            Write(message);
            Debug.Log("[自動テスト] " + message);
        }

        private static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, "log.txt"), message + "\n", Encoding.UTF8);
            }
            catch (IOException) { }
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            shot++;
            ScreenCapture.CaptureScreenshot(Path.Combine(Folder, $"{shot:00}_{name}.png"), 1);
            yield return null;
        }

        private IEnumerator Finish()
        {
            if (finished) yield break;
            finished = true;
            Pointer2D.EndSimulation();
            if (session != null) session.Bottled -= OnBottled;
            float seconds = Time.realtimeSinceStartup - startTime;
            Write("");
            Write(errors == 0
                ? $"＝＝ 通しテスト：問題なし（警告 {warnings} 件・{seconds:0} 秒）＝＝"
                : $"＝＝ 通しテスト：問題 {errors} 件・警告 {warnings} 件（{seconds:0} 秒）。上の「×」「!!」の行を見てください ＝＝");
            Debug.Log($"[自動テスト] 終わり：問題 {errors} 件・警告 {warnings} 件。記録と画面写真は {Folder}");
            yield return Wait(0.5f);
            SaveSystem.Delete();
            SaveSystem.Slot = "save";
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // ==================================================================
        // 小物
        // ==================================================================

        private void OnBottled(LiquidRecord record, bool isNew, float ml, int bottles)
        {
            lastBottled = record;
        }

        private IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string what)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeout)
                {
                    Fail($"{timeout:0} 秒待っても「{what}」にならない");
                    yield break;
                }
                yield return null;
            }
        }

        private Vector2 WorldToScreen(Vector3 world)
        {
            if (cam == null) cam = Camera.main;
            return cam != null ? (Vector2)cam.WorldToScreenPoint(world) : Vector2.zero;
        }

        private Vector2 ColliderScreen(Component c)
        {
            Collider2D col = c != null ? c.GetComponentInChildren<Collider2D>() : null;
            return WorldToScreen(col != null ? col.bounds.center : c != null ? c.transform.position : Vector3.zero);
        }

        /// <summary>
        /// 部屋のクリックする所の中で、ほかの小さい所（作業台の上のレシピ本など）やお客さんに重ならない点。
        /// 重なっているときは小さい方が選ばれるので、真ん中を押すとちがう物が開くことがある
        /// </summary>
        private Vector2 SpotScreen(RoomHotspot2D target)
        {
            Collider2D col = target.GetComponent<Collider2D>();
            if (col == null) return ColliderScreen(target);
            Bounds b = col.bounds;
            for (int gy = 0; gy < 5; gy++)
            {
                for (int gx = 0; gx < 5; gx++)
                {
                    // 真ん中から外へ順に試す
                    float fx = 0.5f + (gx % 2 == 0 ? 1f : -1f) * ((gx + 1) / 2) * 0.17f;
                    float fy = 0.5f + (gy % 2 == 0 ? 1f : -1f) * ((gy + 1) / 2) * 0.17f;
                    var p = new Vector2(Mathf.Lerp(b.min.x, b.max.x, fx), Mathf.Lerp(b.min.y, b.max.y, fy));
                    if (TopSpotAt(p) == target) return WorldToScreen(p);
                }
            }
            return ColliderScreen(target);
        }

        private static RoomHotspot2D TopSpotAt(Vector2 world)
        {
            RoomHotspot2D best = null;
            float bestArea = float.MaxValue;
            foreach (Collider2D c in Physics2D.OverlapPointAll(world))
            {
                if (c.GetComponentInParent<Customer2D>() != null) return null;
                RoomHotspot2D h = c.GetComponent<RoomHotspot2D>();
                if (h == null) continue;
                float area = c.bounds.size.x * c.bounds.size.y;
                if (area < bestArea) { best = h; bestArea = area; }
            }
            return best;
        }

        /// <summary>フラスコの中の高さ（0=底・1=口）の画面の位置</summary>
        private Vector2 FlaskScreen(float height01)
        {
            return WorldToScreen(Vector3.Lerp(flask.BottomWorld, flask.MouthWorld, height01));
        }

        private TrayItem2D FindTrayItem(IngredientData data)
        {
            foreach (TrayItem2D item in FindObjectsByType<TrayItem2D>(FindObjectsSortMode.None))
            {
                if (item != null && item.isActiveAndEnabled && item.Entry.ingredient == data) return item;
            }
            return null;
        }

        private static RoomHotspot2D FindHotspot(RoomHotspot2D.Kind kind)
        {
            foreach (RoomHotspot2D h in FindObjectsByType<RoomHotspot2D>(FindObjectsSortMode.None))
            {
                if (h.Type == kind) return h;
            }
            return null;
        }

        private static Button FindButton(string objectName)
        {
            foreach (Button b in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (b.name == objectName) return b;
            }
            return null;
        }

        private float TotalMl()
        {
            float ml = 0f;
            if (potions == null) return 0f;
            foreach (PotionStack s in potions.Stacks) ml += s.ml;
            return ml;
        }

        private static string Describe(LiquidRecord r)
        {
            return r == null ? "なし" : $"{r.DisplayName}（★{r.stars}・{r.kind}）";
        }

        private static string Fmt(Rect r)
        {
            return $"x{r.x:0.000} y{r.y:0.000} 幅{r.width:0.000} 高さ{r.height:0.000}";
        }
    }
}
#endif
