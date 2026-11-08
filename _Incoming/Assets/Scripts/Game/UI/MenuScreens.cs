using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>見出しのリボンつき羊皮紙パネル（ポーズ・設定・クリア画面で共通）</summary>
    internal static class MenuPanel
    {
        public static Image Create(RectTransform parent, string name, string heading, Vector2 size, out GameObject overlay, float dim = 0.6f)
        {
            Image shade = UIFactory.CreateImage(parent, name, null, new Color(0.02f, 0.012f, 0.008f, dim));
            shade.raycastTarget = true;
            UIFactory.Stretch(shade.rectTransform);
            overlay = shade.gameObject;

            Image paper = UIFactory.CreateImage(shade.transform, "Paper", UIFactory.Parchment(), Color.white);
            UIFactory.Place(paper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), size);

            if (!string.IsNullOrEmpty(heading))
            {
                Image ribbon = UIFactory.CreateImage(paper.transform, "Heading", UIFactory.WoodPanel(), Color.white);
                UIFactory.Place(ribbon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(420f, 82f));
                Text title = UIFactory.Label(ribbon.transform, "Title", heading, 40, UIColors.Cream, TextAnchor.MiddleCenter, FontStyle.Bold, true);
                UIFactory.Stretch(title.rectTransform);
            }
            return paper;
        }

        /// <summary>パネルが出てくるときの小さな拡大アニメーション</summary>
        public static void Pop(RectTransform panel, ref float appear, float dt)
        {
            appear = Mathf.Min(1f, appear + dt * 7f);
            float e = 1f - Mathf.Pow(1f - appear, 3f);
            float s = Mathf.Lerp(0.9f, 1f, e);
            panel.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Esc で開くポーズメニュー（時間が止まる）</summary>
    public sealed class PauseMenuUI
    {
        private readonly GameObject root;
        private readonly RectTransform panel;
        private readonly Text stats;
        private readonly MenuList menu = new MenuList();
        private float appear;

        public bool IsOpen => root.activeSelf;

        public PauseMenuUI(RectTransform parent, Action onResume, Action onSettings, Action onTitle, Action onQuit)
        {
            Image paper = MenuPanel.Create(parent, "PauseMenu", "ひとやすみ", new Vector2(600f, 720f), out root);
            panel = paper.rectTransform;

            stats = UIFactory.Label(panel, "Stats", "", 26, UIColors.Ink, TextAnchor.UpperCenter);
            UIFactory.Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(520f, 130f));

            Image divider = UIFactory.CreateImage(panel, "Divider", null, new Color(0.45f, 0.3f, 0.17f, 0.5f));
            UIFactory.Place(divider.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -214f), new Vector2(480f, 3f));

            RectTransform list = UIFactory.CreateRect("Buttons", panel);
            UIFactory.Place(list, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -244f), new Vector2(440f, 420f));
            UIFactory.Vertical(list.gameObject, new RectOffset(0, 0, 0, 0), 16f, TextAnchor.UpperCenter);
            menu.Add(UIWidgets.WoodButton(list, "つづける", 34, new Vector2(420f, 80f), onResume));
            menu.Add(UIWidgets.WoodButton(list, "せってい", 34, new Vector2(420f, 80f), onSettings));
            menu.Add(UIWidgets.WoodButton(list, "セーブしてタイトルへ", 34, new Vector2(420f, 80f), onTitle));
            menu.Add(UIWidgets.WoodButton(list, "セーブしておわる", 34, new Vector2(420f, 80f), onQuit));

            Text hint = UIFactory.Label(panel, "Hint", "Esc でもどる", 20, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(400f, 30f));
            root.SetActive(false);
        }

        public void Open(PlayerProgress progress, PotionInventory inventory)
        {
            root.SetActive(true);
            appear = 0f;
            int gold = progress != null ? progress.Gold : 0;
            int done = progress != null ? progress.CompletedQuests : 0;
            int potions = inventory != null ? inventory.TotalCount : 0;
            string cleared = progress != null && progress.GameCleared ? "\n<color=#B07A1A>★ 伝説の特効薬を届けた</color>" : "";
            stats.text = $"所持金　<b>{gold} G</b>\n解決した相談　<b>{done} 件</b>　／　棚の瓶　<b>{potions} 本</b>{cleared}";
            menu.ResetSelection(0);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiOpen, 0.7f);
        }

        public void Close()
        {
            root.SetActive(false);
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            MenuPanel.Pop(panel, ref appear, dt);
            if (allowInput) menu.Tick();
        }
    }

    /// <summary>音量とマウス感度の設定（タイトルとポーズの両方から開く）</summary>
    public sealed class SettingsPanel
    {
        private readonly GameObject root;
        private readonly RectTransform panel;
        private readonly Slider bgm;
        private readonly Slider se;
        private readonly Slider mouse;
        private readonly Text bgmValue;
        private readonly Text seValue;
        private readonly Text mouseValue;
        private readonly Text qualityValue;
        private readonly Text bobValue;
        private readonly Text qualityNote;
        private readonly MenuList menu = new MenuList();
        private Action onClosed;
        private float appear;
        private float previewTimer;
        private bool syncing;

        public bool IsOpen => root.activeSelf;

        public SettingsPanel(RectTransform parent)
        {
            Image paper = MenuPanel.Create(parent, "Settings", "せってい", new Vector2(820f, 760f), out root, 0.45f);
            panel = paper.rectTransform;

            RectTransform rows = UIFactory.CreateRect("Rows", panel);
            UIFactory.Place(rows, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(700f, 440f));
            UIFactory.Vertical(rows.gameObject, new RectOffset(0, 0, 0, 0), 26f, TextAnchor.UpperCenter);

            bgm = Row(rows, "BGM の音量", out bgmValue, v =>
            {
                if (syncing) return;
                GameSettings.BgmVolume = v;
                bgmValue.text = Percent(v);
            });
            se = Row(rows, "効果音の音量", out seValue, v =>
            {
                if (syncing) return;
                GameSettings.SeVolume = v;
                seValue.text = Percent(v);
                // 動かしたときに大きさがわかるよう、ときどき鳴らす
                if (previewTimer <= 0f)
                {
                    previewTimer = 0.15f;
                    SoundLibrary lib = AudioManager.Lib;
                    if (lib != null) AudioManager.Se(lib.plop, 0.8f);
                }
            });
            mouse = Row(rows, "マウス感度", out mouseValue, v =>
            {
                if (syncing) return;
                GameSettings.MouseSensitivity = Mathf.Lerp(0.2f, 5f, v);
                mouseValue.text = GameSettings.MouseSensitivity.ToString("0.0");
            });

            OptionRow(rows, "画質", out qualityValue, () =>
            {
                GameSettings.GraphicsQuality = (GameSettings.GraphicsQuality + 1) % GameSettings.QualityNames.Length;
                RefreshOptions();
            });
            qualityNote = UIFactory.Label(rows, "QualityNote", "", 19, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Size(qualityNote.gameObject, 700f, 24f);
            OptionRow(rows, "歩くときの揺れ", out bobValue, () =>
            {
                GameSettings.ViewBob = !GameSettings.ViewBob;
                RefreshOptions();
            });

            Text note = UIFactory.Label(panel, "Note", "設定は自動で保存されます", 22, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 132f), new Vector2(600f, 30f));

            RectTransform buttons = UIFactory.CreateRect("Buttons", panel);
            UIFactory.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(400f, 84f));
            UIFactory.Horizontal(buttons.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleCenter);
            menu.Add(UIWidgets.WoodButton(buttons, "とじる", 32, new Vector2(300f, 76f), Close));
            root.SetActive(false);
        }

        public void Open(Action onClosed)
        {
            this.onClosed = onClosed;
            root.SetActive(true);
            appear = 0f;
            syncing = true;
            bgm.value = GameSettings.BgmVolume;
            se.value = GameSettings.SeVolume;
            mouse.value = Mathf.InverseLerp(0.2f, 5f, GameSettings.MouseSensitivity);
            syncing = false;
            bgmValue.text = Percent(bgm.value);
            seValue.text = Percent(se.value);
            mouseValue.text = GameSettings.MouseSensitivity.ToString("0.0");
            RefreshOptions();
            menu.ResetSelection(0);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiOpen, 0.7f);
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            GameSettings.Save();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.uiClose, 0.7f);
            Action callback = onClosed;
            onClosed = null;
            if (callback != null) callback();
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            previewTimer -= dt;
            MenuPanel.Pop(panel, ref appear, dt);
            if (!allowInput) return;
            // ←→ で一番上の項目（BGM）以外も操作できるように、選択中の行をキーで動かすのは省略し、決定キーで閉じる
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) menu.Activate(0);
        }

        private static string Percent(float v)
        {
            return Mathf.RoundToInt(v * 100f) + "%";
        }

        private void RefreshOptions()
        {
            int q = GameSettings.GraphicsQuality;
            qualityValue.text = GameSettings.QualityNames[q];
            qualityNote.text = q == GameSettings.QualityLow ? "軽量：影を近くだけ・光のにじみと飾りの演出なし・60fps上限"
                             : q == GameSettings.QualityHigh ? "高：なめらかな輪郭・遠くまでくっきりした影・ランタンの影"
                             : "標準：見た目と軽さのバランス（おすすめ）";
            bobValue.text = GameSettings.ViewBob ? "オン" : "オフ";
        }

        /// <summary>押すたびに値が切り替わる項目（画質・揺れ）</summary>
        private static void OptionRow(Transform parent, string label, out Text value, Action onClick)
        {
            RectTransform row = UIFactory.CreateRect("Row_" + label, parent);
            UIFactory.Horizontal(row.gameObject, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleCenter);
            UIFactory.Size(row.gameObject, 700f, 56f);
            Text name = UIFactory.Label(row, "Label", label, 30, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name.gameObject, 220f, 44f);
            Button button = UIWidgets.WoodButton(row, "", 28, new Vector2(240f, 56f), onClick);
            value = button.GetComponentInChildren<Text>();
            Text hint = UIFactory.Label(row, "Hint", "クリックで切替", 18, UIColors.InkLight, TextAnchor.MiddleLeft);
            UIFactory.Size(hint.gameObject, 190f, 44f);
        }

        private static Slider Row(Transform parent, string label, out Text value, Action<float> onChanged)
        {
            RectTransform row = UIFactory.CreateRect("Row_" + label, parent);
            UIFactory.Horizontal(row.gameObject, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleCenter);
            UIFactory.Size(row.gameObject, 700f, 50f);
            Text name = UIFactory.Label(row, "Label", label, 30, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name.gameObject, 220f, 44f);
            Slider slider = UIWidgets.CreateSlider(row, 340f, 0.5f, onChanged);
            value = UIFactory.Label(row, "Value", "", 28, UIColors.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Size(value.gameObject, 90f, 44f);
            return slider;
        }
    }

    /// <summary>最後の相談を解決したときのクリア画面（紙吹雪つき）</summary>
    public sealed class ClearScreenUI
    {
        private readonly GameObject root;
        private readonly RectTransform panel;
        private readonly Text stats;
        private readonly MenuList menu = new MenuList();
        private readonly List<Confetti> confetti = new List<Confetti>();
        private readonly RectTransform confettiLayer;
        private readonly Image glow;
        private float appear;
        private float time;

        private struct Confetti
        {
            public RectTransform rect;
            public Vector2 velocity;
            public float spin;
            public float sway;
        }

        public bool IsOpen => root.activeSelf;

        public ClearScreenUI(RectTransform parent, Action onContinue)
        {
            Image shade = UIFactory.CreateImage(parent, "ClearScreen", null, new Color(0.02f, 0.012f, 0.008f, 0.5f));
            shade.raycastTarget = true;
            UIFactory.Stretch(shade.rectTransform);
            root = shade.gameObject;

            glow = UIFactory.CreateImage(shade.transform, "Glow", UIWidgets.SoftDot(), new Color(1f, 0.8f, 0.4f, 0.45f));
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1800f, 900f));

            confettiLayer = UIFactory.CreateRect("Confetti", shade.transform);
            UIFactory.Stretch(confettiLayer);

            panel = UIFactory.CreateRect("Panel", shade.transform);
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 800f));

            Text title = UIFactory.Label(panel, "Title", "ゲームクリア！", 130, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(1200f, 170f));
            var gradient = title.gameObject.AddComponent<UIGradient>();
            gradient.top = new Color(1f, 0.97f, 0.75f);
            gradient.bottom = new Color(1f, 0.62f, 0.2f);
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.11f, 0.05f, 1f);
            outline.effectDistance = new Vector2(4f, -4f);
            var drop = title.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.5f);
            drop.effectDistance = new Vector2(0f, -10f);

            Image paper = UIFactory.CreateImage(panel, "Paper", UIFactory.Parchment(), Color.white);
            UIFactory.Place(paper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(900f, 400f));
            Text message = UIFactory.Label(paper.transform, "Message",
                "伝説の特効薬のおかげで、町に笑顔がもどりました！\nあなたはもう立派な錬金術師です。",
                32, UIColors.Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(820f, 110f));
            stats = UIFactory.Label(paper.transform, "Stats", "", 28, UIColors.InkLight, TextAnchor.UpperCenter);
            UIFactory.Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(820f, 120f));

            RectTransform buttons = UIFactory.CreateRect("Buttons", paper.transform);
            UIFactory.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(500f, 84f));
            UIFactory.Horizontal(buttons.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleCenter);
            menu.Add(UIWidgets.WoodButton(buttons, "アトリエにもどる", 34, new Vector2(440f, 80f), onContinue));

            Color[] colors =
            {
                new Color(1f, 0.82f, 0.3f), new Color(0.95f, 0.45f, 0.4f), new Color(0.45f, 0.8f, 0.5f),
                new Color(0.5f, 0.65f, 1f), new Color(0.9f, 0.6f, 1f), new Color(1f, 0.95f, 0.85f),
            };
            for (int i = 0; i < 90; i++)
            {
                Image piece = UIFactory.CreateImage(confettiLayer, "Piece" + i, null, colors[i % colors.Length]);
                RectTransform rt = piece.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(UnityEngine.Random.Range(10f, 18f), UnityEngine.Random.Range(18f, 30f));
                confetti.Add(new Confetti { rect = rt });
            }
            root.SetActive(false);
        }

        public void Open(PlayerProgress progress)
        {
            root.SetActive(true);
            appear = 0f;
            time = 0f;
            int gold = progress != null ? progress.Gold : 0;
            int done = progress != null ? progress.CompletedQuests : 0;
            stats.text = $"解決した相談　<b>{done} 件</b>　　所持金　<b>{gold} G</b>\n<size=22>このあとも、新しい調合を探求したり、町の人の相談に乗ったりして遊べます</size>";
            menu.ResetSelection(0);

            Vector2 screen = ((RectTransform)root.transform).rect.size;
            for (int i = 0; i < confetti.Count; i++)
            {
                Confetti c = confetti[i];
                c.rect.anchoredPosition = new Vector2(UnityEngine.Random.Range(0f, screen.x), screen.y + UnityEngine.Random.Range(20f, 900f));
                c.velocity = new Vector2(UnityEngine.Random.Range(-40f, 40f), -UnityEngine.Random.Range(120f, 260f));
                c.spin = UnityEngine.Random.Range(-360f, 360f);
                c.sway = UnityEngine.Random.Range(0f, 6.28f);
                confetti[i] = c;
            }
        }

        public void Close()
        {
            root.SetActive(false);
        }

        public void Tick(float dt, bool allowInput)
        {
            if (!IsOpen) return;
            time += dt;
            MenuPanel.Pop(panel, ref appear, dt);
            float pulse = 0.4f + Mathf.Sin(time * 2f) * 0.08f;
            glow.color = new Color(1f, 0.8f, 0.4f, pulse);

            Vector2 screen = ((RectTransform)root.transform).rect.size;
            for (int i = 0; i < confetti.Count; i++)
            {
                Confetti c = confetti[i];
                Vector2 p = c.rect.anchoredPosition;
                p += new Vector2(c.velocity.x + Mathf.Sin(time * 3f + c.sway) * 60f, c.velocity.y) * dt;
                if (p.y < -40f) p = new Vector2(UnityEngine.Random.Range(0f, screen.x), screen.y + 40f);
                c.rect.anchoredPosition = p;
                c.rect.localRotation = Quaternion.Euler(Mathf.Sin(time * 4f + c.sway) * 70f, 0f, time * c.spin);
                confetti[i] = c;
            }
            if (allowInput && time > 1.2f) menu.Tick();
        }
    }
}
