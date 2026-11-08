using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// レシピ本のページに出す「瓶の見た目」（3D）。部屋から遠く離れた場所に瓶を1本置き、専用のカメラで
    /// 小さな絵（RenderTexture）に写して、ゆっくり回す。本を開いている間だけカメラを動かす。
    /// 瓶のモデルは棚や手に持つ瓶と同じ（ベース液体ごとの瓶 → 標準の瓶）なので、自作の瓶に差し替えるとここにも出る。
    /// </summary>
    public sealed class LiquidPreview
    {
        /// <summary>部屋から十分に離れた場所（部屋の灯りが届かず、カメラにも部屋が写らない）</summary>
        private static readonly Vector3 StagePosition = new Vector3(0f, -400f, 0f);
        private const float FieldOfView = 26f;
        private const float TurnSpeed = 28f; // 度/秒

        private readonly GameObject stage;
        private readonly Transform turntable;
        private readonly Camera camera;
        private readonly RenderTexture texture;
        private GameObject bottle;
        private string shownId;
        private Color shownColor;
        private float angle = 25f;

        public Texture Texture => texture;

        public LiquidPreview(int size, Color background)
        {
            stage = new GameObject("RecipeBookPreview");
            stage.transform.position = StagePosition;
            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(stage.transform, false);

            texture = new RenderTexture(size, size, 16) { name = "RecipeBookPreview", antiAliasing = 2 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(stage.transform, false);
            camera = camGo.AddComponent<Camera>();
            camera.targetTexture = texture;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.enabled = false;

            // 瓶を照らす灯り（ここにしかないので、部屋の物は照らさない）。上手前から暖かい光、反対から弱い光
            AddLight("Key", new Vector3(-0.35f, 0.55f, -0.5f), new Color(1f, 0.93f, 0.8f), 1.5f);
            AddLight("Rim", new Vector3(0.45f, 0.35f, 0.45f), new Color(0.75f, 0.85f, 1f), 0.8f);
        }

        private void AddLight(string name, Vector3 offset, Color color, float intensity)
        {
            var go = new GameObject("Light_" + name);
            go.transform.SetParent(stage.transform, false);
            go.transform.localPosition = offset;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.5f;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>写す液体を変える（同じ液体・同じ色なら作り直さない）</summary>
        public void Show(LiquidRecord record)
        {
            if (record == null)
            {
                Clear();
                return;
            }
            if (bottle != null && shownId == record.id && shownColor == record.color) return;
            Clear();
            shownId = record.id;
            shownColor = record.color;
            bottle = PotionVisuals.CreateBottle(record.color, record.baseLiquidId, turntable, "PreviewBottle");
            bottle.transform.localPosition = Vector3.zero;
            bottle.transform.localRotation = Quaternion.identity;
            Frame();
        }

        public void Clear()
        {
            if (bottle != null) Object.Destroy(bottle);
            bottle = null;
            shownId = null;
        }

        public void SetActive(bool active)
        {
            if (camera != null) camera.enabled = active;
        }

        public void Tick(float dt)
        {
            if (bottle == null) return;
            angle = Mathf.Repeat(angle + dt * TurnSpeed, 360f);
            turntable.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>瓶の大きさに合わせて、カメラを少し上から寄せる</summary>
        private void Frame()
        {
            var bounds = new Bounds(turntable.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in bottle.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) bounds = new Bounds(turntable.position + Vector3.up * 0.1f, Vector3.one * 0.2f);

            // 回しても枠からはみ出さないよう、横幅は大きいほうの辺で見積もる
            float radius = Mathf.Max(bounds.extents.y, Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.2f);
            float distance = radius / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.18f;
            Vector3 center = bounds.center;
            Vector3 direction = Quaternion.Euler(12f, 0f, 0f) * Vector3.back;
            camera.transform.position = center + direction * distance;
            camera.transform.LookAt(center);
            camera.farClipPlane = distance + radius * 3f + 0.5f;
        }
    }
}
