using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 加熱台と火加減のつまみ（Heater の Prefab のルートに付ける）。火加減の値は BrewSession2D が持ち、ここは見た目だけ。
    /// つまみの操作は BrewInput2D が判定する（当たり判定は「Knob」の Collider2D）：クリックで火を点ける／消す、ドラッグ・ホイールで細かく。
    ///
    /// 絵を差し替えるところ：
    ///   Art/Stand … 加熱台 ／ Art/Flame_0〜 … 炎（白〜黄色で描く。火加減で大きさと色が変わる）
    ///   Knob/KnobBase … つまみの台 ／ Knob/KnobDial … 回る部分（つまみの向きが上のときが「消す」より少し左）
    /// </summary>
    public class HeaterKnob2D : MonoBehaviour
    {
        [Header("つまみ")]
        [SerializeField, Tooltip("回る部分（KnobDial）")] private Transform knobDial;
        [SerializeField, Tooltip("「消す」のときの角度")] private float offAngle = 135f;
        [SerializeField, Tooltip("「強火」のときの角度")] private float maxAngle = -135f;

        [Header("炎（なくてもよい）")]
        [SerializeField] private List<SpriteRenderer> flames = new List<SpriteRenderer>();
        [SerializeField] private Color flameLowColor = new Color(0.35f, 0.55f, 1f, 0.9f);
        [SerializeField] private Color flameHighColor = new Color(1f, 0.62f, 0.2f, 1f);
        [SerializeField, Min(0f)] private float flameMinScale = 0.35f;
        [SerializeField, Min(0f)] private float flameMaxScale = 1.15f;

        [Header("加熱台の光")]
        [SerializeField, Tooltip("フラスコの下から照らすオレンジの光")] private Light2D heaterLight;
        [SerializeField, Min(0f)] private float lightMax = 2.2f;

        private readonly List<Vector3> flameBaseScales = new List<Vector3>();
        private BrewSession2D session;
        private float shown;
        private float seed;

        /// <summary>今の見た目の火加減（0〜1）</summary>
        public float ShownLevel => shown;

        private void Awake()
        {
            seed = Random.value * 50f;
            foreach (SpriteRenderer f in flames) flameBaseScales.Add(f != null ? f.transform.localScale : Vector3.one);
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
        }

        private void Update()
        {
            float level = session != null ? session.HeatLevel : 0f;
            shown = Mathf.MoveTowards(shown, level, Time.deltaTime * 3f);
            if (knobDial != null) knobDial.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(offAngle, maxAngle, level));

            float t = Time.time;
            bool on = shown > 0.01f;
            for (int i = 0; i < flames.Count; i++)
            {
                SpriteRenderer f = flames[i];
                if (f == null) continue;
                f.enabled = on;
                if (!on) continue;
                float n = Mathf.PerlinNoise(seed + i * 3.1f, t * 6f) - 0.5f;
                float s = Mathf.Lerp(flameMinScale, flameMaxScale, shown) * (1f + n * 0.35f);
                Vector3 b = flameBaseScales[i];
                f.transform.localScale = new Vector3(b.x * (0.85f + shown * 0.15f) * (1f - n * 0.1f), b.y * s, b.z);
                f.color = Color.Lerp(flameLowColor, flameHighColor, Mathf.SmoothStep(0f, 1f, shown));
            }

            if (heaterLight != null)
            {
                float flicker = 1f + (Mathf.PerlinNoise(seed, t * 5f) - 0.5f) * 0.3f;
                heaterLight.intensity = shown * lightMax * flicker;
                heaterLight.color = Color.Lerp(new Color(1f, 0.7f, 0.45f), new Color(1f, 0.5f, 0.18f), shown);
                bool lightOn = heaterLight.intensity > 0.01f;
                if (heaterLight.enabled != lightOn) heaterLight.enabled = lightOn;
            }
        }
    }
}
