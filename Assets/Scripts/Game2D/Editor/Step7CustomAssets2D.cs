using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step7：差し替え用の絵・音・フォントを Assets/CustomAssets にまとめる（何度実行してもよい）。
    ///   1. Art2D・Audio にあった絵と音を、種類ごとのフォルダへ移す（Unity の移動なので、Prefab・設定とのつながりはそのまま）
    ///   2. コードで描いていた UI の絵・コードで作っていた音を、PNG・WAV のファイルにして、UISkin・SoundLibrary・BrewHUD につなぐ
    ///      → どの絵・音も「同じ名前のファイルで上書き」だけで差し替わる
    ///   3. 差し替え用の絵の「元の大きさ」を .meta に覚える（大きさの違う絵で上書きしても、ゲームの中での大きさがそろう）
    ///   4. 空になった古いフォルダを消し、どこからも使われていないファイル・つながりの切れた欄を一覧にする
    /// 置き場所の表は CustomAssetPaths。
    /// </summary>
    public static class Step7CustomAssets2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string SoundLibraryPath = "Assets/Audio/SoundLibrary.asset";
        private const string BrewHudPath = "Assets/Art2D/Brewing/Prefabs/BrewHUD.prefab";
        private const int Rate = 44100;

        [MenuItem(Menu + "Step7：差し替え用フォルダに整理（絵・音・フォント）", priority = 18)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step7", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new StringBuilder();
            try
            {
                EditorUtility.DisplayProgressBar("Step7", "ファイルを移しています…", 0.1f);
                int moved = MoveAll(log);
                log.Insert(0, $"・移したファイル・フォルダ：{moved}\n");
                EditorUtility.DisplayProgressBar("Step7", "UI の絵をファイルにしています…", 0.4f);
                MakeUiFiles(log);
                EditorUtility.DisplayProgressBar("Step7", "音をファイルにしています…", 0.6f);
                MakeSoundFiles(log);
                EditorUtility.DisplayProgressBar("Step7", "絵の元の大きさを覚えています…", 0.8f);
                int designs = RecordDesignSizes();
                log.AppendLine($"・絵の元の大きさを覚えた：{designs} 枚（大きさの違う絵で上書きしても、ゲームの中の大きさがそろう）");
                RemoveEmptyOldFolders(log);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Report(log);
            Debug.Log("[Step7] 差し替え用フォルダ\n" + log);
            EditorUtility.DisplayDialog("Step7：差し替え用フォルダに整理", log.ToString(), "OK");
        }

        /// <summary>つながりの確認だけ（移動・ファイル作りはしない）</summary>
        [MenuItem(Menu + "Step7：差し替え用ファイルのつながりを確認", priority = 19)]
        private static void CheckOnly()
        {
            var log = new StringBuilder();
            Report(log);
            Debug.Log("[Step7] つながりの確認\n" + log);
            EditorUtility.DisplayDialog("差し替え用ファイルのつながり", log.ToString(), "OK");
        }

        // ==================================================================
        // 1. 移す
        // ==================================================================

        private static int MoveAll(StringBuilder log)
        {
            int moved = 0;
            P.EnsureFolder(CustomAssetPaths.Images);
            P.EnsureFolder(CustomAssetPaths.UI);
            P.EnsureFolder(CustomAssetPaths.Sounds);
            moved += MoveFolder("Assets/Art2D/Ingredients", CustomAssetPaths.Ingredients, log);
            moved += MoveFolder("Assets/Art2D/UI/Discovery", CustomAssetPaths.UIDiscovery, log);
            moved += MoveFolder("Assets/Art2D/Fonts", CustomAssetPaths.Fonts, log);
            moved += MoveFolder("Assets/Audio/BGM", CustomAssetPaths.Bgm, log);
            foreach (string file in FilesIn("Assets/Art2D/Brewing/Sprites"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                moved += MoveFile(file, CustomAssetPaths.FolderForImage(name, CustomAssetPaths.Images + "/Other"), log);
            }
            foreach (string dir in new[] { "Assets/Audio/Ambient", "Assets/Audio/SE" })
            {
                foreach (string file in FilesIn(dir))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string to = CustomAssetPaths.SoundFolders.TryGetValue(name, out string d) ? d : CustomAssetPaths.SE + "/Other";
                    moved += MoveFile(file, to, log);
                }
            }
            return moved;
        }

        private static IEnumerable<string> FilesIn(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir)) yield break;
            foreach (string f in Directory.GetFiles(dir))
            {
                if (f.EndsWith(".meta")) continue;
                yield return f.Replace('\\', '/');
            }
        }

        private static int MoveFolder(string from, string to, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(from)) return 0;
            if (AssetDatabase.IsValidFolder(to))
            {
                // もう移す先がある：中身を1つずつ移す
                int n = 0;
                foreach (string f in FilesIn(from)) n += MoveFile(f, to, log);
                return n;
            }
            P.EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                log.AppendLine($"！ {from} を移せなかった：{error}");
                return 0;
            }
            return 1;
        }

        private static int MoveFile(string from, string toDir, StringBuilder log)
        {
            P.EnsureFolder(toDir);
            string to = toDir + "/" + Path.GetFileName(from);
            if (File.Exists(to))
            {
                log.AppendLine($"！ {to} がもうあるので、{from} は移していない（どちらを使うか確かめてください）");
                return 0;
            }
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                log.AppendLine($"！ {from} を移せなかった：{error}");
                return 0;
            }
            return 1;
        }

        private static void RemoveEmptyOldFolders(StringBuilder log)
        {
            string[] old = { "Assets/Art2D/Brewing/Sprites", "Assets/Art2D/Ingredients", "Assets/Art2D/UI/Discovery", "Assets/Art2D/Fonts",
                             "Assets/Audio/BGM", "Assets/Audio/Ambient", "Assets/Audio/SE" };
            int removed = 0;
            foreach (string dir in old)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                bool empty = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0;
                if (empty && AssetDatabase.DeleteAsset(dir)) removed++;
                else if (!empty) log.AppendLine($"！ {dir} にまだファイルが残っている（表にない名前のファイル？）");
            }
            if (removed > 0) log.AppendLine($"・空になった古いフォルダを消した：{removed}");
        }

        // ==================================================================
        // 2. コードで描いていた UI の絵・コードで作っていた音をファイルに
        // ==================================================================

        private static void MakeUiFiles(StringBuilder log)
        {
            UISkin saved = UIFactory.Skin;
            UIFactory.Skin = null; // コードで描いた絵そのものを取り出す
            int made = 0, linked = 0;
            try
            {
                var common = new List<KeyValuePair<string, Func<Sprite>>>
                {
                    Pair("parchment", UIFactory.Parchment),
                    Pair("wood_panel", UIFactory.WoodPanel),
                    Pair("button", UIFactory.WoodPanel),
                    Pair("slot", () => UIFactory.SlotSprite(false)),
                    Pair("slot_selected", () => UIFactory.SlotSprite(true)),
                    Pair("knob", UIWidgets.Knob),
                    Pair("vignette", UIWidgets.Vignette),
                    Pair("sparkle", UIWidgets.Sparkle),
                    Pair("soft_dot", UIWidgets.SoftDot),
                };
                string[] skinFields = { "parchment", "woodPanel", "button", "slot", "slotSelected", "knob", "vignette", "sparkle", "softDot" };
                var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
                SerializedObject skinSo = skin != null ? new SerializedObject(skin) : null;
                for (int i = 0; i < common.Count; i++)
                {
                    Sprite s = WritePng(CustomAssetPaths.UICommon, common[i].Key, common[i].Value(), ref made);
                    if (skinSo != null) linked += Link(skinSo, skinFields[i], s);
                }
                if (skinSo != null && skinSo.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(skin);

                // 調合台の手ごたえ（見立てカードの棒・温度計・溶け具合の輪）：BrewHUD の Prefab の欄
                var brew = new List<KeyValuePair<string, Func<Sprite>>>
                {
                    Pair("bar_back", () => UIFactory.RoundedRect(new Color(0.22f, 0.14f, 0.08f, 0.9f), new Color(0.55f, 0.4f, 0.24f, 1f), 7, 2)),
                    Pair("bar_fill", () => UIFactory.RoundedRect(Color.white, Color.white, 6, 0)),
                    Pair("thermometer_tube", () => UIFactory.RoundedRect(new Color(0.1f, 0.08f, 0.07f, 0.75f), new Color(0.92f, 0.88f, 0.8f, 0.9f), 12, 3)),
                    Pair("stir_ring", BrewUiSprites2D.Ring),
                };
                string[] hudFields = { "barBack", "barFill", "thermometerTube", "stirRing" };
                var sprites = new Sprite[brew.Count];
                for (int i = 0; i < brew.Count; i++) sprites[i] = WritePng(CustomAssetPaths.UIBrew, brew[i].Key, brew[i].Value(), ref made);
                if (File.Exists(BrewHudPath))
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(BrewHudPath);
                    try
                    {
                        int n = 0;
                        foreach (BrewFeedback2D fb in root.GetComponentsInChildren<BrewFeedback2D>(true))
                        {
                            var so = new SerializedObject(fb);
                            for (int i = 0; i < hudFields.Length; i++) n += Link(so, hudFields[i], sprites[i]);
                            so.ApplyModifiedPropertiesWithoutUndo();
                        }
                        if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, BrewHudPath);
                        linked += n;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                UIFactory.Skin = saved;
            }
            log.AppendLine($"・UI の絵：ファイルにした {made} 枚、つないだ欄 {linked}（{CustomAssetPaths.UICommon}・{CustomAssetPaths.UIBrew}）");
        }

        private static KeyValuePair<string, Func<Sprite>> Pair(string name, Func<Sprite> make) => new KeyValuePair<string, Func<Sprite>>(name, make);

        /// <summary>空の欄だけつなぐ（自分で入れた絵はそのまま）</summary>
        private static int Link(SerializedObject so, string field, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null || value == null || p.objectReferenceValue != null) return 0;
            p.objectReferenceValue = value;
            return 1;
        }

        private static Sprite WritePng(string dir, string name, Sprite source, ref int made)
        {
            P.EnsureFolder(dir);
            string path = $"{dir}/{name}.png";
            if (!File.Exists(path) && source != null)
            {
                File.WriteAllBytes(path, Encode(source));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.spritePixelsPerUnit = source.pixelsPerUnit;
                    ti.spriteBorder = source.border;
                    ti.mipmapEnabled = false;
                    ti.alphaIsTransparency = true;
                    ti.textureCompression = TextureImporterCompression.Uncompressed; // 小さな UI の絵なので、にじまないように圧縮しない
                    ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                }
                made++;
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static byte[] Encode(Sprite sprite)
        {
            Texture2D tex = sprite.texture;
            if (tex.isReadable) return tex.EncodeToPNG();
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = copy.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(copy);
            return png;
        }

        private static void MakeSoundFiles(StringBuilder log)
        {
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            SerializedObject so = lib != null ? new SerializedObject(lib) : null;
            int made = 0, linked = 0;
            AudioClip One(string dir, string name, Func<AudioClip> make, float pitch = 1f) => WriteWav(dir, name, make, pitch, ref made);

            var singles = new (string dir, string file, Func<AudioClip> make, string field)[]
            {
                (CustomAssetPaths.SeFeedback, "se_star_chime", () => SfxSynth.Chime, "starChime"),
                (CustomAssetPaths.SeFeedback, "se_letter", () => SfxSynth.LetterArrive, "ingredientArrived"),
                (CustomAssetPaths.SeFeedback, "se_discovery", () => SfxSynth.Discovery, "discovery"),
                (CustomAssetPaths.SeFeedback, "se_reaction", () => SfxSynth.Reaction, "reactionSting"),
                (CustomAssetPaths.SeFeedback, "se_soft_warning", () => SfxSynth.SoftWarning, "softWarning"),
                (CustomAssetPaths.Ambient, "amb_lamp_fire_loop", () => SfxSynth.LampFireLoop, "lampFireLoop"),
            };
            foreach (var s in singles)
            {
                AudioClip clip = One(s.dir, s.file, s.make);
                if (so != null) linked += Link(so, s.field, clip);
            }
            var knocks = new[]
            {
                One(CustomAssetPaths.SeBrewing, "se_koto_knock_1", () => SfxSynth.Knock, 1f),
                One(CustomAssetPaths.SeBrewing, "se_koto_knock_2", () => SfxSynth.Knock, 0.9f),
                One(CustomAssetPaths.SeBrewing, "se_koto_knock_3", () => SfxSynth.Knock, 1.12f),
            };
            var glass = new[]
            {
                One(CustomAssetPaths.SeBrewing, "se_glass_touch_1", () => SfxSynth.GlassTap, 1f),
                One(CustomAssetPaths.SeBrewing, "se_glass_touch_2", () => SfxSynth.GlassTap, 0.93f),
            };
            if (so != null)
            {
                linked += LinkArray(so, "kotoKnock", knocks);
                linked += LinkArray(so, "glassTouch", glass);
                if (so.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(lib);
            }
            log.AppendLine($"・音：ファイルにした {made} 個、つないだ欄 {linked}（コードで作っていた手ごたえ・夜の環境音）");
        }

        private static int LinkArray(SerializedObject so, string field, AudioClip[] clips)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null || !p.isArray || p.arraySize > 0) return 0;
            p.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            return 1;
        }

        /// <summary>コードで作った音を 16bit の WAV にする（pitch で高さを変えた版も作れる）。ファイルがあれば触らない</summary>
        private static AudioClip WriteWav(string dir, string name, Func<AudioClip> make, float pitch, ref int made)
        {
            P.EnsureFolder(dir);
            string path = $"{dir}/{name}.wav";
            if (!File.Exists(path))
            {
                AudioClip clip = make();
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                if (Mathf.Abs(pitch - 1f) > 0.001f) data = Resample(data, pitch);
                using (var fs = new FileStream(path, FileMode.Create))
                using (var w = new BinaryWriter(fs))
                {
                    int bytes = data.Length * 2;
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));
                    w.Write(36 + bytes);
                    w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)clip.channels);
                    w.Write(clip.frequency);
                    w.Write(clip.frequency * clip.channels * 2);
                    w.Write((short)(clip.channels * 2));
                    w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data"));
                    w.Write(bytes);
                    foreach (float v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                made++;
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static float[] Resample(float[] data, float pitch)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt(data.Length / pitch));
            var result = new float[n];
            for (int i = 0; i < n; i++)
            {
                float src = i * pitch;
                int a = Mathf.Min(data.Length - 1, (int)src);
                int b = Mathf.Min(data.Length - 1, a + 1);
                result[i] = Mathf.Lerp(data[a], data[b], src - a);
            }
            return result;
        }

        // ==================================================================
        // 3. 絵の元の大きさを覚える
        // ==================================================================

        private static int RecordDesignSizes()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CustomAssetPaths.Images }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                if (Art2DImportRules.TryParse(ti.userData, out _)) continue;
                if (!Art2DImportRules.TryReadPngSize(path, out int w, out int h)) continue;
                ti.userData = Art2DImportRules.Format(w, h, ti.spritePixelsPerUnit, ti.spriteBorder);
                ti.SaveAndReimport();
                n++;
            }
            return n;
        }

        // ==================================================================
        // 4. つながりの確認
        // ==================================================================

        internal static void Report(StringBuilder log)
        {
            // どこからも使われていない絵・音（_Unused・フォントの太さ違いは除く）
            var roots = new List<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })) roots.Add(AssetDatabase.GUIDToAssetPath(g));
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Art2D" })) roots.Add(AssetDatabase.GUIDToAssetPath(g));
            foreach (string g in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/GameData", "Assets/Art2D", "Assets/Audio" })) roots.Add(AssetDatabase.GUIDToAssetPath(g));
            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
            var unused = new List<string>();
            if (Directory.Exists(CustomAssetPaths.Root))
            {
                foreach (string f in Directory.GetFiles(CustomAssetPaths.Root, "*", SearchOption.AllDirectories))
                {
                    string path = f.Replace('\\', '/');
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".png" && ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    if (path.Contains("/_Unused/")) continue;
                    if (!used.Contains(path)) unused.Add(path.Substring(CustomAssetPaths.Root.Length + 1));
                }
            }
            log.AppendLine();
            if (unused.Count == 0) log.AppendLine("○ CustomAssets の絵・音は、すべてゲームから使われている");
            else
            {
                log.AppendLine($"△ どこからも使われていないファイル {unused.Count} 個（名前のつづり違い・新しく足したファイル？）：");
                foreach (string u in unused) log.AppendLine("    " + u);
            }

            // つながりの切れた欄（ファイルを消した・名前を変えた）
            var missing = new List<string>();
            CheckFields(AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath), "UISkin", missing, "crosshair");
            CheckFields(AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath), "SoundLibrary", missing);
            foreach (string g in AssetDatabase.FindAssets("t:BrewArt2D")) CheckFields(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g)), "BrewArt2D", missing);
            if (missing.Count == 0) log.AppendLine("○ UISkin・SoundLibrary・BrewArt2D に、つながりの切れた欄はない");
            else
            {
                log.AppendLine($"× つながりの切れた欄・空の欄 {missing.Count} 個（同じ名前のファイルを戻すと直る）：");
                foreach (string m in missing) log.AppendLine("    " + m);
            }
        }

        private static void CheckFields(UnityEngine.Object asset, string label, List<string> missing, params string[] skip)
        {
            if (asset == null) return;
            var so = new SerializedObject(asset);
            SerializedProperty p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = true;
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (p.name == "m_Script" || Array.IndexOf(skip, p.name) >= 0) continue;
                if (p.type != "PPtr<$Sprite>" && p.type != "PPtr<$AudioClip>" && p.type != "PPtr<$Font>") continue;
                // 参照が切れている（ファイルがない）か、空
                if (p.objectReferenceValue == null) missing.Add($"{label}：{p.propertyPath}{(p.objectReferenceInstanceIDValue != 0 ? "（ファイルがない）" : "（空）")}");
            }
        }
    }
}
