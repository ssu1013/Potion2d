using System;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// 開発用の自動化（エディタ専用）。プロジェクト直下に置いた「合図ファイル」を見て動く。
    ///   Claude.refresh … アセットを再読み込み（コンパイル）
    ///   Claude.rebuild … 「アトリエを整える」を確認なしで実行（足りない物だけ作る。シーンがあれば触らない）
    ///   Claude.reset   … 「アトリエを作り直す」を確認なしで実行（シーンの控えを取ってから作り直す）
    ///   Claude.csvtest … 動作確認用のダミー素材 200 種類を CSV から読み込む（Assets/GameData/Alchemy/Ingredients/PerfTest）
    ///   Claude.csvclean… ダミー素材を消す
    ///   Claude.simtest … 調合の計算のテスト（結果は Logs/claude_simtest.txt）
    ///   Claude.play    … 再生を開始
    ///   Claude.stop    … 再生を止める
    /// また、コンパイル結果を Logs/claude_compile.txt、エラーやログを Logs/claude_console.txt に書き出す。
    /// 普段の開発では何もしない（合図ファイルがなければ動かない）。
    /// </summary>
    [InitializeOnLoad]
    public static class DevAutomation
    {
        private static double nextPoll;

        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string CompileLog => Path.Combine(Root, "Logs", "claude_compile.txt");
        private static string ConsoleLog => Path.Combine(Root, "Logs", "claude_console.txt");

        static DevAutomation()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Poll;
        }

        private static void OnCompilationStarted(object context)
        {
            SafeWrite(CompileLog, $"[{DateTime.Now:HH:mm:ss}] compile started\n", false);
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            var sb = new StringBuilder();
            int errors = 0;
            foreach (CompilerMessage m in messages)
            {
                if (m.type == CompilerMessageType.Error) errors++;
                if (m.type == CompilerMessageType.Error || m.message.Contains("PotionFrontier"))
                {
                    sb.Append(m.type).Append(": ").Append(m.file).Append('(').Append(m.line).Append(") ").AppendLine(m.message);
                }
            }
            SafeWrite(CompileLog, $"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(assemblyPath)}: errors={errors}\n{sb}", true);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            bool important = type == LogType.Error || type == LogType.Exception || type == LogType.Assert
                             || type == LogType.Warning || condition.StartsWith("[");
            if (!important) return;

            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").Append(type).Append(": ").AppendLine(condition);
            if (type == LogType.Error || type == LogType.Exception)
            {
                string[] lines = stackTrace.Split('\n');
                for (int i = 0; i < lines.Length && i < 6; i++) sb.Append("    ").AppendLine(lines[i]);
            }
            SafeWrite(ConsoleLog, sb.ToString(), true);
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlaying && Consume("Claude.stop"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] stop requested\n", true);
                EditorApplication.isPlaying = false;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (Consume("Claude.refresh"))
            {
                AssetDatabase.Refresh();
                return;
            }
            if (Consume("Claude.rebuild"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] prepare start\n", true);
                string summary = AtelierSceneBuilder.PrepareAutomated();
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] prepare done\n{summary}\n", true);
                return;
            }
            if (Consume("Claude.reset"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] reset start\n", true);
                string summary = AtelierSceneBuilder.RebuildAutomated();
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] reset done\n{summary}\n", true);
                return;
            }
            if (Consume("Claude.csvtest"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] csvtest: {PerfTestIngredients.Create(200)}\n", true);
                return;
            }
            if (Consume("Claude.simtest"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] simtest: {FlaskSimulationTests.Run()}\n", true);
                return;
            }
            if (Consume("Claude.csvclean"))
            {
                SafeWrite(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] csvclean: {PerfTestIngredients.Remove()}\n", true);
                return;
            }
            if (Consume("Claude.play"))
            {
                File.WriteAllText(ConsoleLog, $"[{DateTime.Now:HH:mm:ss}] play requested\n");
                EditorApplication.isPlaying = true;
            }
        }

        private static bool Consume(string flag)
        {
            string path = Path.Combine(Root, flag);
            if (!File.Exists(path)) return false;
            try { File.Delete(path); } catch (IOException) { return false; }
            return true;
        }

        private static void SafeWrite(string path, string text, bool append)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (append) File.AppendAllText(path, text);
                else File.WriteAllText(path, text);
            }
            catch (IOException)
            {
                // ログの書き込みに失敗しても開発作業は止めない
            }
        }
    }
}
