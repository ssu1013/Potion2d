using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using P = PotionFrontier.Game2D.EditorTools.PlaceholderArt2D;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step8：完全1画面（夜の調合机だけ）のゲームにする（何度実行してもよい）。
    ///   1. シーン：部屋（Room の Prefab）・お店・相談の仕組みの名残り（なくなった Prefab・スクリプト）を取り除き、
    ///      カメラを調合机に固定する
    ///   2. 素材が机に届く時期（「届くのに必要な発見」）を素材データに入れる（はじめから机にある素材は GameBalance）
    ///   3. 素材が届いたときの手紙の絵（letter.png）と音（se_letter.wav）を作って、UISkin・SoundLibrary につなぐ
    ///   4. 部屋・お店・お金・相談の絵と音を、消さずに _Unused へ移す
    ///   5. 消した欄（お金・値段・相談）の名残りを、設定とデータから掃除する
    /// </summary>
    public static class Step8SingleScreen2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string SoundLibraryPath = "Assets/Audio/SoundLibrary.asset";
        private const string BalancePath = "Assets/GameData/GameBalance.asset";

        [MenuItem(Menu + "Step8：1画面にする（お店・仕入れ・相談をなくす）", priority = 19)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step8", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new StringBuilder();
            try
            {
                EditorUtility.DisplayProgressBar("Step8", "素材が届く時期を入れています…", 0.1f);
                AlchemySampleDataGenerator.CreateOrUpdateAssets();
                ReportArrivals(log);

                EditorUtility.DisplayProgressBar("Step8", "手紙の絵と音を作っています…", 0.3f);
                MakeLetter(log);

                EditorUtility.DisplayProgressBar("Step8", "使わなくなった絵と音を _Unused へ移しています…", 0.45f);
                MoveUnused(log);

                EditorUtility.DisplayProgressBar("Step8", "消した欄の名残りを掃除しています…", 0.6f);
                Reserialize(log);

                EditorUtility.DisplayProgressBar("Step8", "シーンを1画面にしています…", 0.8f);
                CleanScene(log);
                CleanBuildSettings(log);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Step7CustomAssets2D.Report(log);
            Debug.Log("[Step8] 1画面にする\n" + log);
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_step8.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, log.ToString());
            }
            catch (IOException) { }
            EditorUtility.DisplayDialog("Step8：1画面にする", log.ToString(), "OK");
        }

        // ==================================================================
        // 2. 素材が届く時期
        // ==================================================================

        private static void ReportArrivals(StringBuilder log)
        {
            var db = AssetDatabase.LoadAssetAtPath<IngredientDatabase>("Assets/GameData/Alchemy/IngredientDatabase.asset");
            var balance = AssetDatabase.LoadAssetAtPath<GameBalance>(BalancePath);
            if (db == null)
            {
                log.AppendLine("！ 素材の一覧（Assets/GameData/Alchemy/IngredientDatabase.asset）がない");
                return;
            }
            var starters = new HashSet<IngredientData>();
            if (balance != null) foreach (IngredientAmount e in balance.startingIngredients) if (e != null && e.ingredient != null) starters.Add(e.ingredient);
            var later = new List<IngredientData>();
            foreach (IngredientData d in db.All) if (d != null && !starters.Contains(d)) later.Add(d);
            later.Sort((a, b) => a.DiscoveriesToUnlock.CompareTo(b.DiscoveriesToUnlock));
            var sb = new StringBuilder();
            foreach (IngredientData d in later)
            {
                if (sb.Length > 0) sb.Append("・");
                sb.Append(d.DisplayName).Append(' ').Append(d.DiscoveriesToUnlock);
            }
            log.AppendLine($"・はじめから机にある素材：{starters.Count} 種類（GameBalance）");
            log.AppendLine($"・発見で届く素材（届くのに必要な発見）：{sb}");
        }

        // ==================================================================
        // 3. 手紙の絵と音
        // ==================================================================

        private static void MakeLetter(StringBuilder log)
        {
            string png = CustomAssetPaths.UIDiscovery + "/letter.png";
            bool newPng = !File.Exists(png);
            Sprite letter = P.Ensure(CustomAssetPaths.UIDiscovery, "letter", 128, 128, LetterArt);
            if (AssetImporter.GetAtPath(png) is TextureImporter ti && !Art2DImportRules.TryParse(ti.userData, out _) &&
                Art2DImportRules.TryReadPngSize(png, out int w, out int h))
            {
                // 小さな UI の絵なので圧縮しない。元の大きさを覚える（大きく描き直して上書きしても、出る大きさがそろう）
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.userData = Art2DImportRules.Format(w, h, ti.spritePixelsPerUnit, ti.spriteBorder);
                ti.SaveAndReimport();
                letter = AssetDatabase.LoadAssetAtPath<Sprite>(png);
            }
            string wav = CustomAssetPaths.SeFeedback + "/se_letter.wav";
            bool newWav = !File.Exists(wav);
            AudioClip sound = WriteWav(CustomAssetPaths.SeFeedback, "se_letter", SfxSynth.LetterArrive);

            int linked = 0;
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin != null && skin.letter == null && letter != null)
            {
                skin.letter = letter;
                EditorUtility.SetDirty(skin);
                linked++;
            }
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SoundLibraryPath);
            if (lib != null && lib.ingredientArrived == null && sound != null)
            {
                lib.ingredientArrived = sound;
                EditorUtility.SetDirty(lib);
                linked++;
            }
            log.AppendLine($"・素材が届いたときの手紙：絵 {(newPng ? "作った" : "そのまま")}（{png}）／ 音 {(newWav ? "作った" : "そのまま")}（{wav}）／ つないだ欄 {linked}");
        }

        /// <summary>封をした手紙（封筒・ふた・赤い封蝋）。128×128、真ん中が (0,0)</summary>
        private static Color LetterArt(float x, float y)
        {
            float body = P.Box(x, y, 0f, -4f, 50f, 33f, 6f);
            Color paper = P.Paint(new Color(0.96f, 0.9f, 0.76f, 1f), P.Fill(body));
            Color edge = P.Paint(new Color(0.52f, 0.37f, 0.22f, 1f), P.Stroke(body, 3f));
            // ふた（上の2つの角から真ん中へ）
            float flap = Mathf.Min(Segment(x, y, -48f, 27f, 0f, -6f), Segment(x, y, 48f, 27f, 0f, -6f));
            Color fold = P.Paint(new Color(0.6f, 0.45f, 0.28f, 0.9f), P.Stroke(flap, 2.6f));
            // 下の折り目（うすく）
            float under = Mathf.Min(Segment(x, y, -48f, -35f, -10f, -12f), Segment(x, y, 48f, -35f, 10f, -12f));
            Color crease = P.Paint(new Color(0.62f, 0.48f, 0.3f, 0.45f), P.Stroke(under, 2f));
            // 封蝋
            float seal = P.Circle(x, y, 0f, -6f, 11f);
            Color wax = P.Paint(new Color(0.66f, 0.17f, 0.14f, 1f), P.Fill(seal));
            Color shine = P.Paint(new Color(0.9f, 0.45f, 0.38f, 0.8f), P.Fill(P.Circle(x, y, -3.5f, -2.5f, 3f)));
            // 影
            Color shadow = P.Paint(new Color(0f, 0f, 0f, 0.25f), P.Fill(P.Box(x, y, 3f, -9f, 50f, 33f, 8f) - 2f));
            return P.Over(shine, P.Over(wax, P.Over(fold, P.Over(crease, P.Over(edge, P.Over(paper, shadow))))));
        }

        /// <summary>点 (x,y) から線分 (ax,ay)-(bx,by) までの距離</summary>
        private static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            var p = new Vector2(x - ax, y - ay);
            var d = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p, d) / Mathf.Max(0.0001f, d.sqrMagnitude));
            return (p - d * t).magnitude;
        }

        /// <summary>コードで作った音を 16bit の WAV にする（ファイルがあれば触らない）</summary>
        internal static AudioClip WriteWav(string dir, string name, AudioClip clip)
        {
            P.EnsureFolder(dir);
            string path = $"{dir}/{name}.wav";
            if (!File.Exists(path) && clip != null)
            {
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
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
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ==================================================================
        // 4. 使わなくなった絵と音を _Unused へ（消さない）
        // ==================================================================

        private static void MoveUnused(StringBuilder log)
        {
            int moved = 0;
            // 表（CustomAssetPaths）の場所と、今の場所がちがう絵・音を、表の場所へ移す（Unity の移動なので、つながりはそのまま）
            moved += MoveByTable(CustomAssetPaths.Images, "*.png", CustomAssetPaths.ImageFolders, log);
            moved += MoveByTable(CustomAssetPaths.Sounds, "*.wav", CustomAssetPaths.SoundFolders, log);
            // 中身が README だけになったフォルダ（部屋の絵・お店の音）は片付ける
            int removed = 0;
            foreach (string dir in new[] { CustomAssetPaths.Images + "/Room", CustomAssetPaths.SE + "/Shop" })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                bool onlyReadme = true;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string n = Path.GetFileName(f);
                    if (n != "README.txt" && n != "README.txt.meta") onlyReadme = false;
                }
                if (onlyReadme && AssetDatabase.DeleteAsset(dir)) removed++;
                else if (!onlyReadme) log.AppendLine($"！ {dir} に、まだファイルが残っている（表にない名前？）");
            }
            log.AppendLine($"・使わなくなった絵と音を移した：{moved} 個（Images/_Unused・Sounds/_Unused。消してはいない）" + (removed > 0 ? $"／ 空になったフォルダ {removed} 個を片付けた" : ""));
        }

        private static int MoveByTable(string root, string pattern, Dictionary<string, string> table, StringBuilder log)
        {
            if (!Directory.Exists(root)) return 0;
            int n = 0;
            foreach (string f in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
            {
                string path = f.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                if (!table.TryGetValue(name, out string dir)) continue;
                string current = Path.GetDirectoryName(path).Replace('\\', '/');
                if (current == dir) continue;
                P.EnsureFolder(dir);
                string to = dir + "/" + Path.GetFileName(path);
                if (File.Exists(to))
                {
                    log.AppendLine($"！ {to} がもうあるので、{path} は移していない");
                    continue;
                }
                string error = AssetDatabase.MoveAsset(path, to);
                if (string.IsNullOrEmpty(error)) n++;
                else log.AppendLine($"！ {path} を移せなかった：{error}");
            }
            return n;
        }

        // ==================================================================
        // 5. 消した欄の名残りを掃除
        // ==================================================================

        private static void Reserialize(StringBuilder log)
        {
            var paths = new List<string> { SkinPath, SoundLibraryPath, BalancePath };
            foreach (string guid in AssetDatabase.FindAssets("t:IngredientData", new[] { "Assets/GameData" })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.RemoveAll(p => !File.Exists(p));
            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
            log.AppendLine($"・お金・値段・相談の欄の名残りを、設定とデータから掃除した：{paths.Count} 個（UISkin・SoundLibrary・GameBalance・素材）");
        }

        // ==================================================================
        // 1. シーン
        // ==================================================================

        private static void CleanScene(StringBuilder log)
        {
            if (!File.Exists(Atelier2DBuilder.ScenePath))
            {
                log.AppendLine($"！ {Atelier2DBuilder.ScenePath} がない（「アトリエを作る」を先に実行）");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(Atelier2DBuilder.ScenePath);

            // なくなった Prefab（部屋）の置き場所
            int prefabs = 0;
            var all = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects()) Collect(root.transform, all);
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                if (PrefabUtility.GetPrefabInstanceStatus(go) != PrefabInstanceStatus.MissingAsset) continue;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go) continue;
                Object.DestroyImmediate(go);
                prefabs++;
            }

            // なくなったスクリプト（お客さん・相談・お金・部屋の仕組み）
            int scripts = 0;
            all.Clear();
            foreach (GameObject root in scene.GetRootGameObjects()) Collect(root.transform, all);
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing <= 0) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    log.AppendLine($"！ {go.name}（Prefab の中）に、なくなったスクリプトが {missing} 個ある（Prefab を開いて取り除く）");
                    continue;
                }
                scripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            }

            // 名前を今の役目に合わせる
            int renamed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("AtelierHUD") && root.name != "AtelierHUD（タイトル・レシピ本・素材ノート・お知らせ）") { root.name = "AtelierHUD（タイトル・レシピ本・素材ノート・お知らせ）"; renamed++; }
                if (root.name.StartsWith("BrewArea") && root.name != "BrewArea（調合机）") { root.name = "BrewArea（調合机）"; renamed++; }
                if (root.name.StartsWith("BrewArea"))
                {
                    // 机の背景とランプのまとまり（部屋ではないので、名前を机に）
                    Transform desk = root.transform.Find("Room");
                    if (desk != null) { desk.name = "Desk（背景・ランプ）"; renamed++; }
                }
            }

            // カメラを調合机に固定
            // 開いたばかりのシーンでは Find が空を返すことがあるので、シーンの中を直接さがす（止めてある物も）
            AtelierView2D view = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                view = root.GetComponentInChildren<AtelierView2D>(true);
                if (view != null) break;
            }
            string cameraNote;
            if (view == null)
            {
                cameraNote = "！ AtelierView2D がない（「アトリエのシーンを作り直す」で作り直してください）";
            }
            else
            {
                var so = new SerializedObject(view);
                SerializedProperty camProp = so.FindProperty("viewCamera");
                SerializedProperty viewProp = so.FindProperty("brewView");
                if (camProp.objectReferenceValue == null)
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        Camera c = root.GetComponentInChildren<Camera>(true);
                        if (c != null) { camProp.objectReferenceValue = c; break; }
                    }
                }
                if (viewProp.objectReferenceValue == null)
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name.StartsWith("BrewView")) { viewProp.objectReferenceValue = t; break; }
                        }
                        if (viewProp.objectReferenceValue != null) break;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                var cam = camProp.objectReferenceValue as Camera;
                var spot = viewProp.objectReferenceValue as Transform;
                if (spot != null && spot.name != "BrewView（調合机のカメラの位置）") { spot.name = "BrewView（調合机のカメラの位置）"; renamed++; }
                float size = so.FindProperty("brewSize").floatValue;
                if (cam != null && spot != null)
                {
                    Vector3 p = cam.transform.position;
                    cam.transform.position = new Vector3(spot.position.x, spot.position.y, p.z);
                    if (size > 0f) cam.orthographicSize = size;
                    cameraNote = $"・カメラを調合机に固定（{cam.transform.position.x:0.##}, {cam.transform.position.y:0.##}・大きさ {cam.orthographicSize:0.##}）";
                }
                else cameraNote = "！ カメラか調合机の位置（BrewView）が見つからない";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"・シーン：なくなった Prefab（部屋）{prefabs} 個・なくなったスクリプト（相談・お金・部屋）{scripts} 個を取り除いた ／ 名前を直した {renamed} 個");
            log.AppendLine(cameraNote);
        }

        private static void Collect(Transform t, List<GameObject> into)
        {
            into.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), into);
        }

        private static void CleanBuildSettings(StringBuilder log)
        {
            var keep = new List<EditorBuildSettingsScene>();
            int dropped = 0;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == Atelier2DBuilder.ScenePath && File.Exists(s.path)) keep.Add(s);
                else dropped++;
            }
            if (keep.Count == 0 && File.Exists(Atelier2DBuilder.ScenePath)) keep.Add(new EditorBuildSettingsScene(Atelier2DBuilder.ScenePath, true));
            if (dropped > 0 || keep.Count != EditorBuildSettings.scenes.Length) EditorBuildSettings.scenes = keep.ToArray();
            log.AppendLine($"・ビルドのシーン：Atelier2D だけ（外した {dropped} 個）");
        }
    }
}
