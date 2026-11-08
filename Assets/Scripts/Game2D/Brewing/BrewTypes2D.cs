using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 手に持っている物（素材か、棚から1本分取り出した自作の液体の瓶）。
    /// 素材は在庫から「使った分だけ」減る（取り出しただけでは減らない）。瓶は取り出した時点で棚から1本分減り、戻すと棚に戻る。
    /// </summary>
    public sealed class HeldThing2D
    {
        public IngredientData Ingredient { get; }
        public PotionStack Potion { get; }

        public HeldThing2D(IngredientData ingredient) { Ingredient = ingredient; }
        public HeldThing2D(PotionStack potion) { Potion = potion; }

        /// <summary>注ぐ物（液体の素材・自作の液体の瓶）</summary>
        public bool IsLiquid => Potion != null || (Ingredient != null && Ingredient.IsPoured);
        public string Name => Potion != null ? Potion.potionName : (Ingredient != null ? Ingredient.DisplayName : "");
        public Color Color => Potion != null ? Potion.color : (Ingredient != null ? Ingredient.Color : Color.white);
    }

    /// <summary>トレイの1マスに並ぶ物（素材か、棚の自作の液体）</summary>
    public struct TrayEntry2D
    {
        public IngredientData ingredient;
        public PotionStack potion;

        public bool IsEmpty => ingredient == null && potion == null;
        public bool Same(TrayEntry2D other) => ingredient == other.ingredient && potion == other.potion;
    }

    /// <summary>絵の大きさをそろえる（自分で描いた絵が何ピクセルでも、決めた高さに収まるようにする）</summary>
    public static class ArtFit2D
    {
        /// <summary>
        /// art（絵をまとめた子）を、高さ height・幅 maxWidth までに収める。絵の中心が親の原点に来る。
        /// 中身の絵（content）は容器の絵と同じ大きさのキャンバスで描く前提（同じ位置・同じ倍率で重なる）。
        /// 収めたあとの大きさ（ワールド単位）を返す。
        /// </summary>
        public static Vector2 Fit(Transform art, Sprite reference, float height, float maxWidth)
        {
            if (art == null) return Vector2.zero;
            if (reference == null)
            {
                art.localScale = Vector3.one;
                art.localPosition = Vector3.zero;
                return new Vector2(maxWidth > 0f ? maxWidth : height, height);
            }
            Bounds b = reference.bounds;
            float s = height / Mathf.Max(0.001f, b.size.y);
            if (maxWidth > 0f) s = Mathf.Min(s, maxWidth / Mathf.Max(0.001f, b.size.x));
            art.localScale = new Vector3(s, s, 1f);
            art.localPosition = new Vector3(-b.center.x * s, -b.center.y * s, 0f);
            return new Vector2(b.size.x * s, b.size.y * s);
        }
    }
}
