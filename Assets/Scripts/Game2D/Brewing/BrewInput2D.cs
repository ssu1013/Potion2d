using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D 調合画面のマウス・キー操作（シーンの GameSystems に付ける）。画面のボタンは使わず、机の上の物を直接さわる。
    ///   手に取る … 右の素材トレイのマスをクリック（持っている物があれば持ちかえる）。押したままフラスコまで運んでもよい
    ///   注ぐ     … 液体を持ってフラスコの上で左ボタンを押したまま「下へ」動かす（深いほど速い。ホイールでも傾く）
    ///              トレイから押したまま運んできたときは、フラスコの上に来たらそのまま傾けられる
    ///   入れる   … 固体・粉を持ってフラスコの上でクリック（押したままで続けて入れる）。トレイから運んで、フラスコの上で離してもよい
    ///   混ぜる   … 手ぶらでフラスコの上を、左ボタンを押したまま円を描く（向きと速さがそのまま計算に入る）
    ///   火       … つまみをクリックで、点ける／消す（点けるときは前の火加減）。ドラッグ・ホイールで細かく調整
    ///   瓶に詰める … 素材トレイのいちばん下の「空き瓶」をクリック（中身を全部詰める）
    ///   ページ   … 素材トレイの上でホイール（入りきらないときだけ）
    ///   戻す     … 右クリック／Q／Esc／素材トレイの空いている所をクリック（同じ素材をもう一度押しても、持ったまま）
    /// キー：C/V かき混ぜる（マウスの代わり）
    /// 守り：ウィンドウが裏に回ったら、押している途中の操作は終える／裏から戻した最初のクリックは机の操作にしない／
    ///       マウスのチャタリング・連打で、固体が一度に何個も落ちたり、瓶詰めが続けて走ったりしない
    /// 当たり判定は Collider2D（トレイの物・トレイの板・空き瓶・つまみ・フラスコ）で調べる。
    /// </summary>
    public class BrewInput2D : MonoBehaviour
    {
        public enum Activity { None, Pouring, Stirring, Dialing }
        public enum HoverKind { None, TrayItem, Knob, Flask, Bottle, Tray }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private BrewSession2D session;
        [SerializeField] private Flask2D flask;
        [SerializeField] private HeldItem2D held;
        [SerializeField] private Camera viewCamera;

        [Header("注ぐ")]
        [SerializeField, Min(1f), Tooltip("いちばん深く傾けたときに注がれる量（ml/秒）")] private float maxPourMl = 140f;
        [SerializeField, Range(0f, 0.9f), Tooltip("この傾き（0〜1）を超えると液体が出始める")] private float pourStartTilt = 0.3f;
        [SerializeField, Min(10f), Tooltip("この画素数だけマウスを下へ動かすと、いちばん深く傾く（1080p 換算）")] private float tiltPixels = 240f;

        [Header("混ぜる")]
        [SerializeField, Min(0.5f), Tooltip("かき混ぜる速さの上限（回転/秒）")] private float maxStirTurnsPerSecond = 4f;
        [SerializeField, Min(2f), Tooltip("この画素数（1080p 換算）動くごとに、進む向きを測る")] private float stirSegmentPixels = 10f;
        [SerializeField, Min(0.1f), Tooltip("C／V キーを押している間の、かき混ぜる速さ（回転/秒）")] private float keyStirTurnsPerSecond = 1.6f;

        [Header("火加減・入れる")]
        [SerializeField, Min(10f), Tooltip("この画素数だけドラッグすると、火加減が 0 → 最大になる（1080p 換算）")] private float knobPixels = 300f;
        [SerializeField, Range(0.05f, 1f), Tooltip("つまみをクリックして火を点けたときの火加減（はじめて点けるとき。2回目からは前の火加減）")] private float clickOnHeat = 0.6f;
        [SerializeField, Min(1f), Tooltip("これより動かさずに離したら「クリック」とみなす（1080p 換算の画素数）")] private float clickPixels = 8f;
        [SerializeField, Min(1f), Tooltip("トレイから押したまま、これより動かしたら「運んでいる」とみなす（1080p 換算の画素数）")] private float dragPixels = 24f;
        [SerializeField, Min(0.05f), Tooltip("固体・粉を押したままのとき、続けて入れる間隔（秒）")] private float dropInterval = 0.4f;

        /// <summary>固体・粉を続けて入れられる、いちばん短い間隔（秒）。マウスのチャタリング・二重クリックで2個落ちないように</summary>
        private const float MinDropGap = 0.1f;
        /// <summary>瓶詰めのあと、次に瓶詰めを受け付けるまで（秒）</summary>
        private const float BottleGuard = 0.35f;
        /// <summary>裏から戻したあと、クリックを机の操作にしない時間（秒）。ウィンドウを前に出すためのクリックで、瓶詰め・火が動かないように</summary>
        private const float RefocusGuard = 0.2f;

        private readonly Collider2D[] hits = new Collider2D[16];
        private Vector2 pointer;
        private Vector2 lastPointer;
        private Vector3 pointerWorld;
        private float scroll;
        private bool leftPressed;
        private bool leftHeld;
        private float dialValue;
        private float dropTimer;
        private Vector2 stirLastPoint;
        private Vector2 stirLastDirection;
        private bool hasStirDirection;
        private float stirElapsed;
        private TrayItem2D hoveredItem;
        private BottleStation2D hoveredBottle;
        private BrewTray2D hoveredTray;
        private float dialTravel;
        private float dialStartLevel;
        private float lastOnHeat;
        private bool dragging;
        private Vector2 dragStart;
        private float pageCooldown;
        private float lastDropTime = -10f;
        private float lastBottleTime = -10f;
        private float ignorePressUntil = -10f;
        // 名前と説明の文字は、指している物・素材ノート・瓶の量が変わったときだけ作り直す（毎フレーム文字を作らない）
        private object hoverKey;
        private int hoverRevision = -1;
        private float hoverMl = -1f;
        private int hoverBottles = -1;

        public Activity Current { get; private set; }
        public HoverKind Hovered { get; private set; }
        public HeldThing2D Held { get; private set; }
        /// <summary>注ぐときの傾き（0〜1）</summary>
        public float Tilt { get; private set; }
        /// <summary>今注いでいる速さ（ml/秒）</summary>
        public float PourRate { get; private set; }
        public float PouredThisTime { get; private set; }
        public float StirTurnsThisTime { get; private set; }
        public bool StirClockwise { get; private set; } = true;
        /// <summary>カーソルが指している物の名前と説明（画面のそばに出す）</summary>
        public string HoverTitle { get; private set; } = "";
        public string HoverSubtitle { get; private set; } = "";
        public Vector2 PointerScreen => pointer;

        private void Start()
        {
            if (session == null) session = FindFirstObjectByType<BrewSession2D>();
            if (flask == null) flask = FindFirstObjectByType<Flask2D>();
            if (held == null) held = FindFirstObjectByType<HeldItem2D>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (held != null) held.Hide();
            pointer = lastPointer = Pointer2D.Position;
        }

        /// <summary>
        /// ウィンドウが裏に回った（Alt+Tab など）：押している途中の操作（注ぐ・かき混ぜる・つまみ・運ぶ）を終える。
        /// 裏に回るとゲームが止まるので、ボタンを離したことに気づけず、戻ったときに勝手に注ぎ続けてしまうのを防ぐ
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ignorePressUntil = Time.unscaledTime + RefocusGuard;
                lastPointer = Pointer2D.Position;
                return;
            }
            if (Current != Activity.None) EndActivity();
            dragging = false;
            dropTimer = 0f;
        }

        /// <summary>調合机の操作を受け付けてよいか（タイトル・ポーズ・本やウィンドウの表示中は止める）</summary>
        private bool InputAllowed
        {
            get
            {
                GameFlow flow = GameFlow.Instance;
                if (flow != null && (flow.Mode != GameMode.Playing || flow.IsPaused)) return false;
                AtelierHUD hud = AtelierHUD.Instance;
                return hud == null || !hud.BlocksPlayerActions;
            }
        }

        private void Update()
        {
            if (session == null || viewCamera == null) return;
            if (!InputAllowed)
            {
                if (Current != Activity.None) EndActivity();
                if (hoveredItem != null) hoveredItem.SetHovered(false);
                if (hoveredBottle != null) hoveredBottle.SetHovered(false);
                hoveredItem = null;
                hoveredBottle = null;
                hoveredTray = null;
                dragging = false;
                SetHover(HoverKind.None, "", "");
                lastPointer = Pointer2D.Position;
                return;
            }
            float dt = Time.deltaTime;
            pointer = Pointer2D.Position;
            scroll = Pointer2D.Scroll;
            // 黒い帯（16:9 の外）の上は、何もない所として扱う。自動テストの「指」は UI の上かどうかを見ない
            bool overUI = (!Pointer2D.Simulated && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || !Pointer2D.InView;
            leftPressed = Pointer2D.LeftPressed && !overUI && Time.unscaledTime >= ignorePressUntil;
            leftHeld = Pointer2D.LeftHeld;
            Vector3 w = viewCamera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, -viewCamera.transform.position.z));
            pointerWorld = new Vector3(w.x, w.y, 0f);

            if (Current == Activity.None) UpdateHover(overUI);
            HandleKeys(dt);
            HandleActivity(dt);
            UpdateHeldView(dt);
            lastPointer = pointer;
        }

        // ------------------------------------------------------------------
        // 指している物
        // ------------------------------------------------------------------

        private void UpdateHover(bool overUI)
        {
            TrayItem2D item = null;
            BottleStation2D bottle = null;
            BrewTray2D trayBoard = null;
            bool knob = false, overFlask = false;
            if (!overUI)
            {
                int count = Physics2D.OverlapPointNonAlloc(pointerWorld, hits);
                for (int i = 0; i < count; i++)
                {
                    Collider2D c = hits[i];
                    if (c == null) continue;
                    TrayItem2D ti = c.GetComponentInParent<TrayItem2D>();
                    if (ti != null) { item = ti; continue; }
                    BottleStation2D bs = c.GetComponentInParent<BottleStation2D>();
                    if (bs != null) { bottle = bs; continue; }
                    BrewTray2D board = c.GetComponent<BrewTray2D>();
                    if (board != null) { trayBoard = board; continue; }
                    if (c.GetComponentInParent<HeaterKnob2D>() != null) { knob = true; continue; }
                    if (c.GetComponentInParent<Flask2D>() != null) overFlask = true;
                }
            }
            if (item != null && trayBoard == null) trayBoard = item.GetComponentInParent<BrewTray2D>();
            hoveredTray = trayBoard;

            if (hoveredItem != item)
            {
                if (hoveredItem != null) hoveredItem.SetHovered(false);
                hoveredItem = item;
                if (hoveredItem != null) hoveredItem.SetHovered(true);
            }
            if (hoveredBottle != bottle)
            {
                if (hoveredBottle != null) hoveredBottle.SetHovered(false);
                hoveredBottle = bottle;
                if (hoveredBottle != null) hoveredBottle.SetHovered(true);
            }

            // 名前を出すのは、素材と空き瓶だけ（つまみ・フラスコは、さわれば分かるので何も出さない）
            if (item != null) HoverItem(item.Entry);
            else if (bottle != null) HoverBottle();
            else if (knob) SetHover(HoverKind.Knob, "", "");
            else if (overFlask) SetHover(HoverKind.Flask, "", "");
            else if (trayBoard != null) SetHover(HoverKind.Tray, "", "");
            else SetHover(HoverKind.None, "", "");
        }

        private void SetHover(HoverKind kind, string title, string subtitle)
        {
            Hovered = kind;
            HoverTitle = title;
            HoverSubtitle = subtitle;
        }

        private void HoverItem(TrayEntry2D e)
        {
            object key = e.potion != null ? (object)e.potion : e.ingredient;
            float ml = e.potion != null ? e.potion.ml : 0f;
            if (Hovered == HoverKind.TrayItem && ReferenceEquals(key, hoverKey) && hoverRevision == DiscoveryJournal.Revision && Mathf.Approximately(ml, hoverMl)) return;
            hoverKey = key;
            hoverRevision = DiscoveryJournal.Revision;
            hoverMl = ml;
            SetHover(HoverKind.TrayItem, TrayTitle(e), TraySubtitle(e));
        }

        private void HoverBottle()
        {
            int n = session.BottlesForAll();
            if (Hovered == HoverKind.Bottle && n == hoverBottles) return;
            hoverBottles = n;
            SetHover(HoverKind.Bottle, n > 0 ? $"瓶に詰める（{n}本）" : "空き瓶", "");
        }

        private string TrayTitle(TrayEntry2D e)
        {
            if (e.potion != null) return e.potion.potionName;
            return e.ingredient != null ? e.ingredient.DisplayName : "";
        }

        private string TraySubtitle(TrayEntry2D e)
        {
            if (e.potion != null) return e.potion.DescribeAmount();
            if (e.ingredient == null) return "";
            // 名前の下には、素材ノートで見つけた性質だけ（操作の説明は出さない）
            string facts = FactLine(e.ingredient);
            return facts.Length > 0 ? facts.Substring(1) : "";
        }

        /// <summary>素材ノートで見つけた性質（まだの物は「？？？」の数だけ）</summary>
        private static string FactLine(IngredientData d)
        {
            IngredientFact all = DiscoveryJournal.FactsOf(d);
            if (all == IngredientFact.None) return "";
            var sb = new System.Text.StringBuilder("\n");
            int unknown = 0;
            foreach (IngredientFact f in DiscoveryJournal.Order)
            {
                if ((all & f) == 0) continue;
                if (!DiscoveryJournal.Knows(d, f)) { unknown++; continue; }
                if (sb.Length > 1) sb.Append('・');
                sb.Append(DiscoveryJournal.ShortLabel(f));
            }
            if (unknown > 0) sb.Append(sb.Length > 1 ? "　" : "").Append("？？？×").Append(unknown);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // キー
        // ------------------------------------------------------------------

        private void HandleKeys(float dt)
        {
            // Esc はアトリエの画面（AtelierHUD）があればそちらで扱う（持っている物を戻す → ひとやすみ）
            bool escape = Input.GetKeyDown(KeyCode.Escape) && AtelierHUD.Instance == null;
            if (Input.GetKeyDown(KeyCode.Q) || escape || (Pointer2D.RightPressed && Current == Activity.None))
            {
                ReturnHeld();
            }
            if (Current == Activity.None)
            {
                if (Input.GetKey(KeyCode.C)) session.Stir(keyStirTurnsPerSecond * dt, true);
                else if (Input.GetKey(KeyCode.V)) session.Stir(keyStirTurnsPerSecond * dt, false);
            }
        }

        // ------------------------------------------------------------------
        // 操作
        // ------------------------------------------------------------------

        private void HandleActivity(float dt)
        {
            float scale = 1080f / Mathf.Max(1f, Screen.height);
            Vector2 delta = (pointer - lastPointer) * scale;

            switch (Current)
            {
                case Activity.Pouring:
                    if (!leftHeld || Held == null || !Held.IsLiquid)
                    {
                        EndActivity();
                        return;
                    }
                    // 下へ動かすほど深く傾く（ホイールでも調整できる）
                    Tilt = Mathf.Clamp01(Tilt - delta.y / tiltPixels + scroll * 0.08f);
                    float k = Mathf.InverseLerp(pourStartTilt, 1f, Tilt);
                    PourRate = maxPourMl * k * Mathf.Sqrt(k);
                    if (PourRate > 0f) PouredThisTime += PourHeld(PourRate * dt);
                    return;

                case Activity.Stirring:
                    if (!leftHeld)
                    {
                        EndActivity();
                        return;
                    }
                    StirStep(dt);
                    return;

                case Activity.Dialing:
                    if (!leftHeld)
                    {
                        // ほとんど動かさずに離した＝クリック：火を点ける／消す
                        if (dialTravel < clickPixels) ToggleFire();
                        EndActivity();
                        return;
                    }
                    dialTravel += delta.magnitude;
                    if (dialTravel < clickPixels) return; // クリックのつもりの小さなぶれでは回さない
                    // 動かした分をためていく（ゆっくり回しても、少しずつの動きが消えないように）
                    dialValue = Mathf.Clamp01(dialValue + (delta.x + delta.y) / knobPixels);
                    float level = dialValue < 0.03f ? 0f : dialValue;
                    if (Mathf.Abs(level - session.HeatLevel) > 0.002f) session.SetHeat(level);
                    return;
            }

            // 何もしていないとき：ホイールでつまみ／素材トレイのページ
            if (Hovered == HoverKind.Knob && Mathf.Abs(scroll) > 0.01f)
            {
                float level = Mathf.Clamp01(Mathf.Round((session.HeatLevel + Mathf.Sign(scroll) * 0.1f) * 10f) / 10f);
                session.SetHeat(level);
            }
            if (pageCooldown > 0f) pageCooldown -= dt;
            if ((Hovered == HoverKind.TrayItem || Hovered == HoverKind.Tray) && hoveredTray != null && Mathf.Abs(scroll) > 0.01f && pageCooldown <= 0f)
            {
                if (hoveredTray.PageCount > 1)
                {
                    hoveredTray.ChangePage(scroll > 0f ? -1 : 1);
                    // トレイの板をめくる、小さな紙の音
                    SoundLibrary lib = AudioManager.Lib;
                    AudioClip page = lib != null && lib.page != null ? lib.page : SfxSynth.PageTurn;
                    AudioManager.Se(page, 0.32f, Random.Range(1.08f, 1.2f));
                }
                pageCooldown = 0.15f;
            }

            // トレイから押したまま運んでいる
            if (dragging && !leftPressed)
            {
                bool moved = (pointer - dragStart).magnitude * scale >= dragPixels;
                if (Held == null) dragging = false;
                else if (leftHeld)
                {
                    // 液体：フラスコの上まで運んだら、そのまま下へ動かして傾けられる
                    if (moved && Held.IsLiquid && Hovered == HoverKind.Flask)
                    {
                        dragging = false;
                        BeginPour();
                        return;
                    }
                }
                else
                {
                    // 固体・粉：フラスコの上で離したら、1回分入れる
                    if (moved && !Held.IsLiquid && Hovered == HoverKind.Flask) DropOne();
                    dragging = false;
                }
            }

            // 押したまま入れ続ける（固体・粉）
            if (Held != null && !Held.IsLiquid && leftHeld && !leftPressed && Hovered == HoverKind.Flask && dropTimer > 0f)
            {
                dropTimer -= dt;
                if (dropTimer <= 0f) DropOne();
            }
            if (!leftHeld) dropTimer = 0f;
            if (!leftPressed) return;

            switch (Hovered)
            {
                case HoverKind.TrayItem:
                    if (hoveredItem != null) Take(hoveredItem.Entry);
                    dragging = Held != null;
                    dragStart = pointer;
                    return;
                case HoverKind.Tray:
                    // トレイの空いている所をクリック：持っている物を戻す
                    ReturnHeld();
                    return;
                case HoverKind.Bottle:
                    // 詰めた直後のチャタリング・連打で、続けて「空っぽです」が出ないように
                    if (Time.unscaledTime - lastBottleTime < BottleGuard) return;
                    lastBottleTime = Time.unscaledTime;
                    if (!session.HasLiquid)
                    {
                        // 詰める物がないときは、空き瓶を指で軽くたたいた音だけ（お知らせは BrewSession2D が出す）
                        SoundLibrary lib = AudioManager.Lib;
                        if (lib != null) AudioManager.Se(lib.putBack, 0.35f, 0.78f);
                    }
                    session.BottleAll();
                    return;
                case HoverKind.Knob:
                    Current = Activity.Dialing;
                    dialValue = session.HeatLevel;
                    dialStartLevel = session.HeatLevel;
                    dialTravel = 0f;
                    return;
                case HoverKind.Flask:
                    if (Held != null && Held.IsLiquid)
                    {
                        BeginPour();
                    }
                    else if (Held != null)
                    {
                        DropOne();
                    }
                    else if (session.Flask != null && !session.Flask.IsEmpty)
                    {
                        Current = Activity.Stirring;
                        StirTurnsThisTime = 0f;
                        stirLastPoint = pointer;
                        hasStirDirection = false;
                        stirElapsed = 0f;
                    }
                    return;
            }
        }

        /// <summary>手に持っている液体を ml だけ注ぐ。使い切ったら手から消える</summary>
        private float PourHeld(float ml)
        {
            if (Held == null) return 0f;
            if (Held.Potion != null)
            {
                float poured = session.PourBottle(Held.Potion, ml);
                if (Held.Potion.ml < PotionStack.EmptyMl)
                {
                    session.Say("瓶が空になった");
                    DropHeld();
                }
                return poured;
            }
            IngredientData data = Held.Ingredient;
            float pouredMl = session.PourIngredient(data, ml);
            if (!session.Has(data))
            {
                session.Say($"{data.DisplayName}を使い切った");
                DropHeld();
            }
            return pouredMl;
        }

        private void DropOne()
        {
            dropTimer = dropInterval;
            if (Held == null || Held.Ingredient == null) return;
            if (Time.unscaledTime - lastDropTime < MinDropGap) return;
            lastDropTime = Time.unscaledTime;
            IngredientData data = Held.Ingredient;
            Vector3 from = held != null && held.HasItem ? held.MouthWorld : (flask != null ? flask.MouthWorld + Vector3.up * 0.5f : pointerWorld);
            if (!session.AddSolid(data)) return;
            if (held != null) held.Shake();
            // 素材ごとの欠片の絵（CustomAssets/Images/Ingredients/<ID>_piece.png）があれば、それをそのままの色で落とす
            if (flask != null && session.Art != null)
            {
                bool own = data.PieceSprite != null;
                flask.PlayDrop(from, own ? data.PieceSprite : session.Art.dropPiece, own ? Color.white : data.Color, data.Form == IngredientForm.Powder);
            }
            if (!session.Has(data))
            {
                session.Say($"{data.DisplayName}を使い切った");
                DropHeld();
            }
        }

        /// <summary>
        /// マウスで円を描いてかき混ぜる。カーソルの「進む向き」が回った分だけ回す
        /// （円を描く場所や大きさに関係なく、向きと速さがそのまま計算に入る。行ったり来たりは混ぜたことにしない）
        /// </summary>
        private void StirStep(float dt)
        {
            stirElapsed += dt;
            float scale = 1080f / Mathf.Max(1f, Screen.height);
            Vector2 segment = (pointer - stirLastPoint) * scale;
            if (segment.magnitude < stirSegmentPixels) return; // 手の細かいぶれを拾わない
            stirLastPoint = pointer;
            Vector2 direction = segment.normalized;
            float elapsed = stirElapsed;
            stirElapsed = 0f;
            if (!hasStirDirection)
            {
                stirLastDirection = direction;
                hasStirDirection = true;
                return;
            }
            // 画面の上が +Y なので、+ は反時計回り
            float d = Vector2.SignedAngle(stirLastDirection, direction) * Mathf.Deg2Rad;
            stirLastDirection = direction;
            if (Mathf.Abs(d) > 2.2f) return; // 折り返した
            float limit = maxStirTurnsPerSecond * Mathf.PI * 2f * Mathf.Max(elapsed, 1e-3f);
            d = Mathf.Clamp(d, -limit, limit);
            if (Mathf.Abs(d) < 1e-4f) return;
            float turns = Mathf.Abs(d) / (Mathf.PI * 2f);
            bool clockwise = d < 0f;
            session.Stir(turns, clockwise);
            StirTurnsThisTime += turns;
            StirClockwise = clockwise;
        }

        private void BeginPour()
        {
            Current = Activity.Pouring;
            Tilt = Mathf.Max(Tilt, 0.15f);
            PouredThisTime = 0f;
            PourRate = 0f;
        }

        /// <summary>つまみのクリック：火が点いていれば消す、消えていれば前の火加減で点ける</summary>
        private void ToggleFire()
        {
            if (dialStartLevel > 0.001f)
            {
                lastOnHeat = dialStartLevel;
                session.SetHeat(0f);
            }
            else session.SetHeat(lastOnHeat > 0.04f ? lastOnHeat : clickOnHeat);
        }

        private void EndActivity()
        {
            Current = Activity.None;
            PourRate = 0f;
            hasStirDirection = false;
            if (held != null) held.HideStream();
        }

        // ------------------------------------------------------------------
        // 手に持つ・戻す
        // ------------------------------------------------------------------

        private void Take(TrayEntry2D entry)
        {
            // 持っているのと同じ素材をもう一度押したときは、持ったまま（戻して取り直す音を重ねない）
            if (Held != null && Held.Potion == null && entry.potion == null && entry.ingredient != null && Held.Ingredient == entry.ingredient)
            {
                if (held != null) held.Pop();
                return;
            }
            // 持ちかえるときは、戻す音を鳴らさず、取る音だけ
            ReturnHeld(Held == null);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.pickup, 0.7f, HandlePitch(entry.ingredient, entry.potion));
            if (entry.potion != null)
            {
                PotionStack single = session.TakeBottle(entry.potion);
                if (single == null) return;
                Held = new HeldThing2D(single);
            }
            else if (entry.ingredient != null)
            {
                if (!session.Has(entry.ingredient))
                {
                    session.Say($"{entry.ingredient.DisplayName}は、まだ机に届いていない");
                    return;
                }
                Held = new HeldThing2D(entry.ingredient);
            }
            if (Held != null && held != null) held.Show(Held, session.Art, pointerWorld);
        }

        /// <summary>手に持っている物をトレイ（在庫）に戻す</summary>
        public void ReturnHeld() => ReturnHeld(true);

        private void ReturnHeld(bool sound)
        {
            if (Held == null) return;
            SoundLibrary lib = AudioManager.Lib;
            if (sound && lib != null) AudioManager.Se(lib.putBack, 0.6f, HandlePitch(Held.Ingredient, Held.Potion));
            if (Held.Potion != null && session != null) session.ReturnBottle(Held.Potion);
            DropHeld();
        }

        /// <summary>
        /// 取る・戻す音の高さを、持つ物で少し変える（液体の瓶は低めの「コトッ」、粉の袋は高めの「カサッ」）。
        /// 同じ音が機械的に続かないよう、毎回ほんの少し揺らす
        /// </summary>
        private static float HandlePitch(IngredientData ingredient, PotionStack potion)
        {
            float pitch = 1f;
            if (potion != null) pitch = 0.95f;
            else if (ingredient != null)
            {
                switch (ingredient.Form)
                {
                    case IngredientForm.Liquid: pitch = 0.93f; break;
                    case IngredientForm.Powder: pitch = 1.1f; break;
                    default: pitch = 1.02f; break;
                }
            }
            return pitch * Random.Range(0.97f, 1.03f);
        }

        /// <summary>手から消す（使い切ったとき）</summary>
        private void DropHeld()
        {
            Held = null;
            if (Current == Activity.Pouring) EndActivity();
            if (held != null) held.Hide();
        }

        private void UpdateHeldView(float dt)
        {
            if (held == null || Held == null) return;
            if (Current == Activity.Pouring && flask != null)
            {
                held.PourPose(flask.MouthWorld, Tilt, dt);
                if (PourRate > 0.5f) held.ShowStream(flask.SurfaceWorld.y, Held.Color, PourRate / maxPourMl);
                else held.HideStream();
            }
            else
            {
                held.Follow(pointerWorld, dt);
                held.HideStream();
            }
        }
    }
}
