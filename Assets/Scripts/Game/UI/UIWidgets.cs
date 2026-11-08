using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>ボタン・スライダー・装飾スプライトなど、メニュー用の部品</summary>
    public static class UIWidgets
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>木の板のボタン（ホバーで少し大きくなり、音が鳴る）</summary>
        public static Button WoodButton(Transform parent, string label, int fontSize, Vector2 size, Action onClick)
        {
            Image bg = UIFactory.CreateImage(parent, "Button_" + label, UIFactory.ButtonSprite(), Color.white);
            bg.raycastTarget = true;
            UIFactory.Size(bg.gameObject, size.x, size.y);
            bg.rectTransform.sizeDelta = size;

            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // キー操作は MenuList で行う
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.82f);
            colors.pressedColor = new Color(0.85f, 0.78f, 0.65f);
            colors.disabledColor = new Color(0.55f, 0.5f, 0.45f, 0.75f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            Text text = UIFactory.Label(bg.transform, "Label", label, fontSize, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(text.rectTransform, 10f, 10f, 4f, 4f);
            bg.gameObject.AddComponent<UIButtonFx>();
            return button;
        }

        /// <summary>羊皮紙の上に置く横長スライダー</summary>
        public static Slider CreateSlider(Transform parent, float width, float value, Action<float> onChanged)
        {
            RectTransform root = UIFactory.CreateRect("Slider", parent);
            root.sizeDelta = new Vector2(width, 34f);
            UIFactory.Size(root.gameObject, width, 34f);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            Image back = UIFactory.CreateImage(root, "Background", UIFactory.RoundedRect(new Color(0.3f, 0.2f, 0.12f, 1f), UIColors.Outline, 9, 2), Color.white);
            UIFactory.Stretch(back.rectTransform, 0f, 0f, 9f, 9f);

            RectTransform fillArea = UIFactory.CreateRect("Fill Area", root);
            UIFactory.Stretch(fillArea, 4f, 4f, 12f, 12f);
            Image fill = UIFactory.CreateImage(fillArea, "Fill", UIFactory.RoundedRect(Color.white, Color.white, 6, 0), UIColors.Gold);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            RectTransform handleArea = UIFactory.CreateRect("Handle Slide Area", root);
            UIFactory.Stretch(handleArea, 14f, 14f, 0f, 0f);
            Image handle = UIFactory.CreateImage(handleArea, "Handle", Knob(), Color.white);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(34f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        /// <summary>金貨のアイコン</summary>
        public static Sprite Coin()
        {
            if (UIFactory.Skin != null && UIFactory.Skin.coin != null) return UIFactory.Skin.coin;
            const string key = "coin";
            if (cache.TryGetValue(key, out Sprite s)) return s;
            const int size = 64;
            var pixels = new Color[size * size];
            var gold = new Color(1f, 0.8f, 0.25f);
            var dark = new Color(0.75f, 0.5f, 0.12f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - 32f, y + 0.5f - 32f);
                    float d = p.magnitude;
                    Color c = Color.clear;
                    if (d < 29f)
                    {
                        c = d > 25f ? UIColors.Outline : (d > 21f ? dark : Color.Lerp(gold, Color.white, Mathf.Clamp01((p.y + p.x * -0.4f) / 40f) * 0.5f));
                        // 中央の「G」っぽい刻印（簡易）
                        if (d < 14f && d > 9f && !(p.x > 2f && p.y > -3f && p.y < 6f)) c = dark;
                        if (p.x > 1f && p.x < 12f && Mathf.Abs(p.y + 1f) < 2.2f) c = dark;
                        c.a = Mathf.Clamp01(29f - d);
                    }
                    pixels[y * size + x] = c;
                }
            }
            return Make(key, size, size, pixels, 0);
        }

        /// <summary>スライダーのつまみ</summary>
        public static Sprite Knob()
        {
            if (UIFactory.Skin != null && UIFactory.Skin.knob != null) return UIFactory.Skin.knob;
            const string key = "knob";
            if (cache.TryGetValue(key, out Sprite s)) return s;
            const int size = 48;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - 24f, y + 0.5f - 24f).magnitude;
                    Color c = d > 21f ? UIColors.Outline : Color.Lerp(UIColors.Cream, UIColors.ParchmentEdge, Mathf.Clamp01((24f - y) / 40f));
                    c.a = Mathf.Clamp01(24f - d);
                    pixels[y * size + x] = c;
                }
            }
            return Make(key, size, size, pixels, 0);
        }

        /// <summary>画面のふちを暗くする（タイトルなど）</summary>
        public static Sprite Vignette()
        {
            if (UIFactory.Skin != null && UIFactory.Skin.vignette != null) return UIFactory.Skin.vignette;
            const string key = "vignette";
            if (cache.TryGetValue(key, out Sprite s)) return s;
            const int size = 128;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f) * 2f;
                    float d = Mathf.Clamp01((p.magnitude - 0.45f) / 0.95f);
                    pixels[y * size + x] = new Color(0.05f, 0.03f, 0.02f, d * d * 0.85f);
                }
            }
            return Make(key, size, size, pixels, 0);
        }

        /// <summary>4方向に光る星（タイトルの飾り）</summary>
        public static Sprite Sparkle()
        {
            if (UIFactory.Skin != null && UIFactory.Skin.sparkle != null) return UIFactory.Skin.sparkle;
            const string key = "sparkle";
            if (cache.TryGetValue(key, out Sprite s)) return s;
            const int size = 64;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - 32f) / 32f;
                    float py = Mathf.Abs(y + 0.5f - 32f) / 32f;
                    // 細い十字の光 + 中心のにじみ
                    float star = Mathf.Clamp01(1f - (Mathf.Pow(px, 0.5f) + Mathf.Pow(py, 0.5f)) * 0.9f);
                    float glow = Mathf.Clamp01(1f - Mathf.Sqrt(px * px + py * py) * 2.2f);
                    float a = Mathf.Clamp01(star * 1.6f + glow * glow * 0.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Make(key, size, size, pixels, 0);
        }

        /// <summary>ふんわりした光の粒</summary>
        public static Sprite SoftDot()
        {
            if (UIFactory.Skin != null && UIFactory.Skin.softDot != null) return UIFactory.Skin.softDot;
            const string key = "softdot";
            if (cache.TryGetValue(key, out Sprite s)) return s;
            const int size = 64;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - 32f, y + 0.5f - 32f).magnitude / 32f;
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
            }
            return Make(key, size, size, pixels, 0);
        }

        /// <summary>左右に飾りのついた羊皮紙のリボン（見出し用）</summary>
        public static Sprite Ribbon()
        {
            return UIFactory.RoundedRect(UIColors.Parchment, UIColors.Outline, 10, 3);
        }

        private static Sprite Make(string key, int w, int h, Color[] pixels, int border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(pixels);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                          new Vector4(border, border, border, border));
            sprite.name = key;
            cache[key] = sprite;
            return sprite;
        }
    }

    /// <summary>ボタンのホバー演出と効果音。MenuList から選択状態（Selected）を受け取る</summary>
    public class UIButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>マウスが乗ったとき（MenuList が選択を合わせるのに使う）。null ならホバー音だけ鳴らす</summary>
        public Action Hovered;
        /// <summary>キーボードで選ばれている</summary>
        public bool Selected;

        private Button button;
        private bool pointerInside;
        private float current = 1f;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnDisable()
        {
            pointerInside = false;
            current = 1f;
            transform.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button != null && !button.interactable) return;
            pointerInside = true;
            if (Hovered != null)
            {
                Hovered();
                return;
            }
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiHover, 0.5f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (button != null && !button.interactable) return;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClick, 0.8f);
        }

        private void Update()
        {
            bool usable = button == null || button.interactable;
            float target = usable && (Selected || pointerInside) ? 1.06f : 1f;
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 0.8f);
            transform.localScale = new Vector3(current, current, 1f);
        }
    }

    /// <summary>
    /// ボタンの縦並びを、マウスとキーボード（↑↓/W・S で選ぶ、Enter/E/Space で決定）の両方で操作できるようにする
    /// </summary>
    public sealed class MenuList
    {
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Text> labels = new List<Text>();
        private readonly bool horizontal;
        private int index;

        public int Index => index;
        public int Count => buttons.Count;

        public MenuList(bool horizontal = false)
        {
            this.horizontal = horizontal;
        }

        public void Add(Button button)
        {
            int i = buttons.Count;
            buttons.Add(button);
            labels.Add(button.GetComponentInChildren<Text>());
            var fx = button.GetComponent<UIButtonFx>();
            if (fx != null) fx.Hovered = () => SetIndex(i, true);
        }

        public void Clear()
        {
            buttons.Clear();
            labels.Clear();
            index = 0;
        }

        /// <summary>最初の押せるボタンを選ぶ</summary>
        public void ResetSelection(int preferred = 0)
        {
            index = Mathf.Clamp(preferred, 0, Mathf.Max(0, buttons.Count - 1));
            if (buttons.Count > 0 && !Usable(index)) index = Step(index, 1);
            Refresh();
        }

        public void SetIndex(int i, bool sound)
        {
            if (i < 0 || i >= buttons.Count || !Usable(i)) return;
            if (i != index && sound)
            {
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null) AudioManager.Se(lib.uiHover, 0.5f);
            }
            index = i;
            Refresh();
        }

        /// <summary>毎フレーム呼ぶ。決定したら true</summary>
        public bool Tick()
        {
            if (buttons.Count == 0) return false;
            int dir = 0;
            if (horizontal)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dir = -1;
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dir = 1;
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;
            }
            if (dir != 0) SetIndex(Step(index, dir), true);

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E))
            {
                return Activate(index);
            }
            return false;
        }

        /// <summary>指定のボタンを押したことにする（クリック音つき）</summary>
        public bool Activate(int i)
        {
            if (i < 0 || i >= buttons.Count || !Usable(i)) return false;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClick, 0.8f);
            buttons[i].onClick.Invoke();
            return true;
        }

        private bool Usable(int i)
        {
            Button b = buttons[i];
            return b != null && b.interactable && b.gameObject.activeInHierarchy;
        }

        private int Step(int from, int dir)
        {
            int i = from;
            for (int n = 0; n < buttons.Count; n++)
            {
                i = (i + dir + buttons.Count) % buttons.Count;
                if (Usable(i)) return i;
            }
            return from;
        }

        private void Refresh()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool selected = i == index;
                var fx = buttons[i].GetComponent<UIButtonFx>();
                if (fx != null) fx.Selected = selected;
                if (labels[i] != null)
                {
                    labels[i].color = !buttons[i].interactable ? new Color(0.75f, 0.68f, 0.58f) : selected ? UIColors.Gold : UIColors.Cream;
                }
            }
        }
    }

    /// <summary>文字やイメージに縦グラデーションをかける（タイトルロゴ用）</summary>
    public class UIGradient : BaseMeshEffect
    {
        public Color top = Color.white;
        public Color bottom = Color.gray;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var vertex = new UIVertex();
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                minY = Mathf.Min(minY, vertex.position.y);
                maxY = Mathf.Max(maxY, vertex.position.y);
            }
            float range = Mathf.Max(0.001f, maxY - minY);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color c = Color.Lerp(bottom, top, (vertex.position.y - minY) / range);
                c.a *= vertex.color.a / 255f;
                vertex.color = c;
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
