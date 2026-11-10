using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step11：画面をスッキリさせて、フラスコに集中できるようにする（何度実行してもよい）。
    ///   1. 調合画面（BrewHUD の Prefab）から、工程・ボタン（瓶に詰める・全部詰める・早送り・ページ送り）・操作の説明を消す
    ///      （見立てカードと「回復 +3」はコードからなくした。残るのは、液体の名前・お知らせ・素材の名前・温度計・演出だけ）
    ///   2. 素材トレイ（Tray の Prefab）を作り直す：画面の右に縦に立つ棚。四角いマス 2列×7行と、いちばん下に「空き瓶」
    ///      （空き瓶をクリックで瓶に詰める。火はつまみのクリックで点ける／消す）
    ///   3. シーンの素材トレイを、新しい Prefab で置き直す（画面の右）
    ///   4. 全体の光（Global Light 2D）を 0.5 に（0.4 より暗いときだけ。ランタンは増やさない）
    ///   5. 使わなくなった絵・Prefab の控えを消す（横長のトレイの板・見立ての棒・BrewHUD の Step4 前の控え）
    /// </summary>
    public static class Step11MinimalUI2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        private const string BrewHudPath = Brew2DBuilder.PrefabDir + "/BrewHUD.prefab";
        private const string TrayPath = Brew2DBuilder.PrefabDir + "/Tray.prefab";
        private const string TrayItemPath = Brew2DBuilder.PrefabDir + "/TrayItem.prefab";
        /// <summary>部屋全体の明るさ（Step11）。夜の空気は残しつつ、机の右側までしっかり見える</summary>
        public const float BrightGlobal = 0.5f;
        private const float TooDark = 0.4f;

        /// <summary>BrewHUD から消す物（どれも Step11 で使わなくなった）</summary>
        private static readonly string[] RemovedHudObjects =
        {
            "ProcessPanel", "BottleOneButton", "BottleAllButton", "FastForwardButton",
            "PagePrevButton", "PageNextButton", "PageLabel", "ControlsHint", "Activity",
        };

        /// <summary>使わなくなったファイル・フォルダ</summary>
        private static readonly string[] ObsoleteAssets =
        {
            CustomAssetPaths.Workbench + "/tray.png",
            CustomAssetPaths.UIBrew + "/bar_back.png",
            CustomAssetPaths.UIBrew + "/bar_fill.png",
            Brew2DBuilder.PrefabDir + "/Backup",
        };

        [MenuItem(Menu + "Step11：画面をスッキリ（UI の断捨離・右の素材トレイ・明るさ）", priority = 21)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step11", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(Atelier2DBuilder.ScenePath))
            {
                EditorUtility.DisplayDialog("Step11", Atelier2DBuilder.ScenePath + " がありません（「アトリエを作る」を先に実行）。", "OK");
                return;
            }
            var log = new StringBuilder();
            UpgradeBrewHud(log);
            GameObject tray = RebuildTray(log);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.OpenScene(Atelier2DBuilder.ScenePath);
            PlaceTray(scene, tray, log);
            Brighten(scene, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            RemoveObsolete(log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Step11] 画面をスッキリ\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step11.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step11：画面をスッキリ", log.ToString(), "OK");
        }

        // ------------------------------------------------------------------
        // 1. 調合画面の文字とボタン
        // ------------------------------------------------------------------

        private static void UpgradeBrewHud(StringBuilder log)
        {
            if (!File.Exists(BrewHudPath))
            {
                log.AppendLine("！ " + BrewHudPath + " がない（「アトリエを作る」を先に実行）");
                return;
            }
            GameObject root = PrefabUtility.LoadPrefabContents(BrewHudPath);
            try
            {
                var removed = new List<string>();
                foreach (string name in RemovedHudObjects)
                {
                    Transform t = FindDeep(root.transform, name);
                    if (t == null || t == root.transform) continue;
                    Object.DestroyImmediate(t.gameObject);
                    removed.Add(name);
                }
                if (root.GetComponent<BrewFeedback2D>() == null) root.AddComponent<BrewFeedback2D>();
                BrewHUD2D hud = root.GetComponent<BrewHUD2D>();
                if (hud != null)
                {
                    var so = new SerializedObject(hud);
                    SerializedProperty v = so.FindProperty("layoutVersion");
                    if (v != null) v.intValue = BrewHUD2D.MinimalLayout;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, BrewHudPath);
                log.AppendLine(removed.Count > 0
                    ? "・調合画面から消した：" + string.Join("・", removed)
                    : "・調合画面の工程・ボタン・操作の説明は、もう消してある");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 2・3. 右の素材トレイ
        // ------------------------------------------------------------------

        private static GameObject RebuildTray(StringBuilder log)
        {
            PlaceholderArt2D.Set s = PlaceholderArt2D.EnsureAll(Brew2DBuilder.SpriteDir);
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrayItemPath);
            if (itemPrefab == null)
            {
                log.AppendLine("！ " + TrayItemPath + " がない（「調合机の絵と部品を作る」を先に実行）");
                return null;
            }
            GameObject built = Brew2DBuilder.BuildTray(s, itemPrefab);
            try
            {
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(built, TrayPath);
                BrewTray2D tray = saved != null ? saved.GetComponent<BrewTray2D>() : null;
                log.AppendLine($"・素材トレイを作り直した：画面の右に縦の棚、四角いマス {(tray != null ? tray.SlotCount : 0)} 個（2列×7行）と、いちばん下に空き瓶（クリックで瓶に詰める）");
                log.AppendLine("  マスの数・大きさは Tray の Prefab の BrewTray2D の数字を変えて、右クリック →「マス目を並べ直す」");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(built);
            }
        }

        private static void PlaceTray(Scene scene, GameObject prefab, StringBuilder log)
        {
            if (prefab == null) return;
            Transform parent = null;
            Vector3 origin = Vector3.zero;
            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BrewTray2D old in root.GetComponentsInChildren<BrewTray2D>(true))
                {
                    // Tray の Prefab の物なら、その Prefab ごと外す（ほかの物は外さない）
                    GameObject top = PrefabUtility.GetNearestPrefabInstanceRoot(old.gameObject);
                    if (top == null || top.GetComponent<BrewTray2D>() != old) top = old.gameObject;
                    if (parent == null)
                    {
                        parent = top.transform.parent;
                        origin = parent != null ? parent.root.position : Vector3.zero;
                    }
                    Object.DestroyImmediate(top);
                    removed++;
                }
            }
            if (parent == null)
            {
                // 置き場所がわからないときは「Workbench」の下
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Transform bench = FindDeep(root.transform, "Workbench");
                    if (bench == null) continue;
                    parent = bench;
                    origin = bench.root.position;
                    break;
                }
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = origin + Brew2DBuilder.TrayPosition;
            log.AppendLine($"・シーンの素材トレイを置き直した（右：{go.transform.position.x:0.##}, {go.transform.position.y:0.##}{(removed > 0 ? "／前の横長のトレイは外した" : "")}）");
        }

        // ------------------------------------------------------------------
        // 4. 明るさ
        // ------------------------------------------------------------------

        private static void Brighten(Scene scene, StringBuilder log)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light2D l in root.GetComponentsInChildren<Light2D>(true))
                {
                    if (l.lightType != Light2D.LightType.Global) continue;
                    if (l.intensity < TooDark)
                    {
                        log.AppendLine($"・全体の光（{l.name}）：{l.intensity:0.00} → {BrightGlobal:0.00}（ランタンは増やしていない）");
                        l.intensity = BrightGlobal;
                        EditorUtility.SetDirty(l);
                    }
                    else log.AppendLine($"・全体の光（{l.name}）は {l.intensity:0.00}（もう十分明るいので、そのまま）");
                }
            }
        }

        // ------------------------------------------------------------------
        // 5. 使わなくなったファイル
        // ------------------------------------------------------------------

        private static void RemoveObsolete(StringBuilder log)
        {
            var gone = new List<string>();
            foreach (string path in ObsoleteAssets)
            {
                bool exists = AssetDatabase.IsValidFolder(path) || File.Exists(path);
                if (!exists) continue;
                if (AssetDatabase.DeleteAsset(path)) gone.Add(path);
            }
            log.AppendLine(gone.Count > 0
                ? "・使わなくなったファイルを消した：" + string.Join("、", gone)
                : "・使わなくなったファイルは、もう消してある");
        }
    }
}
