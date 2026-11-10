using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>見立てカードに出す中身（BrewFeedback2D が毎回作る）</summary>
    public sealed class MeterModel
    {
        public sealed class Row
        {
            public PotionAttribute attribute;
            public string name = "";
            public Color color = Color.white;
            /// <summary>今の値（100ml あたり）</summary>
            public float current;
            /// <summary>溶け残りが溶けきったときの予想</summary>
            public float projected;
            public bool hasMin, hasMax;
            public float min, max;
            /// <summary>目標の線に、今の値が届いている</summary>
            public bool met;
            /// <summary>溶かしきれば届く</summary>
            public bool metProjected;
        }

        public readonly List<Row> rows = new List<Row>();
        public string target = "";
        public string dissolve = "";
        public float dissolveFraction = 1f;
        public bool showDissolve;
        public string conditions = "";
        public string status = "";
        public Color statusColor = UIColors.InkLight;
    }

    /// <summary>
    /// 見立てカード（Step4-A）：フラスコの中の属性を、棒グラフで見せる。
    ///   ■ 塗った棒 … 今の値　▨ 斜線 … 溶け残りが溶けきったら、ここまで行く（予告）
    /// 下に、ひとことの見立て（溶け具合・次の素材が届くまでの発見の数）を出す。値の変化は、なめらかに動く。
    /// （棒に目標の線を引く仕組み（hasMin・hasMax）も残してある。今のゲームでは使っていない）
    /// </summary>
    public sealed class BrewMeterUI2D
    {
        public const int MaxRows = 5;
        private const float RowHeight = 44f;
        private const float BarWidth = 176f;
        private const float BarHeight = 18f;
        private const float RowsTop = 100f;

        private sealed class RowView
        {
            public GameObject root;
            public RectTransform rect;
            public Text name;
            public Text value;
            public RectTransform ghost;
            public Image ghostImage;
            public RectTransform fill;
            public Image fillImage;
            public RectTransform zone;
            public Image zoneImage;
            public RectTransform markerMin;
            public RectTransform markerMax;
            public Text markerLabel;
            public float shownCurrent;
            public float shownProjected;
            public MeterModel.Row data;
        }

        private readonly RectTransform card;
        private readonly Image glow;
        private readonly Text target;
        private readonly Text dissolveText;
        private readonly RectTransform dissolveFill;
        private readonly GameObject dissolveRoot;
        private readonly Text conditions;
        private readonly Text status;
        private readonly RowView[] rows = new RowView[MaxRows];
        private readonly Sprite barFillSprite;
        private float scale = 20f;
        private float shownScale = 20f;
        private float glowTime = 10f;
        private Color glowColor = UIColors.Gold;
        private int visibleRows;

        public RectTransform Rect => card;

        public BrewMeterUI2D(RectTransform parent, Vector2 position, Vector2 size, Sprite panel, Sprite barBack, Sprite barFill)
        {
            card = UIFactory.CreateRect("MeterCard", parent);
            UIFactory.Place(card, Vector2.zero, Vector2.zero, position, size);

            glow = UIFactory.CreateImage(card, "Glow", UIFactory.RoundedRect(new Color(1f, 1f, 1f, 0f), Color.white, 18, 6), new Color(1f, 0.85f, 0.4f, 0f));
            UIFactory.Stretch(glow.rectTransform, -8f, -8f, -8f, -8f);

            Image paper = UIFactory.CreateImage(card, "Paper", panel != null ? panel : UIFactory.Parchment(), UIFactory.Paper);
            UIFactory.Stretch(paper.rectTransform);

            Text title = UIFactory.Label(card, "Title", "見立て", 26, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(150f, 34f));

            // 溶け具合（右上）
            dissolveRoot = UIFactory.CreateRect("Dissolve", card).gameObject;
            var dr = (RectTransform)dissolveRoot.transform;
            UIFactory.Place(dr, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -12f), new Vector2(150f, 34f));
            dissolveText = UIFactory.Label(dr, "Text", "", 17, UIColors.InkLight, TextAnchor.UpperRight);
            UIFactory.Place(dissolveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(150f, 22f));
            Image dBack = UIFactory.CreateImage(dr, "Back", UIFactory.RoundedRect(Color.white, Color.white, 4, 0), new Color(0.3f, 0.2f, 0.12f, 0.35f));
            UIFactory.Place(dBack.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 2f), new Vector2(110f, 7f));
            Image dFill = UIFactory.CreateImage(dBack.transform, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 4, 0), new Color(0.45f, 0.62f, 0.3f, 1f));
            dissolveFill = dFill.rectTransform;
            UIFactory.Place(dissolveFill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 7f));

            target = UIFactory.Label(card, "Target", "", 17, UIColors.InkLight, TextAnchor.UpperLeft);
            UIFactory.Place(target.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(size.x - 36f, 22f));

            // 見方（はじめての人にも、塗り・斜線・点線の意味がわかるように。記号の字はフォントにないことがあるので、小さな絵で見せる）
            RectTransform legend = UIFactory.CreateRect("Legend", card);
            UIFactory.Place(legend, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -70f), new Vector2(size.x - 36f, 20f));
            Color legendColor = new Color(0.55f, 0.38f, 0.22f, 1f);
            LegendItem(legend, 0f, UIFactory.RoundedRect(Color.white, Color.white, 3, 0), Image.Type.Sliced, legendColor, new Vector2(16f, 10f), "今");
            LegendItem(legend, 58f, BrewUiSprites2D.Stripes(), Image.Type.Tiled, Color.Lerp(legendColor, Color.white, 0.15f), new Vector2(16f, 10f), "溶かしきると");

            barFillSprite = barFill != null ? barFill : UIFactory.RoundedRect(Color.white, Color.white, 6, 0);
            Sprite back = barBack != null ? barBack : UIFactory.RoundedRect(new Color(0.22f, 0.14f, 0.08f, 0.9f), new Color(0.55f, 0.4f, 0.24f, 1f), 7, 2);
            for (int i = 0; i < MaxRows; i++) rows[i] = BuildRow(i, back);

            conditions = UIFactory.Label(card, "Conditions", "", 17, UIColors.Ink, TextAnchor.UpperLeft);
            conditions.lineSpacing = 1.1f;
            UIFactory.Place(conditions.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -RowsTop), new Vector2(size.x - 36f, 70f));

            status = UIFactory.Label(card, "Status", "", 19, UIColors.InkLight, TextAnchor.LowerLeft, FontStyle.Bold);
            status.lineSpacing = 1.05f;
            UIFactory.Place(status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 14f), new Vector2(size.x - 36f, 58f));
        }

        private RowView BuildRow(int index, Sprite backSprite)
        {
            var v = new RowView();
            v.rect = UIFactory.CreateRect("Row_" + index, card);
            UIFactory.Place(v.rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -RowsTop - index * RowHeight), new Vector2(324f, RowHeight));
            v.root = v.rect.gameObject;

            v.name = UIFactory.Label(v.rect, "Name", "", 19, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(v.name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -4f), new Vector2(66f, 26f));

            Image back = UIFactory.CreateImage(v.rect, "Bar", backSprite, Color.white);
            UIFactory.Place(back.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, -6f), new Vector2(BarWidth, BarHeight));
            RectTransform bar = back.rectTransform;

            v.zoneImage = UIFactory.CreateImage(bar, "Zone", UIFactory.RoundedRect(Color.white, Color.white, 4, 0), new Color(0.4f, 0.75f, 0.35f, 0f));
            v.zone = v.zoneImage.rectTransform;
            UIFactory.Place(v.zone, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, BarHeight - 4f));

            v.ghostImage = UIFactory.CreateImage(bar, "Projected", BrewUiSprites2D.Stripes(), Color.white);
            v.ghostImage.type = Image.Type.Tiled;
            v.ghost = v.ghostImage.rectTransform;
            UIFactory.Place(v.ghost, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, BarHeight - 6f));

            v.fillImage = UIFactory.CreateImage(bar, "Current", barFillSprite, Color.white);
            v.fill = v.fillImage.rectTransform;
            UIFactory.Place(v.fill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, BarHeight - 6f));

            v.markerMin = Marker(bar, "TargetMin");
            v.markerMax = Marker(bar, "TargetMax");
            v.markerLabel = UIFactory.Label(bar, "TargetValue", "", 13, new Color(0.62f, 0.45f, 0.05f, 1f), TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(v.markerLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 1f), new Vector2(48f, 16f));

            v.value = UIFactory.Label(v.rect, "Value", "", 19, UIColors.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            v.value.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(v.value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(74f, 26f));
            v.root.SetActive(false);
            return v;
        }

        private static void LegendItem(RectTransform parent, float x, Sprite sprite, Image.Type type, Color color, Vector2 size, string label)
        {
            Image swatch = UIFactory.CreateImage(parent, "Swatch_" + label, sprite, color);
            swatch.type = type;
            UIFactory.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), size);
            Text t = UIFactory.Label(parent, "Label_" + label, label, 15, UIColors.InkLight, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x + size.x + 5f, 0f), new Vector2(110f, 20f));
        }

        private static RectTransform Marker(RectTransform bar, string name)
        {
            Image m = UIFactory.CreateImage(bar, name, BrewUiSprites2D.Dashes(), new Color(0.85f, 0.62f, 0.1f, 1f));
            m.type = Image.Type.Tiled;
            UIFactory.Place(m.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, BarHeight + 14f));
            m.gameObject.SetActive(false);
            return m.rectTransform;
        }

        /// <summary>新しい中身を入れる（棒の長さは Tick でなめらかに追いつく）</summary>
        public void Apply(MeterModel m)
        {
            target.text = m.target;
            dissolveRoot.SetActive(m.showDissolve);
            dissolveText.text = m.dissolve;
            dissolveFill.sizeDelta = new Vector2(110f * Mathf.Clamp01(m.dissolveFraction), 7f);
            conditions.text = m.conditions;
            status.text = m.status;
            status.color = m.statusColor;

            float top = 1f;
            visibleRows = Mathf.Min(MaxRows, m.rows.Count);
            for (int i = 0; i < MaxRows; i++)
            {
                RowView v = rows[i];
                bool on = i < visibleRows;
                if (v.root.activeSelf != on) v.root.SetActive(on);
                if (!on) continue;
                MeterModel.Row r = m.rows[i];
                if (v.data == null || v.data.attribute != r.attribute)
                {
                    v.shownCurrent = r.current;
                    v.shownProjected = r.projected;
                }
                v.data = r;
                v.name.text = r.name;
                v.fillImage.color = r.color;
                Color g = Color.Lerp(r.color, Color.white, 0.15f);
                g.a = 0.75f;
                v.ghostImage.color = g;
                top = Mathf.Max(top, r.current, r.projected, r.hasMin ? r.min * 1.3f : 0f, r.hasMax ? r.max * 1.25f : 0f);
            }
            scale = NiceCeil(Mathf.Max(20f, top * 1.08f));
            // 条件の行は、見えている棒のすぐ下へ
            conditions.rectTransform.anchoredPosition = new Vector2(18f, -RowsTop - visibleRows * RowHeight - 4f);
        }

        /// <summary>目標に届いたときなどに、カードのふちを光らせる</summary>
        public void Glow(Color color)
        {
            glowColor = color;
            glowTime = 0f;
        }

        public void Tick(float dt)
        {
            shownScale = Mathf.Lerp(shownScale, scale, 1f - Mathf.Exp(-4f * dt));
            float k = 1f - Mathf.Exp(-8f * dt);
            for (int i = 0; i < visibleRows; i++)
            {
                RowView v = rows[i];
                MeterModel.Row r = v.data;
                if (r == null) continue;
                v.shownCurrent = Mathf.Lerp(v.shownCurrent, r.current, k);
                v.shownProjected = Mathf.Lerp(v.shownProjected, r.projected, k);
                float inner = BarWidth - 4f;
                float cur = Mathf.Clamp01(v.shownCurrent / shownScale) * inner;
                float proj = Mathf.Clamp01(Mathf.Max(v.shownProjected, v.shownCurrent) / shownScale) * inner;
                v.fill.sizeDelta = new Vector2(cur, BarHeight - 6f);
                v.ghost.sizeDelta = new Vector2(proj, BarHeight - 6f);

                // 目標の線（以上 → 点線、以下 → 点線から右が赤っぽい、範囲 → 間が緑っぽい）
                SetMarker(v.markerMin, r.hasMin, r.min, inner);
                SetMarker(v.markerMax, r.hasMax, r.max, inner);
                Color zoneColor = new Color(0f, 0f, 0f, 0f);
                float zx = 0f, zw = 0f;
                if (r.hasMin && r.hasMax)
                {
                    zx = X(r.min, inner);
                    zw = X(r.max, inner) - zx;
                    zoneColor = new Color(0.45f, 0.8f, 0.4f, 0.28f);
                }
                else if (r.hasMax)
                {
                    zx = X(r.max, inner);
                    zw = inner - zx;
                    zoneColor = new Color(0.85f, 0.3f, 0.2f, 0.25f);
                }
                v.zone.anchoredPosition = new Vector2(2f + zx, 0f);
                v.zone.sizeDelta = new Vector2(Mathf.Max(0f, zw), BarHeight - 4f);
                v.zoneImage.color = zoneColor;
                bool showLabel = r.hasMin || r.hasMax;
                v.markerLabel.gameObject.SetActive(showLabel);
                if (showLabel)
                {
                    float tv = r.hasMin ? r.min : r.max;
                    v.markerLabel.text = tv.ToString("0.#");
                    v.markerLabel.rectTransform.anchoredPosition = new Vector2(2f + X(tv, inner), 1f);
                    v.markerLabel.color = r.met ? new Color(0.25f, 0.55f, 0.2f, 1f) : new Color(0.62f, 0.45f, 0.05f, 1f);
                }

                // 数字：今の値（溶かしきると増えるなら、その先も小さく）
                string text = v.shownCurrent.ToString("0.0");
                if (r.projected - r.current > 0.3f) text += $"<size=14><color=#8C6A45> →{r.projected:0.#}</color></size>";
                v.value.text = text;
                v.value.color = r.met ? new Color(0.2f, 0.5f, 0.18f, 1f) : UIColors.Ink;
            }

            glowTime += dt;
            float a = glowTime < 1.6f ? Mathf.Sin(Mathf.Clamp01(glowTime / 1.6f) * Mathf.PI) : 0f;
            Color gc = glowColor;
            gc.a = a * 0.95f;
            glow.color = gc;
        }

        private float X(float value, float inner)
        {
            return Mathf.Clamp01(value / Mathf.Max(0.01f, shownScale)) * inner;
        }

        private void SetMarker(RectTransform marker, bool on, float value, float inner)
        {
            if (marker.gameObject.activeSelf != on) marker.gameObject.SetActive(on);
            if (on) marker.anchoredPosition = new Vector2(2f + X(value, inner), 0f);
        }

        /// <summary>目盛りの上限を、きりのいい数にする（20・25・30・40・50・60・80・100…）</summary>
        private static float NiceCeil(float v)
        {
            float[] steps = { 20f, 25f, 30f, 40f, 50f, 60f, 80f, 100f, 120f, 150f, 200f, 250f, 300f, 400f, 500f };
            foreach (float s in steps) if (v <= s) return s;
            return Mathf.Ceil(v / 100f) * 100f;
        }
    }

    /// <summary>
    /// 温度計（Step4-A）：フラスコの左に立つ。今の温度で液柱がのび、色が青 → 黄 → 赤に変わる。
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
