using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>手に持った瓶の口からフラスコへ落ちる液体の筋（弧を描く線）と、着水のしぶき</summary>
    public class PourStream : MonoBehaviour
    {
        private const int Points = 16;

        private LineRenderer line;
        private ParticleSystem splash;
        private ParticleSystem.EmissionModule splashEmission;
        private float visible;
        private bool active;
        private Vector3 from;
        private Vector3 to;
        private Color color = Color.white;

        public void Init(Material lineMaterial, Material particleMaterial)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = Points;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (lineMaterial != null) line.sharedMaterial = lineMaterial;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.016f), new Keyframe(0.5f, 0.011f), new Keyframe(1f, 0.009f));
            line.enabled = false;

            var go = new GameObject("Splash");
            go.transform.SetParent(transform, false);
            splash = go.AddComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = splash.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            splashEmission = splash.emission;
            splashEmission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = splash.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.01f;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var psr = go.GetComponent<ParticleSystemRenderer>();
            // しぶきは、見た目の一覧のしぶきのマテリアルがあればそれを使う（丸くぼやけた玉にならないように）
            ArtCatalog art = GameContext.Art;
            Material dropletMaterial = art != null && art.droplet != null ? art.droplet : particleMaterial;
            if (dropletMaterial != null) psr.sharedMaterial = dropletMaterial;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            splash.Play();
        }

        /// <summary>注いでいる間、毎フレーム呼ぶ</summary>
        public void Pour(Vector3 mouth, Vector3 target, Color liquid)
        {
            active = true;
            from = mouth;
            to = target;
            color = liquid;
        }

        public void Stop()
        {
            active = false;
        }

        private void LateUpdate()
        {
            if (line == null) return;
            visible = Mathf.MoveTowards(visible, active ? 1f : 0f, Time.deltaTime * (active ? 6f : 4f));
            line.enabled = visible > 0.01f;
            splashEmission.rateOverTime = active ? 30f : 0f;
            if (!line.enabled) return;

            // 出口からいったん少し前に出て、重力で落ちる弧（2次ベジェ）
            Vector3 horizontal = new Vector3(to.x - from.x, 0f, to.z - from.z);
            Vector3 control = from + horizontal * 0.6f + Vector3.up * 0.02f;
            float t0 = active ? 0f : 1f - visible; // 止めたときは上から途切れていく
            for (int i = 0; i < Points; i++)
            {
                float t = Mathf.Lerp(t0, 1f, i / (float)(Points - 1));
                Vector3 p = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * control + t * t * to;
                float wobble = Mathf.Sin(Time.time * 37f + i * 1.7f) * 0.0015f * t;
                line.SetPosition(i, p + new Vector3(wobble, 0f, wobble));
            }

            Color top = Color.Lerp(color, Color.white, 0.35f);
            top.a = 0.95f;
            Color bottom = color;
            bottom.a = 0.9f;
            line.startColor = top;
            line.endColor = bottom;

            splash.transform.position = to;
            ParticleSystem.MainModule main = splash.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.4f), color);
        }
    }
}
