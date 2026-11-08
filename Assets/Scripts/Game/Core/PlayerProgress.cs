using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>所持金と、相談に乗った記録（解決した数・分け与えた数・作り方を教えた数）</summary>
    public class PlayerProgress : MonoBehaviour
    {
        [SerializeField] private int gold;
        [SerializeField, Tooltip("解決した相談の数（分け与えた＋教えた）")] private int completedQuests;
        [SerializeField, Tooltip("瓶を分け与えて解決した数")] private int sharedCount;
        [SerializeField, Tooltip("作り方を教えて解決した数")] private int taughtCount;
        [SerializeField] private List<string> completedQuestIds = new List<string>();
        [SerializeField] private bool gameCleared;

        /// <summary>所持金が変わったとき（新しい金額, 増減）</summary>
        public event Action<int, int> GoldChanged;
        public event Action Changed;

        public int Gold => gold;
        /// <summary>解決した相談の数（取引先の「実績」にもなる）</summary>
        public int CompletedQuests => completedQuests;
        public int SharedCount => sharedCount;
        public int TaughtCount => taughtCount;
        public bool GameCleared => gameCleared;
        public IReadOnlyList<string> CompletedQuestIds => completedQuestIds;

        public bool HasCompleted(string questId)
        {
            return completedQuestIds.Contains(questId);
        }

        public void AddGold(int amount)
        {
            gold = Mathf.Max(0, gold + amount);
            if (GoldChanged != null) GoldChanged(gold, amount);
            Notify();
        }

        /// <summary>お金を払う（足りなければ払わずに false）</summary>
        public bool TrySpendGold(int amount)
        {
            if (amount <= 0) return true;
            if (gold < amount) return false;
            AddGold(-amount);
            return true;
        }

        /// <summary>はじめから遊ぶときの状態に戻す（所持金は最初の金額）</summary>
        public void ResetAll(int startingGold)
        {
            gold = Mathf.Max(0, startingGold);
            completedQuests = 0;
            sharedCount = 0;
            taughtCount = 0;
            completedQuestIds.Clear();
            gameCleared = false;
            if (GoldChanged != null) GoldChanged(gold, 0);
            Notify();
        }

        /// <summary>相談を解決した（taught = 作り方を教えた。false なら瓶を分け与えた）</summary>
        public void MarkCompleted(string questId, bool taught = false)
        {
            completedQuests++;
            if (taught) taughtCount++;
            else sharedCount++;
            if (!completedQuestIds.Contains(questId)) completedQuestIds.Add(questId);
            Notify();
        }

        public void MarkCleared()
        {
            gameCleared = true;
            Notify();
        }

        public void ResetAll()
        {
            ResetAll(0);
        }

        public void WriteTo(SaveData data)
        {
            data.gold = gold;
            data.completedQuests = completedQuests;
            data.sharedCount = sharedCount;
            data.taughtCount = taughtCount;
            data.completedQuestIds = new List<string>(completedQuestIds);
            data.gameCleared = gameCleared;
        }

        public void ReadFrom(SaveData data)
        {
            gold = data.gold;
            completedQuests = data.completedQuests;
            // 版4までのセーブは「分け与える」しかなかったので、解決した数をそのまま分け与えた数にする
            sharedCount = data.version >= 5 ? data.sharedCount : data.completedQuests;
            taughtCount = data.version >= 5 ? data.taughtCount : 0;
            completedQuestIds = data.completedQuestIds != null ? new List<string>(data.completedQuestIds) : new List<string>();
            gameCleared = data.gameCleared;
            if (GoldChanged != null) GoldChanged(gold, 0);
            Notify();
        }

        private void Notify()
        {
            if (Changed != null) Changed();
        }
    }
}
