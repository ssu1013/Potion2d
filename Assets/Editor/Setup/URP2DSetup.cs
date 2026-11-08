// ② 2D URP 環境をセットアップする（URP が入っているときだけコンパイルされる）。
// 何度実行しても、すでにある物は作り直さない（足りない物だけ作る・つなぐ）。
#if POTION_HAS_URP
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Potion2D.Setup
{
    static class URP2DSetup
    {
        const string Root = "Assets/Settings/Rendering";
        const string PipelinePath = Root + "/URP_2D_Pipeline.asset";
        const string RendererPath = Root + "/URP_2D_Renderer.asset";
        const string ProfilePath = Root + "/GlobalVolume_Profile.asset";
        const string UrpPackage = "Packages/com.unity.render-pipelines.universal";

        // 絵本らしい、ほんのり温かい全体の光（白に近い。強すぎると素材の色が変わって見える）
        static readonly Color WarmLight = new Color(1f, 0.97f, 0.92f);

        [MenuItem("Tools/Potion Frontier 2D/② 2D URP 環境をセットアップ", priority = 2)]
        static void Setup()
        {
            var log = new StringBuilder("[2D URP] セットアップの結果\n");
            EnsureFolder(Root);

            var pipeline = FindOrCreatePipeline(log);
            if (pipeline == null)
            {
                EditorUtility.DisplayDialog("2D URP", "URP の設定ファイルを用意できませんでした。Console を確認してください。", "OK");
                return;
            }

            // 光るポーション（ブルーム）には HDR が必要
            if (!pipeline.supportsHDR) { pipeline.supportsHDR = true; log.AppendLine("・HDR をオンにした"); }
            EditorUtility.SetDirty(pipeline);

            // Graphics とすべての画質レベルで、この URP を使う
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            log.AppendLine($"・Graphics と画質レベル {QualitySettings.names.Length} 個に「{pipeline.name}」を設定");

            // 光の重なりを正しく計算するため Linear（このプロジェクトは最初から Linear）
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                log.AppendLine("・色空間を Linear にした");
            }

            var profile = FindOrCreateProfile(log);
            SetupOpenScene(profile, log);

            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("2D URP", "2D URP 環境のセットアップが終わりました。\n内容は Console に出ています。", "OK");
        }

        // ---------- パイプライン（URP の設定ファイル）----------

        static UniversalRenderPipelineAsset FindOrCreatePipeline(StringBuilder log)
        {
            // すでに 2D レンダラーを使う URP 設定があれば、それを使う（手で作った物も含む）
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && Uses2DRenderer(asset))
                {
                    log.AppendLine($"・既存の 2D 用 URP 設定を使う：{AssetDatabase.GetAssetPath(asset)}");
                    return asset;
                }
            }

            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                log.AppendLine($"・2D レンダラーを作った：{RendererPath}");
            }
            ReloadNullResources(renderer);
            AssignPostProcessData(renderer, log);
            EditorUtility.SetDirty(renderer);

            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            log.AppendLine($"・URP 設定を作った：{PipelinePath}");
            return pipeline;
        }

        static bool Uses2DRenderer(UniversalRenderPipelineAsset asset)
        {
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (list == null || !list.isArray) return false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Renderer2DData) return true;
            return false;
        }

        // 2D レンダラーが使うシェーダー・マテリアルの空欄を URP 本体から埋める（メニューから作ったときと同じ状態にする）
        static void ReloadNullResources(Object target)
        {
            var type = typeof(Volume).Assembly.GetType("UnityEngine.Rendering.ResourceReloader");
            if (type == null) return;
            foreach (var name in new[] { "TryReloadAllNullIn", "ReloadAllNullIn" })
            {
                var m = type.GetMethod(name, new[] { typeof(object), typeof(string) });
                if (m == null) continue;
                m.Invoke(null, new object[] { target, UrpPackage });
                return;
            }
        }

        // ブルームなどの画面効果を使うためのデータ（空だと画面効果が効かない）
        static void AssignPostProcessData(Renderer2DData renderer, StringBuilder log)
        {
            var so = new SerializedObject(renderer);
            var prop = so.FindProperty("m_PostProcessData");
            if (prop == null) { log.AppendLine("！画面効果のデータ欄が見つからない（レンダラーの Post-processing を手で確認）"); return; }
            if (prop.objectReferenceValue != null) return;

            var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(UrpPackage + "/Runtime/Data/PostProcessData.asset");
            if (data == null)
            {
                var guid = AssetDatabase.FindAssets("t:PostProcessData", new[] { UrpPackage }).FirstOrDefault();
                if (guid != null) data = AssetDatabase.LoadAssetAtPath<PostProcessData>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (data == null) { log.AppendLine("！PostProcessData が見つからない（レンダラーの Post-processing を手で確認）"); return; }

            prop.objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("・2D レンダラーの画面効果（Post-processing）をオンにした");
        }

        // ---------- 画面効果（ブルーム）----------

        static VolumeProfile FindOrCreateProfile(StringBuilder log)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
                log.AppendLine($"・画面効果の設定を作った：{ProfilePath}");
            }
            if (!profile.TryGet<Bloom>(out _))
            {
                var bloom = profile.Add<Bloom>(false);
                bloom.name = "Bloom";
                bloom.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(bloom, profile);
                // 明るさ 1 を超えた所（強い光・光るポーション）だけをにじませる
                bloom.threshold.Override(1.0f);
                bloom.intensity.Override(0.6f);
                bloom.scatter.Override(0.7f);
                log.AppendLine("・ブルームを足した（しきい値 1.0・強さ 0.6）");
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ---------- 開いているシーン ----------

        static void SetupOpenScene(VolumeProfile profile, StringBuilder log)
        {
            var scene = EditorSceneManager.GetActiveScene();

            var cam = Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(cam);
                EditorUtility.SetDirty(data);
                log.AppendLine($"・カメラ「{cam.name}」の画面効果をオンにした");
            }
            else log.AppendLine("！シーンにカメラがない");

            bool hasGlobal = Object.FindObjectsOfType<Light2D>().Any(l => l.lightType == Light2D.LightType.Global);
            if (!hasGlobal)
            {
                var go = new GameObject("Global Light 2D");
                var light = go.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Global;
                light.color = WarmLight;
                light.intensity = 1f;
                ApplyToAllSortingLayers(light);
                Undo.RegisterCreatedObjectUndo(go, "Global Light 2D");
                log.AppendLine("・全体の光（Global Light 2D）を置いた");
            }

            if (!Object.FindObjectsOfType<Volume>().Any(v => v.isGlobal))
            {
                var go = new GameObject("Global Volume");
                var volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
                Undo.RegisterCreatedObjectUndo(go, "Global Volume");
                log.AppendLine("・画面効果（Global Volume）を置いた");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.SaveScene(scene);
                log.AppendLine($"・シーンを保存した：{scene.path}");
            }
            else log.AppendLine("！シーンが未保存なので、Ctrl+S で保存してください");
        }

        // 全体の光を、すべての描画レイヤー（Sorting Layer）に当てる
        static void ApplyToAllSortingLayers(Light2D light)
        {
            var so = new SerializedObject(light);
            var prop = so.FindProperty("m_ApplyToSortingLayers");
            if (prop == null) return;
            var layers = SortingLayer.layers;
            prop.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++) prop.GetArrayElementAtIndex(i).intValue = layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
