using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面モード（ボーダーレス・フルスクリーン・ウィンドウ）とウィンドウの大きさ。
    /// 選んだ画面モードと大きさは、Unity が自動で覚えていて、次に起動したときもそのまま（自前では保存しない）。
    /// エディタの再生中は切り替わらない（ビルドした製品版で反映される）。
    /// </summary>
    public static class ScreenModes
    {
        public enum Kind { Borderless, Fullscreen, Windowed }

        /// <summary>ウィンドウのときに選べる大きさ（16:9）。画面に収まらない大きさは選べない</summary>
        public static readonly Vector2Int[] WindowSizes =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440),
        };

        private static Kind pendingKind;
        private static int pendingSize = -1;
        private static float pendingTime = -10f;

        /// <summary>専有のフルスクリーンを選べるか（Windows だけ。ほかの OS ではボーダーレスとウィンドウ）</summary>
        public static bool SupportsFullscreen =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        public static Kind Current
        {
            get
            {
                // 切り替えた直後は、画面に反映されるまで少しかかるので、選んだ値を返す
                if (Time.unscaledTime - pendingTime < 1.5f) return pendingKind;
                switch (Screen.fullScreenMode)
                {
                    case FullScreenMode.ExclusiveFullScreen: return Kind.Fullscreen;
                    case FullScreenMode.Windowed: return Kind.Windowed;
                    default: return Kind.Borderless;
                }
            }
        }

        public static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.Fullscreen: return "フルスクリーン";
                case Kind.Windowed: return "ウィンドウ";
                default: return "ボーダーレス";
            }
        }

        /// <summary>次の画面モードへ（ボーダーレス → フルスクリーン → ウィンドウ → …）</summary>
        public static void Next()
        {
            Kind next;
            switch (Current)
            {
                case Kind.Borderless: next = SupportsFullscreen ? Kind.Fullscreen : Kind.Windowed; break;
                case Kind.Fullscreen: next = Kind.Windowed; break;
                default: next = Kind.Borderless; break;
            }
            Apply(next, WindowSizeIndex);
        }

        /// <summary>ウィンドウの大きさを次へ（ウィンドウのときだけ、すぐに変わる）</summary>
        public static void NextWindowSize()
        {
            int count = FittingSizeCount();
            int next = (WindowSizeIndex + 1) % count;
            if (Current == Kind.Windowed) Apply(Kind.Windowed, next);
            else
            {
                pendingKind = Current;
                pendingSize = next;
                pendingTime = Time.unscaledTime;
            }
        }

        public static int WindowSizeIndex
        {
            get
            {
                int count = FittingSizeCount();
                if (pendingSize >= 0) return Mathf.Min(pendingSize, count - 1);
                if (Screen.fullScreenMode == FullScreenMode.Windowed)
                {
                    // 今のウィンドウにいちばん近い大きさ
                    int best = 0;
                    int bestDiff = int.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        int diff = Mathf.Abs(WindowSizes[i].x - Screen.width);
                        if (diff < bestDiff) { best = i; bestDiff = diff; }
                    }
                    return best;
                }
                return DefaultSizeIndex();
            }
        }

        public static string WindowSizeLabel
        {
            get
            {
                Vector2Int s = WindowSizes[WindowSizeIndex];
                return s.x + " × " + s.y;
            }
        }

        private static void Apply(Kind kind, int sizeIndex)
        {
            pendingKind = kind;
            pendingSize = sizeIndex;
            pendingTime = Time.unscaledTime;
            int dw = Mathf.Max(640, Display.main.systemWidth);
            int dh = Mathf.Max(360, Display.main.systemHeight);
            switch (kind)
            {
                case Kind.Fullscreen:
                    Screen.SetResolution(dw, dh, FullScreenMode.ExclusiveFullScreen);
                    break;
                case Kind.Windowed:
                    Vector2Int s = WindowSizes[Mathf.Clamp(sizeIndex, 0, WindowSizes.Length - 1)];
                    Screen.SetResolution(s.x, s.y, FullScreenMode.Windowed);
                    break;
                default:
                    Screen.SetResolution(dw, dh, FullScreenMode.FullScreenWindow);
                    break;
            }
        }

        /// <summary>今のモニターに収まる大きさの数（最低 1 つ）</summary>
        private static int FittingSizeCount()
        {
            int dw = Display.main.systemWidth;
            int dh = Display.main.systemHeight;
            int count = 0;
            for (int i = 0; i < WindowSizes.Length; i++)
            {
                // タスクバーとタイトルバーのぶん、少し余裕を見る
                if (WindowSizes[i].x <= dw && WindowSizes[i].y <= dh - 60) count = i + 1;
            }
            return Mathf.Max(1, count);
        }

        private static int DefaultSizeIndex()
        {
            int dw = Display.main.systemWidth;
            int dh = Display.main.systemHeight;
            int best = 0;
            for (int i = 0; i < WindowSizes.Length; i++)
            {
                if (WindowSizes[i].x <= dw * 0.85f && WindowSizes[i].y <= dh * 0.85f) best = i;
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pendingSize = -1;
            pendingTime = -10f;
        }
    }
}
