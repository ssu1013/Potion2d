using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 調合台のトレイに並んだ素材・自作の液体の瓶。
    /// 調合台に向かっているときはマウスでクリックして手に取る（BrewMode）。歩いているときも見て [E] で手に取れる。
    /// </summary>
    public class BrewTrayItem : Interactable
    {
        private BrewTray tray;
        private TrayEntry entry;
        private ContentVisual content;

        public TrayEntry Entry => entry;
        public BrewTray Tray => tray;

        public void Configure(BrewTray tray, TrayEntry entry)
        {
            this.tray = tray;
            this.entry = entry;
            content = GetComponent<ContentVisual>();
            RefreshAmount();
        }

        /// <summary>中身の量の見た目（希少な素材は在庫、瓶は量で液面が下がる）</summary>
        public void RefreshAmount()
        {
            if (content == null) return;
            if (entry.bottle != null)
            {
                content.SetAmount(Mathf.Clamp01(entry.bottle.ml / PotionStack.BottleMl));
                return;
            }
            IngredientInventory inventory = tray != null && tray.Ingredients != null ? tray.Ingredients : FindFirstObjectByType<IngredientInventory>();
            float stock = inventory != null ? inventory.Get(entry.ingredient) : 1f;
            GameBalance balance = GameContext.Balance;
            float full = balance != null ? balance.containerFullAmount : 8f;
            content.SetAmount(stock < IngredientInventory.Epsilon ? 0f : Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(stock / full)));
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            if (entry.bottle != null) return entry.bottle.potionName;
            return entry.ingredient != null ? entry.ingredient.DisplayName : "";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            return Describe(entry, player != null ? player.Ingredients : null, player != null && player.Alchemy != null ? player.Alchemy.Settings : null);
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(KeyCode.E, "E", "手に取る"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key == KeyCode.E) Take(player);
        }

        /// <summary>手に取る（瓶は満タンの瓶から1本、素材はそのまま）。取れたら true</summary>
        public bool Take(PlayerInteractor player)
        {
            if (player == null) return false;
            if (entry.bottle != null)
            {
                PotionInventory potions = tray != null && tray.Potions != null ? tray.Potions : FindFirstObjectByType<PotionInventory>();
                return player.HoldPotion(potions, entry.bottle);
            }
            return player.HoldIngredient(entry.ingredient);
        }

        /// <summary>トレイの物の説明（分類・量・属性）</summary>
        public static string Describe(TrayEntry e, IngredientInventory ingredients, AlchemySettings settings)
        {
            if (e.bottle != null)
            {
                string stars = e.bottle.stars > 0 ? BrewResult.StarText(e.bottle.stars) + "　" : "";
                return $"{stars}{IngredientBrowserUI.KindLabel(e.bottle.kind)}・自作の液体　{e.bottle.DescribeAmount()}";
            }
            if (e.ingredient == null) return null;
            string attributes = IngredientSource.DescribeAttributes(e.ingredient, settings);
            string amount = BrewTray.AmountLabel(e, ingredients);
            return amount == "∞" ? attributes + "　<color=#9EE08A>使い放題</color>" : $"{attributes}　在庫 {amount}";
        }
    }
}
