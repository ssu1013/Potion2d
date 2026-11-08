using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D 調合画面のマウス・キー操作（シーンの GameSystems に付ける）。3D 版 BrewMode の操作の決まりをそのまま 2D にしたもの。
    ///   手に取る … トレイの物をクリック（持っている物があれば持ちかえる）
    ///   注ぐ     … 液体を持ってフラスコの上で左ボタンを押したまま「下へ」動かす（深いほど速い。ホイールでも傾く）
    ///   入れる   … 固体・粉を持ってフラスコの上でクリック（押したままで続けて入れる）
    ///   混ぜる   … 手ぶらでフラスコの上を、左ボタンを押したまま円を描く（向きと速さがそのまま計算に入る）
    ///   火加減   … つまみを左ドラッグ（右・上で強く）またはホイール
    ///   戻す     … 右クリック／Q／Esc
    /// キー：F 瓶に詰める・Shift+F 全部・H 火加減の段階・C/V かき混ぜる・Space 早送り
    /// 当たり判定は Collider2D（トレイの物・つまみ・フラスコ）で調べる。
    /// </summary>
    public class BrewInput2D : MonoBehaviour
    {
        public enum Activity { None, Pouring, Stirring, Dialing }
        public enum HoverKind { None, TrayItem, Knob, Flask }

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
        [SerializeField, Min(0.05f), Tooltip("固体・粉を押したままのとき、続けて入れる間隔（秒）")] private float dropInterval = 0.4f;

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
            pointer = lastPointer = Input.mousePosition;
        }

        /// <summary>調合台の操作を受け付けてよいか（タイトル・ポーズ・ウィンドウ表示中・部屋にいる間は止める）</summary>
        private bool InputAllowed
        {
            get
            {
                GameFlow flow = GameFlow.Instance;
                if (flow != null && (flow.Mode != GameMode.Playing || flow.IsPaused)) return false;
                AtelierView2D view = AtelierView2D.Current;
                if (view != null && (!view.InBrew || view.Transitioning)) return false;
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
                hoveredItem = null;
                SetHover(HoverKind.None, "", "");
                lastPointer = Input.mousePosition;
                return;
            }
            float dt = Time.deltaTime;
            pointer = Input.mousePosition;
            scroll = Input.mouseScrollDelta.y;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            leftPressed = Input.GetMouseButtonDown(0) && !overUI;
            leftHeld = Input.GetMouseButton(0);
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
                    if (c.GetComponentInParent<HeaterKnob2D>() != null) { knob = true; continue; }
                    if (c.GetComponentInParent<Flask2D>() != null) overFlask = true;
                }
            }

            if (hoveredItem != item)
            {
                if (hoveredItem != null) hoveredItem.SetHovered(false);
                hoveredItem = item;
                if (hoveredItem != null) hoveredItem.SetHovered(true);
            }

            if (item != null) SetHover(HoverKind.TrayItem, TrayTitle(item.Entry), TraySubtitle(item.Entry));
            else if (knob) SetHover(HoverKind.Knob, "火加減のつまみ", $"{ProcessStep.HeatLabel(session.HeatLevel)}（{Mathf.RoundToInt(session.HeatLevel * 100f)}%）　ドラッグ・ホイールで回す");
            else if (overFlask) SetHover(HoverKind.Flask, "調合フラスコ", FlaskHint());
            else SetHover(HoverKind.None, "", "");
        }

        private void SetHover(HoverKind kind, string title, string subtitle)
        {
            Hovered = kind;
            HoverTitle = title;
            HoverSubtitle = subtitle;
        }

        private string FlaskHint()
        {
            if (Held == null) return session.Flask != null && !session.Flask.IsEmpty ? "押したまま円を描いて、かき混ぜる" : "トレイから素材を取って入れよう";
            return Held.IsLiquid ? "押したまま下へ動かして、傾けて注ぐ" : "クリックで1回分入れる（押したままで続けて入れる）";
        }

        private string TrayTitle(TrayEntry2D e)
        {
            if (e.potion != null) return e.potion.potionName;
            return e.ingredient != null ? e.ingredient.DisplayName : "";
        }

        private string TraySubtitle(TrayEntry2D e)
        {
            if (e.potion != null) return $"自作の液体　{e.potion.DescribeAmount()}";
            if (e.ingredient == null) return "";
            string form = e.ingredient.IsPoured ? "液体（注ぐ）" : (e.ingredient.Form == IngredientForm.Powder ? "粉（振り入れる）" : "固体（入れる）");
            return $"{form}　在庫 {session.AmountLabel(e.ingredient)}";
        }

        // ------------------------------------------------------------------
        // キー
        // ------------------------------------------------------------------

        private void HandleKeys(float dt)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                         Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (shift) session.BottleAll();
                else session.BottleOne();
            }
            if (Input.GetKeyDown(KeyCode.H)) session.CycleHeat();
            if (Input.GetKeyDown(KeyCode.Space)) session.FastForward = !session.FastForward;
            // Esc はアトリエの画面（AtelierHUD）があればそちらで扱う（持っている物を戻す → 部屋に戻る）
            bool escape = Input.GetKeyDown(KeyCode.Escape) && AtelierHUD.Instance == null;
            if (Input.GetKeyDown(KeyCode.Q) || escape || (Input.GetMouseButtonDown(1) && Current == Activity.None))
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
                    // 注ぐ音：フラスコが満ちるほど高くなる
                    if (session.Flask != null)
                        AudioManager.SetPourLoop(PourRate > 0.5f, 0.85f + 0.35f * Mathf.Clamp01(session.Flask.VolumeMl / Mathf.Max(1f, session.CapacityMl)));
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
                        EndActivity();
                        return;
                    }
                    // 動かした分をためていく（ゆっくり回しても、少しずつの動きが消えないように）
                    dialValue = Mathf.Clamp01(dialValue + (delta.x + delta.y) / knobPixels);
                    float level = dialValue < 0.03f ? 0f : dialValue;
                    if (Mathf.Abs(level - session.HeatLevel) > 0.002f) session.SetHeat(level);
                    return;
            }

            // 何もしていないとき：ホイールでつまみ
            if (Hovered == HoverKind.Knob && Mathf.Abs(scroll) > 0.01f)
            {
                float level = Mathf.Clamp01(Mathf.Round((session.HeatLevel + Mathf.Sign(scroll) * 0.1f) * 10f) / 10f);
                session.SetHeat(level);
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
                    return;
                case HoverKind.Knob:
                    Current = Activity.Dialing;
                    dialValue = session.HeatLevel;
                    return;
                case HoverKind.Flask:
                    if (Held != null && Held.IsLiquid)
                    {
                        Current = Activity.Pouring;
                        Tilt = Mathf.Max(Tilt, 0.15f);
                        PouredThisTime = 0f;
                        PourRate = 0f;
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
            IngredientData data = Held.Ingredient;
            Vector3 from = held != null && held.HasItem ? held.MouthWorld : (flask != null ? flask.MouthWorld + Vector3.up * 0.5f : pointerWorld);
            if (!session.AddSolid(data)) return;
            if (held != null) held.Shake();
            if (flask != null && session.Art != null) flask.PlayDrop(from, session.Art.dropPiece, data.Color);
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

        private void EndActivity()
        {
            if (Current == Activity.Pouring) AudioManager.SetPourLoop(false);
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
            ReturnHeld();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.pickup, 0.7f);
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
                    session.Say($"{entry.ingredient.DisplayName}は在庫がありません");
                    return;
                }
                Held = new HeldThing2D(entry.ingredient);
            }
            if (Held != null && held != null) held.Show(Held, session.Art, pointerWorld);
        }

        /// <summary>手に持っている物をトレイ（在庫）に戻す</summary>
        public void ReturnHeld()
        {
            if (Held == null) return;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.putBack, 0.6f);
            if (Held.Potion != null) session.ReturnBottle(Held.Potion);
            DropHeld();
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
