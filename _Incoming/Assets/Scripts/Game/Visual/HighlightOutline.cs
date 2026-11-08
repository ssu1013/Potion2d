using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 視線の先の物を金色の輪郭で光らせる。
    /// 対象の見た目（MeshRenderer）と同じ形の「殻」を、輪郭だけを描くマテリアルで重ねて描く方式なので、
    /// 自作モデル・自作マテリアル（どんなシェーダーでも）でも光る。
    /// 輪郭の太さ・色は ArtCatalog のハイライト用マテリアルで変えられる。
    /// なめらかなモデルほどきれいに出る（角ばったモデルは「箱用」のマテリアルを使う）。
    /// 線は物の外側だけに出る（先に「下地」のマテリアルで物の形を印しておき、その内側には線を描かない）。
    /// </summary>
    public static class HighlightOutline
    {
        public const string ShellName = "HighlightShell";
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        private static readonly Dictionary<Interactable, List<GameObject>> shells = new Dictionary<Interactable, List<GameObject>>();
        private static readonly List<MeshRenderer> buffer = new List<MeshRenderer>();

        public static void Show(Interactable target)
        {
            if (target == null) return;
            ArtCatalog art = GameContext.Art;
            if (art == null || art.highlight == null) return;

            var list = new List<GameObject>();
            target.GetComponentsInChildren(buffer);
            foreach (MeshRenderer r in buffer)
            {
                if (!r.enabled || r.name == ShellName) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                // 輪郭線を付けない設定の部品（目・炎・液体など）は光らせない
                Material shared = r.sharedMaterial;
                if (shared != null && shared.HasProperty(OutlineWidthId) && shared.GetFloat(OutlineWidthId) <= 0.01f) continue;
                if (shared != null && shared.shader != null && shared.shader.name == "PotionFrontier/ToonLiquid") continue;

                bool box = filter.sharedMesh.name == "Cube" && art.highlightBox != null;
                if (art.highlightMask != null) list.Add(CreateShell(r, filter.sharedMesh, art.highlightMask));
                list.Add(CreateShell(r, filter.sharedMesh, box ? art.highlightBox : art.highlight));
            }
            shells[target] = list;
        }

        private static GameObject CreateShell(Renderer source, Mesh mesh, Material material)
        {
            var go = new GameObject(ShellName);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shell = go.AddComponent<MeshRenderer>();
            int subMeshes = Mathf.Max(1, mesh.subMeshCount);
            var mats = new Material[subMeshes];
            for (int i = 0; i < subMeshes; i++) mats[i] = material;
            shell.sharedMaterials = mats;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shell.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        public static void Hide(Interactable target)
        {
            if (target == null || !shells.TryGetValue(target, out List<GameObject> list)) return;
            foreach (GameObject go in list)
            {
                if (go != null) Object.Destroy(go);
            }
            shells.Remove(target);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            shells.Clear();
        }
    }
}
