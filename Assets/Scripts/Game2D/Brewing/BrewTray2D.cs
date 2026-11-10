using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 素材トレイ（画面の右に縦に立つ棚。Tray の Prefab のルートに付ける）。使える素材と自作の液体を、四角いマス（Slot_00〜）に並べる。
    /// 並び順：ベース液体 → 自作の液体（新しい順）→ 主素材 → 触媒（左上から右へ、上から下へ）。
    /// マスに入りきらないときは、トレイの上でマウスのホイールを回すとページがめくれる（下に小さな点が出る）。
    ///
    /// マスの数・大きさを変えるとき：下の「マス目」の数字を変えて、このコンポーネントを右クリック →「マス目を並べ直す」
    /// （Prefab を開いて行う。Slot_00〜 とマスの絵が作り直される）。
    /// 絵を差し替えるところ（同じ名前の PNG で上書き）：
    ///   CustomAssets/Images/Workbench/tray_panel.png … 棚の板 ／ tray_tile.png … 素材のマス ／ tray_page_dot.png … ページの点
    /// </summary>
    public class BrewTray2D : MonoBehaviour
    {
        [SerializeField, Tooltip("並べる物の見本（TrayItem2D の付いた Prefab）")] private TrayItem2D itemPrefab;
        [SerializeField, Tooltip("マス（空なら「Slot_」で始まる子を名前順に使う）")] private List<Transform> slots = new List<Transform>();

        [Header("マス目（数字を変えたら、右クリック →「マス目を並べ直す」）")]
        [SerializeField, Min(1)] private int columns = 2;
        [SerializeField, Min(1)] private int rows = 7;
        [SerializeField, Min(0.2f), Tooltip("マスとマスの間隔（ワールド単位。100 ピクセル = 1）")] private float cellSize = 0.92f;
        [SerializeField, Min(0.1f), Tooltip("マスの絵の大きさ（ワールド単位）")] private float tileSize = 0.84f;
        [SerializeField, Tooltip("いちばん上の行の真ん中（このトレイから見た位置）")] private Vector2 gridTop = new Vector2(0f, 3.9f);
        [SerializeField, Min(0.1f), Tooltip("マスの中の素材の絵の大きさ（ワールド単位）")] private float itemSize = 0.66f;

        [Header("マスの絵と色")]
        [SerializeField, Tooltip("マスの下地（tray_tile.png）")] private Sprite tileSprite;
        [SerializeField] private Color tileColor = new Color(0.9f, 0.82f, 0.68f, 1f);
        [SerializeField, Tooltip("指したときのマスの色")] private Color tileHoverColor = new Color(1f, 0.95f, 0.78f, 1f);
        [SerializeField, Tooltip("何も入っていないマスの色")] private Color emptyTileColor = new Color(0.9f, 0.82f, 0.68f, 0.3f);

        [Header("ページの点（入りきらないときだけ出る）")]
        [SerializeField, Tooltip("点の絵（tray_page_dot.png。空ならコードで描いた丸）")] private Sprite dotSprite;
        [SerializeField, Tooltip("点の並びの真ん中（このトレイから見た位置）")] private Vector2 dotsCenter = new Vector2(0f, -2.3f);
        [SerializeField, Min(0.05f)] private float dotSpacing = 0.26f;
        [SerializeField, Min(0.02f)] private float dotSize = 0.13f;
        [SerializeField] private Color dotOn = new Color(1f, 0.88f, 0.6f, 1f);
        [SerializeField] private Color dotOff = new Color(1f, 0.88f, 0.6f, 0.3f);

        private readonly List<TrayEntry2D> all = new List<TrayEntry2D>();
        private readonly List<TrayItem2D> items = new List<TrayItem2D>();
        private readonly List<SpriteRenderer> tiles = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();
        private readonly List<IngredientData> ingredientBuffer = new List<IngredientData>();
        private readonly List<PotionStack> bottleBuffer = new List<PotionStack>();
        private BrewSession2D session;
        private Transform dotsRoot;
        private bool dirty = true;

        public int Page { get; private set; }
        public int PageCount => Mathf.Max(1, Mathf.CeilToInt(all.Count / (float)Mathf.Max(1, slots.Count)));
        /// <summary>1ページに並ぶ数</summary>
        public int SlotCount => slots.Count;

        private void Awake()
        {
            if (slots.Count == 0) FindSlots();
            tiles.Clear();
            foreach (Transform s in slots)
            {
                Transform t = s != null ? s.Find("Tile") : null;
                tiles.Add(t != null ? t.GetComponent<SpriteRenderer>() : null);
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
            UpdateDots();

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
                SpriteRenderer tile = i < tiles.Count ? tiles[i] : null;
                bool filled = index < all.Count && itemPrefab != null && slots[i] != null;
                if (tile != null) tile.color = filled ? tileColor : emptyTileColor;
                if (!filled)
                {
                    items.Add(null);
                    continue;
                }
                TrayItem2D item = Instantiate(itemPrefab, slots[i].position, Quaternion.identity, slots[i]);
                item.name = "Item_" + i.ToString("00");
                item.Configure(itemSize, itemSize, new Vector2(tileSize, tileSize), tile, tileColor, tileHoverColor);
                item.Set(all[index], session != null ? session.Art : null);
                items.Add(item);
            }
        }

        // ------------------------------------------------------------------
        // ページの点
        // ------------------------------------------------------------------

        private void UpdateDots()
        {
            int pages = PageCount;
            bool show = pages > 1;
            if (dotsRoot == null)
            {
                if (!show) return;
                dotsRoot = new GameObject("PageDots").transform;
                dotsRoot.SetParent(transform, false);
            }
            dotsRoot.localPosition = new Vector3(dotsCenter.x, dotsCenter.y, 0f);
            dotsRoot.gameObject.SetActive(show);
            if (!show) return;
            Sprite sprite = dotSprite != null ? dotSprite : BrewUiSprites2D.Circle();
            while (dots.Count < pages)
            {
                var go = new GameObject("Dot_" + dots.Count);
                go.transform.SetParent(dotsRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingLayerName = "Workbench";
                sr.sortingOrder = 30;
                float w = sprite != null ? Mathf.Max(0.001f, sprite.bounds.size.x) : 1f;
                go.transform.localScale = Vector3.one * (dotSize / w);
                dots.Add(sr);
            }
            for (int i = 0; i < dots.Count; i++)
            {
                bool on = i < pages;
                dots[i].gameObject.SetActive(on);
                if (!on) continue;
                dots[i].transform.localPosition = new Vector3((i - (pages - 1) * 0.5f) * dotSpacing, 0f, 0f);
                dots[i].color = i == Page ? dotOn : dotOff;
            }
        }

        // ------------------------------------------------------------------
        // マス目（Prefab を作るとき・数字を変えたとき）
        // ------------------------------------------------------------------

        private void FindSlots()
        {
            slots.Clear();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name.StartsWith("Slot_")) slots.Add(child);
            }
            slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>列・行・大きさの数字どおりに、マス（Slot_00〜 と、その下のマスの絵 Tile）を作り直す</summary>
        [ContextMenu("マス目を並べ直す（列・行・大きさの数字どおりに）")]
        public void LayoutGrid()
        {
            Transform root = transform.Find("Slots");
            if (root == null)
            {
                root = new GameObject("Slots").transform;
                root.SetParent(transform, false);
            }
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject old = root.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(old);
                else DestroyImmediate(old);
            }
            slots.Clear();
            tiles.Clear();
            int index = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var slot = new GameObject("Slot_" + index.ToString("00")).transform;
                    slot.SetParent(root, false);
                    slot.localPosition = new Vector3(gridTop.x + (c - (columns - 1) * 0.5f) * cellSize, gridTop.y - r * cellSize, 0f);
                    var tileGo = new GameObject("Tile");
                    tileGo.transform.SetParent(slot, false);
                    var sr = tileGo.AddComponent<SpriteRenderer>();
                    sr.sprite = tileSprite;
                    sr.sortingLayerName = "Workbench";
                    sr.sortingOrder = 20;
                    sr.color = tileColor;
                    float w = tileSprite != null ? Mathf.Max(0.001f, tileSprite.bounds.size.x) : 1f;
                    tileGo.transform.localScale = Vector3.one * (tileSize / w);
                    slots.Add(slot);
                    tiles.Add(sr);
                    index++;
                }
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // ------------------------------------------------------------------
        // 並べる物
        // ------------------------------------------------------------------

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
