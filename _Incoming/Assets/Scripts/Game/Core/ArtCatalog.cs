using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// ゲーム全体で使う「標準の見た目」の置き場所（Assets/Art/ArtCatalog.asset）。
    /// 自作モデルに差し替えるときは、ここの Prefab を自分の Prefab に入れ替えるか、Prefab の中身（Model）を差し替える。
    /// 素材ごとのモデルは素材データ（IngredientData）の「棚・手に持つときのモデル」で個別に指定できる。
    /// </summary>
    [CreateAssetMenu(fileName = "ArtCatalog", menuName = "Potion Frontier/見た目の一覧 (Art Catalog)", order = 10)]
    public class ArtCatalog : ScriptableObject
    {
        [Header("完成ポーション")]
        [Tooltip("標準のポーションの瓶。ベース液体ごとに「瓶モデル」を指定すれば、そちらが優先される")]
        public GameObject potionBottle;

        [Header("素材の仮の容器（素材データにモデルがないとき）")]
        public GameObject tallBottle;
        public GameObject jar;
        public GameObject roundFlask;
        public GameObject pouch;
        public GameObject crystal;

        [Header("フラスコに入れる欠片（素材データに欠片モデルがないとき）")]
        public GameObject dropPiece;

        [Header("調べるときの光る輪郭")]
        [Tooltip("輪郭だけを描くマテリアル（なめらかなモデル用）")] public Material highlight;
        [Tooltip("輪郭だけを描くマテリアル（箱のような角ばったモデル用）")] public Material highlightBox;
        [Tooltip("輪郭の内側に線を描かないための下地（OutlineMask シェーダー）")] public Material highlightMask;

        [Header("調合の演出（空なら、湯気・泡・しぶきは出ない）")]
        [Tooltip("湯気・煙・粉のもや（2×2 のコマのテクスチャ。半透明）")] public Material steam;
        [Tooltip("液体の中の泡（半透明）")] public Material bubble;
        [Tooltip("しぶき・はじける粒（半透明）")] public Material droplet;

        [Header("UI")]
        [Tooltip("UIの絵・フォント。空ならコードで描いた仮の絵とOSのフォントを使う")] public UISkin uiSkin;

        /// <summary>素材の仮の容器</summary>
        public GameObject Container(ContainerStyle style)
        {
            switch (style)
            {
                case ContainerStyle.TallBottle: return tallBottle;
                case ContainerStyle.RoundFlask: return roundFlask;
                case ContainerStyle.Pouch: return pouch != null ? pouch : jar;
                case ContainerStyle.Crystal: return crystal != null ? crystal : roundFlask;
                default: return jar;
            }
        }

        /// <summary>素材の見た目（素材データのモデル → 仮の容器 の順）</summary>
        public GameObject IngredientModel(IngredientData data)
        {
            if (data == null) return jar;
            return data.WorldPrefab != null ? data.WorldPrefab : Container(data.Container);
        }

        /// <summary>ポーションの瓶（ベース液体の瓶モデル → 標準の瓶 の順）</summary>
        public GameObject PotionBottle(IngredientData baseLiquid)
        {
            if (baseLiquid != null && baseLiquid.PotionBottlePrefab != null) return baseLiquid.PotionBottlePrefab;
            return potionBottle;
        }
    }
}
