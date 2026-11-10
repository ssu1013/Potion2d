using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// タイトル画面。背景はアトリエをゆっくり映すカメラ（TitleCameraRig）、手前にロゴとメニュー。
    /// ロゴ → サブタイトル → ボタンの順に出てくる。光の粒がふわふわ昇っていく。
    /// </summary>
    public sealed class TitleScreenUI
    {
        private readonly GameObject root;
        private readonly CanvasGroup rootGroup;
        private readonly RectTransform logoGroup;
        private readonly CanvasGroup logoCanvas;
        private readonly RectTransform ribbon;
        private readonly CanvasGroup ribbonCanvas;
        private readonly RectTransform potionLeft;
        private readonly RectTransform potionRight;
        private readonly List<Image> sparkles = new List<Image>();
        private readonly List<Mote> motes = new List<Mote>();
        private readonly List<RectTransform> buttonRects = new List<RectTransform>();
        private readonly List<CanvasGroup> buttonGroups = new List<CanvasGroup>();
        private readonly MenuList menu = new MenuList();
        private readonly Button continueButton;
        private readonly Text saveInfo;
        private readonly CanvasGroup saveInfoGroup;

        private readonly GameObject confirmRoot;
        private readonly MenuList confirmMenu = new MenuList(true);

        private readonly Action onNewGame;
        private readonly Action onContinue;
        private readonly Action onSettings;
        private readonly Action onQuit;

        private float time;
        private bool hasSave;
        private bool locked;

        private struct Mote
        {
            public RectTransform rect;
            public Image image;
            public float speed;
            public float phase;
            public float x;
            public float size;
        }

        public bool IsOpen => root.activeSelf;
        public bool IsConfirmOpen => confirmRoot.activeSelf;

        public TitleScreenUI(RectTransform parent, Action onNewGame, Action onContinue, Action onSettings, Action onQuit)
        {
            this.onNewGame = onNewGame;
            this.onContinue = onContinue;
            this.onSettings = onSettings;
            this.onQuit = onQuit;

            RectTransform overlay = UIFactory.CreateRect("TitleScreen", parent);
            UIFactory.Stretch(overlay);
            root = overlay.gameObject;
            rootGroup = root.AddComponent<CanvasGroup>();

            // ふちを暗く、下のほうも少し暗く（ボタンを読みやすく）
            Image vignette = UIFactory.CreateImage(overlay, "Vignette", UIWidgets.Vignette(), Color.white);
            UIFactory.Stretch(vignette.rectTransform, -80f, -80f, -80f, -80f);
            Image lowShade = UIFactory.CreateImage(overlay, "LowShade", UIWidgets.SoftDot(), new Color(0.04f, 0.025f, 0.015f, 0.6f));
            UIFactory.Place(lowShade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(1100f, 760f));

            // 光の粒
            RectTransform moteLayer = UIFactory.CreateRect("Motes", overlay);
            UIFactory.Stretch(moteLayer);
            for (int i = 0; i < 34; i++)
            {
                Image dot = UIFactory.CreateImage(moteLayer, "Mote" + i, UIWidgets.SoftDot(), new Color(1f, 0.85f, 0.5f, 0.5f));
                RectTransform rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                float size = UnityEngine.Random.Range(8f, 26f);
                rt.sizeDelta = new Vector2(size, size);
                motes.Add(new Mote
                {
                    rect = rt,
                    image = dot,
                    speed = UnityEngine.Random.Range(18f, 46f),
                    phase = UnityEngine.Random.Range(0f, 1200f),
                    x = UnityEngine.Random.Range(0f, 1f),
                    size = size,
                });
            }

            // ロゴ
            logoGroup = UIFactory.CreateRect("Logo", overlay);
            UIFactory.Place(logoGroup, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(1400f, 300f));
            logoCanvas = logoGroup.gameObject.AddComponent<CanvasGroup>();

            Image glow = UIFactory.CreateImage(logoGroup, "Glow", UIWidgets.SoftDot(), new Color(1f, 0.75f, 0.35f, 0.28f));
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1500f, 420f));

            Text small = UIFactory.Label(logoGroup, "Kicker", "見習い錬金術師のアトリエ物語", 30, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Place(small.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 128f), new Vector2(1000f, 50f));

            Text logo = UIFactory.Label(logoGroup, "Title", "ポーション・フロンティア", 118, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(logo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1400f, 160f));
            var gradient = logo.gameObject.AddComponent<UIGradient>();
            gradient.top = new Color(1f, 0.96f, 0.72f);
            gradient.bottom = new Color(0.98f, 0.62f, 0.22f);
            var outline = logo.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.11f, 0.05f, 1f);
            outline.effectDistance = new Vector2(4f, -4f);
            var outline2 = logo.gameObject.AddComponent<Outline>();
            outline2.effectColor = new Color(0.22f, 0.11f, 0.05f, 1f);
            outline2.effectDistance = new Vector2(-3f, 3f);
            var drop = logo.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.55f);
            drop.effectDistance = new Vector2(0f, -10f);

            // 左右のポーション
            potionLeft = Potion(logoGroup, "PotionL", new Color(0.35f, 0.85f, 0.45f), new Vector2(-830f, 20f), 12f);
            potionRight = Potion(logoGroup, "PotionR", new Color(0.55f, 0.45f, 0.95f), new Vector2(830f, 20f), -12f);

            // キラキラ
            Vector2[] sparklePositions =
            {
                new Vector2(-640f, 110f), new Vector2(-520f, -60f), new Vector2(560f, 100f),
                new Vector2(660f, -70f), new Vector2(-120f, 120f), new Vector2(300f, -80f),
            };
            for (int i = 0; i < sparklePositions.Length; i++)
            {
                Image s = UIFactory.CreateImage(logoGroup, "Sparkle" + i, UIWidgets.Sparkle(), new Color(1f, 0.95f, 0.75f, 1f));
                UIFactory.Place(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), sparklePositions[i], new Vector2(48f, 48f));
                sparkles.Add(s);
            }

            // サブタイトルのリボン
            Image ribbonImage = UIFactory.CreateImage(overlay, "Ribbon", UIFactory.WoodPanel(), Color.white);
            ribbon = ribbonImage.rectTransform;
            UIFactory.Place(ribbon, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -432f), new Vector2(760f, 78f));
            ribbonCanvas = ribbonImage.gameObject.AddComponent<CanvasGroup>();
            Text sub = UIFactory.Label(ribbon, "Subtitle", "〜 注いで育てる錬金農園 〜", 40, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
            UIFactory.Stretch(sub.rectTransform);

            // メニュー
            RectTransform menuRoot = UIFactory.CreateRect("Menu", overlay);
            UIFactory.Place(menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(460f, 420f));
            UIFactory.Vertical(menuRoot.gameObject, new RectOffset(0, 0, 0, 0), 16f, TextAnchor.UpperCenter);

            AddButton(menuRoot, "はじめから", () => OnNewGamePressed());
            continueButton = AddButton(menuRoot, "つづきから", () => { if (!locked && onContinue != null) { locked = true; onContinue(); } });
            AddButton(menuRoot, "せってい", () => { if (onSettings != null) onSettings(); });
            AddButton(menuRoot, "おわる", () => { if (onQuit != null) onQuit(); });

            saveInfo = UIFactory.Label(overlay, "SaveInfo", "", 22, UIColors.CreamDim, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            UIFactory.Place(saveInfo.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(1200f, 40f));
            saveInfoGroup = saveInfo.gameObject.AddComponent<CanvasGroup>();

            Text help = UIFactory.Label(overlay, "Help", "↑↓ / マウスで選ぶ　　Enter / クリックで決定", 20, UIColors.CreamDim, TextAnchor.LowerLeft, FontStyle.Normal, true);
            UIFactory.Place(help.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 28f), new Vector2(800f, 40f));
            Text version = UIFactory.Label(overlay, "Version", "試作版 v0.3", 20, UIColors.CreamDim, TextAnchor.LowerRight, FontStyle.Normal, true);
            UIFactory.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 28f), new Vector2(400f, 40f));

            confirmRoot = BuildConfirm(overlay);
            root.SetActive(false);
        }

        public void Show(bool saveExists, SaveData save)
        {
            hasSave = saveExists;
            locked = false;
            time = 0f;
            root.SetActive(true);
            confirmRoot.SetActive(false);
            continueButton.interactable = saveExists;
            menu.ResetSelection(saveExists ? 1 : 0);

            if (saveExists && save != null)
            {
                int liquids = save.liquids != null ? save.liquids.Count : 0;
                int onDesk = save.unlockedIngredients != null ? save.unlockedIngredients.Count : 0;
                saveInfo.text = $"つづきから：{save.savedAt}　見つけた液体 {liquids} 種類　机の素材 {onDesk} 種類";
            }
            else
            {
                saveInfo.text = "";
            }
            Animate(0f);
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        /// <summary>毎フレーム呼ぶ（HUD から）。メニューの操作を受け付けるときは allowInput = true</summary>
        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            time += dt;
            Animate(dt);

            if (!allowInput || locked || time < 1.0f) return;
            if (IsConfirmOpen)
            {
                confirmMenu.Tick();
                return;
            }
            menu.Tick();
        }

        public void CloseConfirm()
        {
            confirmRoot.SetActive(false);
        }

        /// <summary>自動テスト用：はじめからを選ぶ（確認なし）</summary>
        public void ForceNewGame()
        {
            if (locked) return;
            locked = true;
            if (onNewGame != null) onNewGame();
        }

        // ------------------------------------------------------------------

        private void OnNewGamePressed()
        {
            if (locked) return;
            if (hasSave)
            {
                confirmRoot.SetActive(true);
                confirmMenu.ResetSelection(1); // 「いいえ」を先に選んでおく
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null) AudioManager.Se(lib.uiOpen, 0.7f);
                return;
            }
            ForceNewGame();
        }

        private void Animate(float dt)
        {
            float t = time;
            // ロゴ：ふわっと拡大しながら出る
            float logoIn = Ease(Mathf.Clamp01(t / 1.3f));
            logoCanvas.alpha = logoIn;
            float scale = Mathf.Lerp(1.12f, 1f, logoIn);
            logoGroup.localScale = new Vector3(scale, scale, 1f);
            logoGroup.anchoredPosition = new Vector2(0f, -250f + Mathf.Sin(t * 1.1f) * 6f);

            float ribbonIn = Ease(Mathf.Clamp01((t - 0.6f) / 0.7f));
            ribbonCanvas.alpha = ribbonIn;
            ribbon.anchoredPosition = new Vector2(0f, -432f - (1f - ribbonIn) * 30f + Mathf.Sin(t * 1.1f - 0.4f) * 4f);

            for (int i = 0; i < buttonRects.Count; i++)
            {
                float k = Ease(Mathf.Clamp01((t - 1.0f - i * 0.1f) / 0.45f));
                buttonGroups[i].alpha = k;
                // レイアウトの位置は変えず、中身（ラベルごと）だけ横にずらす
                buttonRects[i].GetChild(0).localPosition = new Vector3((1f - k) * -60f, 0f, 0f);
            }
            saveInfoGroup.alpha = Ease(Mathf.Clamp01((t - 1.5f) / 0.6f));

            potionLeft.anchoredPosition = new Vector2(-830f, 20f + Mathf.Sin(t * 1.6f) * 12f);
            potionLeft.localRotation = Quaternion.Euler(0f, 0f, 12f + Mathf.Sin(t * 1.3f) * 4f);
            potionRight.anchoredPosition = new Vector2(830f, 20f + Mathf.Sin(t * 1.6f + 1.7f) * 12f);
            potionRight.localRotation = Quaternion.Euler(0f, 0f, -12f + Mathf.Sin(t * 1.3f + 1.2f) * 4f);

            for (int i = 0; i < sparkles.Count; i++)
            {
                float tw = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * (1.4f + i * 0.23f) + i * 2.1f)), 3f);
                float s = Mathf.Lerp(0.3f, 1.25f, tw);
                sparkles[i].rectTransform.localScale = new Vector3(s, s, 1f);
                sparkles[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 25f + i * 30f);
                Color c = sparkles[i].color;
                c.a = tw * logoIn;
                sparkles[i].color = c;
            }

            Vector2 screen = ((RectTransform)root.transform).rect.size;
            for (int i = 0; i < motes.Count; i++)
            {
                Mote m = motes[i];
                float travel = screen.y + 80f;
                float y = Mathf.Repeat(m.phase + t * m.speed, travel) - 40f;
                float x = m.x * screen.x + Mathf.Sin((t + m.phase) * 0.6f) * 30f;
                m.rect.anchoredPosition = new Vector2(x, y);
                float fade = Mathf.Clamp01(y / 200f) * Mathf.Clamp01((travel - 40f - y) / 300f);
                Color c = m.image.color;
                c.a = 0.55f * fade * (0.6f + 0.4f * Mathf.Sin(t * 3f + m.phase));
                m.image.color = c;
            }
        }

        private static float Ease(float k)
        {
            return 1f - Mathf.Pow(1f - k, 3f);
        }

        private Button AddButton(Transform parent, string label, Action onClick)
        {
            // レイアウト用の外枠の中に本体のボタンを置き、本体だけを動かせるようにする
            RectTransform holder = UIFactory.CreateRect("Slot_" + label, parent);
            UIFactory.Size(holder.gameObject, 440f, 84f);
            Button b = UIWidgets.WoodButton(holder, label, 38, new Vector2(440f, 84f), onClick);
            RectTransform br = (RectTransform)b.transform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
            br.anchoredPosition = Vector2.zero;
            buttonRects.Add(holder);
            buttonGroups.Add(holder.gameObject.AddComponent<CanvasGroup>());
            menu.Add(b);
            return b;
        }

        private static RectTransform Potion(Transform parent, string name, Color color, Vector2 position, float angle)
        {
            Image glow = UIFactory.CreateImage(parent, name + "Glow", UIWidgets.SoftDot(), new Color(color.r, color.g, color.b, 0.35f));
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(300f, 300f));
            Image icon = UIFactory.CreateImage(parent, name, UIFactory.PotionIcon(color), Color.white);
            icon.preserveAspect = true;
            UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(170f, 170f));
            icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return icon.rectTransform;
        }

        private GameObject BuildConfirm(RectTransform parent)
        {
            Image dim = UIFactory.CreateImage(parent, "Confirm", null, new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true;
            UIFactory.Stretch(dim.rectTransform);

            Image paper = UIFactory.CreateImage(dim.transform, "Paper", UIFactory.Parchment(), Color.white);
            UIFactory.Place(paper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 340f));
            Text message = UIFactory.Label(paper.transform, "Message",
                "セーブデータがあります。\n消して、はじめから遊びますか？", 34, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(680f, 140f));

            RectTransform row = UIFactory.CreateRect("Buttons", paper.transform);
            UIFactory.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(620f, 80f));
            UIFactory.Horizontal(row.gameObject, new RectOffset(0, 0, 0, 0), 40f, TextAnchor.MiddleCenter);
            Button yes = UIWidgets.WoodButton(row, "はい", 32, new Vector2(260f, 76f), () =>
            {
                confirmRoot.SetActive(false);
                ForceNewGame();
            });
            Button no = UIWidgets.WoodButton(row, "いいえ", 32, new Vector2(260f, 76f), () => confirmRoot.SetActive(false));
            confirmMenu.Add(yes);
            confirmMenu.Add(no);
            dim.gameObject.SetActive(false);
            return dim.gameObject;
        }
    }
}
