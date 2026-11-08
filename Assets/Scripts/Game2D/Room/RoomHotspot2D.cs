using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 部屋の中のクリックできる場所（作業台・レシピ本・瓶の棚・カウンター・取引先帳）。Collider2D と同じオブジェクトに付ける。
    /// 指すと、子の「HoverLight」（Light2D）がふわっと灯る（絵を替えても、光で「ここを触れる」とわかる）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RoomHotspot2D : MonoBehaviour
    {
        public enum Kind { Workbench, RecipeBook, Shelf, Counter, Ledger }

        [SerializeField] private Kind kind = Kind.Workbench;
        [SerializeField] private string title = "";
        [SerializeField, TextArea(1, 2)] private string subtitle = "";
        [SerializeField, Tooltip("指したときに灯る光（なくてもよい）")] private Light2D hoverLight;
        [SerializeField, Min(0f)] private float hoverIntensity = 0.7f;

        private bool hovered;

        public Kind Type => kind;
        public string Title => title;
        public string Subtitle => subtitle;

        public void SetHovered(bool on)
        {
            hovered = on;
        }

        private void Awake()
        {
            if (hoverLight != null)
            {
                hoverLight.intensity = 0f;
                hoverLight.enabled = false;
            }
        }

        private void Update()
        {
            if (hoverLight == null) return;
            float target = hovered ? hoverIntensity : 0f;
            hoverLight.intensity = Mathf.MoveTowards(hoverLight.intensity, target, Time.unscaledDeltaTime * 3f);
            bool on = hoverLight.intensity > 0.01f;
            if (hoverLight.enabled != on) hoverLight.enabled = on;
        }
    }
}
