using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 素材データ（ベース液体・主素材・触媒の共通クラス）。
    /// 1アセット = 1素材。属性パラメータは「1単位あたり」の値。
    ///
    /// 大量に作るときは、表計算（CSV）からまとめて作成・更新できる
    /// （メニュー Tools > Potion Frontier > 素材データ（表計算・チェック））。
    /// 見た目（3Dモデル・アイコン）は、このアセットのインスペクターで指定する。
    /// </summary>
    [CreateAssetMenu(fileName = "Ingredient_New", menuName = "Potion Frontier/素材データ (Ingredient)", order = 0)]
    public class IngredientData : ScriptableObject
    {
        [Header("基本情報")]
        [SerializeField, Tooltip("セーブデータや図鑑で使う一意なID（半角英数字と_推奨）。空欄ならアセット名を使う。\n※遊び始めたあとに変えると、セーブデータの在庫が引き継がれない")]
        private string ingredientId = "";

        [SerializeField]
        private string displayName = "新しい素材";

        [SerializeField, TextArea(2, 4)]
        private string description = "";

        [SerializeField, Tooltip("素材の絵（2D 版：トレイ・手に持ったとき・取引先帳・レシピ本・素材ノート）。Assets/CustomAssets/Images/Ingredients/<ID>.png を同じ名前で上書きすると差し替わる。空なら色と容器から自動で描く")]
        private Sprite icon;

        [SerializeField, Tooltip("ベース液体は必ず1種類、主素材は1種類以上、触媒は任意")]
        private IngredientCategory category = IngredientCategory.MainMaterial;

        [Header("見た目")]
        [SerializeField, Tooltip("素材・液体の色。ベース液体の色は完成品の色に混ざる（αは完成品の透明度になる）")]
        private Color color = Color.white;

        [Header("属性パラメータ（1単位あたり）")]
        [SerializeField]
        private List<AttributeValue> attributes = new List<AttributeValue>();

        [Header("触媒効果（主に触媒で使用）")]
        [SerializeField, Tooltip("全属性をまとめて増幅する%（+50 で 1.5倍）")]
        private float globalAmplifyPercent = 0f;

        [SerializeField, Tooltip("特定の属性だけを増幅・減衰する%")]
        private List<AttributeAmplifier> amplifiers = new List<AttributeAmplifier>();

        [Header("ベース液体専用")]
        [SerializeField, Tooltip("完成品の名前の末尾になる語（例：ポーション、オイル、シロップ）")]
        private string potionFormName = "ポーション";

        [SerializeField, Tooltip("このベース液体で作ったポーションの瓶のモデル（Prefab）。空なら標準の瓶")]
        private GameObject potionBottlePrefab;

        [Header("扱い方")]
        [SerializeField, Tooltip("液体＝長押しで注ぐ／固体・粉＝1回ずつ入れる。自動ならベース液体は液体、それ以外は固体")]
        private IngredientForm form = IngredientForm.Auto;

        [SerializeField, Min(0.05f), Tooltip("固体・粉を1回で入れる量（単位）。液体は注いだ分だけ入る")]
        private float amountPerUse = 1f;

        [Header("入手・分類")]
        [SerializeField, Range(1, 5), Tooltip("レア度（★の数）")]
        private int rarity = 1;

        [SerializeField, Min(0), Tooltip("取り寄せの値段（1単位あたりのゴールド）。使い放題の素材では「開拓の費用（自動）」の元になる。0 なら取引先帳に出ない")]
        private int buyPrice = 10;

        [SerializeField, Tooltip("在庫の扱い。無限 = 一度手に入れたら何度使っても減らない／消費 = 使った分だけ減る")]
        private StockPolicy stockPolicy = StockPolicy.Auto;

        [SerializeField, Min(-1), Tooltip("使い放題の素材：取引先帳で入手ルートを開拓する費用（1回だけ）。-1 = 自動（取り寄せの値段 × ゲームバランスの倍率）、0 = 開拓できない（ファームやお礼で手に入れる）")]
        private int unlockPrice = -1;

        [SerializeField, Min(0), Tooltip("取引先帳で取引できるようになる実績（解決した相談の数）。0 = はじめから")]
        private int requiredReputation = 0;

        [SerializeField, Tooltip("検索や絞り込みに使う分類（例：花、鉱石、森）")]
        private List<string> tags = new List<string>();

        [SerializeField, Tooltip("並べる順番（小さいほど先）。同じならIDの順")]
        private int sortOrder = 0;

        [Header("工程での性質（-1 = 自動）")]
        [SerializeField, Range(-1f, 1f), Tooltip("溶けやすさ 0〜1。低いほど、かき混ぜたり温めたりしないと溶けきらない（溶けた分だけ効く）。-1 = 自動（液体1・粉0.8・固体0.45）")]
        private float solubility = -1f;

        [SerializeField, Min(-1f), Tooltip("この温度（℃）を超えると効き目が少しずつ弱まる。0 = 熱に強い。-1 = 自動（主素材 85℃、それ以外は熱に強い）")]
        private float heatLimit = -1f;

        [SerializeField, Range(-1f, 1f), Tooltip("熱への弱さ 0〜1（大きいほど早く弱まる）。-1 = 自動（0.5）")]
        private float heatSensitivity = -1f;

        [SerializeField, Min(0f), Tooltip("この温度（℃）以上でしばらく温めると力が目覚めて強くなる。0 = なし")]
        private float activationTemp = 0f;

        [SerializeField, Tooltip("力が目覚めたときに強くなる割合（%）")]
        private float activationBonus = 50f;

        [Header("素材の性質（0 = なし。素材ノートで少しずつ見つかる）")]
        [SerializeField, Min(0f), Tooltip("この温度（℃）以上でないと溶けない（樹脂など）。0 = どの温度でも溶ける")]
        private float meltTemp = 0f;

        [SerializeField, Min(0f), Tooltip("入れてからこの回転数より多く混ぜると、崩れて効き目が減る（花びらなど）。0 = 崩れない")]
        private float fragileTurns = 0f;

        [SerializeField, Range(0f, 1f), Tooltip("沸騰させると効き目が飛んでいく強さ（香りの強い花など）。0 = 飛ばない")]
        private float volatility = 0f;

        [SerializeField, Min(0f), Tooltip("1回分入れると、フラスコの温度がおよそこれだけ（℃）下がる（100ml のとき）。0 = 冷えない")]
        private float coolTemp = 0f;

        [SerializeField, Tooltip("かき混ぜて溶かせる向き（どちらでも／時計回りだけ／反時計回りだけ）")]
        private StirDirection dissolveDirection = StirDirection.Any;

        [Header("2D の絵（Assets/CustomAssets/Images/Ingredients。同じ名前で上書きすると差し替わる）")]
        [SerializeField, Tooltip("フラスコに入れたときに落ちる欠片・底に沈む溶け残りの絵（<ID>_piece.png）。空なら共通の欠片を素材の色で塗る")]
        private Sprite pieceSprite;

        [Header("3Dモデル（自作モデルに差し替える所）")]
        [SerializeField, Tooltip("棚に置いたとき・手に持ったときのモデル（Prefab）。空なら「仮の容器」を使う")]
        private GameObject worldPrefab;

        [SerializeField, Tooltip("モデルが空のときに使う仮の容器の形")]
        private ContainerStyle container = ContainerStyle.Auto;

        [SerializeField, Tooltip("フラスコに入れるときに落ちる欠片のモデル（Prefab）。空なら素材の色の粒")]
        private GameObject dropPiecePrefab;

        public string IngredientId => string.IsNullOrEmpty(ingredientId) ? name : ingredientId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public IngredientCategory Category => category;
        public Color Color => color;
        public IReadOnlyList<AttributeValue> Attributes => attributes;
        public float GlobalAmplifyPercent => globalAmplifyPercent;
        public IReadOnlyList<AttributeAmplifier> Amplifiers => amplifiers;
        public string PotionFormName => potionFormName;
        public GameObject PotionBottlePrefab => potionBottlePrefab;
        public float AmountPerUse => amountPerUse;
        public int Rarity => rarity;
        public int BuyPrice => buyPrice;
        /// <summary>開拓の費用の設定値（-1 = 自動）。実際の費用は UnlockCost で求める</summary>
        public int UnlockPriceSetting => unlockPrice;
        public int RequiredReputation => requiredReputation;
        public IReadOnlyList<string> Tags => tags;
        public int SortOrder => sortOrder;
        public GameObject WorldPrefab => worldPrefab;
        public GameObject DropPiecePrefab => dropPiecePrefab;
        public StockPolicy StockPolicySetting => stockPolicy;
        public float ActivationTemp => activationTemp;
        public float ActivationBonus => activationBonus;
        public float MeltTemp => meltTemp;
        public float FragileTurns => fragileTurns;
        public float Volatility => volatility;
        public float CoolTemp => coolTemp;
        public StirDirection DissolveDirection => dissolveDirection;
        public Sprite PieceSprite => pieceSprite;

        /// <summary>溶けやすさ（自動を解決したもの）</summary>
        public float Solubility
        {
            get
            {
                if (solubility >= 0f) return Mathf.Clamp01(solubility);
                switch (Form)
                {
                    case IngredientForm.Liquid: return 1f;
                    case IngredientForm.Powder: return 0.8f;
                    default: return 0.45f;
                }
            }
        }

        /// <summary>熱で弱まり始める温度（0 = 熱に強い）</summary>
        public float HeatLimit
        {
            get
            {
                if (heatLimit >= 0f) return heatLimit;
                return category == IngredientCategory.MainMaterial ? 85f : 0f;
            }
        }

        public float HeatSensitivity => heatSensitivity >= 0f ? Mathf.Clamp01(heatSensitivity) : 0.5f;

        /// <summary>在庫が減らない素材か（自動ならレア度で決める。境目は呼ぶ側が渡す）</summary>
        public bool IsInfinite(int infiniteRarityMax)
        {
            switch (stockPolicy)
            {
                case StockPolicy.Infinite: return true;
                case StockPolicy.Consumable: return false;
                default: return rarity <= infiniteRarityMax;
            }
        }

        /// <summary>入手ルートを開拓する費用（自動を解決したもの）。0 以下なら開拓できない</summary>
        public int UnlockCost(int priceMultiplier)
        {
            if (unlockPrice >= 0) return unlockPrice;
            return buyPrice > 0 ? buyPrice * Mathf.Max(1, priceMultiplier) : 0;
        }

        /// <summary>実際の形状（自動を解決したもの）</summary>
        public IngredientForm Form
        {
            get
            {
                if (form != IngredientForm.Auto) return form;
                return category == IngredientCategory.BaseLiquid ? IngredientForm.Liquid : IngredientForm.Solid;
            }
        }

        /// <summary>注いで入れる素材か（液体）</summary>
        public bool IsPoured => Form == IngredientForm.Liquid;

        /// <summary>実際の仮の容器（自動を解決したもの）</summary>
        public ContainerStyle Container
        {
            get
            {
                if (container != ContainerStyle.Auto) return container;
                switch (category)
                {
                    case IngredientCategory.BaseLiquid: return ContainerStyle.TallBottle;
                    case IngredientCategory.Catalyst: return Form == IngredientForm.Powder ? ContainerStyle.Pouch : ContainerStyle.RoundFlask;
                    default: return Form == IngredientForm.Powder ? ContainerStyle.Pouch : ContainerStyle.Jar;
                }
            }
        }

        /// <summary>タグを持っているか（大文字小文字は区別しない）</summary>
        public bool HasTag(string tag)
        {
            foreach (string t in tags)
            {
                if (string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        /// <summary>エディタ拡張（サンプルデータ生成）用。ゲーム中には使わない</summary>
        public void EditorSetup(
            string id,
            string displayName,
            IngredientCategory category,
            Color color,
            string description,
            AttributeValue[] attributes,
            float globalAmplifyPercent = 0f,
            AttributeAmplifier[] amplifiers = null,
            string potionFormName = "ポーション")
        {
            ingredientId = id;
            this.displayName = displayName;
            this.category = category;
            this.color = color;
            this.description = description;
            this.attributes = new List<AttributeValue>(attributes ?? new AttributeValue[0]);
            this.globalAmplifyPercent = globalAmplifyPercent;
            this.amplifiers = new List<AttributeAmplifier>(amplifiers ?? new AttributeAmplifier[0]);
            this.potionFormName = potionFormName;
        }

        /// <summary>エディタ拡張用：入手・分類などの追加項目</summary>
        public void EditorSetupDetails(int rarity, int buyPrice, string[] tags, IngredientForm form = IngredientForm.Auto,
                                       ContainerStyle container = ContainerStyle.Auto, float amountPerUse = 1f, int sortOrder = 0)
        {
            this.rarity = Mathf.Clamp(rarity, 1, 5);
            this.buyPrice = Mathf.Max(0, buyPrice);
            this.tags = new List<string>(tags ?? new string[0]);
            this.form = form;
            this.container = container;
            this.amountPerUse = Mathf.Max(0.05f, amountPerUse);
            this.sortOrder = sortOrder;
        }

        /// <summary>エディタ拡張用：取引先帳での扱い（開拓の費用・必要な実績）</summary>
        public void EditorSetTrade(int unlockPrice, int requiredReputation)
        {
            this.unlockPrice = Mathf.Max(-1, unlockPrice);
            this.requiredReputation = Mathf.Max(0, requiredReputation);
        }

        /// <summary>エディタ拡張用：工程での性質（溶けやすさ・熱・目覚め）</summary>
        public void EditorSetProcess(float solubility, float heatLimit, float heatSensitivity, float activationTemp, float activationBonus)
        {
            this.solubility = solubility;
            this.heatLimit = heatLimit;
            this.heatSensitivity = heatSensitivity;
            this.activationTemp = activationTemp;
            this.activationBonus = activationBonus;
        }

        /// <summary>エディタ拡張用：素材の性質（溶ける温度・崩れる・香りが飛ぶ・冷える・溶ける向き）</summary>
        public void EditorSetTraits(float meltTemp, float fragileTurns, float volatility, float coolTemp, StirDirection dissolveDirection)
        {
            this.meltTemp = Mathf.Max(0f, meltTemp);
            this.fragileTurns = Mathf.Max(0f, fragileTurns);
            this.volatility = Mathf.Clamp01(volatility);
            this.coolTemp = Mathf.Max(0f, coolTemp);
            this.dissolveDirection = dissolveDirection;
        }

        /// <summary>素材の性質がすべて「なし」のままか</summary>
        public bool EditorTraitsAreDefault => meltTemp <= 0f && fragileTurns <= 0f && volatility <= 0f && coolTemp <= 0f && dissolveDirection == StirDirection.Any;

        /// <summary>エディタ拡張用：2D の絵（空の欄だけ入れる）。入れたら true</summary>
        public bool EditorFillArt(Sprite iconSprite, Sprite piece)
        {
            bool changed = false;
            if (icon == null && iconSprite != null) { icon = iconSprite; changed = true; }
            if (pieceSprite == null && piece != null) { pieceSprite = piece; changed = true; }
            return changed;
        }

        /// <summary>工程での性質がすべて初期値（自動）のままか</summary>
        public bool EditorProcessIsDefault => solubility < 0f && heatLimit < 0f && heatSensitivity < 0f && activationTemp <= 0f;

        /// <summary>エディタ拡張用：形状と仮の容器だけを決める</summary>
        public void EditorSetShape(IngredientForm form, ContainerStyle container)
        {
            this.form = form;
            this.container = container;
        }

        /// <summary>形状・容器がどちらも「自動」のままか</summary>
        public bool EditorShapeIsAuto => form == IngredientForm.Auto && container == ContainerStyle.Auto;

        /// <summary>追加項目がまだ初期値のままか（古いバージョンで作った素材の移行判定用）</summary>
        public bool EditorDetailsAreDefault => rarity == 1 && buyPrice == 10 && tags.Count == 0 && sortOrder == 0;
#endif
    }
}
