using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>日付のアイキャッチの「間」（AtelierHUD のインスペクターで調整できる。単位は秒）</summary>
    [Serializable]
    public class DateCardTiming
    {
        [Tooltip("タイトルが暗がりに沈むまで")] public float coverSeconds = 1.0f;
        [Tooltip("暗くなってから、日付が浮かびはじめるまでの間")] public float beforeText = 0.5f;
        [Tooltip("日付が浮かび上がるまで")] public float textInSeconds = 1.4f;
        [Tooltip("日付を見せておく時間")] public float holdSeconds = 2.4f;
        [Tooltip("日付が消えるまで")] public float textOutSeconds = 1.0f;
        [Tooltip("暗がりが晴れて、調合机が見えてくるまで")] public float revealSeconds = 2.0f;
        [Tooltip("日付が浮かび上がるときに、下から上へ動く距離（ピクセル）")] public float rise = 14f;
        [Tooltip("クリック・Enter・Space で、日付を早めに切り上げられる")] public bool allowSkip = true;
    }

    /// <summary>
    /// 日付のアイキャッチ：タイトルで「はじめから」「つづきから」を選ぶと、画面が静かに暗くなり、
    /// 「4月12日」のように日付が浮かび上がって、数秒後に暗がりが晴れて調合机が現れる。
    ///
    /// 絵を差し替えるところ（Assets/CustomAssets/Images/UI/DateCard。UISkin の「日付のアイキャッチ」欄）：
    ///   date_background.png … 画面いっぱいの背景（1920×1080）。イラストに差し替えられる
    ///   date_ornament.png   … 日付の下の飾りの線（512×32）
    /// 重なりの順（奥 → 手前）：Background（背景の絵）→ Shade（文字を読みやすくする暗がり）→ Date（日付）→ Ornament（飾り）→ Sub・Detail（小さな文字）
    /// 絵が空欄なら、暗い単色の背景・細い線になる。
    /// </summary>
    public sealed class DateCardUI
    {
        private enum Phase { Closed, Cover, Wait, TextIn, Hold, TextOut, Reveal }

        private readonly GameObject root;
        private readonly CanvasGroup rootGroup;
        private readonly CanvasGroup textGroup;
        private readonly RectTransform textRoot;
        private readonly Image shade;
        private readonly Text dateText;
        private readonly Text subText;
        private readonly Text detailText;
        private readonly DateCardTiming timing;

        private Phase phase = Phase.Closed;
        private float t;

        public bool IsOpen => phase != Phase.Closed;
        /// <summary>画面がすっかり暗くなった（後ろでタイトルから調合机へ入れ替えてよい）</summary>
        public bool Covered => phase != Phase.Closed && phase != Phase.Cover;
        /// <summary>暗がりが晴れはじめた（調合机が見えてくる）</summary>
        public bool Revealing => phase == Phase.Reveal;
        /// <summary>これまでに出した回数・いま出している日付（確認用）</summary>
        public int ShownCount { get; private set; }
        public string DateText => dateText.text;

        public DateCardUI(RectTransform parent, DateCardTiming timing)
        {
            this.timing = timing ?? new DateCardTiming();
            UISkin skin = UIFactory.Skin;

            RectTransform rt = UIFactory.CreateRect("DateCard", parent);
            UIFactory.Stretch(rt);
            root = rt.gameObject;
            rootGroup = root.AddComponent<CanvasGroup>();

            // 画面の外（ウルトラワイドなどの余白）まで暗くする土台
            Image bleed = UIFactory.CreateImage(rt, "Bleed", null, new Color(0.02f, 0.015f, 0.02f, 1f));
            UIFactory.Stretch(bleed.rectTransform, -2000f, -2000f, -2000f, -2000f);
            bleed.raycastTarget = true;

            // 背景の絵（date_background.png）。空欄なら暗い単色
            Sprite bg = skin != null ? skin.dateBackground : null;
            Image background = UIFactory.CreateImage(rt, "Background", bg, bg != null ? Color.white : new Color(0.035f, 0.028f, 0.04f, 1f));
            UIFactory.Stretch(background.rectTransform);
            background.raycastTarget = false;

            // 文字を読みやすくする暗がり（明るいイラストに差し替えても、日付が読める。濃さは UISkin で調整）
            float shadeAlpha = skin != null ? skin.dateShade : 0.25f;
            shade = UIFactory.CreateImage(rt, "Shade", null, new Color(0f, 0f, 0f, bg != null ? shadeAlpha : 0f));
            UIFactory.Stretch(shade.rectTransform);
            shade.raycastTarget = false;

            textRoot = UIFactory.CreateRect("Text", rt);
            UIFactory.Stretch(textRoot);
            textGroup = textRoot.gameObject.AddComponent<CanvasGroup>();
            textGroup.blocksRaycasts = false;

            dateText = UIFactory.Label(textRoot, "Date", "", 104, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(dateText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 44f), new Vector2(1400f, 150f));
            dateText.raycastTarget = false;

            Sprite line = skin != null ? skin.dateOrnament : null;
            Image ornament = UIFactory.CreateImage(textRoot, "Ornament", line, line != null ? Color.white : new Color(0.86f, 0.78f, 0.64f, 0.55f));
            UIFactory.Place(ornament.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), line != null ? new Vector2(512f, 32f) : new Vector2(360f, 2f));
            ornament.raycastTarget = false;

            subText = UIFactory.Label(textRoot, "Sub", "", 36, UIColors.CreamDim, TextAnchor.MiddleCenter);
            UIFactory.Place(subText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -92f), new Vector2(1400f, 52f));
            subText.raycastTarget = false;

            detailText = UIFactory.Label(textRoot, "Detail", "", 24, new Color(0.7f, 0.62f, 0.5f, 1f), TextAnchor.MiddleCenter);
            UIFactory.Place(detailText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -142f), new Vector2(1400f, 36f));
            detailText.raycastTarget = false;

            root.SetActive(false);
        }

        /// <summary>日付を出しはじめる（date：「4月12日」／sub：「12日目の夜」／detail：小さなひとこと。空でもよい）</summary>
        public void Show(string date, string sub, string detail)
        {
            dateText.text = date ?? "";
            subText.text = sub ?? "";
            detailText.text = detail ?? "";
            root.SetActive(true);
            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = true;
            textGroup.alpha = 0f;
            phase = Phase.Cover;
            t = 0f;
            ShownCount++;
        }

        /// <summary>すぐに消す（タイトルへ戻ったときなど）</summary>
        public void Close()
        {
            phase = Phase.Closed;
            root.SetActive(false);
        }

        /// <summary>日付を早めに切り上げる（文字が消えて、調合机が現れる）</summary>
        public void Skip()
        {
            if (phase == Phase.Wait || phase == Phase.TextIn || phase == Phase.Hold)
            {
                // いま見えている濃さから、そのまま消えていく
                float a = textGroup.alpha;
                phase = Phase.TextOut;
                t = (1f - a) * Mathf.Max(0.01f, timing.textOutSeconds);
            }
        }

        public void Tick(float dt, bool allowInput)
        {
            if (phase == Phase.Closed) return;
            if (allowInput && timing.allowSkip &&
                (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
            {
                Skip();
            }
            t += dt;
            switch (phase)
            {
                case Phase.Cover:
                    rootGroup.alpha = Ease(t / Mathf.Max(0.01f, timing.coverSeconds));
                    if (t >= timing.coverSeconds) Next(Phase.Wait);
                    break;
                case Phase.Wait:
                    rootGroup.alpha = 1f;
                    if (t >= timing.beforeText) Next(Phase.TextIn);
                    break;
                case Phase.TextIn:
                {
                    float k = Ease(t / Mathf.Max(0.01f, timing.textInSeconds));
                    textGroup.alpha = k;
                    textRoot.anchoredPosition = new Vector2(0f, -timing.rise * (1f - k));
                    if (t >= timing.textInSeconds) Next(Phase.Hold);
                    break;
                }
                case Phase.Hold:
                    textGroup.alpha = 1f;
                    textRoot.anchoredPosition = Vector2.zero;
                    if (t >= timing.holdSeconds) Next(Phase.TextOut);
                    break;
                case Phase.TextOut:
                    textGroup.alpha = 1f - Ease(t / Mathf.Max(0.01f, timing.textOutSeconds));
                    if (t >= timing.textOutSeconds) Next(Phase.Reveal);
                    break;
                case Phase.Reveal:
                    textGroup.alpha = 0f;
                    rootGroup.alpha = 1f - Ease(t / Mathf.Max(0.01f, timing.revealSeconds));
                    // 半分ほど晴れたら、下の机を触れるようにする
                    if (rootGroup.alpha < 0.5f) rootGroup.blocksRaycasts = false;
                    if (t >= timing.revealSeconds) Close();
                    break;
            }
        }

        private void Next(Phase next)
        {
            phase = next;
            t = 0f;
        }

        /// <summary>ゆっくり始まって、ゆっくり終わる</summary>
        private static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
