using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 調合台（調合専用の画面）。フラスコを調べると、カメラがフラスコの手前（BrewingFlask の「Anchors/BrewView」）へ
    /// なめらかに寄って固定され、マウスが「見回す」から「カーソルで直接作業する」に切り替わる。
    ///   ・トレイの素材・自作の瓶をクリック … 手に取る（手の物はカーソルについてくる）
    ///   ・液体を持ってフラスコの上で左ボタンを押したまま、マウスを下へ … 瓶が傾き、深く傾けるほど速く注げる
    ///   ・固体・粉を持ってフラスコの上でクリック … 1回分入れる（押したままで続けて入れる）
    ///   ・手ぶらでフラスコの上を左ボタンを押したまま、ぐるぐる円を描く … ガラス棒でかき混ぜる（向きと速さがそのまま計算に入る）
    ///   ・加熱台のつまみを左ドラッグ（またはホイール）… 火加減
    ///   ・右クリック／Q … 手の物をトレイに戻す
    /// キー：F 瓶に詰める／Shift+F 全部／H 火加減の段階／C・V かき混ぜる／Space 早送り／Esc 離れる。
    /// 周りは暗くなり、フラスコにスポットライトが当たる。画質「標準」以上では、奥がぼける（被写界深度）。
    /// </summary>
    public class BrewMode : MonoBehaviour
    {
        public enum Activity { None, Pouring, Stirring, Dialing }
        public enum HoverKind { None, TrayItem, Flask, Knob, Book }

        private enum State { Off, Entering, On, Exiting }

        public static BrewMode Instance { get; private set; }

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private BrewingStation station;
        [SerializeField] private BrewTray tray;
        [SerializeField] private PlayerInteractor player;
        [SerializeField] private FirstPersonController controller;
        [SerializeField] private Camera viewCamera;

        [Header("カメラ")]
        [SerializeField, Range(20f, 80f)] private float fieldOfView = 50f;
        [SerializeField, Min(0.05f)] private float transitionSeconds = 0.6f;
        [SerializeField, Tooltip("「BrewView」の目印がないときのカメラの位置（フラスコから見て）")] private Vector3 defaultViewOffset = new Vector3(0f, 1.05f, -1.3f);
        [SerializeField, Tooltip("「BrewView」の目印がないときに見る所（フラスコから見て）")] private Vector3 defaultLookOffset = new Vector3(0f, 0.25f, -0.22f);

        [Header("周りの暗さ・スポットライト・ぼかし")]
        [SerializeField, Range(0f, 1f), Tooltip("調合台に向かっている間の、部屋の灯りの明るさ（1 = そのまま）")] private float roomDim = 0.4f;
        [SerializeField] private Color spotColor = new Color(1f, 0.93f, 0.8f);
        [SerializeField, Min(0f)] private float spotIntensity = 0.9f;
        [SerializeField, Range(0f, 1f), Tooltip("ぼけた奥を暗くする割合（1 = 暗くしない）")] private float backgroundDarken = 0.5f;

        [Header("操作の手ざわり")]
        [SerializeField, Min(1f), Tooltip("いちばん深く傾けたときに注がれる量（ml/秒）")] private float maxPourMl = 140f;
        [SerializeField, Range(0f, 0.9f), Tooltip("この傾き（0〜1）を超えると液体が出始める")] private float pourStartTilt = 0.3f;
        [SerializeField, Min(10f), Tooltip("この画素数だけマウスを下へ動かすと、いちばん深く傾く（1080p 換算）")] private float tiltPixels = 240f;
        [SerializeField, Min(0.5f), Tooltip("かき混ぜる速さの上限（回転/秒）")] private float maxStirTurnsPerSecond = 4f;
        [SerializeField, Min(2f), Tooltip("かき混ぜるとき、この画素数（1080p 換算）動くごとに進む向きを測る")] private float stirSegmentPixels = 10f;
        [SerializeField, Min(10f), Tooltip("この画素数だけドラッグすると、火加減が 0 → 最大になる（1080p 換算）")] private float knobPixels = 300f;
        [SerializeField, Min(0.05f), Tooltip("固体・粉を押したままのとき、続けて入れる間隔（秒）")] private float dropInterval = 0.4f;
        [SerializeField, Min(1f)] private float fastForwardSpeed = 4f;
        [SerializeField, Min(0.1f), Tooltip("C／V キーを押している間の、かき混ぜる速さ（回転/秒）")] private float keyStirTurnsPerSecond = 1.6f;
        [SerializeField, Min(0.03f), Tooltip("手に持った物の高さ（m）")] private float handItemHeight = 0.17f;

        private State state = State.Off;
        private float transition;
        private Transform cam;
        private Transform camParent;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private float savedFov;
        private DepthTextureMode savedDepthMode;
        private Vector3 fromPosition, toPosition;
        private Quaternion fromRotation, toRotation;
        private float fromFov, toFov;
        private float focusNear = 0.6f, focusFar = 1.3f;
        private AtelierPostFX postFX;
        private Light spot;
        private readonly List<Light> dimLights = new List<Light>();
        private readonly List<float> dimBase = new List<float>();
        private readonly List<LightFlicker> dimFlickers = new List<LightFlicker>();

        // ポインタ（マウス。自動テストでは仮のポインタ）
        private bool virtualPointer;
        private Vector2 virtualPosition;
        private bool virtualLeft, virtualRight;
        private float virtualScroll;
        private Vector2 pointer, lastPointer;
        private bool leftHeld, leftPressed, leftReleased, rightPressed, lastLeft, lastRight;
        private float scroll;
        private bool overUI;

        // 手に持っている物の見た目
        private GameObject handModel;
        private Renderer[] handRenderers = new Renderer[0];
        private ContentVisual handContent;
        private object handFor;
        private Vector3 handMouthLocal;
        private Vector3 handPosition;
        private Quaternion handRotation = Quaternion.identity;
        private float shownTilt;
        private float shake;

        // 作業
        private float dropTimer;
        private Vector2 stirLastPoint;
        private Vector2 stirLastDirection;
        private bool hasStirDirection;
        private float stirElapsed;
        private float dialValue;
        private Interactable highlighted;
        private int lastBlockedFrame = -10;
        private object hoverTextFor;
        private int hoverTextKey = int.MinValue;
        private float hoverTextTimer;

        /// <summary>調合台に向かっている（寄る・離れる途中も含む）</summary>
        public bool IsActive => state != State.Off;
        /// <summary>寄り終わって、作業できる</summary>
        public bool IsReady => state == State.On;
        /// <summary>画面の演出の強さ（0 = ふだん、1 = 調合台）</summary>
        public float Blend { get; private set; }
        public BrewingStation Station => station;
        public BrewTray Tray => tray;
        public Camera ViewCamera => viewCamera;
        public Activity Current { get; private set; }
        public HoverKind Hovered { get; private set; }
        public string HoverTitle { get; private set; }
        public string HoverSubtitle { get; private set; }
        public Vector2 PointerPosition => pointer;
        /// <summary>瓶の傾き（0〜1）</summary>
        public float Tilt { get; private set; }
        /// <summary>今の傾きで注がれる速さ（ml/秒）</summary>
        public float PourRate { get; private set; }
        /// <summary>今回の注ぎで入った量（ml）</summary>
        public float PouredThisTime { get; private set; }
        /// <summary>今回のかき混ぜの回転数と向き</summary>
        public float StirTurnsThisTime { get; private set; }
        public bool StirClockwise { get; private set; }
        public bool FastForward { get; private set; }
        /// <summary>自動テストの仮のポインタで動いている</summary>
        public bool UsingVirtualPointer => virtualPointer;
        /// <summary>手に持っている物がフラスコの上にある（注げる・入れられる）</summary>
        public bool OverFlask { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (station == null) station = FindFirstObjectByType<BrewingStation>();
            if (tray == null) tray = FindFirstObjectByType<BrewTray>();
            if (player == null) player = FindFirstObjectByType<PlayerInteractor>();
            if (controller == null) controller = FindFirstObjectByType<FirstPersonController>();
            if (viewCamera == null && player != null) viewCamera = player.GetComponentInChildren<Camera>(true);
            if (GameFlow.Instance != null) GameFlow.Instance.ModeChanged += OnGameModeChanged;
        }

        private void OnGameModeChanged(GameMode mode)
        {
            if (mode != GameMode.Playing) ForceExit();
        }

        // ------------------------------------------------------------------
        // 入る・出る
        // ------------------------------------------------------------------

        /// <summary>調合台に向かう（カメラがフラスコの手前へ寄る）</summary>
        public void Enter()
        {
            if (state != State.Off || station == null || viewCamera == null) return;
            if (GameFlow.Instance != null && GameFlow.Instance.Mode != GameMode.Playing) return;
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null && hud.IsModalOpen) return;

            cam = viewCamera.transform;
            camParent = cam.parent;
            savedLocalPosition = cam.localPosition;
            savedLocalRotation = cam.localRotation;
            savedFov = viewCamera.fieldOfView;
            savedDepthMode = viewCamera.depthTextureMode;
            fromPosition = cam.position;
            fromRotation = cam.rotation;
            fromFov = savedFov;
            ComputeView(out toPosition, out toRotation);
            toFov = fieldOfView;
            ComputeFocus();

            state = State.Entering;
            transition = 0f;
            Current = Activity.None;
            hasStirDirection = false;
            if (hud != null) hud.SetBrewMode(true);
            if (player != null && player.HeldView != null) player.HeldView.SetHidden(true);
            if (tray != null) tray.Rebuild();
            CollectRoomLights();
            EnsureSpot();
            postFX = viewCamera.GetComponent<AtelierPostFX>();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiOpen, 0.6f, 0.85f);
        }

        /// <summary>調合台を離れる（カメラが元の位置に戻る）</summary>
        public void Exit()
        {
            if (state == State.Off || state == State.Exiting) return;
            EndActivity();
            if (station != null) station.SimSpeed = 1f;
            FastForward = false;
            SetHover(HoverKind.None, null, null, null);
            fromPosition = cam.position;
            fromRotation = cam.rotation;
            fromFov = viewCamera.fieldOfView;
            state = State.Exiting;
            transition = 0f;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.6f, 0.85f);
        }

        /// <summary>すぐに元に戻す（タイトルに戻るときなど）</summary>
        public void ForceExit()
        {
            if (state == State.Off) return;
            EndActivity();
            if (station != null) station.SimSpeed = 1f;
            FastForward = false;
            SetHover(HoverKind.None, null, null, null);
            Finish();
        }

        private void Finish()
        {
            state = State.Off;
            Blend = 0f;
            if (cam != null)
            {
                cam.localPosition = savedLocalPosition;
                cam.localRotation = savedLocalRotation;
                viewCamera.fieldOfView = savedFov;
                viewCamera.depthTextureMode = savedDepthMode;
            }
            if (postFX != null)
            {
                postFX.SetFocus(0f, focusNear, focusFar, 1f);
                postFX.SetFocusZones(Vector4.zero, Vector4.zero);
            }
            ApplyRoomLights(0f);
            dimLights.Clear();
            dimBase.Clear();
            dimFlickers.Clear();
            if (spot != null) spot.enabled = false;
            DestroyHand();
            if (station != null && station.Heater != null) station.Heater.Highlighted = false;
            if (player != null && player.HeldView != null) player.HeldView.SetHidden(false);
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.SetBrewMode(false);
        }

        private void ComputeView(out Vector3 position, out Quaternion rotation)
        {
            Transform anchor = PrefabAnchors.Find(station.transform, "BrewView");
            if (anchor != null)
            {
                position = anchor.position;
                rotation = anchor.rotation;
                return;
            }
            Transform s = station.transform;
            position = s.TransformPoint(defaultViewOffset);
            rotation = Quaternion.LookRotation(s.TransformPoint(defaultLookOffset) - position, Vector3.up);
        }

        /// <summary>くっきり見せる範囲（手前のトレイ〜フラスコの奥）</summary>
        private void ComputeFocus()
        {
            float flaskFar = Vector3.Distance(toPosition, station.transform.position) + station.BottomRadius + 0.15f;
            float near = 0.25f;
            if (tray != null)
            {
                Bounds b = PrefabAnchors.RendererBounds(tray.transform);
                // 手前のトレイと、トレイの上で持った物までくっきり見せる（手前のボケはほとんど起きない）
                near = Mathf.Max(0.3f, Vector3.Distance(toPosition, b.ClosestPoint(toPosition)) - 0.4f);
            }
            focusNear = near;
            focusFar = flaskFar;
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------

        private void Update()
        {
            if (state == State.Off) return;
            ReadPointer();
            if (state != State.On) return;

            AtelierHUD hud = AtelierHUD.Instance;
            bool blocked = hud != null && hud.BlocksPlayerActions;
            SyncHand();
            if (blocked)
            {
                // 素材箱・結果の表示などが前にある間は、作業を止める
                lastBlockedFrame = Time.frameCount;
                EndActivity();
                SetHover(HoverKind.None, null, null, null);
                return;
            }

            HandleKeys();
            UpdateHover();
            HandleActivity(Time.deltaTime);
            if (station.Heater != null) station.Heater.Highlighted = Hovered == HoverKind.Knob || Current == Activity.Dialing;
        }

        private void LateUpdate()
        {
            if (state == State.Off) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (state == State.Entering || state == State.Exiting)
            {
                transition = Mathf.Min(1f, transition + dt / transitionSeconds);
                float e = transition * transition * (3f - 2f * transition);
                if (state == State.Entering)
                {
                    cam.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, e), Quaternion.Slerp(fromRotation, toRotation, e));
                    viewCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, e);
                    Blend = e;
                    if (transition >= 1f) state = State.On;
                }
                else
                {
                    // 戻る先は、プレイヤーの今の視点（離れている間に動いていないので、入る前と同じ）
                    Vector3 backPosition = camParent != null ? camParent.TransformPoint(savedLocalPosition) : fromPosition;
                    Quaternion backRotation = camParent != null ? camParent.rotation * savedLocalRotation : fromRotation;
                    cam.SetPositionAndRotation(Vector3.Lerp(fromPosition, backPosition, e), Quaternion.Slerp(fromRotation, backRotation, e));
                    viewCamera.fieldOfView = Mathf.Lerp(fromFov, savedFov, e);
                    Blend = 1f - e;
                    if (transition >= 1f)
                    {
                        Finish();
                        return;
                    }
                }
            }
            else
            {
                cam.SetPositionAndRotation(toPosition, toRotation);
                Blend = 1f;
            }

            ApplyRoomLights(Blend);
            if (spot != null)
            {
                spot.enabled = Blend > 0.01f;
                spot.intensity = spotIntensity * Blend;
            }
            UpdateHandPose(dt);
            if (postFX != null && postFX.enabled)
            {
                postFX.SetFocus(Blend, focusNear, focusFar, backgroundDarken);
                postFX.SetFocusZones(FlaskZone(), HandZone());
            }
        }

        // ------------------------------------------------------------------
        // 入力
        // ------------------------------------------------------------------

        /// <summary>自動テスト用：マウスの代わりの仮のポインタ（画面の画素の位置・左右のボタン・ホイール）</summary>
        public void SetVirtualPointer(Vector2 screenPosition, bool left, bool right = false, float wheel = 0f)
        {
            virtualPointer = true;
            virtualPosition = screenPosition;
            virtualLeft = left;
            virtualRight = right;
            virtualScroll = wheel;
        }

        public void ClearVirtualPointer()
        {
            virtualPointer = false;
            virtualLeft = false;
            virtualRight = false;
        }

        /// <summary>ワールドの位置が画面のどこに映るか（自動テスト・UI 用）</summary>
        public Vector2 ScreenPoint(Vector3 world)
        {
            Vector3 p = viewCamera != null ? viewCamera.WorldToScreenPoint(world) : Vector3.zero;
            return new Vector2(p.x, p.y);
        }

        private void ReadPointer()
        {
            lastPointer = pointer;
            bool left, right;
            if (virtualPointer)
            {
                pointer = virtualPosition;
                left = virtualLeft;
                right = virtualRight;
                scroll = virtualScroll;
                virtualScroll = 0f;
                overUI = false;
            }
            else
            {
                pointer = Input.mousePosition;
                left = Input.GetMouseButton(0);
                right = Input.GetMouseButton(1);
                scroll = Input.mouseScrollDelta.y;
                overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            }
            leftPressed = left && !lastLeft && !overUI;
            leftReleased = !left && lastLeft;
            rightPressed = right && !lastRight && !overUI;
            leftHeld = left;
            lastLeft = left;
            lastRight = right;
            if (state != State.On) lastPointer = pointer;
        }

        private void HandleKeys()
        {
            if (station == null) return;
            // E でも離れられる（結果のカードを E で閉じたすぐあとは除く）
            if (Input.GetKeyDown(KeyCode.E) && Current == Activity.None && Time.frameCount - lastBlockedFrame > 1)
            {
                Exit();
                return;
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                             Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift) ||
                             Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
                EndActivity();
                if (shift) station.BottleAll();
                else station.BottleOne();
                return;
            }
            if (Input.GetKeyDown(KeyCode.H)) station.CycleHeat();
            if (Input.GetKeyDown(KeyCode.Space)) ToggleFastForward();
            if (Input.GetKeyDown(KeyCode.Q) || rightPressed) ReturnHeld();
            if (Current == Activity.None)
            {
                if (Input.GetKey(KeyCode.C)) station.Stir(keyStirTurnsPerSecond * Time.deltaTime, true);
                else if (Input.GetKey(KeyCode.V)) station.Stir(keyStirTurnsPerSecond * Time.deltaTime, false);
            }
        }

        /// <summary>早送り（フラスコの中の時間だけ速く進める）</summary>
        public void ToggleFastForward()
        {
            SetFastForward(!FastForward);
        }

        public void SetFastForward(bool on)
        {
            FastForward = on;
            if (station != null) station.SimSpeed = on ? fastForwardSpeed : 1f;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClick, 0.6f, on ? 1.2f : 0.9f);
        }

        public float FastForwardSpeed => fastForwardSpeed;

        /// <summary>手に持っている物をトレイ（在庫）に戻す</summary>
        public void ReturnHeld()
        {
            if (player == null || (player.HeldIngredient == null && player.HeldPotion == null)) return;
            EndActivity();
            player.ClearHeld();
        }

        // ------------------------------------------------------------------
        // 何を指しているか
        // ------------------------------------------------------------------

        private readonly RaycastHit[] hits = new RaycastHit[16];

        private void UpdateHover()
        {
            if (Current != Activity.None) return;
            if (overUI)
            {
                SetHover(HoverKind.None, null, null, null);
                return;
            }

            Ray ray = viewCamera.ScreenPointToRay(pointer);
            int count = Physics.RaycastNonAlloc(ray, hits, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            // 当たった物は「つまみ → トレイの物 → フラスコ」の順に優先する
            // （フラスコの当たり判定は箱なので、すぐ横のつまみや手前の物にかぶることがある）
            BrewTrayItem item = null;
            RecipeBookStand book = null;
            float itemDistance = float.MaxValue;
            bool knob = false, flask = false;
            Transform knobTransform = station.Heater != null ? station.Heater.Knob : null;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                if (player != null && c.transform.IsChildOf(player.transform)) continue;
                if (knobTransform != null && c.transform.IsChildOf(knobTransform))
                {
                    knob = true;
                    continue;
                }
                var trayItem = c.GetComponentInParent<BrewTrayItem>();
                if (trayItem != null)
                {
                    if (hits[i].distance < itemDistance)
                    {
                        itemDistance = hits[i].distance;
                        item = trayItem;
                    }
                    continue;
                }
                if (c.GetComponentInParent<BrewingStation>() == station)
                {
                    flask = true;
                    continue;
                }
                var stand = c.GetComponentInParent<RecipeBookStand>();
                if (stand != null) book = stand;
            }
            if (knob)
            {
                item = null;
                flask = false;
            }
            else if (item != null) flask = false;

            bool holding = player != null && (player.HeldIngredient != null || player.HeldPotion != null);
            // フラスコは口のまわりも「フラスコの上」とみなす（手に持った物の陰でも注げるように）
            if (!flask && item == null && !knob && IsNearFlaskOnScreen()) flask = true;
            if (flask || item != null || knob) book = null; // 作業台の上のレシピ本は、ほかに何も指していないときだけ
            OverFlask = holding && flask;

            // 説明の文字は、指す物が変わったとき（と、量が変わる物は 0.3 秒ごと）だけ作り直す（毎フレームの文字列づくりを避ける）
            hoverTextTimer -= Time.unscaledDeltaTime;
            if (item != null)
            {
                if (!ReferenceEquals(hoverTextFor, item) || hoverTextTimer <= 0f)
                {
                    hoverTextFor = item;
                    hoverTextTimer = 0.3f;
                    string title = item.GetDisplayName(player);
                    string sub = BrewTrayItem.Describe(item.Entry, player != null ? player.Ingredients : null, station.Alchemy != null ? station.Alchemy.Settings : null);
                    SetHover(HoverKind.TrayItem, item, title, sub);
                }
                else SetHover(HoverKind.TrayItem, item, HoverTitle, HoverSubtitle);
            }
            else if (knob)
            {
                float level = station.Flask != null ? station.Flask.HeatLevel : 0f;
                int key = Mathf.RoundToInt(level * 100f);
                if (!ReferenceEquals(hoverTextFor, knobTransform) || key != hoverTextKey)
                {
                    hoverTextFor = knobTransform;
                    hoverTextKey = key;
                    SetHover(HoverKind.Knob, null, $"火加減：{ProcessStep.HeatLabel(level)}（{key}%）", "左ドラッグ・ホイールで調整（右・上で強く）");
                }
                else SetHover(HoverKind.Knob, null, HoverTitle, HoverSubtitle);
            }
            else if (flask)
            {
                string sub;
                if (!holding) sub = station.HasLiquid ? "左ボタンを押したまま円を描く：ガラス棒でかき混ぜる" : "トレイから素材を取って入れよう";
                else if (player.HeldPotion != null || (player.HeldIngredient != null && player.HeldIngredient.IsPoured)) sub = "左ボタンを押したまま下へ動かす：傾けて注ぐ";
                else sub = "クリック：1回分入れる（押したままで続けて入れる）";
                hoverTextFor = station;
                SetHover(HoverKind.Flask, holding ? null : station, "調合フラスコ", sub);
            }
            else if (book != null)
            {
                hoverTextFor = book;
                SetHover(HoverKind.Book, book, "レシピ本", "クリック：開く（R）　これまでに作った液体の記録");
            }
            else
            {
                hoverTextFor = null;
                SetHover(HoverKind.None, null, null, null);
            }
        }

        private bool IsNearFlaskOnScreen()
        {
            Vector2 mouth = ScreenPoint(station.MouthWorld);
            Vector2 bottom = ScreenPoint(station.BottomWorld);
            float radius = Mathf.Max(60f, Vector2.Distance(mouth, bottom) * 0.45f);
            // 口から底までの線分への距離
            Vector2 ab = bottom - mouth;
            float t = Mathf.Clamp01(Vector2.Dot(pointer - mouth, ab) / Mathf.Max(1f, ab.sqrMagnitude));
            Vector2 closest = mouth + ab * t;
            return Vector2.Distance(pointer, closest) < radius || Vector2.Distance(pointer, mouth + Vector2.up * radius * 0.6f) < radius;
        }

        private void SetHover(HoverKind kind, Interactable target, string title, string subtitle)
        {
            Hovered = kind;
            HoverTitle = title;
            HoverSubtitle = subtitle;
            if (target != highlighted)
            {
                HighlightOutline.Hide(highlighted);
                highlighted = target;
                HighlightOutline.Show(target);
            }
        }

        // ------------------------------------------------------------------
        // 作業（取る・注ぐ・入れる・かき混ぜる・火加減）
        // ------------------------------------------------------------------

        private void HandleActivity(float dt)
        {
            bool holding = player != null && (player.HeldIngredient != null || player.HeldPotion != null);
            bool holdingLiquid = player != null && (player.HeldPotion != null || (player.HeldIngredient != null && player.HeldIngredient.IsPoured));
            float scale = 1080f / Mathf.Max(1f, Screen.height);
            Vector2 delta = (pointer - lastPointer) * scale;

            switch (Current)
            {
                case Activity.Pouring:
                    if (!leftHeld || !holdingLiquid)
                    {
                        EndActivity();
                        return;
                    }
                    // 下へ動かすほど深く傾く（ホイールでも調整できる）
                    Tilt = Mathf.Clamp01(Tilt - delta.y / tiltPixels + scroll * 0.08f);
                    float k = Mathf.InverseLerp(pourStartTilt, 1f, Tilt);
                    PourRate = maxPourMl * k * Mathf.Sqrt(k);
                    Vector3 from = HandMouthWorld();
                    PouredThisTime += station.PourHeld(player, PourRate, dt, from);
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
                    if (station.Flask != null)
                    {
                        // 動かした分をためていく（ゆっくり回しても、少しずつの動きが消えないように）
                        float before = station.Flask.HeatLevel;
                        dialValue = Mathf.Clamp01(dialValue + (delta.x + delta.y) / knobPixels);
                        float level = dialValue < 0.03f ? 0f : dialValue;
                        if (Mathf.Abs(level - before) > 0.002f)
                        {
                            station.SetHeat(level);
                            // 5% ごとにカチッと鳴る（つまみの手ごたえ）
                            if (Mathf.FloorToInt(level * 20f + 0.0001f) != Mathf.FloorToInt(before * 20f + 0.0001f)) PlayTick(level);
                        }
                    }
                    return;
            }

            // 何もしていないとき：ホイールでつまみ
            if (Hovered == HoverKind.Knob && Mathf.Abs(scroll) > 0.01f && station.Flask != null)
            {
                float level = Mathf.Clamp01(Mathf.Round((station.Flask.HeatLevel + Mathf.Sign(scroll) * 0.1f) * 10f) / 10f);
                station.SetHeat(level);
                PlayTick(level);
            }

            // 押したまま入れ続ける（固体・粉）
            if (holding && !holdingLiquid && leftHeld && !leftPressed && OverFlask && dropTimer > 0f)
            {
                dropTimer -= dt;
                if (dropTimer <= 0f) DropOne();
            }
            if (!leftHeld) dropTimer = 0f;
            if (!leftPressed) return;

            switch (Hovered)
            {
                case HoverKind.TrayItem:
                    var item = highlighted as BrewTrayItem;
                    if (item == null) return;
                    // 持っている物があれば戻してから持ちかえる
                    if (holding) player.ClearHeld();
                    item.Take(player);
                    return;
                case HoverKind.Book:
                    if (AtelierHUD.Instance != null) AtelierHUD.Instance.OpenRecipeBook();
                    return;
                case HoverKind.Knob:
                    Current = Activity.Dialing;
                    dialValue = station.Flask != null ? station.Flask.HeatLevel : 0f;
                    PlayTick(station.Flask != null ? station.Flask.HeatLevel : 0f);
                    return;
                case HoverKind.Flask:
                    if (holdingLiquid)
                    {
                        Current = Activity.Pouring;
                        Tilt = Mathf.Max(Tilt, 0.15f);
                        PouredThisTime = 0f;
                        PourRate = 0f;
                    }
                    else if (holding)
                    {
                        DropOne();
                    }
                    else if (station.Flask != null && !station.Flask.IsEmpty)
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

        private void DropOne()
        {
            dropTimer = dropInterval;
            if (station.DropHeldSolid(player, HandMouthWorld())) shake = 1f;
        }

        /// <summary>
        /// マウスで円を描いてかき混ぜる。カーソルの「進む向き」が回った分だけ、ガラス棒が回る
        /// （円を描く場所や大きさに関係なく、向きと速さがそのまま計算に入る。行ったり来たりは混ぜたことにしない）
        /// </summary>
        private void StirStep(float dt)
        {
            stirElapsed += dt;
            float scale = 1080f / Mathf.Max(1f, Screen.height);
            Vector2 segment = (pointer - stirLastPoint) * scale;
            // 少し動いてから向きを測る（手の細かいぶれを拾わない）
            if (segment.magnitude < stirSegmentPixels) return;
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
            // 画面の上が +Y なので、+ は反時計回り（上から見たフラスコでも反時計回り）
            float d = Vector2.SignedAngle(stirLastDirection, direction) * Mathf.Deg2Rad;
            stirLastDirection = direction;
            if (Mathf.Abs(d) > 2.2f) return; // 折り返した
            float limit = maxStirTurnsPerSecond * Mathf.PI * 2f * Mathf.Max(elapsed, 1e-3f);
            d = Mathf.Clamp(d, -limit, limit);
            if (Mathf.Abs(d) < 1e-4f) return;
            float turns = Mathf.Abs(d) / (Mathf.PI * 2f);
            bool clockwise = d < 0f;
            station.Stir(turns, clockwise, true);
            StirTurnsThisTime += turns;
            StirClockwise = clockwise;
        }

        private void EndActivity()
        {
            if (Current == Activity.Pouring && station != null) station.StopPour();
            if (Current == Activity.Dialing) PlayTick(station != null && station.Flask != null ? station.Flask.HeatLevel : 0f);
            Current = Activity.None;
            PourRate = 0f;
            hasStirDirection = false;
        }

        private static void PlayTick(float level)
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.knobTick != null ? lib.knobTick : lib.uiClick, 0.55f, 0.85f + level * 0.45f + Random.Range(-0.03f, 0.03f));
        }

        // ------------------------------------------------------------------
        // 手に持っている物（カーソルについてくる）
        // ------------------------------------------------------------------

        private void SyncHand()
        {
            object want = player == null ? null : player.HeldPotion != null ? (object)player.HeldPotion : player.HeldIngredient;
            if (ReferenceEquals(want, handFor)) return;
            DestroyHand();
            handFor = want;
            if (want == null) return;

            GameObject model;
            if (player.HeldPotion != null)
            {
                model = PotionVisuals.CreateBottle(player.HeldPotion.color, player.HeldPotion.baseLiquidId, transform, "BrewHand_" + player.HeldPotion.potionName);
            }
            else
            {
                IngredientData data = player.HeldIngredient;
                GameObject prefab = GameContext.Art != null ? GameContext.Art.IngredientModel(data) : null;
                model = prefab != null ? Instantiate(prefab, transform) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.name = "BrewHand_" + data.DisplayName;
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (Interactable i in model.GetComponentsInChildren<Interactable>(true)) Destroy(i);
                var content = model.GetComponent<ContentVisual>();
                if (content != null)
                {
                    content.SetColor(data.Color);
                    content.SetAmount(player.HeldAmountRatio(data));
                    content.SetFollowMovement(true);
                }
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Transform t = model.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds b = PrefabAnchors.RendererBounds(t);
            t.localScale *= Mathf.Clamp(handItemHeight / Mathf.Max(0.01f, b.size.y), 0.1f, 5f);
            handMouthLocal = t.InverseTransformPoint(PrefabAnchors.WorldPoint(t, PrefabAnchors.Mouth));
            handModel = model;
            handRenderers = model.GetComponentsInChildren<Renderer>();
            handContent = model.GetComponent<ContentVisual>();
            if (handContent != null && player.HeldPotion != null) handContent.SetAmount(Mathf.Clamp01(player.HeldPotion.ml / PotionStack.BottleMl));
            if (handContent != null) handContent.SetFollowMovement(true);
            handPosition = HandTarget(out handRotation);
            t.SetPositionAndRotation(handPosition, handRotation);
        }

        private void DestroyHand()
        {
            if (handModel != null) Destroy(handModel);
            handModel = null;
            handContent = null;
            handFor = null;
        }

        /// <summary>手に持っている物の注ぎ口・入れ口</summary>
        private Vector3 HandMouthWorld()
        {
            if (handModel == null) return station.MouthWorld + Vector3.up * 0.15f;
            return handModel.transform.TransformPoint(handMouthLocal);
        }

        private void UpdateHandPose(float dt)
        {
            if (handModel == null || state == State.Off) return;
            // 傾きは、注いでいないときはゆっくり戻る
            if (Current != Activity.Pouring) Tilt = Mathf.MoveTowards(Tilt, 0f, dt * 2.5f);
            shownTilt = Mathf.Lerp(shownTilt, Tilt, 1f - Mathf.Exp(-14f * dt));
            shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            if (handContent != null && player != null)
            {
                if (player.HeldPotion != null) handContent.SetAmount(Mathf.Clamp01(player.HeldPotion.ml / PotionStack.BottleMl));
                else if (player.HeldIngredient != null) handContent.SetAmount(player.HeldAmountRatio(player.HeldIngredient));
            }

            Vector3 target = HandTarget(out Quaternion rotation);
            float k = 1f - Mathf.Exp(-16f * dt);
            handPosition = Vector3.Lerp(handPosition, target, k);
            handRotation = Quaternion.Slerp(handRotation, rotation, k);
            handModel.transform.SetPositionAndRotation(handPosition, handRotation);
        }

        /// <summary>手に持っている物を置く位置と向き（フラスコの上では注ぐ構え、それ以外はカーソルの下）</summary>
        private Vector3 HandTarget(out Quaternion rotation)
        {
            Vector3 mouth = station.MouthWorld;
            Vector3 right = viewCamera.transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            // 画面の奥へ向かう水平の向き。この軸のまわりに + に回すと、物の上が画面の左へ倒れる
            Vector3 forward = Vector3.Cross(right, Vector3.up);

            bool pourPose = Current == Activity.Pouring || OverFlask;
            if (pourPose)
            {
                // 口の右上に構えて、画面の左（フラスコの口の方）へ傾ける。傾けても注ぎ口が口の真上に来るように置く。
                // 固体・粉は、入れるたびに大きく傾けて戻す
                float angle = Mathf.Lerp(14f, 118f, shownTilt) + Mathf.Sin(shake * Mathf.PI) * 55f;
                rotation = Quaternion.AngleAxis(angle, forward);
                Vector3 mouthLocalScaled = Vector3.Scale(handMouthLocal, handModel.transform.lossyScale);
                Vector3 want = mouth + Vector3.up * Mathf.Lerp(0.07f, 0.035f, shownTilt) + right * Mathf.Lerp(0.03f, 0.005f, shownTilt);
                return want - rotation * mouthLocalScaled;
            }

            // カーソルの下：画面の下（トレイ）ではトレイの少し上、上へ行くほどフラスコの口の高さの水平面に置く
            rotation = Quaternion.AngleAxis(6f, forward);
            float low = (tray != null ? tray.transform.position.y : station.BottomWorld.y - 0.15f) + 0.14f;
            float high = mouth.y + 0.02f;
            float trayScreen = tray != null ? ScreenPoint(tray.transform.position).y : Screen.height * 0.2f;
            float mouthScreen = ScreenPoint(mouth).y;
            float s = Mathf.InverseLerp(trayScreen, mouthScreen, pointer.y);
            float y = Mathf.Lerp(low, high, s * s * (3f - 2f * s));
            Ray ray = viewCamera.ScreenPointToRay(pointer);
            float t = Mathf.Abs(ray.direction.y) > 1e-4f ? (y - ray.origin.y) / ray.direction.y : 0.8f;
            if (t <= 0f) t = 0.8f;
            t = Mathf.Clamp(t, 0.6f, 2.2f);
            Vector3 hit = ray.origin + ray.direction * t;
            Vector3 fromFlask = hit - station.transform.position;
            fromFlask.y = 0f;
            if (fromFlask.magnitude > 0.75f)
            {
                Vector3 clamped = station.transform.position + fromFlask.normalized * 0.75f;
                hit = new Vector3(clamped.x, hit.y, clamped.z);
            }
            // 持つ位置（物の中ほど）がカーソルに来るように、物の高さの半分だけ下げる
            return hit - Vector3.up * handItemHeight * 0.5f;
        }

        // ------------------------------------------------------------------
        // ぼかさない所（ガラスは深さを書かないので、フラスコと手に持った物の所は画面の楕円で指定する）
        // ------------------------------------------------------------------

        private Vector4 FlaskZone()
        {
            Vector3 bottom = station.BottomWorld - Vector3.up * 0.03f;
            Vector3 top = station.MouthWorld + Vector3.up * 0.24f; // ガラス棒の頭・注ぐ瓶・口から出た湯気まで
            Vector3 middle = (bottom + top) * 0.5f;
            Vector2 c = ScreenPoint(middle);
            float ry = Mathf.Abs(ScreenPoint(top).y - ScreenPoint(bottom).y) * 0.5f;
            float rx = Vector2.Distance(c, ScreenPoint(middle + viewCamera.transform.right * station.BottomRadius * 1.25f));
            return ToZone(c, rx * 1.15f, ry * 1.12f);
        }

        private Vector4 HandZone()
        {
            if (handModel == null || handRenderers.Length == 0) return Vector4.zero;
            Bounds b = handRenderers[0].bounds;
            for (int i = 1; i < handRenderers.Length; i++)
            {
                if (handRenderers[i] != null && !(handRenderers[i] is ParticleSystemRenderer)) b.Encapsulate(handRenderers[i].bounds);
            }
            Vector2 c = ScreenPoint(b.center);
            float r = Vector2.Distance(c, ScreenPoint(b.center + viewCamera.transform.right * b.extents.magnitude)) * 1.25f;
            return ToZone(c, r, r);
        }

        private Vector4 ToZone(Vector2 center, float rx, float ry)
        {
            float w = Mathf.Max(1f, viewCamera.pixelWidth);
            float h = Mathf.Max(1f, viewCamera.pixelHeight);
            return new Vector4(center.x / w, center.y / h, rx / w, ry / h);
        }

        // ------------------------------------------------------------------
        // 部屋の灯り・スポットライト
        // ------------------------------------------------------------------

        private void CollectRoomLights()
        {
            dimLights.Clear();
            dimBase.Clear();
            dimFlickers.Clear();
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l == null || l == spot || !l.isActiveAndEnabled) continue;
                if (station != null && l.transform.IsChildOf(station.transform)) continue;
                if (station != null && station.Heater != null && l.transform.IsChildOf(station.Heater.transform)) continue;
                if (l.transform.IsChildOf(transform)) continue;
                dimLights.Add(l);
                dimBase.Add(l.intensity);
                dimFlickers.Add(l.GetComponent<LightFlicker>());
            }
        }

        private void ApplyRoomLights(float blend)
        {
            float scale = Mathf.Lerp(1f, roomDim, blend);
            for (int i = 0; i < dimLights.Count; i++)
            {
                if (dimLights[i] == null) continue;
                if (dimFlickers[i] != null) dimFlickers[i].IntensityScale = scale;
                else dimLights[i].intensity = dimBase[i] * scale;
            }
        }

        private void EnsureSpot()
        {
            if (spot == null)
            {
                var go = new GameObject("BrewSpotlight");
                go.transform.SetParent(transform, false);
                spot = go.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.shadows = LightShadows.None;
                spot.renderMode = LightRenderMode.ForcePixel;
            }
            // フラスコの斜め上・手前から、フラスコとトレイを照らす
            Vector3 target = Vector3.Lerp(station.BottomWorld, station.MouthWorld, 0.3f);
            if (tray != null) target = Vector3.Lerp(target, tray.transform.position, 0.3f);
            Vector3 toCamera = toPosition - station.transform.position;
            toCamera.y = 0f;
            Vector3 position = station.transform.position + Vector3.up * 1.25f + toCamera.normalized * 0.45f + viewCamera.transform.right * 0.2f;
            spot.transform.position = position;
            spot.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            spot.color = spotColor;
            spot.range = 3f;
            spot.spotAngle = 58f;
            spot.innerSpotAngle = 30f;
            spot.intensity = 0f;
            spot.enabled = true;
        }
    }
}
