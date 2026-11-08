using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 一人称の移動とカメラ操作（WASD・マウス／矢印キー・Shiftで走る）と足音。
    /// メニューや会話を開いている間は動かない（カーソルの表示は AtelierHUD が管理する）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float walkSpeed = 2.4f;
        [SerializeField] private float runSpeed = 4.0f;
        [SerializeField, Tooltip("矢印キーで視点を回す速さ（度/秒）")] private float keyLookSpeed = 70f;
        [SerializeField] private float gravity = -15f;
        [SerializeField, Tooltip("足音の間隔（m）")] private float stepInterval = 0.62f;

        private const float EyeHeight = 1.55f;

        private CharacterController characterController;
        private float pitch;
        private float verticalVelocity;
        private float stepDistance;
        private float headBob;
        /// <summary>カーソルを捕まえ直した直後は、マウスの動きを数フレーム無視する（捕まえ直すときのカーソルの飛びで、視点がガクッと動かないように）</summary>
        private int mouseSettleFrames = 3;
        private const int MouseSettleFrameCount = 3;

        /// <summary>移動の強さ（0〜1）。手に持った物の揺れに使う</summary>
        public float MoveAmount { get; private set; }

        /// <summary>false にすると移動・視点操作を受け付けない（タイトル・演出・自動テスト用）</summary>
        public bool InputEnabled { get; set; } = true;

        public void Configure(Transform cameraPivot)
        {
            this.cameraPivot = cameraPivot;
        }

        /// <summary>指定した位置を見る（演出や自動テスト用）</summary>
        public void LookAt(Vector3 worldPoint)
        {
            Vector3 from = cameraPivot != null ? cameraPivot.position : transform.position;
            Vector3 dir = worldPoint - from;
            if (dir.sqrMagnitude < 1e-6f) return;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float flat = new Vector2(dir.x, dir.z).magnitude;
            pitch = Mathf.Clamp(-Mathf.Atan2(dir.y, flat) * Mathf.Rad2Deg, -80f, 80f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot != null) cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        /// <summary>瞬間移動（演出や自動テスト用）</summary>
        public void Teleport(Vector3 position)
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = true;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            AtelierHUD hud = AtelierHUD.Instance;
            // 調合台に向かっている間は、マウスはカーソルの作業に使うので、歩く・見回すのを止める
            bool blocked = !InputEnabled || (hud != null && (hud.IsModalOpen || hud.BrewModeActive));
            if (blocked)
            {
                mouseSettleFrames = MouseSettleFrameCount;
                MoveAmount = Mathf.MoveTowards(MoveAmount, 0f, Time.deltaTime * 6f);
                ApplyGravityOnly();
                return;
            }

            // 画面クリックでカーソルを再ロック（Esc でポーズしたあと等）
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                SetCursorLocked(true);
            }

            float sensitivity = GameSettings.MouseSensitivity;
            float yawDelta = 0f;
            float pitchDelta = 0f;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                mouseSettleFrames = MouseSettleFrameCount;
            }
            else if (mouseSettleFrames > 0)
            {
                // 素材箱・棚・会話などを閉じてカーソルを捕まえ直した直後：カーソルが画面の中心へ飛んだ分を視点の動きにしない
                mouseSettleFrames--;
            }
            else
            {
                yawDelta += Input.GetAxis("Mouse X") * sensitivity;
                pitchDelta -= Input.GetAxis("Mouse Y") * sensitivity;
            }
            float keyYaw = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float keyPitch = (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f);
            yawDelta += keyYaw * keyLookSpeed * Time.deltaTime;
            pitchDelta += keyPitch * keyLookSpeed * Time.deltaTime;

            transform.Rotate(0f, yawDelta, 0f);
            pitch = Mathf.Clamp(pitch + pitchDelta, -80f, 80f);

            // 移動は WASD だけ（Unity の標準の移動の軸は矢印キーも含むが、矢印キーは視点を回すのに使うので）
            float moveX = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float moveZ = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            var input = new Vector3(moveX, 0f, moveZ);
            input = Vector3.ClampMagnitude(input, 1f);
            bool running = Input.GetKey(KeyCode.LeftShift);
            float speed = running ? runSpeed : walkSpeed;
            Vector3 move = transform.TransformDirection(input) * speed;

            if (characterController.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;

            Vector3 before = transform.position;
            characterController.Move(move * Time.deltaTime);
            Vector3 delta = transform.position - before;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            // 歩き始め・止まりで手元の表示がガクッと動かないよう、なめらかに変える
            MoveAmount = Mathf.MoveTowards(MoveAmount, input.magnitude, Time.deltaTime * 6f);

            // 足音
            if (characterController.isGrounded && horizontal > 0.0005f)
            {
                stepDistance += horizontal;
                headBob += horizontal / stepInterval * Mathf.PI;
                if (stepDistance >= stepInterval * (running ? 1.25f : 1f))
                {
                    stepDistance = 0f;
                    SoundLibrary lib = AudioManager.Lib;
                    if (lib != null) AudioManager.Se(lib.RandomFootstep(), running ? 0.55f : 0.4f, Random.Range(0.9f, 1.08f));
                }
            }

            // 視点：初期設定では揺らさない（設定で「歩くときの揺れ」をオンにしたときだけ、ごく小さく上下させる）
            float bobOffset = GameSettings.ViewBob ? Mathf.Sin(headBob * 2f) * 0.008f * MoveAmount : 0f;
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                cameraPivot.localPosition = new Vector3(0f, EyeHeight + bobOffset, 0f);
            }
        }

        private void ApplyGravityOnly()
        {
            if (characterController == null || !characterController.enabled) return;
            if (characterController.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            characterController.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
