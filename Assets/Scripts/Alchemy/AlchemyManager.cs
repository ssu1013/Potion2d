using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 調合マネージャー。シーンに1つ置き、UI・注ぐ操作・相談システムからはここを呼ぶ。
    ///   ・フラスコの工程の計算 … CreateFlask で FlaskSimulation を作る（反応データもここで渡す）
    ///   ・瓶詰め              … Bottle でフラスコの中身から液体の記録を作り、これまでに作った液体の一覧（Liquids）に登録する
    ///   ・旧方式の一発調合    … Brew（フェーズ4までの計算。テスト用に残してある）
    /// 計算そのものは純粋な C# クラスに任せ、このクラスは「データの受け渡し」と「結果の通知」だけを担当する。
    /// </summary>
    [DisallowMultipleComponent]
    public class AlchemyManager : MonoBehaviour
    {
        [SerializeField, Tooltip("調合ルール設定（AlchemySettings アセット）")]
        private AlchemySettings settings;

        [SerializeField, Tooltip("判定に使う完成条件データ。右クリックメニューから自動登録もできる")]
        private List<PotionConditionData> specialRecipes = new List<PotionConditionData>();

        [SerializeField, Tooltip("素材どうしの特別な反応。右クリックメニューから自動登録もできる")]
        private List<ReactionData> reactions = new List<ReactionData>();

        private LiquidAnalyzer analyzer;

        /// <summary>これまでに作った液体（セーブに入る）</summary>
        public LiquidRegistry Liquids { get; } = new LiquidRegistry();

        /// <summary>瓶詰めしたとき（新発見なら isNew）</summary>
        public event Action<LiquidRecord, bool> Bottled;

        public IReadOnlyList<ReactionData> Reactions => reactions;

        /// <summary>調合が行われるたびに呼ばれる（UI表示・レシピ本への記録・相談の判定などをここにつなぐ）</summary>
        public event Action<BrewResult> Brewed;

        private PotionBrewer brewer;

        public AlchemySettings Settings => settings;
        public IReadOnlyList<PotionConditionData> SpecialRecipes => specialRecipes;

        /// <summary>計算エンジン（初回アクセス時に作成。編集モードの右クリック実行でも動く）</summary>
        private PotionBrewer Brewer
        {
            get
            {
                if (brewer == null && settings != null) brewer = new PotionBrewer(settings, specialRecipes);
                return brewer;
            }
        }

        private LiquidAnalyzer Analyzer
        {
            get
            {
                if (analyzer == null && settings != null) analyzer = new LiquidAnalyzer(settings, specialRecipes);
                return analyzer;
            }
        }

        /// <summary>新しいフラスコ（工程の計算）を作る</summary>
        public FlaskSimulation CreateFlask(float capacityMl)
        {
            return settings != null ? new FlaskSimulation(settings, reactions, capacityMl) : null;
        }

        /// <summary>今のフラスコの中身なら、どんな液体になるか（登録はしない。予想表示用）</summary>
        public LiquidRecord Preview(FlaskSimulation flask)
        {
            if (flask == null || settings == null) return null;
            return Analyzer.Analyze(flask.Snapshot());
        }

        /// <summary>中身の記録を作る（登録はしない）</summary>
        public LiquidRecord Analyze(LiquidState state)
        {
            return state != null && settings != null ? Analyzer.Analyze(state) : null;
        }

        /// <summary>
        /// 瓶詰めする：フラスコから ml だけ取り出して液体の記録を作り、一覧に登録する。
        /// 返すのは一覧の記録（同じ液体を前にも作っていれば、そのときの記録）。isNew = 初めて作った液体か
        /// </summary>
        public LiquidRecord Bottle(FlaskSimulation flask, float ml, out float bottledMl, out bool isNew)
        {
            isNew = false;
            bottledMl = 0f;
            if (flask == null || settings == null) return null;
            LiquidState state = flask.Bottle(ml, out bottledMl);
            if (state == null) return null;
            LiquidRecord record = Liquids.Register(Analyzer.Analyze(state), out isNew);
            if (Bottled != null) Bottled(record, isNew);
            return record;
        }

        /// <summary>調合を実行する（旧方式）</summary>
        public BrewResult Brew(IReadOnlyList<IngredientAmount> ingredients)
        {
            if (settings == null)
            {
                Debug.LogError("[AlchemyManager] Settings に AlchemySettings が設定されていません。", this);
                return BrewResult.CreateInvalid("AlchemyManager に調合ルール設定がありません");
            }

            BrewResult result = Brewer.Brew(ingredients);
            if (Brewed != null) Brewed(result);
            return result;
        }

        /// <summary>判定せずに属性値だけを計算する（注いでいる最中のプレビュー用）</summary>
        public AttributeVector PreviewAttributes(IReadOnlyList<IngredientAmount> ingredients)
        {
            return settings != null ? Brewer.CalculateAttributes(ingredients) : new AttributeVector();
        }

        /// <summary>判定せずに液体の色だけを計算する（注いでいる最中の見た目用）</summary>
        public Color PreviewColor(IReadOnlyList<IngredientAmount> ingredients)
        {
            return settings != null ? Brewer.PreviewColor(ingredients) : Color.white;
        }

        /// <summary>完成条件データの優先度などを実行中に変えたときに呼ぶ</summary>
        public void RebuildBrewer()
        {
            brewer = null;
            analyzer = null;
            if (settings != null) settings.InvalidateCache();
        }

        private void OnValidate()
        {
            brewer = null;
            analyzer = null;
        }

#if UNITY_EDITOR
        [ContextMenu("プロジェクト内の完成条件データをすべて登録")]
        private void CollectAllRecipesInProject()
        {
            UnityEditor.Undo.RecordObject(this, "Collect Potion Conditions");
            specialRecipes.Clear();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:PotionConditionData"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var recipe = UnityEditor.AssetDatabase.LoadAssetAtPath<PotionConditionData>(path);
                if (recipe != null) specialRecipes.Add(recipe);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            brewer = null;
            Debug.Log($"[AlchemyManager] 完成条件データを {specialRecipes.Count} 件登録しました。", this);
        }

        [ContextMenu("プロジェクト内の反応データをすべて登録")]
        private void CollectAllReactionsInProject()
        {
            UnityEditor.Undo.RecordObject(this, "Collect Reactions");
            reactions.Clear();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ReactionData"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var reaction = UnityEditor.AssetDatabase.LoadAssetAtPath<ReactionData>(path);
                if (reaction != null) reactions.Add(reaction);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[AlchemyManager] 反応データを {reactions.Count} 件登録しました。", this);
        }

        /// <summary>エディタ拡張（サンプルデータ生成）用</summary>
        public void EditorSetup(AlchemySettings settings, IEnumerable<PotionConditionData> recipes, IEnumerable<ReactionData> reactions = null)
        {
            this.settings = settings;
            specialRecipes = new List<PotionConditionData>(recipes);
            this.reactions = reactions != null ? new List<ReactionData>(reactions) : new List<ReactionData>();
            brewer = null;
            analyzer = null;
        }
#endif
    }
}
