using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>ポーション棚に並んだ瓶。見ると名前と数が出て、[E] で1本手に取れる</summary>
    public class PotionBottle : Interactable
    {
        private PotionInventory inventory;
        private PotionStack stack;

        public PotionStack Stack => stack;

        public void Configure(PotionInventory inventory, PotionStack stack)
        {
            this.inventory = inventory;
            this.stack = stack;
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            return stack != null ? stack.potionName : "ポーション";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (stack == null) return null;
            string stars = stack.stars > 0 ? BrewResult.StarText(stack.stars) + "　" : "";
            return $"{stars}{IngredientBrowserUI.KindLabel(stack.kind)}　{stack.DescribeAmount()}";
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            if (player.HeldPotion != null) options.Add(new InteractionOption(KeyCode.E, "E", "持っている瓶を戻す"));
            else options.Add(new InteractionOption(KeyCode.E, "E", "取る"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key != KeyCode.E) return;
            if (player.HeldPotion != null) player.ClearHeld();
            else player.HoldPotion(inventory, stack);
        }
    }
}
