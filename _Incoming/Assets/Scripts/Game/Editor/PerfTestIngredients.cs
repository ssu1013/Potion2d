using System.IO;
using System.Text;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// 開発用：素材が数百種類になっても動くかを確かめるため、ダミーの素材を CSV から読み込む／消す。
    /// 作る素材の ID は「perf_001」…で、Assets/GameData/Alchemy/Ingredients/PerfTest に入る（消すときはフォルダごと消す）。
    /// </summary>
    public static class PerfTestIngredients
    {
        public const string Folder = IngredientCatalog.DefaultIngredientFolder + "/PerfTest";
        private const string CsvPath = "Temp/perf_ingredients.csv";

        private static readonly string[] Categories = { "ベース液体", "主素材", "主素材", "主素材", "触媒" };
        private static readonly string[] Attributes = { "回復", "火", "氷", "成長促進", "透明化", "毒", "光", "風" };
        private static readonly string[] Words = { "月", "星", "森", "霧", "炎", "雪", "花", "石", "露", "風" };

        public static string Create(int count)
        {
            var rng = new System.Random(12345);
            var sb = new StringBuilder();
            sb.AppendLine("ID,名前,カテゴリ,色,属性,レア度,価格,タグ,説明");
            for (int i = 1; i <= count; i++)
            {
                string category = Categories[i % Categories.Length];
                Color c = Color.HSVToRGB((float)rng.NextDouble(), 0.45f + (float)rng.NextDouble() * 0.4f, 0.65f + (float)rng.NextDouble() * 0.3f);
                string a1 = Attributes[rng.Next(Attributes.Length)];
                string a2 = Attributes[rng.Next(Attributes.Length)];
                string word = Words[rng.Next(Words.Length)];
                string attrs = category == "ベース液体" ? $"{a1}:{rng.Next(1, 5)}" : $"{a1}:{rng.Next(5, 30)}; {a2}:{rng.Next(-8, 12)}";
                sb.Append($"perf_{i:000},試験素材{i:000}（{word}）,{category},#{ColorUtility.ToHtmlStringRGB(c)},\"{attrs}\",{1 + rng.Next(5)},{rng.Next(1, 40)},試験;{word},動作確認用のダミー素材\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(CsvPath));
            File.WriteAllText(CsvPath, sb.ToString(), new UTF8Encoding(true));
            IngredientCatalog.EnsureFolderPath(Folder);
            IngredientCsv.Report report = IngredientCsv.Import(CsvPath, Folder);
            IngredientCatalog.Refresh(false);
            return $"作成 {report.created} ／ 更新 {report.updated} ／ 警告 {report.warnings.Count} ／ エラー {report.errors.Count}" +
                   (report.errors.Count > 0 ? "\n  " + string.Join("\n  ", report.errors) : "");
        }

        public static string Remove()
        {
            bool removed = AssetDatabase.IsValidFolder(Folder) && AssetDatabase.DeleteAsset(Folder);
            IngredientCatalog.Refresh(false);
            return removed ? "ダミー素材を消しました" : "ダミー素材はありませんでした";
        }
    }
}
