using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// フラスコの中で起きていることの計算（MonoBehaviour に依存しない純粋な C#。自動テスト・予想表示・ゲームで共通）。
    ///
    /// ・量は ml（素材の「1単位」= 100ml）。属性は液体に溶けた「量」で持ち、表示や判定は「100ml あたりの濃さ」にする
    ///   → 水を足すと薄まり、煮詰めると濃くなる
    /// ・固体・粉は少しずつ溶ける（溶けた分だけ効く）。かき混ぜる・温めると溶けきる
    /// ・触媒は、溶けたあとにフラスコへ入ってきた（溶けた）分だけを強める → 入れる順番に意味がある
    /// ・熱に弱い素材は熱いと弱まり、目覚める温度がある素材は温めると強まる
    /// ・時計回りは一番強い属性を伸ばし、反時計回りは2番目の属性を伸ばす。混ぜすぎると泡立つ
    /// ・沸騰すると液が減って濃くなり、煮詰めすぎると焦げる
    /// ・反応データの条件がそろうと、属性が別の属性に変わったり特性が付いたりする
    /// 時間は 0.1 秒きざみで進めるので、同じ操作なら何度やっても同じ結果になる。
    /// </summary>
    public sealed class FlaskSimulation
    {
        public const float MlPerUnit = 100f;
        public const float StepSeconds = 0.1f;
        private const float RecentStirWindow = 10f;
        private static readonly int AttributeCount = AttributeVector.Count;

        private readonly AlchemySettings settings;
        private readonly BrewRules rules;
        private readonly List<ReactionData> reactions = new List<ReactionData>();

        private readonly List<Portion> portions = new List<Portion>();
        private readonly float[] amplifierPool = new float[AttributeCount];
        private readonly Dictionary<string, float> intrinsicTraits = new Dictionary<string, float>();
        private readonly Dictionary<string, float> carriedTraitMl = new Dictionary<string, float>();
        private readonly HashSet<string> carriedIngredientIds = new HashSet<string>();
        private readonly List<string> parentLiquidIds = new List<string>();
        private readonly List<string> firedReactions = new List<string>();
        private readonly Dictionary<string, float> reactionProgress = new Dictionary<string, float>();
        private readonly List<StirSample> recentStirs = new List<StirSample>();
        private readonly List<ProcessStep> log = new List<ProcessStep>();

        private float globalAmplifyPool;
        private double accumulator;
        private float lastActionTime;
        private float waitStartTemperature;
        private float evaporatedMl;
        private int nextOrder;

        public float CapacityMl { get; }
        public float VolumeMl { get; private set; }
        public float Temperature { get; private set; }
        public float HeatLevel { get; private set; }
        public float Time { get; private set; }
        public float TotalTurns { get; private set; }
        public bool IsBoiling { get; private set; }
        public float Scorch => GetIntrinsic(LiquidTraits.Scorched);
        /// <summary>今、焦げが進んでいる（煮詰まって焦げつく音・煙の目安）</summary>
        public bool IsScorching { get; private set; }
        public IReadOnlyList<ProcessStep> Log => log;
        public IReadOnlyList<string> FiredReactions => firedReactions;
        public AlchemySettings Settings => settings;

        /// <summary>その素材・液体をこのフラスコに入れた量（単位。液体は ml/100）</summary>
        public float UnitsOf(string sourceId)
        {
            float units = 0f;
            foreach (Portion p in portions)
            {
                if (p.sourceId == sourceId) units += p.units;
            }
            return units;
        }

        /// <summary>何も入っていない（溶け残りもない）</summary>
        public bool IsEmpty => portions.Count == 0;

        /// <summary>反応が起きたとき（演出・お知らせ用）</summary>
        public event Action<ReactionData> ReactionOccurred;

        private sealed class Portion
        {
            public IngredientData ingredient;   // 素材（瓶詰めした液体なら null）
            public string sourceId = "";
            public string sourceName = "";
            public string formName = "";
            public Color color = Color.white;
            public bool isLiquid;               // 注いだ物（量は ml として扱う）
            public bool isBaseLiquid;           // ベース液体（名前の「ポーション/オイル…」と瓶選びに使う）
            public float units;                 // 入れた量（単位）。液体は ml/100
            public float undissolved;           // まだ溶けていない量（単位）
            public float[] essence = new float[AttributeCount]; // 溶けた属性の量（符号つき）
            public float quality = 1f;          // 熱で弱まった割合（これから溶ける分にも掛かる）
            public float activation;            // 力の目覚め 0〜1
            public float turnsSinceAdded;       // 入れてからかき混ぜた回数（溶け方に使う）
            public float clockwiseUsed, clockwiseOthersUsed, counterUsed, counterPrimaryUsed;
            public int order;
            // 画面の手ごたえ・素材ノート用の「起きたこと」（計算には使わない。セーブしない）
            public bool heatDamaged, melted, meltBlocked, broken, evaporated, cooled, spunRight, spunWrong;

            public float DissolvedFraction => units > 0f ? 1f - undissolved / units : 1f;
        }

        private struct StirSample
        {
            public float time;
            public float turns;
            public bool clockwise;
        }

        public FlaskSimulation(AlchemySettings settings, IEnumerable<ReactionData> reactions, float capacityMl)
        {
            this.settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
            rules = settings.Rules;
            if (reactions != null)
            {
                foreach (ReactionData r in reactions)
                {
                    if (r != null && !this.reactions.Contains(r)) this.reactions.Add(r);
                }
            }
            CapacityMl = Mathf.Max(10f, capacityMl);
            Temperature = rules.roomTemperature;
            waitStartTemperature = Temperature;
        }

        // ------------------------------------------------------------------
        // 操作
        // ------------------------------------------------------------------

        public float RoomMl => Mathf.Max(0f, CapacityMl - VolumeMl);

        /// <summary>
        /// 液体の量（ml）。溶け残った固体・粉が場所を取っている分を除く（溶けた分は液体に入る）。
        /// 0 なら「溶け残りだけ」で、溶けることも、沸騰することも、瓶に詰めることもできない
        /// </summary>
        public float LiquidMl
        {
            get
            {
                float solid = 0f;
                foreach (Portion p in portions)
                {
                    if (p.isLiquid || p.ingredient == null || p.undissolved <= 0f) continue;
                    solid += p.undissolved * (p.ingredient.Form == IngredientForm.Powder ? rules.powderVolumeMlPerUnit : rules.solidVolumeMlPerUnit);
                }
                return Mathf.Max(0f, VolumeMl - solid);
            }
        }

        /// <summary>瓶に詰められるだけの液体がある</summary>
        public bool HasLiquid => LiquidMl > 0.5f;

        /// <summary>液体の素材を注ぐ。実際に入った量（ml）を返す</summary>
        public float Pour(IngredientData ingredient, float ml)
        {
            if (ingredient == null || ml <= 0f) return 0f;
            ml = Mathf.Min(ml, RoomMl);
            if (ml <= 0f) return 0f;
            NoteAction();
            float units = ml / MlPerUnit;
            Portion p = FindLast(ingredient.IngredientId, true) ?? NewPortion(ingredient, true);
            p.units += units;
            VolumeMl += ml;
            Dissolve(p, units, ingredient);
            AppendStep(ProcessStepType.Pour, ingredient.IngredientId, ingredient.DisplayName, ml);
            return ml;
        }

        /// <summary>瓶詰めした液体を注ぐ（多段調合）。実際に入った量（ml）を返す</summary>
        public float PourLiquid(LiquidRecord liquid, float ml)
        {
            if (liquid == null || ml <= 0f) return 0f;
            ml = Mathf.Min(ml, RoomMl);
            if (ml <= 0f) return 0f;
            NoteAction();
            float units = ml / MlPerUnit;
            Portion p = FindLast(liquid.id, true);
            if (p == null)
            {
                p = AddPortion(new Portion
                {
                    sourceId = liquid.id,
                    sourceName = liquid.DisplayName,
                    formName = liquid.formName,
                    // 液体そのものの色を使う（できあがりの色を使うと、注ぎ戻すたびに属性の色が重なってずれていく）
                    color = liquid.solventColor.a > 0f ? liquid.solventColor : liquid.color,
                    isLiquid = true,
                    isBaseLiquid = true,
                });
            }
            p.units += units;
            VolumeMl += ml;

            // 液体の中身（濃さ × 量）がそのまま入る。触媒が溶けていれば、それで強まる
            float[] amp = CurrentAmplifiers();
            for (int i = 0; i < AttributeCount && i < liquid.attributes.Length; i++)
            {
                p.essence[i] += liquid.attributes[i] * units * amp[i];
            }
            foreach (LiquidTrait t in liquid.traits)
            {
                carriedTraitMl.TryGetValue(t.id, out float v);
                carriedTraitMl[t.id] = v + t.strength * ml;
            }
            foreach (string id in liquid.ingredientIds) carriedIngredientIds.Add(id);
            if (!parentLiquidIds.Contains(liquid.id)) parentLiquidIds.Add(liquid.id);
            AppendStep(ProcessStepType.PourLiquid, liquid.id, liquid.DisplayName, ml);
            return ml;
        }

        /// <summary>固体・粉を入れる（溶けるのは時間とともに）。入った量（単位）を返す</summary>
        public float AddSolid(IngredientData ingredient, float units)
        {
            if (ingredient == null || units <= 0f) return 0f;
            float perUnit = ingredient.Form == IngredientForm.Powder ? rules.powderVolumeMlPerUnit : rules.solidVolumeMlPerUnit;
            if (perUnit > 0f) units = Mathf.Min(units, RoomMl / perUnit);
            if (units <= 0f) return 0f;
            NoteAction();
            Portion p = FindLast(ingredient.IngredientId, false) ?? NewPortion(ingredient, false);
            p.units += units;
            p.undissolved += units;
            // 入れるとフラスコが冷える素材（氷霜石など）。中身が多いほど冷えにくい
            if (ingredient.CoolTemp > 0f)
            {
                float k = Mathf.Clamp(rules.coolVolumeMl / Mathf.Max(rules.minVolumeMl, VolumeMl), 0.25f, 2f);
                Temperature = Mathf.Max(rules.minTemperature, Temperature - ingredient.CoolTemp * units * k);
                p.cooled = true;
            }
            VolumeMl += units * perUnit;
            AppendStep(ProcessStepType.Add, ingredient.IngredientId, ingredient.DisplayName, units);
            return units;
        }

        /// <summary>かき混ぜる（回転数は小数でよい。操作の途中で少しずつ渡す）</summary>
        public void Stir(float turns, bool clockwise)
        {
            if (turns <= 0f || portions.Count == 0) return;
            NoteAction();
            TotalTurns += turns;
            recentStirs.Add(new StirSample { time = Time, turns = turns, clockwise = clockwise });
            foreach (Portion p in portions) p.turnsSinceAdded += turns;
            StirDissolve(turns, clockwise);
            FragileStep(turns);
            ApplyStirBalance(turns, clockwise);
            AppendStep(ProcessStepType.Stir, "", "", turns, clockwise);
        }

        /// <summary>火加減（0 = 消す 〜 1 = 最大）</summary>
        public void SetHeat(float level)
        {
            level = Mathf.Clamp01(level);
            if (Mathf.Abs(level - HeatLevel) < 0.001f) return;
            NoteAction();
            HeatLevel = level;
            ProcessStep last = log.Count > 0 ? log[log.Count - 1] : null;
            if (last != null && last.type == ProcessStepType.Heat) last.amount = level; // つまみを回している途中は1行にまとめる
            else AppendStep(ProcessStepType.Heat, "", "", level);
        }

        /// <summary>時間を進める（0.1 秒きざみで計算する。早送りは大きな値を渡すだけ）</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0f) return;
            accumulator += seconds;
            while (accumulator >= StepSeconds - 1e-6)
            {
                accumulator -= StepSeconds;
                Step(StepSeconds);
            }
        }

        /// <summary>
        /// 瓶詰めする：今の中身のうち ml だけを取り出して記録を作る（溶け残りの固体はフラスコに残る）。
        /// 中身がなくなったら工程の記録を新しく始める。取り出せなければ null。
        /// </summary>
        public LiquidState Bottle(float ml, out float bottledMl)
        {
            bottledMl = 0f;
            float liquidMl = LiquidMl;
            if (liquidMl <= 0.5f) return null;
            LiquidState state = Snapshot();
            // 取り出せるのは液体だけ（溶け残った固体はフラスコの底に残る）
            float take = Mathf.Min(ml, liquidMl);
            float f = take / VolumeMl;
            bottledMl = take;

            foreach (Portion p in portions)
            {
                for (int i = 0; i < AttributeCount; i++) p.essence[i] *= 1f - f;
                if (p.isLiquid) p.units *= 1f - f;
            }
            for (int i = 0; i < AttributeCount; i++) amplifierPool[i] *= 1f - f;
            globalAmplifyPool *= 1f - f;
            var keys = new List<string>(carriedTraitMl.Keys);
            foreach (string k in keys) carriedTraitMl[k] *= 1f - f;
            evaporatedMl *= 1f - f;
            VolumeMl -= take;
            if (LiquidMl <= 0.5f) StartOver();
            return state;
        }

        // ------------------------------------------------------------------
        // 今の状態
        // ------------------------------------------------------------------

        /// <summary>今の中身を計算する（予想表示・瓶詰めに使う）</summary>
        public LiquidState Snapshot()
        {
            var state = new LiquidState();
            state.volumeMl = VolumeMl;
            state.temperature = Temperature;
            state.heatLevel = HeatLevel;
            state.time = Time;

            var positive = new float[AttributeCount];
            var negative = new float[AttributeCount];
            float totalUnits = 0f, dissolvedUnits = 0f;
            foreach (Portion p in portions)
            {
                float volumeUnits = VolumeUnitsFor(p);
                for (int i = 0; i < AttributeCount; i++)
                {
                    float e = p.essence[i] / volumeUnits;
                    if (e > 0f) positive[i] += e; else negative[i] -= e;
                }
                if (!p.isLiquid)
                {
                    totalUnits += p.units;
                    dissolvedUnits += p.units - p.undissolved;
                }

                // 名前・色・分類のための「何がどれだけ入っているか」
                float strength = 0f;
                for (int i = 0; i < AttributeCount; i++) strength += Mathf.Abs(p.essence[i]);
                state.sources.Add(new LiquidState.Source
                {
                    id = p.sourceId,
                    name = p.sourceName,
                    isLiquid = p.isLiquid,
                    isBaseLiquid = p.isBaseLiquid,
                    ml = p.isLiquid ? p.units * MlPerUnit : 0f,
                    strength = strength / volumeUnits,
                    color = p.color,
                    formName = p.formName,
                    undissolvedUnits = p.undissolved,
                });
                if (p.ingredient != null) state.ingredientIds.Add(p.ingredient.IngredientId);
            }
            foreach (string id in carriedIngredientIds) state.ingredientIds.Add(id);
            state.parentLiquidIds.AddRange(parentLiquidIds);
            state.reactionIds.AddRange(firedReactions);
            state.dissolvedFraction = totalUnits > 0f ? dissolvedUnits / totalUnits : 1f;

            float cancelled = 0f, sumPositive = 0f;
            for (int i = 0; i < AttributeCount; i++)
            {
                float net = positive[i] - negative[i];
                state.concentration[(PotionAttribute)i] = Mathf.Max(0f, net);
                cancelled += Mathf.Min(positive[i], negative[i]);
                sumPositive += positive[i];
            }

            // 特性：工程で付いた物・混ぜた液体から持ち込まれた物・その場で決まる物
            foreach (KeyValuePair<string, float> t in intrinsicTraits) state.SetTrait(t.Key, t.Value);
            foreach (KeyValuePair<string, float> t in carriedTraitMl)
            {
                float s = Mathf.Clamp01(t.Value / Mathf.Max(VolumeMl, rules.minVolumeMl));
                state.SetTrait(t.Key, Mathf.Max(state.GetTrait(t.Key), s));
            }
            float foam = Mathf.InverseLerp(rules.foamStartTurns, rules.foamFullTurns, TotalTurns);
            if (foam > 0f) state.SetTrait(LiquidTraits.Foamy, Mathf.Max(state.GetTrait(LiquidTraits.Foamy), foam));
            if (sumPositive > 0f)
            {
                float cloudy = Mathf.Clamp01((cancelled / sumPositive - 0.15f) * 2.5f);
                if (cloudy > 0f) state.SetTrait(LiquidTraits.Cloudy, Mathf.Max(state.GetTrait(LiquidTraits.Cloudy), cloudy));
            }
            float remaining = VolumeMl / Mathf.Max(1f, VolumeMl + evaporatedMl);
            if (evaporatedMl > 0f && remaining <= rules.concentratedRatio)
            {
                float c = Mathf.Clamp01(0.5f + (rules.concentratedRatio - remaining) / rules.concentratedRatio);
                state.SetTrait(LiquidTraits.Concentrated, Mathf.Max(state.GetTrait(LiquidTraits.Concentrated), c));
            }

            foreach (ProcessStep s in log) state.process.Add(s.Clone());
            return state;
        }

        // ------------------------------------------------------------------
        // 画面の手ごたえ用（読むだけ。中身は変えない）
        // ------------------------------------------------------------------

        /// <summary>溶け残っている固体・粉の1つ分（フラスコの底に見せる）</summary>
        public struct UndissolvedPiece
        {
            public IngredientData ingredient;
            public Color color;
            public float undissolved;
            public float units;
            public int order;
            public bool powder;
        }

        /// <summary>素材の、熱で弱まった割合と力の目覚め（同じ素材が何回分か入っていれば、いちばん弱まった・目覚めた値）</summary>
        public struct IngredientState
        {
            public IngredientData ingredient;
            public float quality;
            public float activation;
            public float units;
            public float undissolved;
            public bool heatDamaged, melted, meltBlocked, broken, evaporated, cooled, spunRight, spunWrong;
        }

        /// <summary>入れた固体・粉の量（単位。溶けた分も含む）</summary>
        public float SolidUnits
        {
            get
            {
                float total = 0f;
                foreach (Portion p in portions) if (!p.isLiquid) total += p.units;
                return total;
            }
        }

        /// <summary>入っている素材ごとの、熱で弱まった割合（1 = 弱まっていない）と目覚め（0〜1）</summary>
        public void GetIngredientStates(List<IngredientState> result)
        {
            result.Clear();
            foreach (Portion p in portions)
            {
                if (p.ingredient == null) continue;
                int index = -1;
                for (int i = 0; i < result.Count; i++) if (result[i].ingredient == p.ingredient) { index = i; break; }
                IngredientState s = index >= 0 ? result[index] : new IngredientState { ingredient = p.ingredient, quality = 1f };
                s.quality = Mathf.Min(s.quality, p.quality);
                s.activation = Mathf.Max(s.activation, p.activation);
                if (!p.isLiquid)
                {
                    s.units += p.units;
                    s.undissolved += p.undissolved;
                }
                s.heatDamaged |= p.heatDamaged;
                s.melted |= p.melted;
                s.meltBlocked |= p.meltBlocked;
                s.broken |= p.broken;
                s.evaporated |= p.evaporated;
                s.cooled |= p.cooled;
                s.spunRight |= p.spunRight;
                s.spunWrong |= p.spunWrong;
                if (index >= 0) result[index] = s;
                else result.Add(s);
            }
        }

        /// <summary>固体・粉の溶けた割合（全部溶けていれば 1。Snapshot より軽い）</summary>
        public float DissolvedFraction
        {
            get
            {
                float total = 0f, left = 0f;
                foreach (Portion p in portions)
                {
                    if (p.isLiquid) continue;
                    total += p.units;
                    left += p.undissolved;
                }
                return total > 0f ? 1f - left / total : 1f;
            }
        }

        /// <summary>溶け残りの一覧（入れた順）</summary>
        public void GetUndissolved(List<UndissolvedPiece> result)
        {
            result.Clear();
            foreach (Portion p in portions)
            {
                if (p.isLiquid || p.undissolved <= 1e-4f) continue;
                result.Add(new UndissolvedPiece
                {
                    ingredient = p.ingredient,
                    color = p.color,
                    undissolved = p.undissolved,
                    units = p.units,
                    order = p.order,
                    powder = p.ingredient != null && p.ingredient.Form == IngredientForm.Powder,
                });
            }
        }

        /// <summary>フラスコに入っている素材（重ならないように。混ぜた液体の中の素材は入らない）</summary>
        public void GetIngredients(List<IngredientData> result)
        {
            result.Clear();
            foreach (Portion p in portions)
            {
                if (p.ingredient != null && !result.Contains(p.ingredient)) result.Add(p.ingredient);
            }
        }

        /// <summary>
        /// 溶け残りが今の状態のまま全部溶けたら、属性の濃さ（100ml あたり）がどこまで行くかの予想（「これから増える分」の表示用）。
        /// 触媒の強め・熱で弱まった割合・目覚めは今の値を使う（この先の温度・かき混ぜる向きで変わる分は入れない）。
        /// result は属性の数（AttributeVector.Count）の長さ
        /// </summary>
        public void ProjectConcentration(float[] result)
        {
            var positive = new float[AttributeCount];
            var negative = new float[AttributeCount];
            float[] amp = CurrentAmplifiers();
            var extra = new float[AttributeCount];
            foreach (Portion p in portions)
            {
                for (int i = 0; i < AttributeCount; i++) extra[i] = 0f;
                if (p.ingredient != null && p.undissolved > 0f)
                {
                    float activationMul = 1f + p.ingredient.ActivationBonus / 100f * p.activation;
                    foreach (AttributeValue av in p.ingredient.Attributes)
                    {
                        int i = (int)av.attribute;
                        float value = av.value * p.undissolved * p.quality;
                        if (value > 0f) value *= activationMul;
                        extra[i] += value * amp[i];
                    }
                }
                float volumeUnits = VolumeUnitsFor(p);
                for (int i = 0; i < AttributeCount; i++)
                {
                    float e = (p.essence[i] + extra[i]) / volumeUnits;
                    if (e > 0f) positive[i] += e; else negative[i] -= e;
                }
            }
            for (int i = 0; i < AttributeCount && i < result.Length; i++) result[i] = Mathf.Max(0f, positive[i] - negative[i]);
        }

        // ------------------------------------------------------------------
        // 時間の計算（0.1 秒ぶん）
        // ------------------------------------------------------------------

        private void Step(float dt)
        {
            Time += dt;
            UpdateTemperature(dt);

            // 溶けるのは液体があるときだけ（溶け残りだけのときは、そのまま次の調合を待つ）
            if (HasLiquid)
            {
                foreach (Portion p in portions)
                {
                    if (p.undissolved > 0f && p.ingredient != null) DissolveStep(p, dt);
                }
            }

            foreach (Portion p in portions)
            {
                if (p.ingredient == null) continue;
                HeatDamageStep(p, dt);
                ActivationStep(p, dt);
                if (IsBoiling) VolatileStep(p, dt);
            }

            ScorchStep(dt);
            ReactionStep(dt);
            recentStirs.RemoveAll(s => Time - s.time > RecentStirWindow);
        }

        private void UpdateTemperature(float dt)
        {
            float target = rules.roomTemperature + HeatLevel * (rules.maxHeatTemperature - rules.roomTemperature);
            float rate = (target > Temperature ? rules.heatingRate : rules.coolingRate) / (1f + VolumeMl / Mathf.Max(1f, rules.heatVolumeMl));
            Temperature += (target - Temperature) * (1f - Mathf.Exp(-rate * dt));

            IsBoiling = false;
            float liquidMl = LiquidMl;
            if (liquidMl > 0.5f && Temperature >= rules.boilingPoint)
            {
                Temperature = rules.boilingPoint;
                if (target > rules.boilingPoint)
                {
                    IsBoiling = true;
                    float excess = Mathf.Clamp01((target - rules.boilingPoint) / Mathf.Max(1f, rules.maxHeatTemperature - rules.boilingPoint));
                    // 残りが少なくなると蒸発はゆっくりになる（底に煮詰まった液が残り、焦げていく）
                    float slow = Mathf.Clamp01((VolumeMl - rules.minVolumeMl * 0.5f) / Mathf.Max(1f, rules.scorchBelowMl));
                    float evaporate = Mathf.Min(liquidMl, rules.boilEvaporationMlPerSec * Mathf.Max(0.2f, excess) * slow * dt);
                    VolumeMl -= evaporate;
                    evaporatedMl += evaporate;
                }
            }
        }

        private void DissolveStep(Portion p, float dt)
        {
            if (!CanMelt(p)) return; // 溶ける温度に届いていない（樹脂など）
            float sol = p.ingredient.Solubility;
            float heat = Mathf.Clamp01((Temperature - rules.roomTemperature) / 60f);
            // 放っておいて溶ける上限（温めると上がる）。それ以上は、かき混ぜたときに溶ける（StirDissolve）
            float cap = Mathf.Clamp01(Mathf.Lerp(rules.naturalDissolveMin, rules.naturalDissolveMax, sol) + rules.heatDissolveBonus * heat);
            float current = p.DissolvedFraction;
            if (current >= cap - 1e-5f) return;
            float k = rules.dissolveRate * (0.5f + sol) * (1f + heat);
            float delta = (cap - current) * (1f - Mathf.Exp(-k * dt));
            float units = Mathf.Min(p.undissolved, delta * p.units);
            if (units <= 0f) return;
            p.undissolved -= units;
            if (p.undissolved < 1e-5f) p.undissolved = 0f;
            if (p.ingredient.MeltTemp > 0f) p.melted = true;
            Dissolve(p, units, p.ingredient);
        }

        /// <summary>溶ける温度がある素材は、その温度に届いているときだけ溶ける</summary>
        private bool CanMelt(Portion p)
        {
            return p.ingredient == null || p.ingredient.MeltTemp <= 0f || Temperature >= p.ingredient.MeltTemp;
        }

        /// <summary>かき混ぜると、その場で溶ける（溶けやすい物・温かいほどよく溶ける）。溶ける向きが決まっている素材は、その向きのときだけ</summary>
        private void StirDissolve(float turns, bool clockwise)
        {
            if (!HasLiquid) return;
            float heat = Mathf.Clamp01((Temperature - rules.roomTemperature) / 60f);
            foreach (Portion p in portions)
            {
                if (p.ingredient == null || p.undissolved <= 0f) continue;
                if (!CanMelt(p))
                {
                    p.meltBlocked = true;
                    continue;
                }
                StirDirection only = p.ingredient.DissolveDirection;
                if ((only == StirDirection.Clockwise && !clockwise) || (only == StirDirection.CounterClockwise && clockwise))
                {
                    p.spunWrong = true;
                    continue;
                }
                if (only != StirDirection.Any) p.spunRight = true;
                if (p.ingredient.MeltTemp > 0f) p.melted = true;
                float fraction = turns * rules.stirDissolvePerTurn * (0.5f + p.ingredient.Solubility) * (1f + heat * 0.5f);
                float units = Mathf.Min(p.undissolved, fraction * p.units);
                if (units <= 0f) continue;
                p.undissolved -= units;
                if (p.undissolved < 1e-5f) p.undissolved = 0f;
                Dissolve(p, units, p.ingredient);
            }
        }

        /// <summary>溶けた分の属性をフラスコに入れる（今溶けている触媒で強まる）。触媒自身は「強める力」として溶け込む</summary>
        private void Dissolve(Portion p, float units, IngredientData data)
        {
            float[] amp = CurrentAmplifiers();
            float activationMul = 1f + data.ActivationBonus / 100f * p.activation;
            foreach (AttributeValue av in data.Attributes)
            {
                int i = (int)av.attribute;
                float value = av.value * units * p.quality;
                if (value > 0f) value *= activationMul;
                p.essence[i] += value * amp[i];
            }
            if (data.GlobalAmplifyPercent != 0f) globalAmplifyPool += data.GlobalAmplifyPercent * units;
            foreach (AttributeAmplifier a in data.Amplifiers) amplifierPool[(int)a.attribute] += a.percent * units;
        }

        /// <summary>今溶けている触媒による倍率（濃さで効く。薄めると弱くなる）</summary>
        private float[] CurrentAmplifiers()
        {
            var result = new float[AttributeCount];
            float volumeUnits = Mathf.Max(VolumeMl, MlPerUnit) / MlPerUnit; // 100ml 未満でも強くなりすぎないように
            for (int i = 0; i < AttributeCount; i++)
            {
                float percent = (globalAmplifyPool + amplifierPool[i]) / volumeUnits;
                result[i] = Mathf.Clamp(1f + percent / 100f, 0f, settings.MaxAmplifyMultiplier);
            }
            return result;
        }

        private void HeatDamageStep(Portion p, float dt)
        {
            float limit = p.ingredient.HeatLimit;
            if (limit <= 0f || Temperature <= limit) return;
            float rate = rules.heatDamageScale * p.ingredient.HeatSensitivity * (Temperature - limit) / 100f;
            float factor = Mathf.Exp(-rate * dt);
            if (factor < 0.99999f) p.heatDamaged = true;
            p.quality *= factor;
            for (int i = 0; i < AttributeCount; i++)
            {
                if (p.essence[i] > 0f) p.essence[i] *= factor;
            }
        }

        /// <summary>崩れやすい素材：入れてから決まった回転数より多く混ぜると、崩れて効き目が減っていく</summary>
        private void FragileStep(float turns)
        {
            foreach (Portion p in portions)
            {
                if (p.ingredient == null) continue;
                float limit = p.ingredient.FragileTurns;
                if (limit <= 0f) continue;
                float over = Mathf.Min(turns, p.turnsSinceAdded - limit);
                if (over <= 0f) continue;
                float factor = Mathf.Exp(-rules.fragileDamagePerTurn * over);
                p.quality *= factor;
                for (int i = 0; i < AttributeCount; i++)
                {
                    if (p.essence[i] > 0f) p.essence[i] *= factor;
                }
                p.broken = true;
            }
        }

        /// <summary>香りが飛ぶ素材：沸騰している間、溶けた力が少しずつ飛んでいく</summary>
        private void VolatileStep(Portion p, float dt)
        {
            float v = p.ingredient.Volatility;
            if (v <= 0f) return;
            float factor = Mathf.Exp(-v * rules.volatileLossPerSec * dt);
            bool any = false;
            for (int i = 0; i < AttributeCount; i++)
            {
                if (p.essence[i] <= 0f) continue;
                p.essence[i] *= factor;
                any = true;
            }
            if (any) p.evaporated = true;
        }

        private void ActivationStep(Portion p, float dt)
        {
            float t = p.ingredient.ActivationTemp;
            if (t <= 0f || p.activation >= 1f || Temperature < t) return;
            float before = 1f + p.ingredient.ActivationBonus / 100f * p.activation;
            p.activation = Mathf.Min(1f, p.activation + dt / Mathf.Max(0.1f, rules.activationSeconds));
            float after = 1f + p.ingredient.ActivationBonus / 100f * p.activation;
            if (before <= 0f) return;
            float scale = after / before;
            for (int i = 0; i < AttributeCount; i++)
            {
                if (p.essence[i] > 0f) p.essence[i] *= scale;
            }
        }

        private void ScorchStep(float dt)
        {
            bool hot = Temperature >= rules.boilingPoint - 0.5f && HeatLevel > 0f;
            // 量が少ないか、煮詰める前の量から減りすぎたら焦げる（煮詰めて濃くできるのは、だいたい 1 / scorchBelowRatio 倍まで）
            float remaining = VolumeMl / Mathf.Max(1f, VolumeMl + evaporatedMl);
            bool little = VolumeMl < rules.scorchBelowMl || (evaporatedMl > 0f && remaining < rules.scorchBelowRatio);
            IsScorching = hot && little && portions.Count > 0;
            if (!IsScorching) return;
            float s = Mathf.Min(1f, GetIntrinsic(LiquidTraits.Scorched) + rules.scorchRate * Mathf.Max(0.3f, HeatLevel) * dt);
            intrinsicTraits[LiquidTraits.Scorched] = s;
            float factor = Mathf.Exp(-rules.scorchDamagePerSec * s * dt);
            foreach (Portion p in portions)
            {
                for (int i = 0; i < AttributeCount; i++)
                {
                    if (p.essence[i] > 0f) p.essence[i] *= factor;
                }
            }
        }

        // ------------------------------------------------------------------
        // かき混ぜる向き
        // ------------------------------------------------------------------

        private void ApplyStirBalance(float turns, bool clockwise)
        {
            var total = new AttributeVector();
            foreach (Portion p in portions)
            {
                for (int i = 0; i < AttributeCount; i++) total.Add((PotionAttribute)i, p.essence[i]);
            }
            List<PotionAttribute> ranked = total.GetRankedAttributes();
            if (ranked.Count == 0) return;
            int primary = (int)ranked[0];
            int secondary = ranked.Count > 1 ? (int)ranked[1] : -1;

            foreach (Portion p in portions)
            {
                if (clockwise)
                {
                    float inc = Mathf.Min(turns * rules.clockwisePrimaryPerTurn, Mathf.Max(0f, rules.clockwisePrimaryMax - p.clockwiseUsed));
                    float dec = Mathf.Min(turns * rules.clockwiseOthersPerTurn, Mathf.Max(0f, rules.clockwiseOthersMax - p.clockwiseOthersUsed));
                    p.clockwiseUsed += inc;
                    p.clockwiseOthersUsed += dec;
                    for (int i = 0; i < AttributeCount; i++)
                    {
                        if (p.essence[i] <= 0f) continue;
                        p.essence[i] *= i == primary ? 1f + inc : 1f - dec;
                    }
                }
                else
                {
                    if (secondary < 0) continue;
                    float inc = Mathf.Min(turns * rules.counterSecondaryPerTurn, Mathf.Max(0f, rules.counterSecondaryMax - p.counterUsed));
                    float dec = Mathf.Min(turns * rules.counterPrimaryPerTurn, Mathf.Max(0f, rules.counterPrimaryMax - p.counterPrimaryUsed));
                    p.counterUsed += inc;
                    p.counterPrimaryUsed += dec;
                    if (p.essence[secondary] > 0f) p.essence[secondary] *= 1f + inc;
                    if (p.essence[primary] > 0f) p.essence[primary] *= 1f - dec;
                }
            }
        }

        // ------------------------------------------------------------------
        // 反応
        // ------------------------------------------------------------------

        private void ReactionStep(float dt)
        {
            if (reactions.Count == 0 || VolumeMl <= 0f) return;
            AttributeVector concentration = null;
            foreach (ReactionData r in reactions)
            {
                if (firedReactions.Contains(r.ReactionId)) continue;
                if (concentration == null) concentration = CurrentConcentration();
                bool ok = ReactionConditionsMet(r, concentration);
                reactionProgress.TryGetValue(r.ReactionId, out float progress);
                progress = ok ? progress + dt : 0f;
                reactionProgress[r.ReactionId] = progress;
                if (ok && progress >= r.Duration)
                {
                    ApplyReaction(r);
                    concentration = null;
                }
            }
        }

        private bool ReactionConditionsMet(ReactionData r, AttributeVector concentration)
        {
            if (Temperature < r.MinTemperature || Temperature > r.MaxTemperature) return false;
            foreach (IngredientData required in r.RequiredIngredients)
            {
                if (required == null) continue;
                float dissolved = 0f;
                foreach (Portion p in portions)
                {
                    if (p.ingredient == required) dissolved += p.units - p.undissolved;
                }
                if (dissolved < r.MinDissolvedUnits) return false;
            }
            if (r.MinRecentTurns > 0f)
            {
                float turns = 0f;
                foreach (StirSample s in recentStirs)
                {
                    if (r.StirDirection == StirDirection.Any ||
                        (r.StirDirection == StirDirection.Clockwise) == s.clockwise) turns += s.turns;
                }
                if (turns < r.MinRecentTurns) return false;
            }
            foreach (AttributeCondition c in r.AttributeConditions)
            {
                if (c != null && !c.Evaluate(concentration)) return false;
            }
            return true;
        }

        private void ApplyReaction(ReactionData r)
        {
            foreach (Portion p in portions)
            {
                foreach (AttributeConversion c in r.Conversions)
                {
                    int from = (int)c.from, to = (int)c.to;
                    if (p.essence[from] <= 0f) continue;
                    float moved = p.essence[from] * Mathf.Clamp01(c.percent / 100f);
                    p.essence[from] -= moved;
                    p.essence[to] += moved * c.efficiency;
                }
                foreach (AttributeMultiplier m in r.Multipliers)
                {
                    int i = (int)m.attribute;
                    if (p.essence[i] > 0f) p.essence[i] *= Mathf.Max(0f, m.multiplier);
                }
            }
            foreach (TraitGain t in r.Traits)
            {
                if (string.IsNullOrEmpty(t.traitId)) continue;
                intrinsicTraits[t.traitId] = Mathf.Clamp01(GetIntrinsic(t.traitId) + t.strength);
            }
            firedReactions.Add(r.ReactionId);
            AppendStep(ProcessStepType.Reaction, r.ReactionId, r.DisplayName, 0f);
            if (ReactionOccurred != null) ReactionOccurred(r);
        }

        private AttributeVector CurrentConcentration()
        {
            var v = new AttributeVector();
            foreach (Portion p in portions)
            {
                float volumeUnits = VolumeUnitsFor(p);
                for (int i = 0; i < AttributeCount; i++) v.Add((PotionAttribute)i, p.essence[i] / volumeUnits);
            }
            v.ClampNegativeToZero();
            return v;
        }

        /// <summary>
        /// 濃さを求めるときに割る液量（単位）。液体（素材・瓶詰めした液体）の中身は量に比例するので実際の量で割る。
        /// 溶けた固体・粉は、ほんの少しの液体で濃さが極端にならないよう、最小の液量（minVolumeMl）で割る
        /// </summary>
        private float VolumeUnitsFor(Portion p)
        {
            float ml = p.isLiquid ? Mathf.Max(VolumeMl, 0.5f) : Mathf.Max(VolumeMl, rules.minVolumeMl);
            return ml / MlPerUnit;
        }

        // ------------------------------------------------------------------
        // 記録・内部
        // ------------------------------------------------------------------

        private Portion NewPortion(IngredientData ingredient, bool liquid)
        {
            return AddPortion(new Portion
            {
                ingredient = ingredient,
                sourceId = ingredient.IngredientId,
                sourceName = ingredient.DisplayName,
                formName = ingredient.PotionFormName,
                color = ingredient.Color,
                isLiquid = liquid,
                isBaseLiquid = ingredient.Category == IngredientCategory.BaseLiquid,
            });
        }

        private Portion AddPortion(Portion p)
        {
            p.order = nextOrder++;
            portions.Add(p);
            return p;
        }

        /// <summary>直前に入れた物と同じなら、それにまとめる（注いでいる間は毎フレーム呼ばれるため）</summary>
        private Portion FindLast(string sourceId, bool liquid)
        {
            if (portions.Count == 0) return null;
            Portion last = portions[portions.Count - 1];
            return last.sourceId == sourceId && last.isLiquid == liquid ? last : null;
        }

        private float GetIntrinsic(string id)
        {
            return intrinsicTraits.TryGetValue(id, out float v) ? v : 0f;
        }

        /// <summary>操作の前に、間があいていたら「待つ」を記録する</summary>
        private void NoteAction()
        {
            float idle = Time - lastActionTime;
            if (log.Count > 0 && idle >= rules.waitStepSeconds)
            {
                log.Add(new ProcessStep
                {
                    type = ProcessStepType.Wait,
                    amount = idle,
                    temperature = waitStartTemperature,
                    endTemperature = Temperature,
                    time = lastActionTime,
                });
            }
            lastActionTime = Time;
            waitStartTemperature = Temperature;
        }

        private void AppendStep(ProcessStepType type, string id, string name, float amount, bool clockwise = false)
        {
            ProcessStep last = log.Count > 0 ? log[log.Count - 1] : null;
            bool mergeable = last != null && last.type == type && last.sourceId == id && last.clockwise == clockwise &&
                             (type == ProcessStepType.Pour || type == ProcessStepType.PourLiquid || type == ProcessStepType.Add || type == ProcessStepType.Stir);
            if (mergeable)
            {
                last.amount += amount;
            }
            else
            {
                log.Add(new ProcessStep
                {
                    type = type,
                    sourceId = id,
                    sourceName = name,
                    amount = amount,
                    temperature = Temperature,
                    clockwise = clockwise,
                    time = Time,
                });
            }
            lastActionTime = Time;
            waitStartTemperature = Temperature;
        }

        /// <summary>液体がなくなったら、新しい調合として始め直す（溶け残りの固体は次に持ち越す）</summary>
        private void StartOver()
        {
            var residue = new List<Portion>();
            foreach (Portion p in portions)
            {
                if (p.ingredient != null && p.undissolved > 1e-4f)
                {
                    residue.Add(new Portion
                    {
                        ingredient = p.ingredient,
                        sourceId = p.sourceId,
                        sourceName = p.sourceName,
                        formName = p.formName,
                        color = p.color,
                        units = p.undissolved,
                        undissolved = p.undissolved,
                        quality = p.quality,
                        activation = p.activation,
                    });
                }
            }
            portions.Clear();
            log.Clear();
            Array.Clear(amplifierPool, 0, amplifierPool.Length);
            globalAmplifyPool = 0f;
            intrinsicTraits.Clear();
            carriedTraitMl.Clear();
            carriedIngredientIds.Clear();
            parentLiquidIds.Clear();
            firedReactions.Clear();
            reactionProgress.Clear();
            recentStirs.Clear();
            evaporatedMl = 0f;
            TotalTurns = 0f;
            VolumeMl = 0f;
            Time = 0f;
            lastActionTime = 0f;
            nextOrder = 0;
            foreach (Portion p in residue)
            {
                AddPortion(p);
                VolumeMl += p.units * (p.ingredient.Form == IngredientForm.Powder ? rules.powderVolumeMlPerUnit : rules.solidVolumeMlPerUnit);
                log.Add(new ProcessStep { type = ProcessStepType.Residue, sourceId = p.sourceId, sourceName = p.sourceName, amount = p.units, temperature = Temperature });
            }
        }

        // ------------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------------

        public FlaskSaveData Export()
        {
            var data = new FlaskSaveData
            {
                version = 1,
                volumeMl = VolumeMl,
                temperature = Temperature,
                heatLevel = HeatLevel,
                time = Time,
                totalTurns = TotalTurns,
                evaporatedMl = evaporatedMl,
                globalAmplifyPool = globalAmplifyPool,
                amplifierPool = (float[])amplifierPool.Clone(),
                parentLiquidIds = new List<string>(parentLiquidIds),
                carriedIngredientIds = new List<string>(carriedIngredientIds),
                firedReactions = new List<string>(firedReactions),
            };
            foreach (Portion p in portions)
            {
                data.portions.Add(new FlaskSaveData.PortionData
                {
                    ingredientId = p.ingredient != null ? p.ingredient.IngredientId : "",
                    sourceId = p.sourceId,
                    sourceName = p.sourceName,
                    formName = p.formName,
                    color = p.color,
                    isLiquid = p.isLiquid,
                    isBaseLiquid = p.isBaseLiquid,
                    units = p.units,
                    undissolved = p.undissolved,
                    essence = (float[])p.essence.Clone(),
                    quality = p.quality,
                    activation = p.activation,
                    turnsSinceAdded = p.turnsSinceAdded,
                    clockwiseUsed = p.clockwiseUsed,
                    clockwiseOthersUsed = p.clockwiseOthersUsed,
                    counterUsed = p.counterUsed,
                    counterPrimaryUsed = p.counterPrimaryUsed,
                });
            }
            foreach (KeyValuePair<string, float> t in intrinsicTraits) data.intrinsicTraits.Add(new LiquidTrait(t.Key, t.Value));
            foreach (KeyValuePair<string, float> t in carriedTraitMl) data.carriedTraitMl.Add(new LiquidTrait(t.Key, t.Value));
            foreach (ProcessStep s in log) data.log.Add(s.Clone());
            return data;
        }

        /// <summary>セーブから戻す（素材データが見つからない溶け残りは飛ばす）</summary>
        public void Import(FlaskSaveData data, Func<string, IngredientData> findIngredient)
        {
            StartOver();
            portions.Clear();
            log.Clear();
            VolumeMl = 0f;
            if (data == null || data.version <= 0) return;
            // 壊れた数字（NaN・無限大）が入っていても、計算が止まらないようにする（セーブは SaveRepair でも直している）
            VolumeMl = Mathf.Max(0f, Finite(data.volumeMl, 0f));
            Temperature = Finite(data.temperature, Temperature);
            HeatLevel = Mathf.Clamp01(Finite(data.heatLevel, 0f));
            Time = Mathf.Max(0f, Finite(data.time, 0f));
            lastActionTime = Time;
            TotalTurns = Mathf.Max(0f, Finite(data.totalTurns, 0f));
            evaporatedMl = Mathf.Max(0f, Finite(data.evaporatedMl, 0f));
            globalAmplifyPool = Finite(data.globalAmplifyPool, 0f);
            if (data.amplifierPool != null) Array.Copy(data.amplifierPool, amplifierPool, Mathf.Min(amplifierPool.Length, data.amplifierPool.Length));
            if (data.parentLiquidIds != null) foreach (string id in data.parentLiquidIds) if (!string.IsNullOrEmpty(id)) parentLiquidIds.Add(id);
            if (data.carriedIngredientIds != null) foreach (string id in data.carriedIngredientIds) if (!string.IsNullOrEmpty(id)) carriedIngredientIds.Add(id);
            if (data.firedReactions != null) foreach (string id in data.firedReactions) if (!string.IsNullOrEmpty(id)) firedReactions.Add(id);
            if (data.portions != null) foreach (FlaskSaveData.PortionData d in data.portions)
            {
                if (d == null) continue;
                IngredientData ingredient = !string.IsNullOrEmpty(d.ingredientId) && findIngredient != null ? findIngredient(d.ingredientId) : null;
                if (!string.IsNullOrEmpty(d.ingredientId) && ingredient == null && d.undissolved > 0f) continue;
                var p = new Portion
                {
                    ingredient = ingredient,
                    sourceId = d.sourceId,
                    sourceName = d.sourceName,
                    formName = d.formName,
                    color = d.color,
                    isLiquid = d.isLiquid,
                    isBaseLiquid = d.isBaseLiquid,
                    units = d.units,
                    undissolved = d.undissolved,
                    quality = d.quality,
                    activation = d.activation,
                    turnsSinceAdded = d.turnsSinceAdded,
                    clockwiseUsed = d.clockwiseUsed,
                    clockwiseOthersUsed = d.clockwiseOthersUsed,
                    counterUsed = d.counterUsed,
                    counterPrimaryUsed = d.counterPrimaryUsed,
                };
                if (d.essence != null) Array.Copy(d.essence, p.essence, Mathf.Min(p.essence.Length, d.essence.Length));
                AddPortion(p);
            }
            if (data.intrinsicTraits != null) foreach (LiquidTrait t in data.intrinsicTraits) if (t != null && !string.IsNullOrEmpty(t.id)) intrinsicTraits[t.id] = Finite(t.strength, 0f);
            if (data.carriedTraitMl != null) foreach (LiquidTrait t in data.carriedTraitMl) if (t != null && !string.IsNullOrEmpty(t.id)) carriedTraitMl[t.id] = Finite(t.strength, 0f);
            if (data.log != null) foreach (ProcessStep s in data.log) if (s != null) log.Add(s.Clone());
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }

    /// <summary>フラスコの中身の計算結果（ある瞬間の様子）</summary>
    public sealed class LiquidState
    {
        public sealed class Source
        {
            public string id;
            public string name;
            public bool isLiquid;
            public bool isBaseLiquid;
            public float ml;
            public float strength;
            public Color color;
            public string formName;
            public float undissolvedUnits;
        }

        public float volumeMl;
        public float temperature;
        public float heatLevel;
        public float time;
        public float dissolvedFraction = 1f;
        public readonly AttributeVector concentration = new AttributeVector();
        public readonly List<Source> sources = new List<Source>();
        public readonly HashSet<string> ingredientIds = new HashSet<string>();
        public readonly List<string> parentLiquidIds = new List<string>();
        public readonly List<string> reactionIds = new List<string>();
        public readonly List<ProcessStep> process = new List<ProcessStep>();
        public readonly List<LiquidTrait> traits = new List<LiquidTrait>();

        public float GetTrait(string id)
        {
            foreach (LiquidTrait t in traits)
            {
                if (t.id == id) return t.strength;
            }
            return 0f;
        }

        public void SetTrait(string id, float strength)
        {
            strength = Mathf.Clamp01(strength);
            foreach (LiquidTrait t in traits)
            {
                if (t.id != id) continue;
                t.strength = strength;
                return;
            }
            if (strength > 0.01f) traits.Add(new LiquidTrait(id, strength));
        }
    }

    /// <summary>フラスコの中身のセーブデータ</summary>
    [Serializable]
    public class FlaskSaveData
    {
        [Serializable]
        public class PortionData
        {
            public string ingredientId = "";
            public string sourceId = "";
            public string sourceName = "";
            public string formName = "";
            public Color color = Color.white;
            public bool isLiquid;
            public bool isBaseLiquid;
            public float units;
            public float undissolved;
            public float[] essence = new float[0];
            public float quality = 1f;
            public float activation;
            public float turnsSinceAdded;
            public float clockwiseUsed, clockwiseOthersUsed, counterUsed, counterPrimaryUsed;
        }

        public int version;
        public float volumeMl;
        public float temperature;
        public float heatLevel;
        public float time;
        public float totalTurns;
        public float evaporatedMl;
        public float globalAmplifyPool;
        public float[] amplifierPool = new float[0];
        public List<PortionData> portions = new List<PortionData>();
        public List<LiquidTrait> intrinsicTraits = new List<LiquidTrait>();
        public List<LiquidTrait> carriedTraitMl = new List<LiquidTrait>();
        public List<string> parentLiquidIds = new List<string>();
        public List<string> carriedIngredientIds = new List<string>();
        public List<string> firedReactions = new List<string>();
        public List<ProcessStep> log = new List<ProcessStep>();
    }
}
