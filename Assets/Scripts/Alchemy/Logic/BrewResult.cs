using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>調合結果の種類</summary>
    public enum BrewOutcome
    {
        /// <summary>配合ルール違反（ベース液体がない等）。素材は消費しない想定</summary>
        Invalid = 0,
        /// <summary>調合はできたが効力不足の失敗作</summary>
        Failed = 1,
        /// <summary>属性値から名前・効果・色を自動生成したポーション</summary>
        Generated = 2,
        /// <summary>完成条件データ（特殊レシピ）に一致したポーション</summary>
        Special = 3,
    }

    /// <summary>
    /// ポーションの効果1つ分。ファーム等のゲームシステムは description ではなく
    /// attribute と power を読んで効果を適用する（例：成長促進 power 46 → 成長速度 +46%）。
    /// </summary>
    [Serializable]
    public struct PotionEffect
    {
        public PotionAttribute attribute;
        public float power;
        public string description;

        public PotionEffect(PotionAttribute attribute, float power, string description)
        {
            this.attribute = attribute;
            this.power = power;
            this.description = description;
        }
    }

    /// <summary>調合1回分の結果</summary>
    public sealed class BrewResult
    {
        internal readonly List<PotionEffect> effects = new List<PotionEffect>();
        internal readonly List<IngredientAmount> usedIngredients = new List<IngredientAmount>();
        internal readonly List<string> messages = new List<string>();

        public BrewOutcome Outcome { get; internal set; }
        public bool IsSuccess => Outcome == BrewOutcome.Generated || Outcome == BrewOutcome.Special;

        /// <summary>図鑑・セーブ用ID。自動生成品は「gen.ベース液体ID.主属性.副属性.g ランク」</summary>
        public string PotionId { get; internal set; } = "";
        public string PotionName { get; internal set; } = "";
        public string Description { get; internal set; } = "";
        public Color PotionColor { get; internal set; } = Color.white;

        /// <summary>★の数（1〜5）。失敗・調合不可は 0</summary>
        public int Stars { get; internal set; }
        /// <summary>ランクの番号（0始まり・低い順）。失敗・調合不可は -1</summary>
        public int GradeIndex { get; internal set; } = -1;

        public float TotalPotency { get; internal set; }
        public AttributeVector Attributes { get; internal set; } = new AttributeVector();
        public PotionAttribute? PrimaryAttribute { get; internal set; }
        public PotionAttribute? SecondaryAttribute { get; internal set; }

        /// <summary>一致した特殊レシピ（自動生成品は null）</summary>
        public PotionConditionData MatchedCondition { get; internal set; }
        public IngredientData BaseLiquid { get; internal set; }

        public IReadOnlyList<PotionEffect> Effects => effects;
        /// <summary>実際に計算に使った素材（同じ素材はまとめ済み）</summary>
        public IReadOnlyList<IngredientAmount> UsedIngredients => usedIngredients;
        /// <summary>エラー・注意メッセージ（UI表示やデバッグ用）</summary>
        public IReadOnlyList<string> Messages => messages;

        /// <summary>コンソール表示用の文字列（リッチテキストで色見本付き）</summary>
        public string ToDebugString(AlchemySettings settings)
        {
            Func<PotionAttribute, string> nameOf = a => settings != null ? settings.GetDisplayName(a) : a.ToString();
            var sb = new StringBuilder();

            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(PotionColor)).Append(">■■■</color> ");
            sb.Append("<b>").Append(PotionName).Append("</b>");
            if (IsSuccess) sb.Append("  ").Append(StarText(Stars));
            sb.Append("  [").Append(OutcomeLabel(Outcome)).Append(']').AppendLine();

            if (!string.IsNullOrEmpty(PotionId)) sb.Append("  ID: ").AppendLine(PotionId);

            if (Outcome != BrewOutcome.Invalid)
            {
                sb.Append("  総効力: ").Append(TotalPotency.ToString("0.#"))
                  .Append("  ／ 属性: ").AppendLine(Attributes.ToString(nameOf));
            }

            if (effects.Count > 0)
            {
                sb.Append("  効果: ");
                for (int i = 0; i < effects.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append(effects[i].description);
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(Description)) sb.Append("  説明: ").AppendLine(Description);

            if (MatchedCondition != null)
            {
                sb.Append("  一致した完成条件: ").Append(MatchedCondition.name)
                  .Append("（").Append(MatchedCondition.DescribeConditions(nameOf)).AppendLine("）");
            }

            foreach (string message in messages) sb.Append("  ※ ").AppendLine(message);

            return sb.ToString().TrimEnd();
        }

        public static string StarText(int stars)
        {
            int filled = Mathf.Clamp(stars, 0, 5);
            return new string('★', filled) + new string('☆', 5 - filled);
        }

        public static string OutcomeLabel(BrewOutcome outcome)
        {
            switch (outcome)
            {
                case BrewOutcome.Invalid: return "調合不可";
                case BrewOutcome.Failed: return "失敗作";
                case BrewOutcome.Generated: return "自動生成";
                case BrewOutcome.Special: return "特殊レシピ";
                default: return outcome.ToString();
            }
        }

        internal static BrewResult CreateInvalid(string message)
        {
            var result = new BrewResult
            {
                Outcome = BrewOutcome.Invalid,
                PotionName = "（調合できません）",
                PotionColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
            };
            result.messages.Add(message);
            return result;
        }
    }
}
