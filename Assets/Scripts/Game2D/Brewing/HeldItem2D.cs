using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// カーソルについてくる「手に持っている物」（HeldItem の Prefab のルートに付ける）。
    /// 絵は手に持った物に合わせて入れ替わる（素材のアイコン、またはトレイと同じ容器の絵）。
    /// 注ぐときは、瓶の口を支点に傾き、口から液体の線が液面まで落ちる。
    ///
    /// 絵を差し替えるところ：
    ///   Art/Body・Art/Content … 持った物で自動で入れ替わる（Prefab では空のままでよい）
    ///   Stream … 注ぐ液体の線（白い縦長の絵。液体の色で塗られ、長さは自動で伸びる）
    /// 瓶の口は「絵の上端の真ん中」として扱う。
    /// </summary>
    public class HeldItem2D : MonoBehaviour
    {
        [Header("絵")]
        [SerializeField, Tooltip("容器の絵（持った物で自動で入れ替わる）")] private SpriteRenderer body;
        [SerializeField, Tooltip("中身（白い形を素材・液体の色で塗る）")] private SpriteRenderer content;
        [SerializeField, Tooltip("Body と Content をまとめた子（大きさをそろえるのに使う）")] private Transform art;
        [SerializeField, Tooltip("注ぐ液体の線（白い縦長の絵）")] private SpriteRenderer stream;

        [Header("大きさ・注ぎ方")]
        [SerializeField, Min(0.1f), Tooltip("手に持ったときの高さ（ワールド単位）")] private float height = 1.5f;
        [SerializeField, Min(0.1f)] private float maxWidth = 1.6f;
        [SerializeField, Tooltip("カーソルから見た、持った物の中心の位置")] private Vector2 cursorOffset = new Vector2(0f, 0.15f);
        [SerializeField, Tooltip("注ぐとき、フラスコの口から見た「瓶の口」の位置")] private Vector2 pourOffset = new Vector2(0.18f, 0.55f);
        [SerializeField, Range(30f, 160f), Tooltip("いちばん深く傾けたときの角度")] private float maxTiltDegrees = 120f;
        [SerializeField, Min(0.01f), Tooltip("注ぐ液体の線の太さ（いちばん速く注いだとき）")] private float streamWidth = 0.1f;
        [SerializeField, Min(1f)] private float followSharpness = 18f;

        private Vector3 mouthLocal;
        private float shake;

        public HeldThing2D Thing { get; private set; }
        public bool HasItem => Thing != null;
        /// <summary>瓶の口（注ぐ・欠片が落ちる位置）</summary>
        public Vector3 MouthWorld => transform.TransformPoint(mouthLocal);

        private void Awake()
        {
            HideStream();
            if (body != null) body.enabled = false;
            if (content != null) content.enabled = false;
        }

        public void Show(HeldThing2D thing, BrewArt2D artSet, Vector3 cursorWorld)
        {
            Thing = thing;
            Sprite bodySprite = null, contentSprite = null;
            Color contentColor = Color.white;
            if (artSet != null) artSet.Resolve(thing.Ingredient, thing.Potion, out bodySprite, out contentSprite, out contentColor);
            if (body != null)
            {
                body.sprite = bodySprite;
                body.enabled = bodySprite != null;
            }
            if (content != null)
            {
                content.sprite = contentSprite;
                contentColor.a = 0.92f;
                content.color = contentColor;
                content.enabled = contentSprite != null;
            }
            Vector2 size = ArtFit2D.Fit(art, bodySprite != null ? bodySprite : contentSprite, height, maxWidth);
            mouthLocal = new Vector3(0f, size.y * 0.5f, 0f);
            transform.position = cursorWorld + (Vector3)cursorOffset;
            transform.rotation = Quaternion.identity;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            Thing = null;
            HideStream();
            if (body != null) body.enabled = false;
            if (content != null) content.enabled = false;
        }

        /// <summary>カーソルについていく（傾きは元に戻る）</summary>
        public void Follow(Vector3 cursorWorld, float dt)
        {
            float k = 1f - Mathf.Exp(-followSharpness * dt);
            Vector3 target = cursorWorld + (Vector3)cursorOffset;
            if (shake > 0f)
            {
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
                target.x += Mathf.Sin(Time.time * 60f) * 0.05f * shake;
            }
            transform.position = Vector3.Lerp(transform.position, target, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.identity, k);
        }

        /// <summary>注ぐ姿勢：瓶の口をフラスコの口の上に置き、tilt（0〜1）だけ傾ける</summary>
        public void PourPose(Vector3 flaskMouth, float tilt, float dt)
        {
            float k = 1f - Mathf.Exp(-followSharpness * dt);
            Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Clamp01(tilt) * maxTiltDegrees);
            Vector3 mouthTarget = flaskMouth + (Vector3)pourOffset;
            Vector3 position = mouthTarget - rotation * mouthLocal;
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, k);
            transform.position = Vector3.Lerp(transform.position, position, k);
        }

        /// <summary>注ぐ液体の線を、瓶の口から真下の液面（toY）まで伸ばす。rate01 は注ぐ速さ（0〜1）</summary>
        public void ShowStream(float toY, Color color, float rate01)
        {
            if (stream == null) return;
            Vector3 from = MouthWorld;
            float length = Mathf.Max(0.05f, from.y - toY);
            Vector2 size = stream.sprite != null ? (Vector2)stream.sprite.bounds.size : Vector2.one;
            float width = streamWidth * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(rate01));
            stream.transform.position = new Vector3(from.x, from.y - length * 0.5f, 0f);
            stream.transform.rotation = Quaternion.identity;
            Vector3 parentScale = transform.lossyScale;
            stream.transform.localScale = new Vector3(
                width / Mathf.Max(0.001f, size.x) / Mathf.Max(0.001f, parentScale.x),
                length / Mathf.Max(0.001f, size.y) / Mathf.Max(0.001f, parentScale.y), 1f);
            color.a = 0.9f;
            stream.color = color;
            stream.enabled = true;
        }

        public void HideStream()
        {
            if (stream != null) stream.enabled = false;
        }

        /// <summary>固体・粉を入れたときに、軽く振る</summary>
        public void Shake()
        {
            shake = 1f;
        }
    }
}
