using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画面中央の視線の先にあるものを調べ、光らせ、キー操作を渡す。手に持っている物（素材 or ポーション）も管理する。
    /// 操作キー: E（左クリック）=メイン操作 / Q=持っている物を戻す / Tab=ポーション棚 / I=素材箱（どちらも同じ一覧）
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private HeldItemView heldView;
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField, Tooltip("手が届く距離（m）")] private float reach = 2.6f;
        [SerializeField] private LayerMask layerMask = ~0;

        private readonly List<InteractionOption> options = new List<InteractionOption>();
        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private Interactable holdTarget;
        private KeyCode holdKey;

        public Interactable Target { get; private set; }
        public IngredientData HeldIngredient { get; private set; }
        public PotionStack HeldPotion { get; private set; }
        public HeldItemView HeldView => heldView;
        public AlchemyManager Alchemy => alchemy;
        public IReadOnlyList<InteractionOption> Options => options;
        public IngredientInventory Ingredients => ingredients;

        public void Configure(Camera viewCamera, HeldItemView heldView, AlchemyManager alchemy, PotionInventory inventory, IngredientInventory ingredients)
        {
            this.viewCamera = viewCamera;
            this.heldView = heldView;
            this.alchemy = alchemy;
            this.inventory = inventory;
            this.ingredients = ingredients;
        }

        private void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
        }

        private void OnDisable()
        {
            EndHold();
            SetTarget(null);
            options.Clear();
        }

        private void Update()
        {
            AtelierHUD hud = AtelierHUD.Instance;

            // 素材箱とポーション棚は同じ一覧（Tab は「自作の液体」タブ、I は「すべて」タブで開く）。R はレシピ本
            if (Input.GetKeyDown(KeyCode.Tab) && hud != null && !hud.IsDialogueOrMenuOpen)
            {
                hud.ToggleShelf();
            }
            else if (Input.GetKeyDown(KeyCode.I) && hud != null && !hud.IsDialogueOrMenuOpen)
            {
                hud.ToggleIngredientBox();
            }
            else if (Input.GetKeyDown(KeyCode.R) && hud != null)
            {
                // レシピ本（開くかどうか・相談に乗っている途中かどうかは HUD が決める）
                hud.ToggleRecipeBook();
            }

            // 調合台に向かっている間は、調べる操作の代わりに BrewMode がカーソルで作業する
            BrewMode brew = BrewMode.Instance;
            if (brew != null && brew.IsActive)
            {
                EndHold();
                SetTarget(null);
                options.Clear();
                return;
            }

            // ウィンドウ表示中・閉じた直後のフレーム・調合結果の表示中は、調べる操作をしない
            bool blocked = hud != null && hud.BlocksPlayerActions;
            if (blocked)
            {
                EndHold();
                SetTarget(null);
                options.Clear();
                hud.SetPrompt(null, null, null, options);
                return;
            }

            UpdateTarget();
            HandleInput(hud);

            if (hud != null)
            {
                string title = Target != null ? Target.GetDisplayName(this) : null;
                string subtitle = Target != null ? Target.GetSubtitle(this) : null;
                hud.SetPrompt(Target, title, subtitle, options);
            }
        }

        // ------------------------------------------------------------------
        // 手に持つ
        // ------------------------------------------------------------------

        /// <summary>素材を手に持つ（在庫がなければ持てない。ポーションを持っていたら棚に戻す）</summary>
        public bool HoldIngredient(IngredientData data)
        {
            if (data == null) return false;
            if (ingredients != null && !ingredients.Has(data))
            {
                AtelierHUD hud = AtelierHUD.Instance;
                if (hud != null) hud.ShowToast(MissingMessage(data));
                return false;
            }
            EndHold();
            ReturnHeldPotion(false);
            HeldIngredient = data;
            if (heldView != null) heldView.ShowIngredient(data, HeldAmountRatio(data));
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.pickup, 0.8f, Random.Range(0.95f, 1.08f));
            return true;
        }

        /// <summary>持てない素材の案内（使い放題の素材はまだ取引していない／希少な素材は在庫切れ）</summary>
        public static string MissingMessage(IngredientData data)
        {
            if (IngredientInventory.IsInfiniteKind(data))
                return $"「{data.DisplayName}」はまだ取引していません（取引先帳で入手ルートを開拓できます）";
            return $"「{data.DisplayName}」の在庫がありません（取引先帳で取り寄せられます）";
        }

        /// <summary>手に持っている容器の中身の見た目（在庫の量に合わせる）</summary>
        public float HeldAmountRatio(IngredientData data)
        {
            if (ingredients == null || data == null) return 1f;
            float stock = ingredients.Get(data);
            if (stock < IngredientInventory.Epsilon) return 0f;
            GameBalance balance = GameContext.Balance;
            float full = balance != null ? balance.containerFullAmount : 8f;
            return Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(stock / full));
        }

        /// <summary>手に持っている素材を使い切ったとき（持っている物を消す。棚に戻す音は鳴らさない）</summary>
        public void UsedUpHeldIngredient()
        {
            if (HeldIngredient == null) return;
            EndHold();
            string name = HeldIngredient.DisplayName;
            HeldIngredient = null;
            if (heldView != null) heldView.Clear();
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast($"「{name}」を使い切った");
        }

        /// <summary>ポーション棚から1本取り出して手に持つ（満タンの瓶があれば満タンの瓶、なければ開けた瓶）</summary>
        public bool HoldPotion(PotionInventory from, PotionStack stack)
        {
            if (from == null || stack == null) return false;
            EndHold();
            ReturnHeldPotion(false);
            PotionStack single = from.TakeOne(stack);
            if (single == null) return false;
            HeldIngredient = null;
            HeldPotion = single;
            if (heldView != null) heldView.ShowPotion(single);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.pickup, 0.8f, Random.Range(1.05f, 1.15f));
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast(single.IsFullBottle ? $"「{single.potionName}」を手に取った" : $"「{single.potionName}」の開けた瓶（{single.ml:0}ml）を手に取った");
            return true;
        }

        /// <summary>手に持っている瓶の中身が減ったとき（注いだ）。空になったら瓶は片付ける</summary>
        public void HeldPotionPoured()
        {
            if (HeldPotion == null) return;
            if (HeldPotion.ml >= PotionStack.EmptyMl)
            {
                if (heldView != null) heldView.SetAmount(HeldPotion.ml / PotionStack.BottleMl);
                return;
            }
            EndHold();
            string name = HeldPotion.potionName;
            HeldPotion = null;
            if (heldView != null) heldView.Clear();
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast($"「{name}」の瓶が空になった");
        }

        /// <summary>手に持っている物を手放す（ポーションは棚に戻る）</summary>
        public void ClearHeld()
        {
            bool hadSomething = HeldIngredient != null || HeldPotion != null;
            EndHold();
            ReturnHeldPotion(true);
            HeldIngredient = null;
            if (heldView != null) heldView.Clear();
            SoundLibrary lib = AudioManager.Lib;
            if (hadSomething && lib != null) AudioManager.Se(lib.putBack, 0.7f);
        }

        /// <summary>手に持っているポーションを渡す（棚には戻らない）</summary>
        public void ConsumeHeldPotion()
        {
            HeldPotion = null;
            if (heldView != null) heldView.Clear();
        }

        private void ReturnHeldPotion(bool toast)
        {
            if (HeldPotion == null) return;
            if (inventory != null) inventory.ReturnOne(HeldPotion);
            if (toast && AtelierHUD.Instance != null) AtelierHUD.Instance.ShowToast($"「{HeldPotion.potionName}」を棚に戻した");
            HeldPotion = null;
            if (heldView != null) heldView.Clear();
        }

        // ------------------------------------------------------------------

        private void UpdateTarget()
        {
            Interactable found = null;
            if (viewCamera != null)
            {
                // カメラはプレイヤー自身の当たり判定（CharacterController）の中にあるので、
                // 当たったものを近い順に見て、自分の当たり判定は飛ばす
                Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                int count = Physics.RaycastNonAlloc(ray, hitBuffer, reach, layerMask, QueryTriggerInteraction.Collide);
                float nearest = float.MaxValue;
                Collider nearestCollider = null;
                for (int i = 0; i < count; i++)
                {
                    Collider c = hitBuffer[i].collider;
                    if (c == null || c.transform.IsChildOf(transform)) continue;
                    if (hitBuffer[i].distance < nearest)
                    {
                        nearest = hitBuffer[i].distance;
                        nearestCollider = c;
                    }
                }
                if (nearestCollider != null) found = nearestCollider.GetComponentInParent<Interactable>();
            }

            if (found != Target)
            {
                EndHold();
                SetTarget(found);
            }

            options.Clear();
            if (Target != null) Target.GetOptions(this, options);
        }

        private void HandleInput(AtelierHUD hud)
        {
            if (Input.GetKeyDown(KeyCode.Q) && (HeldIngredient != null || HeldPotion != null))
            {
                string name = HeldPotion != null ? HeldPotion.potionName : HeldIngredient.DisplayName;
                bool potion = HeldPotion != null;
                ClearHeld();
                if (hud != null && !potion) hud.ShowToast($"{name}を棚に戻した");
                return;
            }

            if (Target != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    InteractionOption option = options[i];
                    if (option.hold)
                    {
                        if (IsKeyHeld(option.key))
                        {
                            if (holdTarget == null)
                            {
                                holdTarget = Target;
                                holdKey = option.key;
                            }
                            if (holdTarget == Target && holdKey == option.key)
                            {
                                Target.OnHold(this, option.key, Time.deltaTime);
                            }
                        }
                    }
                    else if (IsKeyDown(option.key))
                    {
                        Target.OnPress(this, option.key);
                        break; // 状態が変わるので、このフレームはここまで
                    }
                }
            }

            if (holdTarget != null && (holdTarget != Target || !IsKeyHeld(holdKey)))
            {
                EndHold();
            }
        }

        private void EndHold()
        {
            if (holdTarget == null) return;
            Interactable target = holdTarget;
            KeyCode key = holdKey;
            holdTarget = null;
            if (target != null) target.OnRelease(this, key);
        }

        private void SetTarget(Interactable target)
        {
            if (target == Target) return;
            HighlightOutline.Hide(Target);
            Target = target;
            HighlightOutline.Show(target);
        }

        private static bool IsMouseUsable => Cursor.lockState == CursorLockMode.Locked;

        private static bool IsKeyDown(KeyCode key)
        {
            return Input.GetKeyDown(key) || (key == KeyCode.E && IsMouseUsable && Input.GetMouseButtonDown(0));
        }

        private static bool IsKeyHeld(KeyCode key)
        {
            return Input.GetKey(key) || (key == KeyCode.E && IsMouseUsable && Input.GetMouseButton(0));
        }
    }
}
