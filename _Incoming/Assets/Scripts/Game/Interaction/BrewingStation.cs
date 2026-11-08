using System;
using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 作業台の調合フラスコ（Prefab のルートに付ける）。中で起きることの計算は FlaskSimulation が行う。
    /// 歩いているときに [E] で調べると、調合台（BrewMode：カメラが寄って、マウスで直接作業する画面）に向かう。
    /// 調合台での操作（傾けて注ぐ・クリックで入れる・円を描いてかき混ぜる・つまみで火加減・瓶に詰める）は
    /// BrewMode から PourHeld／DropHeldSolid／Stir／SetHeat／BottleOne などを呼ぶ。
    /// 中身を捨てる操作はない：できた物は失敗作も含めてすべて瓶に詰めて残す（あとで別の調合に使える）。
    /// 見た目は Prefab の中身で決まる：液体は子の LiquidFill、口は「Mouth」、調合台のカメラは「BrewView」の目印。
    /// 自作のフラスコや鍋に差し替えられる。加熱台（BrewHeater）とガラス棒（StirRod）はシーンから探す。
    /// </summary>
    public class BrewingStation : Interactable
    {
        public static readonly float[] HeatSteps = { 0f, 0.3f, 0.6f, 1f };

        [Header("参照")]
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private PotionInventory inventory;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField, Tooltip("中の液体（ToonLiquid シェーダー＋LiquidFill）。空なら子から探す")] private LiquidFill liquid;
        [SerializeField, Tooltip("キラキラ・しぶき（加算）")] private Material sparkleMaterial;
        [SerializeField, Tooltip("泡・煙（半透明）")] private Material softParticleMaterial;
        [SerializeField, Tooltip("注ぐ液体の線")] private Material streamMaterial;

        [Header("調合台の道具（空ならシーンから探す）")]
        [SerializeField, Tooltip("フラスコの下の加熱台（火加減の見た目とつまみ）")] private BrewHeater heater;
        [SerializeField, Tooltip("フラスコに差したガラス棒")] private StirRod rod;

        [Header("容量・操作の速さ")]
        [SerializeField, Min(50f), Tooltip("入れられる量（ml）")] private float capacityMl = 600f;
        [SerializeField, Min(5f), Tooltip("1秒間に注がれる量（ml）")] private float pourRateMl = 90f;
        [SerializeField, Min(0.1f), Tooltip("かき混ぜるキーを押している間、1秒に何回転するか")] private float stirTurnsPerSecond = 1.6f;
        [SerializeField, Min(10f), Tooltip("瓶1本の量（ml）")] private float bottleMl = 100f;

        private FlaskSimulation flask;
        private LiquidRecord preview;
        private Color shownColor = new Color(-1f, 0f, 0f);
        private ParticleSystem sparkles;
        private ParticleSystem puff;
        private FlaskEffects effects;
        private PourStream stream;
        private Light flash;
        private Color currentColor = Color.white;
        private Color targetColor = new Color(0.75f, 0.88f, 1f);
        private float shownFill = -1f;
        private float fullWarningCooldown;
        private bool pouring;
        private float flashTime = 10f;
        private float previewTimer;
        private float bottomRadius = 0.12f;

        /// <summary>中身が変わったとき（HUDのフラスコ表示用。4回/秒ほど）</summary>
        public event Action ContentsChanged;

        public FlaskSimulation Flask => flask;
        public BrewHeater Heater => heater;
        public StirRod Rod => rod;
        /// <summary>フラスコの中の時間の進む速さ（調合台の早送りで 4 などにする）</summary>
        public float SimSpeed { get; set; } = 1f;
        /// <summary>フラスコの底の中心</summary>
        public Vector3 BottomWorld => liquid != null ? liquid.SurfacePoint(0f) : transform.position;
        /// <summary>フラスコの底の半径（ガラス棒の先が回る円の大きさに使う）</summary>
        public float BottomRadius => bottomRadius;
        /// <summary>今の液面の位置（液体がなければ底）</summary>
        public Vector3 SurfacePoint => SurfaceWorld;
        /// <summary>フラスコに見えている液体の色（演出のしぶき・泡に使う）</summary>
        public Color DisplayColor => currentColor;
        /// <summary>演出と音（湯気・泡・波紋・渦・操作の音）</summary>
        public FlaskEffects Effects => effects;
        /// <summary>今の中身を瓶に詰めたらどんな液体になるか（登録はしていない）</summary>
        public LiquidRecord Preview => preview;
        public float CapacityMl => capacityMl;
        public float BottleMl => bottleMl;
        public AlchemyManager Alchemy => alchemy;
        /// <summary>瓶に詰められる液体がある（溶け残りだけのときは false）</summary>
        public bool HasLiquid => flask != null && flask.HasLiquid;

        /// <summary>フラスコの口（「Mouth」の目印。なければ見た目の上端）</summary>
        public Vector3 MouthWorld => PrefabAnchors.WorldPoint(transform, PrefabAnchors.Mouth);

        /// <summary>今の液面の位置（液体がなければ底）</summary>
        private Vector3 SurfaceWorld => liquid != null ? liquid.SurfacePoint(Mathf.Max(0f, shownFill)) : transform.position;

        public void Configure(LiquidFill liquid, Material sparkleMaterial, Material softParticleMaterial, Material streamMaterial)
        {
            this.liquid = liquid;
            this.sparkleMaterial = sparkleMaterial;
            this.softParticleMaterial = softParticleMaterial;
            this.streamMaterial = streamMaterial;
        }

        private void Awake()
        {
            // Prefab からはシーンの部品を指定できないので、起動時に探す
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (inventory == null) inventory = FindFirstObjectByType<PotionInventory>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (liquid == null) liquid = GetComponentInChildren<LiquidFill>();
            if (liquid != null)
            {
                liquid.SetAmount(0f);
                var r = liquid.GetComponent<Renderer>();
                if (r != null) bottomRadius = Mathf.Max(0.03f, Mathf.Min(r.bounds.extents.x, r.bounds.extents.z) * 0.9f);
            }
            if (heater == null) heater = FindFirstObjectByType<BrewHeater>();
            if (rod == null) rod = FindFirstObjectByType<StirRod>();
            flask = alchemy != null ? alchemy.CreateFlask(capacityMl) : null;
            if (flask != null) flask.ReactionOccurred += OnReaction;

            var streamGo = new GameObject("PourStream");
            streamGo.transform.SetParent(transform, false);
            stream = streamGo.AddComponent<PourStream>();
            stream.Init(streamMaterial, softParticleMaterial);

            sparkles = CreateBurst("BrewSparkles", sparkleMaterial, 60, new ParticleSystem.MinMaxCurve(0.3f, 1.2f), -0.15f);
            puff = CreateBurst("BrewPuff", softParticleMaterial, 25, new ParticleSystem.MinMaxCurve(0.15f, 0.5f), -0.05f);
            ParticleSystem.MainModule puffMain = puff.main;
            puffMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            puffMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);

            // 湯気・泡・しぶき・液面の動き・操作の音（FlaskEffects）
            effects = gameObject.AddComponent<FlaskEffects>();
            effects.Init(this, liquid);
            effects.StylePuff(puff);

            var lightGo = new GameObject("BrewFlash");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.position = Vector3.Lerp(transform.position, MouthWorld, 0.6f);
            flash = lightGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 2.5f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            if (flask != null) flask.Tick(dt * Mathf.Max(0f, SimSpeed));
            if (heater != null && flask != null) heater.SetLevel(flask.HeatLevel);
            if (rod != null) rod.UpdatePose(MouthWorld, BottomWorld, bottomRadius, dt);

            previewTimer -= dt;
            if (previewTimer <= 0f) RefreshPreview();

            float k = 1f - Mathf.Exp(-4f * dt);
            currentColor = Color.Lerp(currentColor, targetColor, k);
            if (liquid != null && ((Vector4)(currentColor - shownColor)).sqrMagnitude > 1e-6f)
            {
                shownColor = currentColor;
                // 光らせすぎると色が白っぽくなるので、ほんの少しだけ
                liquid.SetColor(currentColor, 0.06f);
            }

            float targetFill = HasLiquid ? Mathf.Clamp01(flask.VolumeMl / capacityMl) : 0f;
            if (Mathf.Abs(shownFill - targetFill) > 0.0005f)
            {
                shownFill = shownFill < 0f ? targetFill : Mathf.MoveTowards(shownFill, targetFill, dt * 0.8f);
                if (liquid != null) liquid.SetAmount(shownFill);
            }

            // 瓶詰めの瞬間の光
            flashTime += dt;
            bool flashing = flashTime < 1.2f;
            // 光っていないときはライトごと止める（ライトが1つ減るだけで、周りの物の描画が軽くなる）
            if (flash.enabled != flashing) flash.enabled = flashing;
            if (flashing) flash.intensity = Mathf.Sin(Mathf.Clamp01(flashTime / 1.2f) * Mathf.PI) * 1.3f;

            if (fullWarningCooldown > 0f) fullWarningCooldown -= dt;
        }

        private void RefreshPreview()
        {
            previewTimer = 0.25f;
            preview = alchemy != null && flask != null && HasLiquid ? alchemy.Preview(flask) : null;
            if (preview != null) targetColor = preview.color;
            if (ContentsChanged != null) ContentsChanged();
        }

        // ------------------------------------------------------------------
        // Interactable
        // ------------------------------------------------------------------

        public override string GetDisplayName(PlayerInteractor player)
        {
            return "調合フラスコ";
        }

        public override string GetSubtitle(PlayerInteractor player)
        {
            if (flask == null) return null;
            string heat = ProcessStep.HeatLabel(flask.HeatLevel);
            string temp = $"{Mathf.RoundToInt(flask.Temperature)}℃（{heat}）{(flask.IsBoiling ? " ぐつぐつ" : "")}";
            if (!HasLiquid) return flask.IsEmpty ? $"空っぽ（{capacityMl:0}ml）　{temp}" : $"溶け残りがある　{temp}";
            return $"{flask.VolumeMl:0} / {capacityMl:0} ml　{temp}";
        }

        public override void GetOptions(PlayerInteractor player, List<InteractionOption> options)
        {
            // 調合台（マウスで作業する画面）に向かう。手に持っている物もそのまま持っていける
            if (BrewMode.Instance != null)
            {
                options.Add(new InteractionOption(KeyCode.E, "E", "調合台に向かう"));
                return;
            }

            IngredientData held = player.HeldIngredient;
            PotionStack heldBottle = player.HeldPotion;
            if (held != null)
            {
                if (held.IsPoured) options.Add(new InteractionOption(KeyCode.E, "E 長押し", $"{held.DisplayName}を注ぐ", true));
                else options.Add(new InteractionOption(KeyCode.E, "E", $"{held.DisplayName}を入れる"));
            }
            else if (heldBottle != null)
            {
                options.Add(new InteractionOption(KeyCode.E, "E 長押し", $"{heldBottle.potionName}を注ぐ（{heldBottle.ml:0}ml）", true));
            }
            else if (HasLiquid)
            {
                options.Add(new InteractionOption(KeyCode.E, "E", "瓶に詰める"));
            }

            if (flask != null && !flask.IsEmpty)
            {
                options.Add(new InteractionOption(KeyCode.C, "C 長押し", "時計回りに混ぜる", true));
                options.Add(new InteractionOption(KeyCode.V, "V 長押し", "反時計回りに混ぜる", true));
            }
            if (flask != null)
            {
                float next = NextHeat(flask.HeatLevel);
                options.Add(new InteractionOption(KeyCode.H, "H", $"火加減：{ProcessStep.HeatLabel(flask.HeatLevel)} → {ProcessStep.HeatLabel(next)}"));
            }
            if ((held != null || heldBottle != null) && HasLiquid) options.Add(new InteractionOption(KeyCode.F, "F", "瓶に詰める"));
            int all = BottlesForAll();
            if (all > 1) options.Add(new InteractionOption(KeyCode.F, "Shift+F", $"全部詰める（{all}本）"));
        }

        public override void OnPress(PlayerInteractor player, KeyCode key)
        {
            IngredientData held = player.HeldIngredient;
            if (key == KeyCode.E && BrewMode.Instance != null)
            {
                BrewMode.Instance.Enter();
                return;
            }
            switch (key)
            {
                case KeyCode.E:
                    if (held == null && player.HeldPotion == null) BottleOne();
                    else if (held != null && !held.IsPoured) AddSolid(player, held);
                    break;
                case KeyCode.F:
                    if (IsShiftHeld()) BottleAll();
                    else BottleOne();
                    break;
                case KeyCode.H:
                    CycleHeat();
                    break;
            }
        }

        public override void OnHold(PlayerInteractor player, KeyCode key, float deltaTime)
        {
            if (key == KeyCode.C || key == KeyCode.V)
            {
                Stir(stirTurnsPerSecond * deltaTime, key == KeyCode.C);
                return;
            }
            if (key != KeyCode.E || (player.HeldIngredient == null && player.HeldPotion == null)) return;
            if (player.HeldIngredient != null && !player.HeldIngredient.IsPoured) return;

            if (player.HeldView != null) player.HeldView.SetPouring(true);
            // 瓶が傾ききるまでは液体は出ない
            if (player.HeldView != null && !player.HeldView.IsPouring)
            {
                stream.Stop();
                return;
            }
            Vector3 mouth = player.HeldView != null ? player.HeldView.MouthWorldPosition : MouthWorld + Vector3.up * 0.3f;
            PourHeld(player, pourRateMl, deltaTime, mouth);
            if (player.HeldView != null && player.HeldIngredient != null) player.HeldView.SetAmount(player.HeldAmountRatio(player.HeldIngredient));
        }

        /// <summary>
        /// 手に持っている液体（液体の素材・瓶詰めした液体）を注ぐ。注いだ量（ml）を返す。
        /// mlPerSecond は注ぐ速さ（調合台では瓶の傾きで決まる）、from は注ぎ口の位置（液体の線の出発点）。
        /// 液体の素材は在庫から減らし（使い放題なら減らない）、瓶は中身が減る。使い切ったら手から消える
        /// </summary>
        public float PourHeld(PlayerInteractor player, float mlPerSecond, float deltaTime, Vector3 from)
        {
            if (flask == null || player == null || mlPerSecond <= 0f || deltaTime <= 0f)
            {
                StopPourEffects();
                return 0f;
            }
            if (flask.RoomMl <= 0.01f)
            {
                StopPourEffects();
                WarnFull();
                return 0f;
            }

            PotionStack bottle = player.HeldPotion;
            if (bottle != null)
            {
                LiquidRecord record = alchemy != null ? alchemy.Liquids.Get(bottle.liquidId) : null;
                if (record == null)
                {
                    StopPourEffects();
                    AtelierHUD hud = AtelierHUD.Instance;
                    if (hud != null) hud.ShowToast("この瓶の中身の記録が見つからないので、注げません");
                    return 0f;
                }
                float ml = Mathf.Min(mlPerSecond * deltaTime, flask.RoomMl, bottle.ml);
                float poured = flask.PourLiquid(record, ml);
                bottle.ml = Mathf.Max(0f, bottle.ml - poured);
                bottle.count = bottle.Bottles;
                Color color = bottle.color;
                player.HeldPotionPoured();
                if (player.HeldPotion == null)
                {
                    // 瓶が空になった
                    StopPour();
                    return poured;
                }
                SetPouring(true);
                stream.Pour(from, SurfaceWorld + Vector3.up * 0.01f, color);
                if (effects != null) effects.Pouring(poured / Mathf.Max(deltaTime, 1e-4f), color);
                return poured;
            }

            IngredientData held = player.HeldIngredient;
            if (held == null || !held.IsPoured)
            {
                StopPourEffects();
                return 0f;
            }
            float want = Mathf.Min(mlPerSecond * deltaTime, flask.RoomMl);
            // 在庫から注いだ分だけ減らす（足りなければ、あるだけ。使い放題なら減らない）
            float usedUnits = ingredients != null ? ingredients.Consume(held, want / FlaskSimulation.MlPerUnit) : want / FlaskSimulation.MlPerUnit;
            float pouredMl = usedUnits > 0f ? flask.Pour(held, usedUnits * FlaskSimulation.MlPerUnit) : 0f;
            if (ingredients != null && !ingredients.Has(held))
            {
                // 使い切った
                StopPour();
                player.UsedUpHeldIngredient();
                return pouredMl;
            }
            SetPouring(true);
            stream.Pour(from, SurfaceWorld + Vector3.up * 0.01f, held.Color);
            if (effects != null) effects.Pouring(pouredMl / Mathf.Max(deltaTime, 1e-4f), held.Color);
            return pouredMl;
        }

        /// <summary>注ぐのをやめる（液体の線と音を止めて、表示を新しくする）</summary>
        public void StopPour()
        {
            StopPourEffects();
            RefreshPreview();
        }

        private void StopPourEffects()
        {
            SetPouring(false);
            stream.Stop();
            if (effects != null) effects.StopPouring();
        }

        /// <summary>手に持っている固体・粉を1回分入れる（from は入れ口。そこから欠片が落ちる）。入れられたら true</summary>
        public bool DropHeldSolid(PlayerInteractor player, Vector3 from)
        {
            IngredientData held = player != null ? player.HeldIngredient : null;
            if (held == null || held.IsPoured) return false;
            return AddSolid(player, held, from);
        }

        public override void OnRelease(PlayerInteractor player, KeyCode key)
        {
            if (key != KeyCode.E) return;
            SetPouring(false);
            stream.Stop();
            if (player.HeldView != null) player.HeldView.SetPouring(false);
            RefreshPreview();
        }

        // ------------------------------------------------------------------
        // 操作（自動テストからも呼べる）
        // ------------------------------------------------------------------

        /// <summary>かき混ぜる。animateRod = false のときはガラス棒を回さない（調合台でマウスに合わせて動かすとき）</summary>
        public void Stir(float turns, bool clockwise, bool animateRod = true)
        {
            if (flask == null || flask.IsEmpty || turns <= 0f) return;
            flask.Stir(turns, clockwise);
            if (animateRod && rod != null) rod.Advance(turns, clockwise);
            if (effects != null) effects.Stirred(turns, clockwise);
        }

        public void SetHeat(float level)
        {
            if (flask == null) return;
            float previous = flask.HeatLevel;
            flask.SetHeat(level);
            if (effects != null) effects.HeatChanged(previous, flask.HeatLevel);
            RefreshPreview();
        }

        public void CycleHeat()
        {
            if (flask == null) return;
            float next = NextHeat(flask.HeatLevel);
            SetHeat(next);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.knobTick != null ? lib.knobTick : lib.uiClick, 0.6f, 0.8f + next * 0.4f);
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast($"火加減：{ProcessStep.HeatLabel(next)}");
        }

        /// <summary>Shift を押しているか（Shift と F をほぼ同時に素早く押して離したときも、押していたことにする）</summary>
        private static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                   Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) ||
                   Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift);
        }

        private static float NextHeat(float current)
        {
            for (int i = 0; i < HeatSteps.Length; i++)
            {
                if (HeatSteps[i] > current + 0.01f) return HeatSteps[i];
            }
            return HeatSteps[0];
        }

        /// <summary>
        /// 1本（100ml）瓶に詰める。できた液体はポーション棚にしまい、これまでに作った液体の一覧に記録する。
        /// 失敗作（未知の液体）も捨てずに残す。詰められなければ null
        /// </summary>
        public LiquidRecord BottleOne()
        {
            return BottleMany(1);
        }

        /// <summary>フラスコの中身を全部瓶に詰める（100ml ずつ。最後は残りの量で1本）</summary>
        public LiquidRecord BottleAll()
        {
            if (flask == null) return null;
            return BottleMany(Mathf.Max(1, BottlesForAll()));
        }

        /// <summary>全部詰めたときの本数（100ml ずつ。少しのあまりは最後の瓶に一緒に入る）</summary>
        public int BottlesForAll()
        {
            if (!HasLiquid) return 0;
            float ml = flask.LiquidMl;
            int full = Mathf.FloorToInt(ml / bottleMl + 0.005f);
            float rest = ml - full * bottleMl;
            if (full == 0) return 1;
            return rest >= flask.Settings.Rules.minVolumeMl * 1.5f ? full + 1 : full;
        }

        /// <summary>
        /// 指定した本数だけ瓶に詰める（液体がなくなったらそこまで）。同じ濃さのまま取り出すので、何本詰めても同じ液体になる。
        /// できた液体は棚にしまい、これまでに作った液体の一覧に記録する（作った回数は1回）
        /// </summary>
        public LiquidRecord BottleMany(int bottles)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            SoundLibrary lib = AudioManager.Lib;
            if (flask == null || alchemy == null) return null;
            if (!HasLiquid)
            {
                if (hud != null) hud.ShowToast(flask.IsEmpty ? "フラスコが空っぽです" : "液体がないので、瓶に詰められません");
                return null;
            }

            // 1本目で記録する。2本目からは同じ濃さの液体を同じ記録として取り出す
            LiquidRecord record = alchemy.Bottle(flask, bottleMl, out float bottledMl, out bool isNew);
            if (record == null) return null;
            int filled = 1;
            for (int i = 1; i < bottles && HasLiquid; i++)
            {
                flask.Bottle(bottleMl, out float more);
                if (more <= 0f) break;
                bottledMl += more;
                filled++;
            }
            // ほんの少し（15ml 未満）しか残らないときは、それも一緒に詰める
            // （少しだけ別に詰めると、同じ液体なのに別の液体として棚に並んでしまうため）
            float leftover = flask.LiquidMl;
            if (leftover > 0.5f && leftover < flask.Settings.Rules.minVolumeMl * 1.5f)
            {
                flask.Bottle(leftover, out float rest);
                bottledMl += rest;
            }
            PotionStack stack = inventory != null ? inventory.AddBottled(record, bottledMl) : null;
            bool stored = stack != null;

            Color c = record.color;
            c.a = 1f;
            bool unknown = record.kind == LiquidKind.Unknown;
            PlayBurst(unknown ? puff : sparkles, unknown ? Color.Lerp(c, Color.gray, 0.4f) : c);
            if (isNew) PlayBurst(sparkles, Color.white, 30);
            flash.color = c;
            flashTime = unknown ? 0.6f : 0f;
            if (effects != null) effects.Bottled();
            if (lib != null)
            {
                if (record.IsSpecial) AudioManager.Se(lib.brewSpecial);
                else if (record.kind == LiquidKind.Potion) AudioManager.Se(lib.brewSuccess);
                else AudioManager.Se(lib.brewSuccess, 0.6f, 0.85f);
            }

            RefreshPreview();
            if (hud != null) hud.ShowResult(record, isNew, stored, bottledMl, filled, stack);
            if (GameFlow.Instance != null) GameFlow.Instance.Save();
            return record;
        }

        // ------------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------------

        /// <summary>はじめから遊ぶとき：フラスコを空にして、火も消す（遊びの中で中身を捨てる操作はない）</summary>
        public void ResetFlask()
        {
            if (alchemy == null) return;
            if (flask != null) flask.ReactionOccurred -= OnReaction;
            flask = alchemy.CreateFlask(capacityMl);
            if (flask != null) flask.ReactionOccurred += OnReaction;
            shownFill = -1f;
            RefreshPreview();
        }

        public FlaskSaveData ExportFlask()
        {
            return flask != null ? flask.Export() : null;
        }

        public void ImportFlask(FlaskSaveData data)
        {
            ResetFlask();
            if (flask == null || data == null) return;
            flask.Import(data, FindIngredient);
            shownFill = -1f;
            RefreshPreview();
        }

        /// <summary>フェーズ4のセーブ（素材と量だけ）を読み込む：素材を入れ直す</summary>
        public void ImportLegacy(List<IngredientStockEntry> saved)
        {
            ResetFlask();
            if (flask == null || saved == null) return;
            foreach (IngredientStockEntry e in saved)
            {
                IngredientData data = FindIngredient(e.id);
                if (data == null || e.amount <= 0f) continue;
                if (data.IsPoured) flask.Pour(data, e.amount * FlaskSimulation.MlPerUnit);
                else flask.AddSolid(data, e.amount);
            }
            RefreshPreview();
        }

        private static IngredientData FindIngredient(string id)
        {
            IngredientDatabase db = GameContext.Ingredients;
            return db != null ? db.Get(id) : null;
        }

        // ------------------------------------------------------------------

        private void AddSolid(PlayerInteractor player, IngredientData held)
        {
            Vector3 from = player.HeldView != null && player.HeldView.HasItem ? player.HeldView.MouthWorldPosition : MouthWorld + Vector3.up * 0.25f;
            if (AddSolid(player, held, from) && player.HeldView != null)
            {
                player.HeldView.PlayShake();
                if (player.HeldIngredient != null) player.HeldView.SetAmount(player.HeldAmountRatio(held));
            }
        }

        /// <summary>固体・粉を1回分入れる（from から欠片が落ちる）。入れられたら true</summary>
        private bool AddSolid(PlayerInteractor player, IngredientData held, Vector3 from)
        {
            if (flask == null) return false;
            float need = held.AmountPerUse;
            BrewRules rules = flask.Settings.Rules;
            float perUnit = held.Form == IngredientForm.Powder ? rules.powderVolumeMlPerUnit : rules.solidVolumeMlPerUnit;
            if (need * perUnit > flask.RoomMl + 1e-3f)
            {
                WarnFull();
                return false;
            }
            float used = ingredients != null ? ingredients.Consume(held, need) : need;
            if (used <= 0f)
            {
                player.UsedUpHeldIngredient();
                return false;
            }
            flask.AddSolid(held, used);
            StartCoroutine(DropAnimation(held, from));
            if (ingredients != null && !ingredients.Has(held)) player.UsedUpHeldIngredient();
            RefreshPreview();
            return true;
        }

        private void OnReaction(ReactionData reaction)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast($"反応が起きた！「{reaction.DisplayName}」");
            PlayBurst(sparkles, Color.Lerp(currentColor, Color.white, 0.5f), 40);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.brewSpecial, 0.7f, 1.2f);
        }

        private void WarnFull()
        {
            if (fullWarningCooldown > 0f) return;
            fullWarningCooldown = 1.5f;
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast("フラスコがいっぱいです！");
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.warning, 0.8f);
        }

        private void SetPouring(bool on)
        {
            if (pouring == on) return;
            pouring = on;
            // 注ぐ音は FlaskEffects が、注ぐ速さとフラスコの量に合わせて鳴らす
        }

        /// <summary>欠片が口からフラスコの液面へ落ちる</summary>
        private IEnumerator DropAnimation(IngredientData data, Vector3 from)
        {
            GameObject piece = CreatePiece(data);
            Transform t = piece.transform;
            Vector3 mouth = MouthWorld;
            Vector3 start = from;
            const float duration = 0.45f;
            yield return new WaitForSeconds(0.12f); // 容器が傾くのを待つ
            for (float time = 0f; time < duration; time += UnityEngine.Time.deltaTime)
            {
                if (t == null) yield break;
                float k = time / duration;
                // 口の上を通って液面へ（ゆるい放物線）
                Vector3 above = mouth + Vector3.up * 0.12f;
                Vector3 a = Vector3.Lerp(start, above, Mathf.Clamp01(k * 1.8f));
                Vector3 b = Vector3.Lerp(above, SurfaceWorld, Mathf.Clamp01((k - 0.45f) / 0.55f));
                t.position = k < 0.45f ? a : b;
                t.Rotate(new Vector3(220f, 140f, 0f) * UnityEngine.Time.deltaTime, Space.Self);
                yield return null;
            }
            if (t != null) Destroy(piece);
            // 着いた所の波紋・しぶき・泡と、固体／粉の音（空のフラスコなら底に当たる音）
            if (effects != null) effects.Dropped(data, HasLiquid ? SurfaceWorld : BottomWorld);
        }

        /// <summary>落とす欠片（素材データの欠片モデル → 見た目の一覧の標準の欠片 → 色の付いた粒）</summary>
        private GameObject CreatePiece(IngredientData data)
        {
            GameObject prefab = data.DropPiecePrefab != null ? data.DropPiecePrefab : GameContext.Art != null ? GameContext.Art.dropPiece : null;
            GameObject piece;
            if (prefab != null) piece = Instantiate(prefab);
            else
            {
                piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                piece.transform.localScale = Vector3.one * 0.045f;
            }
            piece.name = "DroppedPiece";
            foreach (Collider c in piece.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var content = piece.GetComponent<ContentVisual>();
            if (content != null) content.SetColor(data.Color);
            else if (data.DropPiecePrefab == null)
            {
                var block = new MaterialPropertyBlock();
                foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
                {
                    r.GetPropertyBlock(block);
                    block.SetColor("_Color", data.Color);
                    r.SetPropertyBlock(block);
                }
            }
            return piece;
        }

        private ParticleSystem CreateBurst(string name, Material material, int count, ParticleSystem.MinMaxCurve speed, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = MouthWorld;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var psr = go.GetComponent<ParticleSystemRenderer>();
            if (material != null) psr.sharedMaterial = material;
            return ps;
        }

        private void PlayBurst(ParticleSystem ps, Color color, int count = -1)
        {
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.3f), color);
            if (count > 0) ps.Emit(count);
            else ps.Play();
        }
    }
}
