using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Alchemy.EditorTools
{
    /// <summary>
    /// 素材データをまとめて扱うウィンドウ。
    /// Tools > Potion Frontier > 素材データ（表計算・チェック）
    ///   ・表計算（CSV）に書き出す → Excel / Googleスプレッドシートで編集 → 取り込む
    ///   ・素材カタログの更新
    ///   ・まちがいのチェック
    /// </summary>
    public class IngredientDataWindow : EditorWindow
    {
        private string csvPath = IngredientCsv.DefaultPath;
        private string newFolder = IngredientCatalog.DefaultIngredientFolder;
        private readonly List<string> messages = new List<string>();
        private Vector2 scroll;

        [MenuItem("Tools/Potion Frontier/素材データ（表計算・チェック）", false, 20)]
        public static void Open()
        {
            var window = GetWindow<IngredientDataWindow>("素材データ");
            window.minSize = new Vector2(460f, 420f);
        }

        private void OnGUI()
        {
            IngredientDatabase db = AssetDatabase.LoadAssetAtPath<IngredientDatabase>(IngredientCatalog.DatabasePath);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("素材カタログ", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(db, typeof(IngredientDatabase), false);
                EditorGUILayout.LabelField(db != null ? $"{db.Count} 種類" : "未作成", GUILayout.Width(70f));
            }
            if (GUILayout.Button("カタログを更新（Assets/GameData の素材を集める）"))
            {
                IngredientCatalog.EnsureDatabase();
                IngredientCatalog.Refresh(true);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("表計算（CSV）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "① 書き出す → ② Excel や Googleスプレッドシートで編集 → ③ 取り込む\n" +
                "・1行 = 1素材。IDが同じなら更新、新しいIDなら素材データを新しく作ります\n" +
                "・空欄は「変更しない」。リストやモデルを消すときは「なし」\n" +
                "・属性の書き方：回復:20; 火:-5　タグ：花; 森\n" +
                "・Excel は「CSV UTF-8（コンマ区切り）」で保存してください", MessageType.Info);
            csvPath = EditorGUILayout.TextField("CSVファイル", csvPath);
            newFolder = EditorGUILayout.TextField("新しい素材の保存先", newFolder);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("書き出す"))
                {
                    int n = IngredientCsv.Export(csvPath);
                    Add($"{n} 種類を書き出しました: {csvPath}");
                    EditorUtility.RevealInFinder(IngredientCsv.ToFullPath(csvPath));
                }
                if (GUILayout.Button("取り込む"))
                {
                    if (EditorUtility.DisplayDialog("素材データを取り込む", $"{csvPath} の内容で素材データを作成・更新します。", "取り込む", "やめる"))
                    {
                        IngredientCsv.Report report = IngredientCsv.Import(csvPath, newFolder);
                        messages.Clear();
                        Add("取り込み結果：" + report.Summary);
                        foreach (string e in report.errors) Add("✖ " + e);
                        foreach (string w in report.warnings) Add("△ " + w);
                    }
                }
                if (GUILayout.Button("場所を開く", GUILayout.Width(90f)))
                {
                    EditorUtility.RevealInFinder(IngredientCsv.ToFullPath(csvPath));
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("チェック", EditorStyles.boldLabel);
            if (GUILayout.Button("素材データのまちがいを探す"))
            {
                messages.Clear();
                var errors = new List<string>();
                var warnings = new List<string>();
                List<IngredientData> all = IngredientCatalog.FindAllIngredients();
                IngredientCatalog.Validate(all, errors, warnings);
                Add($"{all.Count} 種類を調べました：エラー {errors.Count}・注意 {warnings.Count}");
                foreach (string e in errors) Add("✖ " + e);
                foreach (string w in warnings) Add("△ " + w);
                if (errors.Count == 0 && warnings.Count == 0) Add("問題は見つかりませんでした");
            }

            EditorGUILayout.Space(6f);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string m in messages) EditorGUILayout.LabelField(m, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Add(string message)
        {
            messages.Add(message);
            Debug.Log("[Potion Frontier] " + message);
        }
    }
}
