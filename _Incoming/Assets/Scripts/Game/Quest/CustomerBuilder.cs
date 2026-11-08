using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// お客さん（3頭身のキャラクター）を単純な形の組み合わせで作る。
    /// 前は +Z 方向。足元が原点。本物のモデルができたら差し替える前提の仮の見た目。
    /// </summary>
    public static class CustomerBuilder
    {
        private static Mesh robeMesh;
        private static Mesh pointyHatMesh;
        private static Mesh strawCrownMesh;

        public sealed class Parts
        {
            public Transform root;
            public Transform body;
            public Transform head;
            public Transform leftArm;
            public Transform rightArm;
            public Transform faceAnchor;
        }

        public static Parts Build(Transform parent, CustomerLook look, Material baseMaterial)
        {
            EnsureMeshes();
            var cache = new Dictionary<Color, Material>();
            Material M(Color c, bool box = false, float outline = 2.2f)
            {
                Color key = new Color(c.r, c.g, c.b, box ? 0.5f : 1f);
                if (cache.TryGetValue(key, out Material m)) return m;
                m = new Material(baseMaterial) { name = "Customer_" + ColorUtility.ToHtmlStringRGB(c) };
                m.SetColor("_Color", c);
                m.SetFloat("_OutlineUsePosition", box ? 1f : 0f);
                m.SetFloat("_OutlineWidth", outline);
                cache[key] = m;
                return m;
            }

            var parts = new Parts();
            parts.root = new GameObject("CustomerVisual").transform;
            parts.root.SetParent(parent, false);
            parts.root.localScale = new Vector3(look.width, look.height, look.width) * 1.0f;

            // 体（ローブ）
            parts.body = new GameObject("Body").transform;
            parts.body.SetParent(parts.root, false);
            MeshPart("Robe", parts.body, robeMesh, M(look.cloth), Vector3.zero, Vector3.one);
            Primitive(PrimitiveType.Cylinder, "Belt", parts.body, new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0.025f, 0.36f), M(look.accent, true));
            Primitive(PrimitiveType.Sphere, "Foot_L", parts.root, new Vector3(-0.08f, 0.03f, 0.05f), new Vector3(0.11f, 0.07f, 0.16f), M(new Color(0.3f, 0.2f, 0.14f)));
            Primitive(PrimitiveType.Sphere, "Foot_R", parts.root, new Vector3(0.08f, 0.03f, 0.05f), new Vector3(0.11f, 0.07f, 0.16f), M(new Color(0.3f, 0.2f, 0.14f)));

            // 腕
            parts.leftArm = Arm("Arm_L", parts.body, -1, look, M(look.cloth), M(look.skin));
            parts.rightArm = Arm("Arm_R", parts.body, 1, look, M(look.cloth), M(look.skin));

            // 頭
            parts.head = new GameObject("Head").transform;
            parts.head.SetParent(parts.root, false);
            parts.head.localPosition = new Vector3(0f, 0.98f, 0f);
            Primitive(PrimitiveType.Sphere, "Face", parts.head, new Vector3(0f, 0.2f, 0f), new Vector3(0.42f, 0.4f, 0.4f), M(look.skin));

            // 目・ほっぺ・口
            Material eye = M(new Color(0.12f, 0.08f, 0.06f), false, 0f);
            Material white = M(Color.white, false, 0f);
            Material blush = M(new Color(1f, 0.6f, 0.6f), false, 0f);
            foreach (int side in new[] { -1, 1 })
            {
                Primitive(PrimitiveType.Sphere, "Eye", parts.head, new Vector3(0.075f * side, 0.2f, 0.185f), new Vector3(0.05f, 0.075f, 0.03f), eye);
                Primitive(PrimitiveType.Sphere, "EyeShine", parts.head, new Vector3(0.075f * side + 0.012f, 0.215f, 0.198f), new Vector3(0.016f, 0.02f, 0.01f), white);
                Primitive(PrimitiveType.Sphere, "Blush", parts.head, new Vector3(0.12f * side, 0.15f, 0.165f), new Vector3(0.06f, 0.03f, 0.02f), blush);
            }
            Primitive(PrimitiveType.Sphere, "Mouth", parts.head, new Vector3(0f, 0.125f, 0.192f), new Vector3(0.04f, 0.015f, 0.015f), M(new Color(0.55f, 0.25f, 0.2f), false, 0f));

            BuildHair(parts.head, look, M(look.hair));
            if (look.beard)
            {
                // あごひげ＋口ひげ（口と目は隠さない）
                Primitive(PrimitiveType.Sphere, "Beard", parts.head, new Vector3(0f, 0.045f, 0.13f), new Vector3(0.24f, 0.13f, 0.15f), M(look.hair));
                foreach (int side in new[] { -1, 1 })
                {
                    Primitive(PrimitiveType.Sphere, "Mustache", parts.head, new Vector3(0.038f * side, 0.142f, 0.188f), new Vector3(0.07f, 0.026f, 0.03f), M(look.hair, false, 1.2f));
                }
            }
            BuildHat(parts.head, look, M(look.accent), M(new Color(0.85f, 0.72f, 0.42f)));

            // 会話ウィンドウの顔アップ用カメラの位置
            parts.faceAnchor = new GameObject("FaceAnchor").transform;
            parts.faceAnchor.SetParent(parts.head, false);
            parts.faceAnchor.localPosition = new Vector3(0f, 0.2f, 0f);
            return parts;
        }

        private static Transform Arm(string name, Transform body, int side, CustomerLook look, Material cloth, Material skin)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.2f * side, 0.82f, 0f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, 12f * side);
            Primitive(PrimitiveType.Capsule, "Sleeve", pivot, new Vector3(0f, -0.17f, 0f), new Vector3(0.11f, 0.17f, 0.11f), cloth);
            Primitive(PrimitiveType.Sphere, "Hand", pivot, new Vector3(0f, -0.36f, 0.01f), new Vector3(0.09f, 0.09f, 0.09f), skin);
            return pivot;
        }

        private static void BuildHair(Transform head, CustomerLook look, Material hair)
        {
            switch (look.hairStyle)
            {
                case HairStyle.Bald:
                    break;
                case HairStyle.Short:
                    Primitive(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.26f, -0.03f), new Vector3(0.45f, 0.36f, 0.42f), hair);
                    break;
                case HairStyle.Bob:
                    Primitive(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.22f, -0.04f), new Vector3(0.48f, 0.44f, 0.44f), hair);
                    Primitive(PrimitiveType.Sphere, "Bangs", head, new Vector3(0f, 0.33f, 0.1f), new Vector3(0.36f, 0.12f, 0.2f), hair);
                    break;
                case HairStyle.Long:
                    Primitive(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.24f, -0.04f), new Vector3(0.47f, 0.4f, 0.43f), hair);
                    Primitive(PrimitiveType.Capsule, "Back", head, new Vector3(0f, -0.02f, -0.13f), new Vector3(0.36f, 0.24f, 0.16f), hair);
                    Primitive(PrimitiveType.Sphere, "Bangs", head, new Vector3(0f, 0.33f, 0.1f), new Vector3(0.36f, 0.12f, 0.2f), hair);
                    break;
                case HairStyle.TwinTails:
                    Primitive(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.24f, -0.03f), new Vector3(0.46f, 0.4f, 0.42f), hair);
                    Primitive(PrimitiveType.Sphere, "Bangs", head, new Vector3(0f, 0.33f, 0.1f), new Vector3(0.36f, 0.12f, 0.2f), hair);
                    Primitive(PrimitiveType.Capsule, "Tail_L", head, new Vector3(-0.24f, 0.12f, -0.03f), new Vector3(0.12f, 0.15f, 0.12f), hair);
                    Primitive(PrimitiveType.Capsule, "Tail_R", head, new Vector3(0.24f, 0.12f, -0.03f), new Vector3(0.12f, 0.15f, 0.12f), hair);
                    break;
                case HairStyle.Bun:
                    Primitive(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.25f, -0.03f), new Vector3(0.45f, 0.38f, 0.42f), hair);
                    Primitive(PrimitiveType.Sphere, "Bun", head, new Vector3(0f, 0.44f, -0.08f), new Vector3(0.17f, 0.15f, 0.17f), hair);
                    break;
            }
        }

        private static void BuildHat(Transform head, CustomerLook look, Material accent, Material straw)
        {
            switch (look.hatStyle)
            {
                case HatStyle.None:
                    break;
                case HatStyle.Cap:
                    Primitive(PrimitiveType.Sphere, "Cap", head, new Vector3(0f, 0.33f, -0.01f), new Vector3(0.45f, 0.24f, 0.44f), accent);
                    Primitive(PrimitiveType.Cylinder, "Visor", head, new Vector3(0f, 0.32f, 0.17f), new Vector3(0.24f, 0.01f, 0.16f), accent);
                    break;
                case HatStyle.StrawHat:
                    Primitive(PrimitiveType.Cylinder, "Brim", head, new Vector3(0f, 0.34f, 0f), new Vector3(0.7f, 0.012f, 0.7f), straw);
                    MeshPart("Crown", head, strawCrownMesh, straw, new Vector3(0f, 0.34f, 0f), Vector3.one);
                    Primitive(PrimitiveType.Cylinder, "Ribbon", head, new Vector3(0f, 0.37f, 0f), new Vector3(0.36f, 0.02f, 0.36f), accent);
                    break;
                case HatStyle.Pointy:
                    Primitive(PrimitiveType.Cylinder, "Brim", head, new Vector3(0f, 0.34f, 0f), new Vector3(0.62f, 0.012f, 0.62f), accent);
                    Transform cone = MeshPart("Cone", head, pointyHatMesh, accent, new Vector3(0f, 0.34f, 0f), Vector3.one).transform;
                    cone.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                    break;
                case HatStyle.Scarf:
                    Primitive(PrimitiveType.Sphere, "Scarf", head, new Vector3(0f, 0.28f, -0.04f), new Vector3(0.48f, 0.38f, 0.45f), accent);
                    Primitive(PrimitiveType.Sphere, "Knot", head, new Vector3(0f, 0.1f, -0.2f), new Vector3(0.12f, 0.1f, 0.1f), accent);
                    break;
                case HatStyle.Bandana:
                    Primitive(PrimitiveType.Cylinder, "Band", head, new Vector3(0f, 0.3f, 0f), new Vector3(0.44f, 0.04f, 0.42f), accent);
                    break;
                case HatStyle.Beret:
                    Primitive(PrimitiveType.Sphere, "Beret", head, new Vector3(0.03f, 0.38f, -0.02f), new Vector3(0.48f, 0.13f, 0.46f), accent);
                    break;
                case HatStyle.Hood:
                    Primitive(PrimitiveType.Sphere, "Hood", head, new Vector3(0f, 0.22f, -0.05f), new Vector3(0.52f, 0.5f, 0.48f), accent);
                    break;
            }
        }

        private static void EnsureMeshes()
        {
            if (robeMesh != null) return;
            robeMesh = LatheMesh.Create(new[]
            {
                new Vector2(0f, 0.05f), new Vector2(0.26f, 0.05f), new Vector2(0.26f, 0.05f), new Vector2(0.255f, 0.18f),
                new Vector2(0.22f, 0.45f), new Vector2(0.19f, 0.7f), new Vector2(0.17f, 0.86f), new Vector2(0.1f, 0.95f),
                new Vector2(0f, 0.97f),
            }, 20, "CustomerRobe");
            pointyHatMesh = LatheMesh.Create(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.2f, 0f), new Vector2(0.13f, 0.14f),
                new Vector2(0.06f, 0.3f), new Vector2(0f, 0.42f),
            }, 16, "PointyHat");
            strawCrownMesh = LatheMesh.Create(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.2f, 0f), new Vector2(0.19f, 0.1f),
                new Vector2(0.15f, 0.13f), new Vector2(0f, 0.14f),
            }, 16, "StrawCrown");
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject MeshPart(string name, Transform parent, Mesh mesh, Material material, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
