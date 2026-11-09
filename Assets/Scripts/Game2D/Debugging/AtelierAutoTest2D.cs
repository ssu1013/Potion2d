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
    /// 2D 版（1画面版）の通しテスト（エディタ専用。製品版には含まれない）。
    /// メニュー「Tools/Potion Frontier 2D/仕上げ/④ 通しテストを実行」から始める。
    ///   タイトル → 設定 → はじめから（暗転なしで、そのまま調合机）→ トレイから水を取って傾けて注ぐ → 薬草を2回入れる
    ///   → 円を描いてかき混ぜる → 「全部詰める」→ 強火で薬草の「熱に弱い」を見つける → 発見が進んで新しい素材が机に届く
    ///   （手紙のお知らせ・トレイに並ぶ・手に取れる）→ 素材ノート・レシピ本を机の上で開く（火の音・机はそのまま）
    ///   → ポーズ → 設定 → タイトルへ（セーブ）→ つづきから（届いた素材・液体・ノートが戻る）
    /// を、マウスの代わりの「指」（Pointer2D）で実際に操作して確かめ、各場面を AutoTestShots2D/ に保存して再生を止める。
    /// 最初から最後まで、カメラが調合机から動かない（画面が切り替わらない）ことも確かめる。
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
        private readonly List<IngredientData> arrivals = new List<IngredientData>();
        private float lastArrivalTime = -1f;
        private Vector3 deskCamera;
        private float deskSize;
        private float maxCameraDrift;

        private GameFlow flow;
        private AtelierHUD hud;
        private AtelierView2D view;
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
            if (stock != null) stock.Arrived -= OnArrived;
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
            // カメラが調合机から動いていないか、毎フレーム見張る（1画面のゲーム）
            if (cam != null && deskSize > 0f)
            {
                float drift = Vector2.Distance(cam.transform.position, deskCamera) + Mathf.Abs(cam.orthographicSize - deskSize);
                maxCameraDrift = Mathf.Max(maxCameraDrift, drift);
            }
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
            Write($"2D 通しテスト（1画面版） {System.DateTime.Now:yyyy/MM/dd HH:mm:ss} ／ Unity {Application.unityVersion}");

            flow = GameFlow.Instance;
            hud = AtelierHUD.Instance;
            view = AtelierView2D.Current != null ? AtelierView2D.Current : FindFirstObjectByType<AtelierView2D>();
            stock = FindFirstObjectByType<IngredientInventory>();
            potions = FindFirstObjectByType<PotionInventory>();
            session = FindFirstObjectByType<BrewSession2D>();
            input = FindFirstObjectByType<BrewInput2D>();
            tray = FindFirstObjectByType<BrewTray2D>();
            flask = FindFirstObjectByType<Flask2D>();
            if (flow == null || hud == null || view == null || stock == null || session == null || input == null || flask == null || tray == null)
            {
                Fail($"シーンに足りない物がある（GameFlow {flow != null} / AtelierHUD {hud != null} / AtelierView2D {view != null} / IngredientInventory {stock != null} / BrewSession2D {session != null} / BrewInput2D {input != null} / Flask2D {flask != null} / BrewTray2D {tray != null}）");
                yield return Finish();
                yield break;
            }
            session.Bottled += OnBottled;
            stock.Arrived += OnArrived;

            // ---- 画面の比率（計算だけ）・描く順番 -----------------------------------
            AspectMath();
            int workbench = SortingLayer.GetLayerValueFromName("Workbench");
            foreach (string layer in new[] { "Flask", "Liquid", "Items", "Held", "FX" })
            {
                Check(SortingLayer.NameToID(layer) != 0 && SortingLayer.GetLayerValueFromName(layer) > workbench,
                      $"描く順番「{layer}」が、机の絵より手前（番号 {SortingLayer.NameToID(layer)}）");
            }

            // ---- 1画面の作り（シーンは1つ・部屋とお店の物がない）---------------------------
            int scenes = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
            Check(scenes == 1, $"ビルドに入るシーンは Atelier2D の1つだけ（{scenes} 個）");
            int roomThings = 0;
            foreach (MonoBehaviour m in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m == null) continue;
                string type = m.GetType().Name;
                if (type.Contains("Quest") || type.Contains("Customer") || type.Contains("Hotspot") || type.Contains("Shelf") || type == "PlayerProgress") roomThings++;
            }
            Check(roomThings == 0, $"お店・相談・部屋の仕組みがシーンに残っていない（{roomThings} 個）");

            // ---- タイトル ----------------------------------------------------------
            yield return Wait(2.5f);
            cam = Camera.main;
            if (cam != null)
            {
                deskCamera = cam.transform.position;
                deskSize = cam.orthographicSize;
            }
            Log($"画面 {Screen.width}×{Screen.height} ／ 絵の範囲 {Fmt(CameraAspect2D.ViewRect)} ／ カメラ {(cam != null ? $"{cam.name} {deskCamera} 大きさ {deskSize:0.00}" : "なし")}");
            Check(cam != null && Vector2.Distance(cam.transform.position, view.CameraPosition) < 0.01f && Mathf.Abs(cam.orthographicSize - view.CameraSize) < 0.01f,
                  "タイトルの間も、カメラは調合机に向いている（後ろに机とランプが見える）");
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
            CheckWords("タイトル");

            // ---- はじめから（暗転せず、そのまま調合机）---------------------------------
            hud.Title.ForceNewGame();
            float faderMax = 0f;
            float waitStart = Time.realtimeSinceStartup;
            while (flow.Mode != GameMode.Playing && Time.realtimeSinceStartup - waitStart < 8f)
            {
                if (hud.Fader != null) faderMax = Mathf.Max(faderMax, hud.Fader.Alpha);
                yield return null;
            }
            Check(flow.Mode == GameMode.Playing, "はじめから → 遊べるようになる");
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
            {
                if (hud.Fader != null) faderMax = Mathf.Max(faderMax, hud.Fader.Alpha);
                yield return null;
            }
            Check(faderMax < 0.05f, $"はじめるときに画面が暗転しない（暗さ いちばん {faderMax:0.00}）");
            Check(Drift() < 0.01f, "はじめから遊びはじめても、カメラは調合机のまま");
            BrewHUD2D brewHud = FindFirstObjectByType<BrewHUD2D>();
            Canvas brewCanvas = brewHud != null ? brewHud.GetComponent<Canvas>() : null;
            Check(brewCanvas != null && brewCanvas.enabled, "調合机の操作の画面（トレイ・見立て）が、すぐに出ている");
            if (brewHud != null && brewHud.LayoutVersion < 4) Warn("調合画面の配置が古いまま（メニュー「Step4：調合画面を新しくする」）");
            Pointer2D.BeginSimulation(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

            IngredientDatabase db = GameContext.Ingredients;
            IngredientData water = db != null ? db.Get("base_water") : null;
            IngredientData herb = db != null ? db.Get("main_herb") : null;
            IngredientData ash = db != null ? db.Get("main_volcano_ash") : null;
            IngredientData dew = db != null ? db.Get("base_night_dew") : null;
            Check(water != null && herb != null, "素材カタログに水と薬草がある");
            Check(db != null && db.Count >= 25, $"素材カタログに素材がそろっている（{(db != null ? db.Count : 0)} 種類）");
            IngredientData wax = db != null ? db.Get("main_bees_wax") : null;
            IngredientData fern = db != null ? db.Get("main_fiddlehead") : null;
            IngredientData fizz = db != null ? db.Get("main_fizz_powder") : null;
            Check(wax != null && wax.MeltTemp > 0f && wax.MeltTemp < 60f, "森の蜜蝋（ぬるめで溶ける）がある");
            Check(fern != null && fern.DissolveDirection == StirDirection.Clockwise, "ぜんまい草（時計回りでだけ溶ける）がある");
            Check(fizz != null && fizz.Volatility > 0f && dew != null && dew.Volatility > 0f, "泡雪の粉・月下の夜露（沸騰で力が飛ぶ）がある");
            Check(water != null && water.Icon != null && herb != null && herb.Icon != null && herb.PieceSprite != null,
                  "素材の絵（CustomAssets/Images/Ingredients の <ID>.png・<ID>_piece.png）が割り当てられている");
            Check(UIFactory.Skin != null && UIFactory.Skin.stampNew != null && UIFactory.Skin.discoveryPage != null && UIFactory.Skin.pageTurn != null,
                  "発見の絵（CustomAssets/Images/UI/Discovery。page_turn も）が UISkin に割り当てられている");
            Check(UIFactory.Skin != null && UIFactory.Skin.letter != null, "素材が届いたときの手紙の絵（letter.png）が UISkin に割り当てられている");
            Check(AudioManager.Lib != null && AudioManager.Lib.ingredientArrived != null, "素材が届いたときの音（se_letter.wav）が SoundLibrary に割り当てられている");
            // 差し替え用の絵・音（Assets/CustomAssets）：どの欄もファイルにつながっている（リンク切れ・空の欄がない）
            var broken = new List<string>();
            CheckLinks(UIFactory.Skin, "UISkin", broken, "crosshair");
            CheckLinks(AudioManager.Lib, "SoundLibrary", broken);
            foreach (BrewArt2D artSet in Resources.FindObjectsOfTypeAll<BrewArt2D>()) CheckLinks(artSet, "BrewArt2D", broken, "particleMaterial");
            int missingIcons = 0;
            if (db != null) foreach (IngredientData d in db.All) if (d != null && (d.Icon == null || (!d.IsPoured && d.PieceSprite == null))) missingIcons++;
            Check(broken.Count == 0, "差し替え用の絵・音が、すべてファイルにつながっている（UISkin・SoundLibrary・BrewArt2D）" + (broken.Count > 0 ? "：" + string.Join("、", broken) : ""));
            Check(missingIcons == 0, $"どの素材にも絵（アイコン・欠片）がつながっている（足りない素材 {missingIcons}）");

            // はじめの机：はじめから机にある素材だけ。ほかはまだ届いていない
            int starterKinds = stock.CountKinds(db);
            Check(starterKinds >= 8 && starterKinds < (db != null ? db.Count : 0), $"はじめは、机にある素材だけがトレイに並ぶ（{starterKinds} / {(db != null ? db.Count : 0)} 種類）");
            Check(ash != null && !stock.IsUnlocked(ash) && FindTrayItemAnyPage(ash) == null, "まだ届いていない素材（火山の灰）は、トレイにない");
            Check(water != null && float.IsInfinity(stock.Get(water)), "机にある素材は使い放題（お金・仕入れはない）");
            IngredientData coming = flow.NextArrival(out int remaining);
            Log($"[発見] {flow.DiscoveryPoints} ／ 次に届く素材：{(coming != null ? coming.DisplayName : "なし")}（あと {remaining}）");
            Check(coming != null && remaining > 0, "次に届く素材と、それまでの発見の数がわかる");

            // 夜の空気感（Step6）：部屋全体の光は深い夜の暗さ、ランプの火の音がずっと小さく鳴る
            float globalLight = -1f;
            foreach (UnityEngine.Rendering.Universal.Light2D l in FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>(FindObjectsSortMode.None))
                if (l.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Global) globalLight = l.intensity;
            Log($"[夜の空気感] 全体の光 {globalLight:0.00}");
            Check(globalLight >= 0f && globalLight <= 0.15f, "全体の光（Global Light 2D）が、深い夜の暗さになっている");
            NightSoundscape2D night = NightSoundscape2D.Current;
            Check(night != null && night.FirePlaying, "ランプの火が静かに燃える音（夜の環境音）が鳴っている");
            // 画面の中の物は、すべてカメラの中（見えない所に、クリックする物を置いていない）
            Rect viewRect = DeskRect();
            int outside = 0;
            foreach (Collider2D c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                Bounds b = c.bounds;
                if (!viewRect.Overlaps(new Rect(b.min.x, b.min.y, b.size.x, b.size.y))) outside++;
            }
            Check(outside == 0, $"クリックできる物は、すべて調合机の画面の中にある（外にある物 {outside}）");
            yield return Shot("desk_start");
            yield return Measure("調合机");
            CheckWords("調合机");
            BrewFeedback2D feedback = BrewFeedback2D.Current;
            Check(feedback != null, "調合机に手ごたえ（見立てカード・温度計）がある");

            // ---- 調合：水を注ぐ ----------------------------------------------------
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
            Log($"[注ぐ] {poured:0}ml 注いだ（フラスコ {session.Flask.VolumeMl:0}ml）");
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
                Log($"[見立て] 混ぜる前：回復 今 {now:0.0} → 溶かしきると {soon:0.0} ／ {feedback.Status.Replace('\n', ' ')}");
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
            if (feedback != null)
            {
                string status = feedback.Status.Replace('\n', ' ');
                Log($"[見立て] 混ぜたあと：回復 {feedback.CurrentValue(PotionAttribute.Healing):0.0} ／ {status}");
                Check(ringSeen, "かき混ぜている間、カーソルのそばに溶け具合の輪が出る");
                if (state.dissolvedFraction >= 0.99f)
                    Check(status.Contains("新しい素材まで") || status.Contains("届く") || status.Contains("そろった"), "溶けきると、見立てカードに「新しい素材まで：発見あと N」が出る");
            }

            // ---- 瓶に詰める（「全部詰める」ボタン）------------------------------------
            lastBottled = null;
            Button bottleAll = FindButton("BottleAllButton");
            Check(bottleAll != null, "「全部詰める」ボタンがある");
            if (bottleAll != null) bottleAll.onClick.Invoke();
            else session.BottleAll();
            yield return Wait(0.15f);
            yield return Shot("new_page_turn");
            if (feedback != null)
            {
                Check(feedback.PageTurnCount > 0, "新しい液体の結果カードが、図鑑のページをめくるように現れる");
                Check(feedback.NewHeaderVisible, "結果カードの上に「新発見！」の見出しが出る");
            }
            yield return Wait(0.95f);
            yield return Shot("bottled");
            LiquidRecord bottled = lastBottled;
            FlaskEffects2D fx = FindFirstObjectByType<FlaskEffects2D>();
            if (flask != null)
            {
                Log($"[発見の光] 強さ {flask.BloomLevel:0.00}");
                Check(flask.Blooming && flask.BloomLevel > 0.3f, "新発見のとき、温かい光がフラスコからフワッと漏れる");
            }
            if (feedback != null)
            {
                Check(feedback.ResultVisible, "瓶詰めの結果カードが出る");
                Check(feedback.NewStampVisible, "新しい液体の結果カードに「新発見」のスタンプが押される");
                yield return Wait(0.8f);
                Log($"[結果カード] ★ {feedback.ResultStarsShown} 個（記録は ★{(bottled != null ? bottled.stars : 0)}）");
                Check(bottled == null || feedback.ResultStarsShown == bottled.stars, "結果カードの ★ が1つずつ全部付く");
                feedback.PreviewReaction("テストの反応");
                yield return Wait(0.15f);
                Log($"[反応の演出] スロー中の時間の速さ {Time.timeScale:0.00}");
                yield return Shot("reaction_moment");
                yield return Wait(1.2f);
                Check(Mathf.Approximately(Time.timeScale, 1f), "反応のスローのあと、時間の速さが元に戻る");
                Check(fx == null || fx.SparkleCount == 0, "新発見・反応のときに、きらきらの粒は出ない（夜の空気感）");
            }
            Check(bottled != null, "ボタンで瓶に詰められる");
            PotionStack stack = bottled != null ? potions.Find(bottled.id) : null;
            Log($"[瓶詰め] {Describe(bottled)} ／ 瓶 {(stack != null ? stack.DescribeAmount() : "なし")} ／ フラスコの残り {session.Flask.VolumeMl:0}ml");
            Check(stack != null && stack.FullBottles >= 1, "満タンの瓶がトレイに入る");
            Log($"[発見] 瓶詰めのあと {flow.DiscoveryPoints} ／ 届いた素材 {arrivals.Count} 種類");

            // ---- 素材ノート：熱に弱い薬草を、強火で温めて「熱で弱まる」を見つける（早送り）----------
            Check(DiscoveryJournal.Knows(herb, IngredientFact.Used) && DiscoveryJournal.Knows(water, IngredientFact.Used), "使った素材が素材ノートに「使った」と記録される");
            bool knewWeak = DiscoveryJournal.Knows(herb, IngredientFact.Weakens);
            session.ResetFlask();
            session.PourIngredient(water, 100f);
            session.AddSolid(herb);
            session.SetHeat(1f);
            session.FastForward = true;
            yield return WaitUntil(() => DiscoveryJournal.Knows(herb, IngredientFact.Weakens), 15f, "強火で薬草が熱で弱まり、素材ノートに書き足される");
            yield return Wait(0.5f);
            Log($"[素材ノート] 薬草：{DiscoveryJournal.KnownCount(herb)} / {DiscoveryJournal.TotalCount(herb)} の性質（{session.Flask.Temperature:0}℃）／ はじめから知っていた: {knewWeak}");
            if (!knewWeak && feedback != null) Check(feedback.SlipVisible, "「素材ノートに書き足した」お知らせが出る");
            yield return Shot("fact_slip");
            yield return Wait(0.8f);
            yield return Shot("heat");
            if (fx != null)
            {
                Log($"[夜の環境音] お湯のコト {fx.KnockCount} 回 ／ ガラスのちん {fx.GlassCount} 回（{session.Flask.Temperature:0}℃）");
                Check(fx.KnockCount > 0, "お湯が温まると、コトコトと音がする");
            }
            session.SetHeat(0f);
            session.FastForward = false;
            session.ResetFlask();
            yield return Wait(0.3f);

            // ---- 新しい素材が、調合机に届く（画面は切り替わらない）---------------------------
            yield return WaitUntil(() => ash != null && stock.IsUnlocked(ash), 4f, "発見が進んで、火山の灰が調合机に届く");
            Log($"[届いた] 発見 {flow.DiscoveryPoints} ／ {string.Join("・", arrivals.ConvertAll(a => a != null ? a.DisplayName : "?"))}");
            Check(arrivals.Contains(ash), "火山の灰が届いた（素材が届いたことが、お知らせの仕組みに伝わる）");
            ArrivalNoticeUI notice = hud.Arrival;
            Check(notice != null && notice.ShownCount >= 1, $"右下に小さな手紙のお知らせが出る（{(notice != null ? notice.ShownCount : 0)} 回）");
            if (notice != null && notice.Visible) Log($"[お知らせ] {notice.Text.Replace('\n', ' ')}（届いてから {Time.realtimeSinceStartup - lastArrivalTime:0.0} 秒）");
            Check(!hud.IsModalOpen && flow.Mode == GameMode.Playing && !flow.IsPaused, "お知らせは小さく出るだけで、手を止めない（ウィンドウ・ポーズにならない）");
            if (notice != null && !notice.Visible && ash != null)
            {
                // お知らせが消えたあとなら、写真用にもう一度だけ出す（届いた記録は変えない）
                notice.Show(ash);
                yield return Wait(0.6f);
            }
            yield return Shot("arrival_notice");
            Check(Drift() < 0.01f, "素材が届いても、カメラは調合机のまま");
            yield return Wait(0.3f);
            TrayItem2D ashItem = FindTrayItemAnyPage(ash);
            Check(ashItem != null, "届いた素材（火山の灰）が、自動でトレイに並ぶ");
            yield return PickFromTray(ash);
            Check(input.Held != null && input.Held.Ingredient == ash, "届いた素材を、そのままトレイから手に取れる");
            yield return Shot("arrived_in_hand");
            input.ReturnHeld();
            yield return Wait(0.3f);
            Check(input.Held == null, "手に取った素材をトレイに戻せる");

            // 新しい反応を見つけたときのページ（演出だけ。記録は変えない）
            AlchemyManager am = session.Alchemy;
            ReactionData sampleReaction = am != null && am.Reactions.Count > 2 ? am.Reactions[2] : (am != null && am.Reactions.Count > 0 ? am.Reactions[0] : null);
            Check(am != null && am.Reactions.Count >= 6, $"シーンの調合に反応が登録されている（{(am != null ? am.Reactions.Count : 0)} 個）");
            if (feedback != null && sampleReaction != null)
            {
                feedback.PreviewDiscoveryPage(sampleReaction);
                yield return Wait(1.2f);
                Check(feedback.PageVisible, $"新しい反応のページが出る（{sampleReaction.DisplayName}）");
                yield return Shot("discovery_page");
                yield return Wait(4.6f);
            }

            // ---- 素材ノート：机の上に広げる（火の音・机はそのまま）---------------------------
            float fireBefore = night != null ? night.FireVolume : 0f;
            hud.OpenNotebook();
            yield return Wait(0.15f);
            yield return Shot("notebook_opening");
            yield return Wait(0.6f);
            Check(hud.IsNotebookOpen && hud.Notebook.FullyOpen, "素材ノートが、調合机の上に広がる");
            Check(Drift() < 0.01f && flow.Mode == GameMode.Playing, "素材ノートを開いても、画面は切り替わらない（カメラ・モードがそのまま）");
            Check(brewCanvas != null && brewCanvas.enabled, "素材ノートの後ろに、調合机（トレイ・見立て）が見えたまま");
            Check(night != null && night.FirePlaying && night.FireVolume >= fireBefore * 0.9f, $"素材ノートを開いても、ランプの火の音が途切れない（{fireBefore:0.00} → {(night != null ? night.FireVolume : 0f):0.00}）");
            Check(flask != null && flask.isActiveAndEnabled, "素材ノートの後ろで、フラスコ・火のゆらぎが動いたまま");
            yield return Shot("notebook_on_desk");
            CheckWords("素材ノート");
            if (hud.Notebook != null)
            {
                Check(hud.Notebook.Count >= 25, $"素材ノートに全部の素材が並ぶ（{hud.Notebook.Count}）");
                int turned = hud.Notebook.PageTurnCount;
                hud.Notebook.TurnPage(1);
                yield return Wait(0.25f);
                yield return Shot("notebook_page_turn");
                Check(hud.Notebook.PageTurnCount > turned, "素材ノートのページを送ると、紙がめくれる");
                yield return Wait(0.6f);
                hud.Notebook.SelectById("main_herb");
                yield return Wait(0.5f);
                Log("[素材ノート] 薬草のページ：" + hud.Notebook.FactsText.Replace('\n', ' '));
                Check(hud.Notebook.FactsText.Contains("熱に弱い"), "素材ノートの薬草のページに「熱に弱い」が書いてある");
                yield return Shot("notebook_herb");
                hud.Notebook.SelectById("main_volcano_ash");
                yield return Wait(0.4f);
                yield return Shot("notebook_arrived");
                hud.Notebook.SelectById("base_night_dew");
                yield return Wait(0.5f);
                string hint = hud.Notebook.DescriptionText.Replace('\n', ' ');
                Log("[素材ノート] まだ届いていない素材：" + hint);
                Check(hint.Contains("発見をあと") && hint.Contains("調合机に届く"), "まだ届いていない素材のページに「発見をあと N で、調合机に届く」と書いてある");
                yield return Shot("notebook_locked");
            }
            hud.CloseNotebook();
            yield return Wait(0.3f);
            Check(!hud.IsNotebookOpen, "素材ノートを閉じられる");

            // ---- レシピ本：机の上に広げる --------------------------------------------
            hud.OpenRecipeBook();
            yield return Wait(0.7f);
            Check(hud.IsRecipeBookOpen && hud.RecipeBook.FullyOpen, "レシピ本が、調合机の上に広がる");
            Check(Drift() < 0.01f && night != null && night.FirePlaying, "レシピ本を開いても、画面は切り替わらず、火の音も鳴ったまま");
            if (bottled != null) Check(hud.RecipeBook.SelectById(bottled.id), "作った液体がレシピ本に載っている");
            yield return Wait(0.3f);
            yield return Shot("recipe_book_on_desk");
            Log("[レシピ本] " + hud.RecipeBook.DetailSummary);
            CheckWords("レシピ本");
            hud.CloseRecipeBook();
            yield return Wait(0.3f);

            // ---- ポーズ・設定 ------------------------------------------------------
            hud.OpenPause();
            yield return Wait(0.5f);
            Check(flow.IsPaused && Time.timeScale == 0f, "Esc のポーズで時間が止まる");
            yield return Shot("pause");
            CheckWords("ひとやすみ");
            hud.ClosePause();
            yield return Wait(0.2f);
            Check(!flow.IsPaused && Time.timeScale == 1f, "ポーズを閉じると時間が動く");
            hud.OpenSettings();
            yield return Wait(0.5f);
            yield return Shot("settings");
            hud.CloseSettings();
            yield return Wait(0.3f);

            // ---- セーブ → タイトル → つづきから ----------------------------------------
            int liquids = session.Alchemy != null ? session.Alchemy.Liquids.Count : 0;
            float potionMl = TotalMl();
            int onDesk = stock.CountKinds(db);
            int knownFacts = DiscoveryJournal.KnownCount(herb);
            int points = flow.DiscoveryPoints;
            flow.ReturnToTitle();
            yield return WaitUntil(() => flow.Mode == GameMode.Title && hud.Title.IsOpen, 8f, "タイトルへ戻る");
            yield return Wait(1.2f);
            Check(SaveSystem.Exists, "タイトルへ戻るとセーブされる");
            yield return Shot("title_with_save");
            int noticesBefore = notice != null ? notice.ShownCount : 0;
            flow.ContinueGame();
            yield return WaitUntil(() => flow.Mode == GameMode.Playing, 8f, "つづきから → 遊べるようになる");
            yield return Wait(1.5f);
            yield return Shot("continued");
            int liquidsAfter = session.Alchemy != null ? session.Alchemy.Liquids.Count : 0;
            Log($"[つづきから] 液体 {liquids} → {liquidsAfter} 種類 ／ 瓶 {potionMl:0} → {TotalMl():0}ml ／ 机の素材 {onDesk} → {stock.CountKinds(db)} 種類 ／ 発見 {points} → {flow.DiscoveryPoints}");
            Check(liquidsAfter == liquids, "レシピ本の液体がセーブから戻る");
            Check(Mathf.Abs(TotalMl() - potionMl) < 0.6f, "瓶がセーブから戻る");
            Check(stock.CountKinds(db) == onDesk && ash != null && stock.IsUnlocked(ash), "届いた素材（火山の灰も）がセーブから戻る");
            Check(FindTrayItemAnyPage(ash) != null, "つづきからでも、届いた素材がトレイに並んでいる");
            Check(notice == null || notice.ShownCount == noticesBefore, "つづきからでは、届いた素材のお知らせを出し直さない");
            Check(DiscoveryJournal.KnownCount(herb) == knownFacts && DiscoveryJournal.Knows(herb, IngredientFact.Weakens) && flow.DiscoveryPoints == points,
                  "素材ノート（見つけた性質・発見の数）がセーブから戻る");
            CheckWords("つづきから");
            Log($"[1画面] テスト中のカメラのずれ いちばん {maxCameraDrift:0.000}");
            Check(maxCameraDrift < 0.01f, "最初から最後まで、カメラが調合机から一度も動かなかった（画面の切り替えがない）");

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
            // 1画面版：お店・相談・お金の言葉は出さない（3D 版の操作の説明も）
            string[] banned = { "依頼", "納品", "クエスト", "引き受け", "相談", "お客", "所持金", "取引", "仕入", "注文", "開拓", "部屋に戻る",
                                "WASD", "E キー", "Eキー", "Tab キー", "Tabキー" };
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
            Log($"[言葉] {where}：使わない言葉（お店・相談・お金・3D 版の操作）は {found} 件");
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

        /// <summary>絵・音・フォントの欄が空でないか（ファイルを消した・名前を変えたときに空になる）。入れ子の欄（容器の絵など）も見る</summary>
        private static void CheckLinks(Object asset, string label, List<string> broken, params string[] skip)
        {
            if (asset == null)
            {
                broken.Add(label + " がない");
                return;
            }
            CheckLinksIn(asset, label, broken, skip, 0);
        }

        private static void CheckLinksIn(object target, string label, List<string> broken, string[] skip, int depth)
        {
            foreach (System.Reflection.FieldInfo f in target.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (System.Array.IndexOf(skip, f.Name) >= 0) continue;
                object v = f.GetValue(target);
                if (f.FieldType == typeof(Sprite) || f.FieldType == typeof(AudioClip) || f.FieldType == typeof(Font))
                {
                    if (v as Object == null) broken.Add($"{label}.{f.Name}");
                }
                else if (f.FieldType == typeof(AudioClip[]))
                {
                    var list = v as AudioClip[];
                    if (list == null || list.Length == 0) broken.Add($"{label}.{f.Name}（空）");
                    else for (int i = 0; i < list.Length; i++) if (list[i] == null) broken.Add($"{label}.{f.Name}[{i}]");
                }
                else if (depth == 0 && v != null && f.FieldType.IsClass && f.FieldType.IsSerializable && !typeof(Object).IsAssignableFrom(f.FieldType))
                {
                    CheckLinksIn(v, $"{label}.{f.Name}", broken, skip, depth + 1);
                }
            }
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

        private void OnArrived(IngredientData data)
        {
            if (data != null && !arrivals.Contains(data)) arrivals.Add(data);
            lastArrivalTime = Time.realtimeSinceStartup;
        }

        /// <summary>カメラが、はじめの調合机の位置・大きさからどれだけずれたか（1画面なので、いつも 0）</summary>
        private float Drift()
        {
            if (cam == null) return 0f;
            return Vector2.Distance(cam.transform.position, deskCamera) + Mathf.Abs(cam.orthographicSize - deskSize);
        }

        /// <summary>カメラに映っている、調合机の範囲（ワールド座標）</summary>
        private Rect DeskRect()
        {
            if (cam == null) return new Rect();
            Vector3 a = cam.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
            Vector3 b = cam.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>トレイのページを順に見て、素材を探す（見つかったページのまま）</summary>
        private TrayItem2D FindTrayItemAnyPage(IngredientData data)
        {
            if (data == null || tray == null) return null;
            tray.Rebuild();
            for (int page = 0; page < tray.PageCount; page++)
            {
                TrayItem2D item = FindTrayItem(data);
                if (item != null) return item;
                tray.ChangePage(1);
                tray.Rebuild();
            }
            return null;
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
