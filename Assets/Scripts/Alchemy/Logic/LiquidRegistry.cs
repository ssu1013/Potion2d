using System;
using System.Collections.Generic;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// これまでに作った液体の一覧（遊ぶと増えるデータ。セーブに入る）。
    /// 同じ結果の液体は同じ記録にまとめ、作った回数とちがう作り方（最大3つ）を足していく。
    /// レシピ本・瓶の在庫・相談の判定は、ここの記録を ID で参照する。
    /// </summary>
    public sealed class LiquidRegistry
    {
        private readonly Dictionary<string, LiquidRecord> byId = new Dictionary<string, LiquidRecord>(StringComparer.Ordinal);
        private readonly List<LiquidRecord> ordered = new List<LiquidRecord>();

        /// <summary>新しい液体ができたとき（レシピ本の「新発見」演出など）</summary>
        public event Action<LiquidRecord> Discovered;
        /// <summary>名前などが変わったとき（作った回数・見た印・相談の記録も）</summary>
        public event Action<LiquidRecord> Changed;
        /// <summary>プレイヤーが名前を付けたとき（棚の瓶・手に持っている瓶の名前を合わせる）</summary>
        public event Action<LiquidRecord> Renamed;

        /// <summary>付けられる名前の長さ（文字数）</summary>
        public const int MaxNameLength = 16;

        public IReadOnlyList<LiquidRecord> All => ordered;
        public int Count => ordered.Count;

        public LiquidRecord Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return byId.TryGetValue(id, out LiquidRecord r) ? r : null;
        }

        /// <summary>
        /// 作った液体を登録する。同じ液体がもうあればそれを返し（作った回数を増やし、ちがう作り方なら足す）、
        /// なければ新しく登録する。isNew = 初めて作った液体か
        /// </summary>
        public LiquidRecord Register(LiquidRecord fresh, out bool isNew)
        {
            isNew = false;
            if (fresh == null) return null;
            if (byId.TryGetValue(fresh.id, out LiquidRecord existing))
            {
                existing.madeCount++;
                if (fresh.recipes.Count > 0) existing.AddRecipe(fresh.recipes[0]);
                if (Changed != null) Changed(existing);
                return existing;
            }
            fresh.madeCount = Math.Max(1, fresh.madeCount);
            fresh.unread = true; // レシピ本に「新しいページ」として出す
            byId[fresh.id] = fresh;
            ordered.Add(fresh);
            isNew = true;
            if (Discovered != null) Discovered(fresh);
            return fresh;
        }

        /// <summary>記録がまだなければ足す（古いセーブの瓶の記録を作るとき。作った回数は数えない）</summary>
        public bool AddIfMissing(LiquidRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.id) || byId.ContainsKey(record.id)) return false;
            byId[record.id] = record;
            ordered.Add(record);
            return true;
        }

        /// <summary>プレイヤーが名前を付ける（空にすると自動の名前に戻る）</summary>
        public bool Rename(string id, string name)
        {
            LiquidRecord r = Get(id);
            if (r == null) return false;
            string clean = CleanName(name);
            // 自動の名前と同じなら「名前を付けていない」ことにする（あとで自動の名前が変わっても付いていく）
            r.customName = clean == r.autoName ? "" : clean;
            if (Renamed != null) Renamed(r);
            if (Changed != null) Changed(r);
            return true;
        }

        /// <summary>名前に使えない文字（改行・表示を崩すタグの記号）を取り除き、長さをそろえる</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '<' || c == '>' || c == '\n' || c == '\r' || c == '\t') continue;
                sb.Append(c);
            }
            string clean = sb.ToString().Trim();
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength) : clean;
        }

        /// <summary>レシピ本でまだ見ていないページの数</summary>
        public int UnreadCount
        {
            get
            {
                int n = 0;
                foreach (LiquidRecord r in ordered) if (r.unread) n++;
                return n;
            }
        }

        /// <summary>レシピ本で見た（「新しいページ」の印を消す）。消したら true</summary>
        public bool MarkRead(string id)
        {
            LiquidRecord r = Get(id);
            if (r == null || !r.unread) return false;
            r.unread = false;
            if (Changed != null) Changed(r);
            return true;
        }

        /// <summary>この液体で相談に乗った（taught = 作り方を教えた。false なら瓶を分け与えた）</summary>
        public void NoteHelped(string id, string who, bool taught)
        {
            LiquidRecord r = Get(id);
            if (r == null || string.IsNullOrEmpty(who)) return;
            if (r.sharedWith == null) r.sharedWith = new List<string>();
            if (r.taughtTo == null) r.taughtTo = new List<string>();
            List<string> list = taught ? r.taughtTo : r.sharedWith;
            if (!list.Contains(who)) list.Add(who);
            if (Changed != null) Changed(r);
        }

        /// <summary>この液体を混ぜて作った液体（系譜の「ここから生まれた液体」）</summary>
        public void CollectChildren(string id, List<LiquidRecord> result)
        {
            result.Clear();
            if (string.IsNullOrEmpty(id)) return;
            foreach (LiquidRecord r in ordered)
            {
                if (r.parentLiquidIds != null && r.parentLiquidIds.Contains(id)) result.Add(r);
            }
        }

        /// <summary>記録された順番（レシピ本の No.。1 から。なければ 0）</summary>
        public int NumberOf(LiquidRecord record)
        {
            int i = ordered.IndexOf(record);
            return i + 1;
        }

        public void Clear()
        {
            byId.Clear();
            ordered.Clear();
        }

        public List<LiquidRecord> Export()
        {
            var list = new List<LiquidRecord>(ordered.Count);
            foreach (LiquidRecord r in ordered) list.Add(r.Clone());
            return list;
        }

        public void Import(List<LiquidRecord> saved)
        {
            Clear();
            if (saved == null) return;
            foreach (LiquidRecord r in saved)
            {
                if (r == null || string.IsNullOrEmpty(r.id) || byId.ContainsKey(r.id)) continue;
                if (r.sharedWith == null) r.sharedWith = new List<string>();
                if (r.taughtTo == null) r.taughtTo = new List<string>();
                byId[r.id] = r;
                ordered.Add(r);
            }
        }
    }
}
