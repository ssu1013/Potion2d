using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>素材の性質（素材ノートに書き足していくもの）</summary>
    [Flags]
    public enum IngredientFact
    {
        None = 0,
        /// <summary>使ったことがある（ノートのページが開く）</summary>
        Used = 1,
        /// <summary>溶けやすさ（溶けきるまで見届けた）</summary>
        Solubility = 2,
        /// <summary>熱に弱い</summary>
        Weakens = 4,
        /// <summary>温めると力が目覚める</summary>
        Wakes = 8,
        /// <summary>決まった温度にならないと溶けない</summary>
        Melts = 16,
        /// <summary>混ぜすぎると崩れる</summary>
        Fragile = 32,
        /// <summary>沸騰させると香り（力）が飛ぶ</summary>
        Volatile = 64,
        /// <summary>入れるとフラスコが冷える</summary>
        Cools = 128,
        /// <summary>決まった向きに混ぜたときだけ溶ける</summary>
        OneWay = 256,
        /// <summary>ほかの素材の力を強める（触媒）</summary>
        Amplifies = 512,
    }

    /// <summary>セーブ用：素材1つ分の、見つけた性質</summary>
    [Serializable]
    public class IngredientNote
    {
        public string id = "";
        public int facts;
    }

    /// <summary>
    /// 発見の記録（素材ノートの中身）。調合しているうちに「起きたこと」から、素材の性質が少しずつわかっていく。
    /// 見つけた反応も覚える。セーブに残る（SaveData の版6から）。
    /// 調べる・書き足すのは BrewSession2D（フラスコの中を見て Learn を呼ぶ）。見せるのは素材ノート・調合台の手ごたえ。
    /// </summary>
    public static class DiscoveryJournal
    {
        /// <summary>ノートに並べる性質の順番</summary>
        public static readonly IngredientFact[] Order =
        {
            IngredientFact.Solubility, IngredientFact.Weakens, IngredientFact.Wakes, IngredientFact.Melts, IngredientFact.Fragile,
            IngredientFact.Volatile, IngredientFact.Cools, IngredientFact.OneWay, IngredientFact.Amplifies,
        };

        private static readonly Dictionary<string, int> facts = new Dictionary<string, int>();
        private static readonly HashSet<string> reactions = new HashSet<string>();

        /// <summary>素材の新しい性質を見つけた（素材・性質）</summary>
        public static event Action<IngredientData, IngredientFact> FactDiscovered;
        /// <summary>新しい反応を見つけた</summary>
        public static event Action<ReactionData> ReactionDiscovered;
        /// <summary>中身が変わった（ノートを開いていれば描き直す）</summary>
        public static event Action Changed;

        // ------------------------------------------------------------------
        // 調べる
        // ------------------------------------------------------------------

        /// <summary>この素材が持っている性質（データから。まだ見つけていない物も含む）</summary>
        public static IngredientFact FactsOf(IngredientData d)
        {
            if (d == null) return IngredientFact.None;
            IngredientFact f = IngredientFact.None;
            if (!d.IsPoured) f |= IngredientFact.Solubility;
            if (d.HeatLimit > 0f) f |= IngredientFact.Weakens;
            if (d.ActivationTemp > 0f) f |= IngredientFact.Wakes;
            if (d.MeltTemp > 0f) f |= IngredientFact.Melts;
            if (d.FragileTurns > 0f) f |= IngredientFact.Fragile;
            if (d.Volatility > 0f) f |= IngredientFact.Volatile;
            if (d.CoolTemp > 0f) f |= IngredientFact.Cools;
            if (d.DissolveDirection != StirDirection.Any) f |= IngredientFact.OneWay;
            if (d.GlobalAmplifyPercent != 0f || (d.Amplifiers != null && d.Amplifiers.Count > 0)) f |= IngredientFact.Amplifies;
            return f;
        }

        /// <summary>見つけた性質</summary>
        public static IngredientFact Known(IngredientData d)
        {
            return d != null && facts.TryGetValue(d.IngredientId, out int v) ? (IngredientFact)v : IngredientFact.None;
        }

        public static bool Knows(IngredientData d, IngredientFact fact)
        {
            return (Known(d) & fact) == fact && fact != IngredientFact.None;
        }

        /// <summary>見つけた性質の数・ぜんぶの数（「使った」は数えない）</summary>
        public static int KnownCount(IngredientData d) => Count(Known(d) & FactsOf(d));
        public static int TotalCount(IngredientData d) => Count(FactsOf(d));

        /// <summary>反応を知っているか</summary>
        public static bool KnowsReaction(ReactionData r)
        {
            return r != null && reactions.Contains(r.ReactionId);
        }

        public static int KnownReactionCount => reactions.Count;

        // ------------------------------------------------------------------
        // 書き足す
        // ------------------------------------------------------------------

        /// <summary>性質を書き足す。素材が持っていない性質は書かない。初めてなら true（「使った」以外はお知らせが出る）</summary>
        public static bool Learn(IngredientData d, IngredientFact fact)
        {
            if (d == null || fact == IngredientFact.None) return false;
            if (fact != IngredientFact.Used && (FactsOf(d) & fact) == 0) return false;
            facts.TryGetValue(d.IngredientId, out int v);
            if ((v & (int)fact) != 0) return false;
            // 性質がわかったら、使ったことがあるのも当然
            facts[d.IngredientId] = v | (int)fact | (int)IngredientFact.Used;
            if (fact != IngredientFact.Used && FactDiscovered != null) FactDiscovered(d, fact);
            if (Changed != null) Changed();
            return true;
        }

        /// <summary>反応を書き足す。初めてなら true</summary>
        public static bool LearnReaction(ReactionData r)
        {
            if (r == null || !reactions.Add(r.ReactionId)) return false;
            if (ReactionDiscovered != null) ReactionDiscovered(r);
            if (Changed != null) Changed();
            return true;
        }

        // ------------------------------------------------------------------
        // 言葉
        // ------------------------------------------------------------------

        /// <summary>短い名前（トレイの説明・ノートの一覧）</summary>
        public static string ShortLabel(IngredientFact fact)
        {
            switch (fact)
            {
                case IngredientFact.Solubility: return "溶けやすさ";
                case IngredientFact.Weakens: return "熱に弱い";
                case IngredientFact.Wakes: return "温めると目覚める";
                case IngredientFact.Melts: return "溶ける温度がある";
                case IngredientFact.Fragile: return "崩れやすい";
                case IngredientFact.Volatile: return "香りが飛ぶ";
                case IngredientFact.Cools: return "冷やす";
                case IngredientFact.OneWay: return "溶ける向きがある";
                case IngredientFact.Amplifies: return "ほかの力を強める";
                default: return "";
            }
        }

        /// <summary>くわしい説明（素材ノートのページ・見つけたときのお知らせ）</summary>
        public static string Describe(IngredientData d, IngredientFact fact)
        {
            if (d == null) return "";
            switch (fact)
            {
                case IngredientFact.Solubility:
                    float s = d.Solubility;
                    return s >= 0.75f ? "すぐ溶ける" : s >= 0.4f ? "ふつうに溶ける（かき混ぜると早い）" : "溶けにくい（よく混ぜる・温めると溶ける）";
                case IngredientFact.Weakens: return $"熱に弱い（{d.HeatLimit:0}℃ をこえると、力が弱まっていく）";
                case IngredientFact.Wakes: return $"{d.ActivationTemp:0}℃ 以上でしばらく温めると、力が目覚めて強くなる";
                case IngredientFact.Melts: return $"{d.MeltTemp:0}℃ にならないと溶けない（冷たいと、混ぜても溶けない）";
                case IngredientFact.Fragile: return $"やわらかい。入れてから {d.FragileTurns:0.#} 回転より多く混ぜると、崩れて弱まる";
                case IngredientFact.Volatile: return "香りに力がある。沸騰させると、その力が飛んでいく";
                case IngredientFact.Cools: return $"入れるとフラスコが一気に冷える（1回でおよそ {d.CoolTemp:0}℃）";
                case IngredientFact.OneWay:
                    return d.DissolveDirection == StirDirection.Clockwise ? "時計回りに混ぜたときだけ溶ける" : "反時計回りに混ぜたときだけ溶ける";
                case IngredientFact.Amplifies: return "溶けたあとに溶けた素材の力を強める（先に溶かしておくとよい）";
                default: return "";
            }
        }

        // ------------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------------

        public static void Clear()
        {
            facts.Clear();
            reactions.Clear();
            if (Changed != null) Changed();
        }

        public static List<IngredientNote> ExportNotes()
        {
            var list = new List<IngredientNote>();
            foreach (KeyValuePair<string, int> kv in facts) list.Add(new IngredientNote { id = kv.Key, facts = kv.Value });
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        public static List<string> ExportReactions()
        {
            var list = new List<string>(reactions);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        public static void Import(List<IngredientNote> notes, List<string> knownReactions)
        {
            facts.Clear();
            reactions.Clear();
            if (notes != null)
            {
                foreach (IngredientNote n in notes)
                {
                    if (n != null && !string.IsNullOrEmpty(n.id)) facts[n.id] = n.facts;
                }
            }
            if (knownReactions != null)
            {
                foreach (string r in knownReactions)
                {
                    if (!string.IsNullOrEmpty(r)) reactions.Add(r);
                }
            }
            if (Changed != null) Changed();
        }

        private static int Count(IngredientFact f)
        {
            int n = 0;
            int v = (int)(f & ~IngredientFact.Used);
            while (v != 0)
            {
                n += v & 1;
                v >>= 1;
            }
            return n;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            facts.Clear();
            reactions.Clear();
            FactDiscovered = null;
            ReactionDiscovered = null;
            Changed = null;
        }
    }
}
