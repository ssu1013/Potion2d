using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>液体の特性のID（反応データや調合ルール設定の「液体の特性」から参照する）</summary>
    public static class LiquidTraits
    {
        public const string Scorched = "scorched";
        public const string Foamy = "foamy";
        public const string Concentrated = "concentrated";
        public const string Cloudy = "cloudy";
        public const string Shimmer = "shimmer";
        public const string Syrupy = "syrupy";
    }

    /// <summary>液体の特性と強さ（0〜1）</summary>
    [Serializable]
    public class LiquidTrait
    {
        public string id = "";
        public float strength;

        public LiquidTrait() { }

        public LiquidTrait(string id, float strength)
        {
            this.id = id;
            this.strength = strength;
        }
    }

    /// <summary>工程の種類</summary>
    public enum ProcessStepType
    {
        Pour = 0,        // 液体の素材を注ぐ（量は ml）
        PourLiquid = 1,  // 瓶詰めした液体を注ぐ（量は ml）
        Add = 2,         // 固体・粉を入れる（量は単位）
        Stir = 3,        // かき混ぜる（量は回転数）
        Heat = 4,        // 火加減を変える（量は 0〜1）
        Wait = 5,        // 待つ（量は秒）
        Reaction = 6,    // 反応が起きた
        Residue = 7,     // 前の調合の溶け残り（量は単位）
    }

    /// <summary>工程の1行（レシピ本の「作り方」になる）</summary>
    [Serializable]
    public class ProcessStep
    {
        public ProcessStepType type;
        [Tooltip("素材のID・液体のID・反応のID")] public string sourceId = "";
        [Tooltip("そのときの名前（あとで名前が変わっても記録は残る）")] public string sourceName = "";
        public float amount;
        [Tooltip("その工程を始めたときの温度")] public float temperature;
        [Tooltip("待つ：待ち終わったときの温度")] public float endTemperature;
        public bool clockwise;
        [Tooltip("調合を始めてからの秒数")] public float time;

        public ProcessStep Clone()
        {
            return (ProcessStep)MemberwiseClone();
        }

        /// <summary>「清らかな水を 200ml 注ぐ（20℃）」のような説明</summary>
        public string Describe()
        {
            string temp = $"（{Mathf.RoundToInt(temperature)}℃）";
            switch (type)
            {
                case ProcessStepType.Pour:
                case ProcessStepType.PourLiquid:
                    return $"{sourceName}を {Mathf.RoundToInt(amount)}ml 注ぐ{temp}";
                case ProcessStepType.Add:
                    return $"{sourceName}を {FormatUnits(amount)} 入れる{temp}";
                case ProcessStepType.Residue:
                    return $"前の溶け残り：{sourceName} {FormatUnits(amount)}";
                case ProcessStepType.Stir:
                    return $"{(clockwise ? "時計回り" : "反時計回り")}に {FormatTurns(amount)}回 混ぜる{temp}";
                case ProcessStepType.Heat:
                    return $"火加減を「{HeatLabel(amount)}」にする{temp}";
                case ProcessStepType.Wait:
                    return $"{FormatSeconds(amount)} 待つ（{Mathf.RoundToInt(temperature)}℃ → {Mathf.RoundToInt(endTemperature)}℃）";
                case ProcessStepType.Reaction:
                    return $"反応「{sourceName}」が起きた{temp}";
                default:
                    return sourceName;
            }
        }

        public static string HeatLabel(float level)
        {
            if (level <= 0.01f) return "消す";
            if (level < 0.35f) return "弱火";
            if (level < 0.7f) return "中火";
            return "強火";
        }

        public static string FormatUnits(float units)
        {
            return Mathf.Approximately(units, Mathf.Round(units)) ? "×" + Mathf.RoundToInt(units) : "×" + units.ToString("0.#");
        }

        private static string FormatTurns(float turns)
        {
            return turns >= 10f ? Mathf.RoundToInt(turns).ToString() : turns.ToString("0.#");
        }

        public static string FormatSeconds(float seconds)
        {
            int s = Mathf.RoundToInt(seconds);
            return s >= 60 ? $"{s / 60}分{(s % 60 > 0 ? (s % 60) + "秒" : "")}" : $"{s}秒";
        }
    }

    /// <summary>作り方1つ分（工程の並び）</summary>
    [Serializable]
    public class ProcessRecipe
    {
        public List<ProcessStep> steps = new List<ProcessStep>();

        public ProcessRecipe() { }

        public ProcessRecipe(IEnumerable<ProcessStep> source)
        {
            foreach (ProcessStep s in source) steps.Add(s.Clone());
        }

        /// <summary>同じ作り方か（量の細かい違いは同じとみなす）</summary>
        public bool IsSimilar(ProcessRecipe other)
        {
            if (other == null || other.steps.Count != steps.Count) return false;
            for (int i = 0; i < steps.Count; i++)
            {
                ProcessStep a = steps[i], b = other.steps[i];
                if (a.type != b.type || a.sourceId != b.sourceId || a.clockwise != b.clockwise) return false;
                if (Mathf.Abs(a.amount - b.amount) > Mathf.Max(1f, Mathf.Abs(a.amount) * 0.15f)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// できた液体の記録（中間液体・失敗作も含む）。瓶詰めした液体・レシピ本のページ・特別な薬の判定は、すべてこれを使う。
    /// 属性は「100ml あたりの濃さ」。同じ結果の液体は同じ ID（fingerprint から作る）になる。
    /// </summary>
    [Serializable]
    public class LiquidRecord
    {
        public const int MaxRecipes = 3;

        public string id = "";
        public string fingerprint = "";
        [Tooltip("中身から自動で決まった名前")] public string autoName = "";
        [Tooltip("プレイヤーが付けた名前（空なら自動の名前）")] public string customName = "";
        public LiquidKind kind;
        public Color color = Color.white;
        [Tooltip("属性の色を混ぜる前の、液体そのものの色（多段調合で注ぎ戻したとき、色が少しずつずれていかないように使う）")]
        public Color solventColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("100ml あたりの属性の濃さ")] public float[] attributes = new float[0];
        public List<LiquidTrait> traits = new List<LiquidTrait>();
        [Tooltip("特別なポーション（完成条件データ）に当てはまったときの ID")] public string specialPotionId = "";
        public int stars;
        [Tooltip("名前の末尾（ポーション・オイル・シロップ…）")] public string formName = "";
        [Tooltip("一番多く使ったベース液体（瓶のモデル選びに使う）")] public string baseLiquidId = "";
        public string description = "";
        public List<string> effects = new List<string>();
        [Tooltip("使った素材（混ぜた液体の中の素材も含む）")] public List<string> ingredientIds = new List<string>();
        [Tooltip("混ぜた液体（多段調合の元）")] public List<string> parentLiquidIds = new List<string>();
        [Tooltip("起きた反応")] public List<string> reactionIds = new List<string>();
        [Tooltip("作り方（最初に作った方法と、ちがう方法を最大3つまで）")] public List<ProcessRecipe> recipes = new List<ProcessRecipe>();
        [Tooltip("作った回数")] public int madeCount;
        [Tooltip("レシピ本でまだ見ていない（新しく記録した）ページ")] public bool unread;

        public string DisplayName => !string.IsNullOrEmpty(customName) ? customName : autoName;
        public AttributeVector Attributes => AttributeVector.FromArray(attributes);
        public float TotalPotency => Attributes.TotalPotency;
        public bool IsSpecial => !string.IsNullOrEmpty(specialPotionId);

        public float TraitStrength(string traitId)
        {
            foreach (LiquidTrait t in traits)
            {
                if (t.id == traitId) return t.strength;
            }
            return 0f;
        }

        /// <summary>作り方の記録があるか（フェーズ4までに作った古い瓶には作り方がない）</summary>
        public bool HasRecipe => recipes != null && recipes.Count > 0 && recipes[0].steps.Count > 0;

        public bool UsesIngredient(string ingredientId)
        {
            return ingredientIds.Contains(ingredientId);
        }

        /// <summary>作り方を足す（同じような作り方がすでにあれば足さない）。足したら true</summary>
        public bool AddRecipe(ProcessRecipe recipe)
        {
            if (recipe == null || recipe.steps.Count == 0) return false;
            foreach (ProcessRecipe r in recipes)
            {
                if (r.IsSimilar(recipe)) return false;
            }
            if (recipes.Count >= MaxRecipes) return false;
            recipes.Add(recipe);
            return true;
        }

        public LiquidRecord Clone()
        {
            var c = (LiquidRecord)MemberwiseClone();
            c.attributes = (float[])attributes.Clone();
            c.traits = new List<LiquidTrait>();
            foreach (LiquidTrait t in traits) c.traits.Add(new LiquidTrait(t.id, t.strength));
            c.effects = new List<string>(effects);
            c.ingredientIds = new List<string>(ingredientIds);
            c.parentLiquidIds = new List<string>(parentLiquidIds);
            c.reactionIds = new List<string>(reactionIds);
            c.recipes = new List<ProcessRecipe>();
            foreach (ProcessRecipe r in recipes) c.recipes.Add(new ProcessRecipe(r.steps));
            return c;
        }

        public string ToDebugString(AlchemySettings settings)
        {
            var sb = new StringBuilder();
            sb.Append(DisplayName).Append(" [").Append(kind).Append("] ★").Append(stars)
              .Append(" 色#").Append(ColorUtility.ToHtmlStringRGB(color))
              .Append(" 属性: ").Append(Attributes.ToString(settings != null ? (Func<PotionAttribute, string>)settings.GetDisplayName : null));
            foreach (LiquidTrait t in traits) sb.Append(" ／").Append(t.id).Append(' ').Append(t.strength.ToString("0.##"));
            if (IsSpecial) sb.Append(" ／特別:").Append(specialPotionId);
            return sb.ToString();
        }
    }
}
