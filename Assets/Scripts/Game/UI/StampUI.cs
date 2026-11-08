using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 発見のスタンプ（新発見・性質を発見・反応を発見・役に立った・教えた・すべて発見）。
    /// 絵は UISkin の「発見・素材ノート」（Assets/Art2D/UI/Discovery/stamp_*.png）。空ならコードで描いた赤い輪。
    /// 文字はスタンプの上に重ねて出す（スタンプの絵に文字を描き込んだら、UISkin の「スタンプに文字を重ねる」をオフにする）。
    /// </summary>
    public static class StampUI
    {
        public static readonly Color Red = new Color(0.78f, 0.17f, 0.12f, 0.95f);
        public static readonly Color Green = new Color(0.18f, 0.5f, 0.2f, 0.95f);
        public static readonly Color Gold = new Color(0.78f, 0.55f, 0.08f, 0.95f);

        private static Sprite ring;

        /// <summary>スタンプを作る（size は外枠の大きさ。angle 度だけ傾ける）</summary>
        public static RectTransform Create(Transform parent, string name, Sprite sprite, string label, Vector2 size, float angle, Color color)
        {
            RectTransform rt = UIFactory.CreateRect(name, parent);
            rt.sizeDelta = size;
            Image art = UIFactory.CreateImage(rt, "Art", sprite != null ? sprite : Ring(), sprite != null ? Color.white : color);
            art.preserveAspect = true;
            UIFactory.Stretch(art.rectTransform);
            Text text = UIFactory.Label(rt, "Label", label ?? "", Mathf.Max(12, Mathf.RoundToInt(size.y * 0.2f)), color, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(text.rectTransform);
            text.enabled = ShowLabels && !string.IsNullOrEmpty(label);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            return rt;
        }

        /// <summary>スタンプの絵と文字を入れ替える</summary>
        public static void Set(RectTransform stamp, Sprite sprite, string label, Color color)
        {
            if (stamp == null) return;
            Image art = stamp.Find("Art") != null ? stamp.Find("Art").GetComponent<Image>() : null;
            Text text = stamp.Find("Label") != null ? stamp.Find("Label").GetComponent<Text>() : null;
            if (art != null)
            {
                art.sprite = sprite != null ? sprite : Ring();
                art.color = sprite != null ? Color.white : color;
            }
            if (text != null)
            {
                text.text = label ?? "";
                text.color = color;
                text.enabled = ShowLabels && !string.IsNullOrEmpty(label);
            }
        }

        /// <summary>スタンプを押すときの動き（t = 押してからの秒数。大きく → ぽんっと縮む）</summary>
        public static void Slam(RectTransform stamp, float t, float baseAngle)
        {
            if (stamp == null) return;
            float s = t < 0f ? 0f : t < 0.12f ? Mathf.Lerp(1.9f, 0.92f, t / 0.12f) : Mathf.Lerp(0.92f, 1f, Mathf.Clamp01((t - 0.12f) / 0.1f));
            stamp.localScale = Vector3.one * s;
            stamp.gameObject.SetActive(t >= 0f);
            stamp.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
        }

        private static bool ShowLabels => UIFactory.Skin == null || UIFactory.Skin.drawStampLabels;

        private static Sprite Ring()
        {
            if (ring != null) return ring;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "StampRing" };
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                    float outer = Mathf.Clamp01(Mathf.Min(r - (half - 10f), (half - 3f) - r) + 0.5f);
                    float inner = Mathf.Clamp01(Mathf.Min(r - (half - 17f), (half - 14f) - r) + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(outer, inner) * 0.95f);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            ring = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return ring;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ring = null;
        }
    }
}
