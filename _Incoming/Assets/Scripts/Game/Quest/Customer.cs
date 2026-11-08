using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 来店したお客さん。歩く・待つ・こちらを見る・吹き出しを出す。
    /// 話しかけたときの中身は QuestManager が決める。
    /// 見た目は相談データの「モデル」（Prefab）があればそれを、なければ「見た目」の色・髪型から仮のモデルを作る。
    /// </summary>
    public class Customer : Interactable
    {
        public enum Mood { None, Exclaim, Waiting, Happy, Sad }

        private QuestManager manager;
        private QuestData quest;
        private CustomerBuilder.Parts parts;
        private Camera portraitCamera;
        private RenderTexture portraitTexture;
        private GameObject bubble;
        private Text bubbleText;
        private Mood mood;
        private bool walking;
        private float walkPhase;
        private float idlePhase;
        private float stepDistance;
        private Vector3 lastPosition;
        private Animator animator;
        private bool procedural = true;
        private Vector3 bodyBase;
        private Vector3 headBase;
        private Quaternion leftArmBase = Quaternion.identity;
        private Quaternion rightArmBase = Quaternion.identity;
        private Quaternion headRotationBase = Quaternion.identity;
        private float speedParam;
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoodId = Animator.StringToHash("Mood");
        private bool hasSpeedParam;
        private bool hasMoodParam;

        public QuestData Quest => quest;
        public bool IsWalking => walking;

        public void Init(QuestManager manager, QuestData quest, Material baseMaterial)
        {
            this.manager = manager;
            this.quest = quest;
            idlePhase = UnityEngine.Random.value * 10f;

            if (quest.ModelPrefab != null) BuildFromPrefab(quest.ModelPrefab);
            else
            {
                parts = CustomerBuilder.Build(transform, quest.Look, baseMaterial);
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.7f * quest.Look.height, 0f);
                col.height = 1.45f * quest.Look.height;
                col.radius = 0.3f * quest.Look.width;
            }
            if (parts.body != null) bodyBase = parts.body.localPosition;
            if (parts.head != null)
            {
                headBase = parts.head.localPosition;
                headRotationBase = parts.head.localRotation;
            }
            if (parts.leftArm != null) leftArmBase = parts.leftArm.localRotation;
            if (parts.rightArm != null) rightArmBase = parts.rightArm.localRotation;

            CreateBubble();
            SetMood(Mood.None);
            lastPosition = transform.position;
        }

        /// <summary>自作モデルで来店する（足元が原点・前が +Z）</summary>
        private void BuildFromPrefab(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, transform);
            go.name = "CustomerVisual";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var model = go.GetComponent<CustomerModel>();
            if (model == null) model = go.AddComponent<CustomerModel>();
            model.AutoFill();
            animator = model.animator;
            procedural = model.proceduralMotion;
            if (animator != null)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.nameHash == SpeedId && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;
                    if (p.nameHash == MoodId && p.type == AnimatorControllerParameterType.Int) hasMoodParam = true;
                }
            }

            Bounds b = PrefabAnchors.RendererBounds(go.transform);
            parts = new CustomerBuilder.Parts
            {
                root = go.transform,
                body = model.body,
                head = model.head,
                leftArm = model.leftArm,
                rightArm = model.rightArm,
                faceAnchor = model.faceAnchor,
            };
            if (parts.faceAnchor == null)
            {
                // 顔の位置がわからないときは、頭（なければ背の高さの 85%）
                var face = new GameObject(PrefabAnchors.FaceAnchor).transform;
                face.SetParent(parts.head != null ? parts.head : go.transform, false);
                face.position = parts.head != null ? parts.head.position : new Vector3(b.center.x, b.min.y + b.size.y * 0.85f, b.center.z);
                face.rotation = transform.rotation;
                parts.faceAnchor = face;
            }

            // モデルに当たり判定がなければ、見た目に合わせたカプセルを付ける
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                Vector3 center = transform.InverseTransformPoint(b.center);
                col.center = center;
                col.height = Mathf.Max(0.5f, b.size.y);
                col.radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.4f, 0.15f, 0.6f);
            }
        }

        // ------------------------------------------------------------------
        // 顔アップ（会話ウィンドウ用）
        // ------------------------------------------------------------------

        public Texture Portrait
        {
            get
            {
                EnsurePortraitCamera();
                return portraitTexture;
            }
        }

        public void SetPortraitActive(bool active)
        {
            EnsurePortraitCamera();
            portraitCamera.enabled = active;
        }

        private void EnsurePortraitCamera()
        {
            if (portraitCamera != null) return;
            portraitTexture = new RenderTexture(320, 320, 16) { name = "Portrait_" + quest.ClientName };
            var go = new GameObject("PortraitCamera");
            go.transform.SetParent(parts.faceAnchor, false);
            go.transform.localPosition = new Vector3(0.05f, 0.0f, 0.62f);
            go.transform.localRotation = Quaternion.Euler(4f, 185f, 0f);
            portraitCamera = go.AddComponent<Camera>();
            portraitCamera.targetTexture = portraitTexture;
            portraitCamera.fieldOfView = 34f;
            portraitCamera.nearClipPlane = 0.05f;
            portraitCamera.farClipPlane = 1.1f;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(0.86f, 0.74f, 0.55f);
            portraitCamera.enabled = false;
        }

        // ------------------------------------------------------------------
        // 吹き出し
        // ------------------------------------------------------------------

        public void SetMood(Mood newMood)
        {
            mood = newMood;
            if (bubble == null) return;
            bubble.SetActive(newMood != Mood.None);
            switch (newMood)
            {
                case Mood.Exclaim: bubbleText.text = "！"; bubbleText.color = new Color(0.85f, 0.35f, 0.1f); break;
                case Mood.Waiting: bubbleText.text = "…"; bubbleText.color = UIColors.Ink; break;
                case Mood.Happy: bubbleText.text = "♪"; bubbleText.color = new Color(0.9f, 0.3f, 0.45f); break;
                case Mood.Sad: bubbleText.text = "…"; bubbleText.color = new Color(0.35f, 0.4f, 0.6f); break;
            }
        }

        private void CreateBubble()
        {
            bubble = new GameObject("Bubble", typeof(RectTransform));
            if (quest.ModelPrefab == null)
            {
                bubble.transform.SetParent(parts.head, false);
                bubble.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            }
            else
            {
                // 自作モデル：頭の上（頭がなければ見た目の一番上）に出す
                Bounds b = PrefabAnchors.RendererBounds(parts.root);
                bubble.transform.SetParent(parts.head != null ? parts.head : parts.root, false);
                bubble.transform.position = new Vector3(b.center.x, b.max.y + 0.22f, b.center.z);
            }
            bubble.transform.localScale = Vector3.one * 0.0035f;
            Vector3 ls = bubble.transform.lossyScale;
            if (ls.x > 0f) bubble.transform.localScale *= 0.0035f / ls.x; // 親の大きさに関係なく同じ大きさで見せる
            var canvas = bubble.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)bubble.transform).sizeDelta = new Vector2(100f, 100f);
            Image bg = UIFactory.CreateImage(bubble.transform, "BG", UIFactory.RoundedRect(new Color(1f, 0.98f, 0.92f), UIColors.Outline, 30, 5), Color.white);
            UIFactory.Stretch(bg.rectTransform);
            bubbleText = UIFactory.Label(bubble.transform, "Text", "！", 70, UIColors.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(bubbleText.rectTransform);
            bubble.AddComponent<Billboard>();
        }

        // ------------------------------------------------------------------
        // 移動
        // ------------------------------------------------------------------

        public IEnumerator WalkPath(IList<Vector3> points, float speed, Action onArrive)
        {
            walking = true;
            foreach (Vector3 target in points)
            {
                Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
                while ((transform.position - flatTarget).sqrMagnitude > 0.0004f)
                {
                    Vector3 dir = flatTarget - transform.position;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
                        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 8f);
                    }
                    transform.position = Vector3.MoveTowards(transform.position, flatTarget, speed * Time.deltaTime);
                    yield return null;
                }
            }
            walking = false;
            if (onArrive != null) onArrive();
        }

        public IEnumerator TurnTo(Vector3 forward, float seconds = 0.4f)
        {
            Quaternion from = transform.rotation;
            Quaternion to = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z));
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, t / seconds);
                yield return null;
            }
            transform.rotation = to;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            idlePhase += dt * 2.2f;
            if (walking) walkPhase += dt * 9f;

            if (animator != null)
            {
                speedParam = Mathf.MoveTowards(speedParam, walking ? 1f : 0f, dt * 5f);
                if (hasSpeedParam) animator.SetFloat(SpeedId, speedParam);
                if (hasMoodParam) animator.SetInteger(MoodId, (int)mood);
            }
            if (procedural) AnimateParts(dt);

            // 足音
            Vector3 moved = transform.position - lastPosition;
            lastPosition = transform.position;
            stepDistance += new Vector2(moved.x, moved.z).magnitude;
            if (walking && stepDistance > 0.45f)
            {
                stepDistance = 0f;
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null) AudioManager.SeAt(lib.RandomFootstep(), transform.position, 0.5f, UnityEngine.Random.Range(1.05f, 1.2f));
            }
        }

        /// <summary>体の上下（歩くと大きく）・腕振り・プレイヤーの方を見る（部品がないモデルでは、その部分だけ動かさない）</summary>
        private void AnimateParts(float dt)
        {
            float bob = walking ? Mathf.Abs(Mathf.Sin(walkPhase)) * 0.035f : Mathf.Sin(idlePhase) * 0.008f;
            if (parts.body != null) parts.body.localPosition = bodyBase + new Vector3(0f, bob, 0f);
            if (parts.head != null) parts.head.localPosition = headBase + new Vector3(0f, bob, 0f);
            float swing = walking ? Mathf.Sin(walkPhase) * 25f : Mathf.Sin(idlePhase * 0.7f) * 3f;
            if (parts.leftArm != null) parts.leftArm.localRotation = Quaternion.Euler(swing, 0f, 0f) * leftArmBase;
            if (parts.rightArm != null)
            {
                parts.rightArm.localRotation = mood == Mood.Happy && !walking
                    ? Quaternion.Euler(0f, 0f, 138f + Mathf.Sin(idlePhase * 4f) * 20f) * rightArmBase // 手を振る
                    : Quaternion.Euler(-swing, 0f, 0f) * rightArmBase;
            }

            // 近くのプレイヤーの方へ顔を向ける
            if (parts.head == null) return;
            Camera cam = Camera.main;
            Quaternion headTarget = Quaternion.identity;
            if (cam != null && !walking)
            {
                Vector3 local = transform.InverseTransformPoint(cam.transform.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -45f, 45f);
                float pitch = Mathf.Clamp(-Mathf.Atan2(local.y - 1.2f, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
                if (local.z > 0f) headTarget = Quaternion.Euler(pitch, yaw, Mathf.Sin(idlePhase * 0.5f) * 3f);
            }
            parts.head.localRotation = Quaternion.Slerp(parts.head.localRotation, headRotationBase * headTarget, dt * 5f);
        }

        private void OnDestroy()
        {
            if (portraitTexture != null)
            {
                portraitTexture.Release();
                Destroy(portraitTexture);
            }
            // お客さんごとに複製した色違いのマテリアルも片付ける（来店のたびにメモリが増えないように）
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.name.StartsWith("Customer_")) Destroy(m);
                }
            }
        }

        // ------------------------------------------------------------------
        // Interactable
        // ------------------------------------------------------------------

        public override string GetDisplayName(PlayerInteractor player)
        {
            return quest != null ? quest.ClientName : "お客さん";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            return quest != null ? quest.ClientTitle : null;
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            if (manager != null) manager.GetCustomerOptions(this, player, options);
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key == KeyCode.E && manager != null) manager.TalkTo(this, player);
        }
    }
}
