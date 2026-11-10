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
    /// Step10：夜の机を、空気感はそのままに少し見やすくする（何度実行してもよい）。
    ///   1. 机の右（窓の下）に、左と同じランタンをもう1つ置く（Lamp の Prefab。温かい Point Light 2D と炎のゆらぎつき）
    ///   2. 全体の光（Global Light 2D）を 0.10 → 0.16 に（奥の壁や板が、うっすら見える暗さ）。手で変えてあれば触らない
    ///      （Step11 で、さらに 0.5 に上げた）
    /// 羊皮紙の UI の色は、UISkin の「紙の色合い（Paper Tint）」で調整する（このメニューでは触らない）。
    /// </summary>
    public static class Step10Lighting2D
    {
        private const string LampPrefabPath = "Assets/Art2D/Brewing/Prefabs/Lamp.prefab";
        public const string RightLampName = "Lamp（右・窓の下）";
        /// <summary>右のランタンの位置（左は x = -6.8）。フラスコ・つまみの右、右の素材トレイの左</summary>
        public static readonly Vector3 RightLampPosition = new Vector3(5.2f, -2.08f, 0f);
        public const float OldGlobal = 0.1f, ComfortGlobal = 0.16f;

        [MenuItem("Tools/Potion Frontier 2D/Step10：灯りを足して見やすく（右のランタン・全体の光）", priority = 19)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step10", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new StringBuilder();
            if (!File.Exists(Atelier2DBuilder.ScenePath))
            {
                EditorUtility.DisplayDialog("Step10", Atelier2DBuilder.ScenePath + " がありません（「アトリエを作る」を先に実行）。", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(Atelier2DBuilder.ScenePath);

            // ---- 1. 右のランタン ----
            Transform left = null, right = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == RightLampName) right = t;
                    else if (left == null && t.name.StartsWith("Lamp") && PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) == t.gameObject) left = t;
                }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
            if (right != null) log.AppendLine($"・右のランタンは、もう置いてある（{right.position.x:0.##}, {right.position.y:0.##}）");
            else if (prefab == null) log.AppendLine("！ " + LampPrefabPath + " がない");
            else
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (left != null && left.parent != null) go.transform.SetParent(left.parent, false);
                go.name = RightLampName;
                Vector3 p = RightLampPosition;
                if (left != null) p.y = left.position.y; // 左のランタンと同じ高さ（同じ机の上）
                go.transform.position = p;
                int lights = go.GetComponentsInChildren<Light2D>(true).Length;
                log.AppendLine($"・机の右（窓の下）にランタンを置いた（{p.x:0.##}, {p.y:0.##}）。温かい光 {lights} 個（炎のゆらぎは左とずれて揺れる）");
            }

            // ---- 2. 全体の光 ----
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light2D l in root.GetComponentsInChildren<Light2D>(true))
                {
                    if (l.lightType != Light2D.LightType.Global) continue;
                    if (Mathf.Abs(l.intensity - OldGlobal) < 0.005f)
                    {
                        l.intensity = ComfortGlobal;
                        EditorUtility.SetDirty(l);
                        log.AppendLine($"・全体の光（{l.name}）：{OldGlobal:0.00} → {ComfortGlobal:0.00}");
                    }
                    else log.AppendLine($"・全体の光（{l.name}）は {l.intensity:0.00}（手で変えてあるので、そのまま）");
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("・羊皮紙の UI の色は UISkin（Assets/Art2D/UI/UISkin.asset）の「Paper Tint」で調整できる");
            Debug.Log("[Step10] 灯りを足して見やすく\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step10.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step10：灯りを足して見やすく", log.ToString(), "OK");
        }
    }
}
