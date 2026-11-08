using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>セーブデータに入る素材の在庫1件分</summary>
    [Serializable]
    public class IngredientStockEntry
    {
        public string id;
        public float amount;
    }

    /// <summary>
    /// 素材の在庫。素材は2種類ある。
    ///   ・使い放題の素材（通常の素材） … 一度手に入れたら「取引済み」になり、何度使っても減らない
    ///   ・使った分だけ減る素材（希少な素材） … 量（単位）で数える。液体は注いだ分だけ小数で減る
    /// どちらになるかは素材データの「在庫の扱い」（自動ならレア度と GameBalance の境目）で決まる。
    /// 入手（取引先帳・お礼・ファームの収穫）は Add、使う（フラスコに入れる）は Consume を通す。
    /// </summary>
    public class IngredientInventory : MonoBehaviour
    {
        /// <summary>これより少ない在庫は 0 とみなす（小数の誤差対策）</summary>
        public const float Epsilon = 0.005f;

        private readonly Dictionary<string, float> stock = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>在庫が変わったとき（変わった素材。まとめて変わったときは null）</summary>
        public event Action<IngredientData> Changed;

        /// <summary>このレア度以下は使い放題（GameBalance。なければ 2）</summary>
        public static int InfiniteRarityMax
        {
            get
            {
                GameBalance balance = GameContext.Balance;
                return balance != null ? balance.infiniteRarityMax : 2;
            }
        }

        /// <summary>使い放題の素材か（取引済みかどうかは関係なく、素材の種類として）</summary>
        public static bool IsInfiniteKind(IngredientData data)
        {
            return data != null && data.IsInfinite(InfiniteRarityMax);
        }

        /// <summary>使い放題で、もう取引済み（いくらでも使える）</summary>
        public bool IsUnlimited(IngredientData data)
        {
            return data != null && IsInfiniteKind(data) && unlocked.Contains(data.IngredientId);
        }

        /// <summary>入手ルートが開いている（使い放題の素材が取引済み）</summary>
        public bool IsUnlocked(IngredientData data)
        {
            return data != null && unlocked.Contains(data.IngredientId);
        }

        /// <summary>在庫の量。使い放題なら PositiveInfinity</summary>
        public float Get(IngredientData data)
        {
            if (data == null) return 0f;
            if (IsUnlimited(data)) return float.PositiveInfinity;
            return stock.TryGetValue(data.IngredientId, out float v) ? v : 0f;
        }

        public bool Has(IngredientData data, float amount = Epsilon)
        {
            if (IsUnlimited(data)) return true;
            return Get(data) >= Mathf.Max(Epsilon, amount) - 1e-5f;
        }

        /// <summary>素材を手に入れる。使い放題の素材は「取引済み」になる（量は関係ない）</summary>
        public void Add(IngredientData data, float amount)
        {
            if (data == null || amount <= 0f) return;
            string id = data.IngredientId;
            if (IsInfiniteKind(data))
            {
                if (unlocked.Add(id))
                {
                    stock.Remove(id);
                    Notify(data);
                }
                return;
            }
            stock.TryGetValue(id, out float v);
            stock[id] = Round(v + amount);
            Notify(data);
        }

        /// <summary>入手ルートを開拓する（使い放題の素材を取引済みにする）</summary>
        public void Unlock(IngredientData data)
        {
            if (data == null || !unlocked.Add(data.IngredientId)) return;
            stock.Remove(data.IngredientId);
            Notify(data);
        }

        /// <summary>
        /// 素材を使う。使い放題ならそのまま全部使える（減らない）。
        /// 在庫が足りないときは、あるだけ使う。実際に使った量を返す。
        /// </summary>
        public float Consume(IngredientData data, float amount)
        {
            if (data == null || amount <= 0f) return 0f;
            if (IsUnlimited(data)) return amount;
            string id = data.IngredientId;
            if (!stock.TryGetValue(id, out float v) || v < Epsilon) return 0f;
            float used = Mathf.Min(v, amount);
            float left = v - used;
            if (left < Epsilon)
            {
                // 端数が残らないように、使い切ったことにする（減った分とフラスコに入る分をぴったり同じにする）
                used = v;
                stock.Remove(id);
            }
            else stock[id] = left;
            Notify(data);
            return used;
        }

        /// <summary>すべての素材を消す（はじめから遊ぶとき）</summary>
        public void Clear()
        {
            stock.Clear();
            unlocked.Clear();
            Notify(null);
        }

        /// <summary>はじめから持っている素材を入れる</summary>
        public void ResetToStarter(GameBalance balance)
        {
            stock.Clear();
            unlocked.Clear();
            if (balance != null)
            {
                foreach (IngredientAmount entry in balance.startingIngredients)
                {
                    if (entry == null || entry.ingredient == null || entry.amount <= 0f) continue;
                    string id = entry.ingredient.IngredientId;
                    if (IsInfiniteKind(entry.ingredient))
                    {
                        unlocked.Add(id);
                        continue;
                    }
                    stock.TryGetValue(id, out float v);
                    stock[id] = Round(v + entry.amount);
                }
            }
            Notify(null);
        }

        public List<IngredientStockEntry> Export()
        {
            var list = new List<IngredientStockEntry>();
            foreach (KeyValuePair<string, float> pair in stock)
            {
                if (pair.Value >= Epsilon) list.Add(new IngredientStockEntry { id = pair.Key, amount = pair.Value });
            }
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return list;
        }

        /// <summary>取引済み（使い放題）の素材の ID</summary>
        public List<string> ExportUnlocked()
        {
            var list = new List<string>(unlocked);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>
        /// セーブデータから読み込む。カタログにない素材は、データが消された可能性があるので読み捨てずに残す。
        /// 版3までのセーブ（取引済みの記録がない）は、在庫のあった使い放題の素材を取引済みにする。
        /// </summary>
        public void Import(List<IngredientStockEntry> saved, List<string> savedUnlocked, Func<string, IngredientData> resolve = null)
        {
            stock.Clear();
            unlocked.Clear();
            if (savedUnlocked != null)
            {
                foreach (string id in savedUnlocked)
                {
                    if (!string.IsNullOrEmpty(id)) unlocked.Add(id);
                }
            }
            IngredientDatabase db = GameContext.Ingredients;
            if (resolve == null && db != null) resolve = db.Get;
            if (saved != null)
            {
                foreach (IngredientStockEntry e in saved)
                {
                    if (e == null || string.IsNullOrEmpty(e.id) || e.amount < Epsilon) continue;
                    IngredientData data = resolve != null ? resolve(e.id) : null;
                    if (data != null && IsInfiniteKind(data))
                    {
                        unlocked.Add(e.id);
                        continue;
                    }
                    stock[e.id] = Round(e.amount);
                }
            }
            Notify(null);
        }

        /// <summary>持っている素材の種類の数（カタログにある物のみ。取引済みの使い放題も数える）</summary>
        public int CountKinds(IngredientDatabase database)
        {
            var ids = new HashSet<string>(stock.Keys, StringComparer.Ordinal);
            foreach (string id in unlocked) ids.Add(id);
            if (database == null) return ids.Count;
            int n = 0;
            foreach (string id in ids)
            {
                if (database.Get(id) != null) n++;
            }
            return n;
        }

        private static float Round(float v)
        {
            return Mathf.Round(v * 1000f) / 1000f;
        }

        private void Notify(IngredientData data)
        {
            if (Changed != null) Changed(data);
        }

        /// <summary>在庫の表示（使い放題は「∞」、液体は「3.5」、固体は「×12」）</summary>
        public static string Format(IngredientData data, float amount)
        {
            if (float.IsInfinity(amount)) return "∞";
            if (data != null && data.IsPoured) return amount.ToString(amount < 10f ? "0.0" : "0");
            return "×" + amount.ToString(Mathf.Approximately(amount, Mathf.Round(amount)) ? "0" : "0.0");
        }
    }
}
