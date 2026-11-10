using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>見出しのリボンつき羊皮紙パネル（ポーズ・設定で共通）</summary>
    internal static class MenuPanel
    {
        /// <summary>机の上に本を広げるときの、後ろの暗さ（ランプの火のゆらぎが透けて見える薄さ）</summary>
        public const float DeskDim = 0.3f;

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

        /// <summary>
        /// 本を机の上に広げるアニメーション：少し手前（下）から、わずかに傾いてすっと置かれる。
        /// 画面は切り替えず、後ろの机・火のゆらぎは見えたまま
        /// </summary>
        public static void Rise(RectTransform panel, Vector2 rest, ref float appear, float dt, float restScale = DeskBookScale)
        {
            appear = Mathf.Min(1f, appear + dt * 4.5f);
            float e = 1f - Mathf.Pow(1f - appear, 3f);
            panel.anchoredPosition = rest + new Vector2(0f, -70f * (1f - e));
            panel.localRotation = Quaternion.Euler(0f, 0f, 1.6f * (1f - e));
            float s = restScale * Mathf.Lerp(0.95f, 1f, e);
            panel.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>机の上に置いた本の大きさ（少し小さくして、まわりに机・ランプ・窓が見えるようにする）</summary>
        public const float DeskBookScale = 0.92f;

        /// <summary>後ろの暗がりを、本が開くのに合わせて少しずつ濃くする</summary>
        public static void Shade(Image overlay, float appear, float dim = DeskDim)
        {
            if (overlay == null) return;
            Color c = overlay.color;
            c.a = dim * Mathf.Clamp01(appear * 1.4f);
            overlay.color = c;
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

            stats = UIFactory.Label(panel, "Stats", "", 25, UIColors.Ink, TextAnchor.UpperCenter);
            stats.supportRichText = true;
            UIFactory.Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(560f, 136f));

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

        /// <summary>summary：探求の進み具合（発見・液体・机の素材など。AtelierHUD が作る）</summary>
        public void Open(string summary)
        {
            root.SetActive(true);
            appear = 0f;
            stats.text = summary ?? "";
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
        private readonly Text screenValue;
        private readonly Text sizeValue;
        private readonly Text screenNote;
        private readonly Color sizeColor;
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
            // 画面モード・ウィンドウの大きさ（Unity が覚えていて、次に起動したときもそのまま）
            OptionRow(rows, "画面モード", out screenValue, () =>
            {
                ScreenModes.Next();
                RefreshOptions();
            });
            OptionRow(rows, "画面サイズ", out sizeValue, () =>
            {
                ScreenModes.NextWindowSize();
                RefreshOptions();
            });
            sizeColor = sizeValue.color;
            screenNote = UIFactory.Label(rows, "ScreenNote", "", 19, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Size(screenNote.gameObject, 700f, 24f);

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

            // 2D 版では、マウス感度・画質・歩くときの揺れは使わないので隠す（設定の値そのものは残る）
            mouse.transform.parent.gameObject.SetActive(false);
            qualityValue.transform.parent.parent.gameObject.SetActive(false);
            qualityNote.gameObject.SetActive(false);
            bobValue.transform.parent.parent.gameObject.SetActive(false);

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

            ScreenModes.Kind kind = ScreenModes.Current;
            bool windowed = kind == ScreenModes.Kind.Windowed;
            screenValue.text = ScreenModes.Label(kind);
            sizeValue.text = ScreenModes.WindowSizeLabel;
            sizeValue.color = new Color(sizeColor.r, sizeColor.g, sizeColor.b, windowed ? sizeColor.a : sizeColor.a * 0.45f);
            screenNote.text = Application.isEditor ? "画面の切り替えは、ビルドした製品版で反映されます"
                            : windowed ? "ウィンドウの大きさは、ふちをドラッグしても変えられます"
                            : "画面サイズは「ウィンドウ」のときに使われます";
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
}
