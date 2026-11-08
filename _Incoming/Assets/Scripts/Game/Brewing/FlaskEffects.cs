using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 調合フラスコの演出と音（BrewingStation が起動時に付ける）。フラスコの中の状態と操作から、毎フレーム次を決める。
    ///   液面（ToonLiquid）… かき混ぜた渦（回した向きと速さ。止めてもしばらく回り続ける）・落とした物と注ぐ所の波紋・沸騰の泡とうねり・焦げつき
    ///   粒 … 温度で増える湯気（フラスコの中のもやと、口から立ちのぼる湯気）・沸騰の泡としぶき・落とした所のしぶき・粉のもや・焦げの煙
    ///   音 … 固体／粉の落ちる音、細く／太く注ぐ音（量が増えるほど音が高くなる）、ガラス棒の回る音とフラスコに当たる音
    ///        （回す速さで高さと間隔が変わる）、温まるぽこぽこ音、沸騰のぐつぐつ音、焦げる音、加熱台の炎の音、火をつける音、栓の音
    /// 粒のマテリアルは見た目の一覧（ArtCatalog）、音は SoundLibrary から使う。どちらも空なら、その演出だけ出ない。
    /// </summary>
    public class FlaskEffects : MonoBehaviour
    {
        // 調整値（ここに置き、必要なら後で Inspector に出す）
        private const float SteamStart = 42f;          // 湯気が出始める温度
        private const float SimmerStart = 58f;         // ぽこぽこ音・小さな泡が始まる温度
        private const float RippleLife = 2.6f;
        private const float MaxPourMl = 140f;

        private BrewingStation station;
        private LiquidFill liquid;
        private Transform liquidTransform;
        private float[] radiusTable;
        private float tableMinY, tableMaxY;
        private float radiusScale = 1f;

        private ParticleSystem mist;      // 湯気・もや・煙・粉（やわらかいかたまり）
        private ParticleSystem bubbles;   // 液体の中の泡
        private ParticleSystem drops;     // しぶき・はじける粒

        private readonly Vector4[] ripples = new Vector4[4];   // x, z, 始まった時刻, 強さ
        private readonly Vector4[] rippleParams = new Vector4[4];
        private int nextRipple;
        private float swirlPhase, swirlOmega, stirTurnsFrame, stirSpeed, stirSign = -1f;
        private float boilShown, scorchShown, simmerBlend, boilBlend, scorchBlend;
        private float steamAccum, vaporAccum, smokeAccum, bubbleAccum, popAccum, pourBubbleAccum, pourDropAccum;
        private float pourRate;
        private bool pouring;
        private float rippleTimer;
        private float tinkAccum, tinkNext = 0.5f;

        private AudioSource pourThin, pourThick, stirLoop, simmer, boil, scorch, burner;
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> targets = new Dictionary<AudioSource, float>();

        /// <summary>自動テスト・調整用：今の音の大きさ（設定の音量を掛ける前）</summary>
        public float StirSpeed => stirSpeed;
        public float SwirlStrength => Mathf.Clamp01(Mathf.Abs(swirlOmega) / (Mathf.PI * 2f * 1.4f));
        public float BoilVisual => boilShown;
        public int TinkCount { get; private set; }
        public int DropSoundCount { get; private set; }
        public string LastDropSound { get; private set; } = "";

        public void Init(BrewingStation owner, LiquidFill fill)
        {
            station = owner;
            liquid = fill;
            liquidTransform = fill != null ? fill.transform : null;
            BuildRadiusTable();

            ArtCatalog art = GameContext.Art;
            mist = CreateMist(art != null ? art.steam : null);
            bubbles = CreateBubbles(art != null ? art.bubble : null);
            drops = CreateDrops(art != null ? art.droplet : null);

            pourThin = CreateLoop("PourThin");
            pourThick = CreateLoop("PourThick");
            stirLoop = CreateLoop("StirLoop");
            simmer = CreateLoop("Simmer");
            boil = CreateLoop("Boil");
            scorch = CreateLoop("Scorch");
            burner = CreateLoop("Burner");
        }

        private void Start()
        {
            // 音の一覧は AudioManager が起きてから使えるので、ここで割り当てる
            SoundLibrary lib = AudioManager.Lib;
            if (lib == null) return;
            pourThin.clip = lib.pourThinLoop;
            pourThick.clip = lib.pourLoop;
            stirLoop.clip = lib.stirLoop;
            simmer.clip = lib.simmerLoop;
            boil.clip = lib.boilLoop;
            scorch.clip = lib.scorchLoop;
            burner.clip = lib.burnerLoop;
        }

        /// <summary>瓶詰めの「ぽん」で使う煙のかたまりを、湯気と同じやわらかい見た目にする</summary>
        public void StylePuff(ParticleSystem puff)
        {
            ArtCatalog art = GameContext.Art;
            if (puff == null || art == null || art.steam == null) return;
            var r = puff.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = art.steam;
            ParticleSystem.TextureSheetAnimationModule sheet = puff.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
        }

        // ------------------------------------------------------------------
        // 操作から呼ばれる
        // ------------------------------------------------------------------

        /// <summary>注いでいる間、毎フレーム（mlPerSecond は実際に入った速さ）</summary>
        public void Pouring(float mlPerSecond, Color color)
        {
            pouring = true;
            pourRate = Mathf.Max(0f, mlPerSecond);
        }

        public void StopPouring()
        {
            pouring = false;
            pourRate = 0f;
        }

        /// <summary>落とした物が液面（空なら底）に着いた</summary>
        public void Dropped(IngredientData data, Vector3 point)
        {
            if (data == null) return;
            bool powder = data.Form == IngredientForm.Powder;
            bool wet = station != null && station.HasLiquid;
            Color liquidColor = station != null ? station.DisplayColor : Color.white;
            SoundLibrary lib = AudioManager.Lib;

            if (powder)
            {
                // 粉：表面に広がるもや＋小さな波紋＋しゅわしゅわした泡
                EmitDust(point, data.Color, 14);
                if (wet)
                {
                    AddRipple(point, 0.45f);
                    EmitBubbles(point, 16, 0.03f, 0.5f);
                }
                PlayOneShot(lib != null ? lib.dropPowder : null, point, 0.75f, Random.Range(0.92f, 1.08f), "粉");
            }
            else if (wet)
            {
                // 固体：しぶき＋大きな波紋＋沈むときの泡
                AddRipple(point, 1.25f);
                EmitSplash(point, liquidColor, Random.Range(10, 16), 0.45f, 0.85f);
                EmitBubbles(point, 12, 0.02f, 0.9f);
                PlayOneShot(lib != null ? lib.dropSolid : null, point, 0.85f, Random.Range(0.88f, 1.1f), "固体");
            }
            else
            {
                // 空のフラスコの底に当たった
                if (lib != null && lib.putBack != null) AudioManager.SeAt(lib.putBack, point, 0.6f, Random.Range(1.2f, 1.35f));
                LastDropSound = "空の底";
                DropSoundCount++;
            }
        }

        /// <summary>かき混ぜた（1フレームの回転数と向き）</summary>
        public void Stirred(float turns, bool clockwise)
        {
            if (turns <= 0f) return;
            stirTurnsFrame += turns;
            stirSign = clockwise ? -1f : 1f;

            // ガラス棒がフラスコに当たる音：だいたい半回転ごと。速く回すほど間隔が短く、音が高くなる
            tinkAccum += turns;
            if (tinkAccum >= tinkNext && stirSpeed > 0.12f)
            {
                tinkAccum = 0f;
                tinkNext = Random.Range(0.32f, 0.62f);
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null && lib.stirTink != null && lib.stirTink.Length > 0)
                {
                    float pitch = Mathf.Clamp(0.86f + stirSpeed * 0.12f + Random.Range(-0.05f, 0.05f), 0.8f, 1.5f);
                    float volume = 0.16f + Mathf.Clamp01(stirSpeed / 2.2f) * 0.26f;
                    AudioManager.SeAt(lib.stirTink[Random.Range(0, lib.stirTink.Length)], station.MouthWorld, volume, pitch);
                    TinkCount++;
                }
            }
        }

        /// <summary>火加減が変わった（火をつけた・消した音）</summary>
        public void HeatChanged(float previous, float next)
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib == null || lib.ignite == null) return;
            Vector3 at = station != null && station.Heater != null ? station.Heater.transform.position : transform.position;
            if (previous <= 0.001f && next > 0.001f) AudioManager.SeAt(lib.ignite, at, 0.75f, Random.Range(0.95f, 1.05f));
            else if (previous > 0.001f && next <= 0.001f) AudioManager.SeAt(lib.ignite, at, 0.25f, 1.7f);
        }

        /// <summary>瓶詰めした（栓の音）</summary>
        public void Bottled()
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null && lib.corkPop != null) AudioManager.SeAt(lib.corkPop, station.MouthWorld, 0.7f, Random.Range(0.95f, 1.08f));
        }

        // ------------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            if (station == null || station.Flask == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            FlaskSimulation flask = station.Flask;
            bool wet = station.HasLiquid;
            float temperature = flask.Temperature;
            float heat = flask.HeatLevel;
            Vector3 surface = station.SurfacePoint;
            float radius = SurfaceRadius(surface.y);

            // かき混ぜる速さ（回転/秒）と、液体の渦（止めてもしばらく回る）
            float instant = stirTurnsFrame / dt;
            stirTurnsFrame = 0f;
            stirSpeed = Mathf.Lerp(stirSpeed, instant, 1f - Mathf.Exp(-(instant > stirSpeed ? 10f : 3.5f) * dt));
            if (stirSpeed < 0.01f) stirSpeed = 0f;
            float target = stirSpeed * Mathf.PI * 2f * 0.85f * stirSign;
            swirlOmega = Mathf.Lerp(swirlOmega, wet ? target : 0f, 1f - Mathf.Exp(-(stirSpeed > 0.05f ? 3f : 0.7f) * dt));
            swirlPhase = Mathf.Repeat(swirlPhase + swirlOmega * dt, Mathf.PI * 2f);

            // 沸騰・焦げ
            float boilTarget = 0f;
            if (wet)
            {
                if (flask.IsBoiling) boilTarget = 0.55f + 0.45f * heat;
                else boilTarget = Smooth(SimmerStart + 5f, 99f, temperature) * 0.32f;
            }
            boilShown = Mathf.MoveTowards(boilShown, boilTarget, dt * 0.8f);
            float scorchTarget = flask.IsScorching ? Mathf.Clamp01(0.45f + flask.Scorch) : 0f;
            scorchShown = Mathf.MoveTowards(scorchShown, scorchTarget, dt * 0.5f);

            UpdateRipples(surface, radius, dt);
            if (liquid != null && wet)
            {
                for (int i = 0; i < ripples.Length; i++)
                {
                    float age = Time.time - ripples[i].z;
                    rippleParams[i] = ripples[i].w > 0f && age < RippleLife ? new Vector4(ripples[i].x, ripples[i].y, age, ripples[i].w) : Vector4.zero;
                }
                liquid.SetSurfaceEffects(new Vector4(surface.x, 0f, surface.z, radius),
                                         new Vector4(swirlPhase, SwirlStrength, Mathf.Sign(swirlOmega == 0f ? stirSign : swirlOmega), 0f),
                                         new Vector4(boilShown, scorchShown, 0f, 0f), rippleParams);
            }

            UpdateParticles(flask, wet, surface, radius, temperature, dt);
            UpdateAudio(flask, wet, temperature, heat, dt);
        }

        private void UpdateRipples(Vector3 surface, float radius, float dt)
        {
            if (!pouring || pourRate <= 1f || !station.HasLiquid) return;
            rippleTimer -= dt;
            if (rippleTimer > 0f) return;
            float k = Mathf.Clamp01(pourRate / MaxPourMl);
            rippleTimer = Mathf.Lerp(0.16f, 0.08f, k);
            Vector2 jitter = Random.insideUnitCircle * 0.006f;
            AddRipple(surface + new Vector3(jitter.x, 0f, jitter.y), 0.35f + 0.55f * k);
        }

        private void AddRipple(Vector3 point, float strength)
        {
            // いちばん古い波紋を置き換える
            int slot = nextRipple;
            nextRipple = (nextRipple + 1) % ripples.Length;
            ripples[slot] = new Vector4(point.x, point.z, Time.time, strength);
        }

        private void UpdateParticles(FlaskSimulation flask, bool wet, Vector3 surface, float radius, float temperature, float dt)
        {
            Vector3 mouth = station.MouthWorld;
            float neck = Mathf.Max(0.015f, SurfaceRadius(mouth.y - 0.02f) * 0.7f);
            float hot = Smooth(SteamStart, 100f, temperature);
            bool boiling = flask.IsBoiling;

            if (mist != null && wet && hot > 0f)
            {
                // フラスコの中のもや（液面から口へ）
                vaporAccum += dt * (2f + 10f * hot);
                while (vaporAccum >= 1f)
                {
                    vaporAccum -= 1f;
                    Vector2 c = Random.insideUnitCircle * Mathf.Min(radius * 0.4f, neck);
                    float speed = Random.Range(0.08f, 0.14f) * (boiling ? 1.4f : 1f);
                    float life = Mathf.Clamp((mouth.y - surface.y) / speed, 0.4f, 4f);
                    EmitMist(surface + new Vector3(c.x, 0.01f, c.y), new Vector3(0f, speed, 0f), Random.Range(0.03f, 0.05f), life,
                             new Color(1f, 1f, 1f, 0.06f + 0.1f * hot));
                }
                // 口から立ちのぼる湯気
                steamAccum += dt * (1.5f + 12f * hot * hot) * (boiling ? 1.8f : 1f);
                while (steamAccum >= 1f)
                {
                    steamAccum -= 1f;
                    Vector2 c = Random.insideUnitCircle * neck * 0.6f;
                    Vector3 v = new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(0.1f, 0.2f) * (boiling ? 1.3f : 1f), Random.Range(-0.02f, 0.02f));
                    EmitMist(mouth + new Vector3(c.x, 0.01f, c.y), v, Random.Range(0.06f, 0.11f), Random.Range(2.2f, 3.4f),
                             new Color(1f, 1f, 1f, 0.12f + 0.3f * hot));
                }
            }

            if (mist != null && scorchShown > 0.05f)
            {
                // 焦げの煙（灰色）
                smokeAccum += dt * 7f * scorchShown;
                while (smokeAccum >= 1f)
                {
                    smokeAccum -= 1f;
                    Vector2 c = Random.insideUnitCircle * neck * 0.5f;
                    Vector3 v = new Vector3(Random.Range(-0.015f, 0.015f), Random.Range(0.12f, 0.2f), Random.Range(-0.015f, 0.015f));
                    EmitMist(mouth + new Vector3(c.x, 0.01f, c.y), v, Random.Range(0.06f, 0.1f), Random.Range(2.4f, 3.6f),
                             new Color(0.3f, 0.27f, 0.24f, 0.5f * scorchShown));
                }
            }

            if (bubbles != null && wet)
            {
                // 温まると底から泡がのぼり、沸騰すると一気に増える
                float rate = boiling ? 24f * (0.6f + 0.4f * flask.HeatLevel) : Smooth(SimmerStart, 99f, temperature) * 12f;
                bubbleAccum += dt * rate;
                Vector3 bottom = station.BottomWorld;
                float height = Mathf.Max(0.005f, surface.y - bottom.y);
                while (bubbleAccum >= 1f)
                {
                    bubbleAccum -= 1f;
                    Vector2 c = Random.insideUnitCircle * station.BottomRadius * 0.7f;
                    float speed = Random.Range(0.07f, 0.14f) * (boiling ? 1.5f : 1f);
                    float startY = Random.Range(0.005f, height * 0.5f);
                    float life = (height - startY) / speed;
                    EmitBubble(bottom + new Vector3(c.x, startY, c.y), new Vector3(0f, speed, 0f), Random.Range(0.004f, boiling ? 0.009f : 0.007f), life);
                }
                // 注ぐ所に巻き込まれる泡としぶき
                if (pouring && pourRate > 1f)
                {
                    float k = Mathf.Clamp01(pourRate / MaxPourMl);
                    pourBubbleAccum += dt * (10f + 40f * k);
                    while (pourBubbleAccum >= 1f)
                    {
                        pourBubbleAccum -= 1f;
                        Vector2 c = Random.insideUnitCircle * 0.02f;
                        float depth = Random.Range(0.01f, Mathf.Min(0.08f, height));
                        EmitBubble(surface + new Vector3(c.x, -depth, c.y), new Vector3(0f, Random.Range(0.1f, 0.2f), 0f), Random.Range(0.003f, 0.007f), depth / 0.15f);
                    }
                    pourDropAccum += dt * (2f + 14f * k);
                    if (pourDropAccum >= 1f)
                    {
                        int n = Mathf.FloorToInt(pourDropAccum);
                        pourDropAccum -= n;
                        EmitSplash(surface, station.DisplayColor, n, 0.15f, 0.35f + 0.25f * k);
                    }
                }
            }

            if (drops != null && wet && boiling)
            {
                // 沸騰の泡がはじけて、小さな粒が跳ねる
                popAccum += dt * 10f * boilShown;
                while (popAccum >= 1f)
                {
                    popAccum -= 1f;
                    Vector2 c = Random.insideUnitCircle * radius * 0.8f;
                    EmitSplash(surface + new Vector3(c.x, 0f, c.y), station.DisplayColor, Random.Range(1, 3), 0.12f, 0.32f);
                }
            }
        }

        private void UpdateAudio(FlaskSimulation flask, bool wet, float temperature, float heat, float dt)
        {
            // 注ぐ：細い音と太い音を速さで混ぜ、フラスコが満ちるほど音が高くなる（瓶に水を注ぐときの「とくとく」）
            float k = Mathf.Clamp01(pourRate / MaxPourMl);
            float fillPitch = Mathf.Lerp(0.85f, 1.3f, Mathf.Clamp01(flask.VolumeMl / Mathf.Max(1f, flask.CapacityMl)));
            Set(pourThin, pouring && pourRate > 0.5f ? Mathf.Lerp(0.5f, 0.22f, k) : 0f, fillPitch * Mathf.Lerp(1.08f, 0.96f, k));
            Set(pourThick, pouring && pourRate > 0.5f ? 0.15f + 0.6f * k : 0f, fillPitch * 0.95f);

            // ガラス棒で回す音：速いほど大きく高い。止めたあとも渦が残る分だけ、かすかに聞こえる
            float stirVolume = Mathf.Max(Mathf.Clamp01(stirSpeed / 1.6f) * 0.5f, SwirlStrength * 0.12f) * (wet ? 1f : 0.25f);
            Set(stirLoop, stirVolume, 0.75f + Mathf.Clamp(stirSpeed, 0f, 4f) * 0.16f);

            // 温まる・沸騰する・焦げる
            simmerBlend = Mathf.MoveTowards(simmerBlend, wet && !flask.IsBoiling ? Smooth(SimmerStart, 97f, temperature) : 0f, dt * 0.6f);
            boilBlend = Mathf.MoveTowards(boilBlend, wet && flask.IsBoiling ? 1f : 0f, dt * 0.7f);
            scorchBlend = Mathf.MoveTowards(scorchBlend, flask.IsScorching ? 1f : 0f, dt * 0.8f);
            Set(simmer, simmerBlend * 0.5f, 0.9f + simmerBlend * 0.15f);
            Set(boil, boilBlend * (0.45f + 0.25f * heat), 0.92f + 0.12f * heat);
            Set(scorch, scorchBlend * 0.55f, 1f);

            // 加熱台の炎：火加減で大きさと高さが変わる
            Set(burner, heat > 0.001f ? 0.1f + 0.3f * heat : 0f, 0.85f + 0.3f * heat);

            float master = GameSettings.SeVolume;
            foreach (AudioSource s in sources)
            {
                if (s == null || s.clip == null) continue;
                float want = targets.TryGetValue(s, out float v) ? v * master : 0f;
                s.volume = Mathf.MoveTowards(s.volume, want, Time.unscaledDeltaTime * 1.8f);
                if (s.volume > 0.001f && !s.isPlaying)
                {
                    s.time = Random.Range(0f, s.clip.length * 0.9f);
                    s.Play();
                }
                else if (s.volume <= 0.001f && want <= 0f && s.isPlaying) s.Stop();
            }
        }

        private void Set(AudioSource source, float volume, float pitch)
        {
            if (source == null) return;
            targets[source] = volume;
            source.pitch = Mathf.Lerp(source.pitch, pitch, 0.2f);
        }

        /// <summary>自動テスト用：今鳴っているループの大きさ（設定の音量を掛けたもの）</summary>
        public string DescribeAudio()
        {
            string Part(string name, AudioSource s) => s != null && s.isPlaying && s.volume > 0.01f ? $"{name}{s.volume:0.00}(×{s.pitch:0.00}) " : "";
            string text = Part("細く注ぐ", pourThin) + Part("太く注ぐ", pourThick) + Part("混ぜる", stirLoop) + Part("ぽこぽこ", simmer) +
                          Part("ぐつぐつ", boil) + Part("焦げる", scorch) + Part("炎", burner);
            return string.IsNullOrEmpty(text) ? "（静か）" : text.TrimEnd();
        }

        // ------------------------------------------------------------------
        // 粒
        // ------------------------------------------------------------------

        private void EmitMist(Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-25f, 25f),
            };
            mist.Emit(p, 1);
        }

        private void EmitDust(Vector3 point, Color color, int count)
        {
            if (mist == null) return;
            Color c = Color.Lerp(color, Color.white, 0.25f);
            c.a = 0.45f;
            for (int i = 0; i < count; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(0.04f, 0.16f);
                EmitMist(point + Vector3.up * 0.01f, new Vector3(d.x, Random.Range(0.02f, 0.07f), d.y), Random.Range(0.035f, 0.07f), Random.Range(0.7f, 1.3f), c);
            }
        }

        private void EmitBubble(Vector3 position, Vector3 velocity, float size, float life)
        {
            if (bubbles == null) return;
            Color c = Color.Lerp(station.DisplayColor, Color.white, 0.6f);
            c.a = 0.55f;
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = Mathf.Max(0.05f, life),
                startColor = c,
            };
            bubbles.Emit(p, 1);
        }

        private void EmitBubbles(Vector3 point, int count, float spread, float depth)
        {
            float height = Mathf.Max(0.01f, station.SurfacePoint.y - station.BottomWorld.y);
            for (int i = 0; i < count; i++)
            {
                Vector2 c = Random.insideUnitCircle * spread;
                float d = Random.Range(0.005f, height * depth);
                float speed = Random.Range(0.08f, 0.18f);
                EmitBubble(point + new Vector3(c.x, -d, c.y), new Vector3(0f, speed, 0f), Random.Range(0.003f, 0.008f), d / speed);
            }
        }

        private void EmitSplash(Vector3 point, Color color, int count, float minSpeed, float maxSpeed)
        {
            if (drops == null) return;
            Color c = Color.Lerp(color, Color.white, 0.15f);
            c.a = 0.95f;
            for (int i = 0; i < count; i++)
            {
                Vector2 h = Random.insideUnitCircle * 0.35f;
                Vector3 dir = new Vector3(h.x, 1f, h.y).normalized;
                var p = new ParticleSystem.EmitParams
                {
                    position = point + Vector3.up * 0.004f,
                    velocity = dir * Random.Range(minSpeed, maxSpeed),
                    startSize = Random.Range(0.004f, 0.009f),
                    startLifetime = Random.Range(0.25f, 0.5f),
                    startColor = c,
                };
                drops.Emit(p, 1);
            }
        }

        private ParticleSystem NewSystem(string name, Material material, int maxParticles, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateMist(Material material)
        {
            if (material == null) return null;
            ParticleSystem ps = NewSystem("Mist", material, 400, -0.015f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1.25f)));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = -2f; // フラスコのガラスより後ろに描かれないよう、少し手前扱い
            return ps;
        }

        private ParticleSystem CreateBubbles(Material material)
        {
            if (material == null) return null;
            ParticleSystem ps = NewSystem("Bubbles", material, 300, 0f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.15f)));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            return ps;
        }

        private ParticleSystem CreateDrops(Material material)
        {
            if (material == null) return null;
            ParticleSystem ps = NewSystem("Droplets", material, 200, 1f);
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            return ps;
        }

        // ------------------------------------------------------------------
        // 音
        // ------------------------------------------------------------------

        private AudioSource CreateLoop(string name)
        {
            var go = new GameObject("Sound_" + name);
            go.transform.SetParent(transform, false);
            go.transform.position = station != null ? Vector3.Lerp(station.BottomWorld, station.MouthWorld, 0.4f) : transform.position;
            var s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            s.spatialBlend = 0.6f;
            s.minDistance = 1f;
            s.maxDistance = 10f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            sources.Add(s);
            targets[s] = 0f;
            return s;
        }

        private void PlayOneShot(AudioClip[] clips, Vector3 at, float volume, float pitch, string label)
        {
            DropSoundCount++;
            LastDropSound = label;
            if (clips == null || clips.Length == 0)
            {
                SoundLibrary lib = AudioManager.Lib;
                if (lib != null && lib.plop != null) AudioManager.SeAt(lib.plop, at, volume, pitch);
                return;
            }
            AudioManager.SeAt(clips[Random.Range(0, clips.Length)], at, volume, pitch);
        }

        // ------------------------------------------------------------------
        // 液面の半径（液体のメッシュの形から、高さごとの半径の表を作る）
        // ------------------------------------------------------------------

        private void BuildRadiusTable()
        {
            if (liquidTransform == null) return;
            var mf = liquidTransform.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return;
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0) return;
            tableMinY = float.MaxValue;
            tableMaxY = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                tableMinY = Mathf.Min(tableMinY, v.y);
                tableMaxY = Mathf.Max(tableMaxY, v.y);
            }
            const int bins = 48;
            radiusTable = new float[bins];
            foreach (Vector3 v in vertices)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(tableMinY, tableMaxY, v.y) * (bins - 1) + 0.5f), 0, bins - 1);
                radiusTable[i] = Mathf.Max(radiusTable[i], Mathf.Sqrt(v.x * v.x + v.z * v.z));
            }
            // 頂点のない段は、隣の段から埋める
            for (int i = 1; i < bins; i++) if (radiusTable[i] <= 0f) radiusTable[i] = radiusTable[i - 1];
            for (int i = bins - 2; i >= 0; i--) if (radiusTable[i] <= 0f) radiusTable[i] = radiusTable[i + 1];
            Vector3 scale = liquidTransform.lossyScale;
            radiusScale = (Mathf.Abs(scale.x) + Mathf.Abs(scale.z)) * 0.5f;
        }

        /// <summary>この高さ（ワールド座標）での液面の半径</summary>
        private float SurfaceRadius(float worldY)
        {
            if (radiusTable == null || liquidTransform == null) return station != null ? station.BottomRadius : 0.1f;
            float localY = liquidTransform.InverseTransformPoint(new Vector3(liquidTransform.position.x, worldY, liquidTransform.position.z)).y;
            float t = Mathf.InverseLerp(tableMinY, tableMaxY, localY) * (radiusTable.Length - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, radiusTable.Length - 1);
            int j = Mathf.Min(i + 1, radiusTable.Length - 1);
            return Mathf.Lerp(radiusTable[i], radiusTable[j], t - i) * radiusScale;
        }

        private static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / Mathf.Max(0.001f, to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
