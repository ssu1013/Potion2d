using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>トレイに並ぶ物1つ分（素材か、自分で瓶詰めした液体のどちらか）</summary>
    public struct TrayEntry
    {
        public IngredientData ingredient;
        public PotionStack bottle;

        public bool IsEmpty => ingredient == null && bottle == null;
        public bool Same(TrayEntry other) => ingredient == other.ingredient && bottle == other.bottle;
    }

    /// <summary>
    /// 調合台の手前のトレイ。使える素材と自作の液体を、素材箱と同じ並び（ベース液体 → 自作の液体（新しい順）→ 主素材 → 触媒）で、
    /// 「Slot_00」「Slot_01」…の目印の位置に実物のモデルとして並べる。入りきらない分はページで切り替える（BrewMode と調合画面の UI が操作する）。
    /// トレイを自作モデルにするときは、Slot_ の目印を置けばよい。
    /// </summary>
    public class BrewTray : MonoBehaviour
    {
        public const int TabAll = 0;
        public const int TabLiquids = 1;
        public const int TabMain = 2;
        public const int TabCatalyst = 3;
        public const int TabOwn = 4;
        public static readonly string[] TabLabels = { "すべて", "液体", "主素材", "触媒", "自作" };

        [SerializeField, Min(0.02f), Tooltip("トレイに並べる物の高さ（m）。大きさの違うモデルでもこの高さにそろえる")] private float itemHeight = 0.085f;

        private readonly List<Transform> slots = new List<Transform>();
        private readonly List<BrewTrayItem> items = new List<BrewTrayItem>();
        private readonly List<TrayEntry> all = new List<TrayEntry>();
        private readonly List<TrayEntry> shown = new List<TrayEntry>();
        private readonly List<IngredientData> buffer = new List<IngredientData>();
        private readonly List<PotionStack> bottleBuffer = new List<PotionStack>();
        private IngredientInventory ingredients;
        private PotionInventory potions;
        private bool dirty = true;

        public int Tab { get; private set; }
        public int Page { get; private set; }
        public int PageCount => Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)Mathf.Max(1, slots.Count)));
        public int TotalCount => all.Count;
        public IReadOnlyList<BrewTrayItem> Items => items;
        public int SlotCount => slots.Count;
        public IngredientInventory Ingredients => ingredients;
        public PotionInventory Potions => potions;

        private void Awake()
        {
            for (int i = 0; i < 64; i++)
            {
                Transform t = PrefabAnchors.Find(transform, $"Slot_{i:00}");
                if (t == null) break;
                slots.Add(t);
            }
            ingredients = FindFirstObjectByType<IngredientInventory>();
            potions = FindFirstObjectByType<PotionInventory>();
        }

        private void OnEnable()
        {
            if (ingredients != null) ingredients.Changed += OnIngredientsChanged;
            if (potions != null) potions.Changed += OnPotionsChanged;
        }

        private void OnDisable()
        {
            if (ingredients != null) ingredients.Changed -= OnIngredientsChanged;
            if (potions != null) potions.Changed -= OnPotionsChanged;
        }

        private void Start()
        {
            Rebuild();
        }

        private void OnIngredientsChanged(IngredientData _) { dirty = true; }
        private void OnPotionsChanged() { dirty = true; }

        private void LateUpdate()
        {
            if (dirty) Rebuild();
        }

        public void SetTab(int tab)
        {
            tab = Mathf.Clamp(tab, 0, TabLabels.Length - 1);
            if (tab == Tab) return;
            Tab = tab;
            Page = 0;
            Rebuild();
        }

        public void SetPage(int page)
        {
            int clamped = Mathf.Clamp(page, 0, PageCount - 1);
            if (clamped == Page) return;
            Page = clamped;
            Rebuild();
        }

        /// <summary>中身を作り直す（在庫が変わったとき・ページを変えたとき）</summary>
        public void Rebuild()
        {
            dirty = false;
            Collect(all);
            Page = Mathf.Clamp(Page, 0, PageCount - 1);
            int start = Page * slots.Count;

            shown.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                int index = start + i;
                shown.Add(index < all.Count ? all[index] : default);
            }

            // 並ぶ物が同じなら作り直さない（量の表示だけ変わる）
            bool same = items.Count == slots.Count;
            for (int i = 0; same && i < slots.Count; i++)
            {
                TrayEntry current = items[i] != null ? items[i].Entry : default;
                same = current.Same(shown[i]);
            }
            if (same)
            {
                foreach (BrewTrayItem item in items) if (item != null) item.RefreshAmount();
                return;
            }

            foreach (BrewTrayItem item in items)
            {
                if (item != null) Destroy(item.gameObject);
            }
            items.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                items.Add(shown[i].IsEmpty ? null : CreateItem(shown[i], slots[i]));
            }
        }

        /// <summary>今のタブに並ぶ物（素材箱と同じ順番）</summary>
        private void Collect(List<TrayEntry> result)
        {
            result.Clear();
            buffer.Clear();
            IngredientDatabase db = GameContext.Ingredients;
            if (db != null && Tab != TabOwn)
            {
                db.Collect(buffer, d =>
                {
                    if (d == null || ingredients == null || !ingredients.Has(d)) return false;
                    switch (Tab)
                    {
                        case TabLiquids: return d.Category == IngredientCategory.BaseLiquid;
                        case TabMain: return d.Category == IngredientCategory.MainMaterial;
                        case TabCatalyst: return d.Category == IngredientCategory.Catalyst;
                        default: return true;
                    }
                });
                buffer.Sort(IngredientDatabase.Compare);
            }

            bottleBuffer.Clear();
            if (potions != null && (Tab == TabAll || Tab == TabLiquids || Tab == TabOwn))
            {
                foreach (PotionStack s in potions.Stacks)
                {
                    if (s != null && s.ml >= PotionStack.EmptyMl) bottleBuffer.Add(s);
                }
                bottleBuffer.Sort((a, b) => b.addedOrder.CompareTo(a.addedOrder));
            }

            int insertAt = 0;
            while (insertAt < buffer.Count && buffer[insertAt].Category == IngredientCategory.BaseLiquid) insertAt++;
            for (int i = 0; i < insertAt; i++) result.Add(new TrayEntry { ingredient = buffer[i] });
            foreach (PotionStack s in bottleBuffer) result.Add(new TrayEntry { bottle = s });
            for (int i = insertAt; i < buffer.Count; i++) result.Add(new TrayEntry { ingredient = buffer[i] });
        }

        private BrewTrayItem CreateItem(TrayEntry entry, Transform slot)
        {
            GameObject model;
            if (entry.bottle != null)
            {
                model = PotionVisuals.CreateBottle(entry.bottle.color, entry.bottle.baseLiquidId, slot, "TrayBottle_" + entry.bottle.potionName);
            }
            else
            {
                GameObject prefab = GameContext.Art != null ? GameContext.Art.IngredientModel(entry.ingredient) : null;
                model = prefab != null ? Instantiate(prefab, slot) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                model.name = "TrayItem_" + entry.ingredient.DisplayName;
                model.transform.SetParent(slot, false);
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (Interactable i in model.GetComponentsInChildren<Interactable>(true)) Destroy(i);
                var content = model.GetComponent<ContentVisual>();
                if (content != null) content.SetColor(entry.ingredient.Color);
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 高さをそろえて、スロットの上に底を置く
            Transform t = model.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            Bounds b = PrefabAnchors.RendererBounds(t);
            float scale = Mathf.Clamp(itemHeight / Mathf.Max(0.01f, b.size.y), 0.15f, 4f);
            t.localScale *= scale;
            b = PrefabAnchors.RendererBounds(t);
            t.position += new Vector3(slot.position.x - b.center.x, slot.position.y - b.min.y + 0.002f, slot.position.z - b.center.z);

            var item = model.AddComponent<BrewTrayItem>();
            item.Configure(this, entry);
            b = PrefabAnchors.RendererBounds(t);
            var box = model.AddComponent<BoxCollider>();
            box.center = t.InverseTransformPoint(b.center);
            Vector3 lossy = t.lossyScale;
            box.size = new Vector3(b.size.x / lossy.x, b.size.y / lossy.y, b.size.z / lossy.z) + Vector3.one * (0.01f / Mathf.Max(0.01f, lossy.x));
            return item;
        }

        /// <summary>素材・液体の量の表示（∞・×2・240ml）</summary>
        public static string AmountLabel(TrayEntry entry, IngredientInventory ingredients)
        {
            if (entry.bottle != null) return $"{entry.bottle.ml:0}ml";
            if (entry.ingredient == null || ingredients == null) return "";
            return IngredientInventory.Format(entry.ingredient, ingredients.Get(entry.ingredient));
        }
    }
}
