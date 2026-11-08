using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static PotionFrontier.Game.EditorTools.AtelierArt;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// アトリエ（調合室兼お店）のシーンを組み立てるメニュー。
    ///   Tools > Potion Frontier > アトリエを整える（足りない物だけ作る）
    ///     … 素材・相談のデータ、仮のモデル・マテリアル・Prefab（Assets/Art）のうち、ないものだけを作る。
    ///       すでにあるファイルには触らない（自作の物に差し替えたあとでも安全）。シーンがなければシーンも作る。
    ///   Tools > Potion Frontier > アトリエを作り直す
    ///     … 上と同じく足りない物を作ってから、シーン（Assets/Scenes/Atelier.unity）を作り直す。
    ///       前のシーンは Assets/Scenes/Backup に控えを取る。
    ///
    /// 操作できる家具（調合フラスコ・素材棚・ポーション棚・素材箱・仕入れ帳）と素材の容器・ポーションの瓶は
    /// Assets/Art/Prefabs の Prefab をシーンに置く。Prefab の中身（Model）を自作のモデルにすれば、ゲーム中の見た目が変わる。
    /// 部屋・飾り・外の町並みは単純な形で作るので、シーンの中で直接置き換える。
    /// </summary>
    public static class AtelierSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Atelier.unity";
        private const string BackupDir = "Assets/Scenes/Backup";

        private static Texture2D floorTexture;
        private static Texture2D woodTexture;
        private static Texture2D darkWoodTexture;
        private static Texture2D stoneTexture;
        private static Texture2D cobbleTexture;
        private static Texture2D skyTexture;

        // 粒子・線用のマテリアル
        private static Material sparkleMaterial;
        private static Material softParticleMaterial;
        private static Material streamMaterial;
        private static Material shaftMaterial;

        // お店の配置（入口は手前の壁の右寄り）
        private static readonly Vector3 DoorCenter = new Vector3(1.85f, 0f, -3.1f);
        private const float DoorWidth = 1.0f;
        private const float DoorHeight = 2.2f;

        private sealed class Prepared
        {
            public AlchemySampleDataGenerator.SampleData data;
            public QuestSampleData.Result quests;
            public SoundLibrary sounds;
            public ArtCatalog art;
            public GameBalance balance;
            public IngredientDatabase ingredients;
        }

        // ------------------------------------------------------------------
        // メニュー
        // ------------------------------------------------------------------

        [MenuItem("Tools/Potion Frontier/アトリエを整える（足りない物だけ作る）", false, 1)]
        public static void Prepare()
        {
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (!sceneExists && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string summary = PrepareInternal();
            Debug.Log("[Potion Frontier] " + summary);
            EditorUtility.DisplayDialog("アトリエを整える", summary, "OK");
        }

        [MenuItem("Tools/Potion Frontier/アトリエを作り直す（シーンを作り直す）", false, 2)]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("アトリエを作り直す",
                    "シーン（" + ScenePath + "）を作り直します。今のシーンは " + BackupDir + " に控えを取ります。\n\n" +
                    "モデル・マテリアル・Prefab（Assets/Art）と、素材・相談のデータは作り直しません（足りない物だけ作ります）。\n" +
                    "シーンの中で直接置き換えた部屋の飾りなどは、作り直すと元に戻ります（控えのシーンには残っています）。",
                    "作り直す", "キャンセル"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string summary = RebuildInternal();
            Debug.Log("[Potion Frontier] " + summary);
            EditorUtility.DisplayDialog("アトリエを作り直す", summary, "OK");
        }

        /// <summary>確認なしで「整える」（開発用の自動化から呼ぶ）</summary>
        public static string PrepareAutomated()
        {
            return PrepareInternal();
        }

        /// <summary>確認なしで「作り直す」（開発用の自動化から呼ぶ）</summary>
        public static string RebuildAutomated()
        {
            return RebuildInternal();
        }

        private static string PrepareInternal()
        {
            if (!PrepareAssets(out Prepared p, out string error)) return error;
            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            string note;
            if (!sceneExists)
            {
                BuildScene(p);
                note = "シーンがなかったので作りました: " + ScenePath;
            }
            else
            {
                note = "シーン（" + ScenePath + "）はそのままです。シーンも作り直すときは「アトリエを作り直す」を使ってください。";
            }
            AtelierArt.End();
            return Summary(note);
        }

        private static string RebuildInternal()
        {
            if (!PrepareAssets(out Prepared p, out string error)) return error;
            string backup = BackupScene();
            BuildScene(p);
            AtelierArt.End();
            return Summary("シーンを作り直しました: " + ScenePath + (backup != null ? "\n前のシーンの控え: " + backup : ""));
        }

        private static string Summary(string sceneNote)
        {
            var sb = new StringBuilder();
            if (AtelierArt.Created.Count == 0) sb.Append("足りない物はありませんでした（新しく作ったファイルなし）。\n");
            else
            {
                sb.Append("新しく作ったファイル: ").Append(AtelierArt.Created.Count).Append(" 個\n");
                int shown = 0;
                foreach (string path in AtelierArt.Created)
                {
                    if (!path.EndsWith(".prefab") && !path.EndsWith("Catalog.asset") && !path.EndsWith("Balance.asset") && !path.EndsWith("Skin.asset")) continue;
                    if (shown++ >= 14) { sb.Append("  …ほか\n"); break; }
                    sb.Append("  ").Append(path).Append('\n');
                }
            }
            sb.Append(sceneNote);
            return sb.ToString();
        }

        private static string BackupScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return null;
            IngredientCatalog.EnsureFolderPath(BackupDir);
            string path = BackupDir + "/Atelier_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity";
            return AssetDatabase.CopyAsset(ScenePath, path) ? path : null;
        }

        /// <summary>データと見た目のファイルを用意する（ないものだけ作る）</summary>
        private static bool PrepareAssets(out Prepared p, out string error)
        {
            p = null;
            if (!AtelierArt.Begin(out error))
            {
                Debug.LogError("[Potion Frontier] " + error);
                return false;
            }

            p = new Prepared();
            p.data = AlchemySampleDataGenerator.CreateOrUpdateAssets();
            p.quests = QuestSampleData.CreateOrUpdate(p.data);

            CreateSharedAssets();
            p.sounds = EnsureSounds();
            p.art = EnsureCatalog();
            p.balance = EnsureBalance(p.data);
            p.ingredients = IngredientCatalog.EnsureDatabase();
            IngredientCatalog.Refresh(false);

            // 操作できる家具の Prefab
            BrewingFlaskPrefab();
            BrewHeaterPrefab();
            StirRodPrefab();
            BrewTrayPrefab();
            IngredientShelfPrefab(p.data);
            PotionShelfPrefab();
            IngredientChestPrefab();
            OrderBookPrefab();
            AssetDatabase.SaveAssets();
            return true;
        }

        // ------------------------------------------------------------------
        // シーンを組み立てる
        // ------------------------------------------------------------------

        private static void BuildScene(Prepared p)
        {
            Shader postShader = Shader.Find("Hidden/PotionFrontier/PostFX");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupQuality();
            SetupLighting();

            Transform room = new GameObject("Room").transform;
            BuildRoom(room);
            BuildDecorations(room);
            BuildAtmosphere(room);
            Transform outside = new GameObject("Outside").transform;
            BuildOutside(outside);

            // ゲームの仕組み（GameContext はほかより先に動いて、データと見た目の一覧を配る）
            var systems = new GameObject("GameSystems");
            systems.AddComponent<GameContext>().Configure(p.ingredients, p.art, p.balance);
            var alchemy = systems.AddComponent<AlchemyManager>();
            alchemy.EditorSetup(p.data.settings, p.data.Recipes, p.data.Reactions);
            var inventory = systems.AddComponent<PotionInventory>();
            var ingredients = systems.AddComponent<IngredientInventory>();
            var progress = systems.AddComponent<PlayerProgress>();
            var audio = systems.AddComponent<AudioManager>();
            audio.Configure(p.sounds);

            // 操作できる家具（Prefab）
            Place(IngredientShelfPrefab(p.data), room, new Vector3(-2.72f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            BrewingStation station = BuildWorkTable(room, p.sounds);
            Place(PotionShelfPrefab(), room, new Vector3(1.85f, 0f, 2.78f), Quaternion.Euler(0f, 180f, 0f));
            Place(IngredientChestPrefab(), room, new Vector3(-1.72f, 0f, 1.45f), Quaternion.Euler(0f, 90f, 0f));
            ShopDoor door = BuildShopFront(room, out Transform outsidePoint, out Transform doorPoint, out Transform counterPoint);

            PlayerInteractor player = BuildPlayer(alchemy, inventory, ingredients, postShader, out Camera playerCamera, out Transform playerStart);
            Camera titleCamera = BuildTitleCamera(postShader);

            var quests = systems.AddComponent<QuestManager>();
            quests.Configure(p.quests.regular, p.quests.tutorial, p.quests.final, outsidePoint, doorPoint, counterPoint, door,
                             Toon("Customer", Color.white, null, 0f, false, 2.2f), progress);

            var flow = systems.AddComponent<GameFlow>();
            flow.Configure(titleCamera, playerCamera, player.GetComponent<FirstPersonController>(), player, inventory, progress, quests, station, playerStart);

            var hud = systems.AddComponent<AtelierHUD>();
            hud.Configure(inventory, station, player, quests, progress);
            // 調合台（フラスコを調べると、カメラが寄ってマウスで直接作業する画面になる）
            systems.AddComponent<BrewMode>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Prefab をシーンに置く（Prefab とのつながりを保つので、Prefab を直すとシーンにも反映される）</summary>
        private static GameObject Place(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            return go;
        }

        // ------------------------------------------------------------------
        // 共有アセット（テクスチャ・粒子用のマテリアル）
        // ------------------------------------------------------------------

        private static void CreateSharedAssets()
        {
            floorTexture = TextureAsset("FloorPlanks", () => ProceduralTextures.WoodPlanks(256, new Color(0.5f, 0.35f, 0.23f), 6, 11));
            woodTexture = WoodTexture();
            darkWoodTexture = DarkWoodTexture();
            stoneTexture = TextureAsset("StoneBricks", () => ProceduralTextures.StoneBricks(256, new Color(0.52f, 0.5f, 0.5f), 5));
            cobbleTexture = TextureAsset("Cobble", () => ProceduralTextures.StoneBricks(256, new Color(0.6f, 0.55f, 0.48f), 19));
            skyTexture = TextureAsset("WindowSky", () => ProceduralTextures.WindowSky(128, 128));

            sparkleMaterial = Sparkle();
            softParticleMaterial = SoftParticle();
            streamMaterial = PourStreamMaterial();
            Texture2D shaftTexture = TextureAsset("LightShaft", LightShaftTexture);
            shaftMaterial = MaterialAsset("LightShaft", () =>
            {
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = "LightShaft" };
                m.mainTexture = shaftTexture;
                m.SetColor("_TintColor", new Color(1f, 0.85f, 0.6f, 0.07f));
                return m;
            });
        }

        private static Material WindowSky()
        {
            return MaterialAsset("WindowSky", () => new Material(Shader.Find("Unlit/Texture")) { name = "WindowSky", mainTexture = skyTexture });
        }

        private static Material FlameMaterial()
        {
            return Toon("Flame", new Color(1f, 0.75f, 0.3f), null, 0f, true, 0f, new Color(1.6f, 0.9f, 0.3f));
        }

        // ------------------------------------------------------------------
        // 画質・光
        // ------------------------------------------------------------------

        private static void SetupQuality()
        {
            // 今の画質レベルだけを調整する（ProjectSettings/QualitySettings に保存される）
            // 「標準」と同じ値。ゲーム中は設定の画質（GraphicsQuality）で切り替わる
            QualitySettings.antiAliasing = 2;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 13f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.pixelLightCount = 4;
        }

        private static void SetupLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.42f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.3f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.14f, 0.11f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.24f, 0.18f, 0.12f);

            // 窓から差し込む日の光（壁と天井は影を落とさない設定にして、光を通す）
            var sun = new GameObject("Sunlight (Window)").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.74f);
            sun.intensity = 0.75f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(38f, -72f, 0f);
        }

        // ------------------------------------------------------------------
        // 部屋
        // ------------------------------------------------------------------

        private static void BuildRoom(Transform room)
        {
            Material stone = Toon("Stone", Color.white, stoneTexture, 0.6f, true, 1.6f);
            Material floor = Toon("Floor", Color.white, floorTexture, 0.5f, true, 1.6f);
            Material dark = DarkWood();

            Box("Floor", room, new Vector3(0f, -0.05f, 0f), new Vector3(6.4f, 0.1f, 6.4f), floor, true, false);
            Box("Ceiling", room, new Vector3(0f, 3.05f, 0f), new Vector3(6.4f, 0.1f, 6.4f), dark, true, false);
            Box("Wall_Back", room, new Vector3(0f, 1.5f, 3.1f), new Vector3(6.4f, 3f, 0.2f), stone, true, false);
            Box("Wall_Left", room, new Vector3(-3.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 6.4f), stone, true, false);
            Box("Wall_Right", room, new Vector3(3.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 6.4f), stone, true, false);

            // 手前の壁（入口の穴をあける）
            float doorLeft = DoorCenter.x - DoorWidth * 0.5f;
            float doorRight = DoorCenter.x + DoorWidth * 0.5f;
            float leftWidth = doorLeft + 3.2f;
            float rightWidth = 3.2f - doorRight;
            Box("Wall_Front_L", room, new Vector3(-3.2f + leftWidth * 0.5f, 1.5f, -3.1f), new Vector3(leftWidth, 3f, 0.2f), stone, true, false);
            Box("Wall_Front_R", room, new Vector3(doorRight + rightWidth * 0.5f, 1.5f, -3.1f), new Vector3(rightWidth, 3f, 0.2f), stone, true, false);
            Box("Wall_Front_Top", room, new Vector3(DoorCenter.x, (DoorHeight + 3f) * 0.5f, -3.1f), new Vector3(DoorWidth, 3f - DoorHeight, 0.2f), stone, true, false);

            // 梁と柱
            for (int i = -1; i <= 1; i++)
            {
                Box("Beam" + i, room, new Vector3(0f, 2.86f, i * 1.6f), new Vector3(6.2f, 0.18f, 0.22f), dark, false, false);
            }
            foreach (Vector2 corner in new[] { new Vector2(-2.88f, -2.88f), new Vector2(2.88f, -2.88f), new Vector2(-2.88f, 2.88f), new Vector2(2.88f, 2.88f) })
            {
                Box("Post", room, new Vector3(corner.x, 1.5f, corner.y), new Vector3(0.24f, 3f, 0.24f), dark, true, false);
            }

            // 窓（右の壁）
            Transform window = new GameObject("Window").transform;
            window.SetParent(room, false);
            window.localPosition = new Vector3(2.98f, 1.65f, 0.2f);
            Material skyMaterial = WindowSky();
            Box("Sky", window, new Vector3(0.01f, 0f, 0f), new Vector3(0.02f, 1.1f, 0.9f), skyMaterial, false, false);
            Box("Frame_Top", window, new Vector3(-0.02f, 0.6f, 0f), new Vector3(0.12f, 0.1f, 1.1f), dark, false);
            Box("Frame_Bottom", window, new Vector3(-0.05f, -0.6f, 0f), new Vector3(0.2f, 0.1f, 1.2f), dark, false);
            Box("Frame_Left", window, new Vector3(-0.02f, 0f, -0.5f), new Vector3(0.12f, 1.2f, 0.1f), dark, false);
            Box("Frame_Right", window, new Vector3(-0.02f, 0f, 0.5f), new Vector3(0.12f, 1.2f, 0.1f), dark, false);
            Box("Mullion_V", window, new Vector3(-0.01f, 0f, 0f), new Vector3(0.06f, 1.1f, 0.05f), dark, false);
            Box("Mullion_H", window, new Vector3(-0.01f, 0.1f, 0f), new Vector3(0.06f, 0.05f, 0.9f), dark, false);

            // 窓辺の植木鉢
            Material clay = Toon("Clay", new Color(0.72f, 0.45f, 0.3f), null, 0f, false);
            Material leaf = Toon("Leaf", new Color(0.42f, 0.66f, 0.32f), null, 0f, false, 1.8f);
            for (int i = 0; i < 2; i++)
            {
                Transform pot = new GameObject("WindowPlant").transform;
                pot.SetParent(window, false);
                pot.localPosition = new Vector3(-0.08f, -0.55f, -0.25f + i * 0.5f);
                Cylinder("Pot", pot, new Vector3(0f, 0.06f, 0f), new Vector3(0.12f, 0.06f, 0.12f), clay, false);
                Sphere("Leaves", pot, new Vector3(0f, 0.17f, 0f), new Vector3(0.17f, 0.14f, 0.17f), leaf);
                Sphere("Leaves", pot, new Vector3(0.03f, 0.25f, 0.02f), new Vector3(0.1f, 0.1f, 0.1f), leaf);
            }
        }

        private static void BuildDecorations(Transform room)
        {
            Material wood = Wood();
            Material dark = DarkWood();

            // 樽
            Material barrelMat = Toon("Barrel", Color.white, woodTexture, 2f, false);
            foreach (Vector3 p in new[] { new Vector3(2.55f, 0f, -2.0f), new Vector3(2.5f, 0f, -1.35f), new Vector3(-2.5f, 0f, 1.85f) })
            {
                GameObject barrel = MeshObject("Barrel", room, p, GetMesh("Barrel"), barrelMat);
                barrel.AddComponent<BoxCollider>().size = new Vector3(0.58f, 0.7f, 0.58f);
                barrel.GetComponent<BoxCollider>().center = new Vector3(0f, 0.35f, 0f);
                Cylinder("Band", barrel.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.57f, 0.02f, 0.57f), Iron(), false);
                Cylinder("Band", barrel.transform, new Vector3(0f, 0.52f, 0f), new Vector3(0.57f, 0.02f, 0.57f), Iron(), false);
            }

            // 麻袋（お客さん側の隅）
            Material sack = Toon("Sack", new Color(0.78f, 0.66f, 0.47f), null, 0f, false);
            MeshObject("Sack", room, new Vector3(-2.45f, 0f, -2.5f), GetMesh("Sack"), sack);
            MeshObject("Sack", room, new Vector3(-1.95f, 0f, -2.65f), GetMesh("Sack"), sack).transform.localScale = Vector3.one * 0.8f;

            // 吊るした薬草の束
            Material herbGreen = Toon("HerbGreen", new Color(0.45f, 0.68f, 0.32f), null, 0f, true, 1.6f);
            Material herbPurple = Toon("HerbPurple", new Color(0.62f, 0.45f, 0.75f), null, 0f, true, 1.6f);
            Material herbYellow = Toon("HerbYellow", new Color(0.82f, 0.75f, 0.35f), null, 0f, true, 1.6f);
            Material[] herbMats = { herbGreen, herbPurple, herbGreen, herbYellow, herbGreen };
            for (int i = 0; i < 5; i++)
            {
                var bundle = new GameObject("HerbBundle").transform;
                bundle.SetParent(room, false);
                bundle.localPosition = new Vector3(-1.4f + i * 0.7f, 2.77f, 1.6f + 0.12f);
                Cylinder("String", bundle, new Vector3(0f, -0.08f, 0f), new Vector3(0.008f, 0.08f, 0.008f), Cork(), false);
                for (int k = 0; k < 5; k++)
                {
                    float angle = (k - 2) * 9f;
                    GameObject stem = Capsule("Stem", bundle, new Vector3((k - 2) * 0.025f, -0.32f, 0f), new Vector3(0.05f, 0.17f, 0.05f), herbMats[i], false);
                    stem.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
                Cylinder("Tie", bundle, new Vector3(0f, -0.17f, 0f), new Vector3(0.06f, 0.015f, 0.06f), Cork(), false);
            }

            // 壁のランタン（作業台の上）
            Transform lantern = new GameObject("Lantern").transform;
            lantern.SetParent(room, false);
            lantern.localPosition = new Vector3(0f, 2.3f, 2.86f);
            Box("Bracket", lantern, new Vector3(0f, 0.18f, 0.08f), new Vector3(0.04f, 0.04f, 0.2f), Iron(), false);
            Box("Cage", lantern, Vector3.zero, new Vector3(0.16f, 0.24f, 0.16f), Glass(), false);
            Box("Cap", lantern, new Vector3(0f, 0.14f, 0f), new Vector3(0.2f, 0.05f, 0.2f), Iron(), false);
            Box("Base", lantern, new Vector3(0f, -0.13f, 0f), new Vector3(0.18f, 0.03f, 0.18f), Iron(), false);
            GameObject lanternFlame = Box("Flame", lantern, Vector3.zero, new Vector3(0.07f, 0.12f, 0.07f),
                FlameMaterial(), false, false);
            var lanternLight = new GameObject("LanternLight").AddComponent<Light>();
            lanternLight.transform.SetParent(lantern, false);
            lanternLight.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            lanternLight.type = LightType.Point;
            lanternLight.color = new Color(1f, 0.68f, 0.35f);
            lanternLight.intensity = 1.5f;
            lanternLight.range = 6f;
            lanternLight.shadows = LightShadows.None;
            lanternLight.gameObject.AddComponent<LightFlicker>().Configure(0.1f, 1.8f, lanternFlame.transform);
            // 点光源の影は重い（部屋全体を6方向から描き直す）ので、画質「高」のときだけ
            lanternLight.gameObject.AddComponent<QualityFeature>().Configure(GameSettings.QualityHigh, true);

            // お店側を照らすランタン（カウンターの上の梁から吊るす）
            Transform shopLantern = new GameObject("ShopLantern").transform;
            shopLantern.SetParent(room, false);
            shopLantern.localPosition = new Vector3(0.2f, 2.3f, -1.6f);
            Cylinder("Chain", shopLantern, new Vector3(0f, 0.3f, 0f), new Vector3(0.012f, 0.22f, 0.012f), Iron(), false);
            Box("Cage", shopLantern, Vector3.zero, new Vector3(0.16f, 0.22f, 0.16f), Glass(), false);
            Box("Cap", shopLantern, new Vector3(0f, 0.13f, 0f), new Vector3(0.2f, 0.05f, 0.2f), Iron(), false);
            Box("Base", shopLantern, new Vector3(0f, -0.12f, 0f), new Vector3(0.18f, 0.03f, 0.18f), Iron(), false);
            GameObject shopFlame = Box("Flame", shopLantern, Vector3.zero, new Vector3(0.06f, 0.11f, 0.06f), FlameMaterial(), false, false);
            var shopLight = new GameObject("ShopLight").AddComponent<Light>();
            shopLight.transform.SetParent(shopLantern, false);
            shopLight.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            shopLight.type = LightType.Point;
            shopLight.color = new Color(1f, 0.84f, 0.64f);
            shopLight.intensity = 0.55f;
            shopLight.range = 4f;
            shopLight.shadows = LightShadows.None;
            shopLight.gameObject.AddComponent<LightFlicker>().Configure(0.1f, 2f, shopFlame.transform);

            // 部屋全体をほんのり照らす補助光
            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.transform.SetParent(room, false);
            fill.transform.localPosition = new Vector3(-1.2f, 2.4f, -0.5f);
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.82f, 0.6f);
            fill.intensity = 0.45f;
            fill.range = 6.5f;
            fill.shadows = LightShadows.None;

            // 本棚の飾り（ポーション棚の左、背面の壁ぎわ）
            Material[] bookMats =
            {
                Toon("BookRed", new Color(0.6f, 0.2f, 0.18f)), Toon("BookGreen", new Color(0.25f, 0.42f, 0.28f)),
                Toon("BookBlue", new Color(0.22f, 0.3f, 0.5f)), Toon("BookBrown", new Color(0.5f, 0.33f, 0.18f)),
            };
            Transform bookcase = new GameObject("SmallBookcase").transform;
            bookcase.SetParent(room, false);
            bookcase.localPosition = new Vector3(-1.9f, 0f, 2.75f);
            Box("Side_L", bookcase, new Vector3(-0.5f, 0.6f, 0f), new Vector3(0.04f, 1.2f, 0.38f), wood);
            Box("Side_R", bookcase, new Vector3(0.5f, 0.6f, 0f), new Vector3(0.04f, 1.2f, 0.38f), wood);
            Box("Back", bookcase, new Vector3(0f, 0.6f, 0.17f), new Vector3(1f, 1.2f, 0.03f), dark, false);
            for (int s = 0; s < 3; s++)
            {
                float y = 0.05f + s * 0.55f;
                Box("Board", bookcase, new Vector3(0f, y, 0f), new Vector3(1f, 0.04f, 0.38f), wood);
                if (s == 2) continue;
                float x = -0.44f;
                int n = 0;
                while (x < 0.42f)
                {
                    float w = 0.05f + (n * 37 % 5) * 0.008f;
                    float h = 0.3f + (n * 53 % 7) * 0.025f;
                    Box("Book", bookcase, new Vector3(x + w * 0.5f, y + 0.02f + h * 0.5f, -0.02f), new Vector3(w, h, 0.26f), bookMats[n % bookMats.Length], false, false);
                    x += w + 0.008f;
                    n++;
                }
            }

            // 床のラグ
            Material rug = Toon("Rug", new Color(0.62f, 0.26f, 0.2f), null, 0f, true, 1.2f);
            Material rugBorder = Toon("RugBorder", new Color(0.85f, 0.68f, 0.38f), null, 0f, true, 1.2f);
            Box("Rug", room, new Vector3(0f, 0.004f, 0.35f), new Vector3(2.2f, 0.008f, 1.4f), rugBorder, false, false);
            Box("RugInner", room, new Vector3(0f, 0.009f, 0.35f), new Vector3(1.95f, 0.008f, 1.15f), rug, false, false);
        }

        /// <summary>空気感：窓から差す光の筋・漂うほこり</summary>
        private static void BuildAtmosphere(Transform room)
        {
            // 光の筋（太陽の向きに沿って、窓から床へ）
            Vector3 sunDir = Quaternion.Euler(38f, -72f, 0f) * Vector3.forward;
            var shaft = new GameObject("WindowLightShaft");
            shaft.transform.SetParent(room, false);
            shaft.transform.localPosition = new Vector3(2.96f, 1.65f, 0.2f);
            Mesh shaftMesh = MeshAsset("LightShaft", () => LightShaftMesh(sunDir, 0.88f, 1.08f, 2.9f));
            shaft.AddComponent<MeshFilter>().sharedMesh = shaftMesh;
            var shaftRenderer = shaft.AddComponent<MeshRenderer>();
            shaftRenderer.sharedMaterial = shaftMaterial;
            shaftRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shaftRenderer.receiveShadows = false;

            // 漂うほこり（光の中でキラキラ見える）
            var dustGo = new GameObject("DustMotes");
            dustGo.transform.SetParent(room, false);
            dustGo.transform.localPosition = new Vector3(0.6f, 1.4f, 0.5f);
            var dust = dustGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = dust.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.75f, 0.45f), new Color(1f, 0.85f, 0.6f, 0.25f));
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = dust.emission;
            emission.rateOverTime = 8f;
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(5.2f, 2.4f, 5.2f);
            ParticleSystem.NoiseModule noise = dust.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.05f;
            ParticleSystem.ColorOverLifetimeModule col = dust.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeInOut(Color.white));
            dustGo.AddComponent<QualityFeature>().Configure(GameSettings.QualityStandard, false);
            var dustRenderer = dustGo.GetComponent<ParticleSystemRenderer>();
            dustRenderer.sharedMaterial = sparkleMaterial;
            dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------
        // お店の入口・カウンター
        // ------------------------------------------------------------------

        private static ShopDoor BuildShopFront(Transform room, out Transform outsidePoint, out Transform doorPoint, out Transform counterPoint)
        {
            Material dark = DarkWood();
            Material wood = Wood();

            // 入口の枠
            Transform entrance = new GameObject("Entrance").transform;
            entrance.SetParent(room, false);
            entrance.localPosition = DoorCenter;
            float half = DoorWidth * 0.5f;
            Box("Frame_L", entrance, new Vector3(-half - 0.05f, DoorHeight * 0.5f + 0.02f, 0f), new Vector3(0.1f, DoorHeight + 0.04f, 0.3f), dark);
            Box("Frame_R", entrance, new Vector3(half + 0.05f, DoorHeight * 0.5f + 0.02f, 0f), new Vector3(0.1f, DoorHeight + 0.04f, 0.3f), dark);
            Box("Lintel", entrance, new Vector3(0f, DoorHeight + 0.06f, 0f), new Vector3(DoorWidth + 0.3f, 0.12f, 0.32f), dark);
            Box("Threshold", entrance, new Vector3(0f, 0.01f, 0f), new Vector3(DoorWidth + 0.1f, 0.02f, 0.3f), dark, false);

            // 扉（外開き。蝶番は左側）
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(entrance, false);
            hinge.localPosition = new Vector3(-half + 0.02f, 0f, -0.06f);
            Transform leaf = new GameObject("DoorLeaf").transform;
            leaf.SetParent(hinge, false);
            float leafWidth = DoorWidth - 0.04f;
            Box("Panel", leaf, new Vector3(leafWidth * 0.5f, DoorHeight * 0.5f, 0f), new Vector3(leafWidth, DoorHeight - 0.02f, 0.06f),
                Toon("DoorWood", Color.white, woodTexture, 1.6f), true);
            Box("Batten_Top", leaf, new Vector3(leafWidth * 0.5f, DoorHeight * 0.78f, 0.045f), new Vector3(leafWidth - 0.08f, 0.1f, 0.03f), dark, false);
            Box("Batten_Bottom", leaf, new Vector3(leafWidth * 0.5f, DoorHeight * 0.22f, 0.045f), new Vector3(leafWidth - 0.08f, 0.1f, 0.03f), dark, false);
            Box("Window", leaf, new Vector3(leafWidth * 0.5f, DoorHeight * 0.62f, 0f), new Vector3(0.36f, 0.3f, 0.07f), Glass(), false);
            Sphere("Knob_In", leaf, new Vector3(leafWidth - 0.1f, 1.0f, 0.06f), Vector3.one * 0.06f, Brass());
            Sphere("Knob_Out", leaf, new Vector3(leafWidth - 0.1f, 1.0f, -0.06f), Vector3.one * 0.06f, Brass());
            var door = entrance.gameObject.AddComponent<ShopDoor>();
            door.Configure(leaf.parent, 95f);

            // 扉が開いていてもプレイヤーは外に出られないようにする（見えない壁）
            var blocker = new GameObject("DoorBlocker");
            blocker.transform.SetParent(entrance, false);
            blocker.transform.localPosition = new Vector3(0f, 1.2f, 0.12f);
            blocker.AddComponent<BoxCollider>().size = new Vector3(DoorWidth, 2.4f, 0.05f);

            // ドアベル（入口の内側の上）
            Transform bell = new GameObject("DoorBell").transform;
            bell.SetParent(entrance, false);
            bell.localPosition = new Vector3(half - 0.12f, DoorHeight + 0.0f, 0.2f);
            Box("Arm", bell, new Vector3(0f, 0.05f, -0.04f), new Vector3(0.02f, 0.02f, 0.1f), Iron(), false);
            Cylinder("Bell", bell, new Vector3(0f, -0.02f, 0f), new Vector3(0.07f, 0.04f, 0.07f), Brass(), false);
            Sphere("Clapper", bell, new Vector3(0f, -0.07f, 0f), Vector3.one * 0.025f, Brass());

            // 入口マット
            Box("WelcomeMat", room, new Vector3(DoorCenter.x, 0.006f, -2.6f), new Vector3(0.95f, 0.012f, 0.6f),
                Toon("Mat", new Color(0.5f, 0.32f, 0.22f), null, 0f, true, 1.2f), false, false);

            // カウンター（お客さんはこの向こうに立つ）
            Transform counter = new GameObject("Counter").transform;
            counter.SetParent(room, false);
            counter.localPosition = new Vector3(-0.1f, 0f, -1.95f);
            const float length = 2.6f;
            const float top = 0.95f;
            Box("Top", counter, new Vector3(0f, top - 0.03f, 0f), new Vector3(length, 0.06f, 0.6f), wood);
            Box("FrontPanel", counter, new Vector3(0f, (top - 0.06f) * 0.5f, -0.24f), new Vector3(length - 0.08f, top - 0.06f, 0.06f), dark);
            Box("Side_L", counter, new Vector3(-length * 0.5f + 0.04f, (top - 0.06f) * 0.5f, 0f), new Vector3(0.08f, top - 0.06f, 0.56f), dark);
            Box("Side_R", counter, new Vector3(length * 0.5f - 0.04f, (top - 0.06f) * 0.5f, 0f), new Vector3(0.08f, top - 0.06f, 0.56f), dark);
            Box("Shelf", counter, new Vector3(0f, 0.38f, 0.02f), new Vector3(length - 0.16f, 0.04f, 0.46f), wood, false);
            for (int i = 0; i < 4; i++)
            {
                Box("Trim", counter, new Vector3(-length * 0.5f + 0.35f + i * 0.63f, (top - 0.06f) * 0.5f, -0.28f), new Vector3(0.05f, top - 0.16f, 0.03f), wood, false);
            }

            // カウンターの上の小物
            Transform serviceBell = new GameObject("ServiceBell").transform;
            serviceBell.SetParent(counter, false);
            serviceBell.localPosition = new Vector3(0.75f, top, -0.1f);
            Cylinder("Base", serviceBell, new Vector3(0f, 0.008f, 0f), new Vector3(0.11f, 0.008f, 0.11f), DarkWood(), false);
            Sphere("Dome", serviceBell, new Vector3(0f, 0.035f, 0f), new Vector3(0.09f, 0.07f, 0.09f), Brass());
            Sphere("Button", serviceBell, new Vector3(0f, 0.075f, 0f), Vector3.one * 0.018f, Brass());

            // 取引先帳（台帳）：[E] で素材の入手ルートを開拓する・取り寄せる
            Place(OrderBookPrefab(), counter, new Vector3(-0.55f, top, 0.05f), Quaternion.Euler(0f, 8f, 0f));

            Transform coinDish = new GameObject("CoinDish").transform;
            coinDish.SetParent(counter, false);
            coinDish.localPosition = new Vector3(0.3f, top, 0.05f);
            Cylinder("Dish", coinDish, new Vector3(0f, 0.01f, 0f), new Vector3(0.16f, 0.01f, 0.16f), Toon("Porcelain", new Color(0.92f, 0.9f, 0.86f), null, 0f, false), false);
            Material gold = Toon("Gold", new Color(0.95f, 0.72f, 0.28f), null, 0f, false, 1.6f, new Color(0.08f, 0.05f, 0.01f), new Color(1f, 0.95f, 0.7f, 0.4f));
            for (int i = 0; i < 4; i++)
            {
                Cylinder("Coin", coinDish, new Vector3((i % 2) * 0.03f - 0.015f, 0.024f + i * 0.007f, (i / 2) * 0.025f - 0.01f), new Vector3(0.045f, 0.003f, 0.045f), gold, false);
            }

            Transform plant = new GameObject("CounterPlant").transform;
            plant.SetParent(counter, false);
            plant.localPosition = new Vector3(-1.08f, top, -0.05f);
            Cylinder("Pot", plant, new Vector3(0f, 0.07f, 0f), new Vector3(0.14f, 0.07f, 0.14f), Toon("Clay", Color.white), false);
            Sphere("Leaves", plant, new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.16f, 0.2f), Toon("Leaf", Color.white));
            Sphere("Flower", plant, new Vector3(0.05f, 0.27f, -0.04f), Vector3.one * 0.05f, Toon("FlowerPink", new Color(0.98f, 0.62f, 0.72f)));
            Sphere("Flower", plant, new Vector3(-0.05f, 0.25f, 0.03f), Vector3.one * 0.045f, Toon("FlowerYellow", new Color(1f, 0.88f, 0.4f)));

            // お客さんの立ち位置
            Transform points = new GameObject("CustomerPoints").transform;
            points.SetParent(room, false);
            outsidePoint = Point(points, "Outside", new Vector3(DoorCenter.x, 0f, -7.2f), Vector3.forward);
            doorPoint = Point(points, "Door", new Vector3(DoorCenter.x, 0f, -2.65f), Vector3.forward);
            counterPoint = Point(points, "Counter", new Vector3(-0.1f, 0f, -2.62f), Vector3.forward);
            return door;
        }

        private static Transform Point(Transform parent, string name, Vector3 position, Vector3 forward)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = Quaternion.LookRotation(forward);
            return t;
        }

        // ------------------------------------------------------------------
        // 外の町並み（入口から見える）
        // ------------------------------------------------------------------

        private static void BuildOutside(Transform outside)
        {
            Material cobble = Toon("Cobble", Color.white, cobbleTexture, 0.7f, true, 1.4f);
            Box("Street", outside, new Vector3(1f, -0.05f, -7.6f), new Vector3(16f, 0.1f, 8.8f), cobble, true, false);

            // 空（遠くの板に空の絵）
            Box("SkyBackdrop", outside, new Vector3(1.5f, 5f, -14f), new Vector3(44f, 14f, 0.1f), WindowSky(), false, false);

            Material plasterA = Toon("PlasterCream", new Color(0.95f, 0.88f, 0.74f), null, 0f, true, 2f);
            Material plasterB = Toon("PlasterPink", new Color(0.94f, 0.78f, 0.72f), null, 0f, true, 2f);
            Material plasterC = Toon("PlasterBlue", new Color(0.76f, 0.84f, 0.9f), null, 0f, true, 2f);
            Material roofA = Toon("RoofRed", new Color(0.72f, 0.33f, 0.24f), null, 0f, true, 2f);
            Material roofB = Toon("RoofBlue", new Color(0.32f, 0.42f, 0.58f), null, 0f, true, 2f);
            Material windowGlass = Toon("HouseWindow", new Color(0.55f, 0.72f, 0.82f), null, 0f, true, 1.6f, new Color(0.12f, 0.16f, 0.2f));

            House(outside, new Vector3(-2.6f, 0f, -10.2f), new Vector3(3.6f, 3.8f, 2.2f), plasterA, roofA, windowGlass);
            House(outside, new Vector3(1.6f, 0f, -10.4f), new Vector3(4.0f, 3.3f, 2.2f), plasterB, roofB, windowGlass);
            House(outside, new Vector3(5.8f, 0f, -10.0f), new Vector3(3.4f, 4.2f, 2.2f), plasterC, roofA, windowGlass);

            // 木
            Material trunk = Toon("Trunk", new Color(0.45f, 0.3f, 0.2f), null, 0f, false);
            Material foliage = Toon("Foliage", new Color(0.4f, 0.64f, 0.3f), null, 0f, false, 2.2f);
            foreach (Vector3 p in new[] { new Vector3(4.6f, 0f, -6.6f), new Vector3(-1.6f, 0f, -7.4f) })
            {
                Transform tree = new GameObject("Tree").transform;
                tree.SetParent(outside, false);
                tree.localPosition = p;
                Cylinder("Trunk", tree, new Vector3(0f, 0.9f, 0f), new Vector3(0.22f, 0.9f, 0.22f), trunk, false);
                Sphere("Crown", tree, new Vector3(0f, 2.3f, 0f), new Vector3(1.6f, 1.4f, 1.6f), foliage);
                Sphere("Crown", tree, new Vector3(0.45f, 2.75f, 0.2f), new Vector3(1.0f, 0.9f, 1.0f), foliage);
                Sphere("Crown", tree, new Vector3(-0.4f, 2.6f, -0.25f), new Vector3(0.9f, 0.8f, 0.9f), foliage);
            }

            // 街灯
            Transform lamp = new GameObject("StreetLamp").transform;
            lamp.SetParent(outside, false);
            lamp.localPosition = new Vector3(0.4f, 0f, -5.2f);
            Cylinder("Pole", lamp, new Vector3(0f, 1.2f, 0f), new Vector3(0.07f, 1.2f, 0.07f), Iron(), false);
            Box("Head", lamp, new Vector3(0f, 2.5f, 0f), new Vector3(0.22f, 0.28f, 0.22f), Glass(), false);
            Box("Cap", lamp, new Vector3(0f, 2.68f, 0f), new Vector3(0.28f, 0.06f, 0.28f), Iron(), false);
            Box("Flame", lamp, new Vector3(0f, 2.5f, 0f), new Vector3(0.08f, 0.12f, 0.08f), FlameMaterial(), false, false);

            // 花壇（お店の前）
            Material flowerRed = Toon("FlowerRed", new Color(0.92f, 0.36f, 0.36f));
            Material flowerWhite = Toon("FlowerWhite", new Color(0.98f, 0.96f, 0.9f));
            Transform planter = new GameObject("Planter").transform;
            planter.SetParent(outside, false);
            planter.localPosition = new Vector3(-0.4f, 0f, -3.55f);
            Box("Box", planter, new Vector3(0f, 0.18f, 0f), new Vector3(1.6f, 0.36f, 0.4f), DarkWood());
            for (int i = 0; i < 7; i++)
            {
                Sphere("Bush", planter, new Vector3(-0.66f + i * 0.22f, 0.42f, 0f), new Vector3(0.24f, 0.2f, 0.24f), Toon("Leaf", Color.white));
                Sphere("Flower", planter, new Vector3(-0.62f + i * 0.22f, 0.52f, -0.06f), Vector3.one * 0.07f, i % 2 == 0 ? flowerRed : flowerWhite);
            }

            // 看板（入口の上）
            Transform sign = new GameObject("ShopSign").transform;
            sign.SetParent(outside, false);
            sign.localPosition = new Vector3(DoorCenter.x, 2.65f, -3.3f);
            Box("Board", sign, Vector3.zero, new Vector3(0.9f, 0.4f, 0.05f), Wood(), false);
            Sphere("PotionIcon", sign, new Vector3(0f, -0.02f, -0.04f), new Vector3(0.18f, 0.18f, 0.04f), Toon("SignPotion", new Color(0.4f, 0.85f, 0.55f), null, 0f, false, 1.8f, new Color(0.08f, 0.17f, 0.11f), new Color(1f, 1f, 1f, 0.45f)));
            Box("Neck", sign, new Vector3(0f, 0.1f, -0.04f), new Vector3(0.06f, 0.08f, 0.03f), Glass(), false);
        }

        private static void House(Transform parent, Vector3 position, Vector3 size, Material plaster, Material roof, Material windowGlass)
        {
            Transform house = new GameObject("House").transform;
            house.SetParent(parent, false);
            house.localPosition = position;
            Material dark = DarkWood();

            Box("Body", house, new Vector3(0f, size.y * 0.5f, 0f), size, plaster, false);
            // 木組み
            Box("Beam_Mid", house, new Vector3(0f, size.y * 0.48f, size.z * 0.5f + 0.02f), new Vector3(size.x, 0.12f, 0.06f), dark, false);
            Box("Beam_Top", house, new Vector3(0f, size.y - 0.06f, size.z * 0.5f + 0.02f), new Vector3(size.x, 0.12f, 0.06f), dark, false);
            Box("Post_L", house, new Vector3(-size.x * 0.5f + 0.06f, size.y * 0.5f, size.z * 0.5f + 0.02f), new Vector3(0.12f, size.y, 0.06f), dark, false);
            Box("Post_R", house, new Vector3(size.x * 0.5f - 0.06f, size.y * 0.5f, size.z * 0.5f + 0.02f), new Vector3(0.12f, size.y, 0.06f), dark, false);

            // 屋根（手前に傾いた板＋棟）
            GameObject roofFront = Box("Roof", house, new Vector3(0f, size.y + 0.45f, size.z * 0.18f), new Vector3(size.x + 0.4f, 0.16f, size.z * 0.75f), roof, false);
            roofFront.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            GameObject roofBack = Box("Roof", house, new Vector3(0f, size.y + 0.45f, -size.z * 0.18f), new Vector3(size.x + 0.4f, 0.16f, size.z * 0.75f), roof, false);
            roofBack.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            Box("Chimney", house, new Vector3(size.x * 0.28f, size.y + 0.9f, -0.2f), new Vector3(0.36f, 0.9f, 0.36f), Toon("Brick", new Color(0.62f, 0.38f, 0.3f)), false);

            // 窓と扉
            float front = size.z * 0.5f + 0.03f;
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * size.x * 0.27f;
                Box("Window", house, new Vector3(x, size.y * 0.72f, front), new Vector3(0.62f, 0.7f, 0.04f), windowGlass, false);
                Box("WindowFrame", house, new Vector3(x, size.y * 0.72f - 0.38f, front + 0.02f), new Vector3(0.76f, 0.08f, 0.1f), dark, false);
                Box("Mullion", house, new Vector3(x, size.y * 0.72f, front + 0.01f), new Vector3(0.05f, 0.7f, 0.03f), dark, false);
                Box("FlowerBox", house, new Vector3(x, size.y * 0.72f - 0.46f, front + 0.1f), new Vector3(0.7f, 0.14f, 0.16f), Wood(), false);
            }
            Box("Door", house, new Vector3(0f, 0.95f, front), new Vector3(0.9f, 1.9f, 0.05f), dark, false);
            Box("Window_Low", house, new Vector3(size.x * 0.3f, 1.1f, front), new Vector3(0.7f, 0.6f, 0.04f), windowGlass, false);
        }

        // ------------------------------------------------------------------
        // 作業台と調合フラスコ
        // ------------------------------------------------------------------

        private static BrewingStation BuildWorkTable(Transform room, SoundLibrary sounds)
        {
            Material wood = Wood();
            Transform table = new GameObject("WorkTable").transform;
            table.SetParent(room, false);
            table.localPosition = new Vector3(0f, 0f, 1.75f);

            const float top = 0.9f;
            Box("Top", table, new Vector3(0f, top - 0.04f, 0f), new Vector3(2.3f, 0.08f, 1.0f), wood);
            foreach (Vector2 leg in new[] { new Vector2(-1.05f, -0.42f), new Vector2(1.05f, -0.42f), new Vector2(-1.05f, 0.42f), new Vector2(1.05f, 0.42f) })
            {
                Box("Leg", table, new Vector3(leg.x, (top - 0.08f) * 0.5f, leg.y), new Vector3(0.09f, top - 0.08f, 0.09f), DarkWood());
            }
            Box("Stretcher", table, new Vector3(0f, 0.2f, 0f), new Vector3(2.1f, 0.06f, 0.06f), DarkWood(), false);

            // 調合台：加熱台（アルコールランプと三脚）の上に調合フラスコ、フラスコにガラス棒、手前にトレイ（どれも Prefab）
            GameObject heater = Place(BrewHeaterPrefab(), table, new Vector3(0f, top, -0.05f), Quaternion.identity);
            float heaterTop = PrefabAnchors.WorldPoint(heater.transform, "Top").y - table.position.y;
            GameObject flask = Place(BrewingFlaskPrefab(), table, new Vector3(0f, heaterTop + 0.001f, -0.05f), Quaternion.identity);
            var station = flask.GetComponent<BrewingStation>();
            // 加熱台に載せるので、フラスコの木の敷物は隠す（網の上に木があると不自然なため）
            Transform coaster = PrefabAnchors.Find(flask.transform, "Coaster");
            if (coaster != null)
            {
                coaster.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(coaster.gameObject);
            }
            Place(StirRodPrefab(), table, new Vector3(0f, heaterTop + 0.1f, -0.05f), Quaternion.identity);
            Place(BrewTrayPrefab(), table, new Vector3(0f, top, -0.385f), Quaternion.identity);

            // 机の上の小物：ぐつぐつ煮える大鍋（湯気と音つき）
            GameObject cauldron = MeshObject("Cauldron", table, new Vector3(-0.72f, top, 0.12f), GetMesh("Cauldron"), Iron());
            MeshObject("Brew", cauldron.transform, Vector3.zero, GetMesh("CauldronLiquid"),
                       Toon("CauldronBrew", new Color(0.35f, 0.85f, 0.75f), null, 0f, false, 1.6f, new Color(0.25f, 0.7f, 0.6f)));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Cylinder("Foot", cauldron.transform, new Vector3(Mathf.Cos(a) * 0.12f, 0.015f, Mathf.Sin(a) * 0.12f), new Vector3(0.04f, 0.015f, 0.04f), Iron(), false);
            }
            BuildCauldronSteam(cauldron.transform);
            var cauldronGlow = new GameObject("CauldronGlow").AddComponent<Light>();
            cauldronGlow.transform.SetParent(cauldron.transform, false);
            cauldronGlow.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            cauldronGlow.type = LightType.Point;
            cauldronGlow.color = new Color(0.45f, 1f, 0.85f);
            cauldronGlow.intensity = 0.5f;
            cauldronGlow.range = 1.3f;
            cauldronGlow.shadows = LightShadows.None;
            cauldronGlow.gameObject.AddComponent<LightFlicker>().Configure(0.2f, 1.2f, null);
            cauldronGlow.gameObject.AddComponent<QualityFeature>().Configure(GameSettings.QualityStandard, false);

            var bubbleSound = cauldron.AddComponent<AudioSource>();
            bubbleSound.clip = sounds.ambientCauldron;
            bubbleSound.loop = true;
            bubbleSound.playOnAwake = false;
            bubbleSound.spatialBlend = 1f;
            bubbleSound.minDistance = 0.6f;
            bubbleSound.maxDistance = 5f;
            bubbleSound.rolloffMode = AudioRolloffMode.Linear;
            bubbleSound.dopplerLevel = 0f;
            cauldron.AddComponent<AmbientSoundEmitter>().Configure(0.55f);

            Material stoneWhite = Toon("Porcelain", new Color(0.92f, 0.9f, 0.86f), null, 0f, false);
            GameObject mortar = MeshObject("Mortar", table, new Vector3(0.62f, top, -0.2f), GetMesh("Mortar"), stoneWhite);
            GameObject pestle = Cylinder("Pestle", mortar.transform, new Vector3(0.02f, 0.09f, 0f), new Vector3(0.025f, 0.07f, 0.025f), stoneWhite, false);
            pestle.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);

            // 開いた本（レシピ本）。調べると本が開く
            Transform book = new GameObject("RecipeBook").transform;
            book.SetParent(table, false);
            book.localPosition = new Vector3(0.5f, top, 0.22f); // 加熱台の網にかからないよう、少し右奥
            book.localRotation = Quaternion.Euler(0f, -12f, 0f);
            Box("Cover", book, new Vector3(0f, 0.008f, 0f), new Vector3(0.46f, 0.016f, 0.32f), Toon("BookCover", new Color(0.55f, 0.18f, 0.15f)), false);
            Box("Page_L", book, new Vector3(-0.11f, 0.026f, 0f), new Vector3(0.21f, 0.02f, 0.29f), Paper(), false).transform.localRotation = Quaternion.Euler(0f, 0f, -4f);
            Box("Page_R", book, new Vector3(0.11f, 0.026f, 0f), new Vector3(0.21f, 0.02f, 0.29f), Paper(), false).transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var bookCollider = book.gameObject.AddComponent<BoxCollider>();
            bookCollider.center = new Vector3(0f, 0.03f, 0f);
            bookCollider.size = new Vector3(0.48f, 0.07f, 0.34f);
            book.gameObject.AddComponent<RecipeBookStand>();

            // ろうそく（炎がゆらぐ）
            Transform candle = new GameObject("Candle").transform;
            candle.SetParent(table, false);
            candle.localPosition = new Vector3(0.95f, top, 0.25f);
            Cylinder("Wax", candle, new Vector3(0f, 0.06f, 0f), new Vector3(0.045f, 0.06f, 0.045f), Toon("Wax", new Color(0.96f, 0.92f, 0.8f)), false);
            GameObject candleFlame = Sphere("Flame", candle, new Vector3(0f, 0.14f, 0f), new Vector3(0.02f, 0.035f, 0.02f),
                   Toon("CandleFlame", new Color(1f, 0.85f, 0.4f), null, 0f, true, 0f, new Color(1.8f, 1.1f, 0.35f)));
            var candleLight = new GameObject("CandleLight").AddComponent<Light>();
            candleLight.transform.SetParent(candle, false);
            candleLight.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            candleLight.type = LightType.Point;
            candleLight.color = new Color(1f, 0.7f, 0.35f);
            candleLight.intensity = 0.7f;
            candleLight.range = 1.6f;
            candleLight.gameObject.AddComponent<LightFlicker>().Configure(0.22f, 3f, candleFlame.transform);
            candleLight.gameObject.AddComponent<QualityFeature>().Configure(GameSettings.QualityStandard, false);

            // 飾りの小瓶（ポーションの瓶の Prefab を色違いで置く）
            Color[] decoColors = { new Color(0.9f, 0.2f, 0.22f), new Color(0.3f, 0.8f, 0.35f), new Color(0.65f, 0.3f, 0.85f), new Color(0.25f, 0.5f, 0.95f) };
            GameObject bottle = PotionBottlePrefab();
            for (int i = 0; i < decoColors.Length; i++)
            {
                GameObject deco = Place(bottle, table, new Vector3(0.75f + (i % 2) * 0.16f, top, -0.3f + (i / 2) * 0.18f + (i % 2) * 0.05f),
                                        Quaternion.Euler(0f, i * 40f, 0f));
                deco.name = "DecoPotion";
                deco.transform.localScale = Vector3.one * (1.2f - (i % 2) * 0.15f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(deco.transform);
                var content = deco.GetComponent<ContentVisual>();
                if (content != null)
                {
                    content.SetDefaultColor(decoColors[i]);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(content);
                }
            }

            return station;
        }

        private static void BuildCauldronSteam(Transform cauldron)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(cauldron, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.85f, 1f, 0.95f, 1f);
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 4f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.1f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.6f)));
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.32f, 0.25f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = softParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<QualityFeature>().Configure(GameSettings.QualityStandard, false);
        }
        // ------------------------------------------------------------------
        // プレイヤー・カメラ
        // ------------------------------------------------------------------

        private static PlayerInteractor BuildPlayer(AlchemyManager alchemy, PotionInventory inventory, IngredientInventory ingredients,
                                                    Shader postShader, out Camera camera, out Transform start)
        {
            var player = new GameObject("Player");
            player.layer = 2; // Ignore Raycast（視線の判定に自分が当たらないように）
            player.transform.position = new Vector3(0f, 0.02f, -0.7f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.85f, 0f);

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.55f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(pivot, false);
            camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.07f, 0.05f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.enabled = false; // 最初はタイトル用カメラが映す
            var listener = cameraGo.AddComponent<AudioListener>();
            listener.enabled = false;
            cameraGo.AddComponent<AtelierPostFX>().Configure(postShader);

            // 手に持っている物（手は表示せず、物のモデルだけを画面右下に出す）
            var heldAnchor = new GameObject("HeldItem");
            heldAnchor.transform.SetParent(cameraGo.transform, false);
            var heldView = heldAnchor.AddComponent<HeldItemView>();

            var controller = player.AddComponent<FirstPersonController>();
            controller.Configure(pivot);
            heldView.Configure(controller);

            var interactor = player.AddComponent<PlayerInteractor>();
            interactor.Configure(camera, heldView, alchemy, inventory, ingredients);

            start = new GameObject("PlayerStart").transform;
            start.position = player.transform.position;
            start.rotation = Quaternion.identity;
            return interactor;
        }

        private static Camera BuildTitleCamera(Shader postShader)
        {
            // タイトル画面でゆっくり見回す視点
            Transform viewRoot = new GameObject("TitleViewpoints").transform;
            Transform[] viewpoints =
            {
                Viewpoint(viewRoot, "View_Table", new Vector3(1.55f, 1.7f, -1.25f), new Vector3(-0.35f, 1.0f, 1.9f)),
                Viewpoint(viewRoot, "View_Flask", new Vector3(-0.7f, 1.38f, 0.55f), new Vector3(0.25f, 1.02f, 1.7f)),
                Viewpoint(viewRoot, "View_Shelf", new Vector3(0.5f, 1.6f, -0.2f), new Vector3(-2.7f, 1.2f, 0.3f)),
                Viewpoint(viewRoot, "View_Door", new Vector3(-1.0f, 1.62f, -0.6f), new Vector3(1.85f, 1.15f, -3.2f)),
            };

            var go = new GameObject("TitleCamera");
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.07f, 0.05f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            go.transform.SetPositionAndRotation(viewpoints[0].position, viewpoints[0].rotation);
            go.AddComponent<AudioListener>();
            go.AddComponent<TitleCameraRig>().Configure(viewpoints);
            go.AddComponent<AtelierPostFX>().Configure(postShader);
            return camera;
        }

        private static Transform Viewpoint(Transform parent, string name, Vector3 position, Vector3 lookAt)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = position;
            t.rotation = Quaternion.LookRotation(lookAt - position);
            return t;
        }

        // ------------------------------------------------------------------
        // 光の筋のメッシュとテクスチャ
        // ------------------------------------------------------------------

        /// <summary>窓の四角形を光の向きに押し出した筒（4つの側面）。u=横方向、v=光の進む方向</summary>
        private static Mesh LightShaftMesh(Vector3 direction, float width, float height, float length)
        {
            Vector3 d = direction.normalized * length;
            float hz = width * 0.5f;
            float hy = height * 0.5f;
            Vector3[] corners =
            {
                new Vector3(0f, -hy, -hz), new Vector3(0f, -hy, hz), new Vector3(0f, hy, hz), new Vector3(0f, hy, -hz),
            };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % 4];
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(a + d);
                vertices.Add(b + d);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "LightShaft" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Texture2D LightShaftTexture()
        {
            const int w = 64;
            const int h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LightShaft" };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                float along = Mathf.Pow(1f - v, 1.4f) * Mathf.SmoothStep(0f, 1f, v / 0.06f);
                for (int x = 0; x < w; x++)
                {
                    float u = x / (w - 1f);
                    float across = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 1.5f);
                    float streak = 0.8f + 0.2f * Mathf.Sin(u * 23f + 1.3f) * Mathf.Sin(u * 9f);
                    pixels[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(across * along * streak) * 0.6f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Gradient FadeInOut(Color color)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            return g;
        }
        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
