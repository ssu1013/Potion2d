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

            Image paper = UIFactory.CreateImage(shade.transform, "Paper", UIFactory.Parchment(), UIFactory.Paper);
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

    /// <summary>
    /// 設定（タイトルとポーズの両方から開く）。
    ///   おと：全体・BGM・環境音・効果音の音量、ウィンドウが裏に回ったときの音
    ///   がめん：画面モード・画面サイズ（ウィンドウのとき）・フレームレート
    /// マウスでもキーボードでも操作できる：↑↓（W/S）で項目を選ぶ・←→（A/D）で値を変える（押し続けると続けて動く）・
    /// Enter で切り替え／とじる・Esc でとじる。下の行に、選んでいる項目の説明が出る。
    /// 値はすぐに反映され、閉じるとき（・裏に回ったとき・終了するとき）にディスクへ書かれる。
    /// </summary>
    public sealed class SettingsPanel
    {
        private const float SliderStep = 0.05f;
        private const string DefaultNote = "設定は自動で保存されます";

        private sealed class Row
        {
            public Image background;
            public Slider slider;
            public Text value;
            public Action<int> step;
            public Func<string> describe;
        }

        private static readonly Color RowIdle = new Color(0.45f, 0.28f, 0.12f, 0f);
        private static readonly Color RowFocused = new Color(0.45f, 0.28f, 0.12f, 0.15f);

        private readonly GameObject root;
        private readonly RectTransform panel;
        private readonly List<Row> rows = new List<Row>();
        private readonly Row master;
        private readonly Row bgm;
        private readonly Row ambient;
        private readonly Row se;
        private readonly Row background;
        private readonly Row screenMode;
        private readonly Row screenSize;
        private readonly Row frameRate;
        private readonly Text note;
        private readonly Text closeLabel;
        private readonly UIButtonFx closeFx;
        private readonly Color sizeColor;
        private Action onClosed;
        private float appear;
        private float previewTimer;
        private float refreshTimer;
        private float repeatTimer;
        private int repeatDirection;
        private bool syncing;
        /// <summary>キーボードで選んでいる項目（-1 = なし、rows.Count = とじる）</summary>
        private int focus = -1;

        public bool IsOpen => root.activeSelf;

        public SettingsPanel(RectTransform parent)
        {
            Image paper = MenuPanel.Create(parent, "Settings", "せってい", new Vector2(860f, 860f), out root, 0.45f);
            panel = paper.rectTransform;

            RectTransform list = UIFactory.CreateRect("Rows", panel);
            UIFactory.Place(list, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(760f, 560f));
            UIFactory.Vertical(list.gameObject, new RectOffset(0, 0, 0, 0), 6f, TextAnchor.UpperCenter);

            Heading(list, "おと");
            master = SliderRow(list, "全体の音量", v => { GameSettings.MasterVolume = v; Preview(); }, () => "すべての音の大きさ");
            bgm = SliderRow(list, "BGM", v => GameSettings.BgmVolume = v, () => "曲の大きさ");
            ambient = SliderRow(list, "環境音", v => GameSettings.AmbientVolume = v, () => "部屋の音・ランプの火・お湯のコトコト");
            se = SliderRow(list, "効果音", v => { GameSettings.SeVolume = v; Preview(); }, () => "素材を取る・注ぐ・かき混ぜる・火・瓶詰め・ボタンの音");
            background = OptionRow(list, "裏にあるとき", d => GameSettings.MuteInBackground = !GameSettings.MuteInBackground,
                () => "Alt+Tab などでウィンドウが裏に回ったときも、音を鳴らし続けるか");

            Heading(list, "がめん");
            screenMode = OptionRow(list, "画面モード", ScreenModes.Step, DescribeScreenMode);
            screenSize = OptionRow(list, "画面サイズ", ScreenModes.StepWindowSize, DescribeScreenSize);
            sizeColor = screenSize.value.color;
            frameRate = OptionRow(list, "フレームレート", d =>
            {
                int count = GameSettings.FrameRateNames.Length;
                GameSettings.FrameRate = (FrameRateMode)((((int)GameSettings.FrameRate + d) % count + count) % count);
            }, DescribeFrameRate);

            note = UIFactory.Label(panel, "Note", DefaultNote, 21, UIColors.InkLight, TextAnchor.MiddleCenter);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 124f), new Vector2(760f, 56f));

            RectTransform buttons = UIFactory.CreateRect("Buttons", panel);
            UIFactory.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(400f, 84f));
            UIFactory.Horizontal(buttons.gameObject, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleCenter);
            Button close = UIWidgets.WoodButton(buttons, "とじる", 32, new Vector2(300f, 76f), Close);
            closeLabel = close.GetComponentInChildren<Text>();
            closeFx = close.GetComponent<UIButtonFx>();
            if (closeFx != null) closeFx.Hovered = () => SetFocus(rows.Count, true);
            root.SetActive(false);
        }

        public void Open(Action onClosed)
        {
            this.onClosed = onClosed;
            root.SetActive(true);
            appear = 0f;
            repeatDirection = 0;
            Sync();
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
            // Alt+Enter などで画面モードが外から変わっても、表示が追いつくように
            refreshTimer -= dt;
            if (refreshTimer <= 0f) RefreshOptions();
            if (allowInput) HandleKeys(dt);
        }

        // ------------------------------------------------------------------
        // キーボード
        // ------------------------------------------------------------------

        private void HandleKeys(float dt)
        {
            int count = rows.Count + 1; // 最後は「とじる」
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) SetFocus(focus < 0 ? 0 : (focus - 1 + count) % count, true);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) SetFocus(focus < 0 ? 0 : (focus + 1) % count, true);

            Row row = focus >= 0 && focus < rows.Count ? rows[focus] : null;
            int pressed = 0;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) pressed = -1;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) pressed = 1;
            int held = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? -1
                     : Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0;
            if (pressed != 0)
            {
                if (row != null) Adjust(row, pressed, true);
                repeatDirection = pressed;
                repeatTimer = 0.4f;
            }
            else if (held != 0 && held == repeatDirection && row != null && row.slider != null)
            {
                // スライダーは、押し続けると続けて動く（項目の切り替えは1回ずつ）
                repeatTimer -= dt;
                if (repeatTimer <= 0f)
                {
                    repeatTimer = 0.05f;
                    Adjust(row, held, false);
                }
            }
            else repeatDirection = 0;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E))
            {
                if (row == null)
                {
                    SoundLibrary lib = AudioManager.Lib;
                    if (lib != null) AudioManager.Se(lib.uiClick, 0.8f);
                    Close();
                }
                else if (row.step != null) Adjust(row, 1, true);
            }
        }

        private void Adjust(Row row, int direction, bool sound)
        {
            SoundLibrary lib = AudioManager.Lib;
            if (row.slider != null)
            {
                float before = row.slider.value;
                float next = Mathf.Clamp01(Mathf.Round((before + direction * SliderStep) / SliderStep) * SliderStep);
                if (Mathf.Approximately(next, before)) return;
                row.slider.value = next;
                if (sound && lib != null && row != se && row != master) AudioManager.Se(lib.uiHover, 0.4f);
                return;
            }
            if (row.step == null) return;
            row.step(direction);
            if (lib != null) AudioManager.Se(lib.uiClick, 0.8f);
            RefreshOptions();
        }

        private void SetFocus(int index, bool sound)
        {
            if (index == focus) return;
            focus = index;
            if (sound)
            {
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null) AudioManager.Se(lib.uiHover, 0.45f);
            }
            ApplyFocus();
        }

        private void ApplyFocus()
        {
            for (int i = 0; i < rows.Count; i++) rows[i].background.color = i == focus ? RowFocused : RowIdle;
            bool closeFocused = focus == rows.Count;
            if (closeFx != null) closeFx.Selected = closeFocused;
            if (closeLabel != null) closeLabel.color = closeFocused ? UIColors.Gold : UIColors.Cream;
            UpdateNote();
        }

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------

        private void Sync()
        {
            syncing = true;
            master.slider.value = GameSettings.MasterVolume;
            bgm.slider.value = GameSettings.BgmVolume;
            ambient.slider.value = GameSettings.AmbientVolume;
            se.slider.value = GameSettings.SeVolume;
            syncing = false;
            foreach (Row row in rows) if (row.slider != null) row.value.text = Percent(row.slider.value);
            focus = -1;
            ApplyFocus();
            RefreshOptions();
        }

        private void RefreshOptions()
        {
            refreshTimer = 0.25f;
            SetText(background.value, GameSettings.MuteInBackground ? "止める" : "鳴らす");
            ScreenModes.Kind kind = ScreenModes.Current;
            SetText(screenMode.value, ScreenModes.Label(kind));
            SetText(screenSize.value, ScreenModes.WindowSizeLabel);
            bool windowed = kind == ScreenModes.Kind.Windowed;
            screenSize.value.color = new Color(sizeColor.r, sizeColor.g, sizeColor.b, windowed ? sizeColor.a : sizeColor.a * 0.45f);
            int fr = Mathf.Clamp((int)GameSettings.FrameRate, 0, GameSettings.FrameRateNames.Length - 1);
            SetText(frameRate.value, GameSettings.FrameRateNames[fr]);
            UpdateNote();
        }

        private void UpdateNote()
        {
            Row row = focus >= 0 && focus < rows.Count ? rows[focus] : null;
            SetText(note, row != null && row.describe != null ? row.describe() : DefaultNote);
        }

        private void Preview()
        {
            // 動かしたときに大きさがわかるよう、ときどき鳴らす
            if (previewTimer > 0f) return;
            previewTimer = 0.15f;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.plop, 0.8f);
        }

        private static string DescribeScreenMode()
        {
            if (Application.isEditor) return "画面の切り替えは、ビルドした製品版で反映されます";
            switch (ScreenModes.Current)
            {
                case ScreenModes.Kind.Fullscreen: return "フルスクリーン：画面をひとりじめにする（環境によっては少し軽い）";
                case ScreenModes.Kind.Windowed: return "ウィンドウ：ふちをドラッグしても大きさを変えられる";
                default: return "ボーダーレス：画面いっぱいのウィンドウ（Alt+Tab がすばやい。おすすめ）";
            }
        }

        private static string DescribeScreenSize()
        {
            if (Application.isEditor) return "画面の切り替えは、ビルドした製品版で反映されます";
            return ScreenModes.Current == ScreenModes.Kind.Windowed
                ? "ウィンドウの大きさ（今の画面に収まる大きさだけ選べる）"
                : "画面サイズは「ウィンドウ」のときに使われます";
        }

        private static string DescribeFrameRate()
        {
            switch (GameSettings.FrameRate)
            {
                case FrameRateMode.Monitor: return "モニターに合わせる：144Hz なら 144fps（なめらかだが、PC の負担が大きい）";
                case FrameRateMode.Battery: return "30 fps：いちばん軽い（バッテリーで遊ぶとき）";
                default: return "60 fps：なめらかで静か。ノート PC でもファンが回りにくい（おすすめ）";
            }
        }

        private static string Percent(float v)
        {
            return Mathf.RoundToInt(v * 100f) + "%";
        }

        private static void SetText(Text text, string value)
        {
            if (text != null && text.text != value) text.text = value;
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        private static void Heading(Transform parent, string text)
        {
            Text t = UIFactory.Label(parent, "Heading_" + text, text, 22, UIColors.InkLight, TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Size(t.gameObject, 740f, 30f);
        }

        /// <summary>項目の行（指すと選ばれ、選ばれている行はうっすら色がつく）</summary>
        private Row NewRow(Transform parent, string label, float height, Func<string> describe)
        {
            int index = rows.Count;
            Image back = UIFactory.CreateImage(parent, "Row_" + label, UIFactory.RoundedRect(Color.white, Color.white, 10, 0), RowIdle);
            back.raycastTarget = true;
            UIFactory.Size(back.gameObject, 760f, height);
            UIFactory.Horizontal(back.gameObject, new RectOffset(16, 16, 0, 0), 20f, TextAnchor.MiddleLeft);
            Text name = UIFactory.Label(back.transform, "Label", label, 28, UIColors.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name.gameObject, 240f, 44f);
            back.gameObject.AddComponent<UIHoverRelay>().Entered = () => SetFocus(index, true);
            var row = new Row { background = back, describe = describe };
            rows.Add(row);
            return row;
        }

        private Row SliderRow(Transform parent, string label, Action<float> apply, Func<string> describe)
        {
            Row row = NewRow(parent, label, 50f, describe);
            row.slider = UIWidgets.CreateSlider(row.background.transform, 330f, 0.5f, v =>
            {
                if (syncing) return;
                apply(v);
                if (row.value != null) row.value.text = Percent(v);
            });
            row.value = UIFactory.Label(row.background.transform, "Value", "", 28, UIColors.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Size(row.value.gameObject, 90f, 44f);
            return row;
        }

        /// <summary>押すたびに値が切り替わる項目（←→ で前後に切り替え）</summary>
        private Row OptionRow(Transform parent, string label, Action<int> step, Func<string> describe)
        {
            Row row = NewRow(parent, label, 56f, describe);
            int index = rows.Count - 1;
            row.step = step;
            Button button = UIWidgets.WoodButton(row.background.transform, "", 26, new Vector2(300f, 52f), () =>
            {
                step(1);
                RefreshOptions();
            });
            row.value = button.GetComponentInChildren<Text>();
            UIButtonFx fx = button.GetComponent<UIButtonFx>();
            if (fx != null) fx.Hovered = () => SetFocus(index, true);
            Text hint = UIFactory.Label(row.background.transform, "Hint", "←→ で切替", 18, UIColors.InkLight, TextAnchor.MiddleLeft);
            UIFactory.Size(hint.gameObject, 140f, 44f);
            return row;
        }
    }
}
