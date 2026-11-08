using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>属性ごとの表示名・命名パーツ・色・効果文</summary>
    [Serializable]
    public class AttributeInfo
    {
        // 先頭の string フィールドはインスペクターのリスト見出しに使われるため displayName を先頭に置く
        [Tooltip("UIなどに表示する属性名（例：回復）")]
        public string displayName = "";

        public PotionAttribute attribute;

        [Tooltip("主属性になったとき名前に入る語（例：回復 → 回復ポーション）")]
        public string primaryName = "";

        [Tooltip("副属性になったとき前に付く修飾語（例：灼熱の → 灼熱の回復ポーション）")]
        public string secondaryModifier = "";

        [Tooltip("この属性の色。属性値の比率で混ぜた色が完成品の色になる")]
        public Color color = Color.white;

        [Tooltip("効果の説明文。{0} に効果量（整数）が入る")]
        public string effectFormat = "{0}";

        public AttributeInfo() { }

        public AttributeInfo(PotionAttribute attribute, string displayName, string primaryName,
                             string secondaryModifier, Color color, string effectFormat)
        {
            this.attribute = attribute;
            this.displayName = displayName;
            this.primaryName = primaryName;
            this.secondaryModifier = secondaryModifier;
            this.color = color;
            this.effectFormat = effectFormat;
        }
    }

    /// <summary>総効力で決まるランク</summary>
    [Serializable]
    public class GradeTier
    {
        [Tooltip("総効力がこの値以上ならこのランク")]
        public float minPotency;

        [Tooltip("名前の先頭に付く語（空欄可）")]
        public string prefix = "";

        [Range(1, 5)]
        public int stars = 1;

        public GradeTier() { }

        public GradeTier(float minPotency, string prefix, int stars)
        {
            this.minPotency = minPotency;
            this.prefix = prefix;
            this.stars = stars;
        }
    }

    /// <summary>液体の特性（焦げ・泡立ちなど）の表示</summary>
    [Serializable]
    public class TraitInfo
    {
        [Tooltip("UIに出す名前（例：焦げ）")] public string displayName = "";
        [Tooltip("特性のID（反応データなどから指定する。半角英数字）")] public string id = "";
        [Tooltip("名前の頭に付く語（例：焦げた）。この特性が強いときに付く")] public string nameModifier = "";
        [Tooltip("液体の色に混ぜる色")] public Color tint = Color.white;
        [Range(0f, 1f), Tooltip("色をどれだけ混ぜるか（特性の強さが1のとき）")] public float tintStrength = 0f;

        public TraitInfo() { }

        public TraitInfo(string id, string displayName, string nameModifier, Color tint, float tintStrength)
        {
            this.id = id;
            this.displayName = displayName;
            this.nameModifier = nameModifier;
            this.tint = tint;
            this.tintStrength = tintStrength;
        }
    }

    /// <summary>
    /// フラスコの中の工程のルール（温度・溶け方・かき混ぜ・蒸発）。
    /// 量は ml、温度は ℃、時間は秒。素材ごとの性質（溶けやすさ・熱への強さ）は素材データで決める。
    /// </summary>
    [Serializable]
    public class BrewRules
    {
        [Header("温度")]
        [Tooltip("部屋の温度（火を止めるとこの温度に戻っていく）")] public float roomTemperature = 20f;
        [Tooltip("火加減が最大のときに向かう温度")] public float maxHeatTemperature = 130f;
        [Tooltip("沸騰する温度（液体があるうちはこれ以上にならない）")] public float boilingPoint = 100f;
        [Tooltip("温まる速さ（1秒あたり、目標との差に掛ける）")] public float heatingRate = 0.18f;
        [Tooltip("冷める速さ（1秒あたり、部屋の温度との差に掛ける）")] public float coolingRate = 0.04f;
        [Tooltip("この量（ml）ごとに温まる速さが半分になる（量が多いほど温まりにくい）")] public float heatVolumeMl = 200f;

        [Header("沸騰・煮詰め")]
        [Tooltip("火加減最大で沸騰しているときに蒸発する量（ml/秒）")] public float boilEvaporationMlPerSec = 6f;
        [Tooltip("液体がこれ（ml）より少ない状態で沸騰させ続けると焦げる")] public float scorchBelowMl = 15f;
        [Range(0.05f, 1f), Tooltip("沸騰で量が、煮詰める前のこの割合より減ると焦げ始める（0.25 = 煮詰めて濃くできるのは約4倍まで）")] public float scorchBelowRatio = 0.25f;
        [Tooltip("焦げる速さ（1秒あたり、焦げの強さが増える量）")] public float scorchRate = 0.25f;
        [Tooltip("焦げの強さ1あたり、効き目がどれだけ減るか（1秒あたり）")] public float scorchDamagePerSec = 0.15f;
        [Range(0.1f, 1f), Tooltip("沸騰で量がこの割合まで減ると「濃縮」になる")] public float concentratedRatio = 0.6f;

        [Header("量")]
        [Tooltip("固体1単位が増やす液量（ml）")] public float solidVolumeMlPerUnit = 5f;
        [Tooltip("粉1単位が増やす液量（ml）")] public float powderVolumeMlPerUnit = 2f;
        [Tooltip("溶けた固体・粉の濃さを計算するときの最小の液量（ml）。ほんの少しの液体に素材をたくさん溶かして、濃さが極端にならないように（液体どうしは実際の量で計算する）")] public float minVolumeMl = 10f;

        [Header("溶け方")]
        [Tooltip("溶ける速さ（1秒あたり、溶けられる上限との差に掛ける）")] public float dissolveRate = 0.2f;
        [Tooltip("溶けやすさ0の素材が、混ぜずに自然に溶ける上限")] public float naturalDissolveMin = 0.35f;
        [Tooltip("溶けやすさ1の素材が、混ぜずに自然に溶ける上限")] public float naturalDissolveMax = 0.85f;
        [Tooltip("温度が部屋より60℃高いときに増える溶ける上限")] public float heatDissolveBonus = 0.3f;
        [Tooltip("1回転かき混ぜるごとに溶ける割合（溶けやすさ0.5のとき）。放っておくと溶ける上限を超えて溶ける")] public float stirDissolvePerTurn = 0.1f;

        [Header("熱と目覚め")]
        [Tooltip("熱で弱まる速さの全体の倍率")] public float heatDamageScale = 0.5f;
        [Tooltip("目覚める温度を保って、力が目覚めきるまでの秒数")] public float activationSeconds = 6f;

        [Header("かき混ぜる向き")]
        [Tooltip("時計回り1回転で、一番強い属性が伸びる割合")] public float clockwisePrimaryPerTurn = 0.02f;
        [Tooltip("時計回りで一番強い属性が伸びる上限（素材ごと）")] public float clockwisePrimaryMax = 0.25f;
        [Tooltip("時計回り1回転で、ほかの属性が弱まる割合")] public float clockwiseOthersPerTurn = 0.01f;
        [Tooltip("時計回りでほかの属性が弱まる上限")] public float clockwiseOthersMax = 0.15f;
        [Tooltip("反時計回り1回転で、2番目の属性が伸びる割合")] public float counterSecondaryPerTurn = 0.03f;
        [Tooltip("反時計回りで2番目の属性が伸びる上限（素材ごと）")] public float counterSecondaryMax = 0.3f;
        [Tooltip("反時計回り1回転で、一番強い属性が弱まる割合")] public float counterPrimaryPerTurn = 0.01f;
        [Tooltip("反時計回りで一番強い属性が弱まる上限")] public float counterPrimaryMax = 0.1f;
        [Tooltip("合計この回数を超えてかき混ぜると泡立ちはじめる")] public float foamStartTurns = 30f;
        [Tooltip("この回数で泡立ちが最大になる")] public float foamFullTurns = 60f;

        [Header("素材の性質（溶ける温度・崩れる・香りが飛ぶ・冷える）")]
        [Tooltip("「崩れやすい」素材を、決まった回転数より多く混ぜたとき、1回転ごとに効き目が減る強さ")] public float fragileDamagePerTurn = 0.35f;
        [Tooltip("「香りが飛ぶ」素材を沸騰させたとき、1秒ごとに効き目が減る強さ（飛びやすさ 1 のとき）")] public float volatileLossPerSec = 0.3f;
        [Tooltip("冷やす素材の冷え方の基準の量（ml）。中身が多いほど冷えにくい")] public float coolVolumeMl = 100f;
        [Tooltip("いちばん低くなる温度（℃）")] public float minTemperature = -10f;

        [Header("工程の記録")]
        [Tooltip("操作の間がこの秒数以上あいたら「待つ」として記録する")] public float waitStepSeconds = 5f;
    }

    /// <summary>
    /// 調合ルール設定（ゲーム全体で1つ）。
    /// 名前・効果・色を自動生成するための「部品」と、成功/失敗・配合のルールを持つ。
    /// ここを調整するだけで全ポーションのバランスと命名が変わる。
    /// </summary>
    [CreateAssetMenu(fileName = "AlchemySettings", menuName = "Potion Frontier/調合ルール設定 (Alchemy Settings)", order = 2)]
    public class AlchemySettings : ScriptableObject
    {
        private const string DefaultNameFormat = "{grade}{secondary}{primary}{form}";

        [Header("属性ごとの表示・命名データ")]
        [SerializeField]
        private List<AttributeInfo> attributeInfos = CreateDefaultAttributeInfos();

        [Header("ランク（総効力で決まる。上から順に低→高）")]
        [SerializeField]
        private List<GradeTier> gradeTiers = CreateDefaultGradeTiers();

        [Header("命名ルール")]
        [SerializeField, Tooltip("{grade}=ランク語 {secondary}=副属性の修飾語 {primary}=主属性名 {form}=ベース液体の形態名")]
        private string nameFormat = DefaultNameFormat;

        [SerializeField, Range(0f, 1f), Tooltip("2番目の属性が「主属性の何割以上」なら副属性として名前に出す")]
        private float secondaryMinRatio = 0.4f;

        [SerializeField, Min(0f), Tooltip("この値未満の属性は効果・副属性として扱わない")]
        private float effectMinValue = 3f;

        [Header("失敗判定")]
        [SerializeField, Min(0f), Tooltip("総効力がこの値未満だと失敗作になる")]
        private float minimumPotency = 10f;

        [SerializeField]
        private string failedPotionName = "濁った色水";

        [SerializeField]
        private Color failedColor = new Color(0.45f, 0.40f, 0.32f, 1f);

        [Header("液体の分類（中間液体・ポーション）")]
        [SerializeField, Min(0f), Tooltip("総効力がこの値以上で、一番強い属性の割合が下の値以上なら「ポーション」。足りなければ「中間液体」（成功ライン未満は「未知の液体」）")]
        private float potionMinPotency = 25f;

        [SerializeField, Range(0f, 1f), Tooltip("ポーションとみなす、一番強い属性の割合（効き目がはっきりしているか）")]
        private float potionPrimaryShare = 0.45f;

        [Header("工程のルール")]
        [SerializeField]
        private BrewRules brewRules = new BrewRules();

        [Header("液体の特性")]
        [SerializeField]
        private List<TraitInfo> traitInfos = CreateDefaultTraits();

        /// <summary>工程のルールの初期値を変えたときに上げる（古い設定を新しい初期値に入れ替える）</summary>
        private const int CurrentRulesVersion = 3;
        [SerializeField, HideInInspector] private int rulesVersion;

        [Header("色の混ぜ方")]
        [SerializeField, Range(0f, 1f), Tooltip("ベース液体の色をどれだけ残すか（0=属性の色だけ、1=ベース液体の色だけ）")]
        private float baseLiquidColorInfluence = 0.3f;

        [Header("触媒")]
        [SerializeField, Min(1f), Tooltip("触媒による増幅倍率の上限（例：3 なら最大3倍）")]
        private float maxAmplifyMultiplier = 3f;

        [Header("配合ルール")]
        [SerializeField, Min(1), Tooltip("一度に入れられる主素材の種類数")]
        private int maxMainMaterials = 3;

        [SerializeField, Min(0), Tooltip("一度に入れられる触媒の種類数")]
        private int maxCatalysts = 1;

        [NonSerialized] private AttributeInfo[] infoCache;
        [NonSerialized] private List<GradeTier> sortedTiers;

        public string NameFormat => string.IsNullOrEmpty(nameFormat) ? DefaultNameFormat : nameFormat;
        public float SecondaryMinRatio => secondaryMinRatio;
        public float EffectMinValue => effectMinValue;
        public float MinimumPotency => minimumPotency;
        public string FailedPotionName => failedPotionName;
        public Color FailedColor => failedColor;
        public float BaseLiquidColorInfluence => baseLiquidColorInfluence;
        public float MaxAmplifyMultiplier => maxAmplifyMultiplier;
        public int MaxMainMaterials => maxMainMaterials;
        public int MaxCatalysts => maxCatalysts;
        public float PotionMinPotency => potionMinPotency;
        public float PotionPrimaryShare => potionPrimaryShare;
        public BrewRules Rules => brewRules ?? (brewRules = new BrewRules());

        /// <summary>特性の表示情報（未登録のIDなら IDをそのまま名前にした仮の情報）</summary>
        public TraitInfo GetTrait(string id)
        {
            if (traitInfos != null)
            {
                foreach (TraitInfo t in traitInfos)
                {
                    if (t != null && t.id == id) return t;
                }
            }
            return new TraitInfo(id, id, "", Color.white, 0f);
        }

        /// <summary>属性の表示情報を取得（未登録の属性は仮の情報を返す）</summary>
        public AttributeInfo GetInfo(PotionAttribute attribute)
        {
            if (infoCache == null) BuildInfoCache();
            return infoCache[(int)attribute];
        }

        public string GetDisplayName(PotionAttribute attribute)
        {
            return GetInfo(attribute).displayName;
        }

        /// <summary>総効力からランクを求める。gradeIndex は 0 始まり（低い順）</summary>
        public GradeTier GetGrade(float potency, out int gradeIndex)
        {
            List<GradeTier> tiers = GetSortedTiers();
            gradeIndex = 0;
            if (tiers.Count == 0) return null;

            for (int i = 0; i < tiers.Count; i++)
            {
                if (potency >= tiers[i].minPotency) gradeIndex = i;
            }
            return tiers[gradeIndex];
        }

        public int GradeCount => Mathf.Max(1, GetSortedTiers().Count);

        /// <summary>
        /// 自動生成で作れるポーションの理論上の種類数。
        /// ベース液体の数 × 主属性 × 副属性（他の属性 or なし）× ランク
        /// </summary>
        public int CountProceduralVariants(int baseLiquidCount)
        {
            int n = AttributeVector.Count;
            return baseLiquidCount * n * n * GradeCount;
        }

        public void InvalidateCache()
        {
            infoCache = null;
            sortedTiers = null;
        }

        /// <summary>全項目を初期値に戻す（インスペクターの「Reset」からも呼ばれる）</summary>
        public void ResetToDefaults()
        {
            attributeInfos = CreateDefaultAttributeInfos();
            gradeTiers = CreateDefaultGradeTiers();
            nameFormat = DefaultNameFormat;
            secondaryMinRatio = 0.4f;
            effectMinValue = 3f;
            minimumPotency = 10f;
            failedPotionName = "濁った色水";
            failedColor = new Color(0.45f, 0.40f, 0.32f, 1f);
            baseLiquidColorInfluence = 0.3f;
            maxAmplifyMultiplier = 3f;
            maxMainMaterials = 3;
            maxCatalysts = 1;
            potionMinPotency = 25f;
            potionPrimaryShare = 0.45f;
            brewRules = new BrewRules();
            traitInfos = CreateDefaultTraits();
            rulesVersion = CurrentRulesVersion;
            InvalidateCache();
        }

        private void Reset()
        {
            ResetToDefaults();
        }

        private void OnEnable()
        {
            InvalidateCache();
            if (rulesVersion < CurrentRulesVersion)
            {
                // フェーズ5で追加した「工程のルール」と「液体の特性」を初期値にする（それより前の設定は変えない）
                brewRules = new BrewRules();
                if (traitInfos == null || traitInfos.Count == 0) traitInfos = CreateDefaultTraits();
                rulesVersion = CurrentRulesVersion;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        private void OnValidate()
        {
            InvalidateCache();
        }

        private void BuildInfoCache()
        {
            var cache = new AttributeInfo[AttributeVector.Count];
            if (attributeInfos != null)
            {
                foreach (AttributeInfo info in attributeInfos)
                {
                    if (info == null) continue;
                    int index = (int)info.attribute;
                    if (index < 0 || index >= cache.Length || cache[index] != null) continue; // 重複は先勝ち
                    cache[index] = info;
                }
            }

            for (int i = 0; i < cache.Length; i++)
            {
                if (cache[i] != null) continue;
                var attribute = (PotionAttribute)i;
                string n = attribute.ToString();
                cache[i] = new AttributeInfo(attribute, n, n, n + "の", Color.gray, n + " {0}");
            }
            infoCache = cache;
        }

        private List<GradeTier> GetSortedTiers()
        {
            if (sortedTiers != null) return sortedTiers;

            var list = new List<GradeTier>();
            if (gradeTiers != null)
            {
                foreach (GradeTier tier in gradeTiers)
                {
                    if (tier != null) list.Add(tier);
                }
            }
            list.Sort((a, b) => a.minPotency.CompareTo(b.minPotency));
            sortedTiers = list;
            return sortedTiers;
        }

        private static List<AttributeInfo> CreateDefaultAttributeInfos()
        {
            return new List<AttributeInfo>
            {
                new AttributeInfo(PotionAttribute.Healing,      "回復",     "回復",   "癒やしの",   new Color(0.45f, 0.90f, 0.50f), "体力を{0}回復"),
                new AttributeInfo(PotionAttribute.Fire,         "火",       "火炎",   "灼熱の",     new Color(0.95f, 0.35f, 0.20f), "炎の力 {0}"),
                new AttributeInfo(PotionAttribute.Ice,          "氷",       "氷結",   "凍てつく",   new Color(0.55f, 0.85f, 1.00f), "冷気の力 {0}"),
                new AttributeInfo(PotionAttribute.Growth,       "成長促進", "成長",   "芽吹きの",   new Color(0.65f, 0.90f, 0.20f), "作物の成長速度 +{0}%"),
                new AttributeInfo(PotionAttribute.Invisibility, "透明化",   "透明化", "おぼろげな", new Color(0.85f, 0.85f, 0.95f), "{0}秒間 姿が見えにくくなる"),
                new AttributeInfo(PotionAttribute.Poison,       "毒",       "毒",     "毒を孕んだ", new Color(0.60f, 0.25f, 0.75f), "毒の力 {0}"),
                new AttributeInfo(PotionAttribute.Light,        "光",       "閃光",   "輝く",       new Color(1.00f, 0.90f, 0.40f), "光の力 {0}"),
                new AttributeInfo(PotionAttribute.Wind,         "風",       "疾風",   "風をまとう", new Color(0.50f, 0.95f, 0.85f), "風の力 {0}"),
            };
        }

        private static List<TraitInfo> CreateDefaultTraits()
        {
            return new List<TraitInfo>
            {
                new TraitInfo(LiquidTraits.Scorched, "焦げ", "焦げた", new Color(0.22f, 0.14f, 0.09f), 0.75f),
                new TraitInfo(LiquidTraits.Foamy, "泡立ち", "泡立つ", new Color(1f, 1f, 1f), 0.25f),
                new TraitInfo(LiquidTraits.Concentrated, "濃縮", "濃縮された", new Color(0f, 0f, 0f), 0.15f),
                new TraitInfo(LiquidTraits.Cloudy, "濁り", "濁った", new Color(0.55f, 0.5f, 0.45f), 0.4f),
                new TraitInfo(LiquidTraits.Shimmer, "ゆらめき", "ゆらめく", new Color(0.85f, 0.8f, 1f), 0.25f),
                new TraitInfo(LiquidTraits.Syrupy, "とろみ", "とろりとした", new Color(0.95f, 0.65f, 0.25f), 0.3f),
            };
        }

        private static List<GradeTier> CreateDefaultGradeTiers()
        {
            return new List<GradeTier>
            {
                new GradeTier(10f,  "微弱な",   1),
                new GradeTier(25f,  "",         2),
                new GradeTier(45f,  "上質な",   3),
                new GradeTier(70f,  "極上の",   4),
                new GradeTier(100f, "伝説級の", 5),
            };
        }

#if UNITY_EDITOR
        [ContextMenu("自動生成ポーションの種類数を計算")]
        private void LogVariantCount()
        {
            int baseLiquidCount = 0;
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:IngredientData"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<IngredientData>(path);
                if (data != null && data.Category == IngredientCategory.BaseLiquid) baseLiquidCount++;
            }

            int n = AttributeVector.Count;
            Debug.Log($"[AlchemySettings] 自動生成ポーションの理論上の種類数: {CountProceduralVariants(baseLiquidCount)} 種類\n" +
                      $"= ベース液体 {baseLiquidCount} × 主属性 {n} × 副属性 {n}（他{n - 1}種＋なし） × ランク {GradeCount}", this);
        }
#endif
    }
}
