using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合トレイ（Tray の Prefab のルートに付ける）。使える素材と自作の液体を、置き場所（Slot_00〜）に並べる。
    /// 並び順：ベース液体 → 自作の液体（新しい順）→ 主素材 → 触媒（3D 版の素材箱と同じ）。
    /// 置き場所の数を増やす・減らすと、1ページに並ぶ数も変わる。
    /// </summary>
    public class BrewTray2D : MonoBehaviour
    {
        [SerializeField, Tooltip("並べる物の見本（TrayItem2D の付いた Prefab）")] private TrayItem2D itemPrefab;
        [SerializeField, Tooltip("置き場所（空なら「Slot_」で始まる子を名前順に使う）")] private List<Transform> slots = new List<Transform>();

        private readonly List<TrayEntry2D> all = new List<TrayEntry2D>();
        private readonly List<TrayItem2D> items = new List<TrayItem2D>();
        private readonly List<IngredientData> ingredientBuffer = new List<IngredientData>();
        private readonly List<PotionStack> bottleBuffer = new List<PotionStack>();
        private BrewSession2D session;
        private bool dirty = true;

        public int Page { get; private set; }
        public int PageCount => Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)Mathf.Max(1, slots.Count)));

        private void Awake()
        {
            if (slots.Count == 0)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child != transform && child.name.StartsWith("Slot_")) slots.Add(child);
                }
                slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            }
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            if (session != null)
            {
                if (session.Ingredients != null) session.Ingredients.Changed += OnIngredientsChanged;
                if (session.Potions != null) session.Potions.Changed += MarkDirty;
            }
            Rebuild();
        }

        private void OnDestroy()
        {
            if (session == null) return;
            if (session.Ingredients != null) session.Ingredients.Changed -= OnIngredientsChanged;
            if (session.Potions != null) session.Potions.Changed -= MarkDirty;
        }

        private void OnIngredientsChanged(IngredientData data) => dirty = true;
        private void MarkDirty() => dirty = true;

        private void LateUpdate()
        {
            if (dirty) Rebuild();
        }

        public void ChangePage(int direction)
        {
            Page = (Page + direction + PageCount) % PageCount;
            dirty = true;
        }

        public void Rebuild()
        {
            dirty = false;
            Collect();
            Page = Mathf.Clamp(Page, 0, PageCount - 1);
            int start = Page * slots.Count;

            // 並ぶ物が同じなら作り直さない
            bool same = items.Count == slots.Count;
            for (int i = 0; same && i < slots.Count; i++)
            {
                TrayEntry2D want = start + i < all.Count ? all[start + i] : default(TrayEntry2D);
                TrayEntry2D have = items[i] != null ? items[i].Entry : default(TrayEntry2D);
                same = want.Same(have);
            }
            if (same) return;

            foreach (TrayItem2D item in items)
            {
                if (item != null) Destroy(item.gameObject);
            }
            items.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                int index = start + i;
                if (index >= all.Count || itemPrefab == null || slots[i] == null)
                {
                    items.Add(null);
                    continue;
                }
                TrayItem2D item = Instantiate(itemPrefab, slots[i].position, Quaternion.identity, slots[i]);
                item.name = "Item_" + i.ToString("00");
                item.Set(all[index], session != null ? session.Art : null);
                items.Add(item);
            }
        }

        private void Collect()
        {
            all.Clear();
            ingredientBuffer.Clear();
            bottleBuffer.Clear();
            IngredientDatabase db = GameContext.Ingredients;
            IngredientInventory stock = session != null ? session.Ingredients : null;
            if (db != null)
            {
                db.Collect(ingredientBuffer, d => d != null && (stock == null || stock.Has(d)));
                ingredientBuffer.Sort(IngredientDatabase.Compare);
            }
            PotionInventory potions = session != null ? session.Potions : null;
            if (potions != null)
            {
                foreach (PotionStack s in potions.Stacks)
                {
                    if (s != null && s.ml >= PotionStack.EmptyMl) bottleBuffer.Add(s);
                }
                bottleBuffer.Sort((a, b) => b.addedOrder.CompareTo(a.addedOrder));
            }
            int insertAt = 0;
            while (insertAt < ingredientBuffer.Count && ingredientBuffer[insertAt].Category == IngredientCategory.BaseLiquid) insertAt++;
            for (int i = 0; i < ingredientBuffer.Count; i++)
            {
                if (i == insertAt) AddBottles();
                all.Add(new TrayEntry2D { ingredient = ingredientBuffer[i] });
            }
            if (insertAt >= ingredientBuffer.Count) AddBottles();
        }

        private void AddBottles()
        {
            foreach (PotionStack s in bottleBuffer) all.Add(new TrayEntry2D { potion = s });
        }
    }
}
