using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 素材カタログ（ゲームに登場するすべての素材の一覧）。
    /// 素材データ（IngredientData）を新しく作ったら、このアセットの一覧（Inspector）に手で追加する。
    /// ゲーム中は ID で素材を探す（セーブデータの在庫も ID で記録される）。
    /// </summary>
    [CreateAssetMenu(fileName = "IngredientDatabase", menuName = "Potion Frontier/素材カタログ (Ingredient Database)", order = 2)]
    public class IngredientDatabase : ScriptableObject
    {
        [SerializeField, Tooltip("登録されている素材（新しい素材はここに追加する。並び順は カテゴリ → 並び順 → ID）")]
        private List<IngredientData> ingredients = new List<IngredientData>();

        [NonSerialized] private Dictionary<string, IngredientData> byId;

        public IReadOnlyList<IngredientData> All => ingredients;
        public int Count => ingredients.Count;

        /// <summary>IDで素材を探す（見つからなければ null）</summary>
        public IngredientData Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null) BuildIndex();
            byId.TryGetValue(id, out IngredientData data);
            return data;
        }

        public bool Contains(IngredientData data)
        {
            return data != null && Get(data.IngredientId) == data;
        }

        /// <summary>条件に合う素材を順番どおりに集める</summary>
        public void Collect(List<IngredientData> result, Predicate<IngredientData> match)
        {
            result.Clear();
            foreach (IngredientData data in ingredients)
            {
                if (data != null && (match == null || match(data))) result.Add(data);
            }
        }

        private void OnEnable()
        {
            byId = null;
        }

        private void BuildIndex()
        {
            byId = new Dictionary<string, IngredientData>(StringComparer.Ordinal);
            foreach (IngredientData data in ingredients)
            {
                if (data == null) continue;
                string id = data.IngredientId;
                if (!byId.ContainsKey(id)) byId.Add(id, data);
            }
        }

        /// <summary>カテゴリ → 並び順 → ID の順に並べる比較</summary>
        public static int Compare(IngredientData a, IngredientData b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int c = ((int)a.Category).CompareTo((int)b.Category);
            if (c != 0) return c;
            c = a.SortOrder.CompareTo(b.SortOrder);
            if (c != 0) return c;
            return string.CompareOrdinal(a.IngredientId, b.IngredientId);
        }

#if UNITY_EDITOR
        /// <summary>エディタ拡張用：一覧を入れ替える</summary>
        public void EditorSetIngredients(List<IngredientData> list)
        {
            ingredients = new List<IngredientData>(list);
            ingredients.Sort(Compare);
            byId = null;
        }

        /// <summary>エディタ拡張用：一覧が同じか（無駄な保存を避ける）</summary>
        public bool EditorSameAs(List<IngredientData> sorted)
        {
            if (sorted.Count != ingredients.Count) return false;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != ingredients[i]) return false;
            }
            return true;
        }
#endif
    }
}
