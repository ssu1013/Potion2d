using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>ランタンやろうそくの灯りを、ゆらゆらと不規則に揺らす（炎の形も一緒に揺らせる）</summary>
    public class LightFlicker : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.6f)] private float amount = 0.15f;
        [SerializeField] private float speed = 2.2f;
        [SerializeField, Tooltip("一緒に揺らす炎の見た目（なくてもよい）")] private Transform flame;

        private Light target;
        private float baseIntensity = 1f;
        private float seed;
        private Vector3 flameScale = Vector3.one;
        private Vector3 flamePosition;

        /// <summary>明るさの倍率（調合台に向かっている間、部屋を暗くするのに使う）</summary>
        public float IntensityScale { get; set; } = 1f;

        public void Configure(float amount, float speed, Transform flame)
        {
            this.amount = amount;
            this.speed = speed;
            this.flame = flame;
        }

        private void Awake()
        {
            target = GetComponent<Light>();
            if (target != null) baseIntensity = target.intensity;
            seed = Random.Range(0f, 100f);
            if (flame != null)
            {
                flameScale = flame.localScale;
                flamePosition = flame.localPosition;
            }
        }

        private void Update()
        {
            float t = Time.time * speed;
            // ゆっくりした揺れ＋細かいちらつき
            float slow = Mathf.PerlinNoise(seed, t) * 2f - 1f;
            float fast = Mathf.PerlinNoise(seed + 13.7f, t * 3.1f) * 2f - 1f;
            float k = 1f + (slow * 0.7f + fast * 0.3f) * amount;
            if (target != null) target.intensity = baseIntensity * k * IntensityScale;

            if (flame != null)
            {
                float sway = (Mathf.PerlinNoise(seed + 31.1f, t * 1.7f) - 0.5f) * 0.25f;
                float h = 1f + (k - 1f) * 1.6f;
                flame.localScale = new Vector3(flameScale.x * (2f - h) * 0.5f + flameScale.x * 0.5f, flameScale.y * h, flameScale.z * (2f - h) * 0.5f + flameScale.z * 0.5f);
                flame.localPosition = flamePosition + new Vector3(sway * flameScale.x, 0f, 0f);
            }
        }
    }
}
