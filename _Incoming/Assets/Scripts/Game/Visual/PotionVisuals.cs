using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 完成ポーションの瓶を置く。瓶のモデルは「ベース液体の瓶モデル → 見た目の一覧（ArtCatalog）の標準の瓶」の順に決まる。
    /// 瓶の Prefab に ContentVisual があれば、中身をポーションの色にする。
    /// </summary>
    public static class PotionVisuals
    {
        /// <summary>瓶のモデル（Prefab）。ベース液体がわからなければ標準の瓶</summary>
        public static GameObject BottlePrefab(string baseLiquidId)
        {
            ArtCatalog art = GameContext.Art;
            if (art == null) return null;
            IngredientDatabase db = GameContext.Ingredients;
            IngredientData baseLiquid = db != null ? db.Get(baseLiquidId) : null;
            return art.PotionBottle(baseLiquid);
        }

        /// <summary>瓶を作る（当たり判定は付けない）</summary>
        public static GameObject CreateBottle(Color color, string baseLiquidId, Transform parent, string name = "PotionBottle")
        {
            GameObject prefab = BottlePrefab(baseLiquidId);
            GameObject root;
            if (prefab != null)
            {
                root = Object.Instantiate(prefab, parent);
            }
            else
            {
                // 見た目の一覧がないとき（古いシーンなど）の仮の瓶
                root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                root.transform.SetParent(parent, false);
                root.transform.localScale = Vector3.one * 0.12f;
                root.GetComponent<Renderer>().material.color = color;
            }
            root.name = name;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var content = root.GetComponent<ContentVisual>();
            if (content != null)
            {
                content.SetColor(color);
                content.SetAmount(1f);
            }
            return root;
        }
    }
}
