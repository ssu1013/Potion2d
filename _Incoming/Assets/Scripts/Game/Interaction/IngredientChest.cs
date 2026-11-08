using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 素材箱（作業台の横の木箱など）。[E] で素材箱のウィンドウを開き、持っている素材を全種類から選んで手に持つ。
    /// どんなモデルにも付けられる（当たり判定のある GameObject か、その親に付ける）。[I] キーでもどこからでも開ける。
    /// </summary>
    public class IngredientChest : Interactable
    {
        [SerializeField] private IngredientInventory inventory;
        [SerializeField] private string displayName = "素材箱";

        private void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<IngredientInventory>();
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            return displayName;
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (inventory == null) return null;
            int kinds = inventory.CountKinds(GameContext.Ingredients);
            return kinds > 0 ? $"素材 {kinds} 種類" : "<color=#F08A6A>空っぽ（取引先帳で手に入れられる）</color>";
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(KeyCode.E, "E", "素材を選ぶ"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key == KeyCode.E && AtelierHUD.Instance != null) AtelierHUD.Instance.OpenIngredientBox();
        }
    }
}
