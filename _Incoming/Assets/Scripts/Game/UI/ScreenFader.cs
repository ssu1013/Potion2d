using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面全体の暗転（場面の切り替え用）。一番手前のキャンバスに置く。
    /// 時間停止（ポーズ）中でも動くよう、実時間で進める。
    /// </summary>
    public sealed class ScreenFader
    {
        private readonly Image image;
        private int version;

        public float Alpha => image.color.a;
        public bool IsBusy { get; private set; }

        public ScreenFader(Transform parent)
        {
            var go = new GameObject("FaderCanvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            go.AddComponent<GraphicRaycaster>();

            image = UIFactory.CreateImage(go.transform, "Fader", null, new Color(0.035f, 0.022f, 0.014f, 1f));
            UIFactory.Stretch(image.rectTransform);
            SetAlpha(1f); // 起動直後は真っ暗から始める
        }

        public void SetAlpha(float alpha)
        {
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
            image.enabled = c.a > 0.001f;
            image.raycastTarget = c.a > 0.05f; // 暗転中はクリックを通さない
        }

        /// <summary>暗転する（画面が黒くなる）</summary>
        public IEnumerator FadeOut(float seconds)
        {
            return Fade(1f, seconds);
        }

        /// <summary>暗転から明ける</summary>
        public IEnumerator FadeIn(float seconds)
        {
            return Fade(0f, seconds);
        }

        private IEnumerator Fade(float target, float seconds)
        {
            int my = ++version;
            IsBusy = true;
            float start = Alpha;
            float t = 0f;
            while (t < seconds)
            {
                if (my != version) yield break; // 別のフェードが始まった
                // 読み込み直後の長いフレームで一気に終わらないよう、1フレームの進みに上限をつける
                t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, seconds));
                k = k * k * (3f - 2f * k);
                SetAlpha(Mathf.Lerp(start, target, k));
                yield return null;
            }
            if (my != version) yield break;
            SetAlpha(target);
            IsBusy = false;
        }
    }
}
