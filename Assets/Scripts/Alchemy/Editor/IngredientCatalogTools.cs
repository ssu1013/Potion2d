using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Alchemy.EditorTools
{
    /// <summary>素材カタログ（IngredientDatabase）の作成・更新と、素材データのチェック</summary>
    public static class IngredientCatalog
    {
        public const string DatabasePath = "Assets/GameData/Alchemy/IngredientDatabase.asset";
        public const string DefaultIngredientFolder = "Assets/GameData/Alchemy/Ingredients";

        /// <summary>カタログのアセット（なければ作る）</summary>
        public static IngredientDatabase EnsureDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<IngredientDatabase>(DatabasePath);
            if (db != null) return db;
            EnsureFolder("Assets", "GameData");
            EnsureFolder("Assets/GameData", "Alchemy");
            db = ScriptableObject.CreateInstance<IngredientDatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
            Refresh(false);
            return db;
        }

        /// <summary>プロジェクト内のすべての素材データを集めてカタログに登録する。変化があれば true</summary>
        public static bool Refresh(bool log)
        {
            var db = AssetDatabase.LoadAssetAtPath<IngredientDatabase>(DatabasePath);
            if (db == null) return false;
            List<IngredientData> all = FindAllIngredients();
            all.Sort(IngredientDatabase.Compare);
            if (db.EditorSameAs(all))
            {
                if (log) Debug.Log($"[Potion Frontier] 素材カタログは最新です（{all.Count} 種類）");
                return false;
            }
            db.EditorSetIngredients(all);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            if (log) Debug.Log($"[Potion Frontier] 素材カタログを更新しました（{all.Count} 種類）", db);
            return true;
        }

        public static List<IngredientData> FindAllIngredients()
        {
            var list = new List<IngredientData>();
            foreach (string guid in AssetDatabase.FindAssets("t:IngredientData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<IngredientData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null) list.Add(data);
            }
            return list;
        }

        /// <summary>素材データのまちがいを探す（エラーは遊べなくなるもの、注意は直したほうがよいもの）</summary>
        public static void Validate(List<IngredientData> all, List<string> errors, List<string> warnings)
        {
            var ids = new Dictionary<string, IngredientData>(StringComparer.Ordinal);
            var idPattern = new Regex("^[A-Za-z0-9_\\-]+$");
            foreach (IngredientData d in all)
            {
                if (d == null) continue;
                string where = $"「{d.DisplayName}」({AssetDatabase.GetAssetPath(d)})";
                string id = d.IngredientId;
                if (ids.TryGetValue(id, out IngredientData other))
                    errors.Add($"ID「{id}」が重複しています：{where} と「{other.DisplayName}」。セーブデータの在庫が混ざるので、どちらかを変えてください");
                else ids.Add(id, d);

                if (!idPattern.IsMatch(id)) warnings.Add($"{where} のID「{id}」に半角英数字・_・- 以外が含まれています（使えますが、半角英数字を推奨）");
                if (string.IsNullOrWhiteSpace(d.DisplayName)) errors.Add($"{where} の名前が空です");

                var seen = new HashSet<PotionAttribute>();
                bool anyPositive = false;
                foreach (AttributeValue av in d.Attributes)
                {
                    if (!seen.Add(av.attribute)) warnings.Add($"{where} の属性「{av.attribute}」が2回書かれています（合計されます）");
                    if (av.value > 0f) anyPositive = true;
                }
                if (d.Category == IngredientCategory.MainMaterial && !anyPositive)
                    warnings.Add($"{where} は主素材ですが、プラスの属性がありません（入れても効き目が出ません）");
                if (d.Category == IngredientCategory.Catalyst && d.GlobalAmplifyPercent == 0f && d.Amplifiers.Count == 0 && !anyPositive)
                    warnings.Add($"{where} は触媒ですが、増幅も属性もありません");
                if (d.Category == IngredientCategory.BaseLiquid && !d.IsPoured)
                    warnings.Add($"{where} はベース液体ですが、形状が液体ではありません（注げません）");
                if (d.WorldPrefab != null && d.WorldPrefab.GetComponentInChildren<Renderer>(true) == null)
                    warnings.Add($"{where} のモデル（Prefab）に見た目（Renderer）がありません");
            }
        }

        public static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>"Assets/A/B" のようなフォルダを途中も含めて作る</summary>
        public static void EnsureFolderPath(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                EnsureFolder(current, parts[i]);
                current += "/" + parts[i];
            }
        }
    }

    /// <summary>素材データを作ったり消したりしたら、自動でカタログを更新する</summary>
    public class IngredientCatalogAutoRefresh : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (scheduled) return;
            if (!Touches(imported) && !Touches(deleted) && !Touches(moved)) return;
            scheduled = true;
            // 取り込み処理の最中にアセットを書き換えないよう、少し後で行う
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                IngredientCatalog.Refresh(false);
            };
        }

        private static bool Touches(string[] paths)
        {
            foreach (string p in paths)
            {
                if (!p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (p == IngredientCatalog.DatabasePath) continue;
                if (p.StartsWith("Assets/GameData/", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>列挙型を「英語名」「インスペクターの日本語名」「日本語名の（）より前」のどれでも読めるようにする</summary>
    public static class EnumText
    {
        public static bool TryParse<T>(string text, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var e = (T)f.GetValue(null);
                if (string.Equals(f.Name, t, StringComparison.OrdinalIgnoreCase)) { value = e; return true; }
                var attr = f.GetCustomAttribute<InspectorNameAttribute>();
                if (attr == null) continue;
                string jp = attr.displayName;
                if (jp == t || Short(jp) == t) { value = e; return true; }
            }
            return false;
        }

        /// <summary>表に書き出すときの名前（インスペクターの日本語名の（）より前）</summary>
        public static string Label<T>(T value) where T : struct, Enum
        {
            FieldInfo f = typeof(T).GetField(value.ToString());
            var attr = f != null ? f.GetCustomAttribute<InspectorNameAttribute>() : null;
            return attr != null ? Short(attr.displayName) : value.ToString();
        }

        private static string Short(string s)
        {
            int i = s.IndexOf('（');
            if (i < 0) i = s.IndexOf('(');
            return (i > 0 ? s.Substring(0, i) : s).Trim();
        }
    }
}
