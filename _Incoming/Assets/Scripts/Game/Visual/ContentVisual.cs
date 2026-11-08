using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 容器の「中身」の色と量を変える（素材の容器・ポーションの瓶・飾りの瓶で共通）。Prefab のルートに付ける。
    ///   色を塗る部分 … 素材やポーションの色になる（中身・ラベルの色など）
    ///   液面         … 量に合わせて液面が上下する（LiquidFill を付けた液体）
    ///   空なら隠す物 … 在庫が0のときに見えなくなる（中の粒・葉っぱなど）
    /// 自作モデルの Prefab には付けなくてもよい（付けなければ、モデルの見た目のまま表示される）。
    /// </summary>
    public class ContentVisual : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Tooltip("素材・ポーションの色で塗る部分（液体以外）")] private Renderer[] tintTargets = new Renderer[0];
        [SerializeField, Tooltip("量に合わせて液面が上下する液体")] private LiquidFill[] fills = new LiquidFill[0];
        [SerializeField, Tooltip("空のときに隠す物")] private GameObject[] hideWhenEmpty = new GameObject[0];
        [SerializeField, Range(0f, 1f), Tooltip("中身が光る強さ（色に対する割合）")] private float emission = 0.2f;

        [Header("飾りとして置くとき")]
        [SerializeField, Tooltip("オンにすると、起動時に下の色で塗る（棚の飾りの瓶など）")] private bool useDefaultColor;
        [SerializeField] private Color defaultColor = Color.white;

        private MaterialPropertyBlock block;

        public void Configure(Renderer[] tintTargets, LiquidFill[] fills, GameObject[] hideWhenEmpty, float emission)
        {
            this.tintTargets = tintTargets ?? new Renderer[0];
            this.fills = fills ?? new LiquidFill[0];
            this.hideWhenEmpty = hideWhenEmpty ?? new GameObject[0];
            this.emission = emission;
        }

        public void SetDefaultColor(Color color)
        {
            useDefaultColor = true;
            defaultColor = color;
        }

        private void Awake()
        {
            if (useDefaultColor) SetColor(defaultColor);
        }

        public void SetColor(Color color)
        {
            color.a = 1f;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (Renderer r in tintTargets)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(ColorId, color);
                Color e = color * emission;
                e.a = 1f;
                block.SetColor(EmissionId, e);
                r.SetPropertyBlock(block);
            }
            foreach (LiquidFill f in fills)
            {
                if (f != null) f.SetColor(color, emission);
            }
        }

        /// <summary>量（0〜1）。0 なら空の容器になる</summary>
        public void SetAmount(float amount)
        {
            foreach (LiquidFill f in fills)
            {
                if (f != null) f.SetAmount(amount);
            }
            bool any = amount > 0.001f;
            foreach (GameObject go in hideWhenEmpty)
            {
                if (go != null && go.activeSelf != any) go.SetActive(any);
            }
        }

        /// <summary>動く物（手に持つ瓶など）にする：液面を毎フレーム計算し直す</summary>
        public void SetFollowMovement(bool on)
        {
            foreach (LiquidFill f in fills)
            {
                if (f != null) f.SetFollowMovement(on);
            }
        }
    }
}
