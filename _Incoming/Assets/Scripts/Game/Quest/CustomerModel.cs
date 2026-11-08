using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// お客さんの自作モデル（Prefab）のルートに付ける目印。付けなくても、子の名前（Body・Head・Arm_L・Arm_R・FaceAnchor）から探す。
    ///   ・体・頭・腕 … 簡単な動き（歩くと上下に揺れる・腕を振る・プレイヤーの方を見る）をコードで付ける部品
    ///   ・FaceAnchor … 会話ウィンドウの顔アップのカメラが向く位置（顔の中心、+Z が顔の正面）
    ///   ・Animator … 指定すると「Speed」（歩く速さ 0〜1）と「Mood」（0なし 1驚き 2待つ 3喜ぶ 4悲しむ）を毎フレーム渡す
    /// 足元を原点、前を +Z にして作る。大きさは 1 = 1m（3頭身で身長 1.4m 前後）。
    /// </summary>
    public class CustomerModel : MonoBehaviour
    {
        public Transform body;
        public Transform head;
        public Transform leftArm;
        public Transform rightArm;
        [Tooltip("顔の中心（+Z が顔の正面）")] public Transform faceAnchor;
        [Tooltip("アニメーションを付けたモデルなら指定する")] public Animator animator;
        [Tooltip("体の上下・腕振り・首振りをコードで付ける（Animator で動かすなら切る）")] public bool proceduralMotion = true;

        /// <summary>空欄の部品を名前から探す</summary>
        public void AutoFill()
        {
            if (body == null) body = PrefabAnchors.Find(transform, "Body");
            if (head == null) head = PrefabAnchors.Find(transform, "Head");
            if (leftArm == null) leftArm = PrefabAnchors.Find(transform, "Arm_L");
            if (rightArm == null) rightArm = PrefabAnchors.Find(transform, "Arm_R");
            if (faceAnchor == null) faceAnchor = PrefabAnchors.Find(transform, PrefabAnchors.FaceAnchor);
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }
    }
}
