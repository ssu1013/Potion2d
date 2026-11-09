using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 調合フラスコの演出と音（ポーション作りの手ざわり）。Flask2D が起動時に自分に付ける（シーンに置いてもよい）。
    /// フラスコの中の状態（温度・沸騰・焦げ・かき混ぜる速さ）と操作（注ぐ・落とす）から、毎フレーム決める。
    ///   音：注ぐ（細い音と太い音を速さで混ぜ、満ちるほど高く）・ガラス棒で回す（速いほど大きく高い）・ガラス棒が当たる「カチン」
    ///       （半回転ごと、速いほど間隔が短く高く）・ぽこぽこ（58℃から）・ぐつぐつ（沸騰）・じゅうじゅう（焦げ）・炎（火加減）・
    ///       落ちる「とぷん」「さらさら」
    ///   見た目：液面の渦（かき混ぜた向きと速さで回り、手を止めても数秒かけて弱まる）・波紋・湯気（42℃から、温度で増える）・
    ///       フラスコの中のもや・泡（58℃から底からのぼり、沸騰で一面に）・しぶき・粉のもや・焦げの煙
    /// 絵は BrewArt2D の「演出」（湯気・泡・しぶき・渦・波紋）。白で描けば色はここで付ける。
    /// （3D 版 FlaskEffects の決まりを 2D に移したもの）
    /// </summary>
    public class FlaskEffects2D : MonoBehaviour
    {
        private const float SteamStart = 42f;
        private const float SimmerStart = 58f;
        private const float MaxPourMl = 140f;

        [Header("量（1 = ふつう）")]
        [SerializeField, Range(0f, 3f), Tooltip("湯気の多さ")] private float steamAmount = 1f;
        [SerializeField, Range(0f, 3f), Tooltip("泡の多さ")] private float bubbleAmount = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("液面の渦の濃さ")] private float swirlAlpha = 0.6f;
        [SerializeField, Range(0.05f, 0.6f), Tooltip("液面を斜め上から見たときのつぶれ具合（渦・波紋の縦の比）")] private float surfaceTilt = 0.22f;

        [Header("音の大きさ（設定の効果音の音量と掛け合わせる）")]
        [SerializeField, Range(0f, 2f)] private float pourVolume = 1f;
        [SerializeField, Range(0f, 2f)] private float stirVolume = 1f;
        [SerializeField, Range(0f, 2f)] private float boilVolume = 1f;
        [SerializeField, Range(0f, 2f)] private float burnerVolume = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("本（レシピ本・素材ノート）を開いている間の、ループの音の大きさ（途切れさせず、少しだけ遠くする）")] private float bookVolume = 0.7f;

        [Header("夜の環境音（お湯がコトコト・ガラスが触れ合う）")]
        [SerializeField, Range(0f, 2f), Tooltip("お湯が温まると、泡でフラスコが台に当たる「コト」の大きさ（50℃ から増える）")] private float kotoVolume = 1f;
        [SerializeField, Range(0f, 2f), Tooltip("沸きはじめたフラスコのガラスが、軽く触れ合う「ちん」の大きさ（75℃ から）")] private float glassVolume = 1f;

        private sealed class Ripple
        {
            public SpriteRenderer renderer;
            public float age = 10f;
            public float strength;
            public float x;
        }

        private const float KotoStart = 50f;
        private const float GlassStart = 75f;
        private float kotoTimer = 1f;
        private float glassTimer = 3f;

        /// <summary>お湯の「コト」を鳴らした回数（自動テスト用）</summary>
        public int KnockCount { get; private set; }
        /// <summary>ガラスが触れ合う「ちん」を鳴らした回数（自動テスト用）</summary>
        public int GlassCount { get; private set; }
        /// <summary>きらきらの粒を出した回数（夜の空気感では使わない。自動テストで 0 を確かめる）</summary>
        public int SparkleCount { get; private set; }

        private BrewSession2D session;
        private Flask2D flask;
        private BrewInput2D input;
        private HeldItem2D held;
        private BrewArt2D art;
        private bool ready;

        private ParticleSystem steam, mist, bubbles, drops, dust, sparkle;
        private Transform surfaceRoot, swirlSpin;
        private SpriteRenderer swirl;
        private readonly List<Ripple> ripples = new List<Ripple>();

        private AudioSource pourThin, pourThick, stirLoop, simmer, boil, scorch, burner;
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> targets = new Dictionary<AudioSource, float>();

        private float swirlStrength, swirlAngle, swirlSign = -1f;
        private float lastTurns, tinkAccum, tinkNext = 0.45f;
        private float steamAccum, mistAccum, bubbleAccum, smokeAccum, popAccum, pourBubbleAccum, pourDropAccum, rippleAccum;
        private float simmerBlend, boilBlend, scorchBlend;

        /// <summary>液面の渦の強さ（0〜1）</summary>
        public float SwirlStrength => swirlStrength;

        private void Start()
        {
            session = FindFirstObjectByType<BrewSession2D>();
            flask = FindFirstObjectByType<Flask2D>();
            input = FindFirstObjectByType<BrewInput2D>();
            held = FindFirstObjectByType<HeldItem2D>();
            if (session == null || flask == null) return;
            art = session.Art;
            if (session.Flask != null) lastTurns = session.Flask.TotalTurns;
            flask.PieceLanded += OnPieceLanded;
            BuildParticles();
            BuildSurface();
            BuildAudio();
            ready = true;
            // 溶け残り（底に見える固体・粉と、溶けるときの色の筋）
            if (GetComponent<FlaskSediment2D>() == null) gameObject.AddComponent<FlaskSediment2D>();
        }

        /// <summary>ほかの演出（溶け残りなど）が使う粒の入れ物を作る（この演出と同じ作り・同じ素材）</summary>
        public ParticleSystem CreateExtraSystem(string name, Sprite sprite, string layer, int order, bool masked, float gravity, int max,
                                                bool noise, float sizeFrom, float sizeTo)
        {
            return CreateSystem(name, sprite, layer, order, masked, gravity, max, noise, sizeFrom, sizeTo);
        }

        /// <summary>粒を1つ出す</summary>
        public static void EmitParticle(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            Emit(ps, position, velocity, size, life, color);
        }

        /// <summary>
        /// フラスコの中から、きらきらの粒をはじけさせる。
        /// 夜の空気感（Step6）からは使っていない（発見は Flask2D.WarmBloom の「温かい光がフワッ」）。派手にしたいときのために残してある
        /// </summary>
        public void Sparkle(Color color, int count)
        {
            SparkleCount++;
            if (!ready || flask == null || sparkle == null) return;
            Vector3 center = Vector3.Lerp(flask.BottomWorld, flask.SurfaceWorld, 0.6f);
            Color c = Color.Lerp(color, Color.white, 0.45f);
            c.a = 1f;
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(0.6f, 2.4f);
                var v = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed * 0.8f + 0.6f, 0f);
                Emit(sparkle, center + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.1f, 0.2f), 0f), v,
                     Random.Range(0.06f, 0.15f), Random.Range(0.6f, 1.2f), i % 3 == 0 ? Color.white : c);
            }
        }

        private void OnDestroy()
        {
            if (flask != null) flask.PieceLanded -= OnPieceLanded;
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        private void BuildParticles()
        {
            Sprite soft = art != null ? art.steam : null;
            Sprite bubble = art != null ? art.bubble : null;
            Sprite droplet = art != null ? art.droplet : null;
            // 湯気・煙：ふわっと広がりながらのぼる（フラスコの外。灯りに照らされる）
            steam = CreateSystem("Steam", soft, "FX", 0, false, 0f, 400, true, 0.6f, 2.2f);
            // フラスコの中のもや・泡：フラスコの内側の形で切り抜く
            mist = CreateSystem("Mist", soft, "Liquid", 4, true, 0f, 200, true, 0.8f, 1.6f);
            bubbles = CreateSystem("Bubbles", bubble, "Liquid", 5, true, 0f, 300, false, 1f, 1.2f);
            // しぶき：重力で落ちる
            drops = CreateSystem("Drops", droplet, "FlaskFront", 2, false, 1.2f, 200, false, 1f, 0.6f);
            // 粉のもや：液面に広がる
            dust = CreateSystem("Dust", soft, "Liquid", 6, true, 0f, 120, true, 0.6f, 2.4f);
            // きらきら：フラスコの外へはじけて、小さくなって消える
            sparkle = CreateSystem("Sparkle", bubble, "FX", 5, false, 0.25f, 240, false, 1f, 0.15f);
        }

        private ParticleSystem CreateSystem(string name, Sprite sprite, string layer, int order, bool masked, float gravity, int max,
                                            bool noise, float sizeFrom, float sizeTo)
        {
            var go = new GameObject("FX_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.1f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false; // 決まった時に Emit で出す
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // 出てすぐふわっと現れ、最後に消える
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, sizeFrom, 1f, sizeTo));

            if (noise)
            {
                ParticleSystem.NoiseModule n = ps.noise;
                n.enabled = true;
                n.strength = 0.35f;
                n.frequency = 0.6f;
                n.scrollSpeed = 0.25f;
                n.damping = true;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = MakeMaterial(sprite);
            r.sortingLayerName = layer;
            r.sortingOrder = order;
            r.maskInteraction = masked ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
            ps.Play();
            return ps;
        }

        private Material MakeMaterial(Sprite sprite)
        {
            Material baseMaterial = art != null ? art.particleMaterial : null;
            Material m;
            if (baseMaterial != null) m = new Material(baseMaterial);
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                m = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            }
            if (sprite != null) m.mainTexture = sprite.texture;
            return m;
        }

        private void BuildSurface()
        {
            surfaceRoot = new GameObject("SurfaceFX").transform;
            surfaceRoot.SetParent(flask.ArtRoot, false);
            swirlSpin = new GameObject("Swirl").transform;
            swirlSpin.SetParent(surfaceRoot, false);
            swirl = swirlSpin.gameObject.AddComponent<SpriteRenderer>();
            swirl.sprite = art != null ? art.swirl : null;
            swirl.sortingLayerName = "Liquid";
            swirl.sortingOrder = 3;
            swirl.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            swirl.color = new Color(1f, 1f, 1f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Ripple_" + i);
                go.transform.SetParent(surfaceRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = art != null ? art.ripple : null;
                sr.sortingLayerName = "Liquid";
                sr.sortingOrder = 3;
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                sr.color = new Color(1f, 1f, 1f, 0f);
                ripples.Add(new Ripple { renderer = sr });
            }
        }

        private void BuildAudio()
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib == null) return;
            pourThin = CreateLoop("PourThin", lib.pourThinLoop);
            pourThick = CreateLoop("PourThick", lib.pourLoop);
            stirLoop = CreateLoop("Stir", lib.stirLoop);
            simmer = CreateLoop("Simmer", lib.simmerLoop);
            boil = CreateLoop("Boil", lib.boilLoop);
            scorch = CreateLoop("Scorch", lib.scorchLoop);
            burner = CreateLoop("Burner", lib.burnerLoop);
        }

        private AudioSource CreateLoop(string name, AudioClip clip)
        {
            if (clip == null) return null;
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0f;
            sources.Add(src);
            return src;
        }

        // ------------------------------------------------------------------
        // 落とした物が液面に着いた
        // ------------------------------------------------------------------

        private void OnPieceLanded(Vector3 point, Color color, bool powder)
        {
            bool wet = session != null && session.HasLiquid;
            Color liquid = flask.ShownColor;
            SoundLibrary lib = AudioManager.Lib;
            if (powder)
            {
                // 粉：表面に広がるもや＋小さな波紋＋しゅわしゅわした泡
                for (int i = 0; i < 14; i++)
                {
                    Vector3 v = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.02f, 0.2f), 0f);
                    Color c = color;
                    c.a = 0.45f;
                    Emit(dust, point + new Vector3(Random.Range(-0.15f, 0.15f), 0.02f, 0f), v, Random.Range(0.25f, 0.45f), Random.Range(1f, 1.8f), c);
                }
                if (wet)
                {
                    AddRipple(point.x, 0.45f);
                    EmitBubbles(point, 16, 0.4f, 0.5f);
                }
                PlayOne(lib != null ? lib.dropPowder : null, 0.75f, Random.Range(0.92f, 1.08f));
            }
            else if (wet)
            {
                // 固体：しぶき＋大きな波紋＋沈むときの泡
                AddRipple(point.x, 1.25f);
                EmitSplash(point, liquid, Random.Range(10, 16), 1.2f, 2.4f);
                EmitBubbles(point, 12, 0.25f, 0.9f);
                PlayOne(lib != null ? lib.dropSolid : null, 0.85f, Random.Range(0.88f, 1.1f));
            }
            else if (lib != null && lib.putBack != null)
            {
                // 空のフラスコの底に当たった
                AudioManager.Se(lib.putBack, 0.6f, Random.Range(1.2f, 1.35f));
            }
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            if (!ready || session == null || session.Flask == null) return;
            float dt = Time.deltaTime;
            FlaskSimulation sim = session.Flask;
            bool wet = session.HasLiquid && flask.ShownFill > 0.01f;
            float temperature = sim.Temperature;

            // かき混ぜた回数（ガラス棒の音・渦）
            float turns = sim.TotalTurns;
            float delta = Mathf.Max(0f, turns - lastTurns);
            lastTurns = turns;
            float stirSpeed = session.StirSpeed;
            if (delta > 0f) swirlSign = session.LastStirClockwise ? -1f : 1f;
            Tink(delta, stirSpeed);

            // 渦：回すとすぐ強くなり、止めると数秒かけて弱まる
            float target = wet ? Mathf.Clamp01(stirSpeed / 2f) : 0f;
            swirlStrength = target > swirlStrength ? Mathf.Lerp(swirlStrength, target, 1f - Mathf.Exp(-6f * dt)) : Mathf.MoveTowards(swirlStrength, target, dt * 0.35f);
            UpdateSurface(wet, dt);
            if (dt > 0f) UpdateParticles(sim, wet, temperature, dt);
            UpdateAudio(sim, wet, temperature, Time.unscaledDeltaTime);
        }

        private void Tink(float delta, float stirSpeed)
        {
            // ガラス棒がフラスコに当たる音：だいたい半回転ごと。速く回すほど間隔が短く、音が高くなる
            tinkAccum += delta;
            if (tinkAccum < tinkNext || stirSpeed <= 0.12f) return;
            tinkAccum = 0f;
            tinkNext = Random.Range(0.32f, 0.62f);
            SoundLibrary lib = AudioManager.Lib;
            if (lib == null || lib.stirTink == null || lib.stirTink.Length == 0) return;
            float pitch = Mathf.Clamp(0.86f + stirSpeed * 0.12f + Random.Range(-0.05f, 0.05f), 0.8f, 1.5f);
            float volume = (0.16f + Mathf.Clamp01(stirSpeed / 2.2f) * 0.26f) * stirVolume;
            AudioManager.Se(lib.stirTink[Random.Range(0, lib.stirTink.Length)], volume * BookFactor, pitch);
        }

        private void UpdateSurface(bool wet, float dt)
        {
            if (surfaceRoot == null) return;
            Transform space = surfaceRoot.parent;
            Vector3 local = space.InverseTransformPoint(flask.SurfaceWorld);
            surfaceRoot.localPosition = new Vector3(local.x, local.y + 0.01f, 0f);
            float width = flask.LiquidWidth * 0.9f;
            Vector2 size = swirl.sprite != null ? (Vector2)swirl.sprite.bounds.size : Vector2.one;
            float sx = width / Mathf.Max(0.01f, size.x);
            // 斜め上から見た液面：縦をつぶした中で、渦が回る
            surfaceRoot.localScale = new Vector3(sx, sx * surfaceTilt, 1f);

            swirlAngle += dt * (40f + 520f * swirlStrength) * swirlSign;
            swirlSpin.localRotation = Quaternion.Euler(0f, 0f, swirlAngle);
            Color c = Color.Lerp(flask.ShownColor, Color.white, 0.55f);
            c.a = wet ? swirlStrength * swirlAlpha : 0f;
            swirl.color = c;
            swirl.enabled = c.a > 0.005f;

            foreach (Ripple r in ripples)
            {
                r.age += dt;
                float life = 0.9f;
                float u = Mathf.Clamp01(r.age / life);
                bool on = wet && r.age < life;
                r.renderer.enabled = on;
                if (!on) continue;
                float s = Mathf.Lerp(0.15f, 0.55f + 0.35f * r.strength, 1f - (1f - u) * (1f - u));
                r.renderer.transform.localScale = new Vector3(s, s, 1f);
                r.renderer.transform.localPosition = new Vector3(r.x / Mathf.Max(0.01f, sx), 0f, 0f);
                Color rc = Color.Lerp(flask.ShownColor, Color.white, 0.7f);
                rc.a = (1f - u) * 0.6f * Mathf.Clamp01(r.strength);
                r.renderer.color = rc;
            }
        }

        private void AddRipple(float worldX, float strength)
        {
            if (surfaceRoot == null) return;
            Ripple oldest = ripples[0];
            foreach (Ripple r in ripples) if (r.age > oldest.age) oldest = r;
            oldest.age = 0f;
            oldest.strength = strength;
            oldest.x = worldX - flask.SurfaceWorld.x;
        }

        private void UpdateParticles(FlaskSimulation sim, bool wet, float temperature, float dt)
        {
            Vector3 mouth = flask.MouthWorld;
            Vector3 surface = flask.SurfaceWorld;
            Vector3 bottom = flask.BottomWorld;
            float halfWidth = flask.LiquidWidth * 0.32f;
            float hot = Smooth(SteamStart, 100f, temperature);
            bool boiling = sim.IsBoiling;
            float boilK = boiling ? 0.6f + 0.4f * sim.HeatLevel : 0f;

            if (wet && hot > 0f)
            {
                // フラスコの中のもや（液面から口へ）
                mistAccum += dt * (2f + 10f * hot) * steamAmount;
                while (mistAccum >= 1f)
                {
                    mistAccum -= 1f;
                    float speed = Random.Range(0.4f, 0.7f) * (boiling ? 1.4f : 1f);
                    float life = Mathf.Clamp((mouth.y - surface.y) / speed, 0.5f, 4f);
                    Emit(mist, surface + new Vector3(Random.Range(-halfWidth, halfWidth) * 0.7f, 0.05f, 0f), new Vector3(0f, speed, 0f),
                         Random.Range(0.35f, 0.6f), life, new Color(1f, 1f, 1f, 0.07f + 0.12f * hot));
                }
                // 口から立ちのぼる湯気（温度が上がるほど濃く・多く、沸騰でさらに増える）
                steamAccum += dt * (1.5f + 12f * hot * hot) * (boiling ? 1.8f : 1f) * steamAmount;
                while (steamAccum >= 1f)
                {
                    steamAccum -= 1f;
                    Vector3 v = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.7f, 1.3f) * (boiling ? 1.3f : 1f), 0f);
                    Emit(steam, mouth + new Vector3(Random.Range(-0.2f, 0.2f), 0.05f, 0f), v, Random.Range(0.6f, 1.1f), Random.Range(2f, 3.2f),
                         new Color(1f, 1f, 1f, 0.12f + 0.3f * hot));
                }
            }

            // 焦げの煙（灰色）
            float scorchK = sim.IsScorching ? 1f : Mathf.Clamp01(sim.Scorch) * 0.3f;
            if (scorchK > 0.05f)
            {
                smokeAccum += dt * 7f * scorchK;
                while (smokeAccum >= 1f)
                {
                    smokeAccum -= 1f;
                    Vector3 v = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(0.8f, 1.3f), 0f);
                    Emit(steam, mouth + new Vector3(Random.Range(-0.15f, 0.15f), 0.05f, 0f), v, Random.Range(0.6f, 1f), Random.Range(2.4f, 3.6f),
                         new Color(0.3f, 0.27f, 0.24f, 0.5f * scorchK));
                }
            }

            if (wet)
            {
                // 温まると底から泡がのぼり、沸騰すると一気に増える
                float rate = (boiling ? 24f * boilK : Smooth(SimmerStart, 99f, temperature) * 12f) * bubbleAmount;
                bubbleAccum += dt * rate;
                float height = Mathf.Max(0.05f, surface.y - bottom.y);
                while (bubbleAccum >= 1f)
                {
                    bubbleAccum -= 1f;
                    float startY = Random.Range(0.05f, height * 0.5f);
                    float speed = Random.Range(0.5f, 1f) * (boiling ? 1.5f : 1f);
                    Emit(bubbles, new Vector3(bottom.x + Random.Range(-halfWidth, halfWidth) * 0.8f, bottom.y + startY, 0f), new Vector3(0f, speed, 0f),
                         Random.Range(0.05f, boiling ? 0.12f : 0.09f), (height - startY) / speed, new Color(1f, 1f, 1f, 0.75f));
                }
                // 沸騰の泡がはじけて、小さな粒が跳ねる
                if (boiling)
                {
                    popAccum += dt * 10f * boilK;
                    while (popAccum >= 1f)
                    {
                        popAccum -= 1f;
                        EmitSplash(surface + new Vector3(Random.Range(-halfWidth, halfWidth), 0f, 0f), flask.ShownColor, Random.Range(1, 3), 0.8f, 1.6f);
                    }
                    rippleAccum += dt * 2.5f * boilK;
                    if (rippleAccum >= 1f)
                    {
                        rippleAccum -= 1f;
                        AddRipple(surface.x + Random.Range(-halfWidth, halfWidth), 0.35f);
                    }
                }
            }

            // 注ぐ所に巻き込まれる泡としぶき・波紋
            bool pouring = input != null && input.Current == BrewInput2D.Activity.Pouring && input.PourRate > 0.5f;
            if (pouring && wet)
            {
                float k = Mathf.Clamp01(input.PourRate / MaxPourMl);
                float x = held != null && held.HasItem ? held.MouthWorld.x : surface.x;
                Vector3 at = new Vector3(x, surface.y, 0f);
                pourBubbleAccum += dt * (10f + 40f * k);
                while (pourBubbleAccum >= 1f)
                {
                    pourBubbleAccum -= 1f;
                    float depth = Random.Range(0.05f, 0.5f);
                    Emit(bubbles, at + new Vector3(Random.Range(-0.12f, 0.12f), -depth, 0f), new Vector3(0f, Random.Range(0.8f, 1.4f), 0f),
                         Random.Range(0.04f, 0.08f), depth / 1f, new Color(1f, 1f, 1f, 0.7f));
                }
                pourDropAccum += dt * (2f + 14f * k);
                if (pourDropAccum >= 1f)
                {
                    int n = Mathf.FloorToInt(pourDropAccum);
                    pourDropAccum -= n;
                    EmitSplash(at, flask.ShownColor, n, 0.6f, 1.2f + 1f * k);
                }
                rippleAccum += dt * (1.5f + 3f * k);
                if (rippleAccum >= 1f)
                {
                    rippleAccum -= 1f;
                    AddRipple(x, 0.4f + 0.4f * k);
                }
            }
        }

        private void UpdateAudio(FlaskSimulation sim, bool wet, float temperature, float dt)
        {
            GameFlow flow = GameFlow.Instance;
            bool silent = flow != null && (flow.Mode != GameMode.Playing || flow.IsPaused);
            float room = BookFactor;
            float heat = sim.HeatLevel;

            // 注ぐ：細い音と太い音を速さで混ぜ、フラスコが満ちるほど音が高くなる（瓶に水を注ぐときの「とくとく」）
            bool pouring = input != null && input.Current == BrewInput2D.Activity.Pouring && input.PourRate > 0.5f;
            float k = pouring ? Mathf.Clamp01(input.PourRate / MaxPourMl) : 0f;
            float fillPitch = Mathf.Lerp(0.85f, 1.3f, Mathf.Clamp01(sim.VolumeMl / Mathf.Max(1f, sim.CapacityMl)));
            Set(pourThin, pouring ? Mathf.Lerp(0.5f, 0.22f, k) * pourVolume : 0f, fillPitch * Mathf.Lerp(1.08f, 0.96f, k));
            Set(pourThick, pouring ? (0.15f + 0.6f * k) * pourVolume : 0f, fillPitch * 0.95f);

            // ガラス棒で回す音：速いほど大きく高い。止めたあとも渦が残る分だけ、かすかに聞こえる
            float stirSpeed = session.StirSpeed;
            float stirV = Mathf.Max(Mathf.Clamp01(stirSpeed / 1.6f) * 0.5f, swirlStrength * 0.12f) * (wet ? 1f : 0.25f) * stirVolume;
            Set(stirLoop, stirV * room, 0.75f + Mathf.Clamp(stirSpeed, 0f, 4f) * 0.16f);

            // 温まる（ぽこぽこ）・沸騰する（ぐつぐつ）・焦げる（じゅうじゅう）
            simmerBlend = Mathf.MoveTowards(simmerBlend, wet && !sim.IsBoiling ? Smooth(SimmerStart, 97f, temperature) : 0f, dt * 0.6f);
            boilBlend = Mathf.MoveTowards(boilBlend, wet && sim.IsBoiling ? 1f : 0f, dt * 0.7f);
            scorchBlend = Mathf.MoveTowards(scorchBlend, sim.IsScorching ? 1f : 0f, dt * 0.8f);
            Set(simmer, simmerBlend * 0.5f * boilVolume * room, 0.9f + simmerBlend * 0.15f);
            Set(boil, boilBlend * (0.3f + 0.15f * heat) * boilVolume * room, 0.92f + 0.1f * heat); // ぐつぐつは控えめに（夜の空気感）
            Set(scorch, scorchBlend * 0.55f * boilVolume * room, 1f);

            // 加熱台の炎：火加減で大きさと高さが変わる
            Set(burner, heat > 0.001f ? (0.1f + 0.3f * heat) * burnerVolume * room : 0f, 0.85f + 0.3f * heat);

            // お湯がコトコト・ガラスが軽く触れ合う（温度に合わせて、不規則に）
            if (!silent && wet) Ambience(sim, temperature, room, dt);

            float master = GameSettings.SeVolume;
            foreach (AudioSource s in sources)
            {
                if (s == null || s.clip == null) continue;
                float want = !silent && targets.TryGetValue(s, out float v) ? v * master : 0f;
                s.volume = Mathf.MoveTowards(s.volume, want, dt * 1.8f);
                if (s.volume > 0.001f && !s.isPlaying)
                {
                    s.time = Random.Range(0f, s.clip.length * 0.9f);
                    s.Play();
                }
                else if (s.volume <= 0.001f && want <= 0f && s.isPlaying) s.Stop();
            }
        }

        /// <summary>
        /// お湯が温まると、泡でフラスコが台に当たって「コト…コト」（温まるほど増え、沸騰すると少しせわしなく）。
        /// 75℃ をこえると、ときどきガラスが軽く触れ合う「ちん」。どちらも小さく、間隔は不規則に
        /// </summary>
        private void Ambience(FlaskSimulation sim, float temperature, float room, float dt)
        {
            SoundLibrary lib = AudioManager.Lib;
            float warm = Smooth(KotoStart, 100f, temperature);
            if (warm > 0.001f)
            {
                kotoTimer -= dt;
                if (kotoTimer <= 0f)
                {
                    float perSecond = Mathf.Lerp(0.35f, 2.2f, warm) * (sim.IsBoiling ? 1.4f : 1f);
                    kotoTimer = Random.Range(0.5f, 1.5f) / perSecond;
                    AudioClip clip = Pick(lib != null ? lib.kotoKnock : null, SfxSynth.Knock);
                    AudioManager.Se(clip, (0.1f + 0.18f * warm) * kotoVolume * room, Random.Range(0.85f, 1.18f));
                    KnockCount++;
                }
            }
            else kotoTimer = Random.Range(0.3f, 1f);

            float hot = Smooth(GlassStart, 100f, temperature);
            if (hot > 0.001f)
            {
                glassTimer -= dt;
                if (glassTimer <= 0f)
                {
                    glassTimer = Random.Range(1.5f, 5f) / Mathf.Lerp(0.5f, 1.4f, hot);
                    AudioClip custom = Pick(lib != null ? lib.glassTouch : null, null);
                    if (custom != null) AudioManager.Se(custom, (0.06f + 0.06f * hot) * glassVolume * room, Random.Range(0.95f, 1.08f));
                    else if (lib != null && lib.stirTink != null && lib.stirTink.Length > 0)
                        AudioManager.Se(lib.stirTink[Random.Range(0, lib.stirTink.Length)], (0.05f + 0.05f * hot) * glassVolume * room, Random.Range(1.35f, 1.7f));
                    else AudioManager.Se(SfxSynth.GlassTap, (0.04f + 0.04f * hot) * glassVolume * room, Random.Range(0.9f, 1.1f));
                    GlassCount++;
                }
            }
            else glassTimer = Random.Range(1f, 3f);
        }

        private static AudioClip Pick(AudioClip[] list, AudioClip fallback)
        {
            if (list != null && list.Length > 0)
            {
                AudioClip c = list[Random.Range(0, list.Length)];
                if (c != null) return c;
            }
            return fallback;
        }

        /// <summary>本を開いている間は、ループの音を少しだけ小さくする（画面は切り替わらないので、音も途切れない）</summary>
        private float BookFactor
        {
            get
            {
                AtelierHUD hud = AtelierHUD.Instance;
                return hud != null && (hud.IsRecipeBookOpen || hud.IsNotebookOpen) ? bookVolume : 1f;
            }
        }

        private void Set(AudioSource source, float volume, float pitch)
        {
            if (source == null) return;
            targets[source] = volume;
            source.pitch = Mathf.Lerp(source.pitch, pitch, 0.2f);
        }

        // ------------------------------------------------------------------
        // 粒
        // ------------------------------------------------------------------

        private static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            if (ps == null) return;
            var e = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = Mathf.Max(0.05f, life),
                startColor = color,
                rotation = Random.Range(0f, 360f),
            };
            ps.Emit(e, 1);
        }

        private void EmitBubbles(Vector3 point, int count, float spread, float depth)
        {
            for (int i = 0; i < count; i++)
            {
                float d = Random.Range(0.05f, depth);
                Emit(bubbles, point + new Vector3(Random.Range(-spread, spread), -d, 0f), new Vector3(0f, Random.Range(0.6f, 1.2f), 0f),
                     Random.Range(0.04f, 0.09f), d / 0.9f, new Color(1f, 1f, 1f, 0.75f));
            }
        }

        private void EmitSplash(Vector3 point, Color color, int count, float minSpeed, float maxSpeed)
        {
            Color c = Color.Lerp(color, Color.white, 0.25f);
            c.a = 0.95f;
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(60f, 120f) * Mathf.Deg2Rad;
                float speed = Random.Range(minSpeed, maxSpeed);
                Emit(drops, point + new Vector3(0f, 0.03f, 0f), new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed, 0f),
                     Random.Range(0.04f, 0.08f), Random.Range(0.35f, 0.6f), c);
            }
        }

        private void PlayOne(AudioClip[] clips, float volume, float pitch)
        {
            if (clips == null || clips.Length == 0) return;
            AudioManager.Se(clips[Random.Range(0, clips.Length)], volume, pitch);
        }

        private static float Smooth(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }
    }
}
