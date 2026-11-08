using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// ランプ・ろうそくの光のゆらめき（Light2D と同じオブジェクトに付ける）。明るさと光の届く範囲を、ゆっくり不規則に揺らす。
    /// 「いっしょに揺らす炎の絵」を入れると、炎の大きさも合わせて揺れる。
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    public class LightFlicker2D : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("明るさの揺れ幅")] private float intensityAmount = 0.15f;
        [SerializeField, Range(0f, 1f), Tooltip("光の届く範囲の揺れ幅")] private float radiusAmount = 0.06f;
        [SerializeField, Min(0.1f), Tooltip("揺れる速さ")] private float speed = 2.4f;
        [SerializeField, Tooltip("いっしょに揺らす炎の絵（なくてもよい）")] private Transform flameArt;

        private Light2D lamp;
        private float baseIntensity;
        private float baseRadius;
        private Vector3 flameBaseScale = Vector3.one;
        private float seed;

        private void Awake()
        {
            lamp = GetComponent<Light2D>();
            baseIntensity = lamp.intensity;
            baseRadius = lamp.pointLightOuterRadius;
            if (flameArt != null) flameBaseScale = flameArt.localScale;
            seed = Random.value * 100f;
        }

        /// <summary>揺れの中心になる明るさを変える（暗くする演出など）</summary>
        public float BaseIntensity
        {
            get => baseIntensity;
            set => baseIntensity = value;
        }

        private void Update()
        {
            float t = Time.time * speed;
            float n = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
            float n2 = (Mathf.PerlinNoise(seed + 7.3f, t * 2.7f) - 0.5f) * 2f;
            float f = n * 0.7f + n2 * 0.3f;
            lamp.intensity = Mathf.Max(0f, baseIntensity * (1f + f * intensityAmount));
            lamp.pointLightOuterRadius = Mathf.Max(0.01f, baseRadius * (1f + f * radiusAmount));
            if (flameArt != null)
            {
                flameArt.localScale = new Vector3(flameBaseScale.x * (1f - f * 0.05f), flameBaseScale.y * (1f + f * 0.12f), flameBaseScale.z);
            }
        }
    }
}
