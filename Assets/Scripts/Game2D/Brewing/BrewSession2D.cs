using System;
using PotionFrontier.Alchemy;
using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 2D の調合画面の本体（シーンの GameSystems に付ける）。フラスコの中の計算（FlaskSimulation）を持ち、
    /// 注ぐ・入れる・混ぜる・火加減・瓶に詰めるを、在庫（素材・瓶の棚）とつないで行う。
    /// 見た目（Flask2D など）と操作（BrewInput2D）はここを読む・呼ぶだけで、計算はここにまとまっている。
    /// （3D 版の BrewingStation から、計算と在庫の部分だけを移したもの）
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class BrewSession2D : MonoBehaviour
    {
        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private AlchemyManager alchemy;
        [SerializeField] private IngredientInventory ingredients;
        [SerializeField] private PotionInventory potions;
        [SerializeField, Tooltip("調合画面の共通の絵（容器・自作の液体の瓶・欠片）")] private BrewArt2D art;

        [Header("フラスコ")]
        [SerializeField, Min(50f), Tooltip("入れられる量（ml）")] private float capacityMl = 600f;
        [SerializeField, Min(10f), Tooltip("瓶1本の量（ml）")] private float bottleMl = 100f;
        [SerializeField, Tooltip("机に素材が1つもなければ、GameBalance の「はじめから机にある素材」を並べる（GameFlow がないときだけ）")] private bool giveStarterIngredients = true;

        private FlaskSimulation flask;
        private LiquidRecord preview;
        private float previewTimer;
        private float fullWarningCooldown;
        private float stirTurnsThisFrame;
        private float stirSpeed;

        /// <summary>中身が変わったとき（4回/秒ほど。表示の更新用）</summary>
        public event Action ContentsChanged;
        /// <summary>画面に短く出すお知らせ</summary>
        public event Action<string> Message;
        /// <summary>瓶に詰めたとき（記録・新発見か・詰めた量 ml・本数）</summary>
        public event Action<LiquidRecord, bool, float, int> Bottled;
        /// <summary>反応が起きたとき</summary>
        public event Action<ReactionData> Reacted;
        /// <summary>固体・粉を入れたとき（素材）</summary>
        public event Action<IngredientData> SolidAdded;

        public FlaskSimulation Flask => flask;
        public AlchemyManager Alchemy => alchemy;
        public IngredientInventory Ingredients => ingredients;
        public PotionInventory Potions => potions;
        public BrewArt2D Art => art;
        /// <summary>今の中身を瓶に詰めたらどんな液体になるか（登録はしていない）。液体がなければ null</summary>
        public LiquidRecord Preview => preview;
        public float CapacityMl => capacityMl;
        public float BottleMl => bottleMl;
        public bool HasLiquid => flask != null && flask.HasLiquid;
        public float HeatLevel => flask != null ? flask.HeatLevel : 0f;
        /// <summary>今かき混ぜている速さ（回転/秒。見た目用になめらかにした値）</summary>
        public float StirSpeed => stirSpeed;
        public bool LastStirClockwise { get; private set; } = true;

        private void Awake()
        {
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
            if (ingredients == null) ingredients = FindFirstObjectByType<IngredientInventory>();
            if (potions == null) potions = FindFirstObjectByType<PotionInventory>();
            if (alchemy == null) Debug.LogError("[BrewSession2D] シーンに AlchemyManager がありません。", this);
            flask = alchemy != null ? alchemy.CreateFlask(capacityMl) : null;
            if (flask != null) flask.ReactionOccurred += OnReaction;
            else if (alchemy != null) Debug.LogError("[BrewSession2D] AlchemyManager に AlchemySettings が入っていません。", this);
        }

        private void Start()
        {
            // ゲームの流れ（GameFlow）がないとき（調合机だけを試すとき）は、机が空なら最初の素材を並べる
            if (giveStarterIngredients && GameFlow.Instance == null && ingredients != null && ingredients.UnlockedCount == 0)
            {
                GameBalance balance = GameContext.Balance;
                if (balance != null) ingredients.ResetToStarter(balance);
                else Debug.LogWarning("[BrewSession2D] GameContext に GameBalance が入っていないので、最初の素材を配れません。", this);
            }
            RefreshPreview();
        }

        private void OnDestroy()
        {
            if (flask != null) flask.ReactionOccurred -= OnReaction;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (flask != null) flask.Tick(dt);

            // かき混ぜる速さ（見た目用）。手を止めると数秒かけて弱まる
            float instant = dt > 0f ? stirTurnsThisFrame / dt : 0f;
            stirTurnsThisFrame = 0f;
            float k = instant > stirSpeed ? 1f - Mathf.Exp(-10f * dt) : 1f - Mathf.Exp(-1.5f * dt);
            stirSpeed = Mathf.Lerp(stirSpeed, instant, k);

            previewTimer -= dt;
            if (previewTimer <= 0f) RefreshPreview();
            if (fullWarningCooldown > 0f) fullWarningCooldown -= dt;
        }

        // ------------------------------------------------------------------
        // 操作
        // ------------------------------------------------------------------

        /// <summary>液体の素材を注ぐ（在庫から注いだ分だけ減る。使い放題なら減らない）。注いだ量（ml）を返す</summary>
        public float PourIngredient(IngredientData data, float ml)
        {
            if (flask == null || data == null || !data.IsPoured || ml <= 0f) return 0f;
            if (flask.RoomMl <= 0.01f)
            {
                WarnFull();
                return 0f;
            }
            float want = Mathf.Min(ml, flask.RoomMl);
            float usedUnits = ingredients != null
                ? ingredients.Consume(data, want / FlaskSimulation.MlPerUnit)
                : want / FlaskSimulation.MlPerUnit;
            if (usedUnits > 0f) DiscoveryJournal.Learn(data, IngredientFact.Used);
            return usedUnits > 0f ? flask.Pour(data, usedUnits * FlaskSimulation.MlPerUnit) : 0f;
        }

        /// <summary>手に持った自作の液体の瓶から注ぐ（瓶の中身が減る）。注いだ量（ml）を返す</summary>
        public float PourBottle(PotionStack bottle, float ml)
        {
            if (flask == null || alchemy == null || bottle == null || ml <= 0f) return 0f;
            if (flask.RoomMl <= 0.01f)
            {
                WarnFull();
                return 0f;
            }
            LiquidRecord record = alchemy.Liquids.Get(bottle.liquidId);
            if (record == null)
            {
                Say("この瓶の中身の記録が見つからないので、注げません");
                return 0f;
            }
            float want = Mathf.Min(ml, flask.RoomMl, bottle.ml);
            float poured = flask.PourLiquid(record, want);
            bottle.ml = Mathf.Max(0f, bottle.ml - poured);
            bottle.count = bottle.Bottles;
            return poured;
        }

        /// <summary>固体・粉を1回分入れる（在庫から1回分減る）。入れられたら true</summary>
        public bool AddSolid(IngredientData data)
        {
            if (flask == null || data == null || data.IsPoured) return false;
            float need = data.AmountPerUse;
            BrewRules rules = flask.Settings.Rules;
            float perUnit = data.Form == IngredientForm.Powder ? rules.powderVolumeMlPerUnit : rules.solidVolumeMlPerUnit;
            if (need * perUnit > flask.RoomMl + 1e-3f)
            {
                WarnFull();
                return false;
            }
            float used = ingredients != null ? ingredients.Consume(data, need) : need;
            if (used <= 0f) return false;
            flask.AddSolid(data, used);
            DiscoveryJournal.Learn(data, IngredientFact.Used);
            // 落ちる音は、欠片が液面に着いたときに FlaskEffects2D が鳴らす
            if (SolidAdded != null) SolidAdded(data);
            RefreshPreview();
            return true;
        }

        /// <summary>かき混ぜる（回転数は小数でよい。操作の途中で少しずつ渡す）</summary>
        public void Stir(float turns, bool clockwise)
        {
            if (flask == null || flask.IsEmpty || turns <= 0f) return;
            flask.Stir(turns, clockwise);
            stirTurnsThisFrame += turns;
            LastStirClockwise = clockwise;
        }

        /// <summary>火加減（0 = 消す 〜 1 = 最大）</summary>
        public void SetHeat(float level)
        {
            if (flask == null) return;
            float before = flask.HeatLevel;
            flask.SetHeat(level);
            float after = flask.HeatLevel;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null)
            {
                // 火をつけたら「ぼっ」、5% 回すごとに「カチッ」
                if (before <= 0.001f && after > 0.001f) AudioManager.Se(lib.ignite, 0.7f);
                else if (before > 0.001f && after <= 0.001f) AudioManager.Se(lib.ignite, 0.25f, 1.7f);
                else if (Mathf.FloorToInt(after * 20f + 0.0001f) != Mathf.FloorToInt(before * 20f + 0.0001f))
                    AudioManager.Se(lib.knobTick != null ? lib.knobTick : lib.uiClick, 0.5f, 0.85f + after * 0.45f);
            }
            RefreshPreview();
        }

        /// <summary>中身を全部瓶に詰める（100ml ずつ。最後は残りの量で1本）。素材トレイの下の「空き瓶」をクリックしたとき</summary>
        public LiquidRecord BottleAll() => BottleMany(Mathf.Max(1, BottlesForAll()));

        /// <summary>全部詰めたときの本数（少しのあまりは最後の瓶に一緒に入る）</summary>
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
        /// 指定した本数だけ瓶に詰めて、瓶の棚にしまい、作った液体の一覧に記録する（失敗作＝未知の液体も残す）。
        /// 同じ濃さのまま取り出すので、何本詰めても同じ液体になる。詰められなければ null
        /// </summary>
        public LiquidRecord BottleMany(int bottles)
        {
            if (flask == null || alchemy == null) return null;
            if (!HasLiquid)
            {
                Say(flask.IsEmpty ? "フラスコが空っぽです" : "液体がないので、瓶に詰められません（液体を注ぐと溶ける）");
                return null;
            }
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
            // ほんの少し（15ml 未満）しか残らないときは、それも一緒に詰める（同じ液体が別の液体として並ばないように）
            float leftover = flask.LiquidMl;
            if (leftover > 0.5f && leftover < flask.Settings.Rules.minVolumeMl * 1.5f)
            {
                flask.Bottle(leftover, out float rest);
                bottledMl += rest;
            }
            if (potions != null) potions.AddBottled(record, bottledMl);
            RefreshPreview();
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null)
            {
                // 夜の空気感：ファンファーレはやめて、栓の「ぽん」だけ（新発見のときは BrewFeedback2D がギターを鳴らす）
                AudioManager.Se(lib.corkPop, 0.6f);
                if (record.IsSpecial) AudioManager.Se(lib.brewSpecial, 0.3f);
            }
            if (Bottled != null) Bottled(record, isNew, bottledMl, filled);
            if (GameFlow.Instance != null) GameFlow.Instance.Save();
            return record;
        }

        // ------------------------------------------------------------------
        // セーブ（GameFlow から呼ぶ）
        // ------------------------------------------------------------------

        /// <summary>フラスコを空にして、火も消す（はじめから遊ぶとき。遊びの中で中身を捨てる操作はない）</summary>
        public void ResetFlask()
        {
            if (alchemy == null) return;
            if (flask != null) flask.ReactionOccurred -= OnReaction;
            flask = alchemy.CreateFlask(capacityMl);
            if (flask != null) flask.ReactionOccurred += OnReaction;
            RefreshPreview();
        }

        public FlaskSaveData ExportFlask() => flask != null ? flask.Export() : null;

        public void ImportFlask(FlaskSaveData data)
        {
            ResetFlask();
            if (flask == null || data == null) return;
            flask.Import(data, FindIngredient);
            RefreshPreview();
        }

        private static IngredientData FindIngredient(string id)
        {
            IngredientDatabase db = GameContext.Ingredients;
            return db != null ? db.Get(id) : null;
        }

        private static void PlayRandom(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            AudioManager.Se(clips[UnityEngine.Random.Range(0, clips.Length)], volume, UnityEngine.Random.Range(0.92f, 1.08f));
        }

        // ------------------------------------------------------------------
        // 在庫（トレイ・手に持つ物から使う）
        // ------------------------------------------------------------------

        public bool Has(IngredientData data) => data != null && (ingredients == null || ingredients.Has(data));

        /// <summary>素材の量の表示（∞・×3・6.0 など）</summary>
        public string AmountLabel(IngredientData data)
        {
            if (data == null) return "";
            if (ingredients == null) return "∞";
            return IngredientInventory.Format(data, ingredients.Get(data));
        }

        /// <summary>棚から瓶を1本取り出す（手に持つ用）。なければ null</summary>
        public PotionStack TakeBottle(PotionStack stack) => potions != null ? potions.TakeOne(stack) : null;

        /// <summary>手に持っていた瓶を棚に戻す（減っていれば、減った量のまま戻る）</summary>
        public void ReturnBottle(PotionStack single)
        {
            if (potions != null && single != null) potions.ReturnOne(single);
        }

        public void Say(string text)
        {
            if (!string.IsNullOrEmpty(text) && Message != null) Message(text);
        }

        // ------------------------------------------------------------------

        private void RefreshPreview()
        {
            previewTimer = 0.25f;
            preview = alchemy != null && flask != null && flask.HasLiquid ? alchemy.Preview(flask) : null;
            Observe();
            if (ContentsChanged != null) ContentsChanged();
        }

        private void WarnFull()
        {
            if (fullWarningCooldown > 0f) return;
            fullWarningCooldown = 2f;
            Say("フラスコがいっぱい（右下の空き瓶で、瓶に詰められる）");
        }

        /// <summary>
        /// フラスコの中で起きたことから、素材の性質を素材ノートに書き足す（初めて見つけたら、DiscoveryJournal がお知らせを出す）。
        /// 熱で弱まった・目覚めた・溶ける温度で溶けた・崩れた・香りが飛んだ・冷えた・ちがう向きでは溶けなかった・溶けきった・触媒が効いた
        /// </summary>
        private void Observe()
        {
            if (flask == null) return;
            flask.GetIngredientStates(observed);
            foreach (FlaskSimulation.IngredientState s in observed)
            {
                IngredientData d = s.ingredient;
                if (d == null) continue;
                if (s.heatDamaged && s.quality < 0.97f) DiscoveryJournal.Learn(d, IngredientFact.Weakens);
                if (s.activation > 0.05f) DiscoveryJournal.Learn(d, IngredientFact.Wakes);
                if (s.melted) DiscoveryJournal.Learn(d, IngredientFact.Melts);
                if (s.broken && s.quality < 0.9f) DiscoveryJournal.Learn(d, IngredientFact.Fragile);
                if (s.evaporated) DiscoveryJournal.Learn(d, IngredientFact.Volatile);
                if (s.cooled) DiscoveryJournal.Learn(d, IngredientFact.Cools);
                if (s.spunWrong) DiscoveryJournal.Learn(d, IngredientFact.OneWay);
                if (s.units > 0f && s.undissolved <= 1e-4f && flask.HasLiquid) DiscoveryJournal.Learn(d, IngredientFact.Solubility);
                if (s.units > 0f && s.units - s.undissolved > 0.05f && flask.HasLiquid) DiscoveryJournal.Learn(d, IngredientFact.Amplifies);
            }
        }

        private readonly System.Collections.Generic.List<FlaskSimulation.IngredientState> observed = new System.Collections.Generic.List<FlaskSimulation.IngredientState>();

        private void OnReaction(ReactionData reaction)
        {
            DiscoveryJournal.LearnReaction(reaction);
            if (Reacted != null) Reacted(reaction);
            if (reaction != null) Say($"反応「{reaction.DisplayName}」が起きた！");
        }
    }
}
