using System;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合画面で使う「共通の絵」の一覧（Assets/Art2D/Brewing/BrewArt2D.asset）。
    /// ・素材ごとの絵は、素材データ（IngredientData）の「アイコン」欄に入れる。アイコンがあればそれを優先する。
    /// ・アイコンがない素材は、ここの「容器の絵」で表示し、中身の形を素材の色で塗る。
    /// ・フラスコ・加熱台・トレイ・ランプなどの絵は、それぞれの Prefab（Assets/Art2D/Brewing/Prefabs）の「Art」の下にある。
    /// </summary>
    [CreateAssetMenu(fileName = "BrewArt2D", menuName = "Potion Frontier 2D/調合画面の絵の一覧 (Brew Art 2D)", order = 20)]
    public class BrewArt2D : ScriptableObject
    {
        [Serializable]
        public class ContainerArt
        {
            [Tooltip("容器の絵（ガラス・栓・ラベルなど。中身は描かない）")] public Sprite body;
            [Tooltip("中身の形（白一色で描く。素材や液体の色で塗られる）。容器の絵と同じ大きさのキャンバスで描く。なくてもよい")] public Sprite content;
        }

        [Header("素材の容器（素材データにアイコンがないときに使う）")]
        [Tooltip("背の高い瓶（ベース液体）")] public ContainerArt tallBottle = new ContainerArt();
        [Tooltip("壺（主素材）")] public ContainerArt jar = new ContainerArt();
        [Tooltip("丸底フラスコ（触媒）")] public ContainerArt roundFlask = new ContainerArt();
        [Tooltip("小袋（粉）")] public ContainerArt pouch = new ContainerArt();
        [Tooltip("結晶")] public ContainerArt crystal = new ContainerArt();

        [Header("自作の液体（瓶詰めした液体）の瓶")]
        public ContainerArt potionBottle = new ContainerArt();

        [Header("小物")]
        [Tooltip("固体・粉を入れたときに落ちる欠片（白で描く。素材の色で塗られる）")] public Sprite dropPiece;

        [Header("演出（白で描く。色はコードで付ける）")]
        [Tooltip("湯気・フラスコの中のもや・焦げの煙・粉のもや（ふわっとした丸）")] public Sprite steam;
        [Tooltip("泡（小さな輪）")] public Sprite bubble;
        [Tooltip("しぶき（小さな粒）")] public Sprite droplet;
        [Tooltip("液面の渦（真上から見た渦巻き。かき混ぜると、液面の上で回る）")] public Sprite swirl;
        [Tooltip("液面の波紋（真上から見た輪。物を落としたとき・注いでいる所に広がる）")] public Sprite ripple;
        [Tooltip("湯気・泡・しぶきのマテリアル（空なら Sprite-Lit-Default。灯りに照らされる）")] public Material particleMaterial;

        public ContainerArt ForStyle(ContainerStyle style)
        {
            switch (style)
            {
                case ContainerStyle.Jar: return jar;
                case ContainerStyle.RoundFlask: return roundFlask;
                case ContainerStyle.Pouch: return pouch;
                case ContainerStyle.Crystal: return crystal;
                default: return tallBottle;
            }
        }

        /// <summary>手に持つ物・トレイの物の絵を決める（素材のアイコンがあればそれだけを使う）</summary>
        public void Resolve(IngredientData ingredient, PotionStack potion, out Sprite body, out Sprite content, out Color contentColor)
        {
            body = null;
            content = null;
            contentColor = Color.white;
            if (potion != null)
            {
                body = potionBottle.body;
                content = potionBottle.content;
                contentColor = potion.color;
                return;
            }
            if (ingredient == null) return;
            contentColor = ingredient.Color;
            if (ingredient.Icon != null)
            {
                body = ingredient.Icon;
                return;
            }
            ContainerArt art = ForStyle(ingredient.Container);
            body = art.body;
            content = art.content;
        }
    }
}
