using System;
using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 素材ノート（発見の記録）。素材ごとに1ページ。調合しているうちにわかった性質が書き足されていき、
    /// まだの性質は「？？？」で数だけ見える。かかわる反応も、見つけたら書き足される。
    /// まだ出会っていない素材は影絵。性質と反応をすべて見つけると、ページに「すべて発見」のスタンプ。
    /// 絵：素材の絵は素材データのアイコン（Assets/Art2D/Ingredients）、カード・影絵・スタンプは UISkin（Assets/Art2D/UI/Discovery）。
    /// </summary>
    public sealed class IngredientNotebookUI
    {
        private const int Columns = 5;
        private const int PerPage = 20;
        private static readonly Color Muted = new Color(0.54f, 0.42f, 0.27f);
        private static readonly Color Green = new Color(0.18f, 0.45f, 0.16f);

        private sealed class Card
        {
            public GameObject root;
            public Image back;
            public Image icon;
            public Text name;
            public Text count;
            public RectTransform stamp;
        }

        private readonly GameObject root;
        private readonly RectTransform panel;
        private readonly IngredientInventory stock;
        private readonly Func<AlchemyManager> alchemy;
        private readonly Text subtitle;
        private readonly Text pageLabel;
        private readonly List<Card> cards = new List<Card>();
        private readonly Image detailIcon;
        private readonly RectTransform completeStamp;
        private readonly Text detailName;
        private readonly Text detailMeta;
        private readonly Text detailDescription;
        private readonly Text detailAttributes;
        private readonly Text detailFacts;
        private readonly Text detailReactions;
        private readonly List<IngredientData> list = new List<IngredientData>();
        private readonly StringBuilder sb = new StringBuilder(512);
        private float appear;
        private int page;
        private int selected;
        private float stampTime = -1f;

        public bool IsOpen => root.activeSelf;
        public int Count => list.Count;
        public IngredientData Selected => selected >= 0 && selected < list.Count ? list[selected] : null;
        /// <summary>今のページの、性質の行（自動テスト用）</summary>
        public string FactsText => detailFacts.text;

        public IngredientNotebookUI(RectTransform parent, IngredientInventory stock, Func<AlchemyManager> alchemy, Action onClose)
        {
            this.stock = stock;
            this.alchemy = alchemy;
            Image paper = MenuPanel.Create(parent, "IngredientNotebook", "素材ノート", new Vector2(1660f, 930f), out root, 0.62f);
            panel = paper.rectTransform;

            subtitle = UIFactory.Label(panel, "Subtitle", "", 22, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(1400f, 32f));

            // 左：素材のカード（5 × 4）
            RectTransform grid = UIFactory.CreateRect("Grid", panel);
            UIFactory.Place(grid, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -100f), new Vector2(720f, 650f));
            for (int i = 0; i < PerPage; i++) cards.Add(BuildCard(grid, i, onClose));

            RectTransform pager = UIFactory.CreateRect("Pager", panel);
            UIFactory.Place(pager, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -770f), new Vector2(720f, 56f));
            UIFactory.Horizontal(pager.gameObject, new RectOffset(0, 0, 0, 0), 16f, TextAnchor.MiddleCenter);
            UIWidgets.WoodButton(pager, "＜", 26, new Vector2(80f, 52f), () => ChangePage(-1));
            pageLabel = UIFactory.Label(pager, "Page", "", 22, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Size(pageLabel.gameObject, 120f, 52f);
            UIWidgets.WoodButton(pager, "＞", 26, new Vector2(80f, 52f), () => ChangePage(1));

            // 右：ページ
            Image frame = UIFactory.CreateImage(panel, "IconFrame", UIFactory.RoundedRect(new Color(0.96f, 0.9f, 0.76f), UIColors.ParchmentEdge, 16, 3), Color.white);
            UIFactory.Place(frame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(800f, -100f), new Vector2(220f, 220f));
            detailIcon = UIFactory.CreateImage(frame.transform, "Icon", null, Color.white);
            detailIcon.preserveAspect = true;
            UIFactory.Stretch(detailIcon.rectTransform, 14f, 14f, 14f, 14f);
            completeStamp = StampUI.Create(frame.transform, "CompleteStamp", Skin(s => s.stampComplete), "すべて発見", new Vector2(150f, 150f), -14f, StampUI.Gold);
            completeStamp.anchorMin = completeStamp.anchorMax = new Vector2(1f, 0f);
            completeStamp.anchoredPosition = new Vector2(-20f, 30f);

            detailName = UIFactory.Label(panel, "Name", "", 40, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(detailName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1044f, -104f), new Vector2(580f, 52f));
            detailMeta = UIFactory.Label(panel, "Meta", "", 20, Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(detailMeta.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1044f, -160f), new Vector2(580f, 28f));
            detailDescription = UIFactory.Label(panel, "Description", "", 21, UIColors.Ink, TextAnchor.UpperLeft);
            UIFactory.Place(detailDescription.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1044f, -194f), new Vector2(580f, 96f));
            detailAttributes = UIFactory.Label(panel, "Attributes", "", 21, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(detailAttributes.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1044f, -290f), new Vector2(580f, 30f));

            Heading("FactsHeading", "見つけた性質", -344f);
            detailFacts = UIFactory.Label(panel, "Facts", "", 22, UIColors.Ink, TextAnchor.UpperLeft);
            detailFacts.lineSpacing = 1.25f;
            UIFactory.Place(detailFacts.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(800f, -382f), new Vector2(820f, 290f));
            Heading("ReactionsHeading", "かかわる反応", -680f);
            detailReactions = UIFactory.Label(panel, "Reactions", "", 21, UIColors.Ink, TextAnchor.UpperLeft);
            detailReactions.lineSpacing = 1.15f;
            UIFactory.Place(detailReactions.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(800f, -716f), new Vector2(820f, 120f));

            Text footer = UIFactory.Label(panel, "Footer", "素材をクリックで選ぶ　　←→↑↓ 選ぶ　　M／Esc 閉じる", 19, Muted, TextAnchor.MiddleLeft);
            UIFactory.Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 22f), new Vector2(1100f, 30f));
            RectTransform closeRow = UIFactory.CreateRect("Close", panel);
            UIFactory.Place(closeRow, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 16f), new Vector2(220f, 60f));
            UIFactory.Horizontal(closeRow.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleRight);
            UIWidgets.WoodButton(closeRow, "とじる", 26, new Vector2(200f, 56f), () => { if (onClose != null) onClose(); });

            root.SetActive(false);
        }

        private void Heading(string name, string text, float y)
        {
            Text t = UIFactory.Label(panel, name, "◆ " + text, 24, new Color(0.7f, 0.42f, 0.12f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(800f, y), new Vector2(600f, 34f));
        }

        private Card BuildCard(RectTransform grid, int index, Action onClose)
        {
            var c = new Card();
            int col = index % Columns, row = index / Columns;
            Image back = UIFactory.CreateImage(grid, "Card_" + index, Skin(s => s.notebookCard) ?? UIFactory.SlotSprite(false), Color.white);
            back.raycastTarget = true;
            UIFactory.Place(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(col * 144f, -row * 162f), new Vector2(134f, 152f));
            var button = back.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            int captured = index;
            button.onClick.AddListener(() => SelectSlot(captured));
            c.root = back.gameObject;
            c.back = back;
            c.icon = UIFactory.CreateImage(back.transform, "Icon", null, Color.white);
            c.icon.preserveAspect = true;
            UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(92f, 92f));
            c.name = UIFactory.Label(back.transform, "Name", "", 17, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Place(c.name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(128f, 24f));
            c.count = UIFactory.Label(back.transform, "Count", "", 14, UIColors.CreamDim, TextAnchor.MiddleCenter);
            UIFactory.Place(c.count.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(128f, 20f));
            c.stamp = StampUI.Create(back.transform, "Stamp", Skin(s => s.stampComplete), "", new Vector2(54f, 54f), -12f, StampUI.Gold);
            c.stamp.anchorMin = c.stamp.anchorMax = new Vector2(1f, 1f);
            c.stamp.anchoredPosition = new Vector2(-20f, -22f);
            return c;
        }

        // ------------------------------------------------------------------

        public void Open(IngredientData focus = null)
        {
            list.Clear();
            IngredientDatabase db = GameContext.Ingredients;
            if (db != null) foreach (IngredientData d in db.All) if (d != null) list.Add(d);
            list.Sort(IngredientDatabase.Compare);
            selected = focus != null ? Mathf.Max(0, list.IndexOf(focus)) : FirstMet();
            page = list.Count > 0 ? selected / PerPage : 0;
            root.SetActive(true);
            appear = 0f;
            DiscoveryJournal.Changed -= Refresh;
            DiscoveryJournal.Changed += Refresh;
            Refresh();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.7f);
        }

        public void Close()
        {
            if (!IsOpen) return;
            DiscoveryJournal.Changed -= Refresh;
            root.SetActive(false);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.7f);
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            MenuPanel.Pop(panel, ref appear, dt);
            if (stampTime >= 0f)
            {
                stampTime += dt;
                StampUI.Slam(completeStamp, stampTime, -14f);
            }
            if (!allowInput || list.Count == 0) return;
            int move = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) move = 1;
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) move = -1;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) move = Columns;
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) move = -Columns;
            else if (Input.GetKeyDown(KeyCode.PageDown)) ChangePage(1);
            else if (Input.GetKeyDown(KeyCode.PageUp)) ChangePage(-1);
            if (move != 0) Select(Mathf.Clamp(selected + move, 0, list.Count - 1));
        }

        /// <summary>素材を選ぶ（自動テスト用にも公開）</summary>
        public void Select(int index)
        {
            if (list.Count == 0) return;
            selected = Mathf.Clamp(index, 0, list.Count - 1);
            page = selected / PerPage;
            stampTime = -1f;
            Refresh();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.4f, UnityEngine.Random.Range(0.95f, 1.08f));
        }

        public bool SelectById(string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IngredientId != id) continue;
                Select(i);
                return true;
            }
            return false;
        }

        private void SelectSlot(int slot)
        {
            int index = page * PerPage + slot;
            if (index < list.Count) Select(index);
        }

        private void ChangePage(int direction)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            page = (page + direction + pages) % pages;
            selected = Mathf.Min(list.Count - 1, page * PerPage);
            stampTime = -1f;
            Refresh();
        }

        private int FirstMet()
        {
            for (int i = 0; i < list.Count; i++) if (Met(list[i])) return i;
            return 0;
        }

        private bool Met(IngredientData d)
        {
            if (d == null) return false;
            if (DiscoveryJournal.Knows(d, IngredientFact.Used)) return true;
            return stock != null && (stock.IsUnlocked(d) || stock.Has(d));
        }

        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (!IsOpen) return;
            AlchemyManager am = alchemy != null ? alchemy() : null;
            IReadOnlyList<ReactionData> reactions = am != null ? am.Reactions : null;

            int met = 0, known = 0, total = 0;
            foreach (IngredientData d in list)
            {
                if (Met(d)) met++;
                known += DiscoveryJournal.KnownCount(d);
                total += DiscoveryJournal.TotalCount(d);
            }
            int reactionTotal = reactions != null ? reactions.Count : 0;
            subtitle.text = $"出会った素材 {met} / {list.Count}　　見つけた性質 {known} / {total}　　見つけた反応 {DiscoveryJournal.KnownReactionCount} / {reactionTotal}";

            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            pageLabel.text = $"{page + 1} / {pages}";
            for (int i = 0; i < PerPage; i++)
            {
                Card c = cards[i];
                int index = page * PerPage + i;
                bool on = index < list.Count;
                c.root.SetActive(on);
                if (!on) continue;
                IngredientData d = list[index];
                bool isMet = Met(d);
                bool sel = index == selected;
                if (Skin(s => s.notebookCard) == null) c.back.sprite = UIFactory.SlotSprite(sel);
                c.back.color = sel ? new Color(1f, 0.92f, 0.7f, 1f) : Color.white;
                c.icon.sprite = isMet ? IngredientIcons.Get(d) : Unknown();
                c.icon.color = isMet ? Color.white : new Color(0.15f, 0.1f, 0.08f, 0.85f);
                c.name.text = isMet ? d.DisplayName : "？？？";
                int k = DiscoveryJournal.KnownCount(d), t = DiscoveryJournal.TotalCount(d);
                c.count.text = !isMet ? "まだ出会っていない" : t > 0 ? $"性質 {k} / {t}" : "性質なし";
                c.stamp.gameObject.SetActive(isMet && Complete(d, reactions));
            }
            RefreshDetail(reactions, am != null ? am.Settings : null);
        }

        private void RefreshDetail(IReadOnlyList<ReactionData> reactions, AlchemySettings settings)
        {
            IngredientData d = Selected;
            bool isMet = Met(d);
            if (d == null)
            {
                detailName.text = "";
                detailFacts.text = "";
                return;
            }
            detailIcon.sprite = isMet ? IngredientIcons.Get(d) : Unknown();
            detailIcon.color = isMet ? Color.white : new Color(0.15f, 0.1f, 0.08f, 0.85f);
            if (!isMet)
            {
                detailName.text = "？？？";
                detailMeta.text = "まだ出会っていない素材";
                detailDescription.text = d.RequiredReputation > 0
                    ? $"町の人の相談を {d.RequiredReputation} 件解決すると、取引先帳に載るらしい。"
                    : "取引先帳で取引すると、使えるようになる。";
                detailAttributes.text = "";
                detailFacts.text = $"<color=#A08060>？？？　性質が {DiscoveryJournal.TotalCount(d)} つありそうだ</color>";
                detailReactions.text = "";
                completeStamp.gameObject.SetActive(false);
                return;
            }

            detailName.text = d.DisplayName;
            detailMeta.text = $"{CategoryLabel(d.Category)}　<color=#B5781C>{new string('★', Mathf.Clamp(d.Rarity, 1, 5))}</color>　{string.Join("・", d.Tags)}";
            detailDescription.text = d.Description;
            detailAttributes.text = "力：" + AttributeLine(d, settings);

            sb.Length = 0;
            IngredientFact all = DiscoveryJournal.FactsOf(d);
            foreach (IngredientFact f in DiscoveryJournal.Order)
            {
                if ((all & f) == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                if (DiscoveryJournal.Knows(d, f)) sb.Append("<color=#2E7D32>●</color> <b>").Append(DiscoveryJournal.ShortLabel(f)).Append("</b>：").Append(DiscoveryJournal.Describe(d, f));
                else sb.Append("<color=#A08060>？？？　まだ見つけていない性質</color>");
            }
            detailFacts.text = sb.Length > 0 ? sb.ToString() : "<color=#A08060>特別な性質はないようだ（どんな素材ともなじむ）</color>";

            sb.Length = 0;
            int hidden = 0;
            if (reactions != null)
            {
                foreach (ReactionData r in reactions)
                {
                    if (r == null || !Involves(r, d)) continue;
                    if (!DiscoveryJournal.KnowsReaction(r))
                    {
                        hidden++;
                        continue;
                    }
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("<color=#B5651D>◆</color> <b>").Append(r.DisplayName).Append("</b>（").Append(Partners(r, d)).Append("）");
                }
            }
            if (hidden > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append($"<color=#A08060>？？？　この素材がかかわる反応が、あと {hidden} つ眠っているらしい</color>");
            }
            detailReactions.text = sb.Length > 0 ? sb.ToString() : "<color=#A08060>かかわる反応は、知られていない</color>";

            bool complete = Complete(d, reactions);
            if (complete && stampTime < 0f) stampTime = 0f;
            if (!complete)
            {
                stampTime = -1f;
                completeStamp.gameObject.SetActive(false);
            }
        }

        private static bool Complete(IngredientData d, IReadOnlyList<ReactionData> reactions)
        {
            if (DiscoveryJournal.KnownCount(d) < DiscoveryJournal.TotalCount(d)) return false;
            if (reactions != null)
            {
                foreach (ReactionData r in reactions)
                {
                    if (r != null && Involves(r, d) && !DiscoveryJournal.KnowsReaction(r)) return false;
                }
            }
            return DiscoveryJournal.Knows(d, IngredientFact.Used);
        }

        private static bool Involves(ReactionData r, IngredientData d)
        {
            foreach (IngredientData x in r.RequiredIngredients) if (x == d) return true;
            return false;
        }

        private static string Partners(ReactionData r, IngredientData d)
        {
            var names = new List<string>();
            foreach (IngredientData x in r.RequiredIngredients) if (x != null && x != d) names.Add(x.DisplayName);
            return names.Count > 0 ? string.Join("・", names) + "と" : "この素材だけで";
        }

        private static string AttributeLine(IngredientData d, AlchemySettings settings)
        {
            var parts = new List<string>();
            foreach (AttributeValue a in d.Attributes)
            {
                string n = settings != null ? settings.GetDisplayName(a.attribute) : a.attribute.ToString();
                parts.Add($"{n} {(a.value > 0f ? "+" : "")}{a.value:0.#}");
            }
            if (d.GlobalAmplifyPercent != 0f) parts.Add($"ぜんぶの力 ×{1f + d.GlobalAmplifyPercent / 100f:0.##}");
            foreach (AttributeAmplifier a in d.Amplifiers)
            {
                string n = settings != null ? settings.GetDisplayName(a.attribute) : a.attribute.ToString();
                parts.Add($"{n}の力 {(a.percent > 0f ? "+" : "")}{a.percent:0}%");
            }
            return parts.Count > 0 ? string.Join("・", parts) : "なし";
        }

        private static string CategoryLabel(IngredientCategory c)
        {
            switch (c)
            {
                case IngredientCategory.BaseLiquid: return "ベース液体";
                case IngredientCategory.Catalyst: return "触媒";
                default: return "主素材";
            }
        }

        private static Sprite Unknown()
        {
            return Skin(s => s.unknownMark) ?? UIFactory.RoundedRect(Color.white, Color.white, 30, 0);
        }

        /// <summary>UISkin の絵（空なら本当の null を返す。?? で代わりの絵を使えるように）</summary>
        private static Sprite Skin(Func<UISkin, Sprite> pick)
        {
            if (UIFactory.Skin == null) return null;
            Sprite s = pick(UIFactory.Skin);
            return s != null ? s : null;
        }
    }
}
