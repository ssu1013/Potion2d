using System;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面右上：所持金（増えるとカウントアップ＋「+50 G」が浮かぶ）と、相談メモ（いま乗っている相談の悩み・見立て・
    /// うわさ、手に持っている瓶や棚の瓶・レシピ本の作り方が役に立ちそうか）。
    /// </summary>
    public sealed class QuestTrackerUI
    {
        private readonly QuestManager quests;
        private readonly PlayerProgress progress;
        private readonly Func<PotionStack> heldPotion;

        private readonly RectTransform goldPanel;
        private readonly Text goldText;
        private readonly Image coin;
        private readonly Text goldPopup;
        private readonly CanvasGroup goldPopupGroup;
        private float shownGold;
        private int shownGoldInt = -1;
        private float popupTimer;
        private float coinPulse;

        private readonly RectTransform notePanel;
        private readonly Text noteHeader;
        private readonly Text noteBody;
        private readonly Text noteCheck;
        private readonly Image noteCheckBack;
        private CustomerPhase lastPhase = CustomerPhase.None;
        private QuestData lastQuest;
        private PotionStack lastHeld;
        private PotionStack lastShareable;
        private LiquidRecord lastTeachable;
        private float scanTimer;
        private PotionStack shareable;
        private LiquidRecord teachable;
        private float notePulse;
        private float time;

        private Vector2 PopupBase => new Vector2(-28f - goldPanel.rect.width - 14f, -36f);

        public QuestTrackerUI(RectTransform parent, QuestManager quests, PlayerProgress progress, Func<PotionStack> heldPotion)
        {
            this.quests = quests;
            this.progress = progress;
            this.heldPotion = heldPotion;

            // 所持金
            Image gold = UIFactory.CreateImage(parent, "Gold", UIFactory.RoundedRect(UIColors.Shadow, new Color(0f, 0f, 0f, 0f), 12, 0), Color.white);
            goldPanel = gold.rectTransform;
            UIFactory.Place(goldPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(220f, 64f));
            UIFactory.Horizontal(gold.gameObject, new RectOffset(16, 24, 8, 8), 12f, TextAnchor.MiddleLeft);
            UIFactory.FitContent(gold.gameObject, true, false);
            coin = UIFactory.CreateImage(goldPanel, "Coin", UIWidgets.Coin(), Color.white);
            UIFactory.Size(coin.gameObject, 44f, 44f);
            goldText = UIFactory.Label(goldPanel, "Amount", "0 G", 34, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            goldText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 増えた金額は所持金の左に浮かぶ（相談メモに隠れないように）
            goldPopup = UIFactory.Label(parent, "GoldPopup", "", 36, UIColors.Gold, TextAnchor.MiddleRight, FontStyle.Bold, true);
            UIFactory.Place(goldPopup.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), PopupBase, new Vector2(300f, 50f));
            goldPopupGroup = goldPopup.gameObject.AddComponent<CanvasGroup>();
            goldPopupGroup.alpha = 0f;

            // 相談メモ
            Image paper = UIFactory.CreateImage(parent, "QuestNote", UIFactory.Parchment(), Color.white);
            notePanel = paper.rectTransform;
            UIFactory.Place(notePanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -110f), new Vector2(470f, 200f));
            UIFactory.Vertical(paper.gameObject, new RectOffset(28, 28, 22, 24), 8f).childForceExpandWidth = true;
            UIFactory.FitContent(paper.gameObject, false, true);

            noteHeader = UIFactory.Label(notePanel, "Header", "", 26, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            Image divider = UIFactory.CreateImage(notePanel, "Divider", null, new Color(0.45f, 0.3f, 0.17f, 0.5f));
            UIFactory.Size(divider.gameObject, -1f, 2f);
            noteBody = UIFactory.Label(notePanel, "Body", "", 22, UIColors.Ink, TextAnchor.UpperLeft);
            noteBody.lineSpacing = 1.12f;

            noteCheckBack = UIFactory.CreateImage(notePanel, "CheckBack", UIFactory.RoundedRect(new Color(1f, 1f, 1f, 0.55f), new Color(0f, 0f, 0f, 0f), 8, 0), Color.white);
            UIFactory.Horizontal(noteCheckBack.gameObject, new RectOffset(12, 12, 6, 6), 0f);
            noteCheck = UIFactory.Label(noteCheckBack.transform, "Check", "", 21, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);

            shownGold = progress != null ? progress.Gold : 0;
            if (progress != null) progress.GoldChanged += OnGoldChanged;
            Refresh(true);
        }

        public void Tick(float dt)
        {
            time += dt;
            int gold = progress != null ? progress.Gold : 0;
            // 増えたときは少しずつ数字を上げる
            if (Mathf.Abs(shownGold - gold) > 0.01f)
            {
                float speed = Mathf.Max(60f, Mathf.Abs(gold - shownGold) * 2.5f);
                shownGold = Mathf.MoveTowards(shownGold, gold, speed * dt);
            }
            int display = Mathf.RoundToInt(shownGold);
            if (display != shownGoldInt)
            {
                shownGoldInt = display;
                goldText.text = display.ToString("#,0") + " G";
            }

            if (coinPulse > 0f)
            {
                coinPulse = Mathf.MoveTowards(coinPulse, 0f, dt * 2.5f);
                float s = 1f + coinPulse * 0.35f;
                coin.rectTransform.localScale = new Vector3(s, s, 1f);
                coin.rectTransform.localRotation = Quaternion.Euler(0f, coinPulse * 360f, 0f);
            }

            if (popupTimer > 0f)
            {
                popupTimer -= dt;
                float k = 1f - Mathf.Clamp01(popupTimer / 1.8f);
                goldPopupGroup.alpha = Mathf.Clamp01(popupTimer / 0.5f);
                goldPopup.rectTransform.anchoredPosition = PopupBase + new Vector2(0f, k * 26f);
            }

            Refresh(false);

            // 相談が変わったら少し跳ねて知らせる
            if (notePulse > 0f)
            {
                notePulse = Mathf.MoveTowards(notePulse, 0f, dt * 3f);
                float ns = 1f + Mathf.Sin(notePulse * Mathf.PI) * 0.05f;
                notePanel.localScale = new Vector3(ns, ns, 1f);
            }
        }

        /// <summary>所持金の変化を通知されたとき</summary>
        private void OnGoldChanged(int newGold, int delta)
        {
            if (delta > 0)
            {
                goldPopup.text = "+" + delta + " G";
                goldPopup.color = UIColors.Gold;
                popupTimer = 1.8f;
                coinPulse = 1f;
            }
            else if (delta < 0)
            {
                // 支払い（取引先帳）
                goldPopup.text = delta + " G";
                goldPopup.color = new Color(1f, 0.62f, 0.5f);
                popupTimer = 1.8f;
                shownGold = newGold;
            }
            else
            {
                shownGold = newGold; // 減ったとき（リセットなど）はすぐ合わせる
            }
        }

        public void SnapGold()
        {
            shownGold = progress != null ? progress.Gold : 0;
            shownGoldInt = -1;
            popupTimer = 0f;
            goldPopupGroup.alpha = 0f;
            Refresh(true);
        }

        private void Refresh(bool force)
        {
            CustomerPhase phase = quests != null ? quests.Phase : CustomerPhase.None;
            QuestData quest = quests != null ? quests.CurrentQuest : null;
            PotionStack held = heldPotion != null ? heldPotion() : null;

            // 棚の瓶・レシピ本の作り方が役に立ちそうかは、0.5 秒ごとに調べる（毎フレームは調べない）
            scanTimer -= Time.unscaledDeltaTime;
            if (phase == CustomerPhase.Waiting && quests != null)
            {
                if (scanTimer <= 0f || force)
                {
                    scanTimer = 0.5f;
                    shareable = quests.FindShareable();
                    teachable = quests.FindTeachable();
                }
            }
            else
            {
                shareable = null;
                teachable = null;
            }

            // 状態が変わったときだけ作り直す（毎フレーム文字列を作らない）
            bool changed = force || phase != lastPhase || quest != lastQuest || held != lastHeld ||
                           shareable != lastShareable || teachable != lastTeachable;
            if (!changed) return;
            if (lastQuest != null || lastPhase != CustomerPhase.None)
            {
                if (quest != null && phase == CustomerPhase.Waiting && lastPhase != CustomerPhase.Waiting) notePulse = 1f;
            }
            lastPhase = phase;
            lastQuest = quest;
            lastHeld = held;
            lastShareable = shareable;
            lastTeachable = teachable;
            bool satisfied = quest != null && held != null && quest.IsSatisfiedBy(held);

            var sb = new StringBuilder();
            string check = null;
            Color checkColor = UIColors.Ink;
            Color good = new Color(0.18f, 0.45f, 0.16f);
            switch (phase)
            {
                case CustomerPhase.Waiting when quest != null:
                    noteHeader.text = "<color=#B5651D>◆</color> 相談メモ";
                    sb.Append("<b>").Append(quest.ClientFullName).Append("</b>\n");
                    if (!string.IsNullOrEmpty(quest.TroubleSummary)) sb.Append("悩み：").Append(quest.TroubleSummary).Append('\n');
                    if (!string.IsNullOrEmpty(quest.GoalSummary)) sb.Append("<size=20>見立て：").Append(quest.GoalSummary).Append("</size>\n");
                    if (!string.IsNullOrEmpty(quest.Hint)) sb.Append("<size=19><color=#7A5A38>うわさ：").Append(quest.Hint).Append("</color></size>\n");
                    sb.Append("<size=20>お礼：<b>").Append(quest.RewardGold).Append(" G</b>");
                    if (quest.AcceptsRecipe)
                    {
                        int taught = Mathf.Max(1, Mathf.RoundToInt(quest.RewardGold * (quests != null ? quests.TeachRewardRate : 0.7f)));
                        sb.Append("<color=#7A5A38>（作り方を教えると ").Append(taught).Append(" G）</color>");
                    }
                    sb.Append("</size>");
                    if (held == null)
                    {
                        if (shareable != null)
                        {
                            check = $"○ 棚の「{shareable.potionName}」が役に立ちそう！ 話しかけて分け与えよう";
                            checkColor = good;
                        }
                        else if (teachable != null)
                        {
                            check = $"○ レシピ本の「{teachable.DisplayName}」の作り方を教えられそう！";
                            checkColor = good;
                        }
                        else
                        {
                            check = quest.AcceptsRecipe
                                ? "役に立ちそうな液体を探求しよう（棚の瓶を分け与えるか、作り方を教えられる）"
                                : "役に立ちそうな液体を探求しよう（できたら棚の瓶を分け与えられる）";
                            checkColor = UIColors.InkLight;
                        }
                    }
                    else if (satisfied && !held.IsFullBottle)
                    {
                        check = $"「{held.potionName}」は開けた瓶（{held.ml:0}ml）。満タンの瓶を持ってこよう";
                        checkColor = UIColors.InkLight;
                    }
                    else if (satisfied)
                    {
                        check = $"○「{held.potionName}」で力になれそう！ 話しかけて分け与えよう";
                        checkColor = good;
                    }
                    else
                    {
                        check = $"×「{held.potionName}」は、悩みに合わなさそう";
                        checkColor = new Color(0.62f, 0.22f, 0.12f);
                    }
                    break;
                case CustomerPhase.Offering when quest != null:
                    noteHeader.text = "<color=#B5651D>◆</color> お客さん";
                    sb.Append("<b>").Append(quest.ClientFullName).Append("</b> が\nカウンターで何か困っているようだ。話を聞いてみよう");
                    break;
                case CustomerPhase.Arriving:
                    noteHeader.text = "<color=#B5651D>◆</color> お客さん";
                    sb.Append("だれかがお店に入ってきた…");
                    break;
                case CustomerPhase.Leaving:
                    noteHeader.text = "<color=#B5651D>◆</color> 相談メモ";
                    sb.Append("お客さんが帰っていく。\n次のお客さんを待とう");
                    break;
                default:
                    noteHeader.text = "<color=#B5651D>◆</color> 相談メモ";
                    int done = progress != null ? progress.CompletedQuests : 0;
                    sb.Append("次のお客さんを待っています…\n");
                    sb.Append("<size=19><color=#7A5A38>待つあいだに、新しい調合を探求しよう</color></size>");
                    if (done > 0)
                    {
                        sb.Append("\n<size=19>これまでに解決した相談：").Append(done).Append(" 件");
                        if (progress != null && progress.TaughtCount > 0)
                            sb.Append("（分け与えた ").Append(progress.SharedCount).Append("・教えた ").Append(progress.TaughtCount).Append("）");
                        sb.Append("</size>");
                    }
                    break;
            }
            noteBody.text = sb.ToString();
            noteCheckBack.gameObject.SetActive(check != null);
            if (check != null)
            {
                noteCheck.text = check;
                noteCheck.color = checkColor;
            }
        }
    }
}
