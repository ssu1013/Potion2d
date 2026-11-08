using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step4（調合の手ごたえ）のセットアップ。
    ///   ・描く順番（Sorting Layer）の番号の修理：Flask・Liquid・Items・Held・FX の番号が 0（いちばん奥の Default と同じ）になっていて、
    ///     フラスコの中身・トレイの素材・手に持った瓶・湯気が、部屋の絵の後ろに隠れていたのを直す
    ///   ・調合画面（BrewHUD の Prefab）の配置を新しくする：液体の名前は上の真ん中、工程は左上、
    ///     フラスコの左に見立てカードと温度計、瓶に詰めるボタンはカードの下、右上は早送りだけ
    /// </summary>
    public static class Step4Brew2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        private const string BrewHudPath = Brew2DBuilder.PrefabDir + "/BrewHUD.prefab";
        private const string BackupDir = Brew2DBuilder.PrefabDir + "/Backup";
        public const int LayoutVersion = 4;

        /// <summary>Prefab の中の物 → 本来の描く順番（Brew2DBuilder が作るときに決めていた物）</summary>
        private static readonly Dictionary<string, Dictionary<string, string>> IntendedLayers = new Dictionary<string, Dictionary<string, string>>
        {
            { "Flask", new Dictionary<string, string> { { "GlassBack", "Flask" }, { "Liquid", "Liquid" }, { "Surface", "Liquid" }, { "LiquidMask", "Liquid" } } },
            { "Heater", new Dictionary<string, string> { { "Flame_0", "Flask" }, { "Flame_1", "Flask" }, { "Flame_2", "Flask" } } },
            { "HeldItem", new Dictionary<string, string> { { "Body", "Held" }, { "Content", "Held" }, { "Stream", "Liquid" } } },
            { "StirRod", new Dictionary<string, string> { { "Art", "Liquid" } } },
            { "TrayItem", new Dictionary<string, string> { { "Body", "Items" }, { "Content", "Items" } } },
        };

        [MenuItem(Menu + "Step4：調合画面を新しくする（配置・見える化・演出）", priority = 14)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step4", "再生を止めてから実行してください。", "OK");
                return;
            }
            var log = new StringBuilder();
            RepairSortingLayers(log);
            UpgradeBrewHud(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[Step4] " + log);
            EditorUtility.DisplayDialog("Step4：調合画面を新しくする", log.ToString(), "OK");
        }

        [MenuItem(Menu + "描く順番（Sorting Layer）を直す", priority = 15)]
        private static void RepairMenu()
        {
            var log = new StringBuilder();
            RepairSortingLayers(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[描く順番] " + log);
            EditorUtility.DisplayDialog("描く順番を直す", log.ToString(), "OK");
        }

        // ------------------------------------------------------------------
        // 描く順番
        // ------------------------------------------------------------------

        internal static void RepairSortingLayers(StringBuilder log)
        {
            Brew2DBuilder.EnsureSortingLayers(log);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                log.AppendLine("！ TagManager が読めませんでした");
                return;
            }
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
            var used = new HashSet<long> { 0 };
            var fixedNames = new List<string>();
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty e = layers.GetArrayElementAtIndex(i);
                string name = e.FindPropertyRelative("name").stringValue;
                SerializedProperty idProp = e.FindPropertyRelative("uniqueID");
                long id = idProp.longValue;
                if (name == "Default") continue;
                if (id == 0 || used.Contains(id))
                {
                    long fresh = StableId(name);
                    while (used.Contains(fresh)) fresh = fresh >= int.MaxValue ? 1 : fresh + 1;
                    idProp.longValue = fresh;
                    id = fresh;
                    fixedNames.Add(name);
                }
                used.Add(id);
            }
            if (fixedNames.Count > 0)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("・描く順番の番号を直した：" + string.Join(", ", fixedNames) + "（番号が 0 で、いちばん奥に描かれていた）");
            }
            else log.AppendLine("・描く順番の番号は正しい");

            // Prefab の中の物を、本来の描く順番へ
            int changed = 0;
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in IntendedLayers)
            {
                string path = Brew2DBuilder.PrefabDir + "/" + kv.Key + ".prefab";
                if (!File.Exists(path)) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int here = 0;
                try
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!kv.Value.TryGetValue(r.gameObject.name, out string layer)) continue;
                        if (r.sortingLayerName == layer && r.sortingLayerID != 0) continue;
                        r.sortingLayerName = layer;
                        here++;
                    }
                    foreach (SpriteMask m in root.GetComponentsInChildren<SpriteMask>(true))
                    {
                        if (!kv.Value.TryGetValue(m.gameObject.name, out string layer)) continue;
                        if (m.sortingLayerName == layer && m.sortingLayerID != 0) continue;
                        m.sortingLayerName = layer;
                        here++;
                    }
                    if (here > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                if (here > 0) log.AppendLine($"・{kv.Key} の {here} 個を、本来の描く順番に戻した");
                changed += here;
            }
            if (changed == 0) log.AppendLine("・Prefab の描く順番は正しい");
        }

        /// <summary>名前から決まる番号（毎回同じ。0 にならない正の数）</summary>
        private static long StableId(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in name) h = (h ^ c) * 16777619;
                long id = h & 0x7FFFFFFF;
                return id == 0 ? 1 : id;
            }
        }

        // ------------------------------------------------------------------
        // 調合画面の配置
        // ------------------------------------------------------------------

        private static void UpgradeBrewHud(StringBuilder log)
        {
            if (!File.Exists(BrewHudPath))
            {
                log.AppendLine("！ " + BrewHudPath + " がありません（「調合画面を作る」を先に実行）");
                return;
            }
            if (!AssetDatabase.IsValidFolder(BackupDir)) AssetDatabase.CreateFolder(Brew2DBuilder.PrefabDir, "Backup");
            string backup = BackupDir + "/BrewHUD_before_step4.prefab";
            if (!File.Exists(backup))
            {
                AssetDatabase.CopyAsset(BrewHudPath, backup);
                log.AppendLine("・今の調合画面の控えを取った：" + backup);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BrewHudPath);
            try
            {
                var notes = new List<string>();
                // 液体の名前：上の真ん中（名前を大きく、その下に分類・量・温度）
                RectTransform info = Find(root, "InfoPanel");
                if (info != null)
                {
                    Place(info, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(680f, 116f));
                    RectTransform title = Find(root, "LiquidTitle");
                    if (title != null)
                    {
                        Stretch(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-36f, 44f));
                        Align(title, TextAnchor.UpperCenter);
                    }
                    RectTransform detail = Find(root, "LiquidDetail");
                    if (detail != null)
                    {
                        Stretch(detail, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(-36f, 56f));
                        Align(detail, TextAnchor.UpperCenter);
                    }
                    notes.Add("液体の名前 → 上の真ん中");
                }
                RectTransform process = Find(root, "ProcessPanel");
                if (process != null)
                {
                    Place(process, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(560f, 200f));
                    notes.Add("工程 → 左上");
                }
                RectTransform toast = Find(root, "Toast");
                if (toast != null) Place(toast, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(700f, 64f)); // 左上の工程と重ならない幅
                // 作業中の数字は、カーソルのそばに出す（BrewFeedback2D）
                RectTransform activity = Find(root, "Activity");
                if (activity != null) activity.gameObject.SetActive(false);
                // 瓶に詰める：見立てカードのすぐ下（目標に届いたのを見て、すぐ押せる）
                RectTransform one = Find(root, "BottleOneButton");
                if (one != null)
                {
                    Place(one, Vector2.zero, Vector2.zero, new Vector2(340f, 290f), new Vector2(360f, 54f));
                    FitLabel(one);
                }
                RectTransform all = Find(root, "BottleAllButton");
                if (all != null)
                {
                    Place(all, Vector2.zero, Vector2.zero, new Vector2(340f, 230f), new Vector2(360f, 54f));
                    FitLabel(all);
                }
                if (one != null || all != null) notes.Add("瓶に詰めるボタン → 見立てカードの下");
                RectTransform fast = Find(root, "FastForwardButton");
                if (fast != null)
                {
                    Place(fast, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(260f, 52f));
                    FitLabel(fast);
                    notes.Add("右上は早送りだけ（相談メモが上に詰まる）");
                }

                if (root.GetComponent<BrewFeedback2D>() == null)
                {
                    root.AddComponent<BrewFeedback2D>();
                    notes.Add("手ごたえ（BrewFeedback2D）を付けた");
                }
                BrewHUD2D hud = root.GetComponent<BrewHUD2D>();
                if (hud != null)
                {
                    var so = new SerializedObject(hud);
                    SerializedProperty v = so.FindProperty("layoutVersion");
                    if (v != null)
                    {
                        v.intValue = LayoutVersion;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, BrewHudPath);
                log.AppendLine("・調合画面の配置を新しくした：" + string.Join("／", notes));
                log.AppendLine("  （位置は BrewHUD の Prefab と、BrewFeedback2D の「置き場所」で変えられる）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static RectTransform Find(GameObject root, string name)
        {
            foreach (RectTransform t in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Align(RectTransform rt, TextAnchor anchor)
        {
            Text t = rt.GetComponent<Text>();
            if (t != null) t.alignment = anchor;
        }

        /// <summary>ボタンの文字を、ボタンの大きさいっぱいに広げて真ん中にそろえる</summary>
        private static void FitLabel(RectTransform button)
        {
            Text label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-12f, -6f);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
