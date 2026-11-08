using System;
using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 素材と瓶詰めした液体の一覧ウィンドウ。2つの使い方がある。
    ///   ・素材箱／ポーション棚（Box） … 使える素材と、自分で瓶詰めした液体を並べ、選んで「手に持つ」。
    ///       自作の液体は「すべて」「ベース液体」タブで素材と同じ並びに入り（ベース液体のすぐ後ろ・新しい順）、
    ///       「自作の液体」タブ（＝ポーション棚）ではそれだけを並べる。
    ///   ・取引先帳（Shop） … 素材の入手ルート。使い放題の素材は「開拓する」（一度払えばずっと使い放題）、
    ///       希少な素材は「取り寄せる」（数を決めて、使った分だけ減る在庫を買う）。
    /// 種類の切り替え（タブ）と、名前・タグでの絞り込みができる。素材や液体が数百種類あっても、
    /// マスは表示する数だけ作って使い回すので重くならない。
    /// </summary>
    public sealed class IngredientBrowserUI
    {
        public enum Mode { Box, Shop }

        /// <summary>「自作の液体」タブの番号（Tab キーで開くポーション棚）</summary>
        public const int LiquidTab = 4;

        private const int Columns = 6;
        private const float CellSize = 118f;
        private const float CellSpacing = 12f;
        private const float Padding = 30f;
        private const float TopArea = 96f;
        private const int MaxQuantity = 99;

        private static readonly string[] TabLabels = { "すべて", "ベース液体", "主素材", "触媒", "自作の液体" };
        private static readonly float[] TabWidths = { 88f, 124f, 92f, 76f, 124f };
        private static readonly Color Green = new Color(0.18f, 0.45f, 0.16f);

        /// <summary>一覧の1マス分（素材か、瓶詰めした液体のどちらか）</summary>
        private struct Entry
        {
            public IngredientData ingredient;
            public PotionStack bottle;
        }

        private readonly IngredientInventory inventory;
        private readonly PotionInventory potions;
        private readonly PlayerProgress progress;
        private readonly Func<AlchemySettings> settings;
        private readonly Func<string, LiquidRecord> findLiquid;
        private readonly Func<PotionStack, string> consultationFor;
        private readonly Action onClose;
        private readonly Action<IngredientData> onTake;
        private readonly Action<PotionStack> onTakeBottle;
        private readonly Func<IngredientData, int, bool> onTrade;
        private readonly GameObject root;
        private readonly RectTransform panelRoot;
        private readonly List<Cell> cells = new List<Cell>();
        private readonly List<Entry> items = new List<Entry>();
        private readonly List<IngredientData> buffer = new List<IngredientData>();
        private readonly List<PotionStack> bottleBuffer = new List<PotionStack>();
        private readonly List<Image> tabs = new List<Image>();
        private readonly Sprite slotSprite;
        private readonly Sprite slotSelectedSprite;

        private Text bannerText;
        private Text goldText;
        private Text countText;
        private InputField search;
        private ScrollRect scroll;
        private RectTransform gridContent;
        private Text emptyText;

        private GameObject detailContent;
        private Text detailEmpty;
        private Image detailIcon;
        private Text detailName;
        private Text detailMeta;
        private Text detailBody;
        private Text detailStock;
        private Text detailNote;
        private Button takeButton;
        private GameObject buyArea;
        private GameObject quantityRow;
        private Text quantityText;
        private Button buyButton;
        private Text buyLabel;
        private Text buyNote;
        private Text footerAction;
        private Text footerTabs;
        private GameObject quantityKeys;
        private GameObject quantityKeysLabel;

        private Mode mode;
        private int tab;
        private string filter = "";
        private int selected;
        private int quantity = 1;
        private float appear;
        private bool dirty;
        private float lastClickTime = -10f;

        private struct Cell
        {
            public GameObject root;
            public Image frame;
            public Image icon;
            public Text count;
            public Text price;
            public Text rarity;
            public Text tag;
        }

        public bool IsOpen => root.activeSelf;
        public Mode CurrentMode => mode;
        public int CurrentTab => tab;
        /// <summary>ポーション棚（自作の液体タブ）を見ている</summary>
        public bool IsShowingLiquids => IsOpen && mode == Mode.Box && tab == LiquidTab;
        /// <summary>検索欄に文字を打っている（キー操作をウィンドウに渡さない）</summary>
        public bool IsTyping => search != null && search.isFocused;
        /// <summary>選んでいる素材（瓶詰めした液体を選んでいるときは null）</summary>
        public IngredientData Selected => selected >= 0 && selected < items.Count ? items[selected].ingredient : null;
        /// <summary>選んでいる瓶詰めした液体（素材を選んでいるときは null）</summary>
        public PotionStack SelectedBottle => selected >= 0 && selected < items.Count ? items[selected].bottle : null;
        public int Quantity => quantity;
        public int VisibleCount => items.Count;

        public int VisibleBottleCount
        {
            get
            {
                int n = 0;
                foreach (Entry e in items) if (e.bottle != null) n++;
                return n;
            }
        }

        public IngredientBrowserUI(RectTransform canvasRoot, IngredientInventory inventory, PotionInventory potions, PlayerProgress progress,
                                   Func<AlchemySettings> settings, Func<string, LiquidRecord> findLiquid, Func<PotionStack, string> consultationFor,
                                   Action onClose, Action<IngredientData> onTake, Action<PotionStack> onTakeBottle, Func<IngredientData, int, bool> onTrade)
        {
            this.inventory = inventory;
            this.potions = potions;
            this.progress = progress;
            this.settings = settings;
            this.findLiquid = findLiquid;
            this.consultationFor = consultationFor;
            this.onClose = onClose;
            this.onTake = onTake;
            this.onTakeBottle = onTakeBottle;
            this.onTrade = onTrade;
            slotSprite = UIFactory.SlotSprite(false);
            slotSelectedSprite = UIFactory.SlotSprite(true);

            Image overlay = UIFactory.CreateImage(canvasRoot, "IngredientWindow", null, new Color(0f, 0f, 0f, 0.6f));
            overlay.raycastTarget = true;
            UIFactory.Stretch(overlay.rectTransform);
            root = overlay.gameObject;

            panelRoot = UIFactory.CreateRect("Panel", overlay.rectTransform);
            UIFactory.Stretch(panelRoot);

            BuildBanner(panelRoot);
            BuildGrid(panelRoot);
            BuildDetail(panelRoot);
            BuildCloseButton(panelRoot);
            BuildFooter(panelRoot);

            root.SetActive(false);
            if (inventory != null) inventory.Changed += _ => dirty = true;
            if (potions != null) potions.Changed += () => dirty = true;
            if (progress != null)
            {
                progress.GoldChanged += (_, __) => dirty = true;
                progress.Changed += () => dirty = true;
            }
        }

        /// <summary>開く。startTab を指定するとそのタブで開く（-1 なら前のタブのまま。取引先帳では自作の液体タブは使わない）</summary>
        public void Open(Mode openMode, int startTab = -1)
        {
            mode = openMode;
            if (startTab >= 0) tab = startTab;
            if (mode == Mode.Shop && tab == LiquidTab) tab = 0;
            root.SetActive(true);
            appear = 0f;
            quantity = 1;
            goldText.transform.parent.gameObject.SetActive(mode == Mode.Shop);
            takeButton.transform.parent.gameObject.SetActive(mode == Mode.Box);
            buyArea.SetActive(mode == Mode.Shop);
            footerAction.text = mode == Mode.Shop ? "開拓・取り寄せ" : "手に持つ";
            footerTabs.text = mode == Mode.Shop ? "1〜4" : "1〜5";
            quantityKeys.SetActive(mode == Mode.Shop);
            quantityKeysLabel.SetActive(mode == Mode.Shop);
            tabs[LiquidTab].transform.gameObject.SetActive(mode == Mode.Box);
            RefreshBanner();
            RebuildList(true);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiOpen, 0.7f);
        }

        public void Close()
        {
            if (!root.activeSelf) return;
            if (search.isFocused) search.DeactivateInputField();
            root.SetActive(false);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.7f);
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            appear = Mathf.Min(1f, appear + dt * 7f);
            float e = 1f - Mathf.Pow(1f - appear, 3f);
            float s = Mathf.Lerp(0.94f, 1f, e);
            panelRoot.localScale = new Vector3(s, s, 1f);

            if (dirty)
            {
                dirty = false;
                RebuildList(false);
            }
            if (!allowInput || IsTyping) return;

            int move = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow)) move = 1;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) move = -1;
            if (Input.GetKeyDown(KeyCode.DownArrow)) move = Columns;
            if (Input.GetKeyDown(KeyCode.UpArrow)) move = -Columns;
            if (move != 0 && items.Count > 0)
            {
                int next = Mathf.Clamp(selected + move, 0, items.Count - 1);
                if (next != selected)
                {
                    Select(next);
                    EnsureVisible(next);
                }
            }
            int tabCount = mode == Mode.Shop ? LiquidTab : TabLabels.Length;
            for (int i = 0; i < tabCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetTab(i);
            }
            if (mode == Mode.Shop)
            {
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Q)) ChangeQuantity(-1);
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.R)) ChangeQuantity(1);
            }
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                PrimaryAction();
            }
        }

        // ------------------------------------------------------------------
        // 操作（自動テストからも呼べる）
        // ------------------------------------------------------------------

        /// <summary>選んでいる物を手に持つ（素材箱）／開拓する・取り寄せる（取引先帳）</summary>
        public void PrimaryAction()
        {
            if (mode == Mode.Box)
            {
                PotionStack bottle = SelectedBottle;
                if (bottle != null)
                {
                    if (onTakeBottle != null) onTakeBottle(bottle);
                    return;
                }
                IngredientData data = Selected;
                if (data != null && onTake != null && inventory != null && inventory.Has(data)) onTake(data);
                return;
            }
            Buy();
        }

        /// <summary>取引先帳：選んでいる素材の入手ルートを開拓する（使い放題）／取り寄せる（希少な素材）</summary>
        public void Buy()
        {
            IngredientData data = Selected;
            if (data == null || onTrade == null || mode != Mode.Shop) return;
            if (onTrade(data, quantity))
            {
                quantity = 1;
                RefreshDetail();
            }
        }

        /// <summary>素材または瓶詰めした液体を ID で選ぶ（見つからなければ false）</summary>
        public bool SelectById(string id)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Entry e = items[i];
                bool match = e.ingredient != null ? e.ingredient.IngredientId == id : e.bottle != null && (e.bottle.potionId == id || e.bottle.liquidId == id);
                if (match)
                {
                    Select(i);
                    EnsureVisible(i);
                    return true;
                }
            }
            return false;
        }

        public void SetQuantity(int value)
        {
            quantity = Mathf.Clamp(value, 1, MaxQuantity);
            RefreshDetail();
        }

        public void SetFilter(string value)
        {
            search.text = value ?? "";
        }

        public void SetTab(int index)
        {
            int max = mode == Mode.Shop ? LiquidTab - 1 : TabLabels.Length - 1;
            index = Mathf.Clamp(index, 0, max);
            if (index == tab) return;
            tab = index;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.6f);
            RefreshBanner();
            RebuildList(true);
        }

        // ------------------------------------------------------------------

        private void RefreshBanner()
        {
            bannerText.text = mode == Mode.Shop ? "取引先帳" : tab == LiquidTab ? "ポーション棚" : "素材箱";
            emptyText.text = mode == Mode.Shop
                ? "取引できる素材がありません。"
                : tab == LiquidTab
                    ? "まだ瓶詰めした液体がありません。\nフラスコで調合して、瓶に詰めてみよう。\n（失敗した液体も、次の実験の材料になる）"
                    : "使える素材がありません。\n入口近くの「取引先帳」で入手ルートを開拓できます。";
        }

        private void ChangeQuantity(int delta)
        {
            int before = quantity;
            SetQuantity(quantity + delta);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null && before != quantity) AudioManager.Se(lib.uiHover, 0.5f);
        }

        /// <summary>取引先帳に出る素材か（開拓できる使い放題の素材・取り寄せできる希少な素材）</summary>
        private static bool IsTradable(IngredientData data)
        {
            if (IngredientInventory.IsInfiniteKind(data))
            {
                GameBalance balance = GameContext.Balance;
                return data.UnlockCost(balance != null ? balance.unlockPriceMultiplier : 8) > 0;
            }
            return data.BuyPrice > 0;
        }

        private bool Matches(IngredientData data)
        {
            if (data == null) return false;
            if (mode == Mode.Box && (inventory == null || !inventory.Has(data))) return false;
            if (mode == Mode.Shop && !IsTradable(data) && (inventory == null || !inventory.IsUnlocked(data))) return false;
            if (tab == LiquidTab) return false;
            if (tab == 1 && data.Category != IngredientCategory.BaseLiquid) return false;
            if (tab == 2 && data.Category != IngredientCategory.MainMaterial) return false;
            if (tab == 3 && data.Category != IngredientCategory.Catalyst) return false;
            if (string.IsNullOrEmpty(filter)) return true;
            if (Contains(data.DisplayName, filter) || Contains(data.IngredientId, filter)) return true;
            foreach (string t in data.Tags)
            {
                if (Contains(t, filter)) return true;
            }
            return false;
        }

        private bool MatchesBottle(PotionStack stack)
        {
            if (stack == null || stack.ml < PotionStack.EmptyMl) return false;
            if (string.IsNullOrEmpty(filter)) return true;
            if (Contains(stack.potionName, filter) || Contains(KindLabel(stack.kind), filter)) return true;
            foreach (string effect in stack.effects)
            {
                if (Contains(effect, filter)) return true;
            }
            return false;
        }

        private static bool Contains(string text, string part)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildList(bool resetSelection)
        {
            Entry keep = resetSelection || selected < 0 || selected >= items.Count ? default : items[selected];
            items.Clear();

            buffer.Clear();
            IngredientDatabase db = GameContext.Ingredients;
            if (db != null)
            {
                db.Collect(buffer, Matches);
                buffer.Sort(IngredientDatabase.Compare);
            }

            // 自作の液体（素材箱の「すべて」「ベース液体」「自作の液体」タブ）は新しい順
            bottleBuffer.Clear();
            bool showBottles = mode == Mode.Box && potions != null && (tab == 0 || tab == 1 || tab == LiquidTab);
            if (showBottles)
            {
                foreach (PotionStack s in potions.Stacks)
                {
                    if (MatchesBottle(s)) bottleBuffer.Add(s);
                }
                bottleBuffer.Sort((a, b) => b.addedOrder.CompareTo(a.addedOrder));
            }

            // ベース液体のすぐ後ろに自作の液体を入れる（どちらも「注いで使う液体」）
            int insertAt = 0;
            while (insertAt < buffer.Count && buffer[insertAt].Category == IngredientCategory.BaseLiquid) insertAt++;
            for (int i = 0; i < insertAt; i++) items.Add(new Entry { ingredient = buffer[i] });
            foreach (PotionStack s in bottleBuffer) items.Add(new Entry { bottle = s });
            for (int i = insertAt; i < buffer.Count; i++) items.Add(new Entry { ingredient = buffer[i] });

            // マスは足りない分だけ作り、余りは隠す
            while (cells.Count < items.Count) cells.Add(CreateCell(cells.Count));
            for (int i = 0; i < cells.Count; i++)
            {
                bool active = i < items.Count;
                if (cells[i].root.activeSelf != active) cells[i].root.SetActive(active);
                if (active) FillCell(cells[i], items[i]);
            }

            int index = -1;
            if (keep.ingredient != null || keep.bottle != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if ((keep.ingredient != null && items[i].ingredient == keep.ingredient) || (keep.bottle != null && items[i].bottle == keep.bottle))
                    {
                        index = i;
                        break;
                    }
                }
            }
            selected = index >= 0 ? index : Mathf.Clamp(resetSelection ? 0 : selected, 0, Mathf.Max(0, items.Count - 1));
            if (resetSelection && scroll != null) scroll.verticalNormalizedPosition = 1f;

            for (int i = 0; i < tabs.Count; i++) tabs[i].color = i == tab ? Color.white : new Color(0.62f, 0.55f, 0.48f, 1f);
            int bottles = bottleBuffer.Count;
            countText.text = bottles > 0 && tab != LiquidTab ? $"{items.Count - bottles} 種類 ＋ 自作の液体 {bottles} 種類" : $"{items.Count} 種類";
            emptyText.gameObject.SetActive(items.Count == 0 && string.IsNullOrEmpty(filter));
            RefreshSelection();
            RefreshDetail();
        }

        private void FillCell(Cell cell, Entry entry)
        {
            if (entry.bottle != null)
            {
                PotionStack s = entry.bottle;
                cell.icon.sprite = UIFactory.PotionIcon(s.color);
                cell.icon.color = Color.white;
                cell.count.text = $"{s.ml:0}ml";
                cell.price.gameObject.SetActive(false);
                cell.rarity.text = s.stars > 0 ? new string('★', Mathf.Min(5, s.stars)) : "";
                string consultation = consultationFor != null ? consultationFor(s) : null;
                if (s.isNew) SetTag(cell, "NEW", UIColors.Gold);
                else if (consultation != null) SetTag(cell, "相談", new Color(0.55f, 0.95f, 0.5f));
                else SetTag(cell, "", Color.white);
                return;
            }

            IngredientData data = entry.ingredient;
            cell.icon.sprite = IngredientIcons.Get(data);
            float stock = inventory != null ? inventory.Get(data) : 0f;
            bool has = stock >= IngredientInventory.Epsilon;
            cell.count.text = has ? IngredientInventory.Format(data, stock) : "";
            cell.rarity.text = data.Rarity >= 3 ? new string('★', Mathf.Min(5, data.Rarity)) : "";
            SetTag(cell, "", Color.white);
            cell.price.gameObject.SetActive(mode == Mode.Shop);
            if (mode == Mode.Shop)
            {
                bool locked = IsReputationLocked(data);
                bool infinite = IngredientInventory.IsInfiniteKind(data);
                bool open = inventory != null && inventory.IsUnlimited(data);
                if (locked) cell.price.text = "<color=#C9B79A>実績" + data.RequiredReputation + "</color>";
                else if (infinite && open) cell.price.text = "<color=#8EE07A>取引中</color>";
                else if (infinite) cell.price.text = UnlockCost(data) + "G";
                else cell.price.text = data.BuyPrice + "G";
                cell.icon.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            }
            else cell.icon.color = has ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        private static void SetTag(Cell cell, string text, Color color)
        {
            cell.tag.text = text;
            cell.tag.color = color;
        }

        private bool IsReputationLocked(IngredientData data)
        {
            if (data == null || data.RequiredReputation <= 0) return false;
            if (inventory != null && inventory.IsUnlocked(data)) return false;
            int done = progress != null ? progress.CompletedQuests : 0;
            return done < data.RequiredReputation;
        }

        private static int UnlockCost(IngredientData data)
        {
            GameBalance balance = GameContext.Balance;
            return data.UnlockCost(balance != null ? balance.unlockPriceMultiplier : 8);
        }

        private void Select(int index)
        {
            if (index == selected) return;
            selected = index;
            quantity = 1;
            // 自分で選んだ瓶は「見た」ことにする（NEW の札を消す。開いたときに最初から選ばれているだけでは消さない）
            PotionStack bottle = SelectedBottle;
            if (bottle != null && bottle.isNew && potions != null) potions.MarkSeen(bottle);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.shelfPlace, 0.35f, 1.3f);
            RefreshSelection();
            RefreshDetail();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < items.Count && i < cells.Count; i++)
            {
                Sprite want = i == selected ? slotSelectedSprite : slotSprite;
                if (cells[i].frame.sprite != want) cells[i].frame.sprite = want;
            }
        }

        private void EnsureVisible(int index)
        {
            if (scroll == null) return;
            RectTransform viewport = scroll.viewport;
            float rowTop = (index / Columns) * (CellSize + CellSpacing);
            float rowBottom = rowTop + CellSize;
            float viewHeight = viewport.rect.height;
            Vector2 pos = gridContent.anchoredPosition;
            if (rowTop < pos.y) pos.y = rowTop;
            else if (rowBottom > pos.y + viewHeight) pos.y = rowBottom - viewHeight;
            gridContent.anchoredPosition = pos;
        }

        private void RefreshDetail()
        {
            if (progress != null) goldText.text = progress.Gold + " G";
            Entry entry = selected >= 0 && selected < items.Count ? items[selected] : default;
            bool any = entry.ingredient != null || entry.bottle != null;
            detailContent.SetActive(any);
            detailEmpty.gameObject.SetActive(!any);
            if (!any) return;

            if (entry.bottle != null) RefreshBottleDetail(entry.bottle);
            else RefreshIngredientDetail(entry.ingredient);
        }

        private void RefreshBottleDetail(PotionStack stack)
        {
            AlchemySettings alchemySettings = settings != null ? settings() : null;
            LiquidRecord record = findLiquid != null ? findLiquid(stack.liquidId) : null;
            detailIcon.sprite = UIFactory.PotionIcon(stack.color);
            detailName.text = stack.potionName;
            string stars = stack.stars > 0 ? $"　<color=#B5781C>{BrewResult.StarText(stack.stars)}</color>" : "";
            detailMeta.text = $"{KindLabel(stack.kind)}・自作の液体{stars}";

            var sb = new StringBuilder();
            // 濃さ（100ml あたり。強い順に4つまで）
            AttributeVector v = stack.Attributes;
            int shown = 0;
            foreach (PotionAttribute a in v.GetRankedAttributes())
            {
                if (v[a] < 0.5f || shown >= 4) continue;
                if (shown > 0) sb.Append("　");
                sb.Append(alchemySettings != null ? alchemySettings.GetDisplayName(a) : a.ToString()).Append(' ').Append(v[a].ToString("0.#"));
                shown++;
            }
            if (shown == 0) sb.Append("（属性なし）");
            sb.Append(" <size=17><color=#8A6A45>（100mlあたり）</color></size>");
            if (record != null && alchemySettings != null)
            {
                bool first = true;
                foreach (LiquidTrait t in record.traits)
                {
                    if (t.strength < 0.25f) continue;
                    sb.Append(first ? "\n<color=#7A5A3A>特性：" : "・").Append(alchemySettings.GetTrait(t.id).displayName);
                    first = false;
                }
                if (!first) sb.Append("</color>");
            }
            foreach (string effect in stack.effects) sb.Append("\n・").Append(effect);
            if (record != null && record.recipes.Count > 0)
            {
                List<ProcessStep> steps = record.recipes[0].steps;
                sb.Append("\n<size=17><color=#6B4A2B>作り方：");
                int n = Mathf.Min(3, steps.Count);
                for (int i = 0; i < n; i++) sb.Append(i > 0 ? " → " : "").Append(steps[i].Describe());
                if (steps.Count > n) sb.Append($" …（全{steps.Count}工程）");
                sb.Append("</color></size>");
            }
            detailBody.text = sb.ToString();

            detailStock.text = stack.DescribeAmount();
            string consultation = consultationFor != null ? consultationFor(stack) : null;
            detailNote.gameObject.SetActive(consultation != null);
            if (consultation != null) detailNote.text = $"○ {consultation}さんの悩みに役立ちそう！";
            takeButton.interactable = stack.ml >= PotionStack.EmptyMl;
        }

        private void RefreshIngredientDetail(IngredientData data)
        {
            detailIcon.sprite = IngredientIcons.Get(data);
            detailName.text = data.DisplayName;
            string stars = new string('★', Mathf.Clamp(data.Rarity, 1, 5)) + new string('☆', 5 - Mathf.Clamp(data.Rarity, 1, 5));
            detailMeta.text = $"{CategoryLabel(data.Category)}・{FormLabel(data.Form)}　<color=#B5781C>{stars}</color>";

            var sb = new StringBuilder();
            AlchemySettings alchemySettings = settings != null ? settings() : null;
            string attributes = DescribeAttributes(data, alchemySettings);
            int firstGap = attributes.IndexOf('　');
            sb.Append(firstGap >= 0 ? attributes.Substring(firstGap + 1) : "（属性なし）");
            if (!string.IsNullOrEmpty(data.Description)) sb.Append("\n\n<color=#6B4A2B>").Append(data.Description).Append("</color>");
            if (data.Tags.Count > 0) sb.Append("\n<size=18><color=#8A6A45>タグ：").Append(string.Join("・", data.Tags)).Append("</color></size>");
            detailBody.text = sb.ToString();
            detailNote.gameObject.SetActive(false);

            bool infinite = IngredientInventory.IsInfiniteKind(data);
            bool unlimited = inventory != null && inventory.IsUnlimited(data);
            float stock = inventory != null ? inventory.Get(data) : 0f;
            string unit = data.IsPoured ? "単位" : "";
            if (unlimited) detailStock.text = "<color=#2E7024>使い放題（取引中）</color>";
            else if (infinite) detailStock.text = "<color=#B0503A>まだ取引していない</color>";
            else detailStock.text = stock >= IngredientInventory.Epsilon
                ? $"在庫：{IngredientInventory.Format(data, stock)} {unit}　<size=18><color=#8A6A45>（希少：使った分だけ減る）</color></size>"
                : "<color=#B0503A>在庫なし</color>　<size=18><color=#8A6A45>（希少：使った分だけ減る）</color></size>";

            if (mode == Mode.Box)
            {
                takeButton.interactable = inventory != null && inventory.Has(data);
                return;
            }

            int gold = progress != null ? progress.Gold : 0;
            if (IsReputationLocked(data))
            {
                int done = progress != null ? progress.CompletedQuests : 0;
                quantityRow.SetActive(false);
                buyLabel.text = "まだ取引できない";
                buyButton.interactable = false;
                buyNote.text = $"<color=#B0503A>相談を {data.RequiredReputation} 件解決すると、取引してもらえる（いま {done} 件）</color>";
                return;
            }
            if (infinite)
            {
                quantityRow.SetActive(false);
                if (unlimited)
                {
                    buyLabel.text = "取引中";
                    buyButton.interactable = false;
                    buyNote.text = "<size=18>入手ルートは開拓ずみ。いつでも使い放題</size>";
                    return;
                }
                int unlockCost = UnlockCost(data);
                buyLabel.text = $"開拓する（{unlockCost} G）";
                bool canOpen = gold >= unlockCost;
                buyButton.interactable = canOpen;
                buyNote.text = canOpen
                    ? "<size=18>一度開拓すれば、ずっと使い放題になる</size>"
                    : $"<color=#B0503A>お金が足りません（あと {unlockCost - gold} G）</color>";
                return;
            }

            quantityRow.SetActive(true);
            int cost = data.BuyPrice * quantity;
            float amount = quantity * data.AmountPerUse;
            quantityText.text = data.IsPoured ? $"×{quantity}　<size=20>（{amount:0.##} 単位）</size>" : "×" + quantity;
            buyLabel.text = $"取り寄せる（{cost} G）";
            bool affordable = gold >= cost;
            buyButton.interactable = affordable;
            string per = data.IsPoured ? $"1つ = {data.AmountPerUse:0.##} 単位" : "1つ = 1回分";
            buyNote.text = affordable
                ? $"<size=18>{per}　{data.BuyPrice} G ずつ</size>"
                : $"<color=#B0503A>お金が足りません（あと {cost - gold} G）</color>";
        }

        // 素材の説明（3D 版では棚の素材 IngredientSource が持っていた）
        private static string DescribeAttributes(IngredientData data, AlchemySettings settings)
        {
            var sb = new StringBuilder(CategoryLabel(data.Category));
            foreach (AttributeValue av in data.Attributes)
            {
                string attributeName = settings != null ? settings.GetDisplayName(av.attribute) : av.attribute.ToString();
                sb.Append("　").Append(attributeName).Append(av.value > 0 ? "+" : "").Append(av.value.ToString("0.#"));
            }
            if (data.GlobalAmplifyPercent != 0f)
            {
                sb.Append("　全属性 ×").Append((1f + data.GlobalAmplifyPercent / 100f).ToString("0.##"));
            }
            foreach (AttributeAmplifier amp in data.Amplifiers)
            {
                string attributeName = settings != null ? settings.GetDisplayName(amp.attribute) : amp.attribute.ToString();
                sb.Append("　").Append(attributeName).Append(" ×").Append((1f + amp.percent / 100f).ToString("0.##"));
            }
            return sb.ToString();
        }

        private static string CategoryLabel(IngredientCategory category)
        {
            switch (category)
            {
                case IngredientCategory.BaseLiquid: return "ベース液体";
                case IngredientCategory.MainMaterial: return "主素材";
                case IngredientCategory.Catalyst: return "触媒";
                default: return category.ToString();
            }
        }

        private static string FormLabel(IngredientForm form)
        {
            switch (form)
            {
                case IngredientForm.Liquid: return "液体";
                case IngredientForm.Powder: return "粉";
                default: return "固体";
            }
        }

        public static string KindLabel(LiquidKind kind)
        {
            switch (kind)
            {
                case LiquidKind.Potion: return "ポーション";
                case LiquidKind.Intermediate: return "中間液体";
                default: return "未知の液体";
            }
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        private Cell CreateCell(int index)
        {
            Image frame = UIFactory.CreateImage(gridContent, "Cell" + index, slotSprite, Color.white);
            frame.raycastTarget = true;
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            int captured = index;
            button.onClick.AddListener(() =>
            {
                if (captured == selected && Time.unscaledTime - lastClickTime < 0.35f) PrimaryAction(); // ダブルクリック
                lastClickTime = Time.unscaledTime;
                Select(captured);
            });

            Image icon = UIFactory.CreateImage(frame.transform, "Icon", null, Color.white);
            icon.preserveAspect = true;
            UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(84f, 84f));

            Text count = UIFactory.Label(frame.transform, "Count", "", 22, UIColors.Cream, TextAnchor.LowerRight, FontStyle.Bold, true);
            UIFactory.Stretch(count.rectTransform, 6f, 10f, 6f, 6f);
            Text price = UIFactory.Label(frame.transform, "Price", "", 20, UIColors.Gold, TextAnchor.LowerLeft, FontStyle.Bold, true);
            UIFactory.Stretch(price.rectTransform, 10f, 6f, 6f, 6f);
            Text rarity = UIFactory.Label(frame.transform, "Rarity", "", 14, UIColors.Gold, TextAnchor.UpperLeft, FontStyle.Normal, true);
            UIFactory.Stretch(rarity.rectTransform, 10f, 6f, 6f, 6f);
            Text tag = UIFactory.Label(frame.transform, "Tag", "", 17, UIColors.Gold, TextAnchor.UpperRight, FontStyle.Bold, true);
            UIFactory.Stretch(tag.rectTransform, 6f, 8f, 4f, 6f);
            return new Cell { root = frame.gameObject, frame = frame, icon = icon, count = count, price = price, rarity = rarity, tag = tag };
        }

        private void BuildBanner(RectTransform parent)
        {
            Image banner = UIFactory.CreateImage(parent, "Banner", UIFactory.Parchment(), Color.white);
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(460f, 96f));
            bannerText = UIFactory.Label(banner.transform, "Title", "素材箱", 50, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(bannerText.rectTransform);

            // 所持金（取引先帳のときだけ）
            Image goldPanel = UIFactory.CreateImage(parent, "Gold", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            UIFactory.Place(goldPanel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -44f), new Vector2(240f, 68f));
            UIFactory.Horizontal(goldPanel.gameObject, new RectOffset(16, 22, 8, 8), 10f, TextAnchor.MiddleLeft);
            Image coin = UIFactory.CreateImage(goldPanel.transform, "Coin", UIWidgets.Coin(), Color.white);
            UIFactory.Size(coin.gameObject, 44f, 44f);
            goldText = UIFactory.Label(goldPanel.transform, "Text", "0 G", 34, UIColors.Gold, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            goldText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Size(goldText.gameObject, 150f, 50f);
        }

        private void BuildGrid(RectTransform parent)
        {
            float gridWidth = Columns * CellSize + (Columns - 1) * CellSpacing;
            const float frameHeight = 760f;
            Image frame = UIFactory.CreateImage(parent, "BoxFrame", UIFactory.WoodPanel(), Color.white);
            UIFactory.Place(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-250f, -40f),
                            new Vector2(gridWidth + Padding * 2f + 16f, frameHeight));

            // タブ
            float tabsWidth = 0f;
            foreach (float w in TabWidths) tabsWidth += w + 8f;
            RectTransform tabRow = UIFactory.CreateRect("Tabs", frame.transform);
            UIFactory.Place(tabRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -20f), new Vector2(tabsWidth, 52f));
            UIFactory.Horizontal(tabRow.gameObject, new RectOffset(0, 0, 0, 0), 8f);
            for (int i = 0; i < TabLabels.Length; i++)
            {
                int index = i;
                Button b = UIWidgets.WoodButton(tabRow, TabLabels[i], 22, new Vector2(TabWidths[i], 50f), () => SetTab(index));
                tabs.Add((Image)b.targetGraphic);
            }

            // 検索欄
            Image searchBack = UIFactory.CreateImage(frame.transform, "Search", UIFactory.RoundedRect(UIColors.Parchment, UIColors.Outline, 10, 3), Color.white);
            searchBack.raycastTarget = true;
            UIFactory.Place(searchBack.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Padding, -20f), new Vector2(gridWidth - tabsWidth - 8f, 52f));
            Text text = UIFactory.Label(searchBack.transform, "Text", "", 22, UIColors.Ink, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(text.rectTransform, 14f, 14f, 4f, 4f);
            Text placeholder = UIFactory.Label(searchBack.transform, "Placeholder", "名前・タグで探す", 20, new Color(0.45f, 0.3f, 0.17f, 0.7f), TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.Stretch(placeholder.rectTransform, 14f, 14f, 4f, 4f);
            search = searchBack.gameObject.AddComponent<InputField>();
            search.textComponent = text;
            search.placeholder = placeholder;
            search.targetGraphic = searchBack;
            search.lineType = InputField.LineType.SingleLine;
            search.navigation = new Navigation { mode = Navigation.Mode.None };
            search.onValueChanged.AddListener(v =>
            {
                filter = (v ?? "").Trim();
                RebuildList(true);
            });

            // 一覧（スクロール）
            RectTransform viewport = UIFactory.CreateRect("Viewport", frame.transform);
            UIFactory.Stretch(viewport, Padding, Padding + 16f, TopArea, Padding + 34f);
            Image viewportHit = viewport.gameObject.AddComponent<Image>();
            viewportHit.color = new Color(0f, 0f, 0f, 0f);
            viewportHit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            gridContent = UIFactory.CreateRect("Content", viewport);
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.anchoredPosition = Vector2.zero;
            gridContent.sizeDelta = Vector2.zero;
            var layout = gridContent.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CellSize, CellSize);
            layout.spacing = new Vector2(CellSpacing, CellSpacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;
            UIFactory.FitContent(gridContent.gameObject, false, true);

            scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = gridContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = false;

            // スクロールバー
            Image barBack = UIFactory.CreateImage(frame.transform, "Scrollbar", UIFactory.RoundedRect(new Color(0.1f, 0.07f, 0.05f, 0.8f), new Color(0f, 0f, 0f, 0f), 5, 0), Color.white);
            barBack.raycastTarget = true;
            RectTransform barRt = barBack.rectTransform;
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-Padding + 4f - 10f, Padding + 34f);
            barRt.offsetMax = new Vector2(-Padding + 4f, -TopArea);
            RectTransform slide = UIFactory.CreateRect("Slide", barRt);
            UIFactory.Stretch(slide, 1f, 1f, 1f, 1f);
            Image handle = UIFactory.CreateImage(slide, "Handle", UIFactory.RoundedRect(UIColors.WoodLight, new Color(0f, 0f, 0f, 0f), 4, 0), Color.white);
            handle.raycastTarget = true;
            UIFactory.Stretch(handle.rectTransform);
            var bar = barBack.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            emptyText = UIFactory.Label(frame.transform, "Empty", "", 26, UIColors.CreamDim, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            UIFactory.Stretch(emptyText.rectTransform, Padding, Padding, TopArea + 40f, Padding + 60f);

            countText = UIFactory.Label(frame.transform, "Count", "", 22, UIColors.CreamDim, TextAnchor.MiddleRight, FontStyle.Normal, true);
            UIFactory.Place(countText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Padding, 10f), new Vector2(520f, 30f));
        }

        private void BuildDetail(RectTransform parent)
        {
            Image paper = UIFactory.CreateImage(parent, "Detail", UIFactory.Parchment(), Color.white);
            UIFactory.Place(paper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(470f, -40f), new Vector2(460f, 760f));

            detailEmpty = UIFactory.Label(paper.transform, "Empty", "選ぶと、ここにくわしく表示されます。", 24, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(detailEmpty.rectTransform, 40f, 40f, 40f, 40f);

            RectTransform content = UIFactory.CreateRect("Content", paper.transform);
            UIFactory.Stretch(content);
            UIFactory.Vertical(content.gameObject, new RectOffset(38, 38, 34, 30), 8f, TextAnchor.UpperCenter).childForceExpandWidth = true;
            detailContent = content.gameObject;

            detailIcon = UIFactory.CreateImage(content, "Icon", null, Color.white);
            detailIcon.preserveAspect = true;
            UIFactory.Size(detailIcon.gameObject, 140f, 140f);
            detailName = UIFactory.Label(content, "Name", "", 32, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            detailMeta = UIFactory.Label(content, "Meta", "", 22, UIColors.InkLight, TextAnchor.MiddleCenter);

            Image divider = UIFactory.CreateImage(content, "Divider", null, new Color(0.45f, 0.3f, 0.17f, 0.6f));
            UIFactory.Size(divider.gameObject, -1f, 3f);

            detailBody = UIFactory.Label(content, "Body", "", 22, UIColors.Ink, TextAnchor.UpperLeft);
            UIFactory.Size(detailBody.gameObject, -1f, 190f);
            detailStock = UIFactory.Label(content, "Stock", "", 24, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            detailNote = UIFactory.Label(content, "Note", "", 22, Green, TextAnchor.MiddleCenter, FontStyle.Bold);

            // 素材箱：手に持つ
            RectTransform takeRow = UIFactory.CreateRect("TakeRow", content);
            UIFactory.Horizontal(takeRow.gameObject, new RectOffset(0, 0, 6, 0), 0f, TextAnchor.MiddleCenter);
            takeButton = UIWidgets.WoodButton(takeRow, "手に持つ（E）", 28, new Vector2(300f, 66f), PrimaryAction);

            // 取引先帳：数（取り寄せのときだけ）と、開拓する／取り寄せるボタン
            RectTransform buy = UIFactory.CreateRect("BuyArea", content);
            UIFactory.Vertical(buy.gameObject, new RectOffset(0, 0, 0, 0), 8f, TextAnchor.UpperCenter).childForceExpandWidth = true;
            buyArea = buy.gameObject;
            RectTransform qtyRow = UIFactory.CreateRect("Quantity", buy);
            UIFactory.Horizontal(qtyRow.gameObject, new RectOffset(0, 0, 0, 0), 14f, TextAnchor.MiddleCenter);
            quantityRow = qtyRow.gameObject;
            UIWidgets.WoodButton(qtyRow, "－", 30, new Vector2(64f, 56f), () => ChangeQuantity(-1));
            quantityText = UIFactory.Label(qtyRow, "Value", "×1", 30, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            quantityText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Size(quantityText.gameObject, 190f, 56f);
            UIWidgets.WoodButton(qtyRow, "＋", 30, new Vector2(64f, 56f), () => ChangeQuantity(1));
            RectTransform buyRow = UIFactory.CreateRect("BuyRow", buy);
            UIFactory.Horizontal(buyRow.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleCenter);
            buyButton = UIWidgets.WoodButton(buyRow, "開拓する", 28, new Vector2(320f, 66f), Buy);
            buyLabel = buyButton.GetComponentInChildren<Text>();
            buyNote = UIFactory.Label(buy, "Note", "", 20, UIColors.InkLight, TextAnchor.MiddleCenter);
        }

        private void BuildCloseButton(RectTransform parent)
        {
            Image close = UIFactory.CreateImage(parent, "Close", UIFactory.WoodPanel(), Color.white);
            close.raycastTarget = true;
            UIFactory.Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -36f), new Vector2(84f, 84f));
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            button.onClick.AddListener(() => { if (onClose != null) onClose(); });
            close.gameObject.AddComponent<UIButtonFx>();
            Text x = UIFactory.Label(close.transform, "X", "✕", 44, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(x.rectTransform);
        }

        private void BuildFooter(RectTransform parent)
        {
            RectTransform footer = UIFactory.CreateRect("Footer", parent);
            UIFactory.Place(footer, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 26f), new Vector2(1300f, 50f));
            UIFactory.Horizontal(footer.gameObject, new RectOffset(0, 0, 0, 0), 10f, TextAnchor.MiddleRight);
            UIFactory.KeyCap(footer, "↑↓←→", 22);
            Label(footer, "選ぶ");
            footerTabs = UIFactory.KeyCap(footer, "1〜5", 22);
            Label(footer, "種類");
            UIFactory.KeyCap(footer, "E", 22);
            footerAction = Label(footer, "手に持つ");
            quantityKeys = UIFactory.KeyCap(footer, "Q／R", 22).transform.parent.gameObject;
            quantityKeysLabel = Label(footer, "数を変える").gameObject;
            UIFactory.KeyCap(footer, "Esc", 22);
            Label(footer, "閉じる");
        }

        private static Text Label(Transform parent, string text)
        {
            Text label = UIFactory.Label(parent, "Label", text, 24, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }
    }
}
