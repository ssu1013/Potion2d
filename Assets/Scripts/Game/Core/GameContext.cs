using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// ゲーム全体で使うデータ（素材カタログ・バランス）への入り口。シーンの GameSystems などに1つだけ付ける。
    /// ほかの部品より先に動く。
    /// 2D 版：3D 版で持っていた「見た目の一覧（ArtCatalog）」と UI スキンの設定は外した（見た目は 2D 側で別に持つ）。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameContext : MonoBehaviour
    {
        public static GameContext Instance { get; private set; }

        [SerializeField] private IngredientDatabase ingredients;
        [SerializeField] private GameBalance balance;

        public static IngredientDatabase Ingredients => Instance != null ? Instance.ingredients : null;
        public static GameBalance Balance => Instance != null ? Instance.balance : null;

        public void Configure(IngredientDatabase ingredients, GameBalance balance)
        {
            this.ingredients = ingredients;
            this.balance = balance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[GameContext] シーンに2つ以上あります。あとから動いた方を使います。", this);
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
