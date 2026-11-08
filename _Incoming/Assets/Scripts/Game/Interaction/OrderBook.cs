using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 取引先帳（カウンターの台帳など）。[E] で素材の入手ルートの一覧を開く。
    ///   ・使い放題の素材 … ゴールドを払って入手ルートを「開拓」すると、ずっと使い放題になる
    ///   ・希少な素材 … 数を決めて「取り寄せる」（使った分だけ減る）
    /// 費用は素材データの「開拓の費用」「価格」で決まる（0 にすると取引しない＝ファームや採集でしか手に入らない素材にできる）。
    /// </summary>
    public class OrderBook : Interactable
    {
        [SerializeField] private PlayerProgress progress;
        [SerializeField] private string displayName = "取引先帳";

        private int shownGold = -1;
        private string subtitle;

        private void Awake()
        {
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            // フェーズ4までに作った Prefab は「仕入れ帳」のまま保存されているので、呼び方だけ新しくする
            return string.IsNullOrEmpty(displayName) || displayName == "仕入れ帳" ? "取引先帳" : displayName;
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (progress == null) return "素材の入手ルートを開拓する";
            if (progress.Gold != shownGold)
            {
                shownGold = progress.Gold;
                subtitle = $"素材の入手ルートを開拓する・取り寄せる（所持金 {shownGold} G）";
            }
            return subtitle;
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            options.Add(new InteractionOption(KeyCode.E, "E", "取引先帳を開く"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key == KeyCode.E && AtelierHUD.Instance != null) AtelierHUD.Instance.OpenShop();
        }
    }
}
