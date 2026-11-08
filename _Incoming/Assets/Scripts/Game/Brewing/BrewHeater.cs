using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 加熱台（フラスコの下）。火加減に合わせて、つまみが回り、のぞき窓が赤く光り、炎の大きさと灯りが変わる。
    /// 調合台に向かっているときは、つまみをマウスでドラッグ（またはホイール）して火加減を変えられる（BrewMode が操作する）。
    /// 自作モデルに差し替えるときは、つまみに「Knob」、炎に「Flame」で始まる名前、光る部分に「Glow」で始まる名前を付ける。
    /// </summary>
    public class BrewHeater : MonoBehaviour
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Tooltip("火加減のつまみ（空なら子の「Knob」を探す）")] private Transform knob;
        [SerializeField, Tooltip("火加減が最大のときのつまみの回転（度）")] private float knobMaxAngle = 270f;
        [SerializeField, Tooltip("のぞき窓・炭などの光る部分の、最大の光の色")] private Color glowColor = new Color(2.2f, 0.9f, 0.25f);
        [SerializeField, Tooltip("加熱台の灯り（空なら子から探す）")] private Light glowLight;
        [SerializeField, Min(0f)] private float maxLightIntensity = 1.1f;

        private Transform[] flames = new Transform[0];
        private Vector3[] flameScales = new Vector3[0];
        private Renderer[] glows = new Renderer[0];
        private MaterialPropertyBlock block;
        private Quaternion knobRest;
        private float target;
        private float shown = -1f;
        private float flicker;
        private Vector3 knobScale = Vector3.one;
        private float highlight;

        /// <summary>つまみ（マウスで操作する所）</summary>
        public Transform Knob => knob;
        /// <summary>表示中の火加減（0〜1）</summary>
        public float Level => target;
        /// <summary>マウスで指している・回している（つまみが少し大きくなる）</summary>
        public bool Highlighted { get; set; }

        private void Awake()
        {
            if (knob == null) knob = PrefabAnchors.Find(transform, "Knob");
            if (knob != null)
            {
                knobRest = knob.localRotation;
                knobScale = knob.localScale;
            }
            if (glowLight == null) glowLight = GetComponentInChildren<Light>(true);
            // 灯りが多い所でも必ず照らす（頂点ライトにされると、フラスコが下から赤く照らされなくなる）
            if (glowLight != null) glowLight.renderMode = LightRenderMode.ForcePixel;

            var flameList = new System.Collections.Generic.List<Transform>();
            var glowList = new System.Collections.Generic.List<Renderer>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Flame")) flameList.Add(t);
                if (t.name.StartsWith("Glow"))
                {
                    var r = t.GetComponent<Renderer>();
                    if (r != null) glowList.Add(r);
                }
            }
            flames = flameList.ToArray();
            flameScales = new Vector3[flames.Length];
            for (int i = 0; i < flames.Length; i++) flameScales[i] = flames[i].localScale;
            glows = glowList.ToArray();
            block = new MaterialPropertyBlock();
            Apply(0f);
        }

        /// <summary>火加減（0〜1）を見た目に反映する</summary>
        public void SetLevel(float level)
        {
            target = Mathf.Clamp01(level);
        }

        private void Update()
        {
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            float next = Mathf.Lerp(Mathf.Max(0f, shown), target, k);
            if (Mathf.Abs(next - target) < 0.002f) next = target;
            flicker += Time.deltaTime * 9f;
            float h = Mathf.MoveTowards(highlight, Highlighted ? 1f : 0f, Time.deltaTime * 8f);
            if (knob != null && !Mathf.Approximately(h, highlight)) knob.localScale = knobScale * (1f + 0.15f * h);
            highlight = h;
            if (!Mathf.Approximately(next, shown) || target > 0.001f) Apply(next);
        }

        private void Apply(float level)
        {
            shown = level;
            if (knob != null) knob.localRotation = knobRest * Quaternion.Euler(0f, 0f, -level * knobMaxAngle);

            float wave = 1f + (Mathf.PerlinNoise(flicker, 0.37f) - 0.5f) * 0.35f;
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null) continue;
                bool on = level > 0.01f;
                if (flames[i].gameObject.activeSelf != on) flames[i].gameObject.SetActive(on);
                if (!on) continue;
                float h = Mathf.Lerp(0.35f, 1.25f, level) * (1f + (Mathf.PerlinNoise(flicker + i * 3.1f, 1.7f) - 0.5f) * 0.5f);
                Vector3 s = flameScales[i];
                flames[i].localScale = new Vector3(s.x * Mathf.Lerp(0.6f, 1f, level), s.y * h, s.z * Mathf.Lerp(0.6f, 1f, level));
            }

            Color glow = glowColor * (level * wave);
            foreach (Renderer r in glows)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, glow);
                r.SetPropertyBlock(block);
            }

            if (glowLight != null)
            {
                bool lit = level > 0.01f;
                if (glowLight.enabled != lit) glowLight.enabled = lit;
                if (lit) glowLight.intensity = maxLightIntensity * level * wave;
            }
        }
    }
}
