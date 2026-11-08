using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 画面全体に広がる Canvas（Screen Space - Overlay）の中身を、絵が映っている 16:9 の範囲（CameraAspect2D）に収める。
    /// Canvas のルートに付けると、今ある子を「Safe」という子の下へ移し、Safe の大きさを絵の範囲に合わせる。
    /// 黒い帯の上に UI がはみ出さず、どの比率の画面でも 1920×1080 で作った配置のまま表示される。
    /// </summary>
    [DisallowMultipleComponent]
    public class ViewportFit2D : MonoBehaviour
    {
        private RectTransform safe;
        private CanvasScaler scaler;
        private float baseMatch = 0.5f;
        private Rect applied = new Rect(-1f, -1f, -1f, -1f);

        /// <summary>UI を置く場所（絵の範囲いっぱいに広がる）</summary>
        public RectTransform SafeRoot
        {
            get
            {
                EnsureSafe();
                return safe;
            }
        }

        private void Awake()
        {
            EnsureSafe();
        }

        private void LateUpdate()
        {
            Apply(false);
        }

        private void EnsureSafe()
        {
            if (safe != null) return;
            scaler = GetComponent<CanvasScaler>();
            if (scaler != null) baseMatch = scaler.matchWidthOrHeight;

            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);

            var go = new GameObject("Safe", typeof(RectTransform));
            safe = (RectTransform)go.transform;
            safe.SetParent(transform, false);
            safe.anchorMin = Vector2.zero;
            safe.anchorMax = Vector2.one;
            safe.pivot = new Vector2(0.5f, 0.5f);
            safe.offsetMin = Vector2.zero;
            safe.offsetMax = Vector2.zero;
            // 今ある子は、並び順（手前・奥）を変えずに Safe の下へ
            foreach (Transform child in children) child.SetParent(safe, false);
            Apply(true);
        }

        private void Apply(bool force)
        {
            Rect r = CameraAspect2D.ViewRect;
            if (!force && r == applied) return;
            applied = r;
            safe.anchorMin = r.min;
            safe.anchorMax = r.max;
            safe.offsetMin = Vector2.zero;
            safe.offsetMax = Vector2.zero;
            // 帯が出ているときは、絵の範囲がちょうど基準の大きさ（1920×1080）になるように拡大率を決める
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.matchWidthOrHeight = CameraAspect2D.Pillarbox ? 1f : CameraAspect2D.Letterbox ? 0f : baseMatch;
            }
        }
    }
}
