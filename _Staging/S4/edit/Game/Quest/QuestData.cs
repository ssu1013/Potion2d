using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    public enum HairStyle
    {
        [InspectorName("短髪")] Short = 0,
        [InspectorName("ボブ")] Bob = 1,
        [InspectorName("ロング")] Long = 2,
        [InspectorName("ツインテール")] TwinTails = 3,
        [InspectorName("おだんご")] Bun = 4,
        [InspectorName("なし")] Bald = 5,
    }

    public enum HatStyle
    {
        [InspectorName("なし")] None = 0,
        [InspectorName("キャップ")] Cap = 1,
        [InspectorName("麦わら帽子")] StrawHat = 2,
        [InspectorName("とんがり帽子")] Pointy = 3,
        [InspectorName("頭巾（スカーフ）")] Scarf = 4,
        [InspectorName("バンダナ")] Bandana = 5,
        [InspectorName("ベレー帽")] Beret = 6,
        [InspectorName("フード")] Hood = 7,
    }

    /// <summary>お客さんの仮の見た目（立ち絵がないときに、色・髪型などから仮の絵を作るための値。声の高さも）</summary>
    [Serializable]
    public class CustomerLook
    {
        public Color skin = new Color(1f, 0.85f, 0.72f);
        public Color hair = new Color(0.45f, 0.28f, 0.15f);
        public Color cloth = new Color(0.35f, 0.55f, 0.35f);
        public Color accent = new Color(0.75f, 0.55f, 0.3f);
        public HairStyle hairStyle = HairStyle.Short;
        public HatStyle hatStyle = HatStyle.None;
        public bool beard;
        [Range(0.75f, 1.2f), Tooltip("背の高さ（倍率）")] public float height = 1f;
        [Range(0.8f, 1.3f), Tooltip("横幅（倍率）")] public float width = 1f;
        [Range(0.6f, 1.8f), Tooltip("話し声の高さ")] public float voicePitch = 1f;
    }

    /// <summary>
    /// 町の人の相談。お客さんの見た目・セリフ（困りごと）・役に立つ液体の条件・お礼をまとめて持つ。
    /// お客さんは「〇〇を作れ」とは言わず、「〇〇で困っている」と相談に来る。プレイヤーは
    /// 自分の棚の瓶を「分け与える」か、レシピ本の作り方を「教える」（教えてもよい人だけ）ことで力になる。
    /// 条件は「特定の完成条件データ（特別なポーション）」か「属性条件＋最低ランク」で指定する。
    /// （クラス名は、作ったデータとのつながりを保つため Quest のまま）
    /// </summary>
    [CreateAssetMenu(fileName = "Quest_New", menuName = "Potion Frontier/相談データ (Consultation)", order = 3)]
    public class QuestData : ScriptableObject
    {
        [Header("相談する人")]
        [SerializeField] private string questId = "";
        [SerializeField] private string clientName = "町の人";
        [SerializeField, Tooltip("肩書き（例：村の少年）")] private string clientTitle = "";
        [SerializeField] private CustomerLook look = new CustomerLook();
        [SerializeField, Tooltip("お客さんの立ち絵（2D のスプライト）。空なら、上の「見た目」から仮の絵を作る")]
        private Sprite portrait;

        [Header("セリフ（1行 = 1ページ）")]
        [SerializeField, TextArea(2, 4), Tooltip("相談（困りごとを話す）")] private List<string> requestLines = new List<string>();
        [SerializeField, TextArea(2, 3), Tooltip("「力になりたい」と答えたとき")] private List<string> acceptLines = new List<string>();
        [SerializeField, TextArea(2, 3), Tooltip("待っている間に話しかけたとき")] private List<string> waitingLines = new List<string>();
        [SerializeField, TextArea(2, 3), Tooltip("役に立つ瓶を分け与えたとき")] private List<string> successLines = new List<string>();
        [SerializeField, TextArea(2, 3), Tooltip("役に立たない瓶を渡そうとしたとき（瓶は返してくれる）")] private List<string> wrongLines = new List<string>();
        [SerializeField, TextArea(2, 3), Tooltip("「今は力になれない」と答えたとき")] private List<string> declineLines = new List<string>();

        [Header("困りごと")]
        [SerializeField, Tooltip("悩みの短い説明（相談メモに出す。例：炉の火が弱くて、鉄が赤くならない）")] private string troubleSummary = "";
        [SerializeField, Tooltip("見立て：どんな液体なら役に立ちそうか（相談メモに出す）")] private string goalSummary = "";
        [SerializeField, Tooltip("うわさ・手がかり（相談メモと、待っている間のセリフに出す）")] private string hint = "";
        [SerializeField, Tooltip("指定すると、この特別なポーションでなければ役に立たない")] private PotionConditionData requiredPotion;
        [SerializeField] private List<AttributeCondition> conditions = new List<AttributeCondition>();
        [SerializeField, Range(0, 5)] private int minStars = 0;

        [Header("作り方を教える")]
        [SerializeField, Tooltip("レシピ本で作り方を教えても解決できる（自分で作りたい人・これからも必要な人）")] private bool acceptsRecipe;
        [SerializeField, TextArea(2, 3), Tooltip("作り方を教えたとき（空なら「分け与えたとき」のセリフ）")] private List<string> taughtLines = new List<string>();

        [Header("お礼・進行")]
        [SerializeField, Min(0), Tooltip("お礼（瓶を分け与えたとき。作り方を教えたときは相談役の設定の割合）")] private int rewardGold = 50;
        [SerializeField, HideInInspector, Tooltip("サンプル生成で作った版（数値やセリフの調整を配るため）")] private int sampleVersion;
        [SerializeField, Tooltip("最後の相談（解決するとゲームクリア）")] private bool isFinal;

        public string QuestId => string.IsNullOrEmpty(questId) ? name : questId;
        public string ClientName => clientName;
        public string ClientTitle => clientTitle;
        public string ClientFullName => string.IsNullOrEmpty(clientTitle) ? clientName : $"{clientName}（{clientTitle}）";
        public CustomerLook Look => look;
        public Sprite Portrait => portrait;
        public IReadOnlyList<string> RequestLines => requestLines;
        public IReadOnlyList<string> AcceptLines => acceptLines;
        public IReadOnlyList<string> WaitingLines => waitingLines;
        public IReadOnlyList<string> SuccessLines => successLines;
        public IReadOnlyList<string> WrongLines => wrongLines;
        public IReadOnlyList<string> DeclineLines => declineLines;
        /// <summary>作り方を教えたときのセリフ（なければ分け与えたときのセリフ）</summary>
        public IReadOnlyList<string> TaughtLines => taughtLines != null && taughtLines.Count > 0 ? taughtLines : successLines;
        /// <summary>悩みの短い説明（なければ相談の最初のセリフ）</summary>
        public string TroubleSummary => !string.IsNullOrEmpty(troubleSummary) ? troubleSummary : requestLines.Count > 0 ? requestLines[requestLines.Count - 1] : "";
        /// <summary>見立て（どんな液体が役に立ちそうか）</summary>
        public string GoalSummary => goalSummary;
        public bool AcceptsRecipe => acceptsRecipe;
        public string Hint => hint;
        public int RewardGold => rewardGold;
        /// <summary>悩みに合う液体の条件（属性の値・割合など。調合台の見立てに目標の線として出す）</summary>
        public IReadOnlyList<AttributeCondition> Conditions => conditions;
        /// <summary>指定されていれば、この特別なポーションでなければ役に立たない</summary>
        public PotionConditionData RequiredPotion => requiredPotion;
        public bool IsFinal => isFinal;
        public int MinStars => minStars;

        public int SampleVersion => sampleVersion;

        /// <summary>分け与えようとした瓶が悩みの役に立つか</summary>
        public bool IsSatisfiedBy(PotionStack potion)
        {
            if (potion == null) return false;
            if (requiredPotion != null)
            {
                string special = !string.IsNullOrEmpty(potion.specialPotionId) ? potion.specialPotionId : potion.potionId;
                if (special != requiredPotion.PotionId) return false;
            }
            if (potion.stars < minStars) return false;

            AttributeVector attributes = potion.Attributes;
            foreach (AttributeCondition condition in conditions)
            {
                if (condition != null && !condition.Evaluate(attributes)) return false;
            }
            return true;
        }

        /// <summary>
        /// 液体の記録が相談の条件を満たすか。瓶を「分け与える」ときも、レシピ本で作り方を「教える」ときも、これで判定する
        /// （教えるときは在庫を減らさない）
        /// </summary>
        public bool IsSatisfiedBy(LiquidRecord liquid)
        {
            if (liquid == null) return false;
            if (requiredPotion != null && liquid.specialPotionId != requiredPotion.PotionId) return false;
            if (liquid.stars < minStars) return false;
            AttributeVector attributes = liquid.Attributes;
            foreach (AttributeCondition condition in conditions)
            {
                if (condition != null && !condition.Evaluate(attributes)) return false;
            }
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// サンプル生成用：セリフを「相談」の形に新しくする（フェーズ5作業計画5。見た目・お礼・特別なポーションの指定はそのまま）
        /// </summary>
        public void EditorUpdateTexts(int version, string[] request, string[] accept, string[] waiting, string[] success, string[] wrong,
                                      string[] decline, string trouble, string goalSummary, string hint, bool acceptsRecipe, string[] taught,
                                      AttributeCondition[] conditions, int minStars)
        {
            sampleVersion = version;
            requestLines = new List<string>(request);
            acceptLines = new List<string>(accept);
            waitingLines = new List<string>(waiting);
            successLines = new List<string>(success);
            wrongLines = new List<string>(wrong);
            declineLines = new List<string>(decline);
            troubleSummary = trouble;
            this.goalSummary = goalSummary;
            this.hint = hint;
            this.acceptsRecipe = acceptsRecipe;
            taughtLines = new List<string>(taught ?? new string[0]);
            this.conditions = new List<AttributeCondition>(conditions ?? new AttributeCondition[0]);
            this.minStars = minStars;
        }

        /// <summary>サンプル生成用：数値の調整（ヒント・条件・★）だけを新しくする</summary>
        public void EditorUpdateBalance(int version, string hint, AttributeCondition[] conditions, int minStars)
        {
            sampleVersion = version;
            this.hint = hint;
            this.conditions = new List<AttributeCondition>(conditions ?? new AttributeCondition[0]);
            this.minStars = minStars;
        }

        /// <summary>エディタ拡張（サンプルの相談の生成・自動テスト）用</summary>
        public void EditorSetup(string id, string clientName, string clientTitle, CustomerLook look,
                                string[] request, string[] accept, string[] waiting, string[] success, string[] wrong, string[] decline,
                                string goalSummary, string hint, PotionConditionData requiredPotion, AttributeCondition[] conditions,
                                int minStars, int rewardGold, bool isFinal, string trouble = "", bool acceptsRecipe = false,
                                string[] taught = null, int version = 2)
        {
            troubleSummary = trouble ?? "";
            this.acceptsRecipe = acceptsRecipe;
            taughtLines = new List<string>(taught ?? new string[0]);
            questId = id;
            this.clientName = clientName;
            this.clientTitle = clientTitle;
            this.look = look;
            requestLines = new List<string>(request);
            acceptLines = new List<string>(accept);
            waitingLines = new List<string>(waiting);
            successLines = new List<string>(success);
            wrongLines = new List<string>(wrong);
            declineLines = new List<string>(decline);
            this.goalSummary = goalSummary;
            this.hint = hint;
            this.requiredPotion = requiredPotion;
            this.conditions = new List<AttributeCondition>(conditions ?? new AttributeCondition[0]);
            this.minStars = minStars;
            this.rewardGold = rewardGold;
            this.isFinal = isFinal;
            sampleVersion = version;
        }
#endif
    }
}
