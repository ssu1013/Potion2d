using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>タイトル画面でアトリエをゆっくり見回すカメラ（いくつかの視点の間をなめらかに往復）</summary>
    public class TitleCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform[] viewpoints = new Transform[0];
        [SerializeField, Tooltip("1区間にかける秒数")] private float segmentSeconds = 14f;

        private float time;

        public void Configure(Transform[] viewpoints)
        {
            this.viewpoints = viewpoints;
        }

        private void OnEnable()
        {
            time = 0f;
            Apply();
        }

        private void Update()
        {
            time += Time.unscaledDeltaTime;
            Apply();
        }

        private void Apply()
        {
            if (viewpoints == null || viewpoints.Length == 0) return;
            if (viewpoints.Length == 1)
            {
                transform.SetPositionAndRotation(viewpoints[0].position, viewpoints[0].rotation);
                return;
            }

            // 0→1→…→n-1→…→0 と往復する
            int segments = viewpoints.Length - 1;
            float cycle = segments * 2f * segmentSeconds;
            float t = Mathf.Repeat(time, cycle) / segmentSeconds;
            if (t > segments) t = segments * 2f - t;
            int i = Mathf.Min(Mathf.FloorToInt(t), segments - 1);
            float k = t - i;
            k = k * k * (3f - 2f * k);
            Transform a = viewpoints[i];
            Transform b = viewpoints[i + 1];
            Vector3 sway = new Vector3(Mathf.Sin(time * 0.31f), Mathf.Sin(time * 0.23f) * 0.5f, 0f) * 0.03f;
            transform.SetPositionAndRotation(Vector3.Lerp(a.position, b.position, k) + sway, Quaternion.Slerp(a.rotation, b.rotation, k));
        }
    }
}
