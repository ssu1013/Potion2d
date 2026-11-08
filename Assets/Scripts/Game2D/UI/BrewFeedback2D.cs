using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合台の手ごたえ（Step4 の A「見える化」と B「音と演出」）。BrewHUD の Prefab のルートに付ける（なければ BrewHUD2D が自分に付ける）。
    ///   A：見立てカード（属性の棒・溶かしきったときの予告・相談の目標の点線・条件の ○△×・ひとこと）／温度計／
    ///      かき混ぜている間の溶け具合の輪／注いだ量・火加減の数字／値が変わったときの「回復 +3」
    ///   B：目標に届いた瞬間（和音・きらきら・カードが光る）／反応の瞬間（光・きらきら・スロー・音）／
    ///      瓶詰めの結果カード（★ が1つずつ「ちりん」・新発見）／沸騰・焦げ・溶けきった・素材が弱まった/目覚めたのお知らせ
    /// 見た目は「置き場所」「絵」の欄で変えられる（空の絵はコードで描いた仮の絵）。音は SoundLibrary の「調合の手ごたえ」。
    /// </summary>
    [DisallowMultipleComponent]
    public class BrewFeedback2D : MonoBehaviour
    {
        public enum GoalState { None, Short, Projected, Met }

        [Header("置き場所（1920×1080 の画面で、左下が 0,0）")]
        [SerializeField, Tooltip("見立てカードの左下")] private Vector2 meterPosition = new Vector2(340f, 352f);
        [SerializeField] private Vector2 meterSize = new Vector2(360f, 470f);
        [SerializeField, Tooltip("温度計の左下（球の下）")] private Vector2 thermometerPosition = new Vector2(770f, 312f);
        [SerializeField] private Vector2 thermometerSize = new Vector2(56f, 400f);
        [SerializeField, Tooltip("瓶詰めの結果カードの真ん中")] private Vector2 resultCenter = new Vector2(961f, 800f);

        [Header("絵（空ならコードで描いた仮の絵）")]
        [SerializeField, Tooltip("見立てカードの紙（9スライス推奨。空なら UISkin の羊皮紙）")] private Sprite meterPanel;
        [SerializeField, Tooltip("棒の下地（9スライス推奨）")] private Sprite barBack;
        [SerializeField, Tooltip("棒の中身（白で描く。属性の色で塗られる）")] private Sprite barFill;
        [SerializeField, Tooltip("温度計の管（9スライス推奨）")] private Sprite thermometerTube;
        [SerializeField, Tooltip("溶け具合の輪（白い輪。ぐるっと塗っていく）")] private Sprite stirRing;

        [Header("演出")]
        [SerializeField, Tooltip("反応が起きた瞬間、一瞬だけ時間をゆっくりにする")] private bool reactionSlowMotion = true;
        [SerializeField, Range(0.1f, 1f)] private float slowMotionScale = 0.35f;
        [SerializeField, Min(0f), Tooltip("ゆっくりにする時間（実際の秒）")] private float slowMotionSeconds = 0.45f;
        [SerializeField, Tooltip("反応が起きた瞬間、画面がパッと光る")] private bool screenFlash = true;
        [SerializeField, Tooltip("瓶詰めの結果カードを出す（出すときは、下の文字のお知らせは出さない）")] private bool showBottleResult = true;
        [SerializeField, Min(0.1f), Tooltip("同じ属性の「+3」が続けて出るときの間隔（秒）")] private float popInterval = 0.55f;
        [SerializeField, Range(0f, 2f), Tooltip("手ごたえの音の大きさ")] private float soundVolume = 1f;

        private static readonly float[] StarPitch = { 1f, 1.122f, 1.26f, 1.498f, 1.682f };

        private static readonly int Count = AttributeVector.Count;

        private BrewSession2D session;
        private BrewInput2D input;
        private Flask2D flask;
        private FlaskEffects2D effects;
        private HeldItem2D held;
        private QuestManager quests;
        private Canvas canvas;

        private BrewMeterUI2D meter;
        private BrewThermoUI2D thermo;
        private BrewPopupsUI2D popups;

        private readonly MeterModel model = new MeterModel();
        private readonly float[] current = new float[Count];
        private readonly float[] projected = new float[Count];
        private readonly float[] lastCurrent = new float[Count];
        private readonly float[] popAccum = new float[Count];
        private readonly float[] lastPopTime = new float[Count];
        private readonly List<PotionAttribute> rowAttrs = new List<PotionAttribute>();
        private readonly List<PotionAttribute> candidates = new List<PotionAttribute>();
        private readonly List<FlaskSimulation.IngredientState> states = new List<FlaskSimulation.IngredientState>();
        private readonly List<BrewThermoUI2D.Mark> marks = new List<BrewThermoUI2D.Mark>();
        // このフラスコで、もう知らせたこと（「素材ID:性質」）。フラスコが空になったら忘れる
        private readonly HashSet<string> noticed = new HashSet<string>();

        private GoalState goal = GoalState.None;
        private QuestData lastQuest;
        private bool primed;
        private bool wasBoiling;
        private bool wasScorching;
        private float lastDissolved = 1f;
        private float pollTimer;
        private bool slowing;
        private Coroutine slowRoutine;

        public static BrewFeedback2D Current { get; private set; }

        /// <summary>相談の見立て（目標に届いているか）</summary>
        public GoalState Goal => goal;
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
        public int TargetRows { get; private set; }
        public int ReactionCount { get; private set; }
        public string Status => model.status;
        public float CurrentValue(PotionAttribute a) => current[(int)a];
        public float ProjectedValue(PotionAttribute a) => projected[(int)a];

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
            quests = FindFirstObjectByType<QuestManager>();
            canvas = GetComponent<Canvas>();
            ViewportFit2D fit = GetComponent<ViewportFit2D>();
            RectTransform parent = fit != null ? fit.SafeRoot : (RectTransform)transform;

            // 奥：見立てカード・温度計（Prefab の文字やボタンより奥）／手前：ポップ・輪・結果カード・光
            RectTransform back = UIFactory.CreateRect("FeedbackBack", parent);
            UIFactory.Stretch(back);
            back.SetAsFirstSibling();
            RectTransform front = UIFactory.CreateRect("FeedbackFront", parent);
            UIFactory.Stretch(front);
            front.SetAsLastSibling();
            meter = new BrewMeterUI2D(back, meterPosition, meterSize, meterPanel, barBack, barFill);
            thermo = new BrewThermoUI2D(back, thermometerPosition, thermometerSize, thermometerTube);
            popups = new BrewPopupsUI2D(front, resultCenter, stirRing);
            popups.StarShown = OnStarShown;
            popups.PageTurnStarted = OnPageTurn;
            foreach (UnityEngine.UI.Graphic g in back.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;
            foreach (UnityEngine.UI.Graphic g in front.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;

            if (session != null)
            {
                session.ContentsChanged += Refresh;
                session.Bottled += OnBottled;
                session.Reacted += OnReacted;
            }
            DiscoveryJournal.FactDiscovered += OnFactDiscovered;
            DiscoveryJournal.ReactionDiscovered += OnReactionDiscovered;
            BrewHUD2D hud = GetComponent<BrewHUD2D>();
            if (hud != null && hud.LayoutVersion < 4)
            {
                Debug.LogWarning("[調合画面] 画面の配置が Step4 より前のままです。メニュー「Tools/Potion Frontier 2D/Step4：調合画面を新しくする」を実行すると、見立てカードと重ならない配置になります。", this);
            }
            // コードで作る音は、最初に鳴らすときに引っかからないよう、先に作っておく
            _ = SfxSynth.Chime;
            _ = SfxSynth.Goal;
            Refresh();
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.ContentsChanged -= Refresh;
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
            if (session == null || session.Flask == null || meter == null) return;
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
            thermo.Apply(sim.Temperature, rules != null ? rules.maxHeatTemperature : 130f,
                         ProcessStep.HeatLabel(sim.HeatLevel) + (sim.IsBoiling ? "・沸騰" : ""), sim.IsScorching, marks);

            UpdateCursorHelpers(sim);
            meter.Tick(dt);
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
            switch (input.Current)
            {
                case BrewInput2D.Activity.Pouring:
                    follow = $"+{Mathf.RoundToInt(input.PouredThisTime)}ml\n<size=15>{Mathf.RoundToInt(sim.VolumeMl)} / {Mathf.RoundToInt(session.CapacityMl)} ml</size>";
                    if (held != null && held.HasItem) at = WorldToScreen(held.MouthWorld) + new Vector2(30f, 6f);
                    break;
                case BrewInput2D.Activity.Stirring:
                    if (solids) ring = true;
                    else follow = $"{(input.StirClockwise ? "時計回り" : "反時計回り")} {input.StirTurnsThisTime:0.0}回";
                    break;
                case BrewInput2D.Activity.Dialing:
                    follow = $"{ProcessStep.HeatLabel(session.HeatLevel)}　{Mathf.RoundToInt(session.HeatLevel * 100f)}%";
                    break;
            }
            popups.SetFollow(follow, at);
            float df = sim.DissolvedFraction;
            popups.SetRing(ring, pointer, df, $"溶け {Mathf.FloorToInt(df * 100f)}%　{(input.StirClockwise ? "時計回り" : "反時計回り")}");
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
                Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.55f, 1.5f);
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
                    Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.5f, 1.26f);
                    if (effects == null) effects = FindFirstObjectByType<FlaskEffects2D>();
                    if (effects != null) effects.Sparkle(new Color(1f, 0.85f, 0.4f, 1f), 14);
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
                if (s.meltBlocked && s.undissolved > 0.01f && noticed.Add(d.IngredientId + ":blocked"))
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
            return noticed.Add(d.IngredientId + ":" + fact);
        }

        /// <summary>素材の性質を初めて見つけた：素材ノートに書き足したお知らせ（スタンプがぽんっ）</summary>
        private void OnFactDiscovered(IngredientData d, IngredientFact fact)
        {
            if (d == null) return;
            noticed.Add(d.IngredientId + ":" + fact);
            if (popups == null) return;
            popups.ShowSlip(IngredientIcons.Get(d), "素材ノートに書き足した：" + d.DisplayName, DiscoveryJournal.Describe(d, fact));
            SoundLibrary lib = AudioManager.Lib;
            StartCoroutine(PlayLater(0.3f, lib != null ? lib.putBack : null, null, 0.55f, 0.8f));
            StartCoroutine(PlayLater(0.36f, lib != null ? lib.starChime : null, SfxSynth.Chime, 0.45f, 1.122f));
        }

        /// <summary>新しい反応を初めて見つけた：反応のページ</summary>
        private void OnReactionDiscovered(ReactionData r)
        {
            if (r == null || popups == null) return;
            var icons = new List<Sprite>();
            foreach (IngredientData x in r.RequiredIngredients) if (x != null) icons.Add(IngredientIcons.Get(x));
            popups.ShowDiscoveryPage(r.DisplayName, r.Description, icons);
            SoundLibrary lib = AudioManager.Lib;
            StartCoroutine(PlayLater(0.6f, lib != null ? lib.discovery : null, SfxSynth.Discovery, 0.75f, 1f));
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
        // 見立て（中身が変わるたび：BrewSession2D の ContentsChanged、0.25 秒ごと）
        // ==================================================================

        private void Refresh()
        {
            if (meter == null || session == null || session.Flask == null) return;
            FlaskSimulation sim = session.Flask;
            AlchemySettings settings = sim.Settings;
            if (settings == null) return;
            LiquidRecord preview = session.Preview;
            QuestData quest = ActiveQuest();
            bool wet = session.HasLiquid;
            bool solids = sim.SolidUnits > 0.001f;
            float dissolved = sim.DissolvedFraction;

            for (int i = 0; i < Count; i++) current[i] = 0f;
            if (preview != null && preview.attributes != null)
            {
                for (int i = 0; i < Count && i < preview.attributes.Length; i++) current[i] = preview.attributes[i];
            }
            if (wet) sim.ProjectConcentration(projected);
            else System.Array.Copy(current, projected, Count);
            AttributeVector cur = AttributeVector.FromArray(current);
            AttributeVector proj = AttributeVector.FromArray(projected);

            // 棒に出す属性：相談の条件の属性 → 入っている属性（多い順）
            rowAttrs.Clear();
            candidates.Clear();
            if (quest != null)
            {
                foreach (AttributeCondition c in quest.Conditions)
                {
                    if (c != null && !rowAttrs.Contains(c.attribute)) rowAttrs.Add(c.attribute);
                }
            }
            for (int i = 0; i < Count; i++)
            {
                var a = (PotionAttribute)i;
                if (!rowAttrs.Contains(a) && Mathf.Max(current[i], projected[i]) >= 0.5f) candidates.Add(a);
            }
            candidates.Sort((x, y) => Mathf.Max(current[(int)y], projected[(int)y]).CompareTo(Mathf.Max(current[(int)x], projected[(int)x])));
            rowAttrs.AddRange(candidates);
            if (sim.IsEmpty) rowAttrs.Clear();

            model.rows.Clear();
            int targetRows = 0;
            for (int n = 0; n < rowAttrs.Count && n < BrewMeterUI2D.MaxRows; n++)
            {
                PotionAttribute a = rowAttrs[n];
                var r = new MeterModel.Row
                {
                    attribute = a,
                    name = settings.GetDisplayName(a),
                    color = AttributeColor(settings, a),
                    current = current[(int)a],
                    projected = projected[(int)a],
                };
                if (quest != null)
                {
                    foreach (AttributeCondition c in quest.Conditions)
                    {
                        if (c == null || c.attribute != a) continue;
                        if (c.type == ConditionType.AtLeast || c.type == ConditionType.Between) { r.hasMin = true; r.min = c.min; }
                        if (c.type == ConditionType.AtMost || c.type == ConditionType.Between) { r.hasMax = true; r.max = c.max; }
                    }
                }
                if (r.hasMin || r.hasMax)
                {
                    targetRows++;
                    r.met = Within(r, r.current);
                    r.metProjected = Within(r, r.projected);
                }
                model.rows.Add(r);
            }
            TargetRows = targetRows;

            model.target = quest != null ? $"◆ {quest.ClientName}さんの悩みに合う液体" : "◆ 相談なし（自由に探求）";
            model.showDissolve = solids && !sim.IsEmpty;
            model.dissolveFraction = dissolved;
            model.dissolve = solids ? $"溶け具合 {Mathf.FloorToInt(dissolved * 100f)}%" : "";
            model.conditions = quest != null ? ConditionLines(quest, settings, cur, proj, preview) : "";

            GoalState next = GoalState.None;
            if (sim.IsEmpty)
            {
                model.status = "素材を入れると、見立てが出ます";
                model.statusColor = UIColors.InkLight;
            }
            else if (!wet)
            {
                model.status = "液体を注ぐと、溶けはじめます";
                model.statusColor = UIColors.InkLight;
            }
            else if (quest == null)
            {
                model.status = solids && dissolved < 0.99f ? "円を描いてかき混ぜると、\n溶け残りが溶けていく" : "相談を受けると、目標の線が出ます";
                model.statusColor = UIColors.InkLight;
            }
            else if (quest.IsSatisfiedBy(preview))
            {
                next = GoalState.Met;
                model.status = $"○ 今のままで、{quest.ClientName}さんの\n悩みに合いそう！ 瓶に詰めよう";
                model.statusColor = new Color(0.18f, 0.48f, 0.16f, 1f);
            }
            else if (ProjectedSatisfies(quest, settings, proj, preview))
            {
                next = GoalState.Projected;
                model.status = solids && dissolved < 0.99f ? "△ 溶かしきれば、合いそう！\n円を描いて、よくかき混ぜよう" : "△ もう少しで合いそう";
                model.statusColor = new Color(0.62f, 0.44f, 0.04f, 1f);
            }
            else
            {
                next = GoalState.Short;
                model.status = "まだ合わない：\n" + Gap(quest, settings, proj, preview);
                model.statusColor = new Color(0.55f, 0.3f, 0.18f, 1f);
            }
            meter.Apply(model);

            // 目標に届いた／外れた（入ったばかり・相談が変わったときは鳴らさない）
            if (!primed || quest != lastQuest)
            {
                goal = next;
                lastQuest = quest;
                primed = true;
                for (int i = 0; i < Count; i++) lastCurrent[i] = current[i];
                return;
            }
            if (next != goal)
            {
                if (next == GoalState.Met) OnGoalReached(quest);
                else if (goal == GoalState.Met && (next == GoalState.Short || next == GoalState.Projected)) OnGoalLost(quest);
                goal = next;
            }
            ValuePops(settings, preview);
        }

        /// <summary>属性の値が変わったら「回復 +3」をフラスコから浮かべる（同じ属性は少し間をあける）</summary>
        private void ValuePops(AlchemySettings settings, LiquidRecord preview)
        {
            bool visible = canvas == null || canvas.enabled;
            float now = Time.unscaledTime;
            for (int i = 0; i < Count; i++)
            {
                float d = current[i] - lastCurrent[i];
                lastCurrent[i] = current[i];
                if (preview == null || !visible)
                {
                    popAccum[i] = 0f;
                    continue;
                }
                popAccum[i] += d;
                if (Mathf.Abs(popAccum[i]) < 1f || now - lastPopTime[i] < popInterval) continue;
                var a = (PotionAttribute)i;
                Color c = Color.Lerp(AttributeColor(settings, a), Color.white, 0.3f);
                string sign = popAccum[i] > 0f ? "+" : "-";
                popups.ShowPop($"{settings.GetDisplayName(a)} {sign}{Mathf.Abs(popAccum[i]):0.#}", SurfaceScreen() + new Vector2(Random.Range(-70f, 70f), 10f), c);
                popAccum[i] = 0f;
                lastPopTime[i] = now;
            }
        }

        private static bool Within(MeterModel.Row r, float v)
        {
            if (r.hasMin && v < r.min) return false;
            if (r.hasMax && v > r.max) return false;
            return true;
        }

        private static string ConditionLines(QuestData quest, AlchemySettings settings, AttributeVector cur, AttributeVector proj, LiquidRecord preview)
        {
            var sb = new System.Text.StringBuilder();
            int lines = 0;
            foreach (AttributeCondition c in quest.Conditions)
            {
                if (c == null || lines >= 3) continue;
                bool now = preview != null && c.Evaluate(cur);
                bool soon = c.Evaluate(proj);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Icon(now, soon)).Append(' ').Append(Describe(c, settings));
                lines++;
            }
            if (quest.MinStars > 0 && lines < 3)
            {
                int s = preview != null ? preview.stars : 0;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Icon(s >= quest.MinStars, ProjectedStars(settings, proj, preview) >= quest.MinStars))
                  .Append($" ★{quest.MinStars} 以上（今 ★{s}）");
                lines++;
            }
            if (quest.RequiredPotion != null && lines < 3)
            {
                bool ok = preview != null && preview.specialPotionId == quest.RequiredPotion.PotionId;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Icon(ok, ok)).Append($" 特別な液体「{quest.RequiredPotion.PotionName}」");
            }
            return sb.ToString();
        }

        private static string Icon(bool now, bool soon)
        {
            if (now) return "<color=#2E7D32>○</color>";
            return soon ? "<color=#B8860B>△</color>" : "<color=#A0522D>×</color>";
        }

        /// <summary>条件の説明（記号 ≥ はフォントにないことがあるので、言葉で書く）</summary>
        private static string Describe(AttributeCondition c, AlchemySettings settings)
        {
            string n = settings.GetDisplayName(c.attribute);
            switch (c.type)
            {
                case ConditionType.AtLeast: return $"{n} {c.min:0.#} 以上";
                case ConditionType.AtMost: return $"{n} {c.max:0.#} 以下";
                case ConditionType.Between: return $"{n} {c.min:0.#}〜{c.max:0.#}";
                case ConditionType.IsDominant: return $"{n}がいちばん強い";
                case ConditionType.ShareAtLeast: return $"{n}の割合 {c.min:0.#}% 以上";
                default: return n;
            }
        }

        private static bool ProjectedSatisfies(QuestData quest, AlchemySettings settings, AttributeVector proj, LiquidRecord preview)
        {
            if (quest.RequiredPotion != null) return false;
            if (quest.MinStars > ProjectedStars(settings, proj, preview)) return false;
            foreach (AttributeCondition c in quest.Conditions)
            {
                if (c != null && !c.Evaluate(proj)) return false;
            }
            return true;
        }

        /// <summary>溶かしきったときの ★ の見込み（属性の合計から。名前や特性による変化は入らないので、だいたい）</summary>
        private static int ProjectedStars(AlchemySettings settings, AttributeVector proj, LiquidRecord preview)
        {
            if (preview != null && preview.kind == LiquidKind.Unknown) return 0;
            float potency = proj.TotalPotency;
            if (potency <= 0f) return 0;
            GradeTier tier = settings.GetGrade(potency, out _);
            return tier != null ? Mathf.Clamp(tier.stars, 1, 5) : 1;
        }

        /// <summary>何が足りないかを、ひとことで（溶かしきったときの見込みで比べる）</summary>
        private static string Gap(QuestData quest, AlchemySettings settings, AttributeVector proj, LiquidRecord preview)
        {
            foreach (AttributeCondition c in quest.Conditions)
            {
                if (c == null || c.Evaluate(proj)) continue;
                string n = settings.GetDisplayName(c.attribute);
                float v = proj[c.attribute];
                switch (c.type)
                {
                    case ConditionType.AtLeast: return $"{n}があと {Mathf.Max(0.1f, c.min - v):0.#} 足りない";
                    case ConditionType.AtMost: return $"{n}が {Mathf.Max(0.1f, v - c.max):0.#} 多すぎる";
                    case ConditionType.Between: return v < c.min ? $"{n}があと {c.min - v:0.#} 足りない" : $"{n}が {v - c.max:0.#} 多すぎる";
                    case ConditionType.IsDominant: return $"{n}を、いちばん強くしよう";
                    case ConditionType.ShareAtLeast: return $"{n}の割合が足りない";
                }
            }
            if (quest.MinStars > ProjectedStars(settings, proj, preview)) return $"★が足りない（★{quest.MinStars} 以上）";
            if (quest.RequiredPotion != null) return $"特別な液体「{quest.RequiredPotion.PotionName}」が必要";
            return "悩みに合う液体を探そう";
        }

        private static Color AttributeColor(AlchemySettings settings, PotionAttribute a)
        {
            Color c = settings.GetInfo(a).color;
            c.a = 1f;
            // 羊皮紙の上で見えるよう、明るすぎる色は少し落とす
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            if (lum > 0.8f) c = Color.Lerp(c, new Color(0.35f, 0.3f, 0.25f, 1f), 0.3f);
            return c;
        }

        private QuestData ActiveQuest()
        {
            if (quests == null) return null;
            return quests.Phase == CustomerPhase.Waiting ? quests.CurrentQuest : null;
        }

        // ==================================================================
        // 瞬間の演出
        // ==================================================================

        private void OnGoalReached(QuestData quest)
        {
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.goalReached : null, SfxSynth.Goal, 0.75f, 1f);
            popups.ShowPop($"{quest.ClientName}さんの悩みに合いそう！", MouthScreen() + new Vector2(0f, 70f), new Color(1f, 0.9f, 0.5f, 1f), true);
            meter.Glow(new Color(1f, 0.82f, 0.3f, 1f));
            if (effects == null) effects = FindFirstObjectByType<FlaskEffects2D>();
            if (effects != null) effects.Sparkle(new Color(1f, 0.85f, 0.4f, 1f), 26);
        }

        private void OnGoalLost(QuestData quest)
        {
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.softWarning : null, SfxSynth.SoftWarning, 0.55f, 1f);
            popups.ShowPop("悩みに合わなくなった…", MouthScreen() + new Vector2(0f, 70f), new Color(1f, 0.6f, 0.5f, 1f));
        }

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
            Color color = flask != null ? flask.ShownColor : Color.white;
            Vector2 at = WorldToScreen(flask != null ? flask.MouthWorld : Vector3.zero) + new Vector2(0f, 90f);
            popups.ShowPop(string.IsNullOrEmpty(reactionName) ? "反応が起きた！" : $"反応！「{reactionName}」", at, new Color(1f, 0.93f, 0.65f, 1f), true);
            if (flask != null) flask.Flash(color);
            if (effects == null) effects = FindFirstObjectByType<FlaskEffects2D>();
            if (effects != null) effects.Sparkle(color, 40);
            if (screenFlash) popups.Flash(color);
            meter.Glow(Color.Lerp(color, Color.white, 0.3f));
            SoundLibrary lib = AudioManager.Lib;
            Play(lib != null ? lib.reactionSting : null, SfxSynth.Reaction, 0.85f, 1f);
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
            QuestData quest = ActiveQuest();
            var m = new ResultModel
            {
                record = record,
                name = record.DisplayName,
                stars = record.kind == LiquidKind.Unknown ? 0 : record.stars,
                isNew = isNew,
                detail = $"No.{(session.Alchemy != null ? session.Alchemy.Liquids.NumberOf(record) : 0)}　{KindLabel(record.kind)}　{Mathf.RoundToInt(ml)}ml（{bottles}本）",
            };
            if (quest != null)
            {
                m.questMatch = quest.IsSatisfiedBy(record);
                m.quest = m.questMatch ? $"○ {quest.ClientName}さんの悩みに合う！" : $"× {quest.ClientName}さんの悩みには合わない";
            }
            if (showBottleResult) popups.ShowResult(m);
            if (isNew)
            {
                SoundLibrary lib = AudioManager.Lib;
                StartCoroutine(PlayLater(0.15f, lib != null ? lib.discovery : null, SfxSynth.Discovery, 0.7f, 1f));
                if (effects == null) effects = FindFirstObjectByType<FlaskEffects2D>();
                if (effects != null) effects.Sparkle(record.color, 30);
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
            Play(lib != null ? lib.starChime : null, SfxSynth.Chime, 0.5f, StarPitch[Mathf.Clamp(index, 0, StarPitch.Length - 1)]);
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
