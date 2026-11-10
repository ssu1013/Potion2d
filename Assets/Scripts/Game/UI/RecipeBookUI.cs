using System;
using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 自分だけのレシピ本（図鑑）。これまでに瓶に詰めた液体（ポーション・中間液体・未知の液体＝失敗作も）が、
    /// 1つずつページになって記録される。見開きの左が目次、右がページ。
    ///   ・ページ：見た目（3D の瓶・色・特性からわかる様子）／属性（100ml あたりの濃さ）／特性と効果／
    ///            作り方（素材・量・温度・混ぜた向きと回数・順番。ちがう作り方を3つまで）／使った素材／系譜／記録
    ///   ・名前を付ける：自分で見つけた液体に好きな名前を付けられる（トレイの瓶の名前も変わる）
    /// 画面は切り替えず、調合机の上に本を広げるように出す（後ろは少しだけ暗く、ランプの火のゆらぎは見えたまま）。
    /// </summary>
    public sealed class RecipeBookUI
    {
        private static readonly string[] FilterLabels = { "すべて", "ポーション", "中間液体", "未知の液体" };
        private static readonly float[] FilterWidths = { 96f, 130f, 118f, 132f };
        private const float RowHeight = 84f;
        private const float RowSpacing = 6f;
        private const float PageWidth = 838f;
        private const float PageHeight = 872f;
        private const float AttributeScale = 80f;
        private const int MaxAttributeRows = 8;
        private const int MaxLinks = 6;

        private static readonly Color Muted = new Color(0.54f, 0.42f, 0.27f);
        private static readonly Color TabOff = new Color(0.62f, 0.55f, 0.48f, 1f);

        private struct Row
        {
            public GameObject root;
            public Image back;
            public Text number;
            public Image icon;
            public Text name;
            public Text meta;
            public Text tag;
        }

        private struct AttributeBar
        {
            public GameObject root;
            public Text name;
            public Image fill;
            public Text value;
        }

        private struct Link
        {
            public GameObject root;
            public Text label;
            public LiquidRecord target;
        }

        private readonly Func<AlchemyManager> alchemy;
        private readonly PotionInventory potions;
        private readonly Action onCloseButton;
        private readonly GameObject root;
        private readonly Image overlay;
        private readonly RectTransform panelRoot;
        private readonly LiquidPreview preview;
        private LiquidRegistry registry;

        private readonly List<LiquidRecord> items = new List<LiquidRecord>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Image> filterTabs = new List<Image>();
        private readonly List<AttributeBar> bars = new List<AttributeBar>();
        private readonly List<Link> parentLinks = new List<Link>();
        private readonly List<Link> childLinks = new List<Link>();
        private readonly List<Button> recipeTabs = new List<Button>();
        private readonly List<LiquidRecord> lineageBuffer = new List<LiquidRecord>();
        private readonly Sprite rowSprite;
        private readonly Sprite rowSelectedSprite;

        // 左のページ（目次）
        private Text titleText;
        private Text subtitleText;
        private InputField search;
        private ScrollRect listScroll;
        private RectTransform listContent;
        private Text listEmpty;

        // 右のページ
        private RectTransform detailViewport;
        private RectTransform detailScrollbar;
        private ScrollRect detailScroll;
        private GameObject detailContent;
        private Text detailEmpty;
        private RawImage previewImage;
        private Text kindText;
        private Text nameText;
        private Text autoNameText;
        private Text starsText;
        private GameObject renameButtonRow;
        private GameObject renameArea;
        private InputField renameField;
        private Image swatch;
        private Text appearanceText;
        private GameObject attributeEmpty;
        private Text traitsText;
        private GameObject recipeTabRow;
        private Text recipeText;
        private Text ingredientsText;
        private GameObject lineageHeading;
        private Text parentsLabel;
        private Text childrenLabel;
        private Text recordText;

        // 本の外の案内
        private Text footerText;

        private int filter;
        private string query = "";
        private int selected = -1;
        private LiquidRecord viewing;
        private int recipeIndex;
        private bool renaming;
        private bool focusRenameNextFrame;
        private bool dirty;
        private float appear;

        public bool IsOpen => root.activeSelf;
        public bool IsRenaming => renaming;
        /// <summary>名前・検索の欄に文字を打っている（キー操作を本に渡さない）</summary>
        public bool IsTyping => (renameField != null && renameField.isFocused) || (search != null && search.isFocused);
        public int VisibleCount => items.Count;
        public int CurrentFilter => filter;
        public int RecipeIndex => recipeIndex;
        public LiquidRecord SelectedRecord => selected >= 0 && selected < items.Count ? items[selected] : null;
        /// <summary>右のページに出している文（自動テストの記録用）</summary>
        public string DetailSummary => nameText.text + " ／ " + appearanceText.text + " ／ " + recipeText.text.Replace('\n', ' ');
        /// <summary>本が机の上に置かれきった（開くアニメーションが終わった）</summary>
        public bool FullyOpen => IsOpen && appear >= 1f;

        public RecipeBookUI(RectTransform canvasRoot, Func<AlchemyManager> alchemy, PotionInventory potions, Action onCloseButton)
        {
            this.alchemy = alchemy;
            this.potions = potions;
            this.onCloseButton = onCloseButton;
            rowSprite = UIFactory.RoundedRect(new Color(1f, 1f, 1f, 0.16f), new Color(0.45f, 0.3f, 0.17f, 0.28f), 10, 2);
            rowSelectedSprite = UIFactory.RoundedRect(new Color(1f, 0.86f, 0.45f, 0.62f), new Color(0.71f, 0.4f, 0.11f, 1f), 10, 3);
            preview = new LiquidPreview(512, new Color(0.93f, 0.86f, 0.7f));

            // 後ろの机は少しだけ暗くする（画面は切り替えない）
            overlay = UIFactory.CreateImage(canvasRoot, "RecipeBook", null, new Color(0.02f, 0.012f, 0.008f, MenuPanel.DeskDim));
            overlay.raycastTarget = true;
            UIFactory.Stretch(overlay.rectTransform);
            root = overlay.gameObject;
            panelRoot = UIFactory.CreateRect("Panel", overlay.rectTransform);
            UIFactory.Stretch(panelRoot);

            Image cover = UIFactory.CreateImage(panelRoot, "Cover", UIFactory.RoundedRect(new Color(0.4f, 0.15f, 0.11f), new Color(0.18f, 0.06f, 0.04f), 26, 5), Color.white);
            UIFactory.Place(cover.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(PageWidth * 2f + 64f, PageHeight + 40f));
            Image trim = UIFactory.CreateImage(cover.transform, "Trim", UIFactory.RoundedRect(new Color(0f, 0f, 0f, 0f), new Color(0.86f, 0.66f, 0.3f, 0.85f), 20, 2), Color.white);
            UIFactory.Stretch(trim.rectTransform, 9f, 9f, 9f, 9f);

            Image left = UIFactory.CreateImage(cover.transform, "LeftPage", UIFactory.Parchment(), UIFactory.Paper);
            UIFactory.Place(left.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(PageWidth, PageHeight));
            Image right = UIFactory.CreateImage(cover.transform, "RightPage", UIFactory.Parchment(), UIFactory.Paper);
            UIFactory.Place(right.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(PageWidth, PageHeight));
            Image spine = UIFactory.CreateImage(cover.transform, "Spine", UIWidgets.SoftDot(), new Color(0.25f, 0.13f, 0.05f, 0.45f));
            UIFactory.Place(spine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, PageHeight + 30f));

            BuildIndex(left.rectTransform);
            BuildPage(right.rectTransform);
            BuildCloseButton(cover.rectTransform);
            BuildFooter(panelRoot);

            root.SetActive(false);
            if (potions != null) potions.Changed += () => dirty = true;
        }

        // ------------------------------------------------------------------
        // 開く・閉じる
        // ------------------------------------------------------------------

        /// <summary>開く（調合机の上に本を広げる）</summary>
        public void Open()
        {
            EnsureRegistry();
            renaming = false;
            root.SetActive(true);
            appear = 0f;
            MenuPanel.Rise(panelRoot, Vector2.zero, ref appear, 0f);
            MenuPanel.Shade(overlay, 0f);
            search.text = "";
            query = "";
            RefreshHeader();
            RebuildList(true);
            preview.SetActive(true);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null)
            {
                AudioManager.Se(lib.uiOpen, 0.55f);
                AudioManager.Se(lib.page, 0.8f, 0.92f);
            }
        }

        /// <summary>閉じる（playSound が false なら、閉じる音を鳴らさない：ほかの画面に切り替わるとき）</summary>
        public void Close(bool playSound = true)
        {
            if (!root.activeSelf) return;
            if (search.isFocused) search.DeactivateInputField();
            if (renameField.isFocused) renameField.DeactivateInputField();
            renaming = false;
            MarkViewed();
            viewing = null;
            root.SetActive(false);
            preview.SetActive(false);
            preview.Clear();
            SoundLibrary lib = AudioManager.Lib;
            if (playSound && lib != null) AudioManager.Se(lib.uiClose, 0.7f);
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            MenuPanel.Rise(panelRoot, Vector2.zero, ref appear, dt);
            MenuPanel.Shade(overlay, appear);
            preview.Tick(dt);

            if (dirty)
            {
                dirty = false;
                RebuildList(false);
            }
            if (renaming && focusRenameNextFrame)
            {
                focusRenameNextFrame = false;
                renameField.ActivateInputField();
                renameField.Select();
                return;
            }
            if (!allowInput) return;
            if (renaming)
            {
                // 名前を打っている間は Enter で決める（Esc はウィンドウ全体の Esc で、やめる）
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ConfirmRename();
                return;
            }
            if (IsTyping) return;

            int move = 0;
            if (Input.GetKeyDown(KeyCode.DownArrow)) move = 1;
            if (Input.GetKeyDown(KeyCode.UpArrow)) move = -1;
            if (move != 0 && items.Count > 0)
            {
                int next = Mathf.Clamp(selected + move, 0, items.Count - 1);
                if (next != selected)
                {
                    Select(next);
                    EnsureVisible(next);
                }
            }
            // ページ送り：右のページ（長い作り方）を上下に送る
            if (Input.GetKeyDown(KeyCode.PageDown)) ScrollDetail(-1f);
            if (Input.GetKeyDown(KeyCode.PageUp)) ScrollDetail(1f);
            if (Input.GetKeyDown(KeyCode.RightArrow)) SetRecipeIndex(recipeIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SetRecipeIndex(recipeIndex - 1);
            for (int i = 0; i < FilterLabels.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetFilter(i);
            }
            if (Input.GetKeyDown(KeyCode.N)) BeginRename();
        }

        // ------------------------------------------------------------------
        // 操作（自動テストからも呼べる）
        // ------------------------------------------------------------------

        /// <summary>液体を ID で選ぶ（目次の絞り込みで隠れていたら「すべて」に戻して探す）。見つからなければ false</summary>
        public bool SelectById(string id)
        {
            int index = IndexOf(id);
            if (index < 0 && (filter != 0 || query.Length > 0))
            {
                filter = 0;
                search.text = "";
                query = "";
                RebuildList(true);
                index = IndexOf(id);
            }
            if (index < 0) return false;
            Select(index);
            EnsureVisible(index);
            return true;
        }

        public void SetFilter(int index)
        {
            index = Mathf.Clamp(index, 0, FilterLabels.Length - 1);
            if (index == filter) return;
            filter = index;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.5f, 1.1f);
            RebuildList(true);
        }

        public void SetSearch(string text)
        {
            search.text = text ?? "";
        }

        /// <summary>右のページを、見えている高さの 8 割ずつ送る（direction：-1 で下へ、+1 で上へ）</summary>
        public void ScrollDetail(float direction)
        {
            if (detailScroll == null || detailScroll.content == null) return;
            float overflow = detailScroll.content.rect.height - detailViewport.rect.height;
            if (overflow <= 1f) return;
            float step = detailViewport.rect.height * 0.8f / overflow;
            detailScroll.verticalNormalizedPosition = Mathf.Clamp01(detailScroll.verticalNormalizedPosition + direction * step);
        }

        /// <summary>同じ液体のちがう作り方（最大3つ）を切り替える</summary>
        public void SetRecipeIndex(int index)
        {
            LiquidRecord r = SelectedRecord;
            int count = r != null && r.recipes != null ? r.recipes.Count : 0;
            if (count <= 1) return;
            index = Mathf.Clamp(index, 0, count - 1);
            if (index == recipeIndex) return;
            recipeIndex = index;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.4f, 1.2f);
            RefreshRecipe(r);
        }

        /// <summary>名前を付けはじめる（名前の欄に、今の名前を入れて選んだ状態にする）</summary>
        public void BeginRename()
        {
            LiquidRecord r = SelectedRecord;
            if (r == null) return;
            renaming = true;
            renameField.text = r.DisplayName;
            ShowRenameUI(true);
            // N キーを押したフレームに欄を選ぶと、その「n」が欄に打たれてしまうので、次のフレームで選ぶ
            focusRenameNextFrame = true;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiHover, 0.6f);
        }

        /// <summary>名前を決める（name を省くと、欄に打った名前）。空にすると、もとの名前に戻る</summary>
        public void ConfirmRename(string name = null)
        {
            LiquidRecord r = SelectedRecord;
            if (r == null || registry == null) return;
            string before = r.DisplayName;
            string value = name ?? renameField.text;
            renaming = false;
            if (renameField.isFocused) renameField.DeactivateInputField();
            ShowRenameUI(false);
            registry.Rename(r.id, value);
            RefreshDetail();
            SoundLibrary lib = AudioManager.Lib;
            AtelierHUD hud = AtelierHUD.Instance;
            if (r.DisplayName != before)
            {
                AudioManager.Se(lib != null && lib.starChime != null ? lib.starChime : SfxSynth.Chime, 0.4f, 1.26f);
                if (hud != null)
                {
                    hud.ShowToast(string.IsNullOrEmpty(r.customName)
                        ? $"もとの名前「{r.autoName}」に戻した"
                        : $"「{before}」を「{r.DisplayName}」と名付けた");
                }
            }
        }

        /// <summary>名前を付けるのをやめる（名前は変えない）</summary>
        public void CancelRename()
        {
            if (!renaming) return;
            renaming = false;
            if (renameField.isFocused) renameField.DeactivateInputField();
            ShowRenameUI(false);
        }

        // ------------------------------------------------------------------
        // 目次
        // ------------------------------------------------------------------

        private void EnsureRegistry()
        {
            if (registry != null) return;
            AlchemyManager a = alchemy != null ? alchemy() : null;
            if (a == null) return;
            registry = a.Liquids;
            registry.Changed += _ => dirty = true;
            registry.Discovered += _ => dirty = true;
        }

        private AlchemySettings Settings
        {
            get
            {
                AlchemyManager a = alchemy != null ? alchemy() : null;
                return a != null ? a.Settings : null;
            }
        }

        private int IndexOf(string id)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].id == id) return i;
            }
            return -1;
        }

        private PotionStack StockOf(LiquidRecord r)
        {
            return potions != null && r != null ? potions.Find(r.id) : null;
        }

        private bool Matches(LiquidRecord r)
        {
            if (r == null) return false;
            if (filter == 1 && r.kind != LiquidKind.Potion) return false;
            if (filter == 2 && r.kind != LiquidKind.Intermediate) return false;
            if (filter == 3 && r.kind != LiquidKind.Unknown) return false;
            if (query.Length == 0) return true;
            if (Contains(r.DisplayName, query) || Contains(r.autoName, query) || Contains(KindLabel(r.kind), query)) return true;
            foreach (string effect in r.effects)
            {
                if (Contains(effect, query)) return true;
            }
            IngredientDatabase db = GameContext.Ingredients;
            if (db != null)
            {
                foreach (string id in r.ingredientIds)
                {
                    IngredientData d = db.Get(id);
                    if (d != null && Contains(d.DisplayName, query)) return true;
                }
            }
            return false;
        }

        private static bool Contains(string text, string part)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildList(bool resetSelection)
        {
            LiquidRecord keep = resetSelection ? null : SelectedRecord;
            items.Clear();
            if (registry != null)
            {
                // 新しく記録したページが上（No. は記録した順）
                IReadOnlyList<LiquidRecord> all = registry.All;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (Matches(all[i])) items.Add(all[i]);
                }
            }

            while (rows.Count < items.Count) rows.Add(CreateRow(rows.Count));
            for (int i = 0; i < rows.Count; i++)
            {
                bool active = i < items.Count;
                if (rows[i].root.activeSelf != active) rows[i].root.SetActive(active);
                if (active) FillRow(rows[i], items[i]);
            }

            int index = keep != null ? items.IndexOf(keep) : -1;
            int next = index >= 0 ? index : items.Count > 0 ? Mathf.Clamp(resetSelection ? 0 : selected, 0, items.Count - 1) : -1;
            if (resetSelection && listScroll != null) listScroll.verticalNormalizedPosition = 1f;
            bool changed = next != selected || SelectedRecordAt(next) != viewing;
            selected = next;
            if (changed) OnSelectionChanged();

            for (int i = 0; i < filterTabs.Count; i++) filterTabs[i].color = i == filter ? Color.white : TabOff;
            RefreshHeader();
            listEmpty.gameObject.SetActive(items.Count == 0);
            listEmpty.text = EmptyMessage();
            RefreshRowFrames();
            RefreshDetail();
        }

        private LiquidRecord SelectedRecordAt(int index)
        {
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        private string EmptyMessage()
        {
            if (query.Length > 0 || (filter != 0 && registry != null && registry.Count > 0)) return "見つかりませんでした。";
            return "まだ何も記録されていません。\n\nフラスコで調合して瓶に詰めると、\nその液体がここに記録される。\n（失敗した液体も、大事な探求の記録）";
        }

        private void RefreshHeader()
        {
            int total = registry != null ? registry.Count : 0;
            titleText.text = "<color=#B5651D>◆</color> レシピ本";
            int potionCount = 0, intermediate = 0, unknown = 0;
            if (registry != null)
            {
                foreach (LiquidRecord r in registry.All)
                {
                    if (r.kind == LiquidKind.Potion) potionCount++;
                    else if (r.kind == LiquidKind.Intermediate) intermediate++;
                    else unknown++;
                }
            }
            subtitleText.text = total > 0
                ? $"これまでに記録した液体 {total} 種類（ポーション {potionCount}・中間液体 {intermediate}・未知の液体 {unknown}）"
                : "これまでに記録した液体 0 種類";
            footerText.text = "↑↓ ページを選ぶ　　1〜4 種類　　←→ 作り方を切り替える　　PageUp／PageDown 送る　　N 名前を付ける　　Esc 閉じる";
        }

        private void FillRow(Row row, LiquidRecord r)
        {
            row.number.text = "No." + (registry != null ? registry.NumberOf(r) : 0);
            row.icon.sprite = UIFactory.PotionIcon(r.color);
            row.name.text = r.DisplayName;
            var sb = new StringBuilder();
            sb.Append(KindLabel(r.kind));
            if (r.IsSpecial) sb.Append("（特別）");
            if (r.stars > 0) sb.Append("　<color=#B5781C>").Append(BrewResult.StarText(r.stars)).Append("</color>");
            sb.Append("　作った回数 ").Append(Mathf.Max(1, r.madeCount));
            row.meta.text = sb.ToString();

            PotionStack stock = StockOf(r);
            if (r.unread) SetTag(row, "NEW", new Color(0.78f, 0.45f, 0.05f));
            else if (stock != null && stock.ml >= PotionStack.EmptyMl) SetTag(row, $"瓶 {stock.ml:0}ml", UIColors.InkLight);
            else SetTag(row, "", UIColors.InkLight);
        }

        private static void SetTag(Row row, string text, Color color)
        {
            row.tag.text = text;
            row.tag.color = color;
        }

        private void Select(int index)
        {
            if (index == selected) return;
            selected = index;
            OnSelectionChanged();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.35f, UnityEngine.Random.Range(1.05f, 1.2f));
            RefreshRowFrames();
            RefreshDetail();
        }

        /// <summary>選んでいるページが変わったとき：見ていたページを「見た」ことにする（NEW の印は、見ている間は残す）</summary>
        private void OnSelectionChanged()
        {
            LiquidRecord next = SelectedRecord;
            if (next == viewing) return;
            if (renaming) CancelRename();
            MarkViewed();
            viewing = next;
            recipeIndex = 0;
            if (detailScroll != null) detailScroll.verticalNormalizedPosition = 1f;
        }

        private void MarkViewed()
        {
            if (viewing == null || registry == null || !viewing.unread) return;
            registry.MarkRead(viewing.id);
        }

        private void RefreshRowFrames()
        {
            for (int i = 0; i < items.Count && i < rows.Count; i++)
            {
                Sprite want = i == selected ? rowSelectedSprite : rowSprite;
                if (rows[i].back.sprite != want) rows[i].back.sprite = want;
            }
        }

        private void EnsureVisible(int index)
        {
            if (listScroll == null) return;
            RectTransform viewport = listScroll.viewport;
            float top = index * (RowHeight + RowSpacing);
            float bottom = top + RowHeight;
            float viewHeight = viewport.rect.height;
            Vector2 pos = listContent.anchoredPosition;
            if (top < pos.y) pos.y = top;
            else if (bottom > pos.y + viewHeight) pos.y = bottom - viewHeight;
            listContent.anchoredPosition = pos;
        }

        // ------------------------------------------------------------------
        // ページ
        // ------------------------------------------------------------------

        private void RefreshDetail()
        {
            LiquidRecord r = SelectedRecord;
            bool any = r != null;
            detailContent.SetActive(any);
            detailEmpty.gameObject.SetActive(!any);
            if (!any)
            {
                preview.Clear();
                return;
            }
            AlchemySettings settings = Settings;

            // 見た目
            preview.Show(r);
            previewImage.texture = preview.Texture;
            int number = registry != null ? registry.NumberOf(r) : 0;
            kindText.text = $"No.{number}　{KindLabel(r.kind)}{(r.IsSpecial ? "・特別なポーション" : "")}";
            nameText.text = r.DisplayName;
            bool custom = !string.IsNullOrEmpty(r.customName);
            autoNameText.gameObject.SetActive(custom);
            if (custom) autoNameText.text = $"もとの名前：{r.autoName}";
            starsText.gameObject.SetActive(r.stars > 0);
            if (r.stars > 0) starsText.text = BrewResult.StarText(r.stars);
            renameButtonRow.SetActive(!renaming);
            swatch.color = new Color(r.color.r, r.color.g, r.color.b, 1f);
            appearanceText.text = DescribeLook(r, settings);

            // 属性（100ml あたり。強い順）
            AttributeVector v = r.Attributes;
            int shown = 0;
            foreach (PotionAttribute a in v.GetRankedAttributes())
            {
                if (v[a] < 0.5f || shown >= MaxAttributeRows) continue;
                AttributeBar bar = bars[shown];
                bar.root.SetActive(true);
                bar.name.text = settings != null ? settings.GetDisplayName(a) : a.ToString();
                Color c = settings != null ? settings.GetInfo(a).color : Color.gray;
                bar.fill.color = new Color(c.r, c.g, c.b, 1f);
                bar.fill.fillAmount = Mathf.Clamp01(v[a] / AttributeScale);
                bar.value.text = v[a].ToString("0.#");
                shown++;
            }
            for (int i = shown; i < bars.Count; i++) bars[i].root.SetActive(false);
            attributeEmpty.SetActive(shown == 0);

            // 特性・効果
            var sb = new StringBuilder();
            bool first = true;
            if (settings != null)
            {
                foreach (LiquidTrait t in r.traits)
                {
                    if (t.strength < 0.1f) continue;
                    sb.Append(first ? "特性：" : "・").Append(settings.GetTrait(t.id).displayName)
                      .Append("<size=18><color=#8A6A45>（").Append(StrengthWord(t.strength)).Append("）</color></size>");
                    first = false;
                }
            }
            if (first) sb.Append("<color=#8A6A45>特性：なし</color>");
            foreach (string effect in r.effects) sb.Append("\n・").Append(effect);
            if (!string.IsNullOrEmpty(r.description) && (r.IsSpecial || r.effects.Count == 0)) sb.Append("\n<color=#6B4A2B>").Append(r.description).Append("</color>");
            traitsText.text = sb.ToString();

            RefreshRecipe(r);

            // 使った素材
            sb.Length = 0;
            IngredientDatabase db = GameContext.Ingredients;
            for (int i = 0; i < r.ingredientIds.Count; i++)
            {
                IngredientData d = db != null ? db.Get(r.ingredientIds[i]) : null;
                if (i > 0) sb.Append("・");
                sb.Append(d != null ? d.DisplayName : r.ingredientIds[i]);
            }
            ingredientsText.text = sb.Length > 0 ? sb.ToString() : "<color=#8A6A45>（記録なし）</color>";

            // 系譜：元にした液体と、ここから生まれた液体
            lineageBuffer.Clear();
            if (registry != null)
            {
                foreach (string id in r.parentLiquidIds)
                {
                    LiquidRecord p = registry.Get(id);
                    if (p != null && p != r) lineageBuffer.Add(p);
                }
            }
            int parents = FillLinks(parentLinks, lineageBuffer);
            parentsLabel.gameObject.SetActive(parents > 0);
            if (registry != null) registry.CollectChildren(r.id, lineageBuffer);
            lineageBuffer.Remove(r);
            int children = FillLinks(childLinks, lineageBuffer);
            childrenLabel.gameObject.SetActive(children > 0);
            lineageHeading.SetActive(parents > 0 || children > 0);
            if (children > MaxLinks) childrenLabel.text = $"ここから生まれた液体（{children} 種類。新しい{MaxLinks}つ）";
            else childrenLabel.text = "ここから生まれた液体";

            // 記録
            sb.Length = 0;
            int ways = r.recipes != null ? r.recipes.Count : 0;
            sb.Append("作った回数：").Append(Mathf.Max(1, r.madeCount)).Append(" 回");
            if (ways > 0) sb.Append("　／　作り方：").Append(ways).Append(" 通り");
            PotionStack stock = StockOf(r);
            sb.Append("\nトレイの瓶：").Append(stock != null && stock.ml >= PotionStack.EmptyMl ? stock.DescribeAmount() : "なし");
            recordText.text = sb.ToString();
        }

        private void RefreshRecipe(LiquidRecord r)
        {
            int count = r.recipes != null ? r.recipes.Count : 0;
            recipeIndex = Mathf.Clamp(recipeIndex, 0, Mathf.Max(0, count - 1));
            recipeTabRow.SetActive(count > 1);
            for (int i = 0; i < recipeTabs.Count; i++)
            {
                bool active = i < count;
                recipeTabs[i].gameObject.SetActive(active);
                if (active) ((Image)recipeTabs[i].targetGraphic).color = i == recipeIndex ? Color.white : TabOff;
            }
            if (!r.HasRecipe)
            {
                recipeText.text = "<color=#8A6A45>（作り方の記録が残っていない。前の版で作った瓶など）</color>";
                return;
            }

            List<ProcessStep> steps = r.recipes[recipeIndex].steps;
            var sb = new StringBuilder();
            float maxTemp = 0f, lastTime = 0f;
            int number = 0;
            foreach (ProcessStep step in steps)
            {
                maxTemp = Mathf.Max(maxTemp, Mathf.Max(step.temperature, step.endTemperature));
                lastTime = Mathf.Max(lastTime, step.time + (step.type == ProcessStepType.Wait ? step.amount : 0f));
                if (sb.Length > 0) sb.Append('\n');
                if (step.type == ProcessStepType.Reaction)
                {
                    sb.Append("<color=#7A3E9A>　◇ ").Append(StepText(step)).Append("</color>");
                    continue;
                }
                number++;
                sb.Append("<b>").Append(number).Append(".</b> ").Append(StepText(step));
            }
            sb.Insert(0, $"<size=19><color=#8A6A45>全 {number} 工程・およそ {ProcessStep.FormatSeconds(lastTime)}・いちばん熱いとき {Mathf.RoundToInt(maxTemp)}℃</color></size>\n");
            recipeText.text = sb.ToString();
        }

        /// <summary>工程の説明。瓶詰めした液体を注いだ工程は、その液体の今の名前で書く（あとで名前を付けても読めるように）</summary>
        private string StepText(ProcessStep step)
        {
            if (step.type == ProcessStepType.PourLiquid && registry != null)
            {
                LiquidRecord source = registry.Get(step.sourceId);
                if (source != null && source.DisplayName != step.sourceName)
                {
                    ProcessStep copy = step.Clone();
                    copy.sourceName = source.DisplayName;
                    return copy.Describe();
                }
            }
            return step.Describe();
        }

        private int FillLinks(List<Link> links, List<LiquidRecord> targets)
        {
            for (int i = 0; i < links.Count; i++)
            {
                // 新しい物を先に（系譜の「ここから生まれた」は、新しく作った物ほど知りたい）
                LiquidRecord t = i < targets.Count ? targets[targets.Count - 1 - i] : null;
                Link link = links[i];
                link.target = t;
                links[i] = link;
                link.root.SetActive(t != null);
                if (t == null) continue;
                link.label.text = $"→ {t.DisplayName}　<size=17><color=#8A6A45>{KindLabel(t.kind)}</color></size>";
            }
            return targets.Count;
        }

        private void FollowLink(List<Link> links, int index)
        {
            if (index < 0 || index >= links.Count || links[index].target == null) return;
            if (!SelectById(links[index].target.id))
            {
                AtelierHUD hud = AtelierHUD.Instance;
                if (hud != null) hud.ShowToast("そのページは、今の一覧には出ていない");
            }
        }

        private void ShowRenameUI(bool on)
        {
            renameArea.SetActive(on);
            nameText.gameObject.SetActive(!on);
            renameButtonRow.SetActive(!on);
        }

        // ------------------------------------------------------------------
        // 見た目の言葉
        // ------------------------------------------------------------------

        /// <summary>「淡い緑色。きらきら光っている」のような見た目の説明</summary>
        /// <summary>液体の分類の表示名</summary>
        private static string KindLabel(LiquidKind kind)
        {
            switch (kind)
            {
                case LiquidKind.Potion: return "ポーション";
                case LiquidKind.Intermediate: return "中間液体";
                default: return "未知の液体";
            }
        }

        private static string DescribeLook(LiquidRecord r, AlchemySettings settings)
        {
            // 濁り・焦げがなければ「澄んだ」。色の言葉のあとに、特性からわかる様子を足す
            bool clear = r.TraitStrength(LiquidTraits.Cloudy) < 0.25f && r.TraitStrength(LiquidTraits.Scorched) < 0.25f;
            var sb = new StringBuilder();
            string color = ColorWords(r.color);
            if (color == "ほとんど無色") sb.Append(clear ? "ほとんど無色の、透きとおった液体" : "ほとんど無色の液体");
            else sb.Append(clear ? "澄んだ" : "").Append(color).Append("の液体");
            foreach (LiquidTrait t in r.traits)
            {
                if (t.strength < 0.25f) continue;
                string look = TraitLook(t.id);
                if (look != null) sb.Append("。").Append(look);
            }
            if (r.kind == LiquidKind.Unknown) sb.Append("。<color=#8A6A45>まだ正体のわからない液体</color>");
            return sb.ToString();
        }

        private static string TraitLook(string id)
        {
            switch (id)
            {
                case LiquidTraits.Foamy: return "細かい泡が立っている";
                case LiquidTraits.Cloudy: return "少し濁っている";
                case LiquidTraits.Shimmer: return "きらきらと光っている";
                case LiquidTraits.Syrupy: return "とろりとしている";
                case LiquidTraits.Scorched: return "焦げたにおいがする";
                case LiquidTraits.Concentrated: return "煮詰まって色が濃い";
                default: return null;
            }
        }

        public static string ColorWords(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.12f)
            {
                if (v > 0.85f) return "ほとんど無色";
                if (v > 0.6f) return "白っぽい色";
                if (v > 0.3f) return "灰色";
                return "黒っぽい色";
            }
            float deg = h * 360f;
            string hue;
            if (deg < 12f || deg >= 340f) hue = "赤";
            else if (deg < 40f) hue = "橙";
            else if (deg < 65f) hue = "黄";
            else if (deg < 95f) hue = "黄緑";
            else if (deg < 150f) hue = "緑";
            else if (deg < 185f) hue = "青緑";
            else if (deg < 210f) hue = "水";
            else if (deg < 250f) hue = "青";
            else if (deg < 290f) hue = "紫";
            else hue = v > 0.75f && s < 0.6f ? "桃" : "赤紫";

            string tone = "";
            if (v < 0.35f) tone = "暗い";
            else if (v < 0.55f) tone = "深い";
            else if (s < 0.35f) tone = "淡い";
            else if (s > 0.75f && v > 0.75f) tone = "鮮やかな";
            return tone + hue + "色";
        }

        private static string StrengthWord(float strength)
        {
            if (strength >= 0.75f) return "とても強い";
            if (strength >= 0.45f) return "強い";
            if (strength >= 0.25f) return "ふつう";
            return "少し";
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        private void BuildIndex(RectTransform page)
        {
            titleText = UIFactory.Label(page, "Title", "レシピ本", 42, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(46f, -28f), new Vector2(PageWidth - 92f, 56f));
            subtitleText = UIFactory.Label(page, "Subtitle", "", 21, UIColors.InkLight, TextAnchor.UpperLeft);
            UIFactory.Place(subtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -88f), new Vector2(PageWidth - 96f, 58f));

            // 種類のタブ
            RectTransform tabRow = UIFactory.CreateRect("Tabs", page);
            UIFactory.Place(tabRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -148f), new Vector2(520f, 48f));
            UIFactory.Horizontal(tabRow.gameObject, new RectOffset(0, 0, 0, 0), 6f);
            for (int i = 0; i < FilterLabels.Length; i++)
            {
                int index = i;
                Button b = UIWidgets.WoodButton(tabRow, FilterLabels[i], 21, new Vector2(FilterWidths[i], 46f), () => SetFilter(index));
                filterTabs.Add((Image)b.targetGraphic);
            }

            // 検索欄
            Image searchBack = UIFactory.CreateImage(page, "Search", UIFactory.RoundedRect(new Color(1f, 0.97f, 0.88f), UIColors.Outline, 10, 3), Color.white);
            searchBack.raycastTarget = true;
            UIFactory.Place(searchBack.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -148f), new Vector2(224f, 48f));
            search = CreateInput(searchBack, "名前・素材で探す", 21, 0);
            search.onValueChanged.AddListener(value =>
            {
                query = (value ?? "").Trim();
                RebuildList(true);
            });

            // 一覧
            RectTransform viewport = UIFactory.CreateRect("Viewport", page);
            UIFactory.Stretch(viewport, 36f, 50f, 212f, 36f);
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            listContent = UIFactory.CreateRect("Content", viewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = Vector2.zero;
            UIFactory.Vertical(listContent.gameObject, new RectOffset(0, 0, 0, 0), RowSpacing).childForceExpandWidth = true;
            UIFactory.FitContent(listContent.gameObject, false, true);
            listScroll = CreateScroll(page, viewport, listContent, 36f, 212f, 50f);

            listEmpty = UIFactory.Label(page, "Empty", "", 25, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(listEmpty.rectTransform, 50f, 50f, 260f, 80f);
        }

        private Row CreateRow(int index)
        {
            Image back = UIFactory.CreateImage(listContent, "Row" + index, rowSprite, Color.white);
            back.raycastTarget = true;
            UIFactory.Size(back.gameObject, -1f, RowHeight);
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.96f, 0.86f);
            colors.pressedColor = new Color(0.9f, 0.84f, 0.72f);
            button.colors = colors;
            int captured = index;
            button.onClick.AddListener(() => Select(captured));
            UIFactory.Horizontal(back.gameObject, new RectOffset(10, 16, 8, 8), 12f, TextAnchor.MiddleLeft);

            Text number = UIFactory.Label(back.transform, "No", "", 18, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Size(number.gameObject, 72f, 30f);
            Image icon = UIFactory.CreateImage(back.transform, "Icon", null, Color.white);
            icon.preserveAspect = true;
            UIFactory.Size(icon.gameObject, 62f, 62f);

            RectTransform column = UIFactory.CreateRect("Text", back.transform);
            UIFactory.Vertical(column.gameObject, new RectOffset(0, 0, 0, 0), 2f).childForceExpandWidth = true;
            UIFactory.Size(column.gameObject, 0f, -1f, 1f);
            Text name = UIFactory.Label(column, "Name", "", 26, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Size(name.gameObject, -1f, 34f);
            Text meta = UIFactory.Label(column, "Meta", "", 18, UIColors.InkLight, TextAnchor.MiddleLeft);
            meta.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Size(meta.gameObject, -1f, 26f);

            Text tag = UIFactory.Label(back.transform, "Tag", "", 19, UIColors.InkLight, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Size(tag.gameObject, 118f, 30f);
            return new Row { root = back.gameObject, back = back, number = number, icon = icon, name = name, meta = meta, tag = tag };
        }

        private void BuildPage(RectTransform page)
        {
            detailEmpty = UIFactory.Label(page, "Empty", "ページを選ぶと、ここに記録が表示されます。", 24, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(detailEmpty.rectTransform, 60f, 60f, 60f, 60f);

            detailViewport = UIFactory.CreateRect("Viewport", page);
            UIFactory.Stretch(detailViewport, 44f, 52f, 32f, 34f);
            Image hit = detailViewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            detailViewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = UIFactory.CreateRect("Content", detailViewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            UIFactory.Vertical(content.gameObject, new RectOffset(0, 8, 0, 16), 8f).childForceExpandWidth = true;
            UIFactory.FitContent(content.gameObject, false, true);
            detailContent = content.gameObject;
            detailScroll = CreateScroll(page, detailViewport, content, 44f, 32f, 52f);
            detailScrollbar = (RectTransform)detailScroll.verticalScrollbar.transform;

            BuildHeader(content);
            Divider(content);

            Heading(content, "見た目");
            RectTransform lookRow = UIFactory.CreateRect("Look", content);
            UIFactory.Horizontal(lookRow.gameObject, new RectOffset(4, 0, 2, 2), 12f, TextAnchor.UpperLeft);
            swatch = UIFactory.CreateImage(lookRow, "Swatch", UIFactory.RoundedRect(Color.white, UIColors.Outline, 8, 2), Color.white);
            UIFactory.Size(swatch.gameObject, 30f, 30f);
            appearanceText = UIFactory.Label(lookRow, "Text", "", 22, UIColors.Ink, TextAnchor.UpperLeft);
            UIFactory.Size(appearanceText.gameObject, 0f, -1f, 1f);

            Heading(content, "属性<size=18><color=#8A6A45>（100ml あたりの濃さ）</color></size>");
            for (int i = 0; i < MaxAttributeRows; i++) bars.Add(CreateBar(content));
            attributeEmpty = UIFactory.Label(content, "NoAttribute", "<color=#8A6A45>はっきりした力は感じられない</color>", 21, UIColors.Ink, TextAnchor.MiddleLeft).gameObject;

            Heading(content, "特性・効果");
            traitsText = Body(content);

            Heading(content, "作り方<size=18><color=#8A6A45>（素材・量・温度・混ぜ方・順番）</color></size>");
            RectTransform tabs = UIFactory.CreateRect("RecipeTabs", content);
            UIFactory.Horizontal(tabs.gameObject, new RectOffset(0, 0, 0, 2), 8f);
            recipeTabRow = tabs.gameObject;
            for (int i = 0; i < LiquidRecord.MaxRecipes; i++)
            {
                int index = i;
                recipeTabs.Add(UIWidgets.WoodButton(tabs, $"作り方{i + 1}", 20, new Vector2(118f, 42f), () => SetRecipeIndex(index)));
            }
            recipeText = Body(content);
            recipeText.lineSpacing = 1.14f;

            Heading(content, "使った素材");
            ingredientsText = Body(content);

            lineageHeading = Heading(content, "系譜").gameObject;
            parentsLabel = UIFactory.Label(content, "Parents", "元にした液体", 20, UIColors.InkLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < MaxLinks; i++) parentLinks.Add(CreateLink(content, parentLinks, i));
            childrenLabel = UIFactory.Label(content, "Children", "ここから生まれた液体", 20, UIColors.InkLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < MaxLinks; i++) childLinks.Add(CreateLink(content, childLinks, i));

            Heading(content, "記録");
            recordText = Body(content);
        }

        private void BuildHeader(RectTransform content)
        {
            RectTransform header = UIFactory.CreateRect("Header", content);
            UIFactory.Horizontal(header.gameObject, new RectOffset(0, 0, 0, 4), 22f, TextAnchor.UpperLeft);

            Image frame = UIFactory.CreateImage(header, "PreviewFrame", UIFactory.RoundedRect(new Color(0.93f, 0.86f, 0.7f), UIColors.ParchmentEdge, 14, 3), Color.white);
            UIFactory.Size(frame.gameObject, 236f, 236f);
            previewImage = new GameObject("Preview", typeof(RectTransform)).AddComponent<RawImage>();
            previewImage.transform.SetParent(frame.transform, false);
            previewImage.raycastTarget = false;
            UIFactory.Stretch(previewImage.rectTransform, 6f, 6f, 6f, 6f);

            RectTransform column = UIFactory.CreateRect("Column", header);
            UIFactory.Vertical(column.gameObject, new RectOffset(0, 0, 10, 0), 6f).childForceExpandWidth = true;
            UIFactory.Size(column.gameObject, 0f, -1f, 1f);
            kindText = UIFactory.Label(column, "Kind", "", 20, Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            nameText = UIFactory.Label(column, "Name", "", 36, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);

            // 名前を付ける欄（ふだんは隠す）
            RectTransform area = UIFactory.CreateRect("Rename", column);
            UIFactory.Vertical(area.gameObject, new RectOffset(0, 0, 0, 0), 8f).childForceExpandWidth = true;
            renameArea = area.gameObject;
            Image fieldBack = UIFactory.CreateImage(area, "Field", UIFactory.RoundedRect(new Color(1f, 0.98f, 0.9f), UIColors.Outline, 10, 3), Color.white);
            fieldBack.raycastTarget = true;
            UIFactory.Size(fieldBack.gameObject, -1f, 58f);
            renameField = CreateInput(fieldBack, $"新しい名前（{LiquidRegistry.MaxNameLength}文字まで）", 28, LiquidRegistry.MaxNameLength);
            RectTransform renameButtons = UIFactory.CreateRect("Buttons", area);
            UIFactory.Horizontal(renameButtons.gameObject, new RectOffset(0, 0, 0, 0), 10f);
            UIWidgets.WoodButton(renameButtons, "決める（Enter）", 21, new Vector2(176f, 46f), () => ConfirmRename());
            UIWidgets.WoodButton(renameButtons, "もとの名前に戻す", 21, new Vector2(196f, 46f), () => ConfirmRename(""));
            renameArea.SetActive(false);

            autoNameText = UIFactory.Label(column, "AutoName", "", 18, Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
            starsText = UIFactory.Label(column, "Stars", "", 30, new Color(0.71f, 0.47f, 0.11f), TextAnchor.MiddleLeft, FontStyle.Bold);

            RectTransform buttonRow = UIFactory.CreateRect("RenameButton", column);
            UIFactory.Horizontal(buttonRow.gameObject, new RectOffset(0, 0, 6, 0), 0f);
            UIWidgets.WoodButton(buttonRow, "名前を付ける（N）", 22, new Vector2(236f, 50f), BeginRename);
            renameButtonRow = buttonRow.gameObject;
        }

        private AttributeBar CreateBar(Transform parent)
        {
            RectTransform row = UIFactory.CreateRect("Attribute", parent);
            UIFactory.Horizontal(row.gameObject, new RectOffset(8, 0, 0, 0), 12f);
            Text name = UIFactory.Label(row, "Name", "", 21, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name.gameObject, 112f, 28f);
            Image back = UIFactory.CreateImage(row, "Bar", UIFactory.RoundedRect(new Color(0.36f, 0.25f, 0.15f, 0.35f), new Color(0f, 0f, 0f, 0f), 7, 0), Color.white);
            UIFactory.Size(back.gameObject, 430f, 18f);
            Image fill = UIFactory.CreateImage(back.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 7, 0), Color.white);
            UIFactory.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            Text value = UIFactory.Label(row, "Value", "", 21, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(value.gameObject, 70f, 28f);
            return new AttributeBar { root = row.gameObject, name = name, fill = fill, value = value };
        }

        private Link CreateLink(Transform parent, List<Link> owner, int index)
        {
            Image back = UIFactory.CreateImage(parent, "Link", UIFactory.RoundedRect(new Color(1f, 0.95f, 0.82f, 0.7f), new Color(0.45f, 0.3f, 0.17f, 0.5f), 8, 2), Color.white);
            back.raycastTarget = true;
            UIFactory.Size(back.gameObject, -1f, 40f);
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => FollowLink(owner, index));
            back.gameObject.AddComponent<UIButtonFx>();
            Text label = UIFactory.Label(back.transform, "Label", "", 21, UIColors.Ink, TextAnchor.MiddleLeft);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Stretch(label.rectTransform, 14f, 10f, 2f, 2f);
            back.gameObject.SetActive(false);
            return new Link { root = back.gameObject, label = label };
        }

        private static Text Heading(Transform parent, string text)
        {
            Text t = UIFactory.Label(parent, "Heading", "<color=#B5651D>◆</color> " + text, 24, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t.gameObject, -1f, 40f);
            return t;
        }

        private static Text Body(Transform parent)
        {
            Text t = UIFactory.Label(parent, "Body", "", 21, UIColors.Ink, TextAnchor.UpperLeft);
            t.lineSpacing = 1.08f;
            return t;
        }

        private static void Divider(Transform parent)
        {
            Image line = UIFactory.CreateImage(parent, "Divider", null, new Color(0.45f, 0.3f, 0.17f, 0.5f));
            UIFactory.Size(line.gameObject, -1f, 2f);
        }

        private static InputField CreateInput(Image back, string placeholderText, int fontSize, int limit)
        {
            Text text = UIFactory.Label(back.transform, "Text", "", fontSize, UIColors.Ink, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(text.rectTransform, 14f, 14f, 4f, 4f);
            Text placeholder = UIFactory.Label(back.transform, "Placeholder", placeholderText, fontSize - 2, new Color(0.45f, 0.3f, 0.17f, 0.7f), TextAnchor.MiddleLeft, FontStyle.Italic);
            UIFactory.Stretch(placeholder.rectTransform, 14f, 14f, 4f, 4f);
            var field = back.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = back;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = limit;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            return field;
        }

        private static ScrollRect CreateScroll(RectTransform page, RectTransform viewport, RectTransform content, float left, float top, float right)
        {
            // スクロールはページの紙に付ける（ページの上ならどこでもホイールで送れる。1ページに1つ）
            var scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            scroll.inertia = false;

            Image barBack = UIFactory.CreateImage(page, "Scrollbar", UIFactory.RoundedRect(new Color(0.36f, 0.25f, 0.15f, 0.3f), new Color(0f, 0f, 0f, 0f), 5, 0), Color.white);
            barBack.raycastTarget = true;
            RectTransform rt = barBack.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(-right + 14f - 10f, viewport.offsetMin.y);
            rt.offsetMax = new Vector2(-right + 14f, -top);
            RectTransform slide = UIFactory.CreateRect("Slide", rt);
            UIFactory.Stretch(slide, 1f, 1f, 1f, 1f);
            Image handle = UIFactory.CreateImage(slide, "Handle", UIFactory.RoundedRect(new Color(0.55f, 0.37f, 0.2f, 0.9f), new Color(0f, 0f, 0f, 0f), 4, 0), Color.white);
            handle.raycastTarget = true;
            UIFactory.Stretch(handle.rectTransform);
            var bar = barBack.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        private void BuildCloseButton(RectTransform cover)
        {
            Image close = UIFactory.CreateImage(cover, "Close", UIFactory.WoodPanel(), Color.white);
            close.raycastTarget = true;
            UIFactory.Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-8f, -8f), new Vector2(80f, 80f));
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            button.onClick.AddListener(() => { if (onCloseButton != null) onCloseButton(); });
            close.gameObject.AddComponent<UIButtonFx>();
            Text x = UIFactory.Label(close.transform, "X", "✕", 42, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(x.rectTransform);
        }

        private void BuildFooter(RectTransform parent)
        {
            footerText = UIFactory.Label(parent, "Footer", "", 22, UIColors.CreamDim, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            footerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(footerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1700f, 40f));
        }
    }
}
