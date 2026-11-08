using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>属性条件の種類</summary>
    public enum ConditionType
    {
        [InspectorName("以上（値 ≥ 最小値）")] AtLeast = 0,
        [InspectorName("以下（値 ≤ 最大値）")] AtMost = 1,
        [InspectorName("範囲内（最小値 ≤ 値 ≤ 最大値）")] Between = 2,
        [InspectorName("一番高い属性である")] IsDominant = 3,
        [InspectorName("全体に占める割合が 最小値% 以上")] ShareAtLeast = 4,
    }

    /// <summary>1つの属性に対する条件</summary>
    [Serializable]
    public class AttributeCondition
    {
        public PotionAttribute attribute;
        public ConditionType type = ConditionType.AtLeast;

        [Tooltip("以上・範囲内・割合 で使う値（割合は % で指定）")]
        public float min;

        [Tooltip("以下・範囲内 で使う値")]
        public float max;

        public AttributeCondition() { }

        public AttributeCondition(PotionAttribute attribute, ConditionType type, float min = 0f, float max = 0f)
        {
            this.attribute = attribute;
            this.type = type;
            this.min = min;
            this.max = max;
        }

        public bool Evaluate(AttributeVector attributes)
        {
            float value = attributes[attribute];
            switch (type)
            {
                case ConditionType.AtLeast: return value >= min;
                case ConditionType.AtMost: return value <= max;
                case ConditionType.Between: return value >= min && value <= max;
                case ConditionType.IsDominant: return attributes.IsDominant(attribute);
                case ConditionType.ShareAtLeast: return attributes.GetShare(attribute) * 100f >= min;
                default: return false;
            }
        }

        public string Describe(Func<PotionAttribute, string> nameOf)
        {
            string n = nameOf != null ? nameOf(attribute) : attribute.ToString();
            switch (type)
            {
                case ConditionType.AtLeast: return $"{n} ≥ {min:0.#}";
                case ConditionType.AtMost: return $"{n} ≤ {max:0.#}";
                case ConditionType.Between: return $"{min:0.#} ≤ {n} ≤ {max:0.#}";
                case ConditionType.IsDominant: return $"{n}が一番高い";
                case ConditionType.ShareAtLeast: return $"{n}の割合 ≥ {min:0.#}%";
                default: return "?";
            }
        }
    }

    /// <summary>
    /// ポーションの完成条件データ（特殊レシピ）。
    /// 相談の決め手になる薬・伝説の特効薬など「探求の目標になる」ものだけをこれで定義する。
    /// どれにも当てはまらない場合は、属性値から名前・効果・色が自動生成される。
    /// </summary>
    [CreateAssetMenu(fileName = "Potion_New", menuName = "Potion Frontier/ポーション完成条件 (Potion Condition)", order = 1)]
    public class PotionConditionData : ScriptableObject
    {
        [Header("完成品の情報")]
        [SerializeField, Tooltip("図鑑・セーブ用の一意なID。空欄ならアセット名を使う")]
        private string potionId = "";

        [SerializeField]
        private string potionName = "名もなき秘薬";

        [SerializeField, TextArea(2, 4), Tooltip("空欄なら属性値から効果文を自動生成する")]
        private string effectDescription = "";

        [SerializeField]
        private Sprite icon;

        [SerializeField, Tooltip("オンにすると、混ぜた色ではなく下の色を使う")]
        private bool useCustomColor = false;

        [SerializeField]
        private Color customColor = Color.white;

        [Header("判定")]
        [SerializeField, Tooltip("複数の完成条件に当てはまったとき、数値が大きいほうを優先する")]
        private int priority = 0;

        [SerializeField, Tooltip("総効力（プラスの属性値の合計）がこの値以上であること。0なら不問")]
        private float minTotalPotency = 0f;

        [SerializeField, Tooltip("必ず入れる必要がある素材（空なら不問）")]
        private List<IngredientData> requiredIngredients = new List<IngredientData>();

        [SerializeField, Tooltip("すべて満たしたときに成立（AND条件）")]
        private List<AttributeCondition> conditions = new List<AttributeCondition>();

        public string PotionId => string.IsNullOrEmpty(potionId) ? name : potionId;
        public string PotionName => potionName;
        public string EffectDescription => effectDescription;
        public Sprite Icon => icon;
        public bool UseCustomColor => useCustomColor;
        public Color CustomColor => customColor;
        public int Priority => priority;
        public float MinTotalPotency => minTotalPotency;
        public IReadOnlyList<IngredientData> RequiredIngredients => requiredIngredients;
        public IReadOnlyList<AttributeCondition> Conditions => conditions;

        /// <summary>条件の細かさ。優先度が同じときは条件が多い（より具体的な）ほうを優先する</summary>
        public int Specificity
        {
            get
            {
                int count = (conditions != null ? conditions.Count : 0)
                          + (requiredIngredients != null ? requiredIngredients.Count : 0);
                if (minTotalPotency > 0f) count++;
                return count;
            }
        }

        public bool IsMatch(AttributeVector attributes, IReadOnlyList<IngredientAmount> ingredients)
        {
            // 条件が1つもないデータは「何でも一致」してしまうため、安全のため一致させない
            if (Specificity == 0) return false;

            if (attributes.TotalPotency < minTotalPotency) return false;

            if (requiredIngredients != null)
            {
                foreach (IngredientData required in requiredIngredients)
                {
                    if (required != null && !ContainsIngredient(ingredients, required)) return false;
                }
            }

            if (conditions != null)
            {
                foreach (AttributeCondition condition in conditions)
                {
                    if (condition != null && !condition.Evaluate(attributes)) return false;
                }
            }
            return true;
        }

        /// <summary>液体の記録で判定する（使った素材は ID で照らし合わせる。混ぜた液体の中の素材も含む）</summary>
        public bool IsMatch(AttributeVector attributes, ICollection<string> ingredientIds)
        {
            if (Specificity == 0) return false;
            if (attributes.TotalPotency < minTotalPotency) return false;
            if (requiredIngredients != null)
            {
                foreach (IngredientData required in requiredIngredients)
                {
                    if (required != null && (ingredientIds == null || !ingredientIds.Contains(required.IngredientId))) return false;
                }
            }
            if (conditions != null)
            {
                foreach (AttributeCondition condition in conditions)
                {
                    if (condition != null && !condition.Evaluate(attributes)) return false;
                }
            }
            return true;
        }

        public string DescribeConditions(Func<PotionAttribute, string> nameOf)
        {
            var parts = new List<string>();
            if (minTotalPotency > 0f) parts.Add($"総効力 ≥ {minTotalPotency:0.#}");
            if (requiredIngredients != null)
            {
                foreach (IngredientData required in requiredIngredients)
                {
                    if (required != null) parts.Add($"「{required.DisplayName}」を使う");
                }
            }
            if (conditions != null)
            {
                foreach (AttributeCondition condition in conditions)
                {
                    if (condition != null) parts.Add(condition.Describe(nameOf));
                }
            }
            return parts.Count > 0 ? string.Join(" かつ ", parts) : "（条件なし：一致しません）";
        }

        private static bool ContainsIngredient(IReadOnlyList<IngredientAmount> ingredients, IngredientData target)
        {
            if (ingredients == null) return false;
            for (int i = 0; i < ingredients.Count; i++)
            {
                IngredientAmount entry = ingredients[i];
                if (entry != null && entry.ingredient == target && entry.amount > 0f) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        /// <summary>エディタ拡張（サンプルデータ生成）用。ゲーム中には使わない</summary>
        public void EditorSetup(
            string id,
            string potionName,
            string effectDescription,
            int priority,
            float minTotalPotency,
            IngredientData[] requiredIngredients,
            AttributeCondition[] conditions,
            bool useCustomColor = false,
            Color customColor = default)
        {
            potionId = id;
            this.potionName = potionName;
            this.effectDescription = effectDescription;
            this.priority = priority;
            this.minTotalPotency = minTotalPotency;
            this.requiredIngredients = new List<IngredientData>(requiredIngredients ?? new IngredientData[0]);
            this.conditions = new List<AttributeCondition>(conditions ?? new AttributeCondition[0]);
            this.useCustomColor = useCustomColor;
            this.customColor = customColor;
        }

        /// <summary>サンプルの説明文の言い回しを新しくする（説明が古い文のままのときだけ。作業計画5：「依頼」の言い換え）</summary>
        public bool EditorUpgradeDescription(string oldPart, string newDescription)
        {
            if (string.IsNullOrEmpty(effectDescription) || !effectDescription.Contains(oldPart)) return false;
            effectDescription = newDescription;
            return true;
        }
#endif
    }
}
