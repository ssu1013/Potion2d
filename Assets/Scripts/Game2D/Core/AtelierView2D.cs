using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合机の見え方（1画面版）：ゲーム中ずっと、カメラは調合机（フラスコと素材トレイ）から動かない。
    /// タイトルも同じ机の上に重ねて出すので、画面の切り替え・暗転・ズームはない。
    /// 本（レシピ本・素材ノート）も机の上に広げるだけなので、背景のランプの揺らぎや環境音は途切れない。
    /// シーンの GameSystems に付ける（ファイル名とクラス名は同じ AtelierView2D のまま）。
    /// </summary>
    public class AtelierView2D : MonoBehaviour
    {
        public static AtelierView2D Current { get; private set; }

        [Header("カメラ")]
        [SerializeField] private Camera viewCamera;
        [SerializeField, Tooltip("調合机を見るカメラの位置（BrewArea の BrewView）")] private Transform brewView;
        [SerializeField, Min(1f), Tooltip("カメラの大きさ（画面の高さの半分）")] private float brewSize = 5.4f;

        /// <summary>カメラの位置（自動テストで「動いていない」ことを確かめる）</summary>
        public Vector3 CameraPosition => viewCamera != null ? viewCamera.transform.position : Vector3.zero;
        public float CameraSize => viewCamera != null ? viewCamera.orthographicSize : 0f;

        private void Awake()
        {
            Current = this;
            if (viewCamera == null) viewCamera = Camera.main;
            Place();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>カメラを調合机の前に置く（はじめに1回。そのあとは動かさない）</summary>
        public void Place()
        {
            if (viewCamera == null) return;
            Vector3 p = brewView != null ? brewView.position : viewCamera.transform.position;
            viewCamera.transform.position = new Vector3(p.x, p.y, viewCamera.transform.position.z);
            viewCamera.orthographicSize = brewSize;
        }
    }
}
