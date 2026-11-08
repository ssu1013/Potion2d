using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// 仮の見た目（マテリアル・メッシュ・テクスチャ・Prefab）を Assets/Art に用意する。
    /// すべて「なければ作る」：同じ場所にファイルがあれば、中身には一切触らずにそれを使う。
    /// なので、自作のモデルやマテリアルに差し替えたあとで「アトリエを整える」を実行しても、差し替えた物は上書きされない。
    /// （作り直したいときは、そのファイルを消してから実行する）
    /// </summary>
    public static class AtelierArt
    {
        public const string ArtRoot = "Assets/Art";
        public const string MaterialDir = ArtRoot + "/Materials";
        public const string MeshDir = ArtRoot + "/Meshes";
        public const string TextureDir = ArtRoot + "/Textures";
        public const string ModelDir = ArtRoot + "/Models";
        public const string PrefabDir = ArtRoot + "/Prefabs";
        public const string IngredientPrefabDir = PrefabDir + "/Ingredients";
        public const string PotionPrefabDir = PrefabDir + "/Potions";
        public const string AtelierPrefabDir = PrefabDir + "/Atelier";
        public const string CatalogPath = ArtRoot + "/ArtCatalog.asset";
        public const string SkinPath = ArtRoot + "/UI/UISkin.asset";
        public const string BalancePath = "Assets/GameData/GameBalance.asset";
        public const string SoundPath = "Assets/Audio/SoundLibrary.asset";
        private const string AudioRoot = "Assets/Audio";

        /// <summary>これより小さい物は影を落とさない（m）</summary>
        public const float SmallObjectSize = 0.2f;

        /// <summary>今回あらたに作ったファイル</summary>
        public static readonly List<string> Created = new List<string>();

        private static readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();
        private static Shader toonShader;
        private static Shader glassShader;
        private static Shader liquidShader;
        private static Shader outlineShader;
        private static Shader maskShader;
        private static Scene workScene;
        private static bool workSceneOpen;

        // ------------------------------------------------------------------
        // はじめと終わり
        // ------------------------------------------------------------------

        public static bool Begin(out string error)
        {
            Created.Clear();
            cache.Clear();
            toonShader = Shader.Find("PotionFrontier/ToonOutline");
            glassShader = Shader.Find("PotionFrontier/ToonGlass");
            liquidShader = Shader.Find("PotionFrontier/ToonLiquid");
            outlineShader = Shader.Find("PotionFrontier/OutlineOnly");
            maskShader = Shader.Find("PotionFrontier/OutlineMask");
            if (toonShader == null || glassShader == null || liquidShader == null || outlineShader == null || maskShader == null)
            {
                error = "シェーダー（Assets/Shaders の ToonOutline / ToonGlass / ToonLiquid / OutlineOnly / OutlineMask）が見つかりません。";
                return false;
            }
            foreach (string dir in new[] { MaterialDir, MeshDir, TextureDir, ModelDir, IngredientPrefabDir, PotionPrefabDir, AtelierPrefabDir, ArtRoot + "/UI" })
            {
                IngredientCatalog.EnsureFolderPath(dir);
            }
            error = null;
            return true;
        }

        public static void End()
        {
            if (workSceneOpen)
            {
                EditorSceneManager.ClosePreviewScene(workScene);
                workSceneOpen = false;
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------
        // なければ作る
        // ------------------------------------------------------------------

        private static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            if (cache.TryGetValue(path, out Object cached) && cached != null) return (T)cached;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                cache[path] = existing;
                return existing;
            }
            T asset = create();
            AssetDatabase.CreateAsset(asset, path);
            Created.Add(path);
            cache[path] = asset;
            return asset;
        }

        /// <summary>Prefab（なければ build で組み立てて保存する）</summary>
        public static GameObject Prefab(string path, Func<GameObject> build)
        {
            if (cache.TryGetValue(path, out Object cached) && cached != null) return (GameObject)cached;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                cache[path] = existing;
                return existing;
            }
            GameObject temp = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            Created.Add(path);
            cache[path] = prefab;
            return prefab;
        }

        /// <summary>Prefab を組み立てるための入れ物（シーンを汚さないよう、見えない作業用のシーンに作る）</summary>
        public static GameObject NewRoot(string name)
        {
            if (!workSceneOpen)
            {
                workScene = EditorSceneManager.NewPreviewScene();
                workSceneOpen = true;
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, workScene);
            return go;
        }

        public static Texture2D TextureAsset(string name, Func<Texture2D> create)
        {
            return LoadOrCreate(TextureDir + "/" + name + ".asset", create);
        }

        public static Mesh Lathe(string name, IList<Vector2> profile, int segments)
        {
            return LoadOrCreate(MeshDir + "/" + name + ".asset", () => LatheMesh.Create(profile, segments, name));
        }

        public static Mesh MeshAsset(string name, Func<Mesh> create)
        {
            return LoadOrCreate(MeshDir + "/" + name + ".asset", create);
        }

        public static Material MaterialAsset(string name, Func<Material> create)
        {
            return LoadOrCreate(MaterialDir + "/" + name + ".mat", create);
        }

        // ------------------------------------------------------------------
        // マテリアル
        // ------------------------------------------------------------------

        /// <summary>トゥーン用マテリアル（同じ名前は使い回す）</summary>
        public static Material Toon(string name, Color color, Texture texture = null, float worldUV = 0f,
                                    bool box = true, float outline = 2.4f, Color? emission = null, Color? rim = null)
        {
            return MaterialAsset(name, () =>
            {
                var m = new Material(toonShader) { name = name, enableInstancing = true };
                m.SetColor("_Color", color);
                if (texture != null) m.SetTexture("_MainTex", texture);
                m.SetFloat("_TriplanarScale", worldUV);
                m.SetFloat("_OutlineUsePosition", box ? 1f : 0f);
                m.SetFloat("_OutlineWidth", outline);
                m.SetColor("_EmissionColor", emission ?? Color.black);
                if (rim.HasValue) m.SetColor("_RimColor", rim.Value);
                return m;
            });
        }

        public static Material Glass()
        {
            return MaterialAsset("Glass", () => new Material(glassShader) { name = "Glass", enableInstancing = true });
        }

        /// <summary>容器の中身（色と量は ContentVisual・LiquidFill が実行時に変える）</summary>
        public static Material LiquidMaterial()
        {
            return MaterialAsset("Liquid", () =>
            {
                var m = new Material(liquidShader) { name = "Liquid", enableInstancing = true };
                m.SetColor("_Color", new Color(0.75f, 0.88f, 1f));
                return m;
            });
        }

        public static Material Highlight(bool box)
        {
            string name = box ? "HighlightBox" : "Highlight";
            return MaterialAsset(name, () =>
            {
                var m = new Material(outlineShader) { name = name, enableInstancing = true };
                m.SetColor("_OutlineColor", new Color(1f, 0.82f, 0.3f, 1f));
                m.SetFloat("_OutlineWidth", 4.5f);
                m.SetFloat("_OutlineUsePosition", box ? 1f : 0f);
                return m;
            });
        }

        public static Material HighlightMask()
        {
            return MaterialAsset("HighlightMask", () => new Material(maskShader) { name = "HighlightMask", enableInstancing = true });
        }

        public static Material Cork() { return Toon("Cork", new Color(0.74f, 0.54f, 0.34f)); }
        public static Material Paper() { return Toon("Paper", new Color(0.95f, 0.88f, 0.72f), null, 0f, true, 1.6f); }
        public static Material Wood() { return Toon("Wood", Color.white, WoodTexture(), 1.2f); }
        public static Material DarkWood() { return Toon("DarkWood", Color.white, DarkWoodTexture(), 0.8f); }
        public static Material Iron() { return Toon("Iron", new Color(0.2f, 0.2f, 0.23f), null, 0f, false, 2.4f, null, new Color(0.7f, 0.7f, 0.8f, 0.35f)); }
        public static Material Brass() { return Toon("Brass", new Color(0.86f, 0.66f, 0.3f), null, 0f, false, 2f, null, new Color(1f, 0.9f, 0.6f, 0.5f)); }

        public static Texture2D WoodTexture() { return TextureAsset("Wood", () => ProceduralTextures.WoodPlanks(256, new Color(0.56f, 0.38f, 0.23f), 4, 23)); }
        public static Texture2D DarkWoodTexture() { return TextureAsset("DarkWood", () => ProceduralTextures.WoodPlanks(256, new Color(0.36f, 0.24f, 0.15f), 5, 37)); }

        public static Material Sparkle()
        {
            return MaterialAsset("Sparkle", () =>
            {
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = "Sparkle" };
                m.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
                return m;
            });
        }

        public static Material SoftParticle()
        {
            return MaterialAsset("SoftParticle", () =>
            {
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { name = "SoftParticle" };
                m.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
                m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                return m;
            });
        }

        /// <summary>半透明の粒のマテリアル（色は粒ごとに付ける）</summary>
        public static Material ParticleMaterial(string name, Texture2D texture)
        {
            return MaterialAsset(name, () =>
            {
                var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { name = name };
                m.mainTexture = texture;
                m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                return m;
            });
        }

        public static Material PourStreamMaterial()
        {
            return MaterialAsset("PourStream", () => new Material(Shader.Find("Sprites/Default")) { name = "PourStream" });
        }

        // ------------------------------------------------------------------
        // メッシュ（回転体の容器）
        // ------------------------------------------------------------------

        public static Mesh GetMesh(string name)
        {
            switch (name)
            {
                case "TallBottle": return Lathe(name, VesselShapes.TallBottle, 24);
                case "TallBottle_Liquid": return Lathe(name, LatheMesh.LiquidProfile(VesselShapes.TallBottle, 0.197f, 0.9f, 0.003f), 24);
                case "RoundFlask": return Lathe(name, VesselShapes.RoundFlask, 24);
                case "RoundFlask_Liquid": return Lathe(name, LatheMesh.LiquidProfile(VesselShapes.RoundFlask, 0.122f, 0.9f, 0.003f), 24);
                case "Jar": return Lathe(name, VesselShapes.Jar, 24);
                case "Jar_Content": return Lathe(name, LatheMesh.LiquidProfile(VesselShapes.Jar, 0.142f, 0.92f, 0.003f), 24);
                case "Erlenmeyer": return Lathe(name, VesselShapes.Erlenmeyer, 32);
                case "Erlenmeyer_Liquid": return Lathe(name, LatheMesh.LiquidProfile(VesselShapes.Erlenmeyer, 0.24f, 0.93f, 0.003f), 32);
                case "Cauldron": return Lathe(name, VesselShapes.Cauldron, 32);
                case "CauldronLiquid": return Lathe(name, LatheMesh.LiquidProfile(VesselShapes.Cauldron, 0.2f, 0.97f, 0f), 32);
                case "Barrel": return Lathe(name, VesselShapes.Barrel, 24);
                case "Mortar": return Lathe(name, VesselShapes.Mortar, 24);
                case "Sack": return Lathe(name, VesselShapes.Sack, 16);
                case "CrystalShard": return MeshAsset(name, CrystalShard);
                default: throw new ArgumentException("知らないメッシュ: " + name);
            }
        }

        /// <summary>六角柱の両端をとがらせた結晶（高さ1・半径0.5、面ごとに角ばった見た目）</summary>
        private static Mesh CrystalShard()
        {
            const int sides = 6;
            const float r = 0.5f;
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                ring[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int s = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                triangles.Add(s);
                triangles.Add(s + 1);
                triangles.Add(s + 2);
            }
            var top = new Vector3(0f, 1f, 0f);
            var bottom = new Vector3(0f, 0f, 0f);
            const float low = 0.12f;
            const float high = 0.72f;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = ring[i];
                Vector3 b = ring[(i + 1) % sides];
                Vector3 aLow = a + Vector3.up * low, bLow = b + Vector3.up * low;
                Vector3 aHigh = a + Vector3.up * high, bHigh = b + Vector3.up * high;
                Tri(aLow, bHigh, bLow);
                Tri(aLow, aHigh, bHigh);
                Tri(aHigh, top, bHigh);
                Tri(aLow, bLow, bottom);
            }
            var mesh = new Mesh { name = "CrystalShard" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        // 標準の見た目（Prefab）と、その一覧（ArtCatalog）
        // ------------------------------------------------------------------

        /// <summary>見た目の一覧・UIスキン・標準の Prefab を用意する（空欄だけ埋める）</summary>
        public static ArtCatalog EnsureCatalog()
        {
            var catalog = LoadOrCreate(CatalogPath, () => ScriptableObject.CreateInstance<ArtCatalog>());
            bool changed = false;
            void Fill(ref GameObject field, Func<GameObject> make)
            {
                if (field != null) return;
                field = make();
                changed = true;
            }
            Fill(ref catalog.potionBottle, PotionBottlePrefab);
            Fill(ref catalog.tallBottle, () => Prefab(IngredientPrefabDir + "/Container_TallBottle.prefab", BuildTallBottle));
            Fill(ref catalog.jar, () => Prefab(IngredientPrefabDir + "/Container_Jar.prefab", BuildJar));
            Fill(ref catalog.roundFlask, () => Prefab(IngredientPrefabDir + "/Container_RoundFlask.prefab", BuildRoundFlask));
            Fill(ref catalog.pouch, () => Prefab(IngredientPrefabDir + "/Container_Pouch.prefab", BuildPouch));
            Fill(ref catalog.crystal, () => Prefab(IngredientPrefabDir + "/Container_Crystal.prefab", BuildCrystal));
            Fill(ref catalog.dropPiece, () => Prefab(IngredientPrefabDir + "/DropPiece.prefab", BuildDropPiece));
            if (catalog.highlight == null) { catalog.highlight = Highlight(false); changed = true; }
            if (catalog.highlightBox == null) { catalog.highlightBox = Highlight(true); changed = true; }
            if (catalog.highlightMask == null) { catalog.highlightMask = HighlightMask(); changed = true; }
            if (catalog.steam == null) { catalog.steam = ParticleMaterial("Steam", TextureAsset("SteamPuffs", () => ProceduralTextures.SteamPuffs(64, 11))); changed = true; }
            if (catalog.bubble == null) { catalog.bubble = ParticleMaterial("Bubble", TextureAsset("Bubble", () => ProceduralTextures.Bubble(64))); changed = true; }
            if (catalog.droplet == null) { catalog.droplet = ParticleMaterial("Droplet", TextureAsset("Droplet", () => ProceduralTextures.Droplet(32))); changed = true; }
            if (catalog.uiSkin == null)
            {
                catalog.uiSkin = LoadOrCreate(SkinPath, () => ScriptableObject.CreateInstance<UISkin>());
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(catalog);
            return catalog;
        }

        public static GameObject PotionBottlePrefab()
        {
            return Prefab(PotionPrefabDir + "/PotionBottle.prefab", BuildPotionBottle);
        }

        /// <summary>
        /// 遊び始めの所持金と素材（なければ作る）。古い版の設定は、はじめから持っている素材と取引のルールだけ新しくする。
        /// はじめは基本の素材だけ取引済み（使い放題）。霧隠れの花・ナイトシェード・陽光の花びら・疾風の羽根は取引先帳で開拓する。
        /// 希少素材（レア度3）は少しだけ持たせる（使った分だけ減り、取引先帳で取り寄せる）。
        /// </summary>
        public static GameBalance EnsureBalance(AlchemySampleDataGenerator.SampleData data)
        {
            GameBalance balance = LoadOrCreate(BalancePath, () =>
            {
                var created = ScriptableObject.CreateInstance<GameBalance>();
                created.startingGold = 50;
                return created;
            });
            if (balance.Version >= GameBalance.CurrentVersion) return balance;

            balance.startingIngredients.Clear();
            void Give(IngredientData d, float amount)
            {
                if (d != null) balance.startingIngredients.Add(new IngredientAmount(d, amount));
            }
            // 使い放題（量は 1 でよい。手に入れた時点で取引済みになる）
            Give(data.water, 1f);
            Give(data.oil, 1f);
            Give(data.honey, 1f);
            Give(data.herb, 1f);
            Give(data.fireGrass, 1f);
            Give(data.frostMint, 1f);
            Give(data.sproutSeed, 1f);
            // 希少素材（使った分だけ減る）
            Give(data.spiritWater, 3f);
            Give(data.amplifyCrystal, 2f);
            Give(data.sunStone, 2f);
            Give(data.moonDust, 2f);
            balance.infiniteRarityMax = 2;
            balance.unlockPriceMultiplier = 8;
            balance.EditorSetVersion(GameBalance.CurrentVersion);
            EditorUtility.SetDirty(balance);
            return balance;
        }

        /// <summary>効果音・BGMの一覧（なければ作る。空欄の音だけ Assets/Audio から埋める）</summary>
        public static SoundLibrary EnsureSounds()
        {
            var lib = LoadOrCreate(SoundPath, () => ScriptableObject.CreateInstance<SoundLibrary>());
            bool changed = false;
            void Fill(ref AudioClip field, string relative)
            {
                if (field != null) return;
                field = Clip(relative);
                if (field != null) changed = true;
            }
            Fill(ref lib.bgmTitle, "BGM/bgm_title");
            Fill(ref lib.bgmAtelier, "BGM/bgm_atelier");
            Fill(ref lib.ambientRoom, "Ambient/amb_room");
            Fill(ref lib.ambientCauldron, "Ambient/amb_cauldron");
            Fill(ref lib.uiClick, "SE/se_ui_click");
            Fill(ref lib.uiHover, "SE/se_ui_hover");
            Fill(ref lib.uiOpen, "SE/se_ui_open");
            Fill(ref lib.uiClose, "SE/se_ui_close");
            Fill(ref lib.page, "SE/se_page");
            Fill(ref lib.startGame, "SE/se_start_game");
            Fill(ref lib.pickup, "SE/se_pickup");
            Fill(ref lib.putBack, "SE/se_putback");
            Fill(ref lib.pourLoop, "SE/se_pour_loop");
            Fill(ref lib.plop, "SE/se_plop");
            Fill(ref lib.brewSuccess, "SE/se_brew_success");
            Fill(ref lib.brewSpecial, "SE/se_brew_special");
            Fill(ref lib.brewFail, "SE/se_brew_fail");
            Fill(ref lib.discard, "SE/se_discard");
            Fill(ref lib.warning, "SE/se_warning");
            Fill(ref lib.shelfPlace, "SE/se_shelf_place");
            Fill(ref lib.doorBell, "SE/se_door_bell");
            Fill(ref lib.voiceBlip, "SE/se_voice_blip");
            Fill(ref lib.coins, "SE/se_coins");
            Fill(ref lib.questAccept, "SE/se_quest_accept");
            Fill(ref lib.questComplete, "SE/se_quest_complete");
            Fill(ref lib.pourThinLoop, "SE/se_pour_thin_loop");
            Fill(ref lib.stirLoop, "SE/se_stir_loop");
            Fill(ref lib.simmerLoop, "SE/se_simmer_loop");
            Fill(ref lib.boilLoop, "SE/se_boil_loop");
            Fill(ref lib.scorchLoop, "SE/se_scorch_loop");
            Fill(ref lib.burnerLoop, "SE/se_burner_loop");
            Fill(ref lib.ignite, "SE/se_ignite");
            Fill(ref lib.knobTick, "SE/se_knob_tick");
            Fill(ref lib.corkPop, "SE/se_cork_pop");
            void FillArray(ref AudioClip[] field, params string[] relative)
            {
                if (field != null && field.Length > 0) return;
                var list = new List<AudioClip>();
                foreach (string r in relative)
                {
                    AudioClip c = Clip(r);
                    if (c != null) list.Add(c);
                }
                if (list.Count == 0) return;
                field = list.ToArray();
                changed = true;
            }
            FillArray(ref lib.dropSolid, "SE/se_drop_solid_1", "SE/se_drop_solid_2", "SE/se_drop_solid_3");
            FillArray(ref lib.dropPowder, "SE/se_drop_powder_1", "SE/se_drop_powder_2");
            FillArray(ref lib.stirTink, "SE/se_stir_tink_1", "SE/se_stir_tink_2", "SE/se_stir_tink_3");
            if (lib.footsteps == null || lib.footsteps.Length == 0)
            {
                lib.footsteps = new[] { Clip("SE/se_step_1"), Clip("SE/se_step_2"), Clip("SE/se_step_3"), Clip("SE/se_step_4") };
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(lib);
            return lib;
        }

        private static AudioClip Clip(string relative)
        {
            string path = AudioRoot + "/" + relative + ".wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning("[Potion Frontier] 音が見つかりません: " + path);
            return clip;
        }

        // ------------------------------------------------------------------
        // 素材の容器・欠片・ポーションの瓶
        // 形：ルート（ContentVisual）／Model（見た目）／Anchors（Grip＝持つ位置・Mouth＝口）
        // 前は +Z、足元が原点。1 = 1m。
        // ------------------------------------------------------------------

        private static GameObject BuildTallBottle()
        {
            const float s = 1.25f;
            GameObject root = NewRoot("Container_TallBottle");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            model.localScale = Vector3.one * s;
            LiquidFill fill = LiquidPart(model, GetMesh("TallBottle_Liquid"), 0.003f, 0.197f);
            MeshObject("Glass", model, Vector3.zero, GetMesh("TallBottle"), Glass());
            Cylinder("Cork", model, new Vector3(0f, VesselShapes.TallBottleTop + 0.008f, 0f), new Vector3(0.04f, 0.02f, 0.04f), Cork(), false);
            Box("Label", model, new Vector3(0f, 0.09f, 0.061f), new Vector3(0.07f, 0.06f, 0.006f), Paper(), false);
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.1f * s, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, (VesselShapes.TallBottleTop + 0.01f) * s, 0f));
            root.AddComponent<ContentVisual>().Configure(new Renderer[0], new[] { fill }, new GameObject[0], 0.2f);
            return root;
        }

        private static GameObject BuildJar()
        {
            GameObject root = NewRoot("Container_Jar");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            LiquidFill fill = LiquidPart(model, GetMesh("Jar_Content"), 0.003f, 0.142f);
            MeshObject("Glass", model, Vector3.zero, GetMesh("Jar"), Glass());
            Cylinder("Lid", model, new Vector3(0f, VesselShapes.JarTop + 0.012f, 0f), new Vector3(0.14f, 0.014f, 0.14f), Cork(), false);
            Box("Label", model, new Vector3(0f, 0.07f, 0.079f), new Vector3(0.08f, 0.05f, 0.006f), Paper(), false);
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.08f, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, VesselShapes.JarTop + 0.02f, 0f));
            root.AddComponent<ContentVisual>().Configure(new Renderer[0], new[] { fill }, new GameObject[0], 0.15f);
            return root;
        }

        private static GameObject BuildRoundFlask()
        {
            const float s = 1.4f;
            GameObject root = NewRoot("Container_RoundFlask");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            model.localScale = Vector3.one * s;
            LiquidFill fill = LiquidPart(model, GetMesh("RoundFlask_Liquid"), 0.003f, 0.122f);
            MeshObject("Glass", model, Vector3.zero, GetMesh("RoundFlask"), Glass());
            Cylinder("Cork", model, new Vector3(0f, VesselShapes.RoundFlaskTop + 0.008f, 0f), new Vector3(0.038f, 0.018f, 0.038f), Cork(), false);
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.07f * s, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, (VesselShapes.RoundFlaskTop + 0.01f) * s, 0f));
            root.AddComponent<ContentVisual>().Configure(new Renderer[0], new[] { fill }, new GameObject[0], 0.25f);
            return root;
        }

        private static GameObject BuildPouch()
        {
            const float s = 0.38f;
            GameObject root = NewRoot("Container_Pouch");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            Material cloth = Toon("PouchCloth", new Color(0.8f, 0.68f, 0.48f), null, 0f, false, 2f);
            Material tint = Toon("ContentTint", Color.white, null, 0f, false, 1.6f);
            GameObject sack = MeshObject("Sack", model, Vector3.zero, GetMesh("Sack"), cloth);
            sack.transform.localScale = Vector3.one * s;
            GameObject content = Sphere("Content", model, new Vector3(0f, 0.43f * s, 0f), new Vector3(0.05f, 0.026f, 0.05f), tint);
            Cylinder("Tie", model, new Vector3(0f, 0.385f * s, 0f), new Vector3(0.042f, 0.006f, 0.042f), Cork(), false);
            GameObject tag = Box("Tag", model, new Vector3(0f, 0.075f, 0.082f), new Vector3(0.045f, 0.032f, 0.005f), tint, false);
            tag.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.07f, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, 0.45f * s, 0f));
            root.AddComponent<ContentVisual>().Configure(
                new[] { content.GetComponent<Renderer>(), tag.GetComponent<Renderer>() }, new LiquidFill[0], new[] { content }, 0.1f);
            return root;
        }

        private static GameObject BuildCrystal()
        {
            GameObject root = NewRoot("Container_Crystal");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            Material stone = Toon("CrystalBase", new Color(0.42f, 0.4f, 0.44f), null, 0f, false, 2f);
            Material shardMat = Toon("CrystalShard", Color.white, null, 0f, true, 1.8f, null, new Color(1f, 1f, 1f, 0.55f));
            Cylinder("Base", model, new Vector3(0f, 0.012f, 0f), new Vector3(0.12f, 0.012f, 0.12f), stone, false);
            Mesh shardMesh = GetMesh("CrystalShard");
            var shards = new List<Renderer>();
            var hide = new List<GameObject>();
            (Vector3 pos, Vector3 euler, Vector3 scale)[] layout =
            {
                (new Vector3(0f, 0.018f, 0f), new Vector3(0f, 10f, 0f), new Vector3(0.055f, 0.14f, 0.055f)),
                (new Vector3(0.032f, 0.018f, 0.012f), new Vector3(0f, 0f, -24f), new Vector3(0.036f, 0.095f, 0.036f)),
                (new Vector3(-0.03f, 0.018f, -0.012f), new Vector3(10f, 30f, 22f), new Vector3(0.032f, 0.08f, 0.032f)),
            };
            foreach (var l in layout)
            {
                GameObject shard = MeshObject("Shard", model, l.pos, shardMesh, shardMat);
                shard.transform.localRotation = Quaternion.Euler(l.euler);
                shard.transform.localScale = l.scale;
                shards.Add(shard.GetComponent<Renderer>());
                hide.Add(shard);
            }
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.06f, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, 0.15f, 0f));
            root.AddComponent<ContentVisual>().Configure(shards.ToArray(), new LiquidFill[0], hide.ToArray(), 0.35f);
            return root;
        }

        private static GameObject BuildDropPiece()
        {
            GameObject root = NewRoot("DropPiece");
            GameObject piece = Sphere("Piece", root.transform, Vector3.zero, Vector3.one * 0.045f, Toon("ContentTint", Color.white, null, 0f, false, 1.6f));
            root.AddComponent<ContentVisual>().Configure(new[] { piece.GetComponent<Renderer>() }, new LiquidFill[0], new GameObject[0], 0.15f);
            return root;
        }

        private static GameObject BuildPotionBottle()
        {
            const float s = 1.15f;
            GameObject root = NewRoot("PotionBottle");
            Transform model = Child(root.transform, "Model", Vector3.zero);
            model.localScale = Vector3.one * s;
            LiquidFill fill = LiquidPart(model, GetMesh("RoundFlask_Liquid"), 0.003f, 0.122f);
            MeshObject("Glass", model, Vector3.zero, GetMesh("RoundFlask"), Glass());
            Cylinder("Cork", model, new Vector3(0f, VesselShapes.RoundFlaskTop + 0.008f, 0f), new Vector3(0.038f, 0.018f, 0.038f), Cork(), false);
            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Child(anchors, PrefabAnchors.Grip, new Vector3(0f, 0.07f * s, 0f));
            Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, (VesselShapes.RoundFlaskTop + 0.01f) * s, 0f));
            root.AddComponent<ContentVisual>().Configure(new Renderer[0], new[] { fill }, new GameObject[0], 0.25f);
            return root;
        }

        // ------------------------------------------------------------------
        // アトリエの家具（操作できる物）
        // ------------------------------------------------------------------

        /// <summary>調合台のカメラの位置（フラスコから見て）と、見る所</summary>
        public static readonly Vector3 BrewViewPosition = new Vector3(0f, 1.05f, -1.3f);
        public static readonly Vector3 BrewViewLookAt = new Vector3(0f, 0.25f, -0.22f);
        /// <summary>前の版の目印の位置（この位置のままなら、新しい位置に直す）</summary>
        private static readonly Vector3 OldBrewViewPosition = new Vector3(0f, 0.85f, -1.15f);
        /// <summary>加熱台の網の高さ（フラスコを載せる所。加熱台の「Top」の目印）</summary>
        public const float HeaterTop = 0.14f;

        public static GameObject BrewingFlaskPrefab()
        {
            const string path = AtelierPrefabDir + "/BrewingFlask.prefab";
            GameObject prefab = Prefab(path, () =>
            {
                const float s = 1.3f;
                GameObject root = NewRoot("BrewingFlask");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                model.localScale = Vector3.one * s;
                // 敷物の上面はフラスコの底（y=0）より下にする（液体の底に重なると、中から敷物が透けて見える）。
                // 加熱台に載せるときは、シーンの側で敷物を隠す
                Cylinder("Coaster", model, new Vector3(0f, -0.0075f, 0f), new Vector3(0.36f, 0.006f, 0.36f), DarkWood(), false);
                LiquidFill fill = LiquidPart(model, GetMesh("Erlenmeyer_Liquid"), 0.003f, VesselShapes.ErlenmeyerMaxLiquidHeight);
                fill.GetComponent<Renderer>().receiveShadows = true;
                MeshObject("Glass", model, Vector3.zero, GetMesh("Erlenmeyer"), Glass());
                Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
                Child(anchors, PrefabAnchors.Mouth, new Vector3(0f, VesselShapes.ErlenmeyerTop * s, 0f));
                AddBrewView(anchors);

                var station = root.AddComponent<BrewingStation>();
                station.Configure(fill, Sparkle(), SoftParticle(), PourStreamMaterial());
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.2f * s, 0f);
                collider.size = new Vector3(0.34f, 0.42f, 0.34f) * s;
                return root;
            });
            EnsureBrewView(prefab, path);
            return prefab;
        }

        /// <summary>調合台のカメラの目印（「Anchors/BrewView」。位置と向きをそのまま使う）</summary>
        private static void AddBrewView(Transform anchors)
        {
            Transform view = Child(anchors, "BrewView", BrewViewPosition);
            view.localRotation = Quaternion.LookRotation(BrewViewLookAt - BrewViewPosition, Vector3.up);
        }

        /// <summary>前に作ったフラスコの Prefab に、調合台のカメラの目印がなければ足す（自作のフラスコにも足す）</summary>
        private static void EnsureBrewView(GameObject prefab, string path)
        {
            if (prefab == null) return;
            Transform existing = PrefabAnchors.Find(prefab.transform, "BrewView");
            // 自分で動かした目印はそのまま。前の版の位置のままなら、新しい位置に直す
            if (existing != null && (existing.localPosition - OldBrewViewPosition).sqrMagnitude > 1e-6f) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform view = PrefabAnchors.Find(contents.transform, "BrewView");
                if (view != null)
                {
                    view.localPosition = BrewViewPosition;
                    view.localRotation = Quaternion.LookRotation(BrewViewLookAt - BrewViewPosition, Vector3.up);
                }
                else
                {
                    Transform anchors = PrefabAnchors.Find(contents.transform, "Anchors");
                    if (anchors == null) anchors = Child(contents.transform, "Anchors", Vector3.zero);
                    AddBrewView(anchors);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log("[Potion Frontier] フラスコの Prefab の調合台のカメラの目印（Anchors/BrewView）を新しくしました: " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// 加熱台（アルコールランプと三脚と網）。原点は机の上面、前（−Z）がプレイヤー側。
        /// 網の上面に「Top」の目印（フラスコを載せる高さ）、右の箱の前に火加減のつまみ「Knob」。
        /// 炎は「Flame」、光る部分は「Glow」で始まる名前にする（BrewHeater が火加減に合わせて動かす）。
        /// </summary>
        public static GameObject BrewHeaterPrefab()
        {
            const string path = AtelierPrefabDir + "/BrewHeater.prefab";
            // 焼ける部分（網・バーナーの口）は、冷えているときは鉄の色。火加減で赤く光るのは BrewHeater が光の色で行う。
            // 前の版の赤茶色のままなら直す（横から見ると、空のフラスコに液体が入っているように見えたため）
            Material hotMaterial = Toon("HeaterHot", new Color(0.24f, 0.22f, 0.22f), null, 0f, false, 1.2f);
            Color hotColor = hotMaterial.GetColor("_Color");
            if (hotColor.r > 0.28f && hotColor.g < 0.15f)
            {
                hotMaterial.SetColor("_Color", new Color(0.24f, 0.22f, 0.22f));
                EditorUtility.SetDirty(hotMaterial);
            }
            // 前の版（網が小さく、フラスコの上から火が見えなかった物）はここで作り直す。自作の加熱台には触らない
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (old != null && PrefabAnchors.Find(old.transform, "Gauze") != null && PrefabAnchors.Find(old.transform, "Flame_Ring_0") == null)
            {
                AssetDatabase.DeleteAsset(path);
                cache.Remove(path);
                Debug.Log("[Potion Frontier] 加熱台の Prefab を新しい形で作り直します: " + path);
            }
            return Prefab(path, () =>
            {
                GameObject root = NewRoot("BrewHeater");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Material iron = Iron();
                Material brass = Brass();

                Cylinder("Base", model, new Vector3(0f, 0.012f, 0f), new Vector3(0.34f, 0.012f, 0.34f), iron, false);
                Cylinder("Burner", model, new Vector3(0f, 0.048f, 0f), new Vector3(0.12f, 0.024f, 0.12f), brass, false);
                Material hot = hotMaterial;
                Cylinder("Glow_Burner", model, new Vector3(0f, 0.073f, 0f), new Vector3(0.09f, 0.003f, 0.09f), hot, false);

                // 炎：根元を原点にした入れ物を伸び縮みさせる（火加減で高さが変わる）
                Material flame = Toon("HeaterFlame", new Color(1f, 0.62f, 0.25f), null, 0f, false, 0f, new Color(2.2f, 1.0f, 0.3f));
                Material core = Toon("HeaterFlameCore", new Color(0.65f, 0.8f, 1f), null, 0f, false, 0f, new Color(0.9f, 1.3f, 2.2f));
                Transform flameMain = Child(model, "Flame_Main", new Vector3(0f, 0.075f, 0f));
                Sphere("Outer", flameMain, new Vector3(0f, 0.03f, 0f), new Vector3(0.034f, 0.06f, 0.034f), flame);
                Sphere("Core", flameMain, new Vector3(0f, 0.016f, 0f), new Vector3(0.018f, 0.032f, 0.018f), core);
                for (int i = 0; i < 5; i++)
                {
                    float a = (i * 72f + 18f) * Mathf.Deg2Rad;
                    Transform f = Child(model, $"Flame_{i}", new Vector3(Mathf.Cos(a) * 0.032f, 0.074f, Mathf.Sin(a) * 0.032f));
                    Sphere("Outer", f, new Vector3(0f, 0.02f, 0f), new Vector3(0.018f, 0.04f, 0.018f), flame);
                }

                // 三脚と網。網の上でフラスコのまわりにはみ出す炎と、赤く焼ける網の縁（上から見ても火加減がわかる）
                foreach (float deg in new[] { 90f, 210f, 330f })
                {
                    float a = deg * Mathf.Deg2Rad;
                    Cylinder("Leg", model, new Vector3(Mathf.Cos(a) * 0.17f, 0.08f, Mathf.Sin(a) * 0.17f), new Vector3(0.012f, 0.057f, 0.012f), iron, false);
                }
                Box("Gauze", model, new Vector3(0f, HeaterTop - 0.003f, 0f), new Vector3(0.42f, 0.006f, 0.42f), Toon("Gauze", new Color(0.3f, 0.3f, 0.32f), null, 0f, true, 1.6f), false);
                Cylinder("Glow_Gauze", model, new Vector3(0f, HeaterTop + 0.0006f, 0f), new Vector3(0.4f, 0.0012f, 0.4f), hot, false);
                for (int i = 0; i < 10; i++)
                {
                    float a = (i * 36f + 9f) * Mathf.Deg2Rad;
                    Transform f = Child(model, $"Flame_Ring_{i}", new Vector3(Mathf.Cos(a) * 0.196f, HeaterTop, Mathf.Sin(a) * 0.196f));
                    Sphere("Outer", f, new Vector3(0f, 0.026f, 0f), new Vector3(0.026f, 0.052f, 0.026f), flame);
                    Sphere("Core", f, new Vector3(0f, 0.012f, 0f), new Vector3(0.013f, 0.024f, 0.013f), core);
                }

                // 右の箱と、火加減のつまみ（前を向いている。ドラッグで回す）
                Box("Pipe", model, new Vector3(0.155f, 0.04f, -0.01f), new Vector3(0.19f, 0.016f, 0.016f), brass, false);
                Box("ControlBox", model, new Vector3(0.29f, 0.05f, -0.02f), new Vector3(0.1f, 0.1f, 0.09f), iron, false);
                Box("DialPlate", model, new Vector3(0.29f, 0.055f, -0.066f), new Vector3(0.088f, 0.088f, 0.003f), Toon("DialPlate", new Color(0.95f, 0.88f, 0.72f), null, 0f, true, 1.2f), false);
                // 目盛り（消す → 強火。つまみは時計回りに回る）
                Material tick = Toon("DialTick", new Color(0.55f, 0.2f, 0.1f), null, 0f, true, 0f);
                for (int i = 0; i < 4; i++)
                {
                    float a = (90f - i * 90f) * Mathf.Deg2Rad;
                    Box("Tick", model, new Vector3(0.29f + Mathf.Cos(a) * 0.04f, 0.055f + Mathf.Sin(a) * 0.04f, -0.068f), new Vector3(0.006f, 0.006f, 0.002f), tick, false);
                }
                Transform knob = Child(model, "Knob", new Vector3(0.29f, 0.055f, -0.068f));
                GameObject cap = Cylinder("Cap", knob, new Vector3(0f, 0f, -0.014f), new Vector3(0.068f, 0.014f, 0.068f), brass, false);
                cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Box("Notch", knob, new Vector3(0f, 0.022f, -0.029f), new Vector3(0.009f, 0.022f, 0.004f), Toon("KnobNotch", new Color(0.95f, 0.92f, 0.85f), null, 0f, true, 0f), false);
                var knobCollider = knob.gameObject.AddComponent<BoxCollider>();
                knobCollider.center = new Vector3(0f, 0f, -0.014f);
                knobCollider.size = new Vector3(0.11f, 0.11f, 0.07f);

                // 灯り（火加減で明るさが変わる。網の下からフラスコを照らす）
                var lightGo = new GameObject("HeaterGlow");
                lightGo.transform.SetParent(model, false);
                lightGo.transform.localPosition = new Vector3(0f, HeaterTop + 0.03f, -0.12f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.55f, 0.22f);
                light.range = 0.9f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;

                Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
                Child(anchors, "Top", new Vector3(0f, HeaterTop, 0f));
                root.AddComponent<BrewHeater>();
                NoShadows(root);
                return root;
            });
        }

        /// <summary>
        /// 机の上に置く薄い道具は影を落とさない（影はほとんど見えないのに、影を描く手間はろうそくの灯りの分まで増えるため）
        /// </summary>
        private static void NoShadows(GameObject root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>前の版で作った（影を落とす）加熱台・トレイの Prefab を直す。marker の部品があるときだけ（＝ここで作った物）</summary>
        private static void UpgradeNoShadows(GameObject prefab, string path, string marker)
        {
            if (prefab == null || PrefabAnchors.Find(prefab.transform, marker) == null) return;
            bool any = false;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) any |= r.shadowCastingMode != ShadowCastingMode.Off;
            if (!any) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                NoShadows(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>ガラス棒（先端が原点、+Y に伸びる。StirRod がフラスコの口を支点にして動かす）</summary>
        public static GameObject StirRodPrefab()
        {
            return Prefab(AtelierPrefabDir + "/StirRod.prefab", () =>
            {
                GameObject root = NewRoot("StirRod");
                Material rod = Toon("GlassRod", new Color(0.82f, 0.93f, 0.98f), null, 0f, false, 1.4f, null, new Color(1f, 1f, 1f, 0.6f));
                Cylinder("Rod", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.012f, 0.25f, 0.012f), rod, false).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                Sphere("Tip", root.transform, Vector3.zero, new Vector3(0.016f, 0.016f, 0.016f), rod).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                Sphere("Head", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.02f, 0.02f, 0.02f), rod).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                root.AddComponent<StirRod>();
                return root;
            });
        }

        /// <summary>調合台の手前のトレイ（木の板。Slot_00〜Slot_15 に、使える素材と自作の液体が2列に並ぶ）</summary>
        public static GameObject BrewTrayPrefab()
        {
            const string path = AtelierPrefabDir + "/BrewTray.prefab";
            GameObject prefab = Prefab(path, () =>
            {
                GameObject root = NewRoot("BrewTray");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Material wood = Wood();
                Material dark = DarkWood();
                const float width = 0.86f;
                const float depth = 0.24f;
                Box("Board", model, new Vector3(0f, 0.007f, 0f), new Vector3(width, 0.014f, depth), wood, false);
                Box("Rim_Front", model, new Vector3(0f, 0.022f, -depth * 0.5f + 0.006f), new Vector3(width, 0.022f, 0.012f), dark, false);
                Box("Rim_Back", model, new Vector3(0f, 0.022f, depth * 0.5f - 0.006f), new Vector3(width, 0.022f, 0.012f), dark, false);
                Box("Rim_L", model, new Vector3(-width * 0.5f + 0.006f, 0.022f, 0f), new Vector3(0.012f, 0.022f, depth), dark, false);
                Box("Rim_R", model, new Vector3(width * 0.5f - 0.006f, 0.022f, 0f), new Vector3(0.012f, 0.022f, depth), dark, false);
                Box("Divider", model, new Vector3(0f, 0.017f, 0f), new Vector3(width - 0.03f, 0.006f, 0.006f), dark, false);

                Transform slots = Child(root.transform, "Slots", Vector3.zero);
                int n = 0;
                foreach (float z in new[] { 0.055f, -0.055f })
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Child(slots, $"Slot_{n++:00}", new Vector3(-0.35f + i * 0.1f, 0.014f, z));
                    }
                }
                root.AddComponent<BrewTray>();
                NoShadows(root);
                return root;
            });
            UpgradeNoShadows(prefab, path, "Rim_Front");
            return prefab;
        }

        /// <summary>素材棚（前が +Z。Slot_00〜Slot_14 に素材が並ぶ）</summary>
        public static GameObject IngredientShelfPrefab(AlchemySampleDataGenerator.SampleData data)
        {
            return Prefab(AtelierPrefabDir + "/IngredientShelf.prefab", () =>
            {
                GameObject root = NewRoot("IngredientShelf");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Material wood = Wood();
                const float width = 2.5f;
                const float depth = 0.46f;
                Box("Back", model, new Vector3(0f, 1.15f, -0.21f), new Vector3(width, 2.3f, 0.04f), DarkWood(), false);
                Box("Side_A", model, new Vector3(-width * 0.5f, 1.15f, 0f), new Vector3(0.05f, 2.3f, depth), wood);
                Box("Side_B", model, new Vector3(width * 0.5f, 1.15f, 0f), new Vector3(0.05f, 2.3f, depth), wood);
                foreach (float y in new[] { 0.06f, 0.5f, 1.0f, 1.5f, 2.0f })
                {
                    Box("Board", model, new Vector3(0f, y, 0f), new Vector3(width, 0.04f, depth), wood);
                }
                // 一番上の段は飾り
                Material[] decoMats = { Toon("Clay", new Color(0.72f, 0.45f, 0.3f), null, 0f, false), Toon("ClayDark", new Color(0.45f, 0.32f, 0.25f), null, 0f, false) };
                for (int i = 0; i < 5; i++)
                {
                    GameObject jar = MeshObject("DecoJar", model, new Vector3(-1.0f + i * 0.5f, 2.02f, 0f), GetMesh("Jar"), decoMats[i % 2]);
                    jar.transform.localScale = Vector3.one * (0.9f + (i % 3) * 0.15f);
                }
                // 段ごとの小さな札
                Material tag = Toon("ShelfTag", new Color(0.95f, 0.88f, 0.72f), null, 0f, true, 1.4f);
                foreach (float y in new[] { 1.47f, 0.97f, 0.47f })
                {
                    Box("Tag", model, new Vector3(0f, y, 0.235f), new Vector3(0.3f, 0.05f, 0.01f), tag, false, false);
                }

                // 素材を置く位置：目の高さ＝ベース液体、腰の高さ＝主素材、低い段＝触媒
                Transform slots = Child(root.transform, "Slots", Vector3.zero);
                var displayed = new List<IngredientData>();
                int n = 0;
                void Row(IngredientData[] list, float y, float start, float step)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        Child(slots, $"Slot_{n++:00}", new Vector3(start + i * step, y, 0f));
                        displayed.Add(list[i]);
                    }
                }
                Row(data.BaseLiquids, 1.52f, -0.9f, 0.6f);
                Row(data.MainMaterials, 1.02f, -1.05f, 0.3f);
                Row(data.Catalysts, 0.52f, -0.7f, 0.7f);
                root.AddComponent<IngredientShelf>().Configure(null, displayed);
                return root;
            });
        }

        /// <summary>ポーション棚（前が +Z。Slot_00〜Slot_17 に完成ポーションが並ぶ）</summary>
        public static GameObject PotionShelfPrefab()
        {
            return Prefab(AtelierPrefabDir + "/PotionShelf.prefab", () =>
            {
                GameObject root = NewRoot("PotionShelf");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Material wood = Wood();
                const float width = 1.5f;
                const float depth = 0.36f;
                // 背板にも当たり判定をつける（瓶のない所を見ても棚として調べられる）
                Box("Back", model, new Vector3(0f, 1.05f, -0.16f), new Vector3(width, 2.1f, 0.03f), DarkWood(), true);
                Box("Side_L", model, new Vector3(-width * 0.5f, 1.05f, 0f), new Vector3(0.05f, 2.1f, depth), wood);
                Box("Side_R", model, new Vector3(width * 0.5f, 1.05f, 0f), new Vector3(0.05f, 2.1f, depth), wood);
                Box("Crown", model, new Vector3(0f, 2.12f, 0.02f), new Vector3(width + 0.12f, 0.08f, depth + 0.06f), DarkWood());
                foreach (float y in new[] { 0.06f, 0.5f, 0.92f, 1.34f, 1.76f }) Box("Board", model, new Vector3(0f, y, 0f), new Vector3(width, 0.04f, depth), wood);
                Box("Sign", model, new Vector3(0f, 1.95f, 0.19f), new Vector3(0.5f, 0.14f, 0.02f), Paper(), false);

                Transform slots = Child(root.transform, "Slots", Vector3.zero);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 6; col++)
                    {
                        Child(slots, $"Slot_{row * 6 + col:00}", new Vector3(-width * 0.5f + 0.15f + col * 0.24f, 0.52f + row * 0.42f, 0.02f));
                    }
                }
                root.AddComponent<PotionShelf>();
                return root;
            });
        }

        /// <summary>素材箱（前が +Z）</summary>
        public static GameObject IngredientChestPrefab()
        {
            return Prefab(AtelierPrefabDir + "/IngredientChest.prefab", () =>
            {
                GameObject root = NewRoot("IngredientChest");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Material wood = Toon("ChestWood", Color.white, WoodTexture(), 1.4f);
                Material dark = DarkWood();
                const float w = 0.72f, h = 0.4f, d = 0.46f;
                Box("Body", model, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), wood, false);
                Box("Rim", model, new Vector3(0f, h - 0.02f, 0f), new Vector3(w + 0.02f, 0.04f, d + 0.02f), dark, false);
                // ふたは少し開けておく（中の素材がのぞく）
                Transform hinge = Child(model, "LidHinge", new Vector3(0f, h, -d * 0.5f));
                hinge.localRotation = Quaternion.Euler(-28f, 0f, 0f);
                Box("Lid", hinge, new Vector3(0f, 0.03f, d * 0.5f), new Vector3(w + 0.03f, 0.06f, d + 0.03f), wood, false);
                Box("LidBand", hinge, new Vector3(0f, 0.065f, d * 0.5f), new Vector3(0.06f, 0.012f, d + 0.04f), AtelierArt.Iron(), false);
                foreach (float x in new[] { -w * 0.33f, w * 0.33f })
                {
                    Box("Band", model, new Vector3(x, h * 0.5f, 0f), new Vector3(0.05f, h + 0.01f, d + 0.012f), Iron(), false);
                }
                Box("Latch", model, new Vector3(0f, h - 0.07f, d * 0.5f + 0.012f), new Vector3(0.07f, 0.08f, 0.02f), Brass(), false);
                // 中の素材（色とりどりの包み）
                Color[] colors = { new Color(0.5f, 0.78f, 0.36f), new Color(0.92f, 0.48f, 0.3f), new Color(0.6f, 0.5f, 0.85f), new Color(0.95f, 0.82f, 0.4f) };
                for (int i = 0; i < colors.Length; i++)
                {
                    Sphere("Bundle", model, new Vector3(-0.22f + i * 0.15f, h - 0.02f, -0.04f + (i % 2) * 0.08f), new Vector3(0.13f, 0.08f, 0.12f),
                           Toon("ChestBundle" + i, colors[i], null, 0f, false, 1.6f));
                }
                root.AddComponent<IngredientChest>();
                FitBoxCollider(root, 0.02f);
                return root;
            });
        }

        /// <summary>仕入れ帳（カウンターの台帳。前が +Z＝読む人の側）</summary>
        public static GameObject OrderBookPrefab()
        {
            return Prefab(AtelierPrefabDir + "/OrderBook.prefab", () =>
            {
                GameObject root = NewRoot("OrderBook");
                Transform model = Child(root.transform, "Model", Vector3.zero);
                Box("Cover", model, new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.02f, 0.26f), Toon("LedgerCover", new Color(0.28f, 0.36f, 0.5f)), false);
                Box("Pages", model, new Vector3(0f, 0.024f, 0f), new Vector3(0.34f, 0.012f, 0.24f), Paper(), false);
                GameObject quill = Cylinder("Quill", model, new Vector3(0.08f, 0.04f, 0.02f), new Vector3(0.008f, 0.11f, 0.008f), Toon("Feather", new Color(0.96f, 0.95f, 0.92f)), false);
                quill.transform.localRotation = Quaternion.Euler(0f, 30f, 75f);
                // 立て札（遠くからでも仕入れ帳とわかるように）
                GameObject card = Box("Card", model, new Vector3(-0.1f, 0.075f, -0.1f), new Vector3(0.14f, 0.1f, 0.01f), Toon("OrderCard", new Color(0.95f, 0.82f, 0.45f), null, 0f, true, 1.4f), false);
                card.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                Box("Coin", model, new Vector3(-0.1f, 0.08f, -0.093f), new Vector3(0.04f, 0.04f, 0.006f), Toon("Gold", new Color(0.95f, 0.72f, 0.28f), null, 0f, false, 1.6f, new Color(0.08f, 0.05f, 0.01f), new Color(1f, 0.95f, 0.7f, 0.4f)), false);
                root.AddComponent<OrderBook>();
                FitBoxCollider(root, 0.03f);
                return root;
            });
        }

        // ------------------------------------------------------------------
        // 部品づくり
        // ------------------------------------------------------------------

        public static Transform Child(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        /// <summary>容器の中身（ToonLiquid のマテリアル＋LiquidFill）。bottom/top は空と満タンの高さ</summary>
        private static LiquidFill LiquidPart(Transform parent, Mesh mesh, float bottom, float top)
        {
            GameObject go = MeshObject("Liquid", parent, Vector3.zero, mesh, LiquidMaterial());
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var fill = go.AddComponent<LiquidFill>();
            fill.Configure(bottom, top, false);
            return fill;
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
                                           Material material, bool collider, bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            // 小さな物（20cm未満）の影はほとんど見えないので落とさない（影の描画を軽くする）
            Vector3 size = go.transform.lossyScale;
            if (type == PrimitiveType.Cylinder || type == PrimitiveType.Capsule) size.y *= 2f;
            if (!castShadows || Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallObjectSize) renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material,
                                     bool collider = true, bool castShadows = true)
        {
            return Primitive(PrimitiveType.Cube, name, parent, position, scale, material, collider, castShadows);
        }

        /// <summary>円柱（scale.y は高さの半分。Unity の円柱は高さ2のため）</summary>
        public static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            return Primitive(PrimitiveType.Cylinder, name, parent, position, scale, material, collider, true);
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            return Primitive(PrimitiveType.Sphere, name, parent, position, scale, material, false, true);
        }

        public static GameObject Capsule(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            return Primitive(PrimitiveType.Capsule, name, parent, position, scale, material, collider, true);
        }

        public static GameObject MeshObject(string name, Transform parent, Vector3 position, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            Vector3 size = Vector3.Scale(mesh.bounds.size, go.transform.lossyScale);
            if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallObjectSize) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public static void FitBoxCollider(GameObject root, float padding)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            Vector3 lossy = root.transform.lossyScale;
            box.size = new Vector3(b.size.x / lossy.x, b.size.y / lossy.y, b.size.z / lossy.z) + Vector3.one * padding;
        }
    }
}
