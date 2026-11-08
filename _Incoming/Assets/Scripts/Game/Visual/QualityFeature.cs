using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画質の設定に合わせて、飾りの演出（ほこり・湯気・小さな灯り）を切り替える目印。
    /// 「影だけ」を指定すると、オブジェクトは残したままライトの影の有無だけを切り替える。
    /// </summary>
    public class QualityFeature : MonoBehaviour
    {
        private static readonly List<QualityFeature> all = new List<QualityFeature>();

        [SerializeField, Range(0, 2), Tooltip("この画質以上で有効（0=軽量 / 1=標準 / 2=高）")] private int minLevel = 1;
        [SerializeField, Tooltip("オンにすると、表示はそのままでライトの影だけを切り替える")] private bool shadowsOnly;

        public void Configure(int minLevel, bool shadowsOnly)
        {
            this.minLevel = minLevel;
            this.shadowsOnly = shadowsOnly;
        }

        private void Awake()
        {
            if (!all.Contains(this)) all.Add(this);
            Apply(GameSettings.GraphicsQuality);
        }

        private void OnDestroy()
        {
            all.Remove(this);
        }

        private void Apply(int level)
        {
            bool on = level >= minLevel;
            if (shadowsOnly)
            {
                var light = GetComponent<Light>();
                if (light != null) light.shadows = on ? LightShadows.Soft : LightShadows.None;
                return;
            }
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        /// <summary>登録されているすべての演出に画質を反映する（非表示のものも含む）</summary>
        public static void ApplyAll(int level)
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) all.RemoveAt(i);
                else all[i].Apply(level);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();
        }
    }
}
