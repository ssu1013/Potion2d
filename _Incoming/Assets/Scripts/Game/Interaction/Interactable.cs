using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>画面に出す操作の候補（例：[E] 取る）</summary>
    public struct InteractionOption
    {
        public KeyCode key;
        public string keyLabel;
        public string label;
        /// <summary>true なら長押しで動き続ける（注ぐなど）</summary>
        public bool hold;

        public InteractionOption(KeyCode key, string keyLabel, string label, bool hold = false)
        {
            this.key = key;
            this.keyLabel = keyLabel;
            this.label = label;
            this.hold = hold;
        }
    }

    /// <summary>
    /// 視線を合わせると操作できるものの基底クラス（素材棚・調合フラスコ・ポーション棚など）。
    /// コライダーを持つ GameObject か、その親に付ける。
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string GetDisplayName(PlayerInteractor player);

        /// <summary>名前の下に小さく出す補足（不要なら null）</summary>
        public virtual string GetSubtitle(PlayerInteractor player) { return null; }

        /// <summary>今できる操作を options に追加する</summary>
        public abstract void GetOptions(PlayerInteractor player, List<InteractionOption> options);

        public virtual void OnPress(PlayerInteractor player, KeyCode key) { }
        public virtual void OnHold(PlayerInteractor player, KeyCode key, float deltaTime) { }
        public virtual void OnRelease(PlayerInteractor player, KeyCode key) { }
    }
}
