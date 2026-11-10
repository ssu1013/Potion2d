using System.Collections.Generic;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 温度計：フラスコの左に立つ（Step11 で、なくした見立てカードの BrewMeterUI2D.cs から分けた）。今の温度で液柱がのび、色が青 → 黄 → 赤に変わる。
    /// 目盛り：沸騰（100℃）は いつも。素材が熱で弱まりはじめた温度（赤）・力が目覚めた温度（金）は、一度起きると印が付く。
    /// 焦げているときは、ふるえて赤く光る。
    /// </summary>
    public sealed class BrewThermoUI2D
    {
        public struct Mark
        {
            public float temperature;
            public Color color;
        }

        private const int MaxMarks = 6;
        private readonly RectTransform root;
        private readonly RectTransform tubeArea;
        private readonly RectTransform fill;
        private readonly Image fillImage;
        private readonly Image bulbFill;
        private readonly Text tempText;
        private readonly Text heatText;
        private readonly RectTransform[] marks = new RectTransform[MaxMarks];
        private readonly Image[] markImages = new Image[MaxMarks];
        private readonly Text[] markLabels = new Text[MaxMarks];
        private readonly Vector2 basePosition;
        private readonly float bulbOffset;
        private float shownTemp = 20f;
        private float targetTemp = 20f;
        private float maxTemp = 130f;
        private bool scorching;
        private float time;

        public BrewThermoUI2D(RectTransform parent, Vector2 position, Vector2 size, Sprite tube)
        {
            basePosition = position;
            root = UIFactory.CreateRect("Thermometer", parent);
            UIFactory.Place(root, Vector2.zero, Vector2.zero, position, size);

            float bulb = size.x * 0.82f;
            bulbOffset = bulb * 0.2f;
            Image back = UIFactory.CreateImage(root, "Tube", tube != null ? tube : UIFactory.RoundedRect(new Color(0.1f, 0.08f, 0.07f, 0.75f), new Color(0.92f, 0.88f, 0.8f, 0.9f), 12, 3), Color.white);
            UIFactory.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, bulb * 0.55f), new Vector2(size.x * 0.46f, size.y - bulb * 0.55f - 34f));
            tubeArea = back.rectTransform;

            Image bulbBack = UIFactory.CreateImage(root, "Bulb", BrewUiSprites2D.Circle(), new Color(0.92f, 0.88f, 0.8f, 0.95f));
            UIFactory.Place(bulbBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(bulb, bulb));
            bulbFill = UIFactory.CreateImage(bulbBack.transform, "BulbFill", BrewUiSprites2D.Circle(), Color.white);
            UIFactory.Stretch(bulbFill.rectTransform, 5f, 5f, 5f, 5f);

            fillImage = UIFactory.CreateImage(tubeArea, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 5, 0), Color.white);
            fill = fillImage.rectTransform;
            UIFactory.Place(fill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -bulbOffset), new Vector2(size.x * 0.22f, 0f));

            for (int i = 0; i < MaxMarks; i++)
            {
                markImages[i] = UIFactory.CreateImage(tubeArea, "Mark_" + i, UIFactory.RoundedRect(Color.white, Color.white, 2, 0), Color.white);
                marks[i] = markImages[i].rectTransform;
                UIFactory.Place(marks[i], new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x * 0.75f, 3f));
                markLabels[i] = UIFactory.Label(marks[i], "Label", "", 14, Color.white, TextAnchor.MiddleRight, FontStyle.Bold, true);
                markLabels[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Place(markLabels[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(60f, 18f));
                marks[i].gameObject.SetActive(false);
            }

            tempText = UIFactory.Label(root, "Temperature", "20℃", 22, Color.white, TextAnchor.LowerCenter, FontStyle.Bold, true);
            tempText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(tempText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -10f), new Vector2(100f, 26f));
            heatText = UIFactory.Label(root, "Heat", "", 15, new Color(1f, 0.9f, 0.75f, 0.9f), TextAnchor.LowerCenter, FontStyle.Normal, true);
            heatText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(heatText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(100f, 20f));
        }

        public void Apply(float temperature, float maxTemperature, string heatLabel, bool isScorching, List<Mark> markList)
        {
            targetTemp = temperature;
            maxTemp = Mathf.Max(60f, maxTemperature);
            scorching = isScorching;
            heatText.text = heatLabel;
            for (int i = 0; i < MaxMarks; i++)
            {
                bool on = markList != null && i < markList.Count;
                if (marks[i].gameObject.activeSelf != on) marks[i].gameObject.SetActive(on);
                if (!on) continue;
                Mark m = markList[i];
                markImages[i].color = m.color;
                markLabels[i].text = Mathf.RoundToInt(m.temperature) + "℃";
                markLabels[i].color = Color.Lerp(m.color, Color.white, 0.25f);
                marks[i].anchoredPosition = new Vector2(0f, Y(m.temperature));
                // 近すぎる目盛りは、数字を出さない（重なって読めないので）
                bool crowded = false;
                for (int j = 0; j < i; j++) if (Mathf.Abs(markList[j].temperature - m.temperature) < maxTemp * 0.06f) crowded = true;
                markLabels[i].enabled = !crowded;
            }
        }

        public void Tick(float dt)
        {
            time += dt;
            shownTemp = Mathf.Lerp(shownTemp, targetTemp, 1f - Mathf.Exp(-5f * dt));
            // 液柱は球の中から、今の温度の目盛りまで
            fill.sizeDelta = new Vector2(fill.sizeDelta.x, Y(shownTemp) + bulbOffset);
            Color c = TempColor(shownTemp);
            if (scorching) c = Color.Lerp(c, new Color(1f, 0.15f, 0.1f, 1f), 0.5f + 0.5f * Mathf.Sin(time * 14f));
            fillImage.color = c;
            bulbFill.color = c;
            tempText.text = Mathf.RoundToInt(shownTemp) + "℃";
            tempText.color = scorching ? new Color(1f, 0.5f, 0.4f, 1f) : Color.white;
            root.anchoredPosition = scorching ? basePosition + new Vector2(Mathf.Sin(time * 47f) * 2.5f, 0f) : basePosition;
        }

        private float Y(float temperature)
        {
            float k = Mathf.Clamp01(temperature / maxTemp);
            return k * Mathf.Max(10f, tubeArea.rect.height - 8f);
        }

        /// <summary>温度の色：20℃ 青 → 60℃ 黄 → 100℃ 橙 → 130℃ 赤</summary>
        public static Color TempColor(float t)
        {
            if (t < 60f) return Color.Lerp(new Color(0.45f, 0.72f, 1f, 1f), new Color(1f, 0.85f, 0.35f, 1f), Mathf.InverseLerp(20f, 60f, t));
            if (t < 100f) return Color.Lerp(new Color(1f, 0.85f, 0.35f, 1f), new Color(1f, 0.55f, 0.2f, 1f), Mathf.InverseLerp(60f, 100f, t));
            return Color.Lerp(new Color(1f, 0.55f, 0.2f, 1f), new Color(0.95f, 0.25f, 0.15f, 1f), Mathf.InverseLerp(100f, 130f, t));
        }
    }
}
