using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>セーブデータの中身（JSONで保存）</summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>今のセーブデータの版。古い版を読んだときは、足りない物を補う（素材の在庫など）</summary>
        /// 版7：1画面版（お金・相談・仕入れをなくした。調合机に届いた素材だけを覚える）
        /// 版8：瓶に詰めた回数を覚える。クリアはなくした（いつまでも遊べる）
        /// 項目を足すだけなら版は上げなくてよい（古いセーブには、その項目の初期値が入る）。
        /// 意味の変わる変更（単位・ID の付け方など）をしたときだけ版を上げ、GameFlow.ContinueGame で古い版を読みかえる。
        public const int CurrentVersion = 8;

        public int version = CurrentVersion;
        public string savedAt = "";
        /// <summary>このセーブを書いたゲームの版（Application.version。不具合の問い合わせのときの手がかり）</summary>
        public string appVersion = "";
        /// <summary>これまでに瓶に詰めた回数（版8から）</summary>
        public int brewCount;
        public List<PotionStack> potions = new List<PotionStack>();
        /// <summary>版6までの素材の在庫（読み込みのためだけ。量があった素材は、机に届いていることにする）</summary>
        public List<IngredientStockEntry> ingredients = new List<IngredientStockEntry>();
        /// <summary>調合机に届いている素材の ID</summary>
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

    /// <summary>
    /// セーブ・ロード（Application.persistentDataPath/save.json）。
    /// ・書くとき：別のファイル（.tmp）に書いてディスクまで届けてから、本体と入れ替える。ひとつ前のセーブは .bak に残る
    ///   （書いている途中で電源が落ちた・強制終了しても、本体か .bak のどちらかは必ず無事）
    /// ・読むとき：本体が読めなければ .bak を使う。読めなかった本体は .corrupt.json として残す（消さない）
    ///   読んだあと、欠けた項目・壊れた数字（NaN など）・空の要素を直してから渡す（SaveRepair）
    /// ・新しい版のゲームで書かれたセーブを読んだときは、上書きする前に、元の形のまま .v版.json として残す
    /// ・「はじめから」で消すときも、それまでのセーブを .prev.json として1つだけ残す（間違えて押したときに手で戻せる）
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>保存先のファイル名（テストなどで別の名前にして、本来のセーブを消さない）</summary>
        public static string Slot = "save";

        private static string Folder => Application.persistentDataPath;
        private static string FilePath => Path.Combine(Folder, Slot + ".json");
        private static string TempPath => FilePath + ".tmp";
        private static string BackupPath => FilePath + ".bak";
        private static string PreviousPath => SidePath("prev");

        /// <summary>開発中（エディタ・開発ビルド）は読みやすい形で書く。製品版は小さく書く</summary>
        private static bool Pretty => Application.isEditor || Debug.isDebugBuild;

        /// <summary>最後のセーブが失敗したか（ディスクがいっぱい・書き込み禁止など）</summary>
        public static bool LastSaveFailed { get; private set; }
        /// <summary>最後に失敗したときの理由（ログ用）</summary>
        public static string LastError { get; private set; } = "";

        public static bool Exists => File.Exists(FilePath) || File.Exists(BackupPath);

        /// <summary>セーブする。うまくいったら true（失敗しても例外は外に出さない）</summary>
        public static bool Save(SaveData data)
        {
            if (data == null) return false;
            try
            {
                data.version = SaveData.CurrentVersion;
                data.savedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                data.appVersion = Application.version;
                string json = JsonUtility.ToJson(data, Pretty);
                if (string.IsNullOrEmpty(json) || json.Length < 2) throw new IOException("セーブの中身を作れませんでした");

                Directory.CreateDirectory(Folder);
                WriteDurable(TempPath, json);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(TempPath, FilePath, BackupPath, true);
                    }
                    catch (Exception)
                    {
                        // File.Replace が使えない場所（ネットワークドライブなど）・ほかのソフトが一瞬ファイルをつかんでいたとき
                        File.Copy(FilePath, BackupPath, true);
                        File.Copy(TempPath, FilePath, true);
                        File.Delete(TempPath);
                    }
                }
                else
                {
                    File.Move(TempPath, FilePath);
                }
                LastSaveFailed = false;
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastSaveFailed = true;
                LastError = e.Message;
                Debug.LogWarning("[SaveSystem] セーブに失敗しました: " + e.Message);
                return false;
            }
        }

        /// <summary>セーブを読む（なければ・どれも読めなければ null）。中身は直してから返す</summary>
        public static SaveData Load()
        {
            SaveData data = Read(FilePath, out bool mainBroken);
            if (data == null)
            {
                if (mainBroken) KeepCopy("corrupt");
                data = Read(BackupPath, out _);
                if (data != null) Debug.LogWarning("[SaveSystem] セーブデータが読めなかったので、ひとつ前のセーブ（.bak）を使います");
            }
            if (data == null) return null;

            if (data.version > SaveData.CurrentVersion)
            {
                // 新しい版のゲームで書かれたセーブ。知らない項目は読めないので、この版で上書きする前に、元の形のまま残しておく
                Debug.LogWarning($"[SaveSystem] このゲームより新しい版（{data.version}）のセーブです。読める所だけ読みます（元のファイルは残します）");
                KeepCopyOnce("v" + data.version);
            }
            int fixes = SaveRepair.Repair(data);
            if (fixes > 0) Debug.LogWarning($"[SaveSystem] セーブデータの欠けた所・壊れた所を {fixes} か所直して読み込みました");
            return data;
        }

        private static SaveData Read(string path, out bool broken)
        {
            broken = false;
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    broken = true;
                    return null;
                }
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.version <= 0)
                {
                    broken = true;
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogWarning("[SaveSystem] ロードに失敗しました（" + Path.GetFileName(path) + "）: " + e.Message);
                return null;
            }
        }

        /// <summary>「はじめから」：セーブを消す（それまでのセーブは .prev.json に1つだけ残す）</summary>
        public static void Delete()
        {
            try
            {
                string source = File.Exists(FilePath) ? FilePath : File.Exists(BackupPath) ? BackupPath : null;
                if (source != null) File.Copy(source, PreviousPath, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] 前のセーブを残せませんでした: " + e.Message);
            }
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

        /// <summary>今のセーブ（本体。なければ .bak）を、名前に印をつけて残す（読み込みに失敗したときの調査用。同じ印は上書き）</summary>
        public static void KeepCopy(string tag)
        {
            CopySide(tag, true);
        }

        private static void KeepCopyOnce(string tag)
        {
            CopySide(tag, false);
        }

        private static void CopySide(string tag, bool overwrite)
        {
            try
            {
                string source = File.Exists(FilePath) ? FilePath : File.Exists(BackupPath) ? BackupPath : null;
                if (source == null) return;
                string target = SidePath(tag);
                if (!overwrite && File.Exists(target)) return;
                File.Copy(source, target, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] セーブの控えを作れませんでした: " + e.Message);
            }
        }

        private static string SidePath(string tag) => Path.Combine(Folder, Slot + "." + tag + ".json");

        /// <summary>書いた内容を、OS のキャッシュで止めずにディスクまで届ける（電源が落ちても、入れ替えたあとの本体が空にならない）</summary>
        private static void WriteDurable(string path, string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        // エディタで「ドメインの再読み込みなし」で再生したときも、テストの保存先が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSlot()
        {
            Slot = "save";
            LastSaveFailed = false;
            LastError = "";
        }
    }

    /// <summary>
    /// 読み込んだセーブの「欠けた所・壊れた所」を直す（手で書き換えたセーブ・古い版・途中で壊れたファイルでも、ゲームが止まらないように）。
    /// 空のリスト・null の要素・NaN や無限大の数字・範囲外の値・空の ID を直す。直した数を返す
    /// </summary>
    internal static class SaveRepair
    {
        private static int fixes;

        public static int Repair(SaveData d)
        {
            fixes = 0;
            if (d == null) return 0;
            d.savedAt = Str(d.savedAt);
            d.appVersion = Str(d.appVersion);
            if (d.brewCount < 0) { d.brewCount = 0; fixes++; }

            d.potions = Items(d.potions);
            foreach (PotionStack p in d.potions) Potion(p);

            d.ingredients = Items(d.ingredients);
            foreach (IngredientStockEntry e in d.ingredients) Stock(e);
            d.flask = Items(d.flask);
            foreach (IngredientStockEntry e in d.flask) Stock(e);

            d.unlockedIngredients = Ids(d.unlockedIngredients, true);

            if (d.flaskState == null) { d.flaskState = new FlaskSaveData(); fixes++; }
            Flask(d.flaskState);

            d.liquids = Items(d.liquids);
            foreach (LiquidRecord r in d.liquids) Liquid(r);

            d.notes = Items(d.notes);
            fixes += d.notes.RemoveAll(n => string.IsNullOrEmpty(n.id));
            d.knownReactions = Ids(d.knownReactions, true);
            return fixes;
        }

        private static void Potion(PotionStack p)
        {
            p.potionId = Str(p.potionId);
            p.potionName = Str(p.potionName);
            p.description = Str(p.description);
            p.baseLiquidId = Str(p.baseLiquidId);
            p.liquidId = Str(p.liquidId);
            p.specialPotionId = Str(p.specialPotionId);
            p.color = Col(p.color, Color.white);
            p.ml = Mathf.Clamp(Num(p.ml), 0f, 10000000f);
            if (p.count < 0) { p.count = 0; fixes++; }
            if (p.stars < 0) { p.stars = 0; fixes++; }
            p.effects = Texts(p.effects);
            p.attributes = Nums(p.attributes);
            if (!Enum.IsDefined(typeof(LiquidKind), p.kind)) { p.kind = LiquidKind.Unknown; fixes++; }
            if (!Enum.IsDefined(typeof(BrewOutcome), p.outcome)) { p.outcome = BrewOutcome.Generated; fixes++; }
        }

        private static void Stock(IngredientStockEntry e)
        {
            e.id = Str(e.id);
            e.amount = Mathf.Max(0f, Num(e.amount));
        }

        private static void Liquid(LiquidRecord r)
        {
            r.id = Str(r.id);
            r.fingerprint = Str(r.fingerprint);
            r.autoName = Str(r.autoName);
            r.customName = Str(r.customName);
            r.specialPotionId = Str(r.specialPotionId);
            r.formName = Str(r.formName);
            r.baseLiquidId = Str(r.baseLiquidId);
            r.description = Str(r.description);
            r.color = Col(r.color, Color.white);
            r.solventColor = Col(r.solventColor, new Color(0f, 0f, 0f, 0f));
            r.attributes = Nums(r.attributes);
            if (r.stars < 0) { r.stars = 0; fixes++; }
            if (r.madeCount < 0) { r.madeCount = 0; fixes++; }
            if (!Enum.IsDefined(typeof(LiquidKind), r.kind)) { r.kind = LiquidKind.Unknown; fixes++; }
            r.traits = Traits(r.traits);
            r.effects = Texts(r.effects);
            r.ingredientIds = Ids(r.ingredientIds, false);
            r.parentLiquidIds = Ids(r.parentLiquidIds, false);
            r.reactionIds = Ids(r.reactionIds, false);
            r.recipes = Items(r.recipes);
            foreach (ProcessRecipe recipe in r.recipes)
            {
                recipe.steps = Items(recipe.steps);
                foreach (ProcessStep s in recipe.steps) Step(s);
            }
        }

        private static void Flask(FlaskSaveData f)
        {
            f.volumeMl = Mathf.Max(0f, Num(f.volumeMl));
            f.temperature = Mathf.Clamp(Num(f.temperature, 20f), -50f, 500f);
            f.heatLevel = Mathf.Clamp01(Num(f.heatLevel));
            f.time = Mathf.Max(0f, Num(f.time));
            f.totalTurns = Mathf.Max(0f, Num(f.totalTurns));
            f.evaporatedMl = Mathf.Max(0f, Num(f.evaporatedMl));
            f.globalAmplifyPool = Num(f.globalAmplifyPool);
            f.amplifierPool = Nums(f.amplifierPool);
            f.portions = Items(f.portions);
            foreach (FlaskSaveData.PortionData p in f.portions)
            {
                p.ingredientId = Str(p.ingredientId);
                p.sourceId = Str(p.sourceId);
                p.sourceName = Str(p.sourceName);
                p.formName = Str(p.formName);
                p.color = Col(p.color, Color.white);
                p.units = Mathf.Max(0f, Num(p.units));
                p.undissolved = Mathf.Clamp(Num(p.undissolved), 0f, p.units);
                p.essence = Nums(p.essence);
                p.quality = Mathf.Max(0f, Num(p.quality, 1f));
                p.activation = Mathf.Clamp01(Num(p.activation));
                p.turnsSinceAdded = Mathf.Max(0f, Num(p.turnsSinceAdded));
                p.clockwiseUsed = Mathf.Max(0f, Num(p.clockwiseUsed));
                p.clockwiseOthersUsed = Mathf.Max(0f, Num(p.clockwiseOthersUsed));
                p.counterUsed = Mathf.Max(0f, Num(p.counterUsed));
                p.counterPrimaryUsed = Mathf.Max(0f, Num(p.counterPrimaryUsed));
            }
            f.intrinsicTraits = Traits(f.intrinsicTraits);
            f.carriedTraitMl = Traits(f.carriedTraitMl);
            f.parentLiquidIds = Ids(f.parentLiquidIds, false);
            f.carriedIngredientIds = Ids(f.carriedIngredientIds, false);
            f.firedReactions = Ids(f.firedReactions, true);
            f.log = Items(f.log);
            foreach (ProcessStep s in f.log) Step(s);
        }

        private static void Step(ProcessStep s)
        {
            s.sourceId = Str(s.sourceId);
            s.sourceName = Str(s.sourceName);
            s.amount = Num(s.amount);
            s.temperature = Num(s.temperature, 20f);
            s.endTemperature = Num(s.endTemperature, 20f);
            s.time = Mathf.Max(0f, Num(s.time));
        }

        // ------------------------------------------------------------------

        private static List<T> Items<T>(List<T> list) where T : class
        {
            if (list == null) { fixes++; return new List<T>(); }
            fixes += list.RemoveAll(x => x == null);
            return list;
        }

        private static List<string> Texts(List<string> list)
        {
            if (list == null) { fixes++; return new List<string>(); }
            fixes += list.RemoveAll(s => s == null);
            return list;
        }

        private static List<string> Ids(List<string> list, bool unique)
        {
            if (list == null) { fixes++; return new List<string>(); }
            fixes += list.RemoveAll(string.IsNullOrEmpty);
            if (!unique) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            fixes += list.RemoveAll(id => !seen.Add(id));
            return list;
        }

        private static List<LiquidTrait> Traits(List<LiquidTrait> list)
        {
            list = Items(list);
            fixes += list.RemoveAll(t => string.IsNullOrEmpty(t.id));
            foreach (LiquidTrait t in list) t.strength = Num(t.strength);
            return list;
        }

        private static float[] Nums(float[] values)
        {
            if (values == null) { fixes++; return new float[0]; }
            for (int i = 0; i < values.Length; i++) values[i] = Num(values[i]);
            return values;
        }

        private static float Num(float v, float fallback = 0f)
        {
            if (!float.IsNaN(v) && !float.IsInfinity(v)) return v;
            fixes++;
            return fallback;
        }

        private static Color Col(Color c, Color fallback)
        {
            if (Ok(c.r) && Ok(c.g) && Ok(c.b) && Ok(c.a)) return c;
            fixes++;
            return fallback;
        }

        private static bool Ok(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static string Str(string s)
        {
            if (s != null) return s;
            fixes++;
            return "";
        }
    }
}
