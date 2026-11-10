using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 新しい素材が調合机に届いたときの、小さなお知らせ（右下からすっと出て、少しして消える）。
    /// 手紙の絵は UISkin の letter.png（Assets/CustomAssets/Images/UI/Discovery）、音は SoundLibrary の ingredientArrived（se_letter.wav）。
    /// いくつ届いても、1つずつ順番に出す。
    /// </summary>
    public sealed class ArrivalNoticeUI
    {
        private const float Life = 4.6f;

        private readonly RectTransform card;
        private readonly CanvasGroup group;
        private readonly Image ingredientIcon;
        private readonly Text body;
        private readonly Vector2 rest;
        private readonly Queue<IngredientData> queue = new Queue<IngredientData>();
        private float age = 100f;

        /// <summary>お知らせを出した回数（自動テスト用）</summary>
        public int ShownCount { get; private set; }
        public bool Visible => card.gameObject.activeSelf;
        public string Text => body.text;

        public ArrivalNoticeUI(RectTransform parent)
        {
            // 右下（トレイのページ送りの上）。机の上の物にかからない、小さな紙
            rest = new Vector2(-34f, 266f);
            card = UIFactory.CreateRect("ArrivalNotice", parent);
            UIFactory.Place(card, new Vector2(1f, 0f), new Vector2(1f, 0f), rest, new Vector2(470f, 122f));
            group = card.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Image paper = UIFactory.CreateImage(card, "Paper", Skin(s => s.discoveryPage) ?? UIFactory.Parchment(), UIFactory.Paper * new Color(1f, 0.97f, 0.9f, 0.96f));
            UIFactory.Stretch(paper.rectTransform);
            Image letter = UIFactory.CreateImage(card, "Letter", Skin(s => s.letter), Color.white);
            letter.preserveAspect = true;
            letter.enabled = letter.sprite != null;
            UIFactory.Place(letter.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(76f, 76f));
            Text title = UIFactory.Label(card, "Title", "調合机に届いた", 17, new Color(0.54f, 0.42f, 0.27f), TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -14f), new Vector2(250f, 24f));
            body = UIFactory.Label(card, "Body", "", 24, UIColors.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            body.horizontalOverflow = HorizontalWrapMode.Overflow;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            UIFactory.Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -40f), new Vector2(250f, 72f));
            ingredientIcon = UIFactory.CreateImage(card, "Ingredient", null, Color.white);
            ingredientIcon.preserveAspect = true;
            UIFactory.Place(ingredientIcon.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(84f, 84f));
            foreach (Graphic g in card.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            card.gameObject.SetActive(false);
        }

        public void Show(IngredientData d)
        {
            if (d == null) return;
            queue.Enqueue(d);
            if (!card.gameObject.activeSelf) Next();
        }

        public void Clear()
        {
            queue.Clear();
            card.gameObject.SetActive(false);
        }

        private void Next()
        {
            if (queue.Count == 0)
            {
                card.gameObject.SetActive(false);
                return;
            }
            IngredientData d = queue.Dequeue();
            ingredientIcon.sprite = IngredientIcons.Get(d);
            ingredientIcon.enabled = ingredientIcon.sprite != null;
            body.text = d.DisplayName + "\n<size=16><color=#8A6A45>素材トレイに並んだ</color></size>";
            body.supportRichText = true;
            age = 0f;
            ShownCount++;
            card.gameObject.SetActive(true);
            card.SetAsLastSibling();
            SoundLibrary lib = AudioManager.Lib;
            AudioClip clip = lib != null && lib.ingredientArrived != null ? lib.ingredientArrived : SfxSynth.LetterArrive;
            AudioManager.Se(clip, 0.55f);
        }

        public void Tick(float dt)
        {
            if (!card.gameObject.activeSelf) return;
            age += dt;
            float inK = Mathf.Clamp01(age / 0.45f);
            float outK = Mathf.Clamp01((Life - age) / 0.6f);
            group.alpha = Mathf.Min(inK, outK);
            float ease = 1f - (1f - inK) * (1f - inK);
            card.anchoredPosition = rest + new Vector2(60f * (1f - ease), 0f);
            if (age >= Life) Next();
        }

        private static Sprite Skin(System.Func<UISkin, Sprite> pick)
        {
            if (UIFactory.Skin == null) return null;
            Sprite s = pick(UIFactory.Skin);
            return s != null ? s : null;
        }
    }
}
