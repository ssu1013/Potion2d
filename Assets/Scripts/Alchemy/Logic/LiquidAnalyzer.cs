using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// フラスコの中身（LiquidState）から、液体の記録（LiquidRecord）を作る：分類・名前・色・効果・★・同じ液体かの判定用の指紋。
    ///   ポーション   … 総効力が「ポーションの効力」以上で、一番強い属性がはっきりしている（特別なポーションの条件に合えばその名前）
    ///   中間液体     … 効き目はあるが、まだポーションと呼べない（「薬草入りの清らかな水」「AとBの混合液」）
    ///   未知の液体   … 効き目がほとんどない・焦げた（「濁った緑の液体」。混ぜたのが液体1種類だけならその名前）
    /// </summary>
    public sealed class LiquidAnalyzer
    {
        private const string DefaultFormName = "ポーション";

        private readonly AlchemySettings settings;
        private readonly List<PotionConditionData> specialRecipes;

        public LiquidAnalyzer(AlchemySettings settings, IEnumerable<PotionConditionData> recipes)
        {
            this.settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
            specialRecipes = (recipes ?? Enumerable.Empty<PotionConditionData>())
                .Where(r => r != null && r.Specificity > 0)
                .Distinct()
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.Specificity)
                .ToList();
        }

        public LiquidRecord Analyze(LiquidState state)
        {
            var record = new LiquidRecord();
            AttributeVector c = state.concentration;
            float potency = c.TotalPotency;
            List<PotionAttribute> ranked = c.GetRankedAttributes();

            record.attributes = c.ToArray();
            foreach (LiquidTrait t in state.traits)
            {
                if (t.strength >= 0.05f) record.traits.Add(new LiquidTrait(t.id, Mathf.Round(t.strength * 100f) / 100f));
            }
            record.traits.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            record.ingredientIds.AddRange(state.ingredientIds.OrderBy(s => s, StringComparer.Ordinal));
            record.parentLiquidIds.AddRange(state.parentLiquidIds);
            record.reactionIds.AddRange(state.reactionIds);
            if (state.process.Count > 0) record.recipes.Add(new ProcessRecipe(state.process));

            LiquidState.Source mainLiquid = state.sources.Where(s => s.isLiquid).OrderByDescending(s => s.ml).FirstOrDefault();
            LiquidState.Source mainBase = state.sources.Where(s => s.isBaseLiquid).OrderByDescending(s => s.ml).FirstOrDefault();
            record.formName = mainBase != null && !string.IsNullOrEmpty(mainBase.formName) ? mainBase.formName : DefaultFormName;
            record.baseLiquidId = mainBase != null ? mainBase.id : "";

            float scorched = state.GetTrait(LiquidTraits.Scorched);
            float primaryShare = ranked.Count > 0 ? c.GetShare(ranked[0]) : 0f;

            // 分類
            if (potency < settings.MinimumPotency || scorched >= 0.6f) record.kind = LiquidKind.Unknown;
            else if (potency >= settings.PotionMinPotency && primaryShare >= settings.PotionPrimaryShare) record.kind = LiquidKind.Potion;
            else record.kind = LiquidKind.Intermediate;

            // 特別なポーション
            PotionConditionData special = null;
            if (record.kind != LiquidKind.Unknown)
            {
                foreach (PotionConditionData recipe in specialRecipes)
                {
                    if (!recipe.IsMatch(c, record.ingredientIds)) continue;
                    special = recipe;
                    break;
                }
            }
            if (special != null)
            {
                record.kind = LiquidKind.Potion;
                record.specialPotionId = special.PotionId;
            }

            // ★・効果
            if (record.kind != LiquidKind.Unknown && potency > 0f)
            {
                GradeTier tier = settings.GetGrade(potency, out _);
                record.stars = tier != null ? Mathf.Clamp(tier.stars, 1, 5) : 1;
            }
            foreach (PotionAttribute a in ranked)
            {
                float v = c[a];
                if (v < settings.EffectMinValue) continue;
                AttributeInfo info = settings.GetInfo(a);
                int shown = (int)Math.Round(v, MidpointRounding.AwayFromZero);
                record.effects.Add(FormatEffect(info.effectFormat, shown));
            }

            // 色
            record.color = BlendColor(state, c, potency, special, out record.solventColor);

            // 名前と説明
            record.autoName = BuildName(record, state, c, ranked, special, mainLiquid);
            if (special != null && !string.IsNullOrEmpty(special.EffectDescription)) record.description = special.EffectDescription;
            else if (record.effects.Count > 0) record.description = string.Join("、", record.effects);
            else record.description = record.kind == LiquidKind.Unknown ? "まだ何の効き目も感じられない液体。" : "かすかに効き目がある。";

            record.fingerprint = Fingerprint(record);
            record.id = "liq_" + record.fingerprint;
            return record;
        }

        // ------------------------------------------------------------------
        // 名前
        // ------------------------------------------------------------------

        private string BuildName(LiquidRecord record, LiquidState state, AttributeVector c, List<PotionAttribute> ranked,
                                 PotionConditionData special, LiquidState.Source mainLiquid)
        {
            if (special != null) return special.PotionName;
            string traitModifier = StrongTraitModifier(record);

            if (record.kind == LiquidKind.Potion)
            {
                GradeTier tier = settings.GetGrade(c.TotalPotency, out _);
                PotionAttribute primary = ranked[0];
                string secondary = "";
                if (ranked.Count > 1)
                {
                    float second = c[ranked[1]];
                    if (second >= settings.EffectMinValue && second >= c[primary] * settings.SecondaryMinRatio)
                        secondary = settings.GetInfo(ranked[1]).secondaryModifier;
                }
                string name = settings.NameFormat
                    .Replace("{grade}", tier != null ? tier.prefix : "")
                    .Replace("{secondary}", secondary)
                    .Replace("{primary}", settings.GetInfo(primary).primaryName)
                    .Replace("{form}", record.formName)
                    .Trim();
                return traitModifier + name;
            }

            // 何が入っているか（固体・粉は効き目の強い順、液体は量の多い順）
            List<LiquidState.Source> materials = state.sources.Where(s => !s.isLiquid && s.strength > 0.5f).OrderByDescending(s => s.strength).ToList();
            List<LiquidState.Source> liquids = MergeByName(state.sources.Where(s => s.isLiquid)).OrderByDescending(s => s.ml).ToList();
            string baseName = mainLiquid != null ? mainLiquid.name : "液";

            if (record.kind == LiquidKind.Intermediate)
            {
                if (materials.Count == 0)
                {
                    if (liquids.Count >= 2) return traitModifier + $"{liquids[0].name}と{liquids[1].name}の混合液";
                    return traitModifier + baseName;
                }
                if (materials.Count == 1) return traitModifier + $"{materials[0].name}入りの{baseName}";
                return traitModifier + $"{materials[0].name}と{materials[1].name}の混合液";
            }

            // 未知の液体：液体だけなら、その名前（例：清らかな水・濃縮された霊泉の水・清らかな水と灯り油の混合液）
            if (materials.Count == 0 && liquids.Count == 1) return traitModifier + liquids[0].name;
            if (materials.Count == 0 && liquids.Count >= 2) return traitModifier + $"{liquids[0].name}と{liquids[1].name}の混合液";
            return traitModifier + ColorName(record.color) + "の液体";
        }

        private string StrongTraitModifier(LiquidRecord record)
        {
            LiquidTrait strongest = null;
            foreach (LiquidTrait t in record.traits)
            {
                if (t.strength < 0.5f) continue;
                // 焦げは、ほかの特性より目立つ（同じくらいの強さなら「焦げた」を名前に付ける）
                float weight = t.strength + (t.id == LiquidTraits.Scorched ? 0.2f : 0f);
                float best = strongest == null ? -1f : strongest.strength + (strongest.id == LiquidTraits.Scorched ? 0.2f : 0f);
                if (weight > best) strongest = t;
            }
            if (strongest == null) return "";
            string modifier = settings.GetTrait(strongest.id).nameModifier;
            return string.IsNullOrEmpty(modifier) ? "" : modifier;
        }

        private static IEnumerable<LiquidState.Source> MergeByName(IEnumerable<LiquidState.Source> sources)
        {
            var merged = new Dictionary<string, LiquidState.Source>();
            foreach (LiquidState.Source s in sources)
            {
                if (merged.TryGetValue(s.name, out LiquidState.Source m)) m.ml += s.ml;
                else merged[s.name] = new LiquidState.Source { id = s.id, name = s.name, ml = s.ml, isLiquid = true };
            }
            return merged.Values;
        }

        /// <summary>色の名前（「うすい若葉色」「濃い赤」「濁った茶色」など）</summary>
        public static string ColorName(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (v < 0.18f) return "黒い";
            string tone = "";
            string hue;
            if (s < 0.12f) hue = v > 0.8f ? "白" : "灰色";
            else
            {
                float deg = h * 360f;
                if (deg < 12f || deg >= 340f) hue = "赤";
                else if (deg < 30f) hue = s < 0.55f && v < 0.7f ? "茶色" : "朱色";
                else if (deg < 48f) hue = v < 0.6f ? "茶色" : "橙色";
                else if (deg < 68f) hue = "黄色";
                else if (deg < 95f) hue = "若葉色";
                else if (deg < 150f) hue = "緑";
                else if (deg < 185f) hue = "青緑";
                else if (deg < 210f) hue = "水色";
                else if (deg < 250f) hue = "青";
                else if (deg < 285f) hue = "紫";
                else hue = "桃色";

                if (s < 0.3f) tone = "濁った";
                else if (v > 0.85f && s < 0.5f) tone = "うすい";
                else if (v < 0.45f) tone = "濃い";
            }
            return tone + hue;
        }

        // ------------------------------------------------------------------
        // 色
        // ------------------------------------------------------------------

        private Color BlendColor(LiquidState state, AttributeVector c, float potency, PotionConditionData special, out Color solvent)
        {
            // 注いだ液体の色（量で重みづけ）
            float totalMl = 0f, r = 0f, g = 0f, b = 0f, a = 0f;
            foreach (LiquidState.Source s in state.sources)
            {
                if (!s.isLiquid || s.ml <= 0f) continue;
                totalMl += s.ml;
                r += s.color.r * s.ml;
                g += s.color.g * s.ml;
                b += s.color.b * s.ml;
                a += s.color.a * s.ml;
            }
            Color liquid = totalMl > 0f ? new Color(r / totalMl, g / totalMl, b / totalMl, a / totalMl) : new Color(0.8f, 0.85f, 0.9f, 0.85f);
            solvent = liquid;

            // 属性の色（濃さの割合で）
            Color attribute = liquid;
            if (potency > 0f)
            {
                float ar = 0f, ag = 0f, ab = 0f;
                for (int i = 0; i < AttributeVector.Count; i++)
                {
                    var attr = (PotionAttribute)i;
                    float v = c[attr];
                    if (v <= 0f) continue;
                    Color ac = settings.GetInfo(attr).color;
                    float w = v / potency;
                    ar += ac.r * w;
                    ag += ac.g * w;
                    ab += ac.b * w;
                }
                attribute = new Color(ar, ag, ab, 1f);
            }

            // 効き目が弱いうちは液体の色のまま。強くなるほど属性の色になる
            float influence = Mathf.Lerp(1f, settings.BaseLiquidColorInfluence, Mathf.Clamp01(potency / Mathf.Max(1f, settings.PotionMinPotency)));
            Color result = Color.Lerp(attribute, liquid, influence);
            if (special != null && special.UseCustomColor) result = special.CustomColor;

            foreach (LiquidTrait t in state.traits)
            {
                TraitInfo info = settings.GetTrait(t.id);
                if (info.tintStrength <= 0f) continue;
                Color tint = info.tint;
                tint.a = result.a;
                result = Color.Lerp(result, tint, info.tintStrength * t.strength);
            }
            result.a = Mathf.Clamp(liquid.a, 0.3f, 1f);
            return result;
        }

        // ------------------------------------------------------------------
        // 同じ液体かの判定
        // ------------------------------------------------------------------

        /// <summary>
        /// 結果が「ほぼ同じ」なら同じ文字列になる指紋。属性は 3 きざみ、特性は 0.25 きざみ、色は 12 段階でまとめる。
        /// 素材の量が少し違っても同じ液体として扱い、瓶もレシピ本のページもまとまる。
        /// </summary>
        public static string Fingerprint(LiquidRecord record)
        {
            var sb = new StringBuilder();
            sb.Append((int)record.kind).Append('|').Append(record.specialPotionId).Append('|');
            for (int i = 0; i < record.attributes.Length; i++) sb.Append(Mathf.RoundToInt(record.attributes[i] / 3f)).Append(',');
            sb.Append('|');
            foreach (LiquidTrait t in record.traits)
            {
                int level = Mathf.RoundToInt(t.strength * 4f);
                if (level > 0) sb.Append(t.id).Append(level).Append(',');
            }
            sb.Append('|')
              .Append(Mathf.RoundToInt(record.color.r * 11f)).Append(',')
              .Append(Mathf.RoundToInt(record.color.g * 11f)).Append(',')
              .Append(Mathf.RoundToInt(record.color.b * 11f));
            if (record.kind == LiquidKind.Unknown) sb.Append('|').Append(record.autoName); // 「清らかな水」と「霊泉の水」を分ける
            return Hash(sb.ToString());
        }

        private static string Hash(string text)
        {
            // FNV-1a（64bit）。実行環境に関係なく同じ値になる
            ulong hash = 14695981039346656037UL;
            foreach (char ch in text)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16").Substring(0, 12);
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
