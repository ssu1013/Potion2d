using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>会話ウィンドウに出す内容</summary>
    public class DialogueRequest
    {
        public string speaker = "";
        public string title = "";
        /// <summary>顔の映像（お客さんの顔を映す RenderTexture など）。null なら顔なし</summary>
        public Texture portrait;
        /// <summary>1要素 = 1ページ。「（」で始まるページは地の文として薄い色で出す</summary>
        public List<string> pages = new List<string>();
        /// <summary>話し声（ピッ音）の高さ</summary>
        public float voicePitch = 1f;
        /// <summary>最後のページで出す選択肢（null なら選択肢なし）</summary>
        public string[] choices;
        /// <summary>閉じたときに呼ばれる。引数は選んだ選択肢の番号（選択肢なしは -1）</summary>
        public Action<int> onClose;
    }

    /// <summary>
    /// 画面下の会話ウィンドウ（羊皮紙＋木の名札＋顔の枠）。
    /// 文字を1文字ずつ出し（まだ出ていない文字は透明にして、行の折り返し位置が動かないようにする）、
    /// 文字に合わせて「ピッ」という声を鳴らす。E / Space / Enter / クリックで次へ。
    /// </summary>
    public sealed class DialogueUI
    {
        private const float CharsPerSecond = 40f;
        private const float BoxWidth = 1300f;
        private const float BoxHeight = 250f;

        private readonly GameObject root;
        private readonly CanvasGroup group;
        private readonly RectTransform box;
        private readonly GameObject portraitFrame;
        private readonly RawImage portraitImage;
        private readonly RectTransform namePlate;
        private readonly Text nameText;
        private readonly Text titleText;
        private readonly Text bodyText;
        private readonly RectTransform nextMark;
        private readonly RectTransform choiceRoot;
        private readonly List<Button> choiceButtons = new List<Button>();
        private readonly MenuList choiceMenu = new MenuList();

        private DialogueRequest request;
        private int page;
        private float reveal;
        private int revealed;
        private float pauseTimer;
        private int openFrame = -1;
        private float appear;
        private float time;
        private int blipCounter;
        private bool choicesShown;

        public bool IsOpen => root.activeSelf;
        public bool IsShowingChoices => choicesShown;
        public int ClosedFrame { get; private set; } = -1;
        public string Speaker => request != null ? request.speaker : "";
        /// <summary>今の会話の選択肢（なければ null。自動テスト用）</summary>
        public IReadOnlyList<string> Choices => request != null ? request.choices : null;
        /// <summary>今のページの文（自動テスト用）</summary>
        public string CurrentPageText => CurrentText;

        private string CurrentText => request != null && page < request.pages.Count ? request.pages[page] ?? "" : "";
        private bool IsLastPage => request == null || page >= request.pages.Count - 1;
        private bool HasChoices => request != null && request.choices != null && request.choices.Length > 0;
        private bool PageComplete => revealed >= CurrentText.Length;

        public DialogueUI(RectTransform parent)
        {
            RectTransform overlay = UIFactory.CreateRect("Dialogue", parent);
            UIFactory.Stretch(overlay);
            root = overlay.gameObject;
            group = root.AddComponent<CanvasGroup>();

            // 下のほうを少し暗くして文字を読みやすく
            Image shade = UIFactory.CreateImage(overlay, "Shade", UIWidgets.SoftDot(), new Color(0.05f, 0.03f, 0.02f, 0.55f));
            UIFactory.Place(shade.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(2600f, 700f));

            Image paper = UIFactory.CreateImage(overlay, "Box", UIFactory.Parchment(), Color.white);
            box = paper.rectTransform;
            UIFactory.Place(box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(BoxWidth, BoxHeight));

            // 顔の枠（左端、少し上にはみ出す）
            Image frame = UIFactory.CreateImage(box, "PortraitFrame", UIFactory.WoodPanel(), Color.white);
            UIFactory.Place(frame.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(26f, 22f), new Vector2(250f, 250f));
            portraitFrame = frame.gameObject;
            Image back = UIFactory.CreateImage(frame.transform, "Back", UIFactory.RoundedRect(new Color(0.98f, 0.9f, 0.74f, 1f), UIColors.Outline, 10, 3), Color.white);
            UIFactory.Stretch(back.rectTransform, 16f, 16f, 16f, 16f);
            portraitImage = new GameObject("Face", typeof(RectTransform)).AddComponent<RawImage>();
            portraitImage.transform.SetParent(back.transform, false);
            portraitImage.raycastTarget = false;
            UIFactory.Stretch(portraitImage.rectTransform, 4f, 4f, 4f, 4f);

            // 名札
            Image plate = UIFactory.CreateImage(box, "NamePlate", UIFactory.WoodPanel(), Color.white);
            namePlate = plate.rectTransform;
            UIFactory.Place(namePlate, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(300f, 4f), new Vector2(240f, 62f));
            UIFactory.Horizontal(plate.gameObject, new RectOffset(28, 30, 8, 10), 14f, TextAnchor.MiddleLeft);
            UIFactory.FitContent(plate.gameObject, true, false);
            nameText = UIFactory.Label(namePlate, "Name", "", 32, UIColors.Cream, TextAnchor.MiddleLeft, FontStyle.Bold, true);
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText = UIFactory.Label(namePlate, "Title", "", 21, UIColors.CreamDim, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

            bodyText = UIFactory.Label(box, "Body", "", 33, UIColors.Ink, TextAnchor.UpperLeft);
            bodyText.lineSpacing = 1.18f;
            UIFactory.Stretch(bodyText.rectTransform, 300f, 56f, 54f, 30f);

            Text mark = UIFactory.Label(box, "Next", "▼", 28, new Color(0.7f, 0.42f, 0.12f), TextAnchor.MiddleCenter);
            nextMark = mark.rectTransform;
            UIFactory.Place(nextMark, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-42f, 36f), new Vector2(40f, 40f));

            // 選択肢（ウィンドウの右上に積む）
            choiceRoot = UIFactory.CreateRect("Choices", box);
            UIFactory.Place(choiceRoot, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-24f, 18f), new Vector2(470f, 160f));
            UIFactory.Vertical(choiceRoot.gameObject, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.LowerRight);
            UIFactory.FitContent(choiceRoot.gameObject, false, true);
            choiceRoot.gameObject.SetActive(false);

            root.SetActive(false);
        }

        public void Open(DialogueRequest req)
        {
            if (req == null) return;
            if (req.pages == null || req.pages.Count == 0) req.pages = new List<string> { "……" };
            request = req;
            page = 0;
            root.SetActive(true);
            openFrame = Time.frameCount;
            appear = 0f;
            blipCounter = 0;

            nameText.text = req.speaker ?? "";
            titleText.text = req.title ?? "";
            titleText.gameObject.SetActive(!string.IsNullOrEmpty(req.title));

            bool hasPortrait = req.portrait != null;
            portraitFrame.SetActive(hasPortrait);
            portraitImage.texture = req.portrait;
            float left = hasPortrait ? 300f : 56f;
            bodyText.rectTransform.offsetMin = new Vector2(left, bodyText.rectTransform.offsetMin.y);
            namePlate.anchoredPosition = new Vector2(hasPortrait ? 300f : 40f, 4f);

            BuildChoices();
            StartPage();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.6f, 1.1f);
        }

        /// <summary>毎フレーム呼ぶ（HUD から）</summary>
        public void Tick()
        {
            if (!IsOpen || request == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            time += dt;

            // 出てくるときのアニメーション
            appear = Mathf.Min(1f, appear + dt * 7f);
            float e = 1f - Mathf.Pow(1f - appear, 3f);
            group.alpha = e;
            box.anchoredPosition = new Vector2(0f, Mathf.Lerp(-30f, 34f, e));

            TypeCharacters(dt);

            bool complete = PageComplete;
            if (complete && IsLastPage && HasChoices && !choicesShown) ShowChoices();

            bool showNext = complete && !(IsLastPage && HasChoices);
            nextMark.gameObject.SetActive(showNext);
            if (showNext) nextMark.anchoredPosition = new Vector2(-42f, 36f + Mathf.Abs(Mathf.Sin(time * 4f)) * 6f);

            if (Time.frameCount == openFrame) return; // 開いたときのキー入力で進まないように

            if (choicesShown)
            {
                for (int i = 0; i < choiceButtons.Count && i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    {
                        choiceMenu.Activate(i);
                        return;
                    }
                }
                choiceMenu.Tick();
                return;
            }

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0))
            {
                Advance();
            }
        }

        /// <summary>文字送り（途中なら全部表示、表示済みなら次のページ／閉じる）</summary>
        public void Advance()
        {
            if (!IsOpen || request == null) return;
            if (!PageComplete)
            {
                revealed = CurrentText.Length;
                reveal = revealed;
                UpdateBody();
                return;
            }
            if (IsLastPage)
            {
                if (HasChoices)
                {
                    if (!choicesShown) ShowChoices();
                    return;
                }
                Close(-1);
                return;
            }
            page++;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.page, 0.45f, UnityEngine.Random.Range(0.95f, 1.08f));
            StartPage();
        }

        /// <summary>選択肢を選ぶ（自動テスト用にも公開）</summary>
        public void Choose(int index)
        {
            if (!IsOpen || !HasChoices) return;
            Close(Mathf.Clamp(index, 0, request.choices.Length - 1));
        }

        /// <summary>コールバックを呼ばずに閉じる（タイトルへ戻るときなど）</summary>
        public void ForceClose()
        {
            if (!IsOpen) return;
            request = null;
            HideChoices();
            root.SetActive(false);
            ClosedFrame = Time.frameCount;
        }

        /// <summary>最後まで読み進めた状態にする（自動テスト用）</summary>
        public void SkipToEnd()
        {
            if (!IsOpen || request == null) return;
            page = request.pages.Count - 1;
            revealed = CurrentText.Length;
            reveal = revealed;
            UpdateBody();
        }

        // ------------------------------------------------------------------

        private void StartPage()
        {
            reveal = 0f;
            revealed = 0;
            pauseTimer = 0.08f;
            HideChoices();
            string text = CurrentText;
            bool narration = text.StartsWith("（") || text.StartsWith("(");
            bodyText.color = narration ? UIColors.InkLight : UIColors.Ink;
            bodyText.fontSize = narration ? 30 : 33;
            UpdateBody();
        }

        private void TypeCharacters(float dt)
        {
            string text = CurrentText;
            if (revealed >= text.Length) return;
            if (pauseTimer > 0f)
            {
                pauseTimer -= dt;
                return;
            }

            reveal += dt * CharsPerSecond;
            int target = Mathf.Min(text.Length, Mathf.FloorToInt(reveal));
            while (revealed < target)
            {
                char c = text[revealed];
                revealed++;
                if (!char.IsWhiteSpace(c) && !IsPunctuation(c) && (blipCounter++ % 2) == 0) Blip();
                float pause = PauseAfter(c);
                if (pause > 0f && revealed < text.Length)
                {
                    pauseTimer = pause;
                    reveal = revealed;
                    break;
                }
            }
            UpdateBody();
        }

        private void UpdateBody()
        {
            string text = CurrentText;
            if (revealed >= text.Length)
            {
                bodyText.text = text;
                return;
            }
            // まだ出ていない文字は透明にしておく（折り返し位置がずれない）
            bodyText.text = text.Substring(0, revealed) + "<color=#00000000>" + text.Substring(revealed) + "</color>";
        }

        private void Blip()
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib == null || request == null) return;
            float pitch = request.voicePitch * UnityEngine.Random.Range(0.94f, 1.07f);
            AudioManager.Se(lib.voiceBlip, 0.32f, pitch);
        }

        private static bool IsPunctuation(char c)
        {
            return "、。，．・…！？!?「」『』（）()〜ー".IndexOf(c) >= 0;
        }

        private static float PauseAfter(char c)
        {
            switch (c)
            {
                case '、':
                case ',':
                    return 0.08f;
                case '。':
                case '！':
                case '？':
                case '!':
                case '?':
                    return 0.18f;
                case '…':
                    return 0.12f;
                default:
                    return 0f;
            }
        }

        private void BuildChoices()
        {
            foreach (Button b in choiceButtons)
            {
                if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            }
            choiceButtons.Clear();
            choiceMenu.Clear();
            if (!HasChoices) return;

            for (int i = 0; i < request.choices.Length; i++)
            {
                int index = i;
                // 選択肢が多いとき（相談に乗る方法を選ぶ：最大4つ）は、少し小さくして画面に収める
                bool many = request.choices.Length >= 3;
                string label = $"{i + 1}. {request.choices[i]}";
                Button b = UIWidgets.WoodButton(choiceRoot, label, many ? 27 : 30, new Vector2(460f, many ? 62f : 70f), () => Choose(index));
                choiceButtons.Add(b);
                choiceMenu.Add(b);
            }
        }

        private void ShowChoices()
        {
            choicesShown = true;
            choiceRoot.gameObject.SetActive(true);
            choiceMenu.ResetSelection(0);
        }

        private void HideChoices()
        {
            choicesShown = false;
            choiceRoot.gameObject.SetActive(false);
        }

        private void Close(int choice)
        {
            Action<int> callback = request != null ? request.onClose : null;
            request = null;
            HideChoices();
            root.SetActive(false);
            ClosedFrame = Time.frameCount;
            // コールバックの中で次の会話を開いてもよい
            if (callback != null) callback(choice);
        }
    }
}
