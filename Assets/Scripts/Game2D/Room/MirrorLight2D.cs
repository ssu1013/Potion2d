using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 部屋の作業台のフラスコの光（Light2D と同じオブジェクトに付ける）。調合台のフラスコの液体の光（Flask2D の LiquidGlow）の
    /// 色と明るさをまねるので、部屋に戻っても、作ったポーションがボワッと光って見える。
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    public class MirrorLight2D : MonoBehaviour
    {
        [SerializeField, Range(0f, 2f), Tooltip("まねる明るさの割合")] private float intensityScale = 0.7f;

        private Light2D target;
        private Light2D source;

        private void Awake()
        {
            target = GetComponent<Light2D>();
        }

        private void Start()
        {
            Flask2D flask = FindFirstObjectByType<Flask2D>();
            source = flask != null ? flask.Glow : null;
        }

        private void LateUpdate()
        {
            float intensity = source != null && source.enabled ? source.intensity * intensityScale : 0f;
            target.intensity = intensity;
            if (source != null) target.color = source.color;
            bool on = intensity > 0.01f;
            if (target.enabled != on) target.enabled = on;
        }
    }
}
