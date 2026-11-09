using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// 製品に向けた仕上げのメニュー（Tools/Potion Frontier 2D/仕上げ）。
    ///   ① フォントを入れる      … Assets/CustomAssets/Fonts の TTF/OTF を UISkin と調合画面の文字に割り当てる
    ///   ② 文字の抜けをチェック  … ゲームの文章に、フォントにない文字（□ になる字）がないか調べる
    ///   ③ プレイヤー設定        … ウィンドウの大きさ変更・二重起動の防止・ビルドに入れるシーンなど
    ///   ④ 通しテスト            … 自動で一通り遊んで、画面写真と記録を AutoTestShots2D/ に残す
    ///   ⑤ Windows 版をビルド    … Builds/Windows/ に書き出して、問題点を Logs/claude_build_check.txt にまとめる
    /// </summary>
    public static class Finishing2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/仕上げ/";
        internal const string FontDir = CustomAssetPaths.Fonts;
        private const string SkinPath = "Assets/Art2D/UI/UISkin.asset";
        private const string BrewHudPath = Brew2DBuilder.PrefabDir + "/BrewHUD.prefab";
        private const string AtelierScene = "Assets/Scenes/Atelier2D.unity";
        private const string BuildDir = "Builds/Windows";
        private const string ExeName = "PotionFrontier.exe";

        /// <summary>フォントにない字（記号など）を、プレイヤーの PC の日本語フォントで補う（名前を入れ直すときに、めずらしい漢字を打たれても □ にしない）</summary>
        private static readonly string[] OsFallback = { "Yu Gothic UI", "Meiryo", "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans JP" };
        private static readonly string[] RegularWeights = { "Medium", "Regular", "Book", "Normal" };
        private static readonly string[] BoldWeights = { "Bold", "ExtraBold", "SemiBold", "Black", "Heavy" };

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        // ==================================================================
        // ① フォント
        // ==================================================================

        [MenuItem(Menu + "① フォントを入れる（CustomAssets/Fonts の TTF）", priority = 60)]
        private static void InstallFont()
        {
            if (!AssetDatabase.IsValidFolder(FontDir))
            {
                PlaceholderArt2D.EnsureFolder(FontDir);
                EditorUtility.DisplayDialog("フォントを入れる",
                    FontDir + " を作りました。\n\nGoogle Fonts から「M PLUS Rounded 1c」か「Zen Maru Gothic」をダウンロードして、" +
                    "中の .ttf（Medium と Bold があればその2つ）と OFL.txt をこのフォルダに入れてから、もう一度このメニューを実行してください。", "OK");
                return;
            }

            var families = new SortedDictionary<string, List<string>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { FontDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;
                string family = Family(path);
                if (!families.TryGetValue(family, out List<string> list)) families[family] = list = new List<string>();
                list.Add(path);
            }
            if (families.Count == 0)
            {
                EditorUtility.DisplayDialog("フォントを入れる", FontDir + " に .ttf / .otf がありません。\nダウンロードしたフォントのファイルを入れてください。", "OK");
                return;
            }

            var names = new List<string>(families.Keys);
            string chosen = names[0];
            if (names.Count >= 2)
            {
                int pick = EditorUtility.DisplayDialogComplex("フォントを入れる",
                    "書体が " + names.Count + " 種類あります。どちらを使いますか？\n（" + string.Join("・", names) + "）", names[0], "やめる", names[1]);
                if (pick == 1) return;
                chosen = pick == 2 ? names[1] : names[0];
            }
            List<string> files = families[chosen];
            string regularPath = PickWeight(files, RegularWeights, null) ?? files[0];
            string boldPath = PickWeight(files, BoldWeights, regularPath);

            var log = new StringBuilder();
            Font regular = PrepareFont(regularPath, log);
            Font bold = boldPath != null ? PrepareFont(boldPath, log) : null;
            if (regular == null)
            {
                EditorUtility.DisplayDialog("フォントを入れる", "フォントを読み込めませんでした：" + regularPath, "OK");
                return;
            }

            // UISkin（アトリエの UI 全部）
            UISkin skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin != null)
            {
                Undo.RecordObject(skin, "フォントを入れる");
                skin.font = regular;
                skin.fontBold = bold;
                EditorUtility.SetDirty(skin);
                log.AppendLine("UISkin：ふつう " + regular.name + " ／ 太字 " + (bold != null ? bold.name : "なし（ふつうの字を太らせる）"));
            }
            else log.AppendLine("！ " + SkinPath + " がありません（「アトリエを作る」を先に実行）");

            // 調合画面の文字（BrewHUD の Prefab）
            if (File.Exists(BrewHudPath))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(BrewHudPath);
                try
                {
                    BrewHUD2D hud = root.GetComponent<BrewHUD2D>();
                    if (hud != null)
                    {
                        Brew2DBuilder.SetRef(hud, "font", regular);
                        Brew2DBuilder.SetRef(hud, "boldFont", bold);
                        foreach (Text t in root.GetComponentsInChildren<Text>(true)) t.font = regular;
                        PrefabUtility.SaveAsPrefabAsset(root, BrewHudPath);
                        log.AppendLine("調合画面（BrewHUD）にも同じフォントを入れました");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();

            bool license = HasLicense();
            if (!license) log.AppendLine("！ OFL.txt（ライセンスの文）が " + FontDir + " にありません。ダウンロードした zip の中の OFL.txt を入れてください（製品に同梱が必要）");

            GlyphReport report = CheckGlyphs(regular, bold);
            log.AppendLine();
            log.Append(report.Summary);
            WriteLog("claude_font_check.txt", log.ToString() + "\n\n" + report.Detail);
            Debug.Log("[フォント] " + log);
            EditorUtility.DisplayDialog("フォントを入れる", log.ToString(), "OK");
        }

        [MenuItem(Menu + "② フォントの文字の抜けをチェック", priority = 61)]
        private static void CheckFontMenu()
        {
            UISkin skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin == null || skin.font == null)
            {
                EditorUtility.DisplayDialog("文字の抜けをチェック", "UISkin にフォントが入っていません。先に「① フォントを入れる」を実行してください。", "OK");
                return;
            }
            GlyphReport report = CheckGlyphs(skin.font, skin.fontBold);
            WriteLog("claude_font_check.txt", report.Summary + "\n\n" + report.Detail);
            Debug.Log("[フォント] " + report.Summary + "\n" + report.Detail);
            EditorUtility.DisplayDialog("文字の抜けをチェック", report.Summary + "\n\n（くわしくは Logs/claude_font_check.txt）", "OK");
        }

        private static string Family(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int dash = name.LastIndexOf('-');
            return dash > 0 ? name.Substring(0, dash) : name;
        }

        private static string Weight(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int dash = name.LastIndexOf('-');
            return dash > 0 ? name.Substring(dash + 1) : "Regular";
        }

        private static string PickWeight(List<string> files, string[] order, string exclude)
        {
            foreach (string w in order)
            {
                foreach (string f in files)
                {
                    if (f != exclude && string.Equals(Weight(f), w, System.StringComparison.OrdinalIgnoreCase)) return f;
                }
            }
            return null;
        }

        private static Font PrepareFont(string path, StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(path) as TrueTypeFontImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.fontTextureCase != FontTextureCase.Dynamic) { importer.fontTextureCase = FontTextureCase.Dynamic; changed = true; }
                if (!importer.includeFontData) { importer.includeFontData = true; changed = true; }
                if (importer.fontNames == null || importer.fontNames.Length == 0) { importer.fontNames = OsFallback; changed = true; }
                if (changed)
                {
                    importer.SaveAndReimport();
                    log.AppendLine("読み込み設定を直しました：" + Path.GetFileName(path) + "（Dynamic・フォントの中身を含める）");
                }
            }
            return AssetDatabase.LoadAssetAtPath<Font>(path);
        }

        private static bool HasLicense()
        {
            if (!Directory.Exists(FontDir)) return false;
            foreach (string f in Directory.GetFiles(FontDir, "*.txt"))
            {
                string name = Path.GetFileName(f).ToUpperInvariant();
                if (name.Contains("OFL") || name.Contains("LICENSE")) return true;
                string head = File.ReadAllText(f);
                if (head.Contains("SIL Open Font License")) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 文字の抜け（ゲームの文章に使っている字が、フォントにあるか）
        // ------------------------------------------------------------------

        internal sealed class GlyphReport
        {
            public string Summary = "";
            public string Detail = "";
            public int Missing;
        }

        internal static GlyphReport CheckGlyphs(Font font, Font bold)
        {
            var where = new Dictionary<char, string>();
            int sources = 0;

            // ゲームのデータ（素材・反応など）の文字
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/GameData" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) continue;
                sources++;
                var so = new SerializedObject(asset);
                SerializedProperty p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType == SerializedPropertyType.String) Collect(p.stringValue, Path.GetFileNameWithoutExtension(path), where);
                }
            }
            // コードの中の文章（"…" の中だけ。コメントは見ない。エディタ用のコードは見ない）
            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string unix = file.Replace('\\', '/');
                if (unix.Contains("/Editor/") || unix.Contains("/Debugging/")) continue;
                sources++;
                foreach (string literal in StringLiterals(File.ReadAllText(file))) Collect(literal, Path.GetFileName(file), where);
            }
            // Prefab に直接書いた文字（調合画面など）
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Art2D" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (Text t in go.GetComponentsInChildren<Text>(true))
                {
                    sources++;
                    Collect(t.text, Path.GetFileNameWithoutExtension(path), where);
                }
            }

            var missing = new List<char>();
            var missingBold = new List<char>();
            int nonAscii = 0;
            foreach (KeyValuePair<char, string> kv in where)
            {
                char c = kv.Key;
                if (c > 0x7F) nonAscii++;
                if (!font.HasCharacter(c)) missing.Add(c);
                if (bold != null && !bold.HasCharacter(c)) missingBold.Add(c);
            }
            missing.Sort();
            missingBold.Sort();

            var report = new GlyphReport { Missing = missing.Count };
            var sb = new StringBuilder();
            if (nonAscii > 20 && missing.Count > nonAscii / 2)
            {
                sb.Append("フォントの文字を調べられませんでした（ほとんど全部が「ない」になった）。フォントの読み込み設定が Dynamic か確かめてください。");
                report.Summary = sb.ToString();
                return report;
            }
            sb.Append($"ゲームで使っている字 {where.Count} 種類（{sources} か所から集めた）を調べました。\n");
            sb.Append(missing.Count == 0
                ? $"「{font.name}」に全部あります。"
                : $"「{font.name}」にない字が {missing.Count} 種類あります（PC の日本語フォントで補うので Windows では出ますが、Steam Deck などでは □ になります）：\n" + Chars(missing));
            if (bold != null)
            {
                sb.Append(missingBold.Count == 0 ? $"\n太字「{bold.name}」にも全部あります。" : $"\n太字「{bold.name}」にない字 {missingBold.Count} 種類：" + Chars(missingBold));
            }
            report.Summary = sb.ToString();

            var detail = new StringBuilder();
            foreach (char c in missing) detail.AppendLine($"ない字「{c}」(U+{(int)c:X4})：{where[c]} などで使用");
            foreach (char c in missingBold) if (!missing.Contains(c)) detail.AppendLine($"太字にない字「{c}」(U+{(int)c:X4})：{where[c]} などで使用");
            report.Detail = detail.ToString();
            return report;
        }

        private static string Chars(List<char> list)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < 60; i++) sb.Append(list[i]).Append(' ');
            if (list.Count > 60) sb.Append("…");
            return sb.ToString();
        }

        private static void Collect(string text, string source, Dictionary<char, string> where)
        {
            if (string.IsNullOrEmpty(text)) return;
            bool inTag = false;
            foreach (char c in text)
            {
                // リッチテキストの <size=…> などは画面に出ないので数えない
                if (c == '<') inTag = true;
                else if (c == '>' && inTag) { inTag = false; continue; }
                if (inTag) continue;
                if (char.IsControl(c) || c == ' ' || char.IsSurrogate(c)) continue;
                if (!where.ContainsKey(c)) where[c] = source;
            }
        }

        /// <summary>
        /// C# のソースから、文字列（"…"・@"…"・$"…{式}…"）の中身を取り出す。コメントと文字 '…' は飛ばす。
        /// $"…" の {式} の中にある文字列も、入れ子のまま拾う
        /// </summary>
        private static IEnumerable<string> StringLiterals(string src)
        {
            var result = new List<string>();
            // 0 = コード, 1 = "…", 2 = @"…", 3 = $"…", 4 = $@"…", 5 = $"…" の {式}
            var modes = new Stack<int>();
            var depths = new Stack<int>();
            var sb = new StringBuilder();
            modes.Push(0);
            depths.Push(0);
            int i = 0, n = src.Length;
            while (i < n)
            {
                int mode = modes.Peek();
                char c = src[i];
                char next = i + 1 < n ? src[i + 1] : '\0';
                if (mode == 0 || mode == 5)
                {
                    if (c == '/' && next == '/') { while (i < n && src[i] != '\n') i++; continue; }
                    if (c == '/' && next == '*')
                    {
                        int end = src.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                        i = end < 0 ? n : end + 2;
                        continue;
                    }
                    if (c == '\'')
                    {
                        i++;
                        while (i < n && src[i] != '\'' && src[i] != '\n') i += src[i] == '\\' ? 2 : 1;
                        i++;
                        continue;
                    }
                    int open = -1, skip = 0;
                    if (c == '"') { open = 1; skip = 1; }
                    else if (c == '@' && next == '"') { open = 2; skip = 2; }
                    else if (c == '$' && next == '"') { open = 3; skip = 2; }
                    else if ((c == '$' && next == '@') || (c == '@' && next == '$')) { if (i + 2 < n && src[i + 2] == '"') { open = 4; skip = 3; } }
                    if (open > 0)
                    {
                        if (sb.Length > 0) { result.Add(sb.ToString()); sb.Length = 0; }
                        modes.Push(open);
                        depths.Push(0);
                        i += skip;
                        continue;
                    }
                    if (mode == 5)
                    {
                        if (c == '{') { depths.Push(depths.Pop() + 1); }
                        else if (c == '}')
                        {
                            int d = depths.Pop();
                            if (d == 0) { modes.Pop(); i++; continue; }
                            depths.Push(d - 1);
                        }
                    }
                    i++;
                    continue;
                }

                bool verbatim = mode == 2 || mode == 4;
                bool interpolated = mode == 3 || mode == 4;
                if (verbatim && c == '"' && next == '"') { sb.Append('"'); i += 2; continue; }
                if (!verbatim && c == '\\' && i + 1 < n) { sb.Append(next == 'n' ? '\n' : next); i += 2; continue; }
                if (c == '"' || (!verbatim && c == '\n'))
                {
                    result.Add(sb.ToString());
                    sb.Length = 0;
                    modes.Pop();
                    depths.Pop();
                    i++;
                    continue;
                }
                if (interpolated && c == '{')
                {
                    if (next == '{') { sb.Append('{'); i += 2; continue; }
                    modes.Push(5);
                    depths.Push(0);
                    i++;
                    continue;
                }
                if (interpolated && c == '}' && next == '}') { sb.Append('}'); i += 2; continue; }
                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        // ==================================================================
        // ③ プレイヤー設定
        // ==================================================================

        [MenuItem(Menu + "③ 製品向けのプレイヤー設定にする", priority = 62)]
        private static void ApplyPlayerSettings()
        {
            var log = new StringBuilder();
            if (!PlayerSettings.resizableWindow) { PlayerSettings.resizableWindow = true; log.AppendLine("・ウィンドウのふちをドラッグして大きさを変えられるようにした（比率がずれても黒い帯で 16:9 を保つ）"); }
            if (PlayerSettings.fullScreenMode != FullScreenMode.FullScreenWindow) { PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow; log.AppendLine("・最初の起動はボーダーレスの全画面にした"); }
            if (!PlayerSettings.defaultIsNativeResolution) { PlayerSettings.defaultIsNativeResolution = true; log.AppendLine("・全画面ではモニターの解像度そのままにした"); }
            if (PlayerSettings.defaultScreenWidth != 1600 || PlayerSettings.defaultScreenHeight != 900)
            {
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                log.AppendLine("・ウィンドウにしたときの最初の大きさを 1600×900 にした");
            }
            if (!PlayerSettings.allowFullscreenSwitch) { PlayerSettings.allowFullscreenSwitch = true; log.AppendLine("・Alt+Enter で全画面とウィンドウを切り替えられるようにした"); }
            if (!PlayerSettings.forceSingleInstance) { PlayerSettings.forceSingleInstance = true; log.AppendLine("・ゲームを2つ同時に起動できないようにした（同じセーブに2つが書いて壊すのを防ぐ）"); }

            // ビルドに入れるシーン：アトリエだけ（調合画面だけのシーン・サンプルは入れない）
            if (File.Exists(AtelierScene)) Brew2DBuilder.AddToBuildSettings(AtelierScene, true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool sceneChanged = false;
            foreach (EditorBuildSettingsScene s in scenes)
            {
                bool want = s.path == AtelierScene;
                if (s.enabled != want)
                {
                    s.enabled = want;
                    sceneChanged = true;
                    log.AppendLine((want ? "・ビルドに入れる：" : "・ビルドから外す（一覧には残す）：") + s.path);
                }
            }
            if (sceneChanged) EditorBuildSettings.scenes = scenes.ToArray();

            if (log.Length == 0) log.AppendLine("変えるところはありませんでした（もう製品向けの設定です）");
            log.AppendLine();
            log.AppendLine("自分で決めて入れるところ（Edit → Project Settings → Player）：");
            log.AppendLine($"・Company Name：{PlayerSettings.companyName}{(PlayerSettings.companyName == "DefaultCompany" ? "（← サークル名・屋号などに）" : "")}");
            log.AppendLine($"・Product Name：{PlayerSettings.productName}（ウィンドウの題名になる）");
            log.AppendLine($"・Version：{PlayerSettings.bundleVersion}");
            log.AppendLine("※ Company Name と Product Name はセーブの保存場所の名前にもなるので、発売前に決めて、その後は変えないこと");
            AssetDatabase.SaveAssets();
            Debug.Log("[プレイヤー設定] " + log);
            EditorUtility.DisplayDialog("製品向けのプレイヤー設定", log.ToString(), "OK");
        }

        // ==================================================================
        // ④ 通しテスト
        // ==================================================================

        [MenuItem(Menu + "④ 通しテストを実行（自動で一通り遊ぶ）", priority = 63)]
        private static void RunAutoTest()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("通しテスト", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!File.Exists(AtelierScene))
            {
                EditorUtility.DisplayDialog("通しテスト", AtelierScene + " がありません（「アトリエを作る」を先に実行）。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(AtelierScene);
            SessionState.SetBool(AtelierAutoTest2D.SessionKey, true);
            Debug.Log("[自動テスト] 再生を始めます（1〜2 分）。終わると自動で止まり、記録と画面写真が AutoTestShots2D/ に入ります。テスト中はゲーム画面をマウスで触らないでください。");
            EditorApplication.isPlaying = true;
        }

        // ==================================================================
        // ⑤ ビルド
        // ==================================================================

        [MenuItem(Menu + "⑤ Windows 版をビルドして確認", priority = 64)]
        private static void BuildWindows()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("ビルド", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var log = new StringBuilder();
            int problems = 0;
            int notes = 0;
            log.AppendLine($"Windows 版のビルド確認 {System.DateTime.Now:yyyy/MM/dd HH:mm}（Unity {Application.unityVersion}）");
            log.AppendLine();
            log.AppendLine("■ ビルドの前のチェック");
            PreCheck(log, ref problems, ref notes);

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0)
            {
                problems++;
                log.AppendLine("× ビルドに入れるシーンがない（③ を実行）");
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                problems++;
                log.AppendLine("× Windows のビルド機能が入っていない（Unity Hub → インストール → 歯車 → モジュールを加える → Windows Build Support）");
            }

            string dir = Path.Combine(ProjectRoot, BuildDir);
            string exe = Path.Combine(dir, ExeName);
            BuildReport report = null;
            if (scenes.Count > 0 && BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                log.AppendLine();
                log.AppendLine("■ ビルド");
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
                BuildSummary sum = report.summary;
                log.AppendLine($"結果：{(sum.result == BuildResult.Succeeded ? "成功" : sum.result.ToString())} ／ エラー {sum.totalErrors} ／ 警告 {sum.totalWarnings} ／ 大きさ {sum.totalSize / (1024f * 1024f):0.0} MB ／ かかった時間 {sum.totalTime.TotalSeconds:0} 秒");
                if (sum.result != BuildResult.Succeeded) problems++;
                int shown = 0;
                foreach (BuildStep step in report.steps)
                {
                    foreach (BuildStepMessage m in step.messages)
                    {
                        if (m.type != LogType.Error && m.type != LogType.Exception && m.type != LogType.Warning) continue;
                        if (shown++ >= 60) continue;
                        log.AppendLine((m.type == LogType.Warning ? "△ " : "× ") + m.content.Trim());
                    }
                }
                if (sum.result == BuildResult.Succeeded)
                {
                    int copied = CopyLicenses(dir);
                    log.AppendLine(copied > 0 ? $"フォントのライセンスの文 {copied} 個を Licenses/ に入れました" : "△ 同梱するライセンスの文がない（Assets/CustomAssets/Fonts に OFL.txt）");
                    if (copied == 0) notes++;
                    log.AppendLine("書き出し先：" + exe);
                }
            }

            log.AppendLine();
            log.AppendLine(problems == 0 ? $"＝＝ 問題なし（気にしておくこと {notes} 件）＝＝" : $"＝＝ 問題 {problems} 件・気にしておくこと {notes} 件 ＝＝");
            string logPath = WriteLog("claude_build_check.txt", log.ToString());
            Debug.Log("[ビルド確認] " + log);
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            EditorUtility.DisplayDialog("Windows 版のビルド確認",
                (ok ? "ビルドできました。" : "ビルドできませんでした。") + $"\n問題 {problems} 件・気にしておくこと {notes} 件\n\nくわしくは {logPath}", "OK");
            if (ok) EditorUtility.RevealInFinder(exe);
        }

        private static void PreCheck(StringBuilder log, ref int problems, ref int notes)
        {
            if (GraphicsSettings.defaultRenderPipeline == null) { problems++; log.AppendLine("× 描画の設定（URP）が入っていない（② 2D URP 環境をセットアップ）"); }
            else log.AppendLine("○ 描画の設定：" + GraphicsSettings.defaultRenderPipeline.name);

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            string first = null;
            var others = new List<string>();
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (!s.enabled) continue;
                if (first == null) first = s.path;
                else others.Add(s.path);
            }
            if (first != AtelierScene) { problems++; log.AppendLine($"× 最初のシーンがアトリエではない（{first ?? "なし"}）。③ を実行"); }
            else log.AppendLine("○ 最初のシーン：" + first);
            if (others.Count > 0) { notes++; log.AppendLine("△ ほかのシーンも入っている（使わないなら ③ で外せる）：" + string.Join("、", others)); }

            UISkin skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin == null || skin.font == null) { problems++; log.AppendLine("× UI のフォントが OS のフォントのまま（Mac・Steam Deck では日本語が出ない）。① を実行"); }
            else
            {
                log.AppendLine("○ フォント：" + skin.font.name + (skin.fontBold != null ? " ／ 太字 " + skin.fontBold.name : ""));
                if (!HasLicense()) { problems++; log.AppendLine("× フォントのライセンスの文（OFL.txt）が " + FontDir + " にない"); }
                GlyphReport glyphs = CheckGlyphs(skin.font, skin.fontBold);
                if (glyphs.Missing > 0) { notes++; log.AppendLine("△ フォントにない字がある（② でくわしく）"); }
                else log.AppendLine("○ ゲームで使う字は、全部フォントにある");
            }

            if (PlayerSettings.companyName == "DefaultCompany") { notes++; log.AppendLine("△ Company Name が DefaultCompany のまま（セーブの保存場所の名前になる。発売前に決める）"); }
            log.AppendLine($"・Product Name：{PlayerSettings.productName} ／ Version：{PlayerSettings.bundleVersion}");
            if (!PlayerSettings.resizableWindow || !PlayerSettings.forceSingleInstance) { notes++; log.AppendLine("△ ウィンドウの大きさ変更・二重起動の防止がまだ（③ を実行）"); }
            log.AppendLine($"・色の扱い：{PlayerSettings.colorSpace} ／ スクリプト：{PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)}");
        }

        private static int CopyLicenses(string buildDir)
        {
            if (!Directory.Exists(FontDir)) return 0;
            string dest = Path.Combine(buildDir, "Licenses");
            int count = 0;
            foreach (string f in Directory.GetFiles(FontDir, "*.txt"))
            {
                Directory.CreateDirectory(dest);
                File.Copy(f, Path.Combine(dest, "Font_" + Path.GetFileName(f)), true);
                count++;
            }
            return count;
        }

        private static string WriteLog(string fileName, string text)
        {
            string dir = Path.Combine(ProjectRoot, "Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, text, new UTF8Encoding(true));
            return path;
        }
    }
}
