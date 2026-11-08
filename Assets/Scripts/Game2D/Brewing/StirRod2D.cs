using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// フラスコに差したガラス棒（StirRod の Prefab のルートに付ける）。かき混ぜた回数に合わせて、先が液の中で左右に回る。
    /// 絵を差し替えるところ：Art（縦長の棒の絵。真ん中が回転の中心）。
    /// </summary>
    public class StirRod2D : MonoBehaviour
    {
        [SerializeField, Tooltip("棒の絵（縦長）")] private SpriteRenderer rod;
        [SerializeField, Min(0f), Tooltip("液の中で先が動く幅（ワールド単位）")] private float swayWidth = 0.55f;
        [SerializeField, Tooltip("棒の先の、液体の底からの高さ")] private float tipAboveBottom = 0.35f;
        [SerializeField, Tooltip("棒の上の端の、フラスコの口からの位置")] private Vector2 topAboveMouth = new Vector2(0.28f, 1.1f);

        private Flask2D flask;
        private BrewSession2D session;
        private float phase;
        private float lastTurns;
        private float sway;

        private void Start()
        {
            flask = FindFirstObjectByType<Flask2D>();
            session = FindFirstObjectByType<BrewSession2D>();
            if (session != null && session.Flask != null) lastTurns = session.Flask.TotalTurns;
        }

        private void LateUpdate()
        {
            if (flask == null || rod == null) return;
            float dt = Time.deltaTime;
            float turns = session != null && session.Flask != null ? session.Flask.TotalTurns : 0f;
            float delta = Mathf.Max(0f, turns - lastTurns);
            lastTurns = turns;
            bool clockwise = session == null || session.LastStirClockwise;
            phase += delta * Mathf.PI * 2f * (clockwise ? 1f : -1f);
            float active = session != null ? Mathf.Clamp01(session.StirSpeed / 1.5f) : 0f;
            sway = Mathf.Lerp(sway, active, 1f - Mathf.Exp(-6f * dt));

            Vector3 bottom = flask.BottomWorld;
            Vector3 mouth = flask.MouthWorld;
            float offset = Mathf.Sin(phase) * swayWidth * Mathf.Max(0.15f, sway);
            Vector3 tip = new Vector3(bottom.x + offset, bottom.y + tipAboveBottom, 0f);
            Vector3 top = new Vector3(mouth.x + topAboveMouth.x - offset * 0.25f, mouth.y + topAboveMouth.y, 0f);
            Vector3 dir = top - tip;
            float length = dir.magnitude;
            Vector2 size = rod.sprite != null ? (Vector2)rod.sprite.bounds.size : Vector2.one;
            Transform t = rod.transform;
            t.position = (tip + top) * 0.5f;
            t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            float s = length / Mathf.Max(0.001f, size.y);
            t.localScale = new Vector3(s, s, 1f);
        }
    }
}
