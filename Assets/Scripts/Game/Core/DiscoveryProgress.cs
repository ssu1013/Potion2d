using System.Collections.Generic;
using PotionFrontier.Alchemy;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 発見の進み具合（ポイント）と、新しい素材が机に届く時期。
    ///   ・素材の性質を見つける … 1（素材ノートの「見つけた性質」。「使った」は数えない）
    ///   ・新しい反応を見つける … 2
    ///   ・新しい液体を作る ……… 1（未知の液体＝ただ混ざっただけの物は数えない）
    /// 素材ごとの「届くのに必要な発見」（IngredientData の DiscoveriesToUnlock）に届いたら、机の素材トレイに自動で届く。
    /// はじめから机にある素材（GameBalance）は 0。
    /// </summary>
    public static class DiscoveryProgress
    {
        public const int PointsPerFact = 1;
        public const int PointsPerReaction = 2;
        public const int PointsPerLiquid = 1;

        public static int Points(IngredientDatabase db, LiquidRegistry liquids)
        {
            int facts = 0;
            if (db != null) foreach (IngredientData d in db.All) if (d != null) facts += DiscoveryJournal.KnownCount(d);
            int made = 0;
            if (liquids != null) foreach (LiquidRecord r in liquids.All) if (r != null && r.kind != LiquidKind.Unknown) made++;
            return facts * PointsPerFact + DiscoveryJournal.KnownReactionCount * PointsPerReaction + made * PointsPerLiquid;
        }

        /// <summary>まだ届いていない素材のうち、いちばん早く届く物と、それまでの残りのポイント（なければ null・0）</summary>
        public static IngredientData NextArrival(IngredientDatabase db, IngredientInventory stock, int points, out int remaining)
        {
            remaining = 0;
            IngredientData next = null;
            if (db == null || stock == null) return null;
            foreach (IngredientData d in db.All)
            {
                if (d == null || stock.IsUnlocked(d)) continue;
                if (next == null || d.DiscoveriesToUnlock < next.DiscoveriesToUnlock) next = d;
            }
            if (next != null) remaining = System.Math.Max(0, next.DiscoveriesToUnlock - points);
            return next;
        }

        /// <summary>今のポイントで届く素材を、すべて机に届ける。届いた物を返す</summary>
        public static List<IngredientData> Deliver(IngredientDatabase db, IngredientInventory stock, int points, bool announce = true)
        {
            var arrived = new List<IngredientData>();
            if (db == null || stock == null) return arrived;
            var order = new List<IngredientData>(db.All);
            order.Sort((a, b) => a == null || b == null ? 0 : a.DiscoveriesToUnlock.CompareTo(b.DiscoveriesToUnlock));
            foreach (IngredientData d in order)
            {
                if (d == null || stock.IsUnlocked(d) || d.DiscoveriesToUnlock > points) continue;
                if (stock.Unlock(d, announce)) arrived.Add(d);
            }
            return arrived;
        }
    }
}
