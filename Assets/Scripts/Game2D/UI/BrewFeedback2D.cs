using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合台の手ごたえ。BrewHUD の Prefab のルートに付ける（なければ BrewHUD2D が自分に付ける）。
    ///   見える化：温度計／かき混ぜている間の溶け具合の輪／注いだ量・かき混ぜた回数（作業中だけ、カーソルのそば）
    ///   演出：反応の瞬間（光・スロー・音）／瓶詰めの結果カード（★ が1つずつ「ちりん」・新発見）／
    ///         沸騰・焦げ・溶けきった・素材が弱まった/目覚めたの短いお知らせ／素材ノートに書き足したお知らせ・新しい反応のページ
    /// （Step11 で「見立てカード」と、値が変わったときの「回復 +3」はなくした。フラスコの色・湯気・音で感じ取る）
    /// 見た目は「置き場所」「絵」の欄で変えられる（空の絵はコードで描いた仮の絵）。音は SoundLibrary の「調合の手ごたえ」。
    /// </summary>
    [DisallowMultipleComponent]
    public class BrewFeedback2D : MonoBehaviour
    {
        [Header("置き場所（1920×1080 の画面で、左下が 0,0）")]
        [SerializeField, Tooltip("温度計の左下（球の下）")] private Vector2 thermometerPosition = new Vector2(770f, 312f);
        [SerializeField] private Vector2 thermometerSize = new Vector2(56f, 400f);
        [SerializeField, Tooltip("瓶詰めの結果カードの真ん中")] private Vector2 resultCenter = new Vector2(961f, 800f);

        [Header("絵（空ならコードで描いた仮の絵）")]
        [SerializeField, Tooltip("温度計の管（9スライス推奨）")] private Sprite thermometerTube;
        [SerializeField, Tooltip("溶け具合の輪（白い輪。ぐるっと塗っていく）")] private Sprite stirRing;

        [Header("演出")]
        [SerializeField, Tooltip("反応が起きた瞬間、一瞬だけ時間をゆっくりにする")] private bool reactionSlowMotion = true;
        [SerializeField, Range(0.1f, 1f)] private float slowMotionScale = 0.35f;
        [SerializeField, Min(0f), Tooltip("ゆっくりにする時間（実際の秒）")] private float slowMotionSeconds = 0.45f;
        [SerializeField, Tooltip("瓶詰めの結果カードを出す（出すときは、下の文字のお知らせは出さない）")] private bool showBottleResult = true;
        [SerializeField, Tooltip("注いでいる間の「+35ml」・かき混ぜている間の回数を、カーソルのそばに出す（オフにすると、作業中の数字も出ない）")] private bool showWorkNumbers = true;
        [SerializeField, Range(0f, 2f), Tooltip("手ごたえの音の大きさ")] private float soundVolume = 1f;

        private static readonly float[] StarPitch = { 1f, 1.122f, 1.26f, 1.498f, 1.682f };

        private BrewSession2D session;
        private BrewInput2D input;
        private Flask2D flask;
        private HeldItem2D held;
        private Canvas canvas;

        private BrewThermoUI2D thermo;
        private BrewPopupsUI2D popups;

        private readonly List<FlaskSimulation.IngredientState> states = new List<FlaskSimulation.IngredientState>();
        private readonly List<BrewThermoUI2D.Mark> marks = new List<BrewThermoUI2D.Mark>();
        // このフラスコで、もう知らせたこと（素材と性質の組）。フラスコが空になったら忘れる
        private readonly HashSet<long> noticed = new HashSet<long>();
        /// <summary>「溶ける温度に届かず溶けない」のお知らせ用の印（性質の印と重ならない番号）</summary>
        private const int BlockedNotice = 1 << 20;

        // カーソルのそばの数字・温度計の火加減の文字は、値が変わったときだけ作り直す（毎フレーム文字を作らない）
        private int followMl = int.MinValue;
        private string followPour = "";
        private int followTenths = int.MinValue;
        private bool followClockwise;
        private string followStir = "";
        private int ringPercent = int.MinValue;
        private bool ringClockwise;
        private string ringLabel = "";
        private string heatBase;
        private bool heatBoiling;
        private string heatLabel = "";

        private bool wasBoiling;
        private bool wasScorching;
        private float lastDissolved = 1f;
        private float pollTimer;
        private bool slowing;
        private Coroutine slowRoutine;

        public static BrewFeedback2D Current { get; private set; }

        /// <summary>瓶詰めの結果カードを出す（BrewHUD2D は下の文字のお知らせを出さない）</summary>
        public bool ShowsBottleResult => showBottleResult;
        public bool ResultVisible => popups != null && popups.ResultVisible;
        public int ResultStarsShown => popups != null ? popups.ResultStarsShown : 0;
        public bool RingVisible => popups != null && popups.RingVisible;
        /// <summary>「素材ノートに書き足した」お知らせが出ている</summary>
        public bool SlipVisible => popups != null && popups.SlipVisible;
        /// <summary>新しい反応のページが出ている</summary>
        public bool PageVisible => popups != null && popups.PageVisible;
        /// <summary>結果カードに「新発見」のスタンプが押されている</summary>
        public bool NewStampVisible => popups != null && popups.NewStampVisible;
        public bool NewHeaderVisible => popups != null && popups.NewHeaderVisible;
        public int PageTurnCount => popups != null ? popups.PageTurnCount : 0;
        public int ReactionCount { get; private set; }

        private void Awake()
        {
            Current = this;
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            input = FindFirstObjectByType<BrewInput2D>();
            flask = FindFirstObjectByType<Flask2D>();
            held = FindFirstObjectByType<HeldItem2D>();
            canvas = GetComponent<Canvas>();
            ViewportFit2D fit = GetComponent<ViewportFit2D>();
            RectTransform parent = fit != null ? fit.SafeRoot : (RectTransform)transform;

            // 奥：温度計（Prefab の文字より奥）／手前：ポップ・輪・結果カード・光
            RectTransform back = UIFactory.CreateRect("FeedbackBack", parent);
            UIFactory.Stretch(back);
            back.SetAsFirstSibling();
            RectTransform front = UIFactory.CreateRect("FeedbackFront", parent);
            UIFactory.Stretch(front);
            front.SetAsLastSibling();
            thermo = new BrewThermoUI2D(back, thermometerPosition, thermometerSize, thermometerTube);
            popups = new BrewPopupsUI2D(front, resultCenter, stirRing);
            popups.StarShown = OnStarShown;
            popups.PageTurnStarted = OnPageTurn;
            foreach (UnityEngine.UI.Graphic g in back.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;
            foreach (UnityEngine.UI.Graphic g in front.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;

            if (session != null)
            {
                session.Bottled += OnBottled;
                session.Reacted += OnReacted;
            }
            DiscoveryJournal.FactDiscovered += OnFactDiscovered;
            DiscoveryJournal.ReactionDiscovered += OnReactionDiscovered;
            BrewHUD2D hud = GetComponent<BrewHUD2D>();
            if (hud != null && hud.LayoutVersion < BrewHUD2D.MinimalLayout)
            {
                Debug.LogWarning("[調合画面] BrewHUD の Prefab の配置が古いままです（工程・ボタン・ページ送りが残っています）。Git の履歴から Step11 の BrewHUD.prefab を戻してください。", this);
            }
            // コードで作る音は、最初に鳴らすときに引っかからないよう、先に作っておく
            _ = SfxSynth.Chime;
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Bottled -= OnBottled;
                session.Reacted -= OnReacted;
            }
            DiscoveryJournal.FactDiscovered -= OnFactDiscovered;
            DiscoveryJournal.ReactionDiscovered -= OnReactionDiscovered;
            if (Current == this) Current = null;
            RestoreTime();
        }

        private void OnDisable()
        {
            RestoreTime();
        }

        // ==================================================================
        // 毎フレーム
        // ==================================================================

        private void Update()
        {
            if (session == null || session.Flask == null || thermo == null) return;
            bool visible = canvas == null || canvas.enabled;
            if (!visible)
            {
                popups.SetFollow("", Vector2.zero);
                popups.SetRing(false, Vector2.zero, 0f, "");
                return;
            }
            float dt = Time.unscaledDeltaTime;
            FlaskSimulation sim = session.Flask;

            pollTimer -= dt;
            if (pollTimer <= 0f)
            {
                pollTimer = 0.1f;
                PollEvents(sim);
            }

            BrewRules rules = sim.Settings != null ? sim.Settings.Rules : null;
            marks.Clear();
            if (rules != null) marks.Add(new BrewThermoUI2D.Mark { temperature = rules.boilingPoint, color = new Color(0.65f, 0.85f, 1f, 1f) });
            foreach (FlaskSimulation.IngredientState s in states)
            {
                if (s.ingredient == null || marks.Count >= 6) continue;
                // 素材ノートで見つけた温度に印（赤：熱で弱まる／金：目覚める／琥珀：溶ける温度）
                if (DiscoveryJournal.Knows(s.ingredient, IngredientFact.Weakens)) marks.Add(new BrewThermoUI2D.Mark { temperature = s.ingredient.HeatLimit, color = new Color(1f, 0.4f, 0.3f, 1f) });
                if (DiscoveryJournal.Knows(s.ingredient, IngredientFact.Wakes) && marks.Count < 6) marks.Add(new BrewThermoUI2D.Mark { temperature = s.ingredient.ActivationTemp, color = new Color(1f, 0.82f, 0.3f, 1f) });
                if (DiscoveryJournal.Knows(s.ingredient, IngredientFact.Melts) && marks.Count < 6) marks.Add(new BrewThermoUI2D.Mark { temperature = s.ingredient.MeltTemp, color = new Color(1f, 0.62f, 0.18f, 1f) });
            }
            string heat = ProcessStep.HeatLabel(sim.HeatLevel);
            if (!ReferenceEquals(heat, heatBase) || sim.IsBoiling != heatBoiling)
            {
                heatBase = heat;
                heatBoiling = sim.IsBoiling;
                heatLabel = heatBoiling ? heat + "・沸騰" : heat;
            }
            thermo.Apply(sim.Temperature, rules != null ? rules.maxHeatTemperature : 130f, heatLabel, sim.IsScorching, marks);

            UpdateCursorHelpers(sim);
            thermo.Tick(dt);
            popups.Tick(dt);
        }

        /// <summary>カーソルのそばの数字と、溶け具合の輪</summary>
        private void UpdateCursorHelpers(FlaskSimulation sim)
        {
            if (input == null)
            {
                popups.SetFollow("", Vector2.zero);
                popups.SetRing(false, Vector2.zero, 0f, "");
                return;
            }
            Vector2 pointer = input.PointerScreen;
            bool solids = sim.SolidUnits > 0.001f && session.HasLiquid;
            string follow = "";
            Vector2 at = pointer + new Vector2(34f, 18f);
            bool ring = false;
            // 火加減は温度計の上に出ているので、カーソルのそばには出さない
            switch (input.Current)
            {
                case BrewInput2D.Activity.Pouring:
                    if (showWorkNumbers)
                    {
                        int ml = Mathf.RoundToInt(input.PouredThisTime);
                        if (ml != followMl)
                        {
                            followMl = ml;
                            followPour = "+" + ml + "ml";
                        }
                        follow = followPour;
                    }
                    if (held != null && held.HasItem) at = WorldToScreen(held.MouthWorld) + new Vector2(30f, 6f);
                    break;
                case BrewInput2D.Activity.Stirring:
                    if (solids) ring = true;
                    else if (showWorkNumbers)
                    {
                        int tenths = Mathf.RoundToInt(input.StirTurnsThisTime * 10f);
                        if (tenths != followTenths || input.StirClockwise != followClockwise)
                        {
                            followTenths = tenths;
                            followClockwise = input.StirClockwise;
                            followStir = (followClockwise ? "時計回り " : "反時計回り ") + (tenths / 10f).ToString("0.0") + "回";
                        }
                        follow = followStir;
                    }
                    break;
            }
            popups.SetFollow(follow, at);
            float df = sim.DissolvedFraction;
            int percent = Mathf.FloorToInt(df * 100f);
            if (percent != ringPercent || input.StirClockwise != ringClockwise)
            {
                ringPercent = percent;
                ringClockwise = input.StirClockwise;
                ringLabel = "溶け " + percent + "%　" + (ringClockwise ? "時計回り" : "反時計回り");
            }
            popups.SetRing(ring, pointer, df, ringLabel);
        }

        /// <summary>沸騰した・焦げはじめた・溶けきった・素材が弱まった／目覚めた、を見つけて知らせる</summary>
        private void PollEvents(FlaskSimulation sim)
        {
            bool wet = session.HasLiquid;
            Vector2 mouth = WorldToScreen(flask != null ? flask.MouthWorld : Vector3.zero) + new Vector2(0f, 30f);
            SoundLibrary lib = AudioManager.Lib;

            bool boiling = sim.IsBoiling;
            if (boiling && !wasBoiling) popups.ShowPop("ぐつぐつ… 沸騰した", mouth, new Color(0.7f, 0.88f, 1f, 1f));
            wasBoiling = boiling;

            bool scorching = sim.IsScorching;
            if (scorching && !wasScorching)
            {
                popups.ShowPop("焦げはじめた！ 火を弱めよう", mouth, new Color(1f, 0.45f, 0.35f, 1f), true);
                Play(lib != null ? lib.softWarning : null, SfxSynth.SoftWarning, 0.7f, 1f);
            }
            wasScorching = scorching;

            bool solids = sim.SolidUnits > 0.001f;
            float df = sim.DissolvedFraction;
            if (solids && wet && lastDissolved < 0.99f && df >= 0.995f)
            {
                popups.ShowPop("溶けきった！", SurfaceScreen() + new Vector2(0f, 40f), new Color(1f, 0.88f, 0.45f, 1f), true);
                popups.RingCompleted();
                Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.4f, 1.5f);
            }
            lastDissolved = solids ? df : 1f;

            // 素材の性質が「起きた」ことを知らせる（このフラスコで1回ずつ）。
            // まだ素材ノートにない性質は、BrewSession2D が書き足して「書き足した」お知らせが出るので、ここでは知っている性質だけ
            sim.GetIngredientStates(states);
            if (sim.IsEmpty) noticed.Clear();
            Vector2 at = mouth + new Vector2(0f, 46f);
            foreach (FlaskSimulation.IngredientState s in states)
            {
                IngredientData d = s.ingredient;
                if (d == null) continue;
                string n = d.DisplayName;
                if (s.heatDamaged && s.quality < 0.97f && Once(d, IngredientFact.Weakens))
                {
                    popups.ShowPop($"{n}が熱で弱まりはじめた（{d.HeatLimit:0}℃ をこえた）", at, new Color(1f, 0.55f, 0.45f, 1f), true);
                    Play(lib != null ? lib.softWarning : null, SfxSynth.SoftWarning, 0.6f, 1.05f);
                }
                if (s.activation > 0.05f && Once(d, IngredientFact.Wakes))
                {
                    popups.ShowPop($"{n}の力が目覚めはじめた！（{d.ActivationTemp:0}℃）", at, new Color(1f, 0.85f, 0.4f, 1f), true);
                    Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.38f, 1.26f);
                    if (flask != null) flask.WarmBloom(0.25f);
                }
                if (s.broken && s.quality < 0.9f && Once(d, IngredientFact.Fragile))
                {
                    popups.ShowPop($"{n}が崩れはじめた！（混ぜすぎ）", at, new Color(1f, 0.6f, 0.5f, 1f), true);
                    Play(lib != null ? lib.softWarning : null, SfxSynth.SoftWarning, 0.6f, 1.1f);
                }
                if (s.evaporated && Once(d, IngredientFact.Volatile))
                {
                    popups.ShowPop($"{n}の香りが飛んでいく…", at, new Color(0.85f, 0.75f, 1f, 1f), true);
                }
                if (s.cooled && Once(d, IngredientFact.Cools))
                {
                    popups.ShowPop("ひやっ… フラスコが冷えた", at, new Color(0.7f, 0.9f, 1f, 1f), true);
                }
                if (s.spunWrong && s.undissolved > 0.01f && Once(d, IngredientFact.OneWay))
                {
                    popups.ShowPop($"{n}は、この向きでは溶けないようだ", at, new Color(1f, 0.8f, 0.6f, 1f), true);
                }
                // 溶ける温度に届かず、かき混ぜても溶けない（知っていてもいなくても、このフラスコで1回）
                if (s.meltBlocked && s.undissolved > 0.01f && noticed.Add(NoticeKey(d, BlockedNotice)))
                {
                    string text = DiscoveryJournal.Knows(d, IngredientFact.Melts)
                        ? $"{n}は {d.MeltTemp:0}℃ にならないと溶けない"
                        : $"{n}が、かき混ぜても溶けない…（温めると？）";
                    popups.ShowPop(text, at, new Color(1f, 0.8f, 0.55f, 1f), true);
                }
            }
        }

        /// <summary>知っている性質で、このフラスコではまだ知らせていなければ true（そして「知らせた」にする）</summary>
        private bool Once(IngredientData d, IngredientFact fact)
        {
            if (!DiscoveryJournal.Knows(d, fact)) return false;
            return noticed.Add(NoticeKey(d, (int)fact));
        }

        /// <summary>素材と性質の組を、文字を作らずに1つの数にする</summary>
        private static long NoticeKey(IngredientData d, int code)
        {
            return ((long)d.GetInstanceID() << 32) | (uint)code;
        }

        /// <summary>素材の性質を初めて見つけた：素材ノートに書き足したお知らせ（スタンプがぽんっ）</summary>
        private void OnFactDiscovered(IngredientData d, IngredientFact fact)
        {
            if (d == null) return;
            noticed.Add(NoticeKey(d, (int)fact));
            if (popups == null) return;
            popups.ShowSlip(IngredientIcons.Get(d), "素材ノートに書き足した：" + d.DisplayName, DiscoveryJournal.Describe(d, fact));
            SoundLibrary lib = AudioManager.Lib;
            StartCoroutine(PlayLater(0.36f, lib != null ? lib.starChime : null, SfxSynth.Chime, 0.32f, 1.122f));
        }

        /// <summary>新しい反応を初めて見つけた：反応のページ</summary>
        private void OnReactionDiscovered(ReactionData r)
        {
            if (r == null || popups == null) return;
            var icons = new List<Sprite>();
            foreach (IngredientData x in r.RequiredIngredients) if (x != null) icons.Add(IngredientIcons.Get(x));
            popups.ShowDiscoveryPage(r.DisplayName, r.Description, icons);
            SoundLibrary lib = AudioManager.Lib;
            StartCoroutine(PlayLater(0.6f, lib != null ? lib.discovery : null, SfxSynth.Discovery, 0.55f, 1f));
        }

        /// <summary>素材ノートのお知らせを、試しに出す（確認用・自動テスト用。記録は変わらない）</summary>
        public void PreviewFactSlip(IngredientData d, IngredientFact fact)
        {
            if (d == null || popups == null) return;
            popups.ShowSlip(IngredientIcons.Get(d), "素材ノートに書き足した：" + d.DisplayName, DiscoveryJournal.Describe(d, fact));
        }

        /// <summary>新しい反応のページを、試しに出す（確認用・自動テスト用。記録は変わらない）</summary>
        public void PreviewDiscoveryPage(ReactionData r)
        {
            if (r == null || popups == null) return;
            var icons = new List<Sprite>();
            foreach (IngredientData x in r.RequiredIngredients) if (x != null) icons.Add(IngredientIcons.Get(x));
            popups.ShowDiscoveryPage(r.DisplayName, r.Description, icons);
        }

        // ==================================================================
        // 瞬間の演出
        // ==================================================================

        private void OnReacted(ReactionData reaction)
        {
            ReactionMoment(reaction != null ? reaction.DisplayName : "");
        }

        /// <summary>反応の瞬間の演出だけを見る（確認用・自動テスト用。計算は何も変わらない）</summary>
        public void PreviewReaction(string reactionName)
        {
            ReactionMoment(reactionName);
        }

        private void ReactionMoment(string reactionName)
        {
            ReactionCount++;
            Vector2 at = WorldToScreen(flask != null ? flask.MouthWorld : Vector3.zero) + new Vector2(0f, 90f);
            popups.ShowPop(string.IsNullOrEmpty(reactionName) ? "反応が起きた！" : $"反応！「{reactionName}」", at, new Color(1f, 0.93f, 0.65f, 1f), true);
            // 夜の空気感：きらきら・画面のフラッシュはやめて、温かい光がフラスコからふわっと漏れる
            if (flask != null) flask.WarmBloom(0.7f);
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.reactionSting : null, SfxSynth.Reaction, 0.55f, 1f);
            if (reactionSlowMotion && slowMotionSeconds > 0f)
            {
                if (slowRoutine != null) StopCoroutine(slowRoutine);
                slowRoutine = StartCoroutine(SlowMotion());
            }
        }

        /// <summary>一瞬だけ時間をゆっくりにする（ポーズ中・ほかで時間が止まっているときは何もしない）</summary>
        private IEnumerator SlowMotion()
        {
            GameFlow flow = GameFlow.Instance;
            if ((flow != null && flow.IsPaused) || (!slowing && Time.timeScale < 0.999f)) yield break;
            slowing = true;
            Time.timeScale = slowMotionScale;
            float until = Time.unscaledTime + slowMotionSeconds;
            while (Time.unscaledTime < until)
            {
                if (flow != null && flow.IsPaused) { slowing = false; yield break; }
                yield return null;
            }
            float t = 0f;
            while (t < 0.25f)
            {
                if (flow != null && flow.IsPaused) { slowing = false; yield break; }
                t += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.Lerp(slowMotionScale, 1f, t / 0.25f);
                yield return null;
            }
            Time.timeScale = 1f;
            slowing = false;
            slowRoutine = null;
        }

        private void RestoreTime()
        {
            if (!slowing) return;
            slowing = false;
            GameFlow flow = GameFlow.Instance;
            if (flow == null || !flow.IsPaused) Time.timeScale = 1f;
        }

        private void OnBottled(LiquidRecord record, bool isNew, float ml, int bottles)
        {
            if (record == null || popups == null) return;
            var m = new ResultModel
            {
                record = record,
                name = record.DisplayName,
                stars = record.kind == LiquidKind.Unknown ? 0 : record.stars,
                isNew = isNew,
                detail = $"No.{(session.Alchemy != null ? session.Alchemy.Liquids.NumberOf(record) : 0)}　{KindLabel(record.kind)}　{Mathf.RoundToInt(ml)}ml（{bottles}本）",
            };
            if (showBottleResult) popups.ShowResult(m);
            if (isNew)
            {
                SoundLibrary lib = AudioManager.Lib;
                // 新発見：温かい光がフラスコからフワッと漏れて、ギターが「ポロロン…」
                StartCoroutine(PlayLater(0.3f, lib != null ? lib.discovery : null, SfxSynth.Discovery, 0.6f, 1f));
                if (flask != null) flask.WarmBloom(1f);
            }
        }

        /// <summary>ページめくりの「ぱらっ」（SoundLibrary の page。空ならコードで作った音）</summary>
        private void OnPageTurn()
        {
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.page : null, SfxSynth.PageTurn, 0.75f, Random.Range(0.96f, 1.05f));
        }

        private void OnStarShown(int index)
        {
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.32f, StarPitch[Mathf.Clamp(index, 0, StarPitch.Length - 1)]);
        }

        private IEnumerator PlayLater(float seconds, AudioClip custom, AudioClip fallback, float volume, float pitch)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Play(custom, fallback, volume, pitch);
        }

        private void Play(AudioClip custom, AudioClip fallback, float volume, float pitch)
        {
            AudioClip clip = custom != null ? custom : fallback;
            if (clip != null && soundVolume > 0f) AudioManager.Se(clip, volume * soundVolume, pitch);
        }

        // ==================================================================
        // 小物
        // ==================================================================

        private Vector2 WorldToScreen(Vector3 world)
        {
            Camera cam = Camera.main;
            return cam != null ? (Vector2)cam.WorldToScreenPoint(world) : Vector2.zero;
        }

        private Vector2 MouthScreen()
        {
            return flask != null ? WorldToScreen(flask.MouthWorld) : new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
        }

        private Vector2 SurfaceScreen()
        {
            return flask != null ? WorldToScreen(flask.SurfaceWorld) : Vector2.zero;
        }

        private static string KindLabel(LiquidKind kind)
        {
            switch (kind)
            {
                case LiquidKind.Potion: return "ポーション";
                case LiquidKind.Intermediate: return "中間液体";
                default: return "未知の液体";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }
    }
}
