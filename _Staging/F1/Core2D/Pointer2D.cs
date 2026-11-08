using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// マウスの位置とボタン（部屋・調合台の操作はここから読む）。
    /// ふだんは Input のマウスそのまま。エディタの自動テスト（AtelierAutoTest2D）のときだけ、テストが「指」の代わりになる。
    /// </summary>
    public static class Pointer2D
    {
        private static bool simulated;
        private static Vector2 simPosition;
        private static bool simHeld;
        private static int pressFrame = -1;
        private static int rightFrame = -1;

        /// <summary>自動テストがマウスの代わりをしている</summary>
        public static bool Simulated => simulated;
        public static Vector2 Position => simulated ? simPosition : (Vector2)Input.mousePosition;
        public static bool LeftPressed => simulated ? pressFrame == Time.frameCount : Input.GetMouseButtonDown(0);
        public static bool LeftHeld => simulated ? simHeld : Input.GetMouseButton(0);
        public static bool RightPressed => simulated ? rightFrame == Time.frameCount : Input.GetMouseButtonDown(1);
        public static float Scroll => simulated ? 0f : Input.mouseScrollDelta.y;
        /// <summary>カーソルが、絵の映っている範囲（黒い帯の外）にある</summary>
        public static bool InView => CameraAspect2D.ContainsScreenPoint(Position);

        // ------------------------------------------------------------------
        // 自動テスト用（コルーチンから呼ぶと、次のフレームの Update で押されたことになる）
        // ------------------------------------------------------------------

        public static void BeginSimulation(Vector2 position)
        {
            simulated = true;
            simPosition = position;
            simHeld = false;
        }

        public static void Move(Vector2 position)
        {
            simPosition = position;
        }

        public static void Press()
        {
            if (!simHeld) pressFrame = Time.frameCount + 1;
            simHeld = true;
        }

        public static void Release()
        {
            simHeld = false;
        }

        public static void RightClick()
        {
            rightFrame = Time.frameCount + 1;
        }

        public static void EndSimulation()
        {
            simulated = false;
            simHeld = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            simulated = false;
            simHeld = false;
            pressFrame = -1;
            rightFrame = -1;
        }
    }
}
