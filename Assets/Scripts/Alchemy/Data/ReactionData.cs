using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>反応で起きる「属性の移り変わり」（例：光の 60% が透明化に変わる）</summary>
    [Serializable]
    public class AttributeConversion
    {
        public PotionAttribute from;
        public PotionAttribute to;
        [Range(0f, 100f), Tooltip("元の属性の何%を変えるか")] public float percent = 50f;
        [Tooltip("変わった先での倍率（1 = 同じ量、1.5 = 1.5倍になる）")] public float efficiency = 1f;
    }

    /// <summary>反応で属性を何倍にするか</summary>
    [Serializable]
    public class AttributeMultiplier
    {
        public PotionAttribute attribute;
        [Tooltip("倍率（1.2 = 1.2倍、0.5 = 半分）")] public float multiplier = 1f;
    }

    /// <summary>反応で付く特性</summary>
    [Serializable]
    public class TraitGain
    {
        [Tooltip("特性のID（調合ルール設定の「液体の特性」にある ID）")] public string traitId = "";
        [Range(0f, 1f), Tooltip("付く強さ（0〜1）")] public float strength = 1f;
    }

    /// <summary>
    /// 素材どうしの特別な反応（例：霧隠れの花と月影の粉を冷たいまま反時計回りに混ぜると、光が透明化に変わる）。
    /// 条件をすべて満たした状態が「かかる時間」続くと、1つのフラスコで1回だけ起こる。
    /// 起きた反応は工程の記録とレシピ本に残る（＝プレイヤーの「発見」になる）。
    /// </summary>
    [CreateAssetMenu(fileName = "Reaction_New", menuName = "Potion Frontier/反応 (Reaction)", order = 3)]
    public class ReactionData : ScriptableObject
    {
        [Header("表示")]
        [SerializeField, Tooltip("一意なID。空ならアセット名")] private string reactionId = "";
        [SerializeField] private string displayName = "新しい反応";
        [SerializeField, TextArea(2, 4), Tooltip("レシピ本に出る説明（発見したあとに読める）")] private string description = "";

        [Header("条件（すべて満たすこと）")]
        [SerializeField, Tooltip("フラスコに入っている必要がある素材（溶けた量で判定）")] private List<IngredientData> requiredIngredients = new List<IngredientData>();
        [SerializeField, Min(0f), Tooltip("必要な素材それぞれの、溶けている量の下限（単位）")] private float minDissolvedUnits = 0.3f;
        [SerializeField, Tooltip("温度の下限（℃）")] private float minTemperature = -100f;
        [SerializeField, Tooltip("温度の上限（℃）")] private float maxTemperature = 200f;
        [SerializeField, Tooltip("かき混ぜる向きの条件")] private StirDirection stirDirection = StirDirection.Any;
        [SerializeField, Min(0f), Tooltip("その向きに、直近10秒で何回転以上かき混ぜていること（0 = 不問）")] private float minRecentTurns = 0f;
        [SerializeField, Tooltip("濃さの条件（今のフラスコの属性）")] private List<AttributeCondition> attributeConditions = new List<AttributeCondition>();
        [SerializeField, Min(0f), Tooltip("条件を満たした状態がこの秒数続くと起こる")] private float duration = 3f;

        [Header("起きること")]
        [SerializeField] private List<AttributeConversion> conversions = new List<AttributeConversion>();
        [SerializeField] private List<AttributeMultiplier> multipliers = new List<AttributeMultiplier>();
        [SerializeField] private List<TraitGain> traits = new List<TraitGain>();

        public string ReactionId => string.IsNullOrEmpty(reactionId) ? name : reactionId;
        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<IngredientData> RequiredIngredients => requiredIngredients;
        public float MinDissolvedUnits => minDissolvedUnits;
        public float MinTemperature => minTemperature;
        public float MaxTemperature => maxTemperature;
        public StirDirection StirDirection => stirDirection;
        public float MinRecentTurns => minRecentTurns;
        public IReadOnlyList<AttributeCondition> AttributeConditions => attributeConditions;
        public float Duration => duration;
        public IReadOnlyList<AttributeConversion> Conversions => conversions;
        public IReadOnlyList<AttributeMultiplier> Multipliers => multipliers;
        public IReadOnlyList<TraitGain> Traits => traits;

#if UNITY_EDITOR
        /// <summary>エディタ拡張（サンプルデータ生成）用</summary>
        public void EditorSetup(string id, string displayName, string description, IngredientData[] required, float minDissolved,
                                float minTemp, float maxTemp, StirDirection direction, float minTurns, AttributeCondition[] conditions,
                                float duration, AttributeConversion[] conversions, AttributeMultiplier[] multipliers, TraitGain[] traits)
        {
            reactionId = id;
            this.displayName = displayName;
            this.description = description;
            requiredIngredients = new List<IngredientData>(required ?? new IngredientData[0]);
            minDissolvedUnits = minDissolved;
            minTemperature = minTemp;
            maxTemperature = maxTemp;
            stirDirection = direction;
            minRecentTurns = minTurns;
            attributeConditions = new List<AttributeCondition>(conditions ?? new AttributeCondition[0]);
            this.duration = duration;
            this.conversions = new List<AttributeConversion>(conversions ?? new AttributeConversion[0]);
            this.multipliers = new List<AttributeMultiplier>(multipliers ?? new AttributeMultiplier[0]);
            this.traits = new List<TraitGain>(traits ?? new TraitGain[0]);
        }
#endif
    }
}
