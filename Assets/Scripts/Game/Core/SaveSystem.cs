using System;
using System.Collections.Generic;
using System.IO;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>セーブデータの中身（JSONで保存）</summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>今のセーブデータの版。古い版を読んだときは、足りない物を補う（素材の在庫など）</summary>
        public const int CurrentVersion = 6;

        public int version = CurrentVersion;
        public string savedAt = "";
        public int gold;
        /// <summary>解決した相談の数</summary>
        public int completedQuests;
        /// <summary>瓶を分け与えて解決した数（版5から）</summary>
        public int sharedCount;
        /// <summary>作り方を教えて解決した数（版5から）</summary>
        public int taughtCount;
        /// <summary>解決した相談の ID</summary>
        public List<string> completedQuestIds = new List<string>();
        public bool gameCleared;
        public List<PotionStack> potions = new List<PotionStack>();
        /// <summary>素材の在庫（版2から。版4からは使った分だけ減る素材のみ）</summary>
        public List<IngredientStockEntry> ingredients = new List<IngredientStockEntry>();
        /// <summary>取引済み（使い放題）の素材の ID（版4から）</summary>
        public List<string> unlockedIngredients = new List<string>();
        /// <summary>調合フラスコに入っている素材（版2のみ。版3からは flaskState）</summary>
        public List<IngredientStockEntry> flask = new List<IngredientStockEntry>();
        /// <summary>調合フラスコの中の状態（版3から：温度・溶け具合・工程の記録まで）</summary>
        public FlaskSaveData flaskState = new FlaskSaveData();
        /// <summary>これまでに作った液体（版3から）</summary>
        public List<LiquidRecord> liquids = new List<LiquidRecord>();
        /// <summary>素材ノート：素材ごとに見つけた性質（版6から）</summary>
        public List<IngredientNote> notes = new List<IngredientNote>();
        /// <summary>見つけた反応の ID（版6から）</summary>
        public List<string> knownReactions = new List<string>();
    }

    /// <summary>セーブ・ロード（Application.persistentDataPath/save.json）</summary>
    public static class SaveSystem
    {
        /// <summary>保存先のファイル名（自動テストは別の名前にして、本来のセーブを消さない）</summary>
        public static string Slot = "save";

        private static string FilePath => Path.Combine(Application.persistentDataPath, Slot + ".json");
        private static string TempPath => FilePath + ".tmp";
        private static string BackupPath => FilePath + ".bak";

        public static bool Exists => File.Exists(FilePath) || File.Exists(BackupPath);

        public static void Save(SaveData data)
        {
            try
            {
                data.savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                // 書いている途中で止まっても（電源が落ちた・強制終了など）壊れないよう、別のファイルに書いてから入れ替える。
                // ひとつ前のセーブは .bak に残し、本体が読めないときに使う
                File.WriteAllText(TempPath, JsonUtility.ToJson(data, true));
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(TempPath, FilePath, BackupPath);
                    }
                    catch (Exception)
                    {
                        File.Copy(FilePath, BackupPath, true);
                        File.Copy(TempPath, FilePath, true);
                        File.Delete(TempPath);
                    }
                }
                else
                {
                    File.Move(TempPath, FilePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] セーブに失敗しました: " + e.Message);
            }
        }

        public static SaveData Load()
        {
            SaveData data = Read(FilePath);
            if (data != null) return data;
            data = Read(BackupPath);
            if (data != null) Debug.LogWarning("[SaveSystem] セーブデータが読めなかったので、ひとつ前のセーブ（.bak）を使います");
            return data;
        }

        private static SaveData Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                return data != null && data.version > 0 ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] ロードに失敗しました（" + Path.GetFileName(path) + "）: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] セーブデータの削除に失敗しました: " + e.Message);
            }
        }

        // エディタで「ドメインの再読み込みなし」で再生したときも、自動テストの保存先が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSlot()
        {
            Slot = "save";
        }
    }
}
