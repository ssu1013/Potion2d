using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// Prefab の「目印」（空のオブジェクト）を探す。目印の名前でコードが位置を知るので、モデルの形に縛られない。
    ///   Grip         … 手に持ったときの基準点（瓶の胴の中ほど）
    ///   Mouth        … 注ぎ口・入れ口（瓶の口、フラスコの口）
    ///   LiquidBottom / LiquidTop … フラスコの液面の下限・上限
    ///   FaceAnchor   … お客さんの顔の中心（会話ウィンドウの顔アップ用）
    ///   Slot_*       … 棚に物を置く位置
    /// 目印は Prefab の「Anchors」の下に置くのがおすすめ（どこにあっても名前で見つける）。
    /// </summary>
    public static class PrefabAnchors
    {
        public const string Grip = "Grip";
        public const string Mouth = "Mouth";
        public const string LiquidBottom = "LiquidBottom";
        public const string LiquidTop = "LiquidTop";
        public const string FaceAnchor = "FaceAnchor";

        /// <summary>名前の一致する子孫を探す（見つからなければ null）</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>見た目（Renderer）全体の範囲。目印がないときの代わりに使う</summary>
        public static Bounds RendererBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            var b = new Bounds(root.position, Vector3.zero);
            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(root.position, Vector3.one * 0.1f);
            return b;
        }

        /// <summary>目印の位置（なければ見た目の範囲から決める：Mouth は上端の中心、Grip は中心）</summary>
        public static Vector3 WorldPoint(Transform root, string name)
        {
            Transform t = Find(root, name);
            if (t != null) return t.position;
            Bounds b = RendererBounds(root);
            if (name == Mouth) return new Vector3(b.center.x, b.max.y, b.center.z);
            return b.center;
        }
    }
}
