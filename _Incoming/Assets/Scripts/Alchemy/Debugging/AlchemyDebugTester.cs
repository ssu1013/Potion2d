using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 調合ロジックの動作確認用コンポーネント。
    /// インスペクターに並べた「テスト用の配合」を順に調合し、結果を Console に出す。
    /// 再生ボタン（Start時）でも、コンポーネント右上の︙メニュー（編集中）からでも実行できる。
    /// </summary>
    public class AlchemyDebugTester : MonoBehaviour
    {
        [Serializable]
        public class TestRecipe
        {
            [Tooltip("ログに出す見出し")]
            public string label = "テスト";

            public List<IngredientAmount> ingredients = new List<IngredientAmount>();

            public TestRecipe() { }

            public TestRecipe(string label, params IngredientAmount[] ingredients)
            {
                this.label = label;
                this.ingredients = new List<IngredientAmount>(ingredients);
            }
        }

        [SerializeField, Tooltip("空なら同じGameObjectの AlchemyManager を使う")]
        private AlchemyManager manager;

        [SerializeField, Tooltip("再生開始時に自動でテストを実行する")]
        private bool runOnStart = true;

        [SerializeField]
        private List<TestRecipe> testRecipes = new List<TestRecipe>();

        private void Start()
        {
            if (runOnStart) RunAllTests();
        }

        [ContextMenu("テスト調合をすべて実行")]
        public void RunAllTests()
        {
            if (manager == null) manager = GetComponent<AlchemyManager>();
            if (manager == null)
            {
                Debug.LogError("[AlchemyDebugTester] AlchemyManager が見つかりません。", this);
                return;
            }

            // アセットを編集した直後でも最新の値で計算されるように作り直す
            manager.RebuildBrewer();

            Debug.Log($"===== 調合テスト開始（{testRecipes.Count}件） =====", this);
            for (int i = 0; i < testRecipes.Count; i++)
            {
                TestRecipe test = testRecipes[i];
                if (test == null) continue;

                BrewResult result = manager.Brew(test.ingredients);
                Debug.Log($"<b>[{i + 1}] {test.label}</b>\n{result.ToDebugString(manager.Settings)}", this);
            }
            Debug.Log("===== 調合テスト終了 =====", this);
        }

#if UNITY_EDITOR
        /// <summary>エディタ拡張（サンプルデータ生成）用</summary>
        public void EditorSetup(AlchemyManager manager, IEnumerable<TestRecipe> recipes)
        {
            this.manager = manager;
            testRecipes = new List<TestRecipe>(recipes);
        }
#endif
    }
}
