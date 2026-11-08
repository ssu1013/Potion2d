using System.Collections.Generic;
using System.IO;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using B = PotionFrontier.Game2D.EditorTools.Brew2DBuilder;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 2D のアトリエ（ゲーム本編）を組み立てるメニュー（Tools > Potion Frontier 2D）。
    /// 横長の部屋（Room の Prefab）＋調合台の画面（調合画面の部品）＋ゲームの仕組み（流れ・セーブ・相談・レシピ本・音）を
    /// 1つのシーン Assets/Scenes/Atelier2D.unity にまとめる。部屋は上（y = 20）、調合台は下（y = 0）に置き、
    /// 作業台にズームしたところでカメラが調合台へ移る。
    /// 絵・Prefab は「なければ作る」（自分の絵に差し替えたあとで実行しても上書きしない）。
    /// </summary>
    public static class Atelier2DBuilder
    {
        private const string ScenePath = "Assets/Scenes/Atelier2D.unity";
        private const string RoomPrefabPath = B.PrefabDir + "/Room.prefab";
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string SoundLibraryPath = "Assets/Audio/SoundLibrary.asset";
        private static readonly Vector3 RoomOrigin = new Vector3(0f, 20f, 0f);

        [MenuItem("Tools/Potion Frontier 2D/アトリエを作る（ゲーム本編・足りない物だけ）", priority = 10)]
        private static void Build() => Run(false);

        [MenuItem("Tools/Potion Frontier 2D/Step3 の追加分を入れる（演出の絵・取引先帳）", priority = 12)]
        private static void Upgrade()
        {
            var log = new System.Text.StringBuilder("[2D アトリエ] Step3 の追加分\n");
            B.BrewAssets a = B.EnsureAssets(log); // 共通の絵の一覧に、演出の絵（湯気・泡など）を足す
            RoomArt2D.Set room = RoomArt2D.EnsureRoom(B.SpriteDir);
            GameObject prefab = B.EnsurePrefab("Room", () => BuildRoom(room, a), log);
            UpgradeRoom(prefab, a, log);
            AssetDatabase.SaveAssets();
            log.AppendLine("・シーンはそのままでよい（演出と音は、フラスコが起動時に自分で付ける）");
            Debug.Log(log.ToString());
        }

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
            RoomArt2D.Set room = RoomArt2D.EnsureRoom(B.SpriteDir);
            UISkin skin = EnsureSkin(log);
            GameObject roomPrefab = B.EnsurePrefab("Room", () => BuildRoom(room, a), log);
            UpgradeRoom(roomPrefab, a, log);

            bool exists = File.Exists(ScenePath);
            if (!exists || rebuild)
            {
                if (exists) B.Backup(ScenePath, log);
                BuildScene(a, roomPrefab, skin, log);
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
        // 部屋の Prefab
        // ------------------------------------------------------------------

        private static GameObject BuildRoom(RoomArt2D.Set art, B.BrewAssets a)
        {
            var root = new GameObject("Room");
            Transform artRoot = B.Child(root.transform, "Art", Vector3.zero);
            B.AddSprite(artRoot, "Left（入口とカウンター）", art.left, "Background", 0, new Vector3(-19.2f, 0f, 0f));
            B.AddSprite(artRoot, "Center（作業台）", art.center, "Background", 0, Vector3.zero);
            B.AddSprite(artRoot, "Right（瓶の棚とレシピ本）", art.right, "Background", 0, new Vector3(19.2f, 0f, 0f));

            Transform anchors = B.Child(root.transform, "Anchors", Vector3.zero);
            B.Child(anchors, "CameraStart", Vector3.zero);
            B.Child(anchors, "BoundsLeft", new Vector3(-19.2f, 0f, 0f));
            B.Child(anchors, "BoundsRight", new Vector3(19.2f, 0f, 0f));
            B.Child(anchors, "WorkbenchFocus", new Vector3(0f, -1.9f, 0f));
            B.Child(anchors, "CustomerOutside", new Vector3(-30.5f, -3.7f, 0f));
            B.Child(anchors, "CustomerCounter", new Vector3(-19f, -3.7f, 0f));
            B.Child(root.transform, "Customers", Vector3.zero);

            Transform spots = B.Child(root.transform, "Hotspots", Vector3.zero);
            Hotspot(spots, "Workbench", RoomHotspot2D.Kind.Workbench, new Vector3(0f, -2.6f, 0f), new Vector2(13f, 4f), "作業台", "クリックで調合台に向かう");
            Hotspot(spots, "Counter", RoomHotspot2D.Kind.Counter, new Vector3(-14f, -2.5f, 0f), new Vector2(8f, 2.8f), "カウンター", "お客さんが相談に来る所");
            Hotspot(spots, "Shelf", RoomHotspot2D.Kind.Shelf, new Vector3(16.2f, 0.2f, 0f), new Vector2(10.4f, 6.6f), "瓶の棚", "瓶詰めした液体が並ぶ（クリックでレシピ本）");
            Hotspot(spots, "RecipeBook", RoomHotspot2D.Kind.RecipeBook, new Vector3(24.7f, -2.2f, 0f), new Vector2(2.8f, 3.6f), "レシピ本", "クリックで開く（R でも開ける）");
            AddLedger(spots, a);

            // 瓶の棚（3段 × 13 本）
            Transform shelf = B.Child(root.transform, "Shelf", Vector3.zero);
            Transform slots = B.Child(shelf, "Slots", Vector3.zero);
            int n = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < 13; i++)
                {
                    B.Child(slots, "Slot_" + (n++).ToString("00"), new Vector3(11.4f + i * 0.8f, 2.62f - row * 2.5f, 0f));
                }
            }
            shelf.gameObject.AddComponent<RoomShelf2D>();

            // 光：ランプ・窓の月明かり・作業台のフラスコの光
            Transform lights = B.Child(root.transform, "Lights", Vector3.zero);
            B.Instance(a.lamp, lights, new Vector3(-11.5f, -0.2f, 0f)).name = "Lamp（カウンター）";
            B.Instance(a.lamp, lights, new Vector3(-6.8f, -2.08f, 0f)).name = "Lamp（作業台）";
            B.Instance(a.lamp, lights, new Vector3(21.6f, -2.68f, 0f)).name = "Lamp（棚の横）";
            B.PointLight(lights, "MoonLight（窓・中央）", new Vector3(5.8f, 2.6f, 0f), new Color(0.55f, 0.68f, 1f), 0.7f, 5.5f, 0.8f);
            B.PointLight(lights, "MoonLight（窓・入口）", new Vector3(-14.8f, 2.6f, 0f), new Color(0.55f, 0.68f, 1f), 0.5f, 4f, 0.6f);
            B.PointLight(lights, "MoonLight（窓・棚）", new Vector3(25.4f, 3f, 0f), new Color(0.55f, 0.68f, 1f), 0.45f, 3.5f, 0.5f);
            Light2D glow = B.PointLight(lights, "FlaskGlow（作業台のフラスコの光）", new Vector3(0f, -1.7f, 0f), Color.white, 0f, 2.4f, 0.4f);
            glow.gameObject.AddComponent<MirrorLight2D>();
            return root;
        }

        /// <summary>あとから増えた物（取引先帳の台帳）が部屋の Prefab になければ足す（ほかの所はそのまま）</summary>
        private static void UpgradeRoom(GameObject prefab, B.BrewAssets a, System.Text.StringBuilder log)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform spots = root.transform.Find("Hotspots");
                if (spots == null) spots = B.Child(root.transform, "Hotspots", Vector3.zero);
                if (spots.Find("Ledger") == null)
                {
                    AddLedger(spots, a);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"・部屋のカウンターに取引先帳を置いた：{path}（Hotspots/Ledger）");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>カウンターの上の取引先帳（台帳の絵＋クリックの当たり判定）</summary>
        private static void AddLedger(Transform spots, B.BrewAssets a)
        {
            Hotspot(spots, "Ledger", RoomHotspot2D.Kind.Ledger, new Vector3(-16.4f, -0.75f, 0f), new Vector2(2f, 1.3f),
                    "取引先帳", "新しい素材の入手ルートを開拓する・希少な素材を取り寄せる");
            Transform ledger = spots.Find("Ledger");
            B.AddSprite(ledger, "Art（台帳の絵）", a.sprites.ledgerBook, "Room", 10, new Vector3(0f, -0.05f, 0f));
        }

        private static void Hotspot(Transform parent, string name, RoomHotspot2D.Kind kind, Vector3 position, Vector2 size, string title, string subtitle)
        {
            Transform t = B.Child(parent, name, position);
            var box = t.gameObject.AddComponent<BoxCollider2D>();
            box.size = size;
            Light2D light = B.PointLight(t, "HoverLight", Vector3.zero, new Color(1f, 0.85f, 0.55f), 0f, Mathf.Max(size.x, size.y) * 0.6f, 0.3f);
            var spot = t.gameObject.AddComponent<RoomHotspot2D>();
            var so = new SerializedObject(spot);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("subtitle").stringValue = subtitle;
            so.FindProperty("hoverLight").objectReferenceValue = light;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // シーン
        // ------------------------------------------------------------------

        private static void BuildScene(B.BrewAssets a, GameObject roomPrefab, UISkin skin, System.Text.StringBuilder log)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera cam = B.CreateCamera(new Vector3(RoomOrigin.x, RoomOrigin.y, -10f));
            B.CreateGlobalLightAndVolume(log);

            GameObject room = B.Instance(roomPrefab, null, RoomOrigin);
            var brewArea = new GameObject("BrewArea（調合台の画面）");
            B.PlaceBrewArea(a, brewArea.transform);
            Transform brewView = B.Child(brewArea.transform, "BrewView（調合台のカメラの位置）", Vector3.zero);
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
            systems.AddComponent<PlayerProgress>();
            var session = systems.AddComponent<BrewSession2D>();
            B.SetRef(session, "art", a.art);
            systems.AddComponent<BrewInput2D>();

            var quests = systems.AddComponent<QuestManager>();
            List<QuestData> all = B.LoadAll<QuestData>();
            all.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            QuestData final = all.Find(q => q.IsFinal);
            QuestData tutorial = all.Find(q => q.name.Contains("_00_")) ?? (all.Count > 0 ? all[0] : null);
            var questObjects = new List<Object>();
            foreach (QuestData q in all) questObjects.Add(q);
            B.SetList(quests, "quests", questObjects);
            B.SetRef(quests, "tutorialQuest", tutorial);
            B.SetRef(quests, "finalQuest", final);
            B.SetRef(quests, "outsidePoint", room.transform.Find("Anchors/CustomerOutside"));
            B.SetRef(quests, "counterPoint", room.transform.Find("Anchors/CustomerCounter"));
            B.SetRef(quests, "customerParent", room.transform.Find("Customers"));
            log.AppendLine($"・相談データ {all.Count} 件（最初：{(tutorial != null ? tutorial.name : "なし")}／最後：{(final != null ? final.name : "なし")}）");

            var view = systems.AddComponent<AtelierView2D>();
            B.SetRef(view, "viewCamera", cam);
            B.SetRef(view, "roomStart", room.transform.Find("Anchors/CameraStart"));
            B.SetRef(view, "boundsLeft", room.transform.Find("Anchors/BoundsLeft"));
            B.SetRef(view, "boundsRight", room.transform.Find("Anchors/BoundsRight"));
            B.SetRef(view, "workbenchFocus", room.transform.Find("Anchors/WorkbenchFocus"));
            B.SetRef(view, "brewView", brewView);

            systems.AddComponent<GameFlow>();
            var audio = systems.AddComponent<AudioManager>();
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            if (library == null) log.AppendLine($"！音の一覧 {SoundLibraryPath} が見つからない（音なしで動く）");
            B.SetRef(audio, "library", library);

            var hudGo = new GameObject("AtelierHUD（タイトル・会話・レシピ本・相談メモ）");
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
