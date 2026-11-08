using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 作業台の上のレシピ本（調べると本が開く。どこにいても R キーで開ける）。
    /// 当たり判定（コライダー）と一緒に、本のモデルの一番上に付ける。
    /// </summary>
    public class RecipeBookStand : Interactable
    {
        [SerializeField] private string displayName = "レシピ本";
        [SerializeField, Tooltip("記録の数を数える調合の管理役（空なら起動時にシーンから探す）")] private AlchemyManager alchemy;

        private int shownCount = -1;
        private int shownUnread = -1;
        private string subtitle;

        private void Awake()
        {
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            return string.IsNullOrEmpty(displayName) ? "レシピ本" : displayName;
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (alchemy == null) return "これまでに作った液体の記録";
            int count = alchemy.Liquids.Count;
            int unread = alchemy.Liquids.UnreadCount;
            if (count != shownCount || unread != shownUnread)
            {
                shownCount = count;
                shownUnread = unread;
                subtitle = unread > 0
                    ? $"記録した液体 {count} 種類（新しいページ {unread}）"
                    : $"記録した液体 {count} 種類";
            }
            return subtitle;
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(KeyCode.E, "E", "レシピ本を開く"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key == KeyCode.E && AtelierHUD.Instance != null) AtelierHUD.Instance.OpenRecipeBook();
        }
    }
}
