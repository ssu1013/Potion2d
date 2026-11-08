using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 素材棚。棚の Prefab の中にある「Slot_00」「Slot_01」…という目印の位置に、指定した素材を並べる。
    /// 並べる素材はインスペクターの「棚に並べる素材」で入れ替えられる（全種類は素材箱から取り出せる）。
    /// 素材の見た目は素材データのモデル（なければ仮の容器）。在庫が減ると中身が減って見え、0なら空になる。
    /// </summary>
    public class IngredientShelf : MonoBehaviour
    {
        [SerializeField] private IngredientInventory inventory;
        [SerializeField, Tooltip("棚に並べる素材。上から順に Slot_00, Slot_01 … に置く（空欄のマスには何も置かない）")]
        private List<IngredientData> displayed = new List<IngredientData>();

        private readonly List<IngredientSource> sources = new List<IngredientSource>();

        public IReadOnlyList<IngredientSource> Sources => sources;

        public void Configure(IngredientInventory inventory, List<IngredientData> displayed)
        {
            this.inventory = inventory;
            this.displayed = displayed;
        }

        /// <summary>シーンにあとから置いたときなど、在庫の参照が空なら探す</summary>
        private void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<IngredientInventory>();
        }

        private void Start()
        {
            Build();
        }

        private void Build()
        {
            foreach (IngredientSource s in sources)
            {
                if (s != null) Destroy(s.gameObject);
            }
            sources.Clear();

            List<Transform> slots = FindSlots(transform);
            for (int i = 0; i < slots.Count && i < displayed.Count; i++)
            {
                IngredientData data = displayed[i];
                if (data == null) continue;
                var go = new GameObject("Ingredient_" + data.DisplayName);
                go.transform.SetParent(slots[i], false);
                var source = go.AddComponent<IngredientSource>();
                source.Setup(data, inventory);
                sources.Add(source);
            }
        }

        /// <summary>「Slot_」で始まる目印を名前順に集める</summary>
        public static List<Transform> FindSlots(Transform root)
        {
            var list = new List<Transform>();
            Collect(root, list);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private static void Collect(Transform t, List<Transform> list)
        {
            foreach (Transform child in t)
            {
                if (child.name.StartsWith("Slot_")) list.Add(child);
                else Collect(child, list);
            }
        }
    }
}
