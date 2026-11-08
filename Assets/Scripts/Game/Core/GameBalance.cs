using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// ゲームバランスの数値（Assets/GameData/GameBalance.asset）。はじめから遊ぶときの所持金・素材、素材の取引のルールなど。
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalance", menuName = "Potion Frontier/ゲームバランス (Game Balance)", order = 11)]
    public class GameBalance : ScriptableObject
    {
        /// <summary>サンプルの初期値を変えたときに上げる（「アトリエを整える」で古い設定を新しい初期値にする）</summary>
        public const int CurrentVersion = 2;

        [Header("はじめから")]
        [Min(0)] public int startingGold = 50;
        [Tooltip("はじめから持っている素材と量（単位）。使い放題の素材は量に関係なく「取引済み」になる")]
        public List<IngredientAmount> startingIngredients = new List<IngredientAmount>();

        [Header("素材の取引")]
        [Tooltip("このレア度以下の素材は、一度手に入れたら使い放題（素材ごとの「在庫の扱い」が自動のとき）。それより上は使った分だけ減る")]
        [Range(0, 5)] public int infiniteRarityMax = 2;
        [Tooltip("使い放題の素材の「開拓の費用」が自動のとき：取り寄せの値段 × この倍率")]
        [Min(1)] public int unlockPriceMultiplier = 8;

        [Header("表示")]
        [Tooltip("棚の容器の液面が満タンに見える在庫の量（単位）。これより少ないと液面が下がって見える")]
        [Min(0.1f)] public float containerFullAmount = 8f;

        [SerializeField, HideInInspector] private int version;
        public int Version => version;

#if UNITY_EDITOR
        public void EditorSetVersion(int value)
        {
            version = value;
        }
#endif
    }
}
