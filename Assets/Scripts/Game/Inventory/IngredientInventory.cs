using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>古いセーブの「素材の在庫」1件（読み込みのためだけに残してある）</summary>
    [Serializable]
    public class IngredientStockEntry
    {
        public string id;
        public float amount;
    }

    /// <summary>
    /// 調合机の素材トレイに並ぶ素材（シーンの GameSystems に付ける）。
    /// 1画面版：お金・仕入れはない。机に届いた（解放された）素材は、何度使っても減らない。
    /// はじめは GameBalance の「はじめから机にある素材」、あとは発見が進むと DiscoveryProgress が届ける（Unlock）。
    /// </summary>
    public class IngredientInventory : MonoBehaviour
    {
        public const float Epsilon = 0.005f;

        private readonly HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>中身が変わった（届いた素材。まとめて変わったときは null）</summary>
        public event Action<IngredientData> Changed;
        /// <summary>新しい素材が机に届いた（発見が進んだとき。はじめからある素材・セーブの読み込みでは呼ばれない）</summary>
        public event Action<IngredientData> Arrived;

        /// <summary>机に届いていて、使える素材か（使っても減らない）</summary>
        public bool IsUnlocked(IngredientData data) => data != null && unlocked.Contains(data.IngredientId);

        public float Get(IngredientData data) => IsUnlocked(data) ? float.PositiveInfinity : 0f;

        public bool Has(IngredientData data, float amount = Epsilon) => IsUnlocked(data);

        /// <summary>使う。届いている素材は減らないので、そのままの量を返す（届いていなければ 0）</summary>
        public float Consume(IngredientData data, float amount) => IsUnlocked(data) && amount > 0f ? amount : 0f;

        /// <summary>新しい素材を机に届ける（もう届いていれば何もしない）。announce なら Arrived を呼ぶ</summary>
        public bool Unlock(IngredientData data, bool announce = true)
        {
            if (data == null || !unlocked.Add(data.IngredientId)) return false;
            Notify(data);
            if (announce && Arrived != null) Arrived(data);
            return true;
        }

        public int UnlockedCount => unlocked.Count;

        public void Clear()
        {
            unlocked.Clear();
            Notify(null);
        }

        /// <summary>はじめから机にある素材にする（GameBalance）</summary>
        public void ResetToStarter(GameBalance balance)
        {
            unlocked.Clear();
            if (balance != null)
            {
                foreach (IngredientAmount entry in balance.startingIngredients)
                {
                    if (entry != null && entry.ingredient != null) unlocked.Add(entry.ingredient.IngredientId);
                }
            }
            Notify(null);
        }

        public List<string> ExportUnlocked()
        {
            var list = new List<string>(unlocked);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>セーブから読み込む。古いセーブの「在庫」に量があった素材も、届いていることにする</summary>
        public void Import(List<string> savedUnlocked, List<IngredientStockEntry> legacyStock = null)
        {
            unlocked.Clear();
            if (savedUnlocked != null)
            {
                foreach (string id in savedUnlocked) if (!string.IsNullOrEmpty(id)) unlocked.Add(id);
            }
            if (legacyStock != null)
            {
                foreach (IngredientStockEntry e in legacyStock) if (e != null && !string.IsNullOrEmpty(e.id) && e.amount >= Epsilon) unlocked.Add(e.id);
            }
            Notify(null);
        }

        /// <summary>机にある素材の種類の数（カタログにある物のみ）</summary>
        public int CountKinds(IngredientDatabase database)
        {
            if (database == null) return unlocked.Count;
            int n = 0;
            foreach (string id in unlocked) if (database.Get(id) != null) n++;
            return n;
        }

        private void Notify(IngredientData data)
        {
            if (Changed != null) Changed(data);
        }

        /// <summary>量の表示（机の素材は「∞」、液体は「3.5」、固体は「×12」）</summary>
        public static string Format(IngredientData data, float amount)
        {
            if (float.IsInfinity(amount)) return "∞";
            if (data != null && data.IsPoured) return amount.ToString(amount < 10f ? "0.0" : "0");
            return "×" + amount.ToString(Mathf.Approximately(amount, Mathf.Round(amount)) ? "0" : "0.0");
        }
    }
}
