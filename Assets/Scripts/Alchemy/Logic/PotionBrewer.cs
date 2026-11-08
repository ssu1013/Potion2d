using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 調合計算の本体。MonoBehaviour に依存しない純粋な C# クラスなので、
    /// UI・注ぐ操作・自動テストのどこからでも同じ計算を呼べる。
    ///
    /// 判定の流れ:
    ///   1. 配合チェック（ベース液体1種・主素材1種以上・触媒の上限）
    ///   2. 属性の合算（属性値 × 分量）→ 触媒の増幅 → マイナスを0に
    ///   3. 総効力が足りなければ「失敗作」
    ///   4. 完成条件データ（特殊レシピ）に一致すればそれ（優先度順）
    ///   5. どれにも一致しなければ属性値から名前・効果・色を自動生成
    /// </summary>
    public sealed class PotionBrewer
    {
        private const string DefaultFormName = "ポーション";
        private const float FailedColorBlend = 0.6f;

        private readonly AlchemySettings settings;
        private readonly List<PotionConditionData> specialRecipes;

        public PotionBrewer(AlchemySettings settings, IEnumerable<PotionConditionData> recipes)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            this.settings = settings;

            var unique = new List<PotionConditionData>();
            if (recipes != null)
            {
                foreach (PotionConditionData recipe in recipes)
                {
                    if (recipe == null || unique.Contains(recipe)) continue;
                    if (recipe.Specificity == 0)
                    {
                        Debug.LogWarning($"[PotionBrewer] 完成条件「{recipe.name}」には条件が1つもないため、判定には使われません。", recipe);
                    }
                    unique.Add(recipe);
                }
            }

            // 優先度が高い順 → 条件が細かい順（OrderBy は安定ソートなので、同点なら登録順）
            specialRecipes = unique
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.Specificity)
                .ToList();
        }

        public AlchemySettings Settings => settings;
        public IReadOnlyList<PotionConditionData> SpecialRecipes => specialRecipes;

        /// <summary>調合を実行して結果を返す（入力リストは変更しない）</summary>
        public BrewResult Brew(IReadOnlyList<IngredientAmount> ingredients)
        {
            var result = new BrewResult();
            List<IngredientAmount> used = Normalize(ingredients, result.messages);
            result.usedIngredients.AddRange(used);

            // 1. 配合チェック
            IngredientData baseLiquid;
            if (!ValidateComposition(used, result.messages, out baseLiquid))
            {
                result.Outcome = BrewOutcome.Invalid;
                result.PotionName = "（調合できません）";
                result.PotionColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                return result;
            }
            result.BaseLiquid = baseLiquid;

            // 2. 属性の合算
            AttributeVector attributes = CalculateAttributesInternal(used);
            List<PotionAttribute> ranked = attributes.GetRankedAttributes();
            result.Attributes = attributes;
            result.TotalPotency = attributes.TotalPotency;
            if (ranked.Count > 0) result.PrimaryAttribute = ranked[0];
            result.SecondaryAttribute = FindSecondary(attributes, ranked);

            Color mixedColor = BlendColor(attributes, baseLiquid);

            // 3. 失敗判定
            if (ranked.Count == 0 || result.TotalPotency < settings.MinimumPotency)
            {
                result.Outcome = BrewOutcome.Failed;
                result.PotionId = "failed";
                result.PotionName = settings.FailedPotionName;
                result.Description = "効力が弱すぎて、何の効果もない液体になった。";
                result.PotionColor = Color.Lerp(mixedColor, settings.FailedColor, FailedColorBlend);
                result.messages.Add($"総効力 {result.TotalPotency:0.#} が成功ライン {settings.MinimumPotency:0.#} に届きませんでした");
                return result;
            }

            int gradeIndex;
            GradeTier tier = settings.GetGrade(result.TotalPotency, out gradeIndex);
            result.GradeIndex = gradeIndex;
            result.Stars = tier != null ? Mathf.Clamp(tier.stars, 1, 5) : 1;
            BuildEffects(attributes, ranked, result.effects);

            // 4. 特殊レシピ
            foreach (PotionConditionData recipe in specialRecipes)
            {
                if (!recipe.IsMatch(attributes, used)) continue;

                result.Outcome = BrewOutcome.Special;
                result.MatchedCondition = recipe;
                result.PotionId = recipe.PotionId;
                result.PotionName = recipe.PotionName;
                result.Description = string.IsNullOrEmpty(recipe.EffectDescription)
                    ? JoinEffects(result.effects)
                    : recipe.EffectDescription;
                result.PotionColor = recipe.UseCustomColor ? recipe.CustomColor : mixedColor;
                return result;
            }

            // 5. 自動生成
            PotionAttribute primary = ranked[0];
            result.Outcome = BrewOutcome.Generated;
            result.PotionId = BuildGeneratedId(baseLiquid, primary, result.SecondaryAttribute, gradeIndex);
            result.PotionName = BuildGeneratedName(tier, primary, result.SecondaryAttribute, baseLiquid);
            result.Description = JoinEffects(result.effects);
            result.PotionColor = mixedColor;
            return result;
        }

        /// <summary>
        /// 属性値だけを計算する（判定はしない）。
        /// 注ぐ操作中に「今こんな属性になっている」とリアルタイム表示する用途を想定。
        /// </summary>
        public AttributeVector CalculateAttributes(IReadOnlyList<IngredientAmount> ingredients)
        {
            return CalculateAttributesInternal(Normalize(ingredients, new List<string>()));
        }

        /// <summary>今の配合で液体が何色になるか（判定はしない）。注いでいる最中の見た目に使う</summary>
        public Color PreviewColor(IReadOnlyList<IngredientAmount> ingredients)
        {
            List<IngredientAmount> used = Normalize(ingredients, new List<string>());
            IngredientData baseLiquid = null;
            foreach (IngredientAmount entry in used)
            {
                if (entry.ingredient.Category == IngredientCategory.BaseLiquid)
                {
                    baseLiquid = entry.ingredient;
                    break;
                }
            }
            return BlendColor(CalculateAttributesInternal(used), baseLiquid);
        }

        // ---------------------------------------------------------------------
        // 内部処理
        // ---------------------------------------------------------------------

        /// <summary>空スロット・分量0を除き、同じ素材は分量を合算した新しいリストを作る</summary>
        private static List<IngredientAmount> Normalize(IReadOnlyList<IngredientAmount> ingredients, List<string> messages)
        {
            var merged = new List<IngredientAmount>();
            if (ingredients == null) return merged;

            for (int i = 0; i < ingredients.Count; i++)
            {
                IngredientAmount entry = ingredients[i];
                if (entry == null || entry.ingredient == null)
                {
                    messages.Add($"{i + 1}番目のスロットが空なので無視しました");
                    continue;
                }
                if (entry.amount <= 0f)
                {
                    messages.Add($"「{entry.ingredient.DisplayName}」は分量が0以下なので無視しました");
                    continue;
                }

                IngredientAmount existing = merged.Find(m => m.ingredient == entry.ingredient);
                if (existing != null) existing.amount += entry.amount;
                else merged.Add(new IngredientAmount(entry.ingredient, entry.amount));
            }
            return merged;
        }

        private bool ValidateComposition(List<IngredientAmount> used, List<string> messages, out IngredientData baseLiquid)
        {
            baseLiquid = null;
            int baseCount = 0;
            int mainCount = 0;
            int catalystCount = 0;

            foreach (IngredientAmount entry in used)
            {
                switch (entry.ingredient.Category)
                {
                    case IngredientCategory.BaseLiquid:
                        baseCount++;
                        if (baseLiquid == null) baseLiquid = entry.ingredient;
                        break;
                    case IngredientCategory.MainMaterial:
                        mainCount++;
                        break;
                    case IngredientCategory.Catalyst:
                        catalystCount++;
                        break;
                }
            }

            bool ok = true;
            if (baseCount == 0) { messages.Add("ベース液体が入っていません"); ok = false; }
            else if (baseCount > 1) { messages.Add($"ベース液体は1種類までです（{baseCount}種類入っています）"); ok = false; }

            if (mainCount == 0) { messages.Add("主素材が入っていません"); ok = false; }
            else if (mainCount > settings.MaxMainMaterials) { messages.Add($"主素材は{settings.MaxMainMaterials}種類までです（{mainCount}種類入っています）"); ok = false; }

            if (catalystCount > settings.MaxCatalysts) { messages.Add($"触媒は{settings.MaxCatalysts}種類までです（{catalystCount}種類入っています）"); ok = false; }

            return ok;
        }

        /// <summary>合算 → 触媒で増幅 → マイナスを0に</summary>
        private AttributeVector CalculateAttributesInternal(List<IngredientAmount> used)
        {
            var sum = new AttributeVector();
            var amplifyPercent = new float[AttributeVector.Count];

            foreach (IngredientAmount entry in used)
            {
                IngredientData data = entry.ingredient;
                float amount = entry.amount;

                foreach (AttributeValue av in data.Attributes)
                {
                    sum.Add(av.attribute, av.value * amount);
                }

                if (data.GlobalAmplifyPercent != 0f)
                {
                    for (int i = 0; i < amplifyPercent.Length; i++)
                    {
                        amplifyPercent[i] += data.GlobalAmplifyPercent * amount;
                    }
                }

                foreach (AttributeAmplifier amp in data.Amplifiers)
                {
                    amplifyPercent[(int)amp.attribute] += amp.percent * amount;
                }
            }

            // 増幅は「足し算で重ねてから1回だけ掛ける」。掛け算で重ねると倍率が爆発しやすいため
            for (int i = 0; i < amplifyPercent.Length; i++)
            {
                if (amplifyPercent[i] == 0f) continue;
                float multiplier = Mathf.Clamp(1f + amplifyPercent[i] / 100f, 0f, settings.MaxAmplifyMultiplier);
                sum.Multiply((PotionAttribute)i, multiplier);
            }

            sum.ClampNegativeToZero();
            return sum;
        }

        private PotionAttribute? FindSecondary(AttributeVector attributes, List<PotionAttribute> ranked)
        {
            if (ranked.Count < 2) return null;

            float primaryValue = attributes[ranked[0]];
            float secondValue = attributes[ranked[1]];
            if (secondValue < settings.EffectMinValue) return null;
            if (secondValue < primaryValue * settings.SecondaryMinRatio) return null;
            return ranked[1];
        }

        private void BuildEffects(AttributeVector attributes, List<PotionAttribute> ranked, List<PotionEffect> output)
        {
            foreach (PotionAttribute attribute in ranked)
            {
                float value = attributes[attribute];
                if (value < settings.EffectMinValue) continue;

                AttributeInfo info = settings.GetInfo(attribute);
                // Mathf.RoundToInt は 58.5 → 58（偶数丸め）になるため、表示用は普通の四捨五入にする
                int shown = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                output.Add(new PotionEffect(attribute, value, FormatEffect(info.effectFormat, shown)));
            }
        }

        private string BuildGeneratedName(GradeTier tier, PotionAttribute primary, PotionAttribute? secondary, IngredientData baseLiquid)
        {
            string grade = tier != null ? tier.prefix : "";
            string secondaryText = secondary.HasValue ? settings.GetInfo(secondary.Value).secondaryModifier : "";
            string primaryText = settings.GetInfo(primary).primaryName;
            string form = string.IsNullOrEmpty(baseLiquid.PotionFormName) ? DefaultFormName : baseLiquid.PotionFormName;

            // string.Replace は置換後の値が null でも空文字として扱う
            return settings.NameFormat
                .Replace("{grade}", grade)
                .Replace("{secondary}", secondaryText)
                .Replace("{primary}", primaryText)
                .Replace("{form}", form)
                .Trim();
        }

        private static string BuildGeneratedId(IngredientData baseLiquid, PotionAttribute primary, PotionAttribute? secondary, int gradeIndex)
        {
            string secondaryId = secondary.HasValue ? secondary.Value.ToString() : "None";
            return $"gen.{baseLiquid.IngredientId}.{primary}.{secondaryId}.g{gradeIndex + 1}";
        }

        /// <summary>属性値の比率で属性の色を混ぜ、ベース液体の色を少し残す</summary>
        private Color BlendColor(AttributeVector attributes, IngredientData baseLiquid)
        {
            Color baseColor = baseLiquid != null ? baseLiquid.Color : Color.white;
            float total = attributes.TotalPotency;
            if (total <= 0f) return baseColor;

            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < AttributeVector.Count; i++)
            {
                var attribute = (PotionAttribute)i;
                float value = attributes[attribute];
                if (value <= 0f) continue;

                float weight = value / total;
                Color c = settings.GetInfo(attribute).color;
                r += c.r * weight;
                g += c.g * weight;
                b += c.b * weight;
            }

            Color mixed = Color.Lerp(new Color(r, g, b, 1f), baseColor, settings.BaseLiquidColorInfluence);
            mixed.a = baseColor.a;
            return mixed;
        }

        private static string JoinEffects(List<PotionEffect> effects)
        {
            if (effects.Count == 0) return "目立った効果はない。";
            return string.Join("、", effects.Select(e => e.description));
        }

        private static string FormatEffect(string format, int value)
        {
            if (string.IsNullOrEmpty(format)) return value.ToString();
            try
            {
                return string.Format(format, value);
            }
            catch (FormatException)
            {
                return format + " " + value;
            }
        }
    }
}
