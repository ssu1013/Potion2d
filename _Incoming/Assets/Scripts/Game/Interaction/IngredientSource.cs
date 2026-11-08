using System.Collections.Generic;
using System.Text;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 棚に置かれた素材。視線を合わせて [E] で手に取る。在庫が0なら取れない。
    /// 見た目は素材データのモデル（なければ仮の容器）を実行時に置く。
    /// </summary>
    public class IngredientSource : Interactable
    {
        [SerializeField] private IngredientData ingredient;

        private IngredientInventory inventory;
        private Transform model;
        private ContentVisual content;
        private string cachedAttributes;
        private string cachedSubtitle;
        private float cachedStock = -1f;

        public IngredientData Ingredient => ingredient;
        public Transform Model => model != null ? model : transform;

        public void Setup(IngredientData data, IngredientInventory inventory)
        {
            ingredient = data;
            this.inventory = inventory;
            BuildModel();
            if (inventory != null) inventory.Changed += OnStockChanged;
            RefreshStock();
        }

        private void OnDestroy()
        {
            if (inventory != null) inventory.Changed -= OnStockChanged;
        }

        private void OnStockChanged(IngredientData changed)
        {
            if (changed == null || changed == ingredient) RefreshStock();
        }

        private void BuildModel()
        {
            GameObject prefab = GameContext.Art != null ? GameContext.Art.IngredientModel(ingredient) : null;
            GameObject go = prefab != null ? Instantiate(prefab, transform) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Model";
            go.transform.SetParent(transform, false);
            if (prefab == null) go.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f);
            go.transform.localPosition = prefab == null ? new Vector3(0f, 0.08f, 0f) : Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            model = go.transform;

            // 当たり判定はこちらで1つにまとめる（モデル側の当たり判定は外す）
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            content = go.GetComponent<ContentVisual>();
            if (content != null) content.SetColor(ingredient.Color);

            Bounds b = PrefabAnchors.RendererBounds(go.transform);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(b.center);
            Vector3 s = transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z) + Vector3.one * 0.02f;
        }

        private void RefreshStock()
        {
            float stock = Stock;
            if (content != null)
            {
                GameBalance balance = GameContext.Balance;
                float full = balance != null ? balance.containerFullAmount : 8f;
                content.SetAmount(stock < IngredientInventory.Epsilon ? 0f : Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(stock / full)));
            }
        }

        private float Stock => inventory != null ? inventory.Get(ingredient) : float.PositiveInfinity;

        // ------------------------------------------------------------------
        // Interactable
        // ------------------------------------------------------------------

        public override string GetDisplayName(PlayerInteractor player)
        {
            return ingredient != null ? ingredient.DisplayName : "（空の容器）";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (ingredient == null) return null;
            float stock = Stock;
            if (cachedSubtitle != null && Mathf.Approximately(stock, cachedStock)) return cachedSubtitle;
            cachedStock = stock;
            if (cachedAttributes == null) cachedAttributes = DescribeAttributes(ingredient, player.Alchemy != null ? player.Alchemy.Settings : null);
            string stockText = float.IsInfinity(stock) ? "　<color=#9EE08A>使い放題</color>" :
                stock >= IngredientInventory.Epsilon ? $"　在庫 {IngredientInventory.Format(ingredient, stock)}" :
                IngredientInventory.IsInfiniteKind(ingredient) ? "　<color=#F08A6A>まだ取引していない（取引先帳で開拓できる）</color>" :
                "　<color=#F08A6A>在庫なし（取引先帳で取り寄せられる）</color>";
            cachedSubtitle = cachedAttributes + stockText;
            return cachedSubtitle;
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            if (ingredient == null) return;
            if (player.HeldIngredient == ingredient)
            {
                options.Add(new InteractionOption(KeyCode.E, "E", "棚に戻す"));
                return;
            }
            if (Stock < IngredientInventory.Epsilon) return; // 在庫がないので取れない
            options.Add(new InteractionOption(KeyCode.E, "E", player.HeldIngredient == null ? "取る" : "持ちかえる"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            if (key != KeyCode.E || ingredient == null) return;
            if (player.HeldIngredient == ingredient) player.ClearHeld();
            else player.HoldIngredient(ingredient);
        }

        /// <summary>素材の説明（カテゴリと属性）。素材箱や取引先帳でも使う</summary>
        public static string DescribeAttributes(IngredientData data, AlchemySettings settings)
        {
            var sb = new StringBuilder(CategoryLabel(data.Category));
            foreach (AttributeValue av in data.Attributes)
            {
                string attributeName = settings != null ? settings.GetDisplayName(av.attribute) : av.attribute.ToString();
                sb.Append("　").Append(attributeName).Append(av.value > 0 ? "+" : "").Append(av.value.ToString("0.#"));
            }
            if (data.GlobalAmplifyPercent != 0f)
            {
                sb.Append("　全属性 ×").Append((1f + data.GlobalAmplifyPercent / 100f).ToString("0.##"));
            }
            foreach (AttributeAmplifier amp in data.Amplifiers)
            {
                string attributeName = settings != null ? settings.GetDisplayName(amp.attribute) : amp.attribute.ToString();
                sb.Append("　").Append(attributeName).Append(" ×").Append((1f + amp.percent / 100f).ToString("0.##"));
            }
            return sb.ToString();
        }

        public static string CategoryLabel(IngredientCategory category)
        {
            switch (category)
            {
                case IngredientCategory.BaseLiquid: return "ベース液体";
                case IngredientCategory.MainMaterial: return "主素材";
                case IngredientCategory.Catalyst: return "触媒";
                default: return category.ToString();
            }
        }

        public static string FormLabel(IngredientForm form)
        {
            switch (form)
            {
                case IngredientForm.Liquid: return "液体";
                case IngredientForm.Powder: return "粉";
                default: return "固体";
            }
        }
    }
}
