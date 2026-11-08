using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面の仕上げ（ブルーム・暖かい色調整・周辺減光）。カメラに付ける。
    /// Built-in レンダーパイプラインの OnRenderImage を使う。カメラの HDR を有効にすると、炎や光る液体だけがにじむ。
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Camera)), ImageEffectAllowedInSceneView]
    public class AtelierPostFX : MonoBehaviour
    {
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int BloomIntensityId = Shader.PropertyToID("_BloomIntensity");
        private static readonly int GradeId = Shader.PropertyToID("_Grade");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");
        private static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        private static readonly int DofTexId = Shader.PropertyToID("_DofTex");
        private static readonly int DofParamsId = Shader.PropertyToID("_DofParams");
        private static readonly int DofAmountId = Shader.PropertyToID("_DofAmount");
        private static readonly int DofFocusAId = Shader.PropertyToID("_DofFocusA");
        private static readonly int DofFocusBId = Shader.PropertyToID("_DofFocusB");

        [SerializeField] private Shader shader;

        [Header("ブルーム")]
        [SerializeField, Tooltip("この明るさを超えた所がにじむ")] private float threshold = 1.25f;
        [SerializeField, Range(0f, 1f)] private float softKnee = 0.5f;
        [SerializeField, Range(0f, 3f)] private float bloomIntensity = 0.4f;
        [SerializeField, Range(1, 8)] private int iterations = 5;

        [Header("色")]
        [SerializeField, Range(0f, 2f)] private float saturation = 1.04f;
        [SerializeField, Range(0.5f, 1.5f)] private float contrast = 1.05f;
        [SerializeField] private Color tint = new Color(1.02f, 1f, 0.97f, 1f);

        [Header("周辺減光")]
        [SerializeField, Range(0f, 1f)] private float vignette = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("中心からどのくらい離れたら暗くし始めるか")] private float vignetteStart = 0.45f;
        [SerializeField] private Color vignetteColor = new Color(0.3f, 0.18f, 0.12f, 1f);

        [Header("被写界深度（調合台に向かっているとき。BrewMode が設定する）")]
        [SerializeField, Range(0f, 1f)] private float dofAmount;
        [SerializeField, Tooltip("くっきり見える範囲の手前（m）")] private float focusNear = 0.6f;
        [SerializeField, Tooltip("くっきり見える範囲の奥（m）")] private float focusFar = 1.3f;
        [SerializeField, Tooltip("手前がぼけきるまでの幅（m）")] private float nearRamp = 0.25f;
        [SerializeField, Tooltip("奥がぼけきるまでの幅（m）")] private float farRamp = 0.9f;
        [SerializeField, Range(0f, 1f), Tooltip("ぼけた奥を暗くする割合（1 = 暗くしない）")] private float farDarken = 0.55f;
        [SerializeField, Range(1f, 12f), Tooltip("ぼかしの大きさ")] private float dofRadius = 7f;

        private Material material;
        private Camera cam;

        /// <summary>被写界深度を設定する（amount = 0 で無効）。深さの画像を使うので、有効な間はカメラに深さを描かせる</summary>
        public void SetFocus(float amount, float near, float far, float darken)
        {
            dofAmount = Mathf.Clamp01(amount);
            focusNear = near;
            focusFar = far;
            farDarken = Mathf.Clamp01(darken);
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null && dofAmount > 0.001f) cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        public bool DofActive => dofAmount > 0.001f && enabled;

        private Vector4 focusZoneA, focusZoneB;

        /// <summary>
        /// 深さに関係なくくっきり見せる、画面の楕円を2つまで（xy: 中心、zw: 半径。どちらも画面の幅・高さに対する割合。zw = 0 で無し）。
        /// ガラスは深さを書かないので、フラスコや手に持った瓶の所をここで指定する
        /// </summary>
        public void SetFocusZones(Vector4 a, Vector4 b)
        {
            focusZoneA = a;
            focusZoneB = b;
        }
        private readonly RenderTexture[] textures = new RenderTexture[8];

        public void Configure(Shader shader)
        {
            this.shader = shader;
        }

        /// <summary>ブルームの段数（少ないほど軽い）</summary>
        public void SetBloomIterations(int count)
        {
            iterations = Mathf.Clamp(count, 1, 8);
        }

        private void OnDisable()
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
                material = null;
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            float knee = Mathf.Max(threshold * softKnee, 0.0001f);
            material.SetVector(ThresholdId, new Vector4(threshold, threshold - knee, 2f * knee, 0.25f / knee));
            material.SetFloat(BloomIntensityId, bloomIntensity);
            material.SetVector(GradeId, new Vector4(saturation, contrast, vignette, vignetteStart));
            material.SetColor(TintId, tint);
            material.SetColor(VignetteColorId, vignetteColor);

            RenderTextureFormat format = source.format;
            int width = Mathf.Max(1, source.width / 2);
            int height = Mathf.Max(1, source.height / 2);

            RenderTexture current = textures[0] = RenderTexture.GetTemporary(width, height, 0, format);
            Graphics.Blit(source, current, material, 0);
            RenderTexture currentSource = current;

            int i = 1;
            for (; i < iterations; i++)
            {
                width /= 2;
                height /= 2;
                if (height < 2 || width < 2) break;
                current = textures[i] = RenderTexture.GetTemporary(width, height, 0, format);
                Graphics.Blit(currentSource, current, material, 1);
                currentSource = current;
            }

            for (i -= 2; i >= 0; i--)
            {
                current = textures[i];
                textures[i] = null;
                Graphics.Blit(currentSource, current, material, 2);
                RenderTexture.ReleaseTemporary(currentSource);
                currentSource = current;
            }

            // 被写界深度（縮小画像でぼかしておき、仕上げで深さに合わせて混ぜる）
            RenderTexture dofBlur = null;
            material.SetVector(DofParamsId, new Vector4(focusNear, focusFar, nearRamp, farRamp));
            material.SetVector(DofAmountId, new Vector4(dofAmount, farDarken, dofRadius, 0f));
            material.SetVector(DofFocusAId, focusZoneA);
            material.SetVector(DofFocusBId, focusZoneB);
            if (dofAmount > 0.001f)
            {
                int dw = Mathf.Max(1, source.width / 2);
                int dh = Mathf.Max(1, source.height / 2);
                // ぼけの量を α に入れるので、α のある形式にする
                RenderTextureFormat dofFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                RenderTexture half = RenderTexture.GetTemporary(dw, dh, 0, dofFormat);
                Graphics.Blit(source, half, material, 4);
                dofBlur = RenderTexture.GetTemporary(dw, dh, 0, dofFormat);
                Graphics.Blit(half, dofBlur, material, 5);
                RenderTexture.ReleaseTemporary(half);
                material.SetTexture(DofTexId, dofBlur);
            }

            material.SetTexture(BloomTexId, currentSource);
            Graphics.Blit(source, destination, material, 3);
            RenderTexture.ReleaseTemporary(currentSource);
            if (dofBlur != null) RenderTexture.ReleaseTemporary(dofBlur);
        }
    }
}
