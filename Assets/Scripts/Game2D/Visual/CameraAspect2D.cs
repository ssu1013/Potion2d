using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 画面の比率を、いつも 16:9 に保つ（ゲームを起動すると自動で働く。シーンに置く必要はない）。
    ///   横に長い画面（21:9 のウルトラワイドなど）… 左右に黒い帯（ピラーボックス）
    ///   縦に長い画面（16:10 の Steam Deck・4:3 など）… 上下に黒い帯（レターボックス）
    /// 絵は 16:9 で描いておけば、どの画面でも切れたり伸びたりしない。UI も同じ範囲に収まる（ViewportFit2D）。
    /// 黒い帯は、何も映さない「帯のカメラ」が黒で塗る。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class CameraAspect2D : MonoBehaviour
    {
        /// <summary>ゲームの絵の比率</summary>
        public const float TargetAspect = 16f / 9f;
        /// <summary>この割合より比率の差が小さいときは、帯を出さない（1920×1080 と 1366×768 など）</summary>
        public const float Tolerance = 0.004f;

        private static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);
        private static CameraAspect2D instance;

        /// <summary>絵が映っている範囲（画面全体を 0〜1 とした割合）</summary>
        public static Rect ViewRect { get; private set; } = Full;
        /// <summary>左右に帯が出ている（横に長い画面）</summary>
        public static bool Pillarbox { get; private set; }
        /// <summary>上下に帯が出ている（縦に長い画面）</summary>
        public static bool Letterbox { get; private set; }
        /// <summary>映る範囲が変わったとき（ウィンドウの大きさを変えたときなど）</summary>
        public static event Action Changed;

        private Camera target;
        private Camera bars;
        private int lastWidth = -1;
        private int lastHeight = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            ViewRect = Full;
            Pillarbox = false;
            Letterbox = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("ScreenAspect");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CameraAspect2D>();
        }

        /// <summary>画面の大きさ（画素）から、16:9 の絵が映る範囲を求める</summary>
        public static Rect Compute(float width, float height, float targetAspect = TargetAspect)
        {
            if (width <= 0f || height <= 0f) return Full;
            float aspect = width / height;
            if (Mathf.Abs(aspect - targetAspect) / targetAspect < Tolerance) return Full;
            if (aspect > targetAspect)
            {
                float w = targetAspect / aspect;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = aspect / targetAspect;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        /// <summary>画面の点（画素）が、絵の映っている範囲の中か（黒い帯の上のクリックを無視するため）</summary>
        public static bool ContainsScreenPoint(Vector2 point)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return true;
            Rect r = ViewRect;
            float x = point.x / Screen.width;
            float y = point.y / Screen.height;
            return x >= r.xMin && x <= r.xMax && y >= r.yMin && y <= r.yMax;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void Update()
        {
            Camera main = Camera.main;
            if (main != target)
            {
                target = main;
                lastWidth = -1;
            }
            if (target == null) return;
            if (Screen.width == lastWidth && Screen.height == lastHeight && target.rect == ViewRect) return;

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            Rect rect = Compute(Screen.width, Screen.height);
            bool changed = rect != ViewRect;
            ViewRect = rect;
            Pillarbox = rect.width < 0.999f;
            Letterbox = rect.height < 0.999f;
            target.rect = rect;
            EnsureBars();
            bars.enabled = Pillarbox || Letterbox;
            bars.depth = target.depth - 10f;
            if (changed && Changed != null) Changed();
        }

        private void EnsureBars()
        {
            if (bars != null) return;
            var go = new GameObject("BarsCamera");
            go.transform.SetParent(transform, false);
            bars = go.AddComponent<Camera>();
            bars.clearFlags = CameraClearFlags.SolidColor;
            bars.backgroundColor = Color.black;
            bars.cullingMask = 0;
            bars.orthographic = true;
            bars.rect = Full;
            bars.useOcclusionCulling = false;
            bars.allowHDR = false;
            bars.allowMSAA = false;
            UniversalAdditionalCameraData data = bars.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
            }
        }
    }
}
