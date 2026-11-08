using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 液体の量（液面の高さ）と色を、ToonLiquid シェーダーの液体に伝える。液体のメッシュに付ける。
    /// メッシュは「容器の内側いっぱいの閉じた形」で作り、下端・上端の高さ（このオブジェクトの座標）を指定する。
    /// 液面はワールド座標で水平に切られるので、瓶を傾けても液面は水平のまま。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class LiquidFill : MonoBehaviour
    {
        private static readonly int FillId = Shader.PropertyToID("_FillWorldY");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceFxId = Shader.PropertyToID("_SurfaceFx");
        private static readonly int SwirlId = Shader.PropertyToID("_Swirl");
        private static readonly int BoilId = Shader.PropertyToID("_Boil");
        private static readonly int[] RippleIds =
        {
            Shader.PropertyToID("_Ripple0"), Shader.PropertyToID("_Ripple1"),
            Shader.PropertyToID("_Ripple2"), Shader.PropertyToID("_Ripple3"),
        };

        [SerializeField, Tooltip("液体が空のときの高さ（このオブジェクトの座標）")] private float bottom = 0f;
        [SerializeField, Tooltip("液体が満タンのときの高さ（このオブジェクトの座標）")] private float top = 0.2f;
        [SerializeField, Range(0f, 1f)] private float amount = 1f;
        [SerializeField, Tooltip("動く物（手に持つ瓶など）は毎フレーム液面を計算し直す")] private bool followMovement;

        private Renderer target;
        private MaterialPropertyBlock block;
        private bool hasColor;
        private Color color = Color.white;
        private Color emission = Color.black;
        private bool hasSurfaceFx;
        private Vector4 surfaceFx, swirl, boil;
        private readonly Vector4[] ripples = new Vector4[4];
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        public float Amount => amount;

        public void Configure(float bottom, float top, bool followMovement)
        {
            this.bottom = bottom;
            this.top = top;
            this.followMovement = followMovement;
        }

        public void SetFollowMovement(bool on)
        {
            followMovement = on;
        }

        /// <summary>指定した量のときの液面の中心（ワールド座標）。落とした欠片や泡の行き先に使う</summary>
        public Vector3 SurfacePoint(float atAmount)
        {
            return transform.TransformPoint(new Vector3(0f, Mathf.Lerp(bottom, top, Mathf.Clamp01(atAmount)), 0f));
        }

        private void Awake()
        {
            Init();
            Apply();
        }

        private void Init()
        {
            if (target != null) return;
            target = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        /// <summary>量（0〜1）</summary>
        public void SetAmount(float value)
        {
            amount = Mathf.Clamp01(value);
            Apply();
        }

        /// <summary>色（光る強さは色に対する割合）</summary>
        public void SetColor(Color value, float emissionStrength)
        {
            hasColor = true;
            color = value;
            color.a = 1f;
            emission = color * emissionStrength;
            emission.a = 1f;
            Apply();
        }

        /// <summary>
        /// 液面の動き（調合フラスコ用。ToonLiquid シェーダー）。
        /// surface: 液面の中心（x, z）と半径（w）。swirl: 渦の回った角度・強さ（0〜1）・向き（+1 反時計回り）。
        /// boil: 沸騰の強さ（0〜1）・焦げ（0〜1）。ripples: 波紋（x, z, 経った秒数, 強さ）を4つまで
        /// </summary>
        public void SetSurfaceEffects(Vector4 surface, Vector4 swirlParams, Vector4 boilParams, Vector4[] rippleParams)
        {
            hasSurfaceFx = true;
            surfaceFx = surface;
            swirl = swirlParams;
            boil = boilParams;
            for (int i = 0; i < ripples.Length; i++) ripples[i] = rippleParams != null && i < rippleParams.Length ? rippleParams[i] : Vector4.zero;
            Apply();
        }

        private void LateUpdate()
        {
            if (!followMovement) return;
            if (transform.position == lastPosition && transform.rotation == lastRotation) return;
            Apply();
        }

        private void Apply()
        {
            Init();
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            bool visible = amount > 0.001f;
            if (target.enabled != visible) target.enabled = visible;
            if (!visible) return;

            target.GetPropertyBlock(block);
            // 液面の高さ：満タンの近くでは少しだけ上に余裕を持たせて、上面が切れないようにする
            float local = amount >= 0.999f ? top + (top - bottom) * 2f : Mathf.Lerp(bottom, top, amount);
            block.SetFloat(FillId, transform.TransformPoint(new Vector3(0f, local, 0f)).y);
            if (hasColor)
            {
                block.SetColor(ColorId, color);
                block.SetColor(EmissionId, emission);
            }
            if (hasSurfaceFx)
            {
                block.SetVector(SurfaceFxId, surfaceFx);
                block.SetVector(SwirlId, swirl);
                block.SetVector(BoilId, boil);
                for (int i = 0; i < ripples.Length; i++) block.SetVector(RippleIds[i], ripples[i]);
            }
            target.SetPropertyBlock(block);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }
    }
}
