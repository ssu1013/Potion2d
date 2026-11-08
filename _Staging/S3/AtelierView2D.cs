using System.Collections;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// アトリエの見え方（案A＋B）：横長の部屋を、画面の左右の「＜」「＞」でカウンター ⇄ 作業台 ⇄ 棚と行き来し（マウスだけで遊べる）、作業台をクリックすると、カメラが作業台へなめらかに寄って、
    /// 寄り切ったところで調合台の画面（高解像度で描いた調合用の絵）に切り替わる。Esc・「部屋に戻る」で逆の順に戻る。
    /// 部屋では、カウンターのお客さん・レシピ本・瓶の棚・作業台をクリックして調べる（RoomHotspot2D）。
    /// シーンの GameSystems に付ける。
    /// </summary>
    public class AtelierView2D : MonoBehaviour
    {
        public static AtelierView2D Current { get; private set; }

        [Header("カメラ")]
        [SerializeField] private Camera viewCamera;

        [Header("部屋（Room の Prefab の Anchors）")]
        [SerializeField, Tooltip("部屋に入ったときのカメラの位置（作業台の前）")] private Transform roomStart;
        [SerializeField, Tooltip("カメラが行ける左の端（カメラの真ん中の位置）")] private Transform boundsLeft;
        [SerializeField, Tooltip("カメラが行ける右の端（カメラの真ん中の位置）")] private Transform boundsRight;
        [SerializeField, Min(1f), Tooltip("部屋を見るときのカメラの大きさ（画面の高さの半分）")] private float roomSize = 5.4f;
        [SerializeField, Min(0.05f), Tooltip("「＜」「＞」で隣の場所へ動く時間（秒）")] private float slideSeconds = 0.5f;

        [Header("作業台へのズーム")]
        [SerializeField, Tooltip("寄っていく先（部屋の作業台のフラスコのあたり）")] private Transform workbenchFocus;
        [SerializeField, Min(0.5f), Tooltip("寄り切ったときのカメラの大きさ")] private float zoomSize = 1.9f;
        [SerializeField, Min(0.05f)] private float zoomSeconds = 0.55f;
        [SerializeField, Tooltip("調合台の画面のカメラの位置（調合用の絵の真ん中）")] private Transform brewView;
        [SerializeField, Min(1f)] private float brewSize = 5.4f;
        [SerializeField, Min(0.01f), Tooltip("切り替えるときの暗転（秒）")] private float switchFadeOut = 0.12f;
        [SerializeField, Min(0.01f)] private float switchFadeIn = 0.28f;

        [Header("タイトル")]
        [SerializeField, Min(0f), Tooltip("タイトル画面で、部屋をゆっくり見回す幅")] private float titleSway = 2.5f;

        /// <summary>立ち止まる場所（左から：カウンター・作業台・棚）</summary>
        public static readonly string[] StopNames = { "カウンター", "作業台", "棚" };

        private readonly Collider2D[] hits = new Collider2D[16];
        private int stop = 1;
        private QuestManager quests;
        private RoomHotspot2D hovered;
        private float titleTime;

        /// <summary>調合台の画面にいる（寄り切って切り替わったあと）</summary>
        public bool InBrew { get; private set; }
        /// <summary>寄っている・戻っている途中</summary>
        public bool Transitioning { get; private set; }
        public string HoverTitle { get; private set; } = "";
        public string HoverSubtitle { get; private set; } = "";
        /// <summary>今いる場所（0 = カウンター・1 = 作業台・2 = 棚）</summary>
        public int Stop => stop;
        /// <summary>隣の場所へ動いている途中</summary>
        public bool Sliding { get; private set; }

        /// <summary>direction（-1 = 左・+1 = 右）へ動けるか</summary>
        public bool CanMove(int direction)
        {
            int next = stop + direction;
            return next >= 0 && next < StopNames.Length;
        }

        /// <summary>隣の場所の名前（動けなければ空）</summary>
        public string NeighborName(int direction) => CanMove(direction) ? StopNames[stop + direction] : "";

        /// <summary>「＜」「＞」を押したとき：隣の場所へカメラがすっと動く</summary>
        public void MoveStop(int direction)
        {
            if (InBrew || Transitioning || Sliding || !CanMove(direction)) return;
            stop += direction;
            StartCoroutine(SlideRoutine(StopX(stop)));
        }

        /// <summary>カウンターの前へ動く（お客さんが来たときなど）</summary>
        public void GoToStop(int index)
        {
            if (InBrew || Transitioning || Sliding || index == stop || index < 0 || index >= StopNames.Length) return;
            stop = index;
            StartCoroutine(SlideRoutine(StopX(stop)));
        }

        private float StopX(int index)
        {
            Vector3 center = roomStart != null ? roomStart.position : Vector3.zero;
            if (index == 0) return boundsLeft != null ? boundsLeft.position.x : center.x;
            if (index == 2) return boundsRight != null ? boundsRight.position.x : center.x;
            return center.x;
        }

        private IEnumerator SlideRoutine(float targetX)
        {
            Sliding = true;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.footsteps != null && lib.footsteps.Length > 0 ? lib.footsteps[0] : lib.uiClick, 0.35f);
            Vector3 from = viewCamera.transform.position;
            Vector3 to = new Vector3(targetX, from.y, from.z);
            yield return Move(from, viewCamera.orthographicSize, to, roomSize, slideSeconds);
            Sliding = false;
        }

        private void Awake()
        {
            Current = this;
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            quests = FindFirstObjectByType<QuestManager>();
            if (GameFlow.Instance == null) EnterRoomImmediate();
        }

        // ------------------------------------------------------------------
        // 呼び出し（GameFlow・AtelierHUD から）
        // ------------------------------------------------------------------

        public void EnterRoomImmediate()
        {
            StopAllCoroutines();
            InBrew = false;
            Transitioning = false;
            Sliding = false;
            stop = 1;
            Vector3 p = roomStart != null ? roomStart.position : Vector3.zero;
            SetCamera(new Vector3(p.x, p.y, CameraZ), roomSize);
        }

        public void EnterTitle()
        {
            EnterRoomImmediate();
            titleTime = 0f;
        }

        public void EnterBrew()
        {
            if (InBrew || Transitioning) return;
            StartCoroutine(EnterBrewRoutine());
        }

        public void ExitBrew()
        {
            if (!InBrew || Transitioning) return;
            StartCoroutine(ExitBrewRoutine());
        }

        // ------------------------------------------------------------------

        private float CameraZ => viewCamera != null ? viewCamera.transform.position.z : -10f;

        private void SetCamera(Vector3 position, float size)
        {
            if (viewCamera == null) return;
            viewCamera.transform.position = position;
            viewCamera.orthographicSize = size;
        }

        private void Update()
        {
            ClearHover();
            if (viewCamera == null) return;
            GameFlow flow = GameFlow.Instance;
            if (flow != null && flow.Mode == GameMode.Title)
            {
                // タイトル：部屋をゆっくり見回す
                titleTime += Time.unscaledDeltaTime;
                Vector3 p = roomStart != null ? roomStart.position : Vector3.zero;
                SetCamera(new Vector3(p.x + Mathf.Sin(titleTime * 0.15f) * titleSway, p.y, CameraZ), roomSize);
                return;
            }
            if (InBrew || Transitioning || Sliding) return;
            if (flow != null && flow.IsPaused) return;
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null && hud.BlocksPlayerActions) return;

            UpdateHoverAndClick();
        }

        private void UpdateHoverAndClick()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            Vector3 m = Input.mousePosition;
            Vector3 w = viewCamera.ScreenToWorldPoint(new Vector3(m.x, m.y, -CameraZ));
            int count = Physics2D.OverlapPointNonAlloc(new Vector2(w.x, w.y), hits);
            Customer2D customer = null;
            RoomHotspot2D spot = null;
            float spotArea = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i] == null) continue;
                Customer2D c = hits[i].GetComponentInParent<Customer2D>();
                if (c != null) { customer = c; continue; }
                RoomHotspot2D h = hits[i].GetComponent<RoomHotspot2D>();
                // 重なっているときは、小さい方（カウンターの上の台帳など）を選ぶ
                if (h != null && (spot == null || Area(hits[i]) < spotArea))
                {
                    spot = h;
                    spotArea = Area(hits[i]);
                }
            }

            if (customer != null && quests != null && customer == quests.CurrentCustomer)
            {
                QuestData q = customer.Quest;
                string sub = quests.Phase == CustomerPhase.Offering ? "クリックで相談を聞く"
                           : quests.Phase == CustomerPhase.Waiting ? "クリックで話す（分け与える・作り方を教える）" : "";
                HoverTitle = q != null ? q.ClientFullName : "お客さん";
                HoverSubtitle = sub;
                if (Input.GetMouseButtonDown(0) && !string.IsNullOrEmpty(sub)) quests.TalkTo(customer);
                return;
            }
            if (spot == null) return;
            hovered = spot;
            spot.SetHovered(true);
            HoverTitle = spot.Title;
            HoverSubtitle = spot.Subtitle;
            if (!Input.GetMouseButtonDown(0)) return;

            AtelierHUD hud = AtelierHUD.Instance;
            switch (spot.Type)
            {
                case RoomHotspot2D.Kind.Workbench:
                    EnterBrew();
                    break;
                case RoomHotspot2D.Kind.RecipeBook:
                case RoomHotspot2D.Kind.Shelf:
                    if (hud != null) hud.OpenRecipeBook();
                    break;
                case RoomHotspot2D.Kind.Ledger:
                    if (hud != null) hud.OpenLedger();
                    break;
                case RoomHotspot2D.Kind.Counter:
                    if (quests != null && quests.CurrentCustomer != null &&
                        (quests.Phase == CustomerPhase.Offering || quests.Phase == CustomerPhase.Waiting))
                        quests.TalkTo(quests.CurrentCustomer);
                    else if (hud != null) hud.ShowToast("今はお客さんがいない。来たらドアベルが鳴る");
                    break;
            }
        }

        private static float Area(Collider2D c)
        {
            Vector3 size = c.bounds.size;
            return size.x * size.y;
        }

        private void ClearHover()
        {
            if (hovered != null) hovered.SetHovered(false);
            hovered = null;
            HoverTitle = "";
            HoverSubtitle = "";
        }

        // ------------------------------------------------------------------
        // ズームと切り替え
        // ------------------------------------------------------------------

        private IEnumerator EnterBrewRoutine()
        {
            Transitioning = true;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiOpen, 0.5f);
            Vector3 from = viewCamera.transform.position;
            float fromSize = viewCamera.orthographicSize;
            Vector3 to = workbenchFocus != null ? workbenchFocus.position : from;
            to.z = CameraZ;
            yield return Move(from, fromSize, to, zoomSize, zoomSeconds);

            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) yield return hud.Fader.FadeOut(switchFadeOut);
            Vector3 brew = brewView != null ? brewView.position : Vector3.zero;
            brew.z = CameraZ;
            SetCamera(brew, brewSize * 1.08f);
            InBrew = true;
            if (hud != null) StartCoroutine(hud.Fader.FadeIn(switchFadeIn));
            // 切り替わったあとも、ほんの少し寄り続ける（寄った勢いがつながって見えるように）
            yield return Move(brew, brewSize * 1.08f, brew, brewSize, switchFadeIn);
            Transitioning = false;
        }

        private IEnumerator ExitBrewRoutine()
        {
            Transitioning = true;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.5f);
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) yield return hud.Fader.FadeOut(switchFadeOut);
            Vector3 focus = workbenchFocus != null ? workbenchFocus.position : viewCamera.transform.position;
            focus.z = CameraZ;
            SetCamera(focus, zoomSize);
            InBrew = false;
            if (hud != null) StartCoroutine(hud.Fader.FadeIn(switchFadeIn));
            Vector3 room = roomStart != null ? roomStart.position : focus;
            room.z = CameraZ;
            stop = 1;
            yield return Move(focus, zoomSize, room, roomSize, zoomSeconds);
            Transitioning = false;
        }

        private IEnumerator Move(Vector3 fromPos, float fromSize, Vector3 toPos, float toSize, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / seconds);
                float e = u * u * (3f - 2f * u); // なめらかに始まって、なめらかに止まる
                SetCamera(Vector3.Lerp(fromPos, toPos, e), Mathf.Lerp(fromSize, toSize, e));
                yield return null;
            }
            SetCamera(toPos, toSize);
        }
    }
}
