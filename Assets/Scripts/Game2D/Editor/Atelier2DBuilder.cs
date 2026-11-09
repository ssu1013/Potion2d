using System.IO;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using B = PotionFrontier.Game2D.EditorTools.Brew2DBuilder;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 2D のアトリエ（ゲーム本編）を組み立てるメニュー（Tools > Potion Frontier 2D）。
    /// 1画面版：夜の調合机（フラスコ・加熱台・素材トレイ・ランプ）＋ゲームの仕組み（流れ・セーブ・レシピ本・素材ノート・音）を
    /// 1つのシーン Assets/Scenes/Atelier2D.unity にまとめる。カメラは調合机に固定で、ほかの画面へは移らない。
    /// 絵・Prefab は「なければ作る」（自分の絵に差し替えたあとで実行しても上書きしない）。
    /// 夜の空気感（Step6）などは、このあとそれぞれのメニューで足す（「仕上げ」の一括実行でもよい）。
    /// </summary>
    public static class Atelier2DBuilder
    {
        internal const string ScenePath = "Assets/Scenes/Atelier2D.unity";
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string SoundLibraryPath = "Assets/Audio/SoundLibrary.asset";

        [MenuItem("Tools/Potion Frontier 2D/アトリエを作る（ゲーム本編・足りない物だけ）", priority = 10)]
        private static void Build() => Run(false);

        [MenuItem("Tools/Potion Frontier 2D/アトリエのシーンを作り直す（控えを取る）", priority = 11)]
        private static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("2D アトリエ", "Atelier2D シーンを作り直します（今のシーンは Assets/Scenes/Backup に控えを取ります）。\n絵・Prefab はそのまま使います。", "作り直す", "やめる")) return;
            Run(true);
        }

        private static void Run(bool rebuild)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new System.Text.StringBuilder("[2D アトリエ] 結果\n");
            B.BrewAssets a = B.EnsureAssets(log);
            UISkin skin = EnsureSkin(log);

            bool exists = File.Exists(ScenePath);
            if (!exists || rebuild)
            {
                if (exists) B.Backup(ScenePath, log);
                BuildScene(a, skin, log);
            }
            else
            {
                log.AppendLine($"・シーンはすでにあるので、そのまま開きます：{ScenePath}（作り直すときは「アトリエのシーンを作り直す」）");
                EditorSceneManager.OpenScene(ScenePath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        private static UISkin EnsureSkin(System.Text.StringBuilder log)
        {
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin != null) return skin;
            PlaceholderArt2D.EnsureFolder(Path.GetDirectoryName(SkinPath).Replace('\\', '/'));
            skin = ScriptableObject.CreateInstance<UISkin>();
            AssetDatabase.CreateAsset(skin, SkinPath);
            log.AppendLine($"・UI の絵の一覧を作った（空欄はコードで描いた仮の絵）：{SkinPath}");
            return skin;
        }

        // ------------------------------------------------------------------
        // シーン
        // ------------------------------------------------------------------

        private static void BuildScene(B.BrewAssets a, UISkin skin, System.Text.StringBuilder log)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera cam = B.CreateCamera(new Vector3(0f, 0f, -10f));
            B.CreateGlobalLightAndVolume(log);

            var brewArea = new GameObject("BrewArea（調合机）");
            B.PlaceBrewArea(a, brewArea.transform);
            Transform brewView = B.Child(brewArea.transform, "BrewView（調合机のカメラの位置）", Vector3.zero);
            B.Instance(a.heldItem, null, Vector3.zero);

            // 仕組み
            var systems = new GameObject("GameSystems");
            var context = systems.AddComponent<GameContext>();
            context.Configure(AssetDatabase.LoadAssetAtPath<IngredientDatabase>("Assets/GameData/Alchemy/IngredientDatabase.asset"),
                              AssetDatabase.LoadAssetAtPath<GameBalance>("Assets/GameData/GameBalance.asset"));
            var alchemy = systems.AddComponent<AlchemyManager>();
            var settings = AssetDatabase.LoadAssetAtPath<AlchemySettings>("Assets/GameData/Alchemy/AlchemySettings.asset");
            if (settings == null) log.AppendLine("！AlchemySettings が見つからない（Assets/GameData/Alchemy/AlchemySettings.asset）");
            alchemy.EditorSetup(settings, B.LoadAll<PotionConditionData>(), B.LoadAll<ReactionData>());
            systems.AddComponent<IngredientInventory>();
            systems.AddComponent<PotionInventory>();
            var session = systems.AddComponent<BrewSession2D>();
            B.SetRef(session, "art", a.art);
            systems.AddComponent<BrewInput2D>();

            var view = systems.AddComponent<AtelierView2D>();
            B.SetRef(view, "viewCamera", cam);
            B.SetRef(view, "brewView", brewView);

            systems.AddComponent<GameFlow>();
            var audio = systems.AddComponent<AudioManager>();
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            if (library == null) log.AppendLine($"！音の一覧 {SoundLibraryPath} が見つからない（音なしで動く）");
            B.SetRef(audio, "library", library);

            var hudGo = new GameObject("AtelierHUD（タイトル・レシピ本・素材ノート・お知らせ）");
            var hud = hudGo.AddComponent<AtelierHUD>();
            B.SetRef(hud, "skin", skin);
            B.Instance(a.hud, null, Vector3.zero);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.SaveScene(scene, ScenePath);
            B.AddToBuildSettings(ScenePath, true);
            log.AppendLine($"・シーンを作った：{ScenePath}（▶ でタイトルから遊べます。ビルドの最初のシーンにしました）");
        }
    }
}
