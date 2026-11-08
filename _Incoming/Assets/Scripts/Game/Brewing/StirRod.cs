using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// フラスコに差したガラス棒。フラスコの口を支点にして、先（Tip）がフラスコの底の近くで円を描く。
    /// かき混ぜた向きと回数（BrewingStation.Stir）に合わせて回り、調合台ではマウスの位置に先がついてくる。
    /// モデルは先端が原点、棒が +Y 方向に伸びる形にする（自作モデルに差し替えられる）。
    /// </summary>
    public class StirRod : MonoBehaviour
    {
        [SerializeField, Tooltip("口から先端までの深さ（フラスコの高さに対する割合）")] private float tipDepth = 0.82f;
        [SerializeField, Tooltip("先端が描く円の半径（フラスコの底の半径に対する割合）")] private float tipRadius = 0.62f;

        private float angle = -Mathf.PI * 0.5f; // 手前
        private float shownAngle = -Mathf.PI * 0.5f;
        private float wobble;

        /// <summary>今の先端の向き（フラスコの軸のまわりの角度。上から見て反時計回りが +）</summary>
        public float Angle => angle;

        /// <summary>かき混ぜた分だけ回す（上から見て時計回りなら角度が減る）</summary>
        public void Advance(float turns, bool clockwise)
        {
            angle += (clockwise ? -1f : 1f) * turns * Mathf.PI * 2f;
            wobble = 1f;
        }

        /// <summary>先端の向きを直接決める（調合台でマウスに合わせる）</summary>
        public void SetAngle(float radians)
        {
            // 前の角度から近い方へ（2π の飛びを作らない）
            float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, radians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            angle += delta;
            wobble = 1f;
        }

        /// <summary>フラスコの口・底・底の半径から、棒の位置と向きを決める（毎フレーム呼ぶ）</summary>
        public void UpdatePose(Vector3 mouth, Vector3 bottom, float bottomRadius, float deltaTime)
        {
            float k = 1f - Mathf.Exp(-18f * deltaTime);
            shownAngle = Mathf.Lerp(shownAngle, angle, k);
            wobble = Mathf.MoveTowards(wobble, 0f, deltaTime * 1.5f);

            float depth = (mouth.y - bottom.y) * tipDepth;
            float radius = bottomRadius * tipRadius * (0.92f + 0.08f * Mathf.Sin(Time.time * 7f) * wobble);
            Vector3 axisPoint = new Vector3(mouth.x, mouth.y - depth, mouth.z);
            Vector3 tip = axisPoint + new Vector3(Mathf.Cos(shownAngle), 0f, Mathf.Sin(shownAngle)) * radius;
            Vector3 up = (mouth - tip).normalized;
            transform.position = tip;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        }
    }
}
