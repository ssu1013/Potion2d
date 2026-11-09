using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Alchemy.EditorTools
{
    /// <summary>
    /// 素材データと表計算（CSV）の取り込み・書き出し。
    /// ・1行 = 1素材。1行目は見出し（列の順番は自由。見出しの名前で判断する）
    /// ・ID が同じ素材があれば更新、なければ新しく作る
    /// ・空欄は「変更しない」（新しく作る素材では初期値）。リストやモデルを消したいときは「なし」と書く
    /// ・属性は「回復:20; 火:-5」、属性増幅%は「成長促進:80; 光:-50」、タグは「花; 森」のように書く
    /// ・工程での性質（溶けやすさ・熱に弱い温度・熱への弱さ）は「自動」と書くと、形やカテゴリから決まる値になる
    /// ・Excel で保存するときは「CSV UTF-8（コンマ区切り）」を選ぶ（Shift_JIS でも読めるようにしてある）
    /// </summary>
    public static class IngredientCsv
    {
        public const string DefaultPath = "Assets/GameData/Alchemy/Ingredients.csv";

        // 見出し（書き出し順）。取り込みは、この名前か英語名で列を探す
        private static readonly string[][] Columns =
        {
            new[] { "ID", "id" },
            new[] { "名前", "name" },
            new[] { "カテゴリ", "category" },
            new[] { "形状", "form" },
            new[] { "色", "color" },
            new[] { "属性", "attributes" },
            new[] { "全属性増幅%", "amplify_all" },
            new[] { "属性増幅%", "amplifiers" },
            new[] { "完成品の形態", "form_name" },
            new[] { "1回の量", "amount_per_use" },
            new[] { "レア度", "rarity" },
            new[] { "タグ", "tags" },
            new[] { "並び順", "sort" },
            new[] { "容器", "container" },
            new[] { "モデル", "model" },
            new[] { "瓶モデル", "bottle_model" },
            new[] { "欠片モデル", "piece_model" },
            new[] { "アイコン", "icon" },
            new[] { "説明", "description" },
            new[] { "溶けやすさ", "solubility" },
            new[] { "熱に弱い温度", "heat_limit" },
            new[] { "熱への弱さ", "heat_sensitivity" },
            new[] { "目覚める温度", "activation_temp" },
            new[] { "目覚めの強さ%", "activation_bonus" },
            new[] { "届くのに必要な発見", "discoveries", "必要な実績", "reputation" },
        };

        private const string Auto = "自動";

        private const string Clear = "なし";

        public sealed class Report
        {
            public int created;
            public int updated;
            public int unchanged;
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> errors = new List<string>();

            public string Summary => $"新規 {created} ／ 更新 {updated} ／ 変更なし {unchanged}（注意 {warnings.Count}・エラー {errors.Count}）";
        }

        // ------------------------------------------------------------------
        // 書き出し
        // ------------------------------------------------------------------

        public static int Export(string path)
        {
            List<IngredientData> all = IngredientCatalog.FindAllIngredients();
            all.Sort(IngredientDatabase.Compare);
            AlchemySettings settings = FindSettings();

            var sb = new StringBuilder();
            var header = new List<string>();
            foreach (string[] c in Columns) header.Add(c[0]);
            AppendRow(sb, header);

            foreach (IngredientData d in all)
            {
                var so = new SerializedObject(d);
                var row = new List<string>
                {
                    d.IngredientId,
                    d.DisplayName,
                    EnumText.Label(d.Category),
                    EnumText.Label((IngredientForm)so.FindProperty("form").intValue),
                    "#" + ColorUtility.ToHtmlStringRGBA(d.Color),
                    AttributeList(d.Attributes, settings),
                    d.GlobalAmplifyPercent == 0f ? "" : Num(d.GlobalAmplifyPercent),
                    AmplifierList(d.Amplifiers, settings),
                    d.PotionFormName,
                    Num(d.AmountPerUse),
                    d.Rarity.ToString(CultureInfo.InvariantCulture),
                    string.Join("; ", d.Tags),
                    d.SortOrder.ToString(CultureInfo.InvariantCulture),
                    EnumText.Label((ContainerStyle)so.FindProperty("container").intValue),
                    AssetPath(d.WorldPrefab),
                    AssetPath(d.PotionBottlePrefab),
                    AssetPath(d.DropPiecePrefab),
                    AssetPath(d.Icon),
                    d.Description,
                    AutoNum(so.FindProperty("solubility").floatValue),
                    AutoNum(so.FindProperty("heatLimit").floatValue),
                    AutoNum(so.FindProperty("heatSensitivity").floatValue),
                    d.ActivationTemp > 0f ? Num(d.ActivationTemp) : "",
                    d.ActivationTemp > 0f ? Num(d.ActivationBonus) : "",
                    d.DiscoveriesToUnlock.ToString(CultureInfo.InvariantCulture),
                };
                AppendRow(sb, row);
            }

            string full = ToFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString(), new UTF8Encoding(true)); // Excel で文字化けしないよう BOM 付き
            AssetDatabase.Refresh();
            return all.Count;
        }

        // ------------------------------------------------------------------
        // 取り込み
        // ------------------------------------------------------------------

        public static Report Import(string path, string newAssetFolder)
        {
            var report = new Report();
            string full = ToFullPath(path);
            if (!File.Exists(full))
            {
                report.errors.Add("ファイルが見つかりません: " + path);
                return report;
            }

            string text = ReadText(full, report);
            List<List<string>> rows = Parse(text);
            if (rows.Count < 2)
            {
                report.errors.Add("データの行がありません（1行目は見出し、2行目から素材）");
                return report;
            }

            var index = new Dictionary<int, int>(); // 列番号 → Columns の番号
            List<string> head = rows[0];
            for (int c = 0; c < head.Count; c++)
            {
                string h = head[c].Trim().TrimStart('﻿');
                for (int k = 0; k < Columns.Length; k++)
                {
                    if (Array.Exists(Columns[k], name => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        index[c] = k;
                    }
                }
            }
            if (!index.ContainsValue(0))
            {
                report.errors.Add("「ID」の列がありません");
                return report;
            }

            AlchemySettings settings = FindSettings();
            var existing = new Dictionary<string, IngredientData>(StringComparer.Ordinal);
            foreach (IngredientData d in IngredientCatalog.FindAllIngredients())
            {
                if (!existing.ContainsKey(d.IngredientId)) existing.Add(d.IngredientId, d);
            }

            IngredientCatalog.EnsureFolderPath(newAssetFolder);
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int r = 1; r < rows.Count; r++)
                {
                    List<string> row = rows[r];
                    var cells = new string[Columns.Length];
                    bool any = false;
                    for (int c = 0; c < row.Count; c++)
                    {
                        if (!index.TryGetValue(c, out int k)) continue;
                        cells[k] = row[c].Trim();
                        if (cells[k].Length > 0) any = true;
                    }
                    if (!any) continue; // 空行

                    string line = $"{r + 1}行目";
                    string id = cells[0];
                    if (string.IsNullOrEmpty(id))
                    {
                        report.errors.Add($"{line}: ID が空なので読み飛ばしました");
                        continue;
                    }
                    if (!seenInFile.Add(id))
                    {
                        report.errors.Add($"{line}: ID「{id}」が表の中で重複しています（後の行は読み飛ばしました）");
                        continue;
                    }

                    bool isNew = !existing.TryGetValue(id, out IngredientData data);
                    if (isNew)
                    {
                        data = ScriptableObject.CreateInstance<IngredientData>();
                        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{newAssetFolder}/Ingredient_{SafeFileName(id)}.asset");
                        AssetDatabase.CreateAsset(data, assetPath);
                        existing.Add(id, data);
                    }

                    var so = new SerializedObject(data);
                    bool changed = ApplyRow(so, cells, settings, line, report);
                    if (isNew)
                    {
                        so.FindProperty("ingredientId").stringValue = id;
                        if (string.IsNullOrEmpty(cells[1])) so.FindProperty("displayName").stringValue = id;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        report.created++;
                    }
                    else if (changed)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        report.updated++;
                    }
                    else
                    {
                        report.unchanged++;
                    }
                    EditorUtility.SetDirty(data);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            IngredientCatalog.Refresh(false);
            return report;
        }

        private static bool ApplyRow(SerializedObject so, string[] cells, AlchemySettings settings, string line, Report report)
        {
            bool changed = false;

            void SetString(string prop, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                SerializedProperty p = so.FindProperty(prop);
                string v = value == Clear && prop != "displayName" ? "" : value;
                if (p.stringValue != v) { p.stringValue = v; changed = true; }
            }

            void SetInt(string prop, string value, int min, int max)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    report.warnings.Add($"{line}: 「{value}」は整数ではありません（{prop}）");
                    return;
                }
                v = Mathf.Clamp(v, min, max);
                SerializedProperty p = so.FindProperty(prop);
                if (p.intValue != v) { p.intValue = v; changed = true; }
            }

            void SetFloat(string prop, string value, float min)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!TryFloat(value, out float v))
                {
                    report.warnings.Add($"{line}: 「{value}」は数値ではありません（{prop}）");
                    return;
                }
                v = Mathf.Max(min, v);
                SerializedProperty p = so.FindProperty(prop);
                if (!Mathf.Approximately(p.floatValue, v)) { p.floatValue = v; changed = true; }
            }

            // 「自動」は -1（形やカテゴリから決まる値）
            void SetAutoFloat(string prop, string value, float min, float max)
            {
                if (string.IsNullOrEmpty(value)) return;
                float v;
                if (value == Auto || value == Clear) v = -1f;
                else if (TryFloat(value, out float parsed)) v = Mathf.Clamp(parsed, min, max);
                else
                {
                    report.warnings.Add($"{line}: 「{value}」は数値でも「自動」でもありません（{prop}）");
                    return;
                }
                SerializedProperty p = so.FindProperty(prop);
                if (!Mathf.Approximately(p.floatValue, v)) { p.floatValue = v; changed = true; }
            }

            void SetEnum<T>(string prop, string value) where T : struct, Enum
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!EnumText.TryParse(value, out T e))
                {
                    report.warnings.Add($"{line}: 「{value}」は {prop} として読めません");
                    return;
                }
                SerializedProperty p = so.FindProperty(prop);
                int v = Convert.ToInt32(e);
                if (p.intValue != v) { p.intValue = v; changed = true; }
            }

            void SetObject<T>(string prop, string value) where T : UnityEngine.Object
            {
                if (string.IsNullOrEmpty(value)) return;
                SerializedProperty p = so.FindProperty(prop);
                T obj = null;
                if (value != Clear)
                {
                    string path = value.StartsWith("Assets/", StringComparison.Ordinal) ? value : "Assets/" + value.TrimStart('/');
                    obj = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (obj == null)
                    {
                        report.warnings.Add($"{line}: 「{value}」が見つかりません（{prop}）");
                        return;
                    }
                }
                if (p.objectReferenceValue != obj) { p.objectReferenceValue = obj; changed = true; }
            }

            SetString("displayName", cells[1]);
            SetEnum<IngredientCategory>("category", cells[2]);
            SetEnum<IngredientForm>("form", cells[3]);
            if (!string.IsNullOrEmpty(cells[4]))
            {
                string hex = cells[4].StartsWith("#") ? cells[4] : "#" + cells[4];
                if (ColorUtility.TryParseHtmlString(hex, out Color color))
                {
                    SerializedProperty p = so.FindProperty("color");
                    if (p.colorValue != color) { p.colorValue = color; changed = true; }
                }
                else report.warnings.Add($"{line}: 色「{cells[4]}」が読めません（#RRGGBB の形で書く）");
            }
            if (!string.IsNullOrEmpty(cells[5])) changed |= SetPairs(so.FindProperty("attributes"), "value", cells[5], settings, line, report);
            SetFloat("globalAmplifyPercent", cells[6] == Clear ? "0" : cells[6], -100f);
            if (!string.IsNullOrEmpty(cells[7])) changed |= SetPairs(so.FindProperty("amplifiers"), "percent", cells[7], settings, line, report);
            SetString("potionFormName", cells[8]);
            SetFloat("amountPerUse", cells[9], 0.05f);
            SetInt("rarity", cells[10], 1, 5);
            if (!string.IsNullOrEmpty(cells[11]))
            {
                SerializedProperty p = so.FindProperty("tags");
                var tags = new List<string>();
                if (cells[11] != Clear)
                {
                    foreach (string t in cells[11].Split(new[] { ';', '；', '、', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (t.Trim().Length > 0) tags.Add(t.Trim());
                    }
                }
                bool same = p.arraySize == tags.Count;
                for (int i = 0; same && i < tags.Count; i++) same = p.GetArrayElementAtIndex(i).stringValue == tags[i];
                if (!same)
                {
                    p.arraySize = tags.Count;
                    for (int i = 0; i < tags.Count; i++) p.GetArrayElementAtIndex(i).stringValue = tags[i];
                    changed = true;
                }
            }
            SetInt("sortOrder", cells[12], -99999, 99999);
            SetEnum<ContainerStyle>("container", cells[13]);
            SetObject<GameObject>("worldPrefab", cells[14]);
            SetObject<GameObject>("potionBottlePrefab", cells[15]);
            SetObject<GameObject>("dropPiecePrefab", cells[16]);
            SetObject<Sprite>("icon", cells[17]);
            SetString("description", cells[18]);
            SetAutoFloat("solubility", cells[19], 0f, 1f);
            SetAutoFloat("heatLimit", cells[20], 0f, 1000f);
            SetAutoFloat("heatSensitivity", cells[21], 0f, 1f);
            SetFloat("activationTemp", cells[22] == Clear ? "0" : cells[22], 0f);
            SetFloat("activationBonus", cells[23], -100f);
            SetInt("discoveriesToUnlock", cells[24], 0, 9999);
            return changed;
        }

        /// <summary>「回復:20; 火:-5」を属性のリストに入れる。変わったら true</summary>
        private static bool SetPairs(SerializedProperty list, string valueField, string text, AlchemySettings settings, string line, Report report)
        {
            var pairs = new List<KeyValuePair<PotionAttribute, float>>();
            if (text != Clear)
            {
                foreach (string part in text.Split(new[] { ';', '；', '、', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    int colon = p.IndexOfAny(new[] { ':', '：', '=' });
                    if (colon <= 0)
                    {
                        report.warnings.Add($"{line}: 「{p}」は「属性名:数値」の形ではありません");
                        continue;
                    }
                    string name = p.Substring(0, colon).Trim();
                    string num = p.Substring(colon + 1).Trim();
                    if (!TryAttribute(name, settings, out PotionAttribute attr))
                    {
                        report.warnings.Add($"{line}: 属性「{name}」はありません（回復・火・氷・成長促進・透明化・毒・光・風）");
                        continue;
                    }
                    if (!TryFloat(num, out float v))
                    {
                        report.warnings.Add($"{line}: 「{num}」は数値ではありません（{name}）");
                        continue;
                    }
                    pairs.Add(new KeyValuePair<PotionAttribute, float>(attr, v));
                }
            }

            bool same = list.arraySize == pairs.Count;
            for (int i = 0; same && i < pairs.Count; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                same = e.FindPropertyRelative("attribute").intValue == (int)pairs[i].Key &&
                       Mathf.Approximately(e.FindPropertyRelative(valueField).floatValue, pairs[i].Value);
            }
            if (same) return false;
            list.arraySize = pairs.Count;
            for (int i = 0; i < pairs.Count; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("attribute").intValue = (int)pairs[i].Key;
                e.FindPropertyRelative(valueField).floatValue = pairs[i].Value;
            }
            return true;
        }

        private static bool TryAttribute(string name, AlchemySettings settings, out PotionAttribute attr)
        {
            if (EnumText.TryParse(name, out attr)) return true;
            if (settings != null)
            {
                foreach (PotionAttribute a in (PotionAttribute[])Enum.GetValues(typeof(PotionAttribute)))
                {
                    if (settings.GetDisplayName(a) == name) { attr = a; return true; }
                }
            }
            return false;
        }

        private static string AttributeList(IReadOnlyList<AttributeValue> list, AlchemySettings settings)
        {
            var parts = new List<string>();
            foreach (AttributeValue av in list) parts.Add(AttrName(av.attribute) + ":" + Num(av.value));
            return string.Join("; ", parts);
        }

        private static string AmplifierList(IReadOnlyList<AttributeAmplifier> list, AlchemySettings settings)
        {
            var parts = new List<string>();
            foreach (AttributeAmplifier am in list) parts.Add(AttrName(am.attribute) + ":" + Num(am.percent));
            return string.Join("; ", parts);
        }

        private static string AttrName(PotionAttribute a)
        {
            return EnumText.Label(a);
        }

        // ------------------------------------------------------------------
        // CSV の読み書き（"" で囲んだ中の , と改行に対応）
        // ------------------------------------------------------------------

        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                    case '\t':
                        row.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(ch);
                        break;
                }
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }

        private static void AppendRow(StringBuilder sb, List<string> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                string c = cells[i] ?? "";
                if (c.IndexOfAny(new[] { ',', '"', '\n', '\r', '\t' }) >= 0) sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
                else sb.Append(c);
            }
            sb.Append("\r\n");
        }

        private static string ReadText(string full, Report report)
        {
            byte[] bytes = File.ReadAllBytes(full);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    report.warnings.Add("ファイルが UTF-8 ではなかったので Shift_JIS として読みました（「CSV UTF-8」で保存するのがおすすめ）");
                    return Encoding.GetEncoding(932).GetString(bytes);
                }
                catch (Exception)
                {
                    report.errors.Add("文字コードが読めません。Excel なら「CSV UTF-8（コンマ区切り）」で保存してください");
                    return "";
                }
            }
        }

        private static bool TryFloat(string s, out float v)
        {
            return float.TryParse(s.Replace("＋", "+").Replace("－", "-").Replace("%", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static string AutoNum(float v)
        {
            return v < 0f ? Auto : Num(v);
        }

        private static string Num(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string AssetPath(UnityEngine.Object o)
        {
            return o != null ? AssetDatabase.GetAssetPath(o) : "";
        }

        private static string SafeFileName(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        public static string ToFullPath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
        }

        private static AlchemySettings FindSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AlchemySettings"))
            {
                var s = AssetDatabase.LoadAssetAtPath<AlchemySettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) return s;
            }
            return null;
        }
    }
}
