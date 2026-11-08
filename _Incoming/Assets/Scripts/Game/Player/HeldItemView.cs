using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 手に持っている物を画面右下に表示する（カメラの子に置く）。手は表示せず、物のモデルだけが動く。
    ///   ・持ち方 … モデルの「Grip」の目印が持つ位置に来るように置く（目印がなければ見た目の中心）
    ///   ・注ぐ   … 瓶を傾ける。液体は「Mouth」の目印（なければ上端の中心）から出る
    ///   ・入れる … 容器をフラスコの上で軽く傾け、口から欠片を落とす
    /// 中の液体（LiquidFill）は傾けても水平を保ち、在庫に合わせて減って見える。
    /// </summary>
    public class HeldItemView : MonoBehaviour
    {
        [SerializeField] private FirstPersonController controller;
        [SerializeField] private Vector3 restPosition = new Vector3(0.28f, -0.25f, 0.62f);
        [SerializeField] private Vector3 restEuler = new Vector3(-4f, -16f, 4f);
        [SerializeField] private Vector3 pourPosition = new Vector3(0.1f, -0.08f, 0.62f);
        [SerializeField] private Vector3 pourEuler = new Vector3(-4f, -16f, 100f);
        [SerializeField] private Vector3 shakeEuler = new Vector3(-4f, -16f, 70f);
        [SerializeField, Tooltip("手に持ったときの高さ（m）。大きさの違うモデルでも同じくらいに見えるようにそろえる")] private float targetHeight = 0.17f;

        private Transform pivot;
        private Transform item;
        private ContentVisual content;
        private Vector3 mouthLocal;
        private bool pouring;
        private float pourBlend;
        private float shakeTimer;
        private float bobPhase;
        private float showBlend;
        private bool hiddenByMode;

        /// <summary>手に持っている物の口（注ぐ液体・落とす欠片の出発点）</summary>
        public Vector3 MouthWorldPosition => item != null ? item.TransformPoint(mouthLocal) : transform.position;
        public bool IsPouring => pouring && pourBlend > 0.6f;
        public bool HasItem => item != null;

        public void Configure(FirstPersonController controller)
        {
            this.controller = controller;
        }

        private void Awake()
        {
            pivot = new GameObject("HeldPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = restPosition;
            pivot.localRotation = Quaternion.Euler(restEuler);
        }

        /// <summary>素材を手に持つ（素材データのモデル、なければ仮の容器）</summary>
        public void ShowIngredient(IngredientData data, float amount)
        {
            GameObject prefab = GameContext.Art != null ? GameContext.Art.IngredientModel(data) : null;
            Show(prefab, data != null ? data.Color : Color.white, amount);
        }

        /// <summary>瓶詰めした液体を手に持つ（中身の量に合わせて液面が下がる）</summary>
        public void ShowPotion(PotionStack stack)
        {
            float amount = stack.ml > 0f ? Mathf.Clamp01(stack.ml / PotionStack.BottleMl) : 1f;
            Show(PotionVisuals.BottlePrefab(stack.baseLiquidId), stack.color, amount);
        }

        /// <summary>中身の量を変える（注いで在庫が減ったとき）</summary>
        public void SetAmount(float amount)
        {
            if (content != null) content.SetAmount(amount);
        }

        public void Clear()
        {
            if (item != null) Destroy(item.gameObject);
            item = null;
            content = null;
            pouring = false;
        }

        /// <summary>手元の表示を隠す（調合台では、持った物がカーソルについてくるので）</summary>
        public void SetHidden(bool hidden)
        {
            hiddenByMode = hidden;
            if (pivot != null) pivot.gameObject.SetActive(!hidden);
        }

        public void SetPouring(bool on)
        {
            pouring = on;
        }

        /// <summary>固体・粉を入れるときに、容器を軽く傾ける</summary>
        public void PlayShake()
        {
            shakeTimer = 0.45f;
        }

        private void Show(GameObject prefab, Color color, float amount)
        {
            Clear();
            GameObject go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Held_" + (prefab != null ? prefab.name : "Item");
            // 手に持つ物は飾り：当たり判定と、ゲームの操作用の部品を外す。影も落とさない
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (Interactable i in go.GetComponentsInChildren<Interactable>(true)) Destroy(i);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            content = go.GetComponent<ContentVisual>();
            if (content != null)
            {
                content.SetColor(color);
                content.SetAmount(amount);
                content.SetFollowMovement(true);
            }

            // 大きさをそろえ、Grip の目印が持つ位置に来るように置く
            Transform t = go.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = prefab != null ? prefab.transform.localScale : new Vector3(0.08f, 0.08f, 0.08f);
            Bounds b = PrefabAnchors.RendererBounds(t);
            float scale = Mathf.Clamp(targetHeight / Mathf.Max(0.01f, b.size.y), 0.2f, 5f);
            t.localScale *= scale;
            Vector3 grip = PrefabAnchors.WorldPoint(t, PrefabAnchors.Grip);
            Vector3 mouth = PrefabAnchors.WorldPoint(t, PrefabAnchors.Mouth);
            mouthLocal = t.InverseTransformPoint(mouth);

            t.SetParent(pivot, false);
            t.localRotation = Quaternion.identity;
            t.localPosition = -grip; // 置いた時点でモデルは原点・回転なしなので、そのまま差し引ける
            item = t;
            pourBlend = 0f;
            showBlend = 0f;
        }

        private void LateUpdate()
        {
            if (hiddenByMode) return;
            float dt = Time.deltaTime;
            pourBlend = Mathf.MoveTowards(pourBlend, pouring ? 1f : 0f, dt * 3.5f);
            float eased = pourBlend * pourBlend * (3f - 2f * pourBlend);
            showBlend = Mathf.MoveTowards(showBlend, item != null ? 1f : 0f, dt * 5f);

            // 手元のゆれ：止まっているときはゆっくり呼吸する程度。歩いても大きくは揺らさない
            // （「歩くときの揺れ」をオンにしたときだけ、歩くリズムで少し揺れる）
            float move = controller != null ? controller.MoveAmount : 0f;
            bool walkBob = GameSettings.ViewBob;
            bobPhase += dt * (walkBob ? Mathf.Lerp(2f, 9f, move) : 2f);
            float amount = walkBob ? 0.4f + move : 0.4f + move * 0.15f;
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase) * 0.004f, Mathf.Sin(bobPhase * 2f) * 0.004f, 0f) * amount * (1f - eased);

            // 入れるときの小さな傾け（行って戻る）
            float shake = 0f;
            if (shakeTimer > 0f)
            {
                shakeTimer -= dt;
                shake = Mathf.Sin(Mathf.Clamp01(1f - shakeTimer / 0.45f) * Mathf.PI);
            }

            // 持ち上げるときは画面の下からせり上がる
            Vector3 raise = new Vector3(0f, -0.25f, 0f) * (1f - showBlend * showBlend * (3f - 2f * showBlend));
            Vector3 position = Vector3.Lerp(restPosition, pourPosition, Mathf.Max(eased, shake * 0.8f));
            Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(restEuler), Quaternion.Euler(pourEuler), eased);
            if (shake > 0f) rotation = Quaternion.Slerp(rotation, Quaternion.Euler(shakeEuler), shake);
            pivot.localPosition = position + bob + raise;
            pivot.localRotation = rotation;
        }
    }
}
