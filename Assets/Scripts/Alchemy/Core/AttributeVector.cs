using System;
using System.Collections.Generic;
using System.Text;

namespace PotionFrontier.Alchemy
{
    /// <summary>
    /// 全属性の値をまとめて持つ計算用のベクトル（実行時専用。アセットには保存しない）。
    /// 素材の属性はインスペクターで扱いやすい「リスト形式」で保存し、計算時にこの形へ変換する。
    /// </summary>
    public sealed class AttributeVector
    {
        /// <summary>属性の数（enum の最大値 + 1）</summary>
        public static readonly int Count = CalculateCount();

        private readonly float[] values = new float[Count];

        public float this[PotionAttribute attribute]
        {
            get { return values[(int)attribute]; }
            set { values[(int)attribute] = value; }
        }

        public void Add(PotionAttribute attribute, float amount)
        {
            values[(int)attribute] += amount;
        }

        public void Multiply(PotionAttribute attribute, float multiplier)
        {
            values[(int)attribute] *= multiplier;
        }

        /// <summary>打ち消しでマイナスになった属性を 0 に揃える</summary>
        public void ClampNegativeToZero()
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0f) values[i] = 0f;
            }
        }

        /// <summary>総効力 ＝ プラスの属性値の合計。ランクや成功・失敗の判定に使う</summary>
        public float TotalPotency
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] > 0f) sum += values[i];
                }
                return sum;
            }
        }

        /// <summary>総効力に占めるその属性の割合（0〜1）</summary>
        public float GetShare(PotionAttribute attribute)
        {
            float total = TotalPotency;
            if (total <= 0f) return 0f;
            return Math.Max(0f, this[attribute]) / total;
        }

        /// <summary>
        /// 値が大きい順に並べた属性一覧（0以下は含まない）。
        /// 同じ値の場合は enum の定義順が先のものを上位にする（結果が毎回同じになるように）。
        /// </summary>
        public List<PotionAttribute> GetRankedAttributes()
        {
            var list = new List<PotionAttribute>(Count);
            for (int i = 0; i < Count; i++)
            {
                if (values[i] > 0f) list.Add((PotionAttribute)i);
            }

            list.Sort((a, b) =>
            {
                int byValue = values[(int)b].CompareTo(values[(int)a]);
                return byValue != 0 ? byValue : ((int)a).CompareTo((int)b);
            });
            return list;
        }

        /// <summary>その属性が最も高い（＝主属性）か。同値のときは GetRankedAttributes と同じ規則で判定</summary>
        public bool IsDominant(PotionAttribute attribute)
        {
            int index = (int)attribute;
            float value = values[index];
            if (value <= 0f) return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (i == index) continue;
                if (values[i] > value) return false;
                if (values[i] == value && i < index) return false;
            }
            return true;
        }

        /// <summary>保存用に配列へ書き出す</summary>
        public float[] ToArray()
        {
            var copy = new float[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        /// <summary>保存した配列から作る（長さが違っても読める分だけ読む）</summary>
        public static AttributeVector FromArray(float[] source)
        {
            var v = new AttributeVector();
            if (source == null) return v;
            Array.Copy(source, v.values, Math.Min(source.Length, v.values.Length));
            return v;
        }

        public AttributeVector Clone()
        {
            var copy = new AttributeVector();
            Array.Copy(values, copy.values, values.Length);
            return copy;
        }

        /// <summary>「回復 40, 火 18」のような文字列にする（0の属性は省略）</summary>
        public string ToString(Func<PotionAttribute, string> nameSelector)
        {
            var ranked = GetRankedAttributes();
            if (ranked.Count == 0) return "なし";

            var sb = new StringBuilder();
            for (int i = 0; i < ranked.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                PotionAttribute a = ranked[i];
                sb.Append(nameSelector != null ? nameSelector(a) : a.ToString());
                sb.Append(' ').Append(values[(int)a].ToString("0.#"));
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return ToString(null);
        }

        private static int CalculateCount()
        {
            // 連番でなくても範囲外アクセスしないよう「最大値 + 1」を要素数にする
            int max = -1;
            foreach (int v in Enum.GetValues(typeof(PotionAttribute)))
            {
                if (v > max) max = v;
            }
            return max + 1;
        }
    }
}
