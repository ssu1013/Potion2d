using System.Collections.Generic;
using System.IO;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 2D 調合机の部品を組み立てるメニュー（Tools > Potion Frontier 2D）と、シーン作りの共通の道具。
    /// ・仮の絵（Assets/CustomAssets/Images の下）、絵の一覧（BrewArt2D）、Prefab（Assets/Art2D/Brewing/Prefabs）は「なければ作る」。
    ///   すでにある物には触らないので、自分の絵に差し替えたあとで実行しても上書きされない。
    /// ・シーンは Atelier2D の1つだけ（1画面版。組み立ては Atelier2DBuilder）。
    /// </summary>
    public static class Brew2DBuilder
    {
        internal const string ArtRoot = "Assets/Art2D/Brewing";
        internal const string SpriteDir = CustomAssetPaths.Images; // 仮の絵は CustomAssets/Images の下（名前ごとの場所は CustomAssetPaths）
        internal const string PrefabDir = ArtRoot + "/Prefabs";
        private const string ArtSetPath = ArtRoot + "/BrewArt2D.asset";
        private const string BackupDir = "Assets/Scenes/Backup";
        private const string VolumeProfilePath = "Assets/Settings/Rendering/GlobalVolume_Profile.asset";

        /// <summary>描く順番（上ほど奥）</summary>
        public static readonly string[] SortingLayers = { "Background", "Room", "Workbench", "Flask", "Liquid", "FlaskFront", "Items", "Held", "FX" };

        /// <summary>調合机の絵・Prefab だけを、足りない物だけ作る（シーンには触らない）</summary>
        [MenuItem("Tools/Potion Frontier 2D/調合机の絵と部品を作る（足りない物だけ）", priority = 20)]
        private static void Build()
        {
            var log = new System.Text.StringBuilder("[2D 調合机] 結果\n");
            EnsureAssets(log);
            AssetDatabase.SaveAssets();
            log.AppendLine("・シーンはそのまま（組み立ては「アトリエを作る」）");
            Debug.Log(log.ToString());
        }

        /// <summary>調合画面の絵・Prefab（アトリエの組み立てからも使う）</summary>
        internal sealed class BrewAssets
        {
            public PlaceholderArt2D.Set sprites;
            public BrewArt2D art;
            public GameObject flask, heater, trayItem, tray, heldItem, stirRod, lamp, hud;
        }

        /// <summary>描く順番・仮の絵・絵の一覧・Prefab を、なければ作る</summary>
        internal static BrewAssets EnsureAssets(System.Text.StringBuilder log)
        {
            EnsureSortingLayers(log);
            PlaceholderArt2D.EnsureFolder(PrefabDir);
            var a = new BrewAssets();
            PlaceholderArt2D.Set s = PlaceholderArt2D.EnsureAll(SpriteDir);
            a.sprites = s;
            a.art = EnsureArtSet(s, log);
            a.flask = EnsurePrefab("Flask", () => BuildFlask(s), log);
            a.heater = EnsurePrefab("Heater", () => BuildHeater(s), log);
            a.trayItem = EnsurePrefab("TrayItem", BuildTrayItem, log);
            GameObject trayItem = a.trayItem;
            a.tray = EnsurePrefab("Tray", () => BuildTray(s, trayItem), log);
            a.heldItem = EnsurePrefab("HeldItem", () => BuildHeldItem(s), log);
            a.stirRod = EnsurePrefab("StirRod", () => BuildStirRod(s), log);
            a.lamp = EnsurePrefab("Lamp", () => BuildLamp(s), log);
            a.hud = EnsurePrefab("BrewHUD", BuildHud, log);
            return a;
        }

        // ------------------------------------------------------------------
        // 描く順番（Sorting Layer）
        // ------------------------------------------------------------------

        internal static void EnsureSortingLayers(System.Text.StringBuilder log)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
            if (layers == null) return;
            var names = new HashSet<string>();
            var ids = new HashSet<int>();
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty e = layers.GetArrayElementAtIndex(i);
                names.Add(e.FindPropertyRelative("name").stringValue);
                ids.Add(e.FindPropertyRelative("uniqueID").intValue);
            }
            var added = new List<string>();
            foreach (string name in SortingLayers)
            {
                if (names.Contains(name)) continue;
                // 番号は正の数にする（負の数は Unity が 0 にしてしまい、いちばん奥の Default と同じになる）
                int id = name.GetHashCode() & 0x7FFFFFFF;
                while (id == 0 || ids.Contains(id)) id = id == int.MaxValue ? 1 : id + 1;
                ids.Add(id);
                layers.InsertArrayElementAtIndex(layers.arraySize);
                SerializedProperty e = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                e.FindPropertyRelative("name").stringValue = name;
                e.FindPropertyRelative("uniqueID").intValue = id;
                SerializedProperty locked = e.FindPropertyRelative("locked");
                if (locked != null) locked.intValue = 0;
                added.Add(name);
            }
            if (added.Count > 0)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("・描く順番（Sorting Layer）を足した：" + string.Join(", ", added));
            }
        }

        // ------------------------------------------------------------------
        // 絵の一覧
        // ------------------------------------------------------------------

        private static BrewArt2D EnsureArtSet(PlaceholderArt2D.Set s, System.Text.StringBuilder log)
        {
            var artSet = AssetDatabase.LoadAssetAtPath<BrewArt2D>(ArtSetPath);
            if (artSet != null)
            {
                // あとから増えた欄（演出の絵など）が空なら、仮の絵を入れる（入れてある絵はそのまま）
                if (FillNewArt(artSet, s)) log.AppendLine($"・共通の絵の一覧に、演出の絵（湯気・泡・しぶき・渦・波紋）を足した：{ArtSetPath}");
                return artSet;
            }
            artSet = ScriptableObject.CreateInstance<BrewArt2D>();
            artSet.tallBottle.body = s.tallBottle; artSet.tallBottle.content = s.tallBottleContent;
            artSet.jar.body = s.jar; artSet.jar.content = s.jarContent;
            artSet.roundFlask.body = s.roundFlask; artSet.roundFlask.content = s.roundFlaskContent;
            artSet.pouch.body = s.pouch; artSet.pouch.content = s.pouchContent;
            artSet.crystal.body = s.crystal; artSet.crystal.content = s.crystalContent;
            artSet.potionBottle.body = s.potionBottle; artSet.potionBottle.content = s.potionBottleContent;
            artSet.dropPiece = s.dropPiece;
            FillNewArt(artSet, s);
            AssetDatabase.CreateAsset(artSet, ArtSetPath);
            log.AppendLine($"・共通の絵の一覧を作った：{ArtSetPath}");
            return artSet;
        }

        private const string ParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        /// <summary>空の欄だけ埋める。何か入れたら true</summary>
        private static bool FillNewArt(BrewArt2D artSet, PlaceholderArt2D.Set s)
        {
            bool changed = false;
            if (artSet.steam == null && s.fxSteam != null) { artSet.steam = s.fxSteam; changed = true; }
            if (artSet.bubble == null && s.fxBubble != null) { artSet.bubble = s.fxBubble; changed = true; }
            if (artSet.droplet == null && s.fxDroplet != null) { artSet.droplet = s.fxDroplet; changed = true; }
            if (artSet.swirl == null && s.fxSwirl != null) { artSet.swirl = s.fxSwirl; changed = true; }
            if (artSet.ripple == null && s.fxRipple != null) { artSet.ripple = s.fxRipple; changed = true; }
            if (artSet.particleMaterial == null)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
                if (m != null) { artSet.particleMaterial = m; changed = true; }
            }
            if (changed) EditorUtility.SetDirty(artSet);
            return changed;
        }

        // ------------------------------------------------------------------
        // Prefab
        // ------------------------------------------------------------------

        internal static GameObject EnsurePrefab(string name, System.Func<GameObject> build, System.Text.StringBuilder log)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            GameObject go = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            log.AppendLine($"・Prefab を作った：{path}");
            return prefab;
        }

        private static GameObject BuildFlask(PlaceholderArt2D.Set s)
        {
            var root = new GameObject("Flask");
            var collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2.6f, 5f);
            collider.offset = new Vector2(0f, 0.8f);
            Transform art = Child(root.transform, "Art", Vector3.zero);
            AddSprite(art, "GlassBack", s.flaskBack, "Flask", 0, Vector3.zero);
            var maskGo = new GameObject("LiquidMask");
            maskGo.transform.SetParent(art, false);
            var mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = s.flaskMask;
            mask.alphaCutoff = 0.5f;
            SpriteRenderer liquid = AddSprite(art, "Liquid", s.liquidBody, "Liquid", 0, Vector3.zero);
            liquid.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            SpriteRenderer surface = AddSprite(art, "Surface", s.liquidSurface, "Liquid", 1, Vector3.zero);
            surface.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            AddSprite(art, "GlassFront", s.flaskFront, "FlaskFront", 0, Vector3.zero);

            Transform anchors = Child(root.transform, "Anchors", Vector3.zero);
            Transform mouth = Child(anchors, "Mouth", new Vector3(0f, 1.68f, 0f));
            Transform bottom = Child(anchors, "LiquidBottom", new Vector3(0f, -1.47f, 0f));
            Transform top = Child(anchors, "LiquidTop", new Vector3(0f, 1.05f, 0f));
            Light2D glow = PointLight(root.transform, "LiquidGlow", new Vector3(0f, -0.6f, 0f), Color.white, 0f, 2f, 0.4f);

            var flask = root.AddComponent<Flask2D>();
            SetRef(flask, "liquid", liquid);
            SetRef(flask, "surface", surface);
            SetRef(flask, "mouth", mouth);
            SetRef(flask, "liquidBottom", bottom);
            SetRef(flask, "liquidTop", top);
            SetRef(flask, "glow", glow);
            return root;
        }

        private static GameObject BuildHeater(PlaceholderArt2D.Set s)
        {
            var root = new GameObject("Heater");
            Transform art = Child(root.transform, "Art", Vector3.zero);
            AddSprite(art, "Stand", s.heaterStand, "Workbench", 5, Vector3.zero);
            var flames = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                SpriteRenderer f = AddSprite(art, "Flame_" + i, s.flame, "Flask", -1, new Vector3(-0.5f + i * 0.5f, 0.62f, 0f));
                f.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                flames.Add(f);
            }
            Transform knob = Child(root.transform, "Knob", new Vector3(2.6f, 0f, 0f));
            var knobCollider = knob.gameObject.AddComponent<CircleCollider2D>();
            knobCollider.radius = 0.55f;
            AddSprite(knob, "KnobBase", s.knobBase, "Workbench", 6, Vector3.zero);
            SpriteRenderer dial = AddSprite(knob, "KnobDial", s.knobDial, "Workbench", 7, Vector3.zero);
            Light2D heaterLight = PointLight(root.transform, "HeaterLight", new Vector3(0f, 0.7f, 0f), new Color(1f, 0.55f, 0.2f), 0f, 3.4f, 0.5f);

            var heater = root.AddComponent<HeaterKnob2D>();
            SetRef(heater, "knobDial", dial.transform);
            SetList(heater, "flames", flames);
            SetRef(heater, "heaterLight", heaterLight);
            return root;
        }

        private static GameObject BuildTrayItem()
        {
            var root = new GameObject("TrayItem");
            var hit = root.AddComponent<BoxCollider2D>();
            hit.size = new Vector2(0.9f, 0.95f);
            Transform art = Child(root.transform, "Art", Vector3.zero);
            SpriteRenderer content = AddSprite(art, "Content", null, "Items", 0, Vector3.zero);
            SpriteRenderer body = AddSprite(art, "Body", null, "Items", 1, Vector3.zero);
            var item = root.AddComponent<TrayItem2D>();
            SetRef(item, "body", body);
            SetRef(item, "content", content);
            SetRef(item, "art", art);
            SetRef(item, "hitBox", hit);
            return root;
        }

        private static GameObject BuildTray(PlaceholderArt2D.Set s, GameObject itemPrefab)
        {
            var root = new GameObject("Tray");
            Transform art = Child(root.transform, "Art", Vector3.zero);
            AddSprite(art, "Board", s.tray, "Workbench", 10, Vector3.zero);
            Transform slotsRoot = Child(root.transform, "Slots", Vector3.zero);
            var slots = new List<Object>();
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * 1.2f;
                slots.Add(Child(slotsRoot, "Slot_" + i.ToString("00"), new Vector3(x, 0.93f, 0f)));
            }
            var tray = root.AddComponent<BrewTray2D>();
            SetRef(tray, "itemPrefab", itemPrefab != null ? itemPrefab.GetComponent<TrayItem2D>() : null);
            SetList(tray, "slots", slots);
            return root;
        }

        private static GameObject BuildHeldItem(PlaceholderArt2D.Set s)
        {
            var root = new GameObject("HeldItem");
            Transform art = Child(root.transform, "Art", Vector3.zero);
            SpriteRenderer content = AddSprite(art, "Content", null, "Held", 0, Vector3.zero);
            SpriteRenderer body = AddSprite(art, "Body", null, "Held", 1, Vector3.zero);
            SpriteRenderer stream = AddSprite(root.transform, "Stream", s.pourStream, "Liquid", 10, Vector3.zero);
            stream.enabled = false;
            var held = root.AddComponent<HeldItem2D>();
            SetRef(held, "body", body);
            SetRef(held, "content", content);
            SetRef(held, "art", art);
            SetRef(held, "stream", stream);
            return root;
        }

        private static GameObject BuildStirRod(PlaceholderArt2D.Set s)
        {
            var root = new GameObject("StirRod");
            SpriteRenderer rod = AddSprite(root.transform, "Art", s.stirRod, "Liquid", 20, Vector3.zero);
            var stir = root.AddComponent<StirRod2D>();
            SetRef(stir, "rod", rod);
            return root;
        }

        private static GameObject BuildLamp(PlaceholderArt2D.Set s)
        {
            var root = new GameObject("Lamp");
            Transform art = Child(root.transform, "Art", Vector3.zero);
            AddSprite(art, "Body", s.lamp, "Room", 5, Vector3.zero);
            SpriteRenderer flame = AddSprite(art, "Flame", s.flame, "Room", 6, new Vector3(0f, -0.15f, 0f), new Color(1f, 0.8f, 0.45f, 1f));
            flame.transform.localScale = new Vector3(0.45f, 0.5f, 1f);
            Light2D light = PointLight(root.transform, "LampLight", new Vector3(0f, -0.1f, 0f), new Color(1f, 0.74f, 0.42f), 1.1f, 7f, 0.6f);
            var flicker = light.gameObject.AddComponent<LightFlicker2D>();
            SetRef(flicker, "flameArt", flame.transform);
            return root;
        }

        private static GameObject BuildHud()
        {
            var root = new GameObject("BrewHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Color panel = new Color(0.08f, 0.05f, 0.03f, 0.62f);
            Color ink = new Color(1f, 0.95f, 0.85f, 1f);

            // 左上：フラスコの中身
            RectTransform info = Panel(root.transform, "InfoPanel", new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(560f, 210f), panel);
            Text title = Label(info, "LiquidTitle", font, 30, ink, new Vector2(18f, -12f), new Vector2(524f, 44f), TextAnchor.UpperLeft);
            Text detail = Label(info, "LiquidDetail", font, 19, ink, new Vector2(18f, -60f), new Vector2(524f, 140f), TextAnchor.UpperLeft);

            // 左：工程
            RectTransform logPanel = Panel(root.transform, "ProcessPanel", new Vector2(0f, 1f), new Vector2(24f, -250f), new Vector2(560f, 200f), panel);
            Label(logPanel, "Heading", font, 16, new Color(1f, 0.85f, 0.6f, 1f), new Vector2(18f, -10f), new Vector2(524f, 24f), TextAnchor.UpperLeft).text = "これまでの工程";
            Text process = Label(logPanel, "ProcessLog", font, 16, ink, new Vector2(18f, -36f), new Vector2(524f, 156f), TextAnchor.UpperLeft);

            // 右上：ボタン
            Button bottleOne = MakeButton(root.transform, "BottleOneButton", font, "瓶に詰める F", new Vector2(1f, 1f), new Vector2(-24f, -24f), out Text _);
            Button bottleAll = MakeButton(root.transform, "BottleAllButton", font, "全部詰める Shift+F", new Vector2(1f, 1f), new Vector2(-24f, -92f), out Text bottleAllLabel);
            Button fastForward = MakeButton(root.transform, "FastForwardButton", font, "早送り Space", new Vector2(1f, 1f), new Vector2(-24f, -160f), out Text fastForwardLabel);

            // 右下：トレイのページ
            Button prev = MakeButton(root.transform, "PagePrevButton", font, "◀", new Vector2(1f, 0f), new Vector2(-200f, 200f), out Text _, new Vector2(64f, 52f));
            Button next = MakeButton(root.transform, "PageNextButton", font, "▶", new Vector2(1f, 0f), new Vector2(-24f, 200f), out Text _, new Vector2(64f, 52f));
            Text pageLabel = Label(root.transform, "PageLabel", font, 20, ink, Vector2.zero, new Vector2(100f, 52f), TextAnchor.MiddleCenter);
            Place(pageLabel.rectTransform, new Vector2(1f, 0f), new Vector2(-96f, 200f), new Vector2(100f, 52f), new Vector2(1f, 1f));

            // 上の真ん中：お知らせ・作業中の数字
            Text toast = Label(root.transform, "Toast", font, 26, ink, Vector2.zero, new Vector2(1000f, 44f), TextAnchor.MiddleCenter);
            Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1000f, 44f), new Vector2(0.5f, 1f));
            Text activity = Label(root.transform, "Activity", font, 22, new Color(1f, 0.9f, 0.6f, 1f), Vector2.zero, new Vector2(800f, 36f), TextAnchor.MiddleCenter);
            Place(activity.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(800f, 36f), new Vector2(0.5f, 1f));

            // 下：操作の案内
            Text hint = Label(root.transform, "ControlsHint", font, 15, new Color(1f, 0.95f, 0.85f, 0.75f), Vector2.zero, new Vector2(1500f, 26f), TextAnchor.MiddleCenter);
            Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(1500f, 26f), new Vector2(0.5f, 0f));
            hint.text = "トレイの物をクリックで持つ　液体：フラスコの上で押したまま下へ動かして注ぐ　固体：フラスコの上でクリック　手ぶらで円を描いて混ぜる　つまみをドラッグで火加減　右クリック・Q で戻す";

            // カーソルのそば：説明
            RectTransform tip = Panel(root.transform, "Tooltip", new Vector2(0f, 0f), Vector2.zero, new Vector2(420f, 74f), new Color(0.05f, 0.03f, 0.02f, 0.85f));
            tip.pivot = new Vector2(0f, 1f);
            Text tipText = Label(tip, "TooltipText", font, 20, ink, new Vector2(14f, -8f), new Vector2(392f, 60f), TextAnchor.UpperLeft);
            tip.GetComponent<Image>().raycastTarget = false;

            var hud = root.AddComponent<BrewHUD2D>();
            SetRef(hud, "liquidTitle", title);
            SetRef(hud, "liquidDetail", detail);
            SetRef(hud, "processLog", process);
            SetRef(hud, "tooltipPanel", tip);
            SetRef(hud, "tooltipText", tipText);
            SetRef(hud, "toast", toast);
            SetRef(hud, "activityText", activity);
            SetRef(hud, "bottleOneButton", bottleOne);
            SetRef(hud, "bottleAllButton", bottleAll);
            SetRef(hud, "bottleAllLabel", bottleAllLabel);
            SetRef(hud, "fastForwardButton", fastForward);
            SetRef(hud, "fastForwardLabel", fastForwardLabel);
            SetRef(hud, "pagePrevButton", prev);
            SetRef(hud, "pageNextButton", next);
            SetRef(hud, "pageLabel", pageLabel);
            return root;
        }

        // ------------------------------------------------------------------
        // シーン
        // ------------------------------------------------------------------

        internal static void Backup(string scenePath, System.Text.StringBuilder log)
        {
            PlaceholderArt2D.EnsureFolder(BackupDir);
            string name = Path.GetFileNameWithoutExtension(scenePath);
            string path = $"{BackupDir}/{name}_{System.DateTime.Now:yyyyMMdd_HHmmss}.unity";
            if (AssetDatabase.CopyAsset(scenePath, path)) log.AppendLine($"・前のシーンの控え：{path}");
        }

        /// <summary>カメラ（画面の高さ 10.8 ワールド単位 = 1080 ピクセル）</summary>
        internal static Camera CreateCamera(Vector3 position)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = position;
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.045f, 0.035f, 1f);
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            return cam;
        }

        /// <summary>部屋全体の明るさ（暗めの暖色）とブルーム</summary>
        internal static void CreateGlobalLightAndVolume(System.Text.StringBuilder log)
        {
            var globalGo = new GameObject("GlobalLight（部屋全体の明るさ）");
            var global = globalGo.AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.color = new Color(1f, 0.84f, 0.66f);
            global.intensity = 0.32f;
            ApplyAllLayers(global);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            var volumeGo = new GameObject("Global Volume（ブルーム）");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            if (profile == null) log.AppendLine($"！画面効果の設定 {VolumeProfilePath} が見つからない（「② 2D URP 環境をセットアップ」を先に実行）");
        }

        /// <summary>調合台のまわり（背景・ランプ・加熱台・フラスコ・ガラス棒・トレイ・調合台の灯り）を parent の下に置く</summary>
        internal static void PlaceBrewArea(BrewAssets a, Transform parent)
        {
            Vector3 o = parent != null ? parent.position : Vector3.zero;
            var lights = new GameObject("Lights").transform;
            lights.SetParent(parent, false);
            PointLight(lights, "MoonLight（窓の月明かり）", new Vector3(5.8f, 2.6f, 0f), new Color(0.55f, 0.68f, 1f), 0.7f, 5.5f, 0.8f);
            PointLight(lights, "FocusLight（調合台を照らす灯り）", new Vector3(0f, -0.6f, 0f), new Color(1f, 0.92f, 0.78f), 0.65f, 4.6f, 1.2f);

            var room = new GameObject("Desk（背景・ランプ）").transform;
            room.SetParent(parent, false);
            AddSprite(room, "Background（背景の絵）", a.sprites.background, "Background", 0, Vector3.zero);
            Instance(a.lamp, room, o + new Vector3(-6.8f, -2.08f, 0f));
            Instance(a.lamp, room, o + Step10Lighting2D.RightLampPosition).name = Step10Lighting2D.RightLampName;

            var bench = new GameObject("Workbench").transform;
            bench.SetParent(parent, false);
            Instance(a.heater, bench, o + new Vector3(0f, -2.68f, 0f));
            Instance(a.flask, bench, o + new Vector3(0f, -0.83f, 0f));
            Instance(a.stirRod, bench, o);
            Instance(a.tray, bench, o + new Vector3(0f, -4.6f, 0f));
        }

        /// <summary>ビルドに入れるシーンに足す（first なら一番はじめ＝起動したときのシーンにする）</summary>
        internal static void AddToBuildSettings(string scenePath, bool first)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = list.FindIndex(e => e.path == scenePath);
            if (index >= 0)
            {
                if (!first || index == 0) return;
                list.RemoveAt(index);
            }
            if (first) list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            else list.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        internal static List<T> LoadAll<T>() where T : Object
        {
            var result = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/GameData" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) result.Add(asset);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 部品づくりの小道具
        // ------------------------------------------------------------------

        internal static GameObject Instance(GameObject prefab, Transform parent, Vector3 position)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go;
        }

        internal static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        internal static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, string layer, int order, Vector3 localPosition, Color? color = null)
        {
            Transform t = Child(parent, name, localPosition);
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            if (color.HasValue) sr.color = color.Value;
            return sr;
        }

        internal static Light2D PointLight(Transform parent, string name, Vector3 localPosition, Color color, float intensity, float outer, float inner)
        {
            Transform t = Child(parent, name, localPosition);
            var light = t.gameObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightOuterRadius = outer;
            light.pointLightInnerRadius = inner;
            ApplyAllLayers(light);
            return light;
        }

        /// <summary>光をすべての描く順番（Sorting Layer）に当てる</summary>
        internal static void ApplyAllLayers(Light2D light)
        {
            var so = new SerializedObject(light);
            SerializedProperty prop = so.FindProperty("m_ApplyToSortingLayers");
            if (prop == null) return;
            UnityEngine.SortingLayer[] layers = UnityEngine.SortingLayer.layers;
            prop.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++) prop.GetArrayElementAtIndex(i).intValue = layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[2D 調合画面] {target.GetType().Name} に欄「{field}」がありません");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetList(Object target, string field, List<Object> values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null || !p.isArray)
            {
                Debug.LogError($"[2D 調合画面] {target.GetType().Name} に一覧「{field}」がありません");
                return;
            }
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- UI ----

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, anchor, position, size, anchor);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }

        private static Text Label(Transform parent, string name, Font font, int size, Color color, Vector2 position, Vector2 boxSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(0f, 1f), position, boxSize, new Vector2(0f, 1f));
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(Transform parent, string name, Font font, string caption, Vector2 anchor, Vector2 position, out Text label, Vector2? size = null)
        {
            Vector2 s = size ?? new Vector2(260f, 56f);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, anchor, position, s, anchor);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.32f, 0.21f, 0.13f, 0.92f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.9f, 0.75f, 1f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.55f, 1f);
            button.colors = colors;
            label = Label(go.transform, "Label", font, 20, new Color(1f, 0.95f, 0.85f, 1f), Vector2.zero, s, TextAnchor.MiddleCenter);
            label.text = caption;
            return button;
        }
    }
}
