using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// ゲームバランスの数値（Assets/GameData/GameBalance.asset）。はじめから調合机にある素材。
    /// （1画面版：お金・仕入れはない。ほかの素材は、発見が進むと机に届く＝IngredientData の「届くのに必要な発見」）
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalance", menuName = "Potion Frontier/ゲームバランス (Game Balance)", order = 11)]
    public class GameBalance : ScriptableObject
    {
        /// <summary>サンプルの初期値を変えたときに上げる（「アトリエを整える」で古い設定を新しい初期値にする）</summary>
        public const int CurrentVersion = 2;

        [Header("はじめから")]
        [Tooltip("はじめから調合机のトレイにある素材（量は使わない。どれも使っても減らない）")]
        public List<IngredientAmount> startingIngredients = new List<IngredientAmount>();

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
