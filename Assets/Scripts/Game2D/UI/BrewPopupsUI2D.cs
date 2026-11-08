using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>瓶詰めの結果カードに出す中身</summary>
    public sealed class ResultModel
    {
        public LiquidRecord record;
        public string name = "";
        public string detail = "";
        public int stars;
        public bool isNew;
        /// <summary>相談の見立て（空なら出さない）</summary>
        public string quest = "";
        public bool questMatch;
    }

    /// <summary>
    /// 調合台の、その場に出る手ごたえ（Step4-A・B）。
    ///   ふわっと浮かぶ文字（回復 +3・沸騰した・溶けきった！など）／カーソルのそばの数字（注いだ量・火加減）／
    ///   かき混ぜている間の溶け具合の輪／瓶詰めの結果カード（★ が1つずつ付く・新発見・相談に合うか）／画面がパッと光る
    /// 位置は画面の画素（オーバーレイの Canvas なので、ワールドの位置は Camera.WorldToScreenPoint で変えて渡す）。
    /// </summary>
    public sealed class BrewPopupsUI2D
    {
        private const int PopCount = 14;

        private sealed class Pop
        {
            public Text text;
            public float age = 10f;
            public float life;
            public Vector2 from;
            public float rise;
            public bool big;
        }

        private readonly RectTransform root;
        private readonly List<Pop> pops = new List<Pop>();
        private readonly Text follow;
        private readonly RectTransform ring;
        private readonly Image ringBack;
        private readonly Image ringFill;
        private readonly Text ringText;
        private readonly Image flash;
        private float flashTime = 10f;
        private Color flashColor = Color.white;
        private float ringDone = 10f;

        // 結果カード
        private readonly RectTransform result;
        private readonly CanvasGroup resultGroup;
        private readonly RawImage bottle;
        private readonly LiquidPreview preview;
        private readonly Text resultName;
        private readonly Text resultDetail;
        private readonly Text resultQuest;
        private readonly RectTransform newStamp;
        private bool resultIsNew;
        // 新しい液体：「新発見！」の見出し・ページめくり・影絵から色が付いていく瓶
        private readonly Text newHeader;
        private readonly PageFlipUI resultFlip;
        private const float NewPageDelay = 0.45f;

        // 素材ノートに書き足したお知らせ（順番に1つずつ）
        private sealed class SlipData
        {
            public Sprite icon;
            public string title;
            public string body;
        }

        private readonly RectTransform slip;
        private readonly CanvasGroup slipGroup;
        private readonly Image slipIcon;
        private readonly Text slipTitle;
        private readonly Text slipBody;
        private readonly RectTransform slipStamp;
        private readonly Vector2 slipCenter;
        private readonly Queue<SlipData> slipQueue = new Queue<SlipData>();
        private float slipAge = 100f;

        // 新しい反応を見つけたときのページ
        private readonly RectTransform page;
        private readonly CanvasGroup pageGroup;
        private readonly Text pageName;
        private readonly Text pageDescription;
        private readonly Image[] pageIcons = new Image[3];
        private readonly Text[] pagePlus = new Text[2];
        private readonly RectTransform pageStamp;
        private readonly PageFlipUI pageFlip;
        private float pageAge = 100f;
        private readonly Text[] stars = new Text[5];
        private readonly float[] starAge = new float[5];
        private float resultAge = 100f;
        private int resultStars;
        private int starsShown;

        /// <summary>結果カードの ★ が1つ付いた（番号 0〜4）。音を鳴らすのに使う</summary>
        public System.Action<int> StarShown;
        /// <summary>ページをめくりはじめた（新しい液体・新しい反応）。音を鳴らすのに使う</summary>
        public System.Action PageTurnStarted;

        public bool ResultVisible => result.gameObject.activeSelf;
        public bool SlipVisible => slip.gameObject.activeSelf;
        public bool PageVisible => page.gameObject.activeSelf;
        public bool NewStampVisible => newStamp.gameObject.activeSelf;
        public bool NewHeaderVisible => newHeader.gameObject.activeSelf;
        /// <summary>これまでにページをめくった回数（自動テスト用）</summary>
        public int PageTurnCount => resultFlip.PlayCount + pageFlip.PlayCount;
        public bool PageTurning => resultFlip.Playing || pageFlip.Playing;
        public int ResultStarsShown => starsShown;
        public bool RingVisible => ring.gameObject.activeSelf;

        public BrewPopupsUI2D(RectTransform parent, Vector2 resultCenter, Sprite ringSprite)
        {
            root = parent;

            flash = UIFactory.CreateImage(parent, "Flash", null, new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(flash.rectTransform);
            flash.gameObject.SetActive(false);

            for (int i = 0; i < PopCount; i++)
            {
                Text t = UIFactory.Label(parent, "Pop_" + i, "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.12f, 0.07f, 0.03f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
                UIFactory.Place(t.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 40f));
                t.gameObject.SetActive(false);
                pops.Add(new Pop { text = t });
            }

            follow = UIFactory.Label(parent, "Follow", "", 22, Color.white, TextAnchor.LowerLeft, FontStyle.Bold, true);
            follow.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(follow.rectTransform, Vector2.zero, new Vector2(0f, 0f), Vector2.zero, new Vector2(300f, 60f));
            follow.gameObject.SetActive(false);

            Sprite rs = ringSprite != null ? ringSprite : BrewUiSprites2D.Ring();
            ring = UIFactory.CreateRect("StirRing", parent);
            UIFactory.Place(ring, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 104f));
            ringBack = UIFactory.CreateImage(ring, "Back", rs, new Color(0f, 0f, 0f, 0.35f));
            UIFactory.Stretch(ringBack.rectTransform);
            ringFill = UIFactory.CreateImage(ring, "Fill", rs, new Color(0.6f, 0.9f, 0.5f, 0.95f));
            ringFill.type = Image.Type.Filled;
            ringFill.fillMethod = Image.FillMethod.Radial360;
            ringFill.fillOrigin = (int)Image.Origin360.Top;
            ringFill.fillClockwise = true;
            UIFactory.Stretch(ringFill.rectTransform);
            ringText = UIFactory.Label(ring, "Text", "", 17, Color.white, TextAnchor.UpperCenter, FontStyle.Bold, true);
            ringText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(ringText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(160f, 22f));
            ring.gameObject.SetActive(false);

            // 結果カード
            result = UIFactory.CreateRect("ResultCard", parent);
            UIFactory.Place(result, Vector2.zero, new Vector2(0.5f, 0.5f), resultCenter, new Vector2(500f, 236f));
            resultGroup = result.gameObject.AddComponent<CanvasGroup>();
            resultGroup.blocksRaycasts = false;
            resultGroup.interactable = false;
            Image paper = UIFactory.CreateImage(result, "Paper", UIFactory.Parchment(), Color.white);
            UIFactory.Stretch(paper.rectTransform);

            preview = new LiquidPreview(256, new Color(0f, 0f, 0f, 0f));
            bottle = new GameObject("Bottle", typeof(RectTransform)).AddComponent<RawImage>();
            bottle.transform.SetParent(result, false);
            bottle.texture = preview.Texture;
            bottle.raycastTarget = false;
            UIFactory.Place(bottle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(170f, 170f));

            resultName = UIFactory.Label(result, "Name", "", 28, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(resultName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -26f), new Vector2(296f, 72f));
            resultDetail = UIFactory.Label(result, "Detail", "", 18, UIColors.InkLight, TextAnchor.UpperLeft);
            UIFactory.Place(resultDetail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -100f), new Vector2(296f, 26f));
            for (int i = 0; i < 5; i++)
            {
                stars[i] = UIFactory.Label(result, "Star_" + i, "★", 34, new Color(0.45f, 0.35f, 0.25f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Place(stars[i].rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(210f + i * 38f, -146f), new Vector2(40f, 40f));
            }
            resultQuest = UIFactory.Label(result, "Quest", "", 18, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(resultQuest.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -172f), new Vector2(296f, 50f));

            // 新しい液体なら「新発見」のスタンプ（UISkin の stamp_new）
            newStamp = StampUI.Create(result, "NewStamp", SkinSprite(s => s.stampNew), "新発見", new Vector2(112f, 112f), -12f, StampUI.Red);
            newStamp.anchorMin = newStamp.anchorMax = new Vector2(1f, 1f);
            newStamp.anchoredPosition = new Vector2(-44f, -36f);
            newStamp.gameObject.SetActive(false);
            // 新しい液体なら、カードの上に「新発見！」の見出し。カードは図鑑のページのようにめくれて現れる
            newHeader = UIFactory.Label(result, "NewHeader", "新発見！　図鑑に新しいページ", 26, new Color(1f, 0.86f, 0.36f), TextAnchor.LowerCenter, FontStyle.Bold);
            newHeader.horizontalOverflow = HorizontalWrapMode.Overflow;
            var headerOutline = newHeader.gameObject.AddComponent<Outline>();
            headerOutline.effectColor = new Color(0.25f, 0.12f, 0.04f, 0.9f);
            headerOutline.effectDistance = new Vector2(2f, -2f);
            UIFactory.Place(newHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(480f, 40f));
            newHeader.gameObject.SetActive(false);
            resultFlip = new PageFlipUI(result, "PageTurn");
            result.gameObject.SetActive(false);

            // 素材ノートに書き足したお知らせ（上の真ん中）
            slipCenter = new Vector2(resultCenter.x, 930f);
            slip = UIFactory.CreateRect("NoteSlip", parent);
            UIFactory.Place(slip, Vector2.zero, new Vector2(0.5f, 0.5f), slipCenter, new Vector2(680f, 118f));
            slipGroup = slip.gameObject.AddComponent<CanvasGroup>();
            slipGroup.blocksRaycasts = false;
            slipGroup.interactable = false;
            Image slipPaper = UIFactory.CreateImage(slip, "Paper", SkinSprite(s => s.discoveryPage) ?? UIFactory.Parchment(), Color.white);
            UIFactory.Stretch(slipPaper.rectTransform);
            slipIcon = UIFactory.CreateImage(slip, "Icon", null, Color.white);
            slipIcon.preserveAspect = true;
            UIFactory.Place(slipIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(88f, 88f));
            slipTitle = UIFactory.Label(slip, "Title", "", 18, new Color(0.54f, 0.42f, 0.27f), TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(slipTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -14f), new Vector2(440f, 24f));
            slipBody = UIFactory.Label(slip, "Body", "", 21, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(slipBody.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(122f, -42f), new Vector2(440f, 66f));
            slipStamp = StampUI.Create(slip, "Stamp", SkinSprite(s => s.stampFact), "発見", new Vector2(96f, 96f), -10f, StampUI.Red);
            slipStamp.anchorMin = slipStamp.anchorMax = new Vector2(1f, 0.5f);
            slipStamp.anchoredPosition = new Vector2(-62f, 0f);
            slip.gameObject.SetActive(false);

            // 新しい反応を見つけたときのページ（真ん中）
            page = UIFactory.CreateRect("DiscoveryPage", parent);
            UIFactory.Place(page, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(resultCenter.x, 560f), new Vector2(880f, 440f));
            pageGroup = page.gameObject.AddComponent<CanvasGroup>();
            pageGroup.blocksRaycasts = false;
            pageGroup.interactable = false;
            Image pagePaper = UIFactory.CreateImage(page, "Paper", SkinSprite(s => s.discoveryPage) ?? UIFactory.Parchment(), Color.white);
            UIFactory.Stretch(pagePaper.rectTransform);
            Text pageTitle = UIFactory.Label(page, "Title", "新しい反応を発見！", 28, new Color(0.72f, 0.46f, 0.08f), TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(pageTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(800f, 36f));
            pageName = UIFactory.Label(page, "Name", "", 46, UIColors.Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(pageName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(800f, 58f));
            for (int i = 0; i < 3; i++)
            {
                pageIcons[i] = UIFactory.CreateImage(page, "Icon_" + i, null, Color.white);
                pageIcons[i].preserveAspect = true;
                UIFactory.Place(pageIcons[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(112f, 112f));
                if (i < 2)
                {
                    pagePlus[i] = UIFactory.Label(page, "Plus_" + i, "＋", 40, UIColors.InkLight, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Place(pagePlus[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(50f, 112f));
                }
            }
            pageDescription = UIFactory.Label(page, "Description", "", 21, UIColors.Ink, TextAnchor.UpperCenter);
            UIFactory.Place(pageDescription.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(760f, 110f));
            Text pageFooter = UIFactory.Label(page, "Footer", "素材ノートに書き足した（M で開く）", 17, new Color(0.54f, 0.42f, 0.27f), TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(pageFooter.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(760f, 26f));
            pageStamp = StampUI.Create(page, "Stamp", SkinSprite(s => s.stampReaction), "反応", new Vector2(136f, 136f), -14f, StampUI.Red);
            pageStamp.anchorMin = pageStamp.anchorMax = new Vector2(1f, 1f);
            pageStamp.anchoredPosition = new Vector2(-92f, -86f);
            pageFlip = new PageFlipUI(page, "PageTurn");
            page.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // ふわっと浮かぶ文字
        // ------------------------------------------------------------------

        public void ShowPop(string text, Vector2 screen, Color color, bool big = false)
        {
            Pop pop = pops[0];
            foreach (Pop p in pops)
            {
                if (p.age >= p.life) { pop = p; break; }
                if (p.age > pop.age) pop = p;
            }
            pop.age = 0f;
            pop.big = big;
            pop.life = big ? 2.2f : 1.4f;
            pop.rise = big ? 60f : 80f;
            pop.from = screen;
            pop.text.text = text;
            pop.text.fontSize = big ? 34 : 26;
            pop.text.color = color;
            pop.text.gameObject.SetActive(true);
            pop.text.transform.SetAsLastSibling();
        }

        // ------------------------------------------------------------------
        // カーソルのそばの数字・溶け具合の輪
        // ------------------------------------------------------------------

        public void SetFollow(string text, Vector2 screen)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (follow.gameObject.activeSelf != on) follow.gameObject.SetActive(on);
            if (!on) return;
            if (follow.text != text) follow.text = text;
            follow.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
        }

        public void SetRing(bool on, Vector2 screen, float fraction, string label)
        {
            bool show = on || ringDone < 0.8f;
            if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
            if (!show) return;
            ring.position = new Vector3(screen.x, screen.y, 0f);
            ringFill.fillAmount = Mathf.Clamp01(fraction);
            bool done = fraction >= 0.995f;
            ringFill.color = done ? new Color(1f, 0.85f, 0.35f, 1f) : Color.Lerp(new Color(0.55f, 0.75f, 1f, 0.95f), new Color(0.6f, 0.9f, 0.5f, 0.95f), fraction);
            ringText.text = label;
        }

        /// <summary>溶けきった瞬間：輪が金色に光って、ふくらんで消える</summary>
        public void RingCompleted()
        {
            ringDone = 0f;
        }

        // ------------------------------------------------------------------
        // 結果カード
        // ------------------------------------------------------------------

        public void ShowResult(ResultModel m)
        {
            result.gameObject.SetActive(true);
            result.SetAsLastSibling();
            resultAge = 0f;
            preview.Show(m.record);
            resultName.text = m.name;
            resultDetail.text = m.detail;
            resultQuest.text = m.quest;
            resultQuest.color = m.questMatch ? new Color(0.2f, 0.5f, 0.18f, 1f) : UIColors.InkLight;
            resultIsNew = m.isNew;
            newStamp.gameObject.SetActive(false);
            newHeader.gameObject.SetActive(m.isNew);
            bottle.color = m.isNew ? SilhouetteColor : Color.white;
            if (m.isNew)
            {
                resultFlip.Play(true);
                if (PageTurnStarted != null) PageTurnStarted();
            }
            else resultFlip.Stop();
            resultStars = Mathf.Clamp(m.stars, 0, 5);
            starsShown = 0;
            for (int i = 0; i < 5; i++)
            {
                starAge[i] = -1f;
                stars[i].color = new Color(0.45f, 0.35f, 0.25f, i < resultStars ? 0.3f : 0.12f);
                stars[i].rectTransform.localScale = Vector3.one;
            }
        }

        public void HideResult()
        {
            resultAge = 100f;
            result.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 画面がパッと光る
        // ------------------------------------------------------------------

        public void Flash(Color color)
        {
            flashColor = Color.Lerp(color, Color.white, 0.5f);
            flashTime = 0f;
            flash.gameObject.SetActive(true);
            flash.transform.SetAsLastSibling();
        }

        // ------------------------------------------------------------------

        public void Tick(float dt)
        {
            foreach (Pop p in pops)
            {
                if (!p.text.gameObject.activeSelf) continue;
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.text.gameObject.SetActive(false);
                    continue;
                }
                float u = p.age / p.life;
                float ease = 1f - (1f - u) * (1f - u);
                p.text.rectTransform.position = new Vector3(p.from.x, p.from.y + p.rise * ease, 0f);
                float pop = p.age < 0.12f ? Mathf.Lerp(0.6f, 1.15f, p.age / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((p.age - 0.12f) / 0.15f));
                p.text.transform.localScale = Vector3.one * pop;
                Color c = p.text.color;
                c.a = u < 0.7f ? 1f : Mathf.Clamp01((1f - u) / 0.3f);
                p.text.color = c;
            }

            ringDone += dt;
            if (ringDone < 0.8f)
            {
                float k = ringDone / 0.8f;
                ring.localScale = Vector3.one * (1f + 0.35f * k);
                Color c = ringFill.color;
                c.a = 1f - k;
                ringFill.color = c;
                ringBack.color = new Color(0f, 0f, 0f, 0.35f * (1f - k));
            }
            else
            {
                ring.localScale = Vector3.one;
                ringBack.color = new Color(0f, 0f, 0f, 0.35f);
            }

            flashTime += dt;
            if (flash.gameObject.activeSelf)
            {
                float a = flashTime < 0.08f ? flashTime / 0.08f : Mathf.Clamp01(1f - (flashTime - 0.08f) / 0.5f);
                Color fc = flashColor;
                fc.a = a * 0.38f;
                flash.color = fc;
                if (flashTime > 0.6f) flash.gameObject.SetActive(false);
            }

            TickResult(dt);
            TickSlip(dt);
            TickPage(dt);
        }

        // ------------------------------------------------------------------
        // 素材ノートに書き足したお知らせ・新しい反応のページ
        // ------------------------------------------------------------------

        /// <summary>「素材ノートに書き足した」お知らせ（いくつ来ても、1つずつ順番に出す）</summary>
        public void ShowSlip(Sprite icon, string title, string body)
        {
            slipQueue.Enqueue(new SlipData { icon = icon, title = title, body = body });
            if (!slip.gameObject.activeSelf) NextSlip();
        }

        private void NextSlip()
        {
            if (slipQueue.Count == 0)
            {
                slip.gameObject.SetActive(false);
                return;
            }
            SlipData d = slipQueue.Dequeue();
            slipIcon.sprite = d.icon;
            slipIcon.enabled = d.icon != null;
            slipTitle.text = d.title;
            slipBody.text = d.body;
            slipAge = 0f;
            slip.gameObject.SetActive(true);
            slip.SetAsLastSibling();
        }

        private void TickSlip(float dt)
        {
            if (!slip.gameObject.activeSelf) return;
            slipAge += dt;
            const float life = 3.4f;
            float inK = Mathf.Clamp01(slipAge / 0.25f);
            float outK = Mathf.Clamp01((life - slipAge) / 0.35f);
            slipGroup.alpha = Mathf.Min(inK, outK);
            float ease = 1f - (1f - inK) * (1f - inK);
            slip.anchoredPosition = slipCenter + new Vector2(0f, 40f * (1f - ease));
            StampUI.Slam(slipStamp, slipAge - 0.3f, -10f);
            if (slipAge >= life) NextSlip();
        }

        /// <summary>新しい反応を見つけたときのページ（素材の絵を「＋」で並べる）</summary>
        public void ShowDiscoveryPage(string name, string description, IReadOnlyList<Sprite> icons)
        {
            pageName.text = name;
            pageDescription.text = description;
            int n = icons != null ? Mathf.Min(3, icons.Count) : 0;
            float iconW = 112f, plusW = 50f;
            float width = n * iconW + Mathf.Max(0, n - 1) * plusW;
            float x = -width * 0.5f + iconW * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                bool on = i < n;
                pageIcons[i].gameObject.SetActive(on);
                if (on)
                {
                    pageIcons[i].sprite = icons[i];
                    pageIcons[i].rectTransform.anchoredPosition = new Vector2(x, -128f);
                }
                if (i < 2)
                {
                    bool plus = i < n - 1;
                    pagePlus[i].gameObject.SetActive(plus);
                    if (plus) pagePlus[i].rectTransform.anchoredPosition = new Vector2(x + iconW * 0.5f + plusW * 0.5f, -128f);
                }
                x += iconW + plusW;
            }
            pageAge = 0f;
            page.gameObject.SetActive(true);
            page.SetAsLastSibling();
            pageFlip.Play(true);
            if (PageTurnStarted != null) PageTurnStarted();
        }

        private void TickPage(float dt)
        {
            if (!page.gameObject.activeSelf) return;
            pageAge += dt;
            const float life = 5.8f;
            pageFlip.Tick(dt);
            float s = pageAge < 0.2f ? Mathf.Lerp(0.8f, 1.04f, pageAge / 0.2f) : Mathf.Lerp(1.04f, 1f, Mathf.Clamp01((pageAge - 0.2f) / 0.15f));
            page.localScale = Vector3.one * s;
            pageGroup.alpha = pageAge < life - 0.5f ? Mathf.Clamp01(pageAge / 0.15f) : Mathf.Clamp01((life - pageAge) / 0.5f);
            StampUI.Slam(pageStamp, pageAge - 0.95f, -14f);
            if (pageAge >= life) page.gameObject.SetActive(false);
        }

        private static readonly Color SilhouetteColor = new Color(0.1f, 0.07f, 0.05f, 1f);

        private static Sprite SkinSprite(System.Func<UISkin, Sprite> pick)
        {
            if (UIFactory.Skin == null) return null;
            Sprite s = pick(UIFactory.Skin);
            return s != null ? s : null;
        }

        private void TickResult(float dt)
        {
            if (!result.gameObject.activeSelf) return;
            resultAge += dt;
            float delay = resultIsNew ? NewPageDelay : 0f;    // 新しい液体はページをめくり終えてから
            float life = 4.2f + delay;
            resultFlip.Tick(dt);
            if (resultIsNew)
            {
                // 影絵の瓶に、めくり終わるころから色が付いていく
                bottle.color = Color.Lerp(SilhouetteColor, Color.white, Mathf.Clamp01((resultAge - 0.4f) / 0.7f));
                float h = resultAge < 0.2f ? Mathf.Lerp(0.5f, 1.12f, resultAge / 0.2f) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((resultAge - 0.2f) / 0.15f));
                newHeader.rectTransform.localScale = Vector3.one * h;
            }
            // ぽんっと出て、最後にすっと消える
            float s = resultAge < 0.18f ? Mathf.Lerp(0.7f, 1.06f, resultAge / 0.18f) : Mathf.Lerp(1.06f, 1f, Mathf.Clamp01((resultAge - 0.18f) / 0.15f));
            result.localScale = Vector3.one * s;
            resultGroup.alpha = resultAge < life - 0.4f ? Mathf.Clamp01(resultAge / 0.12f) : Mathf.Clamp01((life - resultAge) / 0.4f);
            // ★ を1つずつ
            if (resultIsNew) StampUI.Slam(newStamp, resultAge - 0.3f - delay, -12f);
            float starTime = resultAge - delay;
            int want = starTime < 0.45f ? 0 : Mathf.Min(resultStars, 1 + Mathf.FloorToInt((starTime - 0.45f) / 0.2f));
            while (starsShown < want)
            {
                starAge[starsShown] = 0f;
                if (StarShown != null) StarShown(starsShown);
                starsShown++;
            }
            for (int i = 0; i < 5; i++)
            {
                if (starAge[i] < 0f) continue;
                starAge[i] += dt;
                float a = starAge[i];
                float sc = a < 0.12f ? Mathf.Lerp(1.8f, 0.9f, a / 0.12f) : Mathf.Lerp(0.9f, 1f, Mathf.Clamp01((a - 0.12f) / 0.1f));
                stars[i].rectTransform.localScale = Vector3.one * sc;
                stars[i].color = new Color(1f, 0.78f, 0.2f, 1f);
            }
            if (resultAge >= life) HideResult();
        }
    }
}
