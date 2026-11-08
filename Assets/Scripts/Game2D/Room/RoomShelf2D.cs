using System.Collections.Generic;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 部屋の瓶の棚（自分で瓶詰めした液体が、新しい順に並ぶ）。置き場所は子の「Slot_00〜」。
    /// 瓶の絵は BrewArt2D の「自作の液体の瓶」（中身は液体の色で塗られる）。
    /// </summary>
    public class RoomShelf2D : MonoBehaviour
    {
        [SerializeField, Tooltip("置き場所（空なら「Slot_」で始まる子を名前順に使う）")] private List<Transform> slots = new List<Transform>();
        [SerializeField, Min(0.1f), Tooltip("棚に並べたときの瓶の高さ（ワールド単位）")] private float height = 0.85f;
        [SerializeField] private string sortingLayer = "Room";
        [SerializeField] private int sortingOrder = 30;

        private readonly List<GameObject> shown = new List<GameObject>();
        private readonly List<PotionStack> buffer = new List<PotionStack>();
        private BrewSession2D session;
        private PotionInventory potions;
        private bool dirty = true;

        private void Awake()
        {
            if (slots.Count == 0)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child != transform && child.name.StartsWith("Slot_")) slots.Add(child);
                }
                slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            }
        }

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            potions = FindFirstObjectByType<PotionInventory>();
            if (potions != null) potions.Changed += MarkDirty;
        }

        private void OnDestroy()
        {
            if (potions != null) potions.Changed -= MarkDirty;
        }

        private void MarkDirty() => dirty = true;

        private void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            foreach (GameObject go in shown) if (go != null) Destroy(go);
            shown.Clear();
            if (potions == null || session == null || session.Art == null) return;
            buffer.Clear();
            foreach (PotionStack s in potions.Stacks) if (s != null && s.ml >= PotionStack.EmptyMl) buffer.Add(s);
            buffer.Sort((a, b) => b.addedOrder.CompareTo(a.addedOrder));
            BrewArt2D.ContainerArt art = session.Art.potionBottle;
            for (int i = 0; i < slots.Count && i < buffer.Count; i++)
            {
                if (slots[i] == null) continue;
                var root = new GameObject("Bottle_" + i.ToString("00"));
                root.transform.SetParent(slots[i], false);
                var artGo = new GameObject("Art");
                artGo.transform.SetParent(root.transform, false);
                SpriteRenderer content = Add(artGo.transform, "Content", art.content, sortingOrder);
                Color c = buffer[i].color;
                c.a = 0.92f;
                content.color = c;
                Add(artGo.transform, "Body", art.body, sortingOrder + 1);
                Sprite reference = art.body != null ? art.body : art.content;
                Vector2 size = ArtFit2D.Fit(artGo.transform, reference, height, height);
                // 置き場所は瓶の底
                artGo.transform.localPosition += new Vector3(0f, size.y * 0.5f, 0f);
                shown.Add(root);
            }
        }

        private SpriteRenderer Add(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
