using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>タイトル → 調合机の切り替え（AtelierHUD のインスペクターで調整できる。単位は秒。ロード画面は合計で 2〜3 秒が目安）</summary>
    [Serializable]
    public class LoadingTiming
    {
        [Tooltip("タイトルから遊びはじめるときに、ロード画面をはさむ（オフにすると、タイトルの1枚絵から直接、調合机へ静かにフェードする）")] public bool showLoadingScreen = true;
        [Tooltip("ロード画面をはさまないときに、タイトルの1枚絵が消えて調合机が見えてくるまで")] public float titleFadeOutSeconds = 1.2f;
        [Tooltip("タイトルが消えて、ロード画面の絵が出るまで")] public float fadeInSeconds = 0.6f;
        [Tooltip("ロード画面の絵を見せておく時間")] public float holdSeconds = 1.1f;
        [Tooltip("ロード画面が消えて、調合机が見えてくるまで")] public float fadeOutSeconds = 0.8f;
    }

    /// <summary>
    /// ロード画面：タイトルで「はじめから」「つづきから」を選ぶと、画面が1枚絵に切り替わり、
    /// 右下に小さく「Loading...」と出て、2〜3秒で調合机が現れる。
    ///
    /// 絵を差し替えるところ：Assets/CustomAssets/Images/UI/Loading/loading_background.png（1920×1080）を、
    /// 同じ名前の PNG で上書きするだけ（UISkin の「ロード画面」欄につないである）。絵が空欄なら、暗い単色になる。
    /// 重なりの順（奥 → 手前）：Background（1枚絵）→ Shade（暗がり。濃さは UISkin）→ Loading...（右下の小さな字）
    /// </summary>
    public sealed class LoadingScreenUI
    {
        private enum Phase { Closed, FadeIn, Hold, FadeOut }

        private readonly GameObject root;
        private readonly CanvasGroup rootGroup;
        private readonly Text label;
        private readonly LoadingTiming timing;

        private Phase phase = Phase.Closed;
        private float t;
        private float dots;

        public bool IsOpen => phase != Phase.Closed;
        /// <summary>絵が画面をすっかり覆った（後ろでタイトルから調合机へ入れ替えてよい）</summary>
        public bool Covered => phase == Phase.Hold || phase == Phase.FadeOut;
        /// <summary>消えはじめた（調合机が見えてくる）</summary>
        public bool Revealing => phase == Phase.FadeOut;
        /// <summary>これまでに出した回数（確認用）</summary>
        public int ShownCount { get; private set; }
        public string LabelText => label.text;

        public LoadingScreenUI(RectTransform parent, LoadingTiming timing)
        {
            this.timing = timing ?? new LoadingTiming();
            UISkin skin = UIFactory.Skin;

            RectTransform rt = UIFactory.CreateRect("LoadingScreen", parent);
            UIFactory.Stretch(rt);
            root = rt.gameObject;
            rootGroup = root.AddComponent<CanvasGroup>();

            // 画面の外（ウルトラワイドなどの余白）まで暗くする土台
            Image bleed = UIFactory.CreateImage(rt, "Bleed", null, new Color(0.02f, 0.015f, 0.02f, 1f));
            UIFactory.Stretch(bleed.rectTransform, -2000f, -2000f, -2000f, -2000f);
            bleed.raycastTarget = true;

            // 1枚絵（loading_background.png）。空欄なら暗い単色
            Sprite bg = skin != null ? skin.loadingBackground : null;
            Image background = UIFactory.CreateImage(rt, "Background", bg, bg != null ? Color.white : new Color(0.035f, 0.028f, 0.04f, 1f));
            UIFactory.Stretch(background.rectTransform);
            background.raycastTarget = false;

            // 絵の上に重ねる暗がり（濃さは UISkin。0 なら絵をそのまま見せる）
            float shadeAlpha = skin != null ? skin.loadingShade : 0f;
            Image shade = UIFactory.CreateImage(rt, "Shade", null, new Color(0f, 0f, 0f, bg != null ? shadeAlpha : 0f));
            UIFactory.Stretch(shade.rectTransform);
            shade.raycastTarget = false;

            // 右下の小さな「Loading...」（左ぞろえなので、点が増えても字が動かない）
            label = UIFactory.Label(rt, "Loading", "Loading...", 30, UIColors.CreamDim, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            UIFactory.Place(label.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 40f), new Vector2(190f, 44f));
            label.raycastTarget = false;

            root.SetActive(false);
        }

        /// <summary>ロード画面を出しはじめる</summary>
        public void Show()
        {
            root.SetActive(true);
            rootGroup.alpha = 0f;
            rootGroup.blocksRaycasts = true;
            label.text = "Loading";
            dots = 0f;
            phase = Phase.FadeIn;
            t = 0f;
            ShownCount++;
        }

        /// <summary>すぐに消す（タイトルへ戻ったときなど）</summary>
        public void Close()
        {
            phase = Phase.Closed;
            root.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (phase == Phase.Closed) return;
            t += dt;
            // 点が1つずつ増える（Loading → Loading. → Loading.. → Loading...）
            dots += dt * 3f;
            int n = (int)dots % 4;
            string text = n == 0 ? "Loading" : n == 1 ? "Loading." : n == 2 ? "Loading.." : "Loading...";
            if (label.text != text) label.text = text;
            switch (phase)
            {
                case Phase.FadeIn:
                    rootGroup.alpha = Ease(t / Mathf.Max(0.01f, timing.fadeInSeconds));
                    if (t >= timing.fadeInSeconds) Next(Phase.Hold);
                    break;
                case Phase.Hold:
                    rootGroup.alpha = 1f;
                    if (t >= timing.holdSeconds) Next(Phase.FadeOut);
                    break;
                case Phase.FadeOut:
                    rootGroup.alpha = 1f - Ease(t / Mathf.Max(0.01f, timing.fadeOutSeconds));
                    // 半分ほど消えたら、下の机を触れるようにする
                    if (rootGroup.alpha < 0.5f) rootGroup.blocksRaycasts = false;
                    if (t >= timing.fadeOutSeconds) Close();
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
