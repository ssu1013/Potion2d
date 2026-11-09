using System;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 瓶詰めした液体1種類分（同じ液体はまとめて量で数える）。セーブデータにもそのまま入る。
    /// フェーズ5からは液体の記録（LiquidRecord）から作る：potionId = liquidId = 液体のID、属性は 100ml あたりの濃さ。
    /// 量は ml で持つ。1本 = 100ml。満タンでない瓶（あまり）は「開けた瓶」として1本に数える。
    /// 手に持った瓶も同じ形（ml がその瓶の中身）。
    /// </summary>
    [Serializable]
    public class PotionStack
    {
        public const float BottleMl = 100f;
        /// <summary>これより少ない量は空とみなす</summary>
        public const float EmptyMl = 0.5f;

        public string potionId;
        public string potionName;
        public string description;
        public Color color = Color.white;
        public int stars;
        public BrewOutcome outcome;
        /// <summary>瓶の本数（ml から計算した表示用。版3までのセーブでは、これが量の元になる）</summary>
        public int count;
        /// <summary>中身の量（ml）。版4から</summary>
        public float ml;
        public List<string> effects = new List<string>();
        /// <summary>作ったときのベース液体のID（瓶のモデルを決めるのに使う）</summary>
        public string baseLiquidId = "";
        /// <summary>属性値（100ml あたりの濃さ。特別な薬の判定に使う。AttributeVector.ToArray の形）</summary>
        public float[] attributes = new float[0];
        /// <summary>液体の記録の ID（フェーズ5から。古いセーブの瓶は読み込み時に記録を作って入れる）</summary>
        public string liquidId = "";
        /// <summary>特別なポーションの ID（当てはまったときだけ）</summary>
        public string specialPotionId = "";
        public LiquidKind kind = LiquidKind.Potion;
        /// <summary>まだ見ていない（新しく瓶詰めした）。素材箱で選ぶか手に持つと消える</summary>
        public bool isNew;
        /// <summary>最後に瓶詰めした順番（大きいほど新しい。新しい順に並べる）</summary>
        public int addedOrder;

        public AttributeVector Attributes => AttributeVector.FromArray(attributes);

        /// <summary>満タンの瓶（100ml）か。分け与えるのは満タンの瓶</summary>
        public bool IsFullBottle => ml >= BottleMl - EmptyMl;

        /// <summary>瓶の本数（あまりは開けた瓶として1本）</summary>
        public int Bottles => ml < EmptyMl ? 0 : Mathf.CeilToInt(ml / BottleMl - 0.005f);

        /// <summary>満タンの瓶の本数</summary>
        public int FullBottles => Mathf.FloorToInt((ml + EmptyMl) / BottleMl);

        /// <summary>開けた瓶の量（満タンでないあまり。なければ 0）</summary>
        public float OpenedMl
        {
            get
            {
                float rest = ml - FullBottles * BottleMl;
                return rest < EmptyMl ? 0f : rest;
            }
        }

        /// <summary>量の表示（例：240ml（満タン2本＋開けた瓶 40ml））</summary>
        public string DescribeAmount()
        {
            int full = FullBottles;
            float opened = OpenedMl;
            if (full > 0 && opened > 0f) return $"{ml:0}ml（満タン{full}本＋開けた瓶 {opened:0}ml）";
            if (full > 0) return $"{ml:0}ml（満タン{full}本）";
            return $"{ml:0}ml（開けた瓶）";
        }

        /// <summary>同じ液体の複製（量は指定。手に持つとき・セーブ用）</summary>
        public PotionStack CloneWith(float amountMl)
        {
            var copy = (PotionStack)MemberwiseClone();
            copy.ml = amountMl;
            copy.count = copy.Bottles;
            copy.effects = new List<string>(effects);
            copy.attributes = attributes != null ? (float[])attributes.Clone() : new float[0];
            return copy;
        }

        /// <summary>1本分の複製（100ml まで。手に持った瓶なら、その中身の量）</summary>
        public PotionStack CloneSingle()
        {
            return CloneWith(Mathf.Min(BottleMl, ml));
        }

        /// <summary>液体の記録から名前・色・属性などを写す（量は変えない）</summary>
        public void CopyFrom(LiquidRecord record)
        {
            potionId = record.id;
            liquidId = record.id;
            potionName = record.DisplayName;
            description = record.description;
            color = record.color;
            stars = record.stars;
            kind = record.kind;
            specialPotionId = record.specialPotionId;
            outcome = record.IsSpecial ? BrewOutcome.Special : record.kind == LiquidKind.Unknown ? BrewOutcome.Failed : BrewOutcome.Generated;
            attributes = (float[])record.attributes.Clone();
            baseLiquidId = record.baseLiquidId;
            effects = new List<string>(record.effects);
        }

        /// <summary>版3までのセーブ（本数だけ）を ml にそろえる</summary>
        public void Normalize()
        {
            if (ml <= 0f && count > 0) ml = count * BottleMl;
            if (string.IsNullOrEmpty(liquidId)) liquidId = "";
            count = Bottles;
        }
    }

    /// <summary>
    /// 瓶詰めした液体の置き場（ポーション棚）。量は ml で持ち、入れられる量に上限はない
    /// （中身を捨てる操作がないので、上限があると「瓶に詰められず、フラスコも空にできない」行き止まりになるため）。
    /// </summary>
    public class PotionInventory : MonoBehaviour
    {
        [SerializeField] private List<PotionStack> stacks = new List<PotionStack>();

        private int orderCounter;

        /// <summary>中身が変わったとき（棚の見た目・UIの更新用）</summary>
        public event Action Changed;

        public IReadOnlyList<PotionStack> Stacks => stacks;

        /// <summary>瓶の本数の合計（開けた瓶も1本）</summary>
        public int TotalCount
        {
            get
            {
                int total = 0;
                foreach (PotionStack stack in stacks) total += stack.Bottles;
                return total;
            }
        }

        public float TotalMl
        {
            get
            {
                float total = 0f;
                foreach (PotionStack stack in stacks) total += stack.ml;
                return total;
            }
        }

        public PotionStack Find(string liquidId)
        {
            if (string.IsNullOrEmpty(liquidId)) return null;
            return stacks.Find(s => s.potionId == liquidId || s.liquidId == liquidId);
        }

        /// <summary>瓶詰めした液体をしまう（失敗作＝未知の液体もしまう）。同じ液体なら量を足す</summary>
        public PotionStack AddBottled(LiquidRecord record, float amountMl)
        {
            if (record == null || amountMl < PotionStack.EmptyMl) return null;
            PotionStack stack = Find(record.id);
            if (stack == null)
            {
                stack = new PotionStack();
                stacks.Add(stack);
            }
            // 名前などは記録に合わせて新しくする（プレイヤーが名前を付けたとき用）
            stack.CopyFrom(record);
            stack.ml = Round(stack.ml + amountMl);
            stack.count = stack.Bottles;
            stack.isNew = true;
            stack.addedOrder = ++orderCounter;
            NotifyChanged();
            return stack;
        }

        /// <summary>1本しまう（100ml）。フェーズ5の最初の版の呼び方</summary>
        public bool TryAdd(LiquidRecord record, out PotionStack stack)
        {
            stack = AddBottled(record, PotionStack.BottleMl);
            return stack != null;
        }

        /// <summary>調合結果を1本しまう（フェーズ4までの方式）。失敗作なら false</summary>
        public bool TryAdd(BrewResult result, out PotionStack stack)
        {
            stack = null;
            if (result == null || !result.IsSuccess) return false;

            stack = Find(result.PotionId);
            if (stack == null)
            {
                stack = new PotionStack
                {
                    potionId = result.PotionId,
                    potionName = result.PotionName,
                    description = result.Description,
                    color = result.PotionColor,
                    stars = result.Stars,
                    outcome = result.Outcome,
                    attributes = result.Attributes.ToArray(),
                    baseLiquidId = result.BaseLiquid != null ? result.BaseLiquid.IngredientId : "",
                };
                foreach (PotionEffect effect in result.Effects) stack.effects.Add(effect.description);
                stacks.Add(stack);
            }
            stack.ml += PotionStack.BottleMl;
            stack.count = stack.Bottles;
            stack.addedOrder = ++orderCounter;
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// 1本取り出す（手に持つ用の複製を返す）。満タンの瓶があれば満タンの瓶、なければ開けた瓶。なければ null
        /// </summary>
        public PotionStack TakeOne(PotionStack stack)
        {
            if (stack == null || !stacks.Contains(stack) || stack.ml < PotionStack.EmptyMl) return null;
            float take = Mathf.Min(PotionStack.BottleMl, stack.ml);
            PotionStack single = stack.CloneWith(take);
            single.isNew = false;
            stack.ml = Round(stack.ml - take);
            stack.isNew = false;
            if (stack.ml < PotionStack.EmptyMl) stacks.Remove(stack);
            else stack.count = stack.Bottles;
            NotifyChanged();
            return single;
        }

        /// <summary>手に持っていた瓶を棚に戻す（減っていれば、減った量のまま戻る）</summary>
        public void ReturnOne(PotionStack single)
        {
            if (single == null || single.ml < PotionStack.EmptyMl) return;
            PotionStack stack = Find(single.potionId);
            if (stack == null)
            {
                stack = single.CloneWith(0f);
                stacks.Add(stack);
            }
            stack.ml = Round(stack.ml + single.ml);
            stack.count = stack.Bottles;
            NotifyChanged();
        }

        /// <summary>まだ見ていない印を消す</summary>
        public void MarkSeen(PotionStack stack)
        {
            if (stack == null || !stack.isNew) return;
            stack.isNew = false;
            NotifyChanged();
        }

        /// <summary>液体の記録の名前が変わったとき（名前を付けたとき）に、棚の瓶にも反映する</summary>
        public void RefreshFrom(LiquidRecord record)
        {
            PotionStack stack = record != null ? Find(record.id) : null;
            if (stack == null) return;
            float keep = stack.ml;
            stack.CopyFrom(record);
            stack.ml = keep;
            NotifyChanged();
        }

        public void Clear()
        {
            stacks.Clear();
            orderCounter = 0;
            NotifyChanged();
        }

        /// <summary>セーブ用に中身を書き出す</summary>
        public List<PotionStack> Export()
        {
            var list = new List<PotionStack>();
            foreach (PotionStack s in stacks) list.Add(s.CloneWith(s.ml));
            return list;
        }

        /// <summary>セーブデータから中身を読み込む（版3までの本数は ml に直す。同じ液体はまとめる）</summary>
        public void Import(List<PotionStack> saved)
        {
            stacks.Clear();
            orderCounter = 0;
            if (saved != null)
            {
                foreach (PotionStack s in saved)
                {
                    if (s == null) continue;
                    s.Normalize();
                    if (s.ml < PotionStack.EmptyMl) continue;
                    PotionStack same = Find(s.potionId);
                    if (same != null)
                    {
                        same.ml = Round(same.ml + s.ml);
                        same.count = same.Bottles;
                        same.addedOrder = Mathf.Max(same.addedOrder, s.addedOrder);
                        continue;
                    }
                    stacks.Add(s);
                    orderCounter = Mathf.Max(orderCounter, s.addedOrder);
                }
            }
            NotifyChanged();
        }

        /// <summary>
        /// 液体の記録がない瓶（フェーズ4までに作ったポーション）に記録を作る。
        /// 記録があれば、注ぎ戻す・レシピ本に載せることができる。
        /// </summary>
        public void EnsureRecords(LiquidRegistry registry)
        {
            if (registry == null) return;
            bool changed = false;
            foreach (PotionStack s in stacks)
            {
                string id = !string.IsNullOrEmpty(s.liquidId) ? s.liquidId : "liq_old_" + s.potionId;
                if (registry.Get(id) == null)
                {
                    var record = new LiquidRecord
                    {
                        id = id,
                        fingerprint = id,
                        autoName = s.potionName,
                        kind = s.outcome == BrewOutcome.Failed ? LiquidKind.Unknown : s.kind,
                        color = s.color,
                        attributes = s.attributes != null ? (float[])s.attributes.Clone() : new float[0],
                        specialPotionId = s.outcome == BrewOutcome.Special && string.IsNullOrEmpty(s.specialPotionId) ? s.potionId : s.specialPotionId,
                        stars = s.stars,
                        baseLiquidId = s.baseLiquidId,
                        description = s.description,
                        effects = new List<string>(s.effects),
                    };
                    if (!string.IsNullOrEmpty(s.baseLiquidId)) record.ingredientIds.Add(s.baseLiquidId);
                    registry.AddIfMissing(record);
                }
                if (s.liquidId != id || s.potionId != id)
                {
                    s.liquidId = id;
                    s.potionId = id;
                    changed = true;
                }
            }
            if (changed) NotifyChanged();
        }

        private static float Round(float v)
        {
            return Mathf.Round(v * 100f) / 100f;
        }

        private void NotifyChanged()
        {
            if (Changed != null) Changed();
        }
    }
}
