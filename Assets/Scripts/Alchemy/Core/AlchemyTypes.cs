using System;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// ポーションの属性。
    /// 【重要】アセットには「数値」で保存されるため、既存の値の変更・並べ替えはしないこと。
    /// 新しい属性は末尾に連番で追加する（AttributeVector が配列のインデックスとして使う）。
    /// </summary>
    public enum PotionAttribute
    {
        [InspectorName("回復")] Healing = 0,
        [InspectorName("火")] Fire = 1,
        [InspectorName("氷")] Ice = 2,
        [InspectorName("成長促進")] Growth = 3,
        [InspectorName("透明化")] Invisibility = 4,
        [InspectorName("毒")] Poison = 5,
        [InspectorName("光")] Light = 6,
        [InspectorName("風")] Wind = 7,
    }

    /// <summary>素材の種類（ベース液体 × 主素材 × 触媒）</summary>
    public enum IngredientCategory
    {
        [InspectorName("ベース液体")] BaseLiquid = 0,
        [InspectorName("主素材")] MainMaterial = 1,
        [InspectorName("触媒")] Catalyst = 2,
    }

    /// <summary>
    /// 素材の形状（扱い方）。カテゴリとは別に決める（例：液体の触媒も作れる）。
    /// 自動 … ベース液体なら液体、それ以外は固体として扱う
    /// </summary>
    public enum IngredientForm
    {
        [InspectorName("自動")] Auto = 0,
        [InspectorName("液体（注ぐ）")] Liquid = 1,
        [InspectorName("固体（入れる）")] Solid = 2,
        [InspectorName("粉（振り入れる）")] Powder = 3,
    }

    /// <summary>
    /// 自作モデルを指定していない素材を、棚に置くときの仮の容器。
    /// 自動 … ベース液体は背の高い瓶、主素材は壺、触媒は丸底フラスコ
    /// </summary>
    public enum ContainerStyle
    {
        [InspectorName("自動")] Auto = 0,
        [InspectorName("背の高い瓶")] TallBottle = 1,
        [InspectorName("壺")] Jar = 2,
        [InspectorName("丸底フラスコ")] RoundFlask = 3,
        [InspectorName("小袋")] Pouch = 4,
        [InspectorName("結晶")] Crystal = 5,
    }

    /// <summary>できた液体の分類（レシピ本のタブ・名前の付け方に使う）</summary>
    public enum LiquidKind
    {
        [InspectorName("未知の液体")] Unknown = 0,
        [InspectorName("中間液体")] Intermediate = 1,
        [InspectorName("ポーション")] Potion = 2,
    }

    /// <summary>かき混ぜる向き</summary>
    public enum StirDirection
    {
        [InspectorName("どちらでも")] Any = 0,
        [InspectorName("時計回り")] Clockwise = 1,
        [InspectorName("反時計回り")] CounterClockwise = 2,
    }

    /// <summary>素材が持つ属性値（1単位あたり）。マイナス値で他の素材の属性を打ち消せる。</summary>
    [Serializable]
    public struct AttributeValue
    {
        public PotionAttribute attribute;

        [Tooltip("1単位あたりの属性値。マイナスにすると他の素材のその属性を打ち消す")]
        public float value;

        public AttributeValue(PotionAttribute attribute, float value)
        {
            this.attribute = attribute;
            this.value = value;
        }
    }

    /// <summary>触媒による属性の増幅（1単位あたりの%）</summary>
    [Serializable]
    public struct AttributeAmplifier
    {
        public PotionAttribute attribute;

        [Tooltip("+50 で 1.5倍、-50 で 0.5倍（1単位あたり。複数あると足し算で重なる）")]
        public float percent;

        public AttributeAmplifier(PotionAttribute attribute, float percent)
        {
            this.attribute = attribute;
            this.percent = percent;
        }
    }

    /// <summary>
    /// 調合に投入する「素材＋分量」。
    /// 分量の単位は「1単位」。将来の注ぐ操作では ml などをこの単位へ変換して渡す。
    /// </summary>
    [Serializable]
    public class IngredientAmount
    {
        public IngredientData ingredient;

        [Min(0f), Tooltip("分量（単位）。0以下は無視される")]
        public float amount = 1f;

        public IngredientAmount() { }

        public IngredientAmount(IngredientData ingredient, float amount)
        {
            this.ingredient = ingredient;
            this.amount = amount;
        }
    }
}
