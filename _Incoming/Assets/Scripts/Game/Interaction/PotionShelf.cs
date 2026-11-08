using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 瓶詰めした液体を並べる棚。[E] で棚のウィンドウ（素材箱の「自作の液体」タブ）を開く／手に持った瓶を戻す。
    /// しまった液体は種類ごとに1本ずつ、新しい順に棚の Prefab の「Slot_00」「Slot_01」…の位置に実物の瓶として並び、
    /// 瓶を見て [E] で直接取り出せる（取り出した瓶はフラスコに注ぎ戻したり、相談に来た人に分け与えたりできる）。
    /// 棚を自作モデルにするときは、Slot_ の目印を置けばよい。
    /// </summary>
    public class PotionShelf : Interactable
    {
        [SerializeField] private PotionInventory inventory;

        private readonly List<GameObject> bottles = new List<GameObject>();
        private readonly List<PotionStack> shown = new List<PotionStack>();
        private readonly List<PotionStack> order = new List<PotionStack>();
        private List<Transform> slots;

        public void Configure(PotionInventory inventory)
        {
            this.inventory = inventory;
        }

        private void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            slots = IngredientShelf.FindSlots(transform);
        }

        private void OnEnable()
        {
            if (inventory != null) inventory.Changed += RebuildBottles;
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.Changed -= RebuildBottles;
        }

        private void Start()
        {
            RebuildBottles();
        }

        public override string GetDisplayName(PlayerInteractor player)
        {
            return "ポーション棚";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            return inventory != null ? $"{inventory.Stacks.Count} 種類・{inventory.TotalCount} 本" : null;
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            if (player.HeldPotion != null) options.Add(new InteractionOption(KeyCode.E, "E", "持っている瓶を戻す"));
            else options.Add(new InteractionOption(KeyCode.E, "E", "棚を見る"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key != KeyCode.E) return;
            if (player.HeldPotion != null)
            {
                player.ClearHeld();
                return;
            }
            if (AtelierHUD.Instance != null) AtelierHUD.Instance.OpenShelf();
        }

        private void RebuildBottles()
        {
            if (inventory == null || slots == null) return;
            // 新しく瓶詰めした順に並べる（並ぶ物が変わらなければ作り直さない）
            order.Clear();
            order.AddRange(inventory.Stacks);
            order.Sort((a, b) => b.addedOrder.CompareTo(a.addedOrder));
            if (order.Count > slots.Count) order.RemoveRange(slots.Count, order.Count - slots.Count);
            bool same = order.Count == shown.Count && bottles.Count == shown.Count;
            for (int i = 0; same && i < order.Count; i++) same = order[i] == shown[i] && order[i].color == shown[i].color;
            if (same) return;

            foreach (GameObject bottle in bottles)
            {
                if (bottle != null) Destroy(bottle);
            }
            bottles.Clear();
            shown.Clear();

            for (int i = 0; i < order.Count; i++)
            {
                PotionStack stack = order[i];
                shown.Add(stack);
                GameObject root = PotionVisuals.CreateBottle(stack.color, stack.baseLiquidId, slots[i], "Potion_" + stack.potionName);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                var bottle = root.AddComponent<PotionBottle>();
                bottle.Configure(inventory, stack);
                Bounds b = PrefabAnchors.RendererBounds(root.transform);
                var col = root.AddComponent<BoxCollider>();
                col.center = root.transform.InverseTransformPoint(b.center);
                Vector3 s = root.transform.lossyScale;
                col.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z) + Vector3.one * 0.01f;
                bottles.Add(root);
            }
        }
    }
}
