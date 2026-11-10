using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// フラスコの工程の計算（FlaskSimulation）を、サンプル素材でまとめて確かめる（エディタ専用）。
    /// メニュー Tools > Potion Frontier > 開発 > 調合の計算をテスト。結果は Logs/claude_simtest.txt にも書き出す。
    /// 薄める・煮詰める・焦げる・熱で弱まる・目覚める・入れる順番・混ぜる向き・反応・多段調合・名前・同じ液体の判定・
    /// 時間の進め方で結果が変わらないこと・セーブと読み込み・発見で素材が届くこと・伝説の特効薬が作れること、を見る。
    /// </summary>
    public static class FlaskSimulationTests
    {
        private static readonly StringBuilder report = new StringBuilder();
        private static int passed;
        private static int failed;
        private static AlchemySampleDataGenerator.SampleData d;
        private static LiquidAnalyzer analyzer;

        [MenuItem("Tools/Potion Frontier/開発/調合の計算をテスト", false, 200)]
        public static void RunMenu()
        {
            string summary = Run();
            Debug.Log("[SimTest] " + summary);
        }

        public static string Run()
        {
            report.Clear();
            passed = 0;
            failed = 0;
            d = AlchemySampleDataGenerator.CreateOrUpdateAssets();
            analyzer = new LiquidAnalyzer(d.settings, d.Recipes);

            try
            {
                Basics();
                Dilution();
                Boiling();
                HeatAndActivation();
                Order();
                Direction();
                MultiStage();
                ReactionTest();
                Step5Traits();
                Naming();
                Identity();
                Determinism();
                PartialAndResidue();
                SaveLoad();
                Inventories();
                Arrivals();
                FinalPotion();
            }
            catch (Exception e)
            {
                failed++;
                Line("!! 例外: " + e);
            }

            string summary = $"調合の計算テスト：成功 {passed} ／ 失敗 {failed}";
            report.Insert(0, summary + "\n\n");
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "claude_simtest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString());
            return summary;
        }

        // ------------------------------------------------------------------

        private static void Basics()
        {
            Section("基本（水＋薬草）");
            LiquidRecord noStir = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 1f); f.AddSolid(d.herb, 1f); f.Tick(2f); });
            LiquidRecord stirred = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 8f, true, 4f); f.Tick(4f); });
            Show("混ぜずに2秒", noStir);
            Show("よく混ぜた", stirred);
            Check("混ぜるとよく溶けて回復が強くなる", H(stirred) > H(noStir) * 1.5f, $"{H(noStir):0.0} → {H(stirred):0.0}");
            Check("よく混ぜた水＋薬草2つはポーション", stirred.kind == LiquidKind.Potion, stirred.autoName);
        }

        private static void Dilution()
        {
            Section("薄める");
            LiquidRecord a = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(3f); });
            LiquidRecord b = Make(f => { f.Pour(d.water, 200f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(3f); });
            Show("水100ml", a);
            Show("水200ml", b);
            float ratio = H(b) / Mathf.Max(0.01f, H(a));
            Check("水を倍にすると回復の濃さはおよそ半分", ratio > 0.45f && ratio < 0.62f, $"比 {ratio:0.00}");
        }

        private static void Boiling()
        {
            Section("煮詰める・焦げる");
            FlaskSimulation f = New();
            f.Pour(d.spiritWater, 200f);
            float before = d.spiritWater.Attributes[0].value;
            f.SetHeat(1f);
            f.Tick(30f);
            float volume = f.VolumeMl;
            LiquidRecord boiled = Bottle(f);
            Show($"霊泉の水200ml を30秒 強火（残り {volume:0}ml）", boiled);
            float light = boiled.Attributes[PotionAttribute.Light];
            Check("沸騰すると量が減る", volume < 160f, $"{volume:0}ml");
            Check("量が減った分だけ濃くなる", Mathf.Abs(light - before * 200f / volume) < 0.6f, $"光 {light:0.0}（期待 {before * 200f / volume:0.0}）");

            LiquidRecord scorched = Make(x => { x.Pour(d.spiritWater, 40f); x.SetHeat(1f); x.Tick(40f); });
            Show("40ml を40秒 強火", scorched);
            Check("煮詰めすぎると焦げて未知の液体になる", scorched.TraitStrength(LiquidTraits.Scorched) > 0.5f && scorched.kind == LiquidKind.Unknown, scorched.autoName);
            Check("焦げた液体は名前に「焦げた」が付く", scorched.autoName.StartsWith("焦げた"), scorched.autoName);

            // たくさん作って煮詰めても、強くできるのは約4倍まで（それ以上減らすと焦げて弱まる）。熱に強い霊泉の水で確かめる
            FlaskSimulation big = New();
            big.Pour(d.spiritWater, 400f);
            float baseLight = big.Snapshot().concentration[PotionAttribute.Light];
            big.SetHeat(1f);
            for (int i = 0; i < 600 && big.VolumeMl > 130f; i++) big.Tick(1f);
            float light30 = big.Snapshot().concentration[PotionAttribute.Light];
            float scorch30 = big.Scorch;
            for (int i = 0; i < 600 && big.VolumeMl > 40f; i++) big.Tick(1f);
            LiquidRecord over = Bottle(big);
            Show($"霊泉の水 400ml を煮詰めた（光 {baseLight:0.0} → 約3分の1で {light30:0.0}）", over);
            Check("3分の1まで煮詰めても焦げない（濃さはおよそ3倍）", scorch30 < 0.01f && light30 > baseLight * 2.5f, $"{baseLight:0.0} → {light30:0.0}（焦げ {scorch30:0.00}）");
            Check("4分の1より減らすと焦げ始め、それ以上は強くならない", over.TraitStrength(LiquidTraits.Scorched) > 0.3f && over.Attributes[PotionAttribute.Light] < baseLight * 5f,
                  $"光 {over.Attributes[PotionAttribute.Light]:0.0}（10倍なら {baseLight * 10f:0}）／ 焦げ {over.TraitStrength(LiquidTraits.Scorched):0.00}");
        }

        private static void HeatAndActivation()
        {
            Section("熱で弱まる・温めると目覚める");
            LiquidRecord cold = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(10f); });
            LiquidRecord hot = Make(f => { f.Pour(d.water, 100f); f.SetHeat(0.6f); f.Tick(25f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(10f); });
            Show("薬草を冷たいまま", cold);
            Show("薬草を中火（80℃台）のお湯に", hot);
            Check("熱に弱い薬草は、熱いと弱まる", H(hot) < H(cold) * 0.8f, $"{H(cold):0.0} → {H(hot):0.0}");

            LiquidRecord plain = Make(f => { f.Pour(d.oil, 100f); f.AddSolid(d.fireGrass, 2f); Stir(f, 10f, true, 5f); f.Tick(15f); });
            LiquidRecord awake = Make(f => { f.Pour(d.oil, 100f); f.AddSolid(d.fireGrass, 2f); Stir(f, 10f, true, 5f); f.SetHeat(0.65f); f.Tick(25f); f.SetHeat(0f); f.Tick(2f); });
            Show("火炎草：温めない", plain);
            Show("火炎草：80℃以上で温めた", awake);
            float fire0 = plain.Attributes[PotionAttribute.Fire], fire1 = awake.Attributes[PotionAttribute.Fire];
            Check("火炎草は温めると火の力が目覚める", fire1 > fire0 * 1.3f, $"{fire0:0.0} → {fire1:0.0}");
        }

        private static void Order()
        {
            Section("入れる順番（触媒）");
            LiquidRecord catalystFirst = Make(f =>
            {
                f.Pour(d.water, 100f); f.AddSolid(d.amplifyCrystal, 1f); Stir(f, 15f, true, 6f); f.Tick(8f);
                f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(4f);
            });
            LiquidRecord catalystLast = Make(f =>
            {
                f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(8f);
                f.AddSolid(d.amplifyCrystal, 1f); Stir(f, 15f, true, 6f); f.Tick(4f);
            });
            Show("結晶を先に溶かす", catalystFirst);
            Show("結晶をあとから", catalystLast);
            Check("触媒は、溶けたあとに溶けた素材を強める（順番で結果が変わる）", H(catalystFirst) > H(catalystLast) * 1.2f, $"{H(catalystLast):0.0} → {H(catalystFirst):0.0}");
        }

        private static void Direction()
        {
            Section("かき混ぜる向き");
            LiquidRecord cw = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); f.AddSolid(d.sunPetal, 1f); Stir(f, 14f, true, 6f); f.Tick(4f); });
            LiquidRecord ccw = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); f.AddSolid(d.sunPetal, 1f); Stir(f, 14f, false, 6f); f.Tick(4f); });
            Show("時計回り", cw);
            Show("反時計回り", ccw);
            float cwShare = cw.Attributes.GetShare(PotionAttribute.Healing), ccwShare = ccw.Attributes.GetShare(PotionAttribute.Healing);
            Check("時計回りは一番強い属性を、反時計回りは2番目の属性を伸ばす", cwShare > ccwShare + 0.04f, $"回復の割合 時計 {cwShare:P0} ／ 反時計 {ccwShare:P0}");
            LiquidRecord foamy = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 70f, true, 20f); f.Tick(2f); });
            Check("混ぜすぎると泡立つ", foamy.TraitStrength(LiquidTraits.Foamy) > 0.5f, foamy.autoName);
        }

        private static void MultiStage()
        {
            Section("多段調合（瓶詰めした液体を混ぜる）");
            LiquidRecord a = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(3f); });
            LiquidRecord half = Make(f => { f.PourLiquid(a, 50f); f.Pour(d.water, 50f); f.Tick(1f); });
            Show("A：水＋薬草", a);
            Show("A 50ml ＋ 水 50ml", half);
            float ratio = H(half) / Mathf.Max(0.01f, H(a));
            Check("液体どうしを混ぜると濃さは平均になる", ratio > 0.47f && ratio < 0.53f, $"比 {ratio:0.00}");
            Check("混ぜた液体が「元になった液体」として残る", half.parentLiquidIds.Contains(a.id), string.Join(",", half.parentLiquidIds));
            Check("混ぜた液体の中の素材も「使った素材」に入る", half.UsesIngredient(d.herb.IngredientId) && half.UsesIngredient(d.water.IngredientId), string.Join(",", half.ingredientIds));
            LiquidRecord little = Make(f => { f.PourLiquid(a, 5f); f.Tick(0.5f); });
            Check("瓶詰めした液体を少し（5ml）だけ注いで詰めても、同じ液体", little.id == a.id, $"{little.autoName} 回復 {H(little):0.0} ／ 元 {H(a):0.0}");
            LiquidRecord mixed = Make(f => { f.PourLiquid(a, 100f); f.AddSolid(d.sunPetal, 1f); Stir(f, 8f, false, 4f); f.Tick(3f); });
            Show("A に陽光の花びらを足して反時計回り", mixed);
            Check("瓶詰めした液体に素材を足して、別の液体にできる", mixed.id != a.id && mixed.Attributes[PotionAttribute.Light] > 5f, mixed.autoName);
        }

        private static void ReactionTest()
        {
            Section("反応");
            LiquidRecord r = Make(f =>
            {
                f.Pour(d.water, 100f); f.AddSolid(d.moonDust, 1f); f.AddSolid(d.mistFlower, 2f);
                Stir(f, 8f, false, 4f); f.Tick(4f);
            });
            Show("霧隠れの花＋月影の粉を冷たいまま反時計回り", r);
            Check("反応「霧と月の共鳴」が起きる", r.reactionIds.Contains(d.mistMoon.ReactionId), string.Join(",", r.reactionIds));
            Check("ゆらめきの特性が付く", r.TraitStrength(LiquidTraits.Shimmer) > 0.5f, r.ToDebugString(d.settings));
            Check("特別なポーション「透明化の秘薬」になる", r.specialPotionId == d.invisibility.PotionId, r.autoName);
            LiquidRecord noReaction = Make(f =>
            {
                f.Pour(d.water, 100f); f.AddSolid(d.moonDust, 1f); f.AddSolid(d.mistFlower, 2f);
                Stir(f, 8f, true, 4f); f.Tick(4f);
            });
            Check("時計回りでは反応しない", !noReaction.reactionIds.Contains(d.mistMoon.ReactionId), noReaction.autoName);
            LiquidRecord syrup = Make(f => { f.Pour(d.honey, 150f); f.SetHeat(1f); f.Tick(25f); });
            Show("花の蜜を煮立てる", syrup);
            Check("反応「蜜のとろみ」が起きる", syrup.reactionIds.Contains(d.honeySyrup.ReactionId), syrup.autoName);
        }

        private static void Naming()
        {
            Section("名前と分類");
            LiquidRecord water = Make(f => f.Pour(d.water, 100f));
            Show("水だけ", water);
            Check("液体1種類だけなら、その名前（未知の液体）", water.autoName == d.water.DisplayName && water.kind == LiquidKind.Unknown, water.autoName);
            LiquidRecord weak = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); f.Tick(10f); });
            Show("水＋薬草2つ（混ぜずに10秒）", weak);
            Check("効き目が弱いと中間液体「薬草入りの清らかな水」", weak.kind == LiquidKind.Intermediate && weak.autoName.Contains("薬草入り"), weak.autoName);
            LiquidRecord two = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 1f); f.AddSolid(d.galeFeather, 1f); f.Tick(6f); });
            Show("水＋薬草＋疾風の羽根", two);
            LiquidRecord mix = Make(f => { f.Pour(d.water, 100f); f.Pour(d.oil, 100f); });
            Show("水＋灯り油", mix);
            Check("液体を2種類混ぜた名前", mix.autoName.Contains("と") , mix.autoName);
            LiquidRecord cancel = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.fireGrass, 1f); f.AddSolid(d.frostMint, 1f); Stir(f, 10f, true, 5f); f.Tick(3f); });
            Show("火炎草＋氷晶ミント（打ち消し合う）", cancel);
        }

        private static void Identity()
        {
            Section("同じ液体の判定");
            Func<LiquidRecord> recipe = () => Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 10f, true, 5f); f.Tick(3f); });
            LiquidRecord a = recipe(), b = recipe();
            Check("同じ作り方なら同じ液体", a.id == b.id, a.id);
            LiquidRecord c = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 4f); Stir(f, 12f, true, 5f); f.Tick(3f); });
            Check("素材の量が倍ならちがう液体", c.id != a.id, $"{a.autoName} / {c.autoName}");
            var registry = new LiquidRegistry();
            registry.Register(a, out bool new1);
            registry.Register(b, out bool new2);
            Check("一覧には1つだけ登録され、作った回数が増える", new1 && !new2 && registry.Count == 1 && registry.Get(a.id).madeCount == 2, $"件数 {registry.Count}");
        }

        /// <summary>Step5：素材の新しい性質（溶ける温度・崩れる・香りが飛ぶ・冷える・溶ける向き）と、新しい反応</summary>
        private static void Step5Traits()
        {
            Section("素材の性質（溶ける温度・崩れる・香りが飛ぶ・冷える・溶ける向き）");

            FlaskSimulation cold = New();
            cold.Pour(d.water, 100f); cold.AddSolid(d.amberResin, 1f); Stir(cold, 10f, true, 5f); cold.Tick(5f);
            FlaskSimulation warm = New();
            warm.Pour(d.water, 100f); warm.SetHeat(0.6f); warm.Tick(40f); warm.AddSolid(d.amberResin, 1f); Stir(warm, 10f, true, 5f); warm.Tick(2f);
            Check("琥珀樹脂は 65℃ にならないと、かき混ぜても溶けない", cold.DissolvedFraction < 0.01f, $"溶けた割合 {cold.DissolvedFraction:P0}（{cold.Temperature:0}℃）");
            Check("温めると琥珀樹脂が溶ける", warm.DissolvedFraction > 0.5f, $"溶けた割合 {warm.DissolvedFraction:P0}（{warm.Temperature:0}℃）");

            LiquidRecord gentle = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.starPetal, 2f); Stir(f, 2.5f, true, 3f); f.Tick(6f); });
            LiquidRecord rough = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.starPetal, 2f); Stir(f, 12f, true, 6f); f.Tick(3f); });
            float lg = gentle.Attributes[PotionAttribute.Light], lr = rough.Attributes[PotionAttribute.Light];
            Check("星の花びらは、そっと混ぜると光が強く、混ぜすぎると崩れて弱まる", lg > lr * 1.5f, $"そっと {lg:0.0} ／ 混ぜすぎ {lr:0.0}");

            FlaskSimulation calm = New();
            calm.Pour(d.water, 150f); calm.AddSolid(d.dreamLavender, 2f); Stir(calm, 6f, true, 4f); calm.SetHeat(0.5f); calm.Tick(30f);
            FlaskSimulation boiled = New();
            boiled.Pour(d.water, 150f); boiled.AddSolid(d.dreamLavender, 2f); Stir(boiled, 6f, true, 4f); boiled.SetHeat(1f); boiled.Tick(30f);
            float ic = Total(calm, PotionAttribute.Invisibility), ib = Total(boiled, PotionAttribute.Invisibility);
            Check("夢見ラベンダーは、沸騰させると香り（力）が飛ぶ", ic > ib * 3f, $"沸騰させない {ic:0.0} ／ 沸騰させた {ib:0.0}（量で掛けた合計）");

            FlaskSimulation chill = New();
            chill.Pour(d.water, 100f); chill.SetHeat(0.8f); chill.Tick(40f); chill.SetHeat(0f);
            float before = chill.Temperature;
            chill.AddSolid(d.frostStone, 1f);
            float after = chill.Temperature;
            Check("氷霜石を入れると、フラスコが一気に冷える", before - after > 20f, $"{before:0}℃ → {after:0}℃");

            FlaskSimulation cw = New();
            cw.Pour(d.water, 100f); cw.AddSolid(d.spiralShell, 1f); Stir(cw, 10f, true, 5f);
            FlaskSimulation ccw = New();
            ccw.Pour(d.water, 100f); ccw.AddSolid(d.spiralShell, 1f); Stir(ccw, 10f, false, 5f);
            Check("渦巻き貝は、時計回りではほとんど溶けない", cw.DissolvedFraction < 0.5f, $"時計回り {cw.DissolvedFraction:P0}");
            Check("渦巻き貝は、反時計回りに混ぜると溶ける", ccw.DissolvedFraction > cw.DissolvedFraction + 0.3f, $"反時計回り {ccw.DissolvedFraction:P0}");

            FlaskSimulation ash = New();
            ash.Pour(d.water, 100f); ash.AddSolid(d.volcanoAsh, 1f); ash.Tick(10f);
            FlaskSimulation seed = New();
            seed.Pour(d.water, 100f); seed.AddSolid(d.sproutSeed, 1f); seed.Tick(10f);
            Check("火山の灰は、混ぜなくてもすぐ溶ける（芽吹きの種よりずっと速い）", ash.DissolvedFraction > 0.7f && seed.DissolvedFraction < 0.45f,
                  $"灰 {ash.DissolvedFraction:P0} ／ 種 {seed.DissolvedFraction:P0}（10秒、混ぜない）");

            LiquidRecord peace = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.frostStone, 1f); f.AddSolid(d.fireGrass, 1f); Stir(f, 8f, true, 4f); f.Tick(4f); });
            Show("氷霜石で冷やして火炎草を溶かす", peace);
            Check("反応「霜と炎の和解」が起きる", peace.reactionIds.Contains(d.frostFire.ReactionId), string.Join(",", peace.reactionIds));
            LiquidRecord lamp = Make(f => { f.Pour(d.water, 100f); f.SetHeat(0.6f); f.Tick(40f); f.AddSolid(d.amberResin, 1f); f.AddSolid(d.sunPetal, 1f); Stir(f, 10f, true, 6f); f.Tick(4f); });
            Show("温かいまま琥珀樹脂と陽光の花びらを時計回り", lamp);
            Check("反応「琥珀の灯」が起きる", lamp.reactionIds.Contains(d.amberSun.ReactionId), string.Join(",", lamp.reactionIds));
            LiquidRecord star = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.spiralShell, 1f); Stir(f, 10f, false, 5f); f.AddSolid(d.starPetal, 1f); Stir(f, 2.5f, false, 3f); f.Tick(4f); });
            Show("渦巻き貝を溶かしてから、星の花びらをそっと反時計回り", star);
            Check("反応「流れ星の渦」が起きる", star.reactionIds.Contains(d.starSpiral.ReactionId), string.Join(",", star.reactionIds));

            Section("追加の素材（森の蜜蝋・ぜんまい草・泡雪の粉・月下の夜露）");
            // 火加減 0.4 は 64℃ に向かう：蜜蝋（45℃）は溶けて、琥珀樹脂（65℃）は溶けない温度
            FlaskSimulation tepidWax = HeatTo(56f, 0.4f); tepidWax.AddSolid(d.beesWax, 1f); Stir(tepidWax, 10f, true, 5f);
            FlaskSimulation tepidAmber = HeatTo(56f, 0.4f); tepidAmber.AddSolid(d.amberResin, 1f); Stir(tepidAmber, 10f, true, 5f);
            FlaskSimulation coldWax = New(); coldWax.Pour(d.water, 100f); coldWax.AddSolid(d.beesWax, 1f); Stir(coldWax, 10f, true, 5f);
            Check("森の蜜蝋は、冷たいままだと溶けない", coldWax.DissolvedFraction < 0.01f, $"溶けた割合 {coldWax.DissolvedFraction:P0}（{coldWax.Temperature:0}℃）");
            Check("森の蜜蝋は、ぬるめ（45℃以上）で溶ける。同じ温度で琥珀樹脂は溶けない", tepidWax.DissolvedFraction > 0.5f && tepidAmber.DissolvedFraction < 0.01f,
                  $"蜜蝋 {tepidWax.DissolvedFraction:P0} ／ 琥珀樹脂 {tepidAmber.DissolvedFraction:P0}（{tepidWax.Temperature:0}℃）");

            FlaskSimulation fernCw = New();
            fernCw.Pour(d.water, 100f); fernCw.AddSolid(d.fiddlehead, 1f); Stir(fernCw, 10f, true, 5f);
            FlaskSimulation fernCcw = New();
            fernCcw.Pour(d.water, 100f); fernCcw.AddSolid(d.fiddlehead, 1f); Stir(fernCcw, 10f, false, 5f);
            Check("ぜんまい草は、時計回りに混ぜると溶けて、反時計回りではほとんど溶けない（渦巻き貝と逆）",
                  fernCw.DissolvedFraction > fernCcw.DissolvedFraction + 0.3f && fernCcw.DissolvedFraction < 0.5f,
                  $"時計回り {fernCw.DissolvedFraction:P0} ／ 反時計回り {fernCcw.DissolvedFraction:P0}");

            FlaskSimulation fizz = New();
            fizz.Pour(d.water, 100f); fizz.AddSolid(d.fizzPowder, 1f); fizz.Tick(10f);
            Check("泡雪の粉は、混ぜなくてもすぐ溶ける", fizz.DissolvedFraction > 0.7f, $"{fizz.DissolvedFraction:P0}（10秒、混ぜない）");
            FlaskSimulation fizzCalm = New();
            fizzCalm.Pour(d.water, 150f); fizzCalm.AddSolid(d.fizzPowder, 2f); fizzCalm.Tick(6f); fizzCalm.SetHeat(0.5f); fizzCalm.Tick(30f);
            FlaskSimulation fizzBoil = New();
            fizzBoil.Pour(d.water, 150f); fizzBoil.AddSolid(d.fizzPowder, 2f); fizzBoil.Tick(6f); fizzBoil.SetHeat(1f); fizzBoil.Tick(30f);
            float wc = Total(fizzCalm, PotionAttribute.Wind), wb = Total(fizzBoil, PotionAttribute.Wind);
            Check("泡雪の粉は、沸騰させると泡（風の力）が抜ける", wc > wb * 2f, $"沸騰させない {wc:0.0} ／ 沸騰させた {wb:0.0}");

            FlaskSimulation dewCalm = New();
            dewCalm.Pour(d.nightDew, 150f); dewCalm.SetHeat(0.5f); dewCalm.Tick(30f);
            FlaskSimulation dewBoil = New();
            dewBoil.Pour(d.nightDew, 150f); dewBoil.SetHeat(1f); dewBoil.Tick(30f);
            float vc = Total(dewCalm, PotionAttribute.Invisibility), vb = Total(dewBoil, PotionAttribute.Invisibility);
            Check("月下の夜露（ベース液体）も、煮立てると力が飛ぶ", vc > vb * 2f, $"沸騰させない {vc:0.0} ／ 沸騰させた {vb:0.0}");

            LiquidRecord cradle = Make(f =>
            {
                f.Pour(d.water, 100f); f.SetHeat(0.6f);
                for (int i = 0; i < 2000 && f.Temperature < 55f; i++) f.Tick(FlaskSimulation.StepSeconds);
                f.SetHeat(0.4f);
                f.AddSolid(d.beesWax, 1f); f.AddSolid(d.fiddlehead, 1f); Stir(f, 10f, true, 6f); f.Tick(4f);
            });
            Show("ぬるいまま森の蜜蝋とぜんまい草を時計回り", cradle);
            Check("反応「蜜蝋のゆりかご」が起きる", cradle.reactionIds.Contains(d.waxCradle.ReactionId), string.Join(",", cradle.reactionIds));
        }

        /// <summary>水 100ml を火加減 0.6 で temp℃ まで温め、火加減を hold にしたフラスコ</summary>
        private static FlaskSimulation HeatTo(float temp, float hold)
        {
            FlaskSimulation f = New();
            f.Pour(d.water, 100f);
            f.SetHeat(0.6f);
            for (int i = 0; i < 2000 && f.Temperature < temp; i++) f.Tick(FlaskSimulation.StepSeconds);
            f.SetHeat(hold);
            return f;
        }

        /// <summary>属性の合計（100ml あたりの濃さ × 量）。煮詰めて濃くなった分を差し引いて比べる</summary>
        private static float Total(FlaskSimulation f, PotionAttribute a)
        {
            LiquidState s = f.Snapshot();
            return s.concentration[a] * s.volumeMl / FlaskSimulation.MlPerUnit;
        }

        private static void Determinism()
        {
            Section("時間の進め方で結果が変わらない");
            FlaskSimulation x = New(), y = New();
            foreach (FlaskSimulation f in new[] { x, y })
            {
                f.Pour(d.water, 120f);
                f.AddSolid(d.herb, 2f);
                f.AddSolid(d.fireGrass, 1f);
                f.SetHeat(0.7f);
            }
            x.Tick(12f);
            for (int i = 0; i < 120; i++) y.Tick(0.1f);
            LiquidRecord rx = Bottle(x), ry = Bottle(y);
            float diff = 0f;
            for (int i = 0; i < rx.attributes.Length; i++) diff += Mathf.Abs(rx.attributes[i] - ry.attributes[i]);
            Check("12秒まとめて進めても、0.1秒ずつ進めても同じ", diff < 0.01f && rx.id == ry.id, $"差 {diff:0.0000}");
        }

        private static void PartialAndResidue()
        {
            Section("少しずつ瓶詰め・溶け残り");
            FlaskSimulation f = New();
            f.Pour(d.water, 300f); f.AddSolid(d.herb, 6f); Stir(f, 12f, true, 5f); f.Tick(3f);
            LiquidRecord first = analyzer.Analyze(f.Bottle(100f, out float ml1));
            Check("100ml だけ瓶詰めすると、残りはフラスコに残る", Mathf.Abs(f.VolumeMl - (330f - 100f)) < 1f, $"{f.VolumeMl:0}ml");
            LiquidRecord second = analyzer.Analyze(f.Bottle(100f, out _));
            Check("残りを瓶詰めしても同じ液体", first.id == second.id, $"{first.autoName}");

            FlaskSimulation g = New();
            g.Pour(d.water, 60f); g.AddSolid(d.sproutSeed, 3f); g.Tick(1f);
            g.Bottle(1000f, out _);
            bool residue = !g.IsEmpty && g.Log.Count > 0 && g.Log[0].type == ProcessStepType.Residue;
            Check("溶け残った固体はフラスコに残り、次の調合に持ち越される", residue, g.Log.Count > 0 ? g.Log[0].Describe() : "なし");
            g.Tick(10f);
            LiquidState dry = g.Bottle(100f, out float dryMl);
            Check("溶け残りだけのときは液体がない（溶けない・瓶に詰められない）", !g.HasLiquid && dry == null && g.LiquidMl < 0.01f,
                  $"液体 {g.LiquidMl:0.0}ml ／ 全体 {g.VolumeMl:0.0}ml");
            g.Pour(d.water, 100f); Stir(g, 10f, true, 5f); g.Tick(3f);
            LiquidRecord next = Bottle(g);
            Check("水を注ぐと、溶け残りが溶けて次の液体に入る", next.UsesIngredient(d.sproutSeed.IngredientId) && next.TotalPotency > 1f, $"{next.autoName}");
        }

        /// <summary>調合机の素材（届いた素材は使い放題）と、瓶詰めした液体（ml）</summary>
        private static void Inventories()
        {
            Section("調合机の素材と瓶");
            var go = new GameObject("InventoryTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                IngredientInventory stock = go.AddComponent<IngredientInventory>();
                int arrivedEvents = 0;
                stock.Arrived += _ => arrivedEvents++;
                stock.Unlock(d.water, false);
                Check("机にある素材は使い放題", float.IsInfinity(stock.Get(d.water)) && Mathf.Approximately(stock.Consume(d.water, 50f), 50f) && stock.Has(d.water),
                      $"水 {IngredientInventory.Format(d.water, stock.Get(d.water))}");
                Check("まだ届いていない素材は使えない", !stock.Has(d.mistFlower) && stock.Consume(d.mistFlower, 1f) <= 0f, d.mistFlower.DisplayName);
                bool first = stock.Unlock(d.mistFlower);
                bool second = stock.Unlock(d.mistFlower);
                Check("素材が届くと使い放題になり、届いたお知らせは1回だけ", first && !second && arrivedEvents == 1 && stock.Has(d.mistFlower),
                      $"お知らせ {arrivedEvents} 回");
                stock.Unlock(d.nightshade, false);
                Check("つづきから始めたとき（お知らせなし）は、静かに机に並ぶ", stock.Has(d.nightshade) && arrivedEvents == 1, $"お知らせ {arrivedEvents} 回");

                // 版6までのセーブ：素材の量 → 量があった素材は、机にある素材として使える（お金・仕入れはもうない）
                var legacy = new List<IngredientStockEntry>
                {
                    new IngredientStockEntry { id = d.herb.IngredientId, amount = 6f },
                    new IngredientStockEntry { id = d.sunStone.IngredientId, amount = 2f },
                    new IngredientStockEntry { id = d.galeFeather.IngredientId, amount = 0f },
                };
                stock.Clear();
                stock.Import(null, legacy);
                Check("古いセーブの素材（量）は、机にある素材としてそのまま使える", stock.Has(d.herb) && stock.Has(d.sunStone) && !stock.Has(d.galeFeather) && !stock.Has(d.water),
                      string.Join(",", stock.ExportUnlocked()));
                List<string> unlocked = stock.ExportUnlocked();
                stock.Clear();
                stock.Import(unlocked);
                Check("机にある素材はセーブに残る", stock.Has(d.herb) && stock.Has(d.sunStone) && stock.UnlockedCount == unlocked.Count, string.Join(",", unlocked));

                // 瓶の棚（ml）
                PotionInventory shelf = go.AddComponent<PotionInventory>();
                LiquidRecord healing = Make(f => { f.Pour(d.water, 100f); f.AddSolid(d.herb, 2f); Stir(f, 8f, true, 4f); f.Tick(4f); });
                shelf.AddBottled(healing, 100f);
                PotionStack stack = shelf.AddBottled(healing, 18f);
                Check("同じ液体はまとめて量で数える（満タン1本＋開けた瓶）", shelf.Stacks.Count == 1 && Mathf.Approximately(stack.ml, 118f) && stack.FullBottles == 1 && stack.Bottles == 2 && Mathf.Approximately(stack.OpenedMl, 18f),
                      stack.DescribeAmount());
                PotionStack full = shelf.TakeOne(stack);
                PotionStack opened = shelf.TakeOne(stack);
                Check("取り出すときは満タンの瓶から。最後は開けた瓶", full != null && full.IsFullBottle && opened != null && !opened.IsFullBottle && Mathf.Approximately(opened.ml, 18f) && shelf.Stacks.Count == 0,
                      $"{(full != null ? full.ml : 0f):0}ml → {(opened != null ? opened.ml : 0f):0}ml");
                opened.ml = 8f; // 10ml 注いだ
                shelf.ReturnOne(opened);
                shelf.ReturnOne(full);
                PotionStack back = shelf.Find(healing.id);
                Check("減った瓶は減った量のまま棚に戻る", back != null && Mathf.Approximately(back.ml, 108f), back != null ? back.DescribeAmount() : "なし");

                // 版3までのセーブ（本数だけ）・同じ液体が2つに分かれていた
                var old = new List<PotionStack>
                {
                    new PotionStack { potionId = healing.id, liquidId = healing.id, potionName = healing.DisplayName, count = 2 },
                    new PotionStack { potionId = healing.id, liquidId = healing.id, potionName = healing.DisplayName, count = 1 },
                };
                shelf.Import(old);
                Check("古いセーブの瓶（本数）は ml に直してまとめる", shelf.Stacks.Count == 1 && Mathf.Approximately(shelf.Stacks[0].ml, 300f), shelf.Stacks.Count > 0 ? shelf.Stacks[0].DescribeAmount() : "なし");

                // フェーズ4までのポーション（液体の記録がない）
                var registry = new LiquidRegistry();
                shelf.Import(new List<PotionStack>
                {
                    new PotionStack { potionId = "potion_healing", potionName = "回復ポーション", count = 1, attributes = (float[])healing.attributes.Clone(), stars = 2, color = healing.color },
                });
                shelf.EnsureRecords(registry);
                PotionStack migrated = shelf.Stacks[0];
                LiquidRecord made = registry.Get(migrated.liquidId);
                Check("フェーズ4までのポーションにも液体の記録を作る（注ぎ戻せる）", made != null && migrated.potionId == migrated.liquidId && made.DisplayName == "回復ポーション",
                      migrated.liquidId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void SaveLoad()
        {
            Section("セーブと読み込み");
            FlaskSimulation a = New();
            a.Pour(d.water, 100f); a.AddSolid(d.herb, 2f); a.AddSolid(d.amplifyCrystal, 1f); Stir(a, 5f, false, 3f); a.SetHeat(0.5f); a.Tick(4f);
            string json = JsonUtility.ToJson(a.Export());
            FlaskSimulation b = New();
            b.Import(JsonUtility.FromJson<FlaskSaveData>(json), FindIngredient);
            a.Tick(8f);
            b.Tick(8f);
            LiquidRecord ra = Bottle(a), rb = Bottle(b);
            float diff = 0f;
            for (int i = 0; i < ra.attributes.Length; i++) diff += Mathf.Abs(ra.attributes[i] - rb.attributes[i]);
            Check("途中で保存して読み込んでも、続きが同じになる", diff < 0.01f && ra.id == rb.id, $"差 {diff:0.0000}");
            Check("工程の記録も残る", ra.recipes.Count > 0 && rb.recipes.Count > 0 && ra.recipes[0].steps.Count == rb.recipes[0].steps.Count,
                  ra.recipes.Count > 0 ? string.Join(" → ", ra.recipes[0].steps.ConvertAll(s => s.Describe())) : "");
        }

        /// <summary>発見のポイントで、素材が調合机に届く（届く時期の表と、はじめから机にある素材）</summary>
        private static void Arrivals()
        {
            Section("発見で素材が調合机に届く");
            var db = AssetDatabase.LoadAssetAtPath<IngredientDatabase>("Assets/GameData/Alchemy/IngredientDatabase.asset");
            var balance = AssetDatabase.LoadAssetAtPath<GameBalance>("Assets/GameData/GameBalance.asset");
            if (db == null || balance == null)
            {
                Check("素材の一覧と GameBalance がある", false, "Assets/GameData/Alchemy/IngredientDatabase.asset ／ Assets/GameData/GameBalance.asset");
                return;
            }
            var go = new GameObject("ArrivalTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                IngredientInventory stock = go.AddComponent<IngredientInventory>();
                stock.ResetToStarter(balance);
                int starters = stock.UnlockedCount;
                var late = new List<string>();
                int maxNeed = 0;
                foreach (IngredientData x in db.All)
                {
                    if (x == null || stock.IsUnlocked(x)) continue;
                    if (x.DiscoveriesToUnlock <= 0) late.Add(x.DisplayName);
                    maxNeed = Mathf.Max(maxNeed, x.DiscoveriesToUnlock);
                }
                Check("はじめから机にある素材がある", starters >= 8, $"{starters} 種類");
                Check("はじめにない素材には、届くのに必要な発見が決まっている", late.Count == 0, late.Count > 0 ? string.Join("・", late) : $"最後の素材は 発見 {maxNeed}");

                List<IngredientData> none = DiscoveryProgress.Deliver(db, stock, 0, false);
                List<IngredientData> two = DiscoveryProgress.Deliver(db, stock, 2, false);
                IngredientData next = DiscoveryProgress.NextArrival(db, stock, 2, out int remaining);
                Check("発見 0 では何も届かない", none.Count == 0, $"{none.Count} 種類");
                Check("発見 2 で火山の灰が届く（泡雪の粉はまだ）", two.Contains(d.volcanoAsh) && !stock.IsUnlocked(d.fizzPowder),
                      string.Join("・", two.ConvertAll(x => x.DisplayName)));
                Check("次に届く素材と、残りの発見がわかる", next == d.fizzPowder && remaining == 1, next != null ? $"{next.DisplayName}（あと {remaining}）" : "なし");
                List<IngredientData> all = DiscoveryProgress.Deliver(db, stock, maxNeed, false);
                Check("発見を重ねると、すべての素材が机にそろう", stock.CountKinds(db) == db.Count && DiscoveryProgress.NextArrival(db, stock, maxNeed, out _) == null,
                      $"{stock.CountKinds(db)} / {db.Count}");

                // 性質と反応だけでも（新しい液体を数えなくても）、いちばん遅い素材まで届く
                int facts = 0;
                foreach (IngredientData x in db.All) if (x != null) facts += DiscoveryJournal.TotalCount(x);
                var reactions = AssetDatabase.FindAssets("t:ReactionData");
                int reachable = facts * DiscoveryProgress.PointsPerFact + reactions.Length * DiscoveryProgress.PointsPerReaction;
                Check("性質と反応を見つけるだけでも、いちばん遅い素材まで届く", reachable >= maxNeed, $"性質 {facts}・反応 {reactions.Length} → 発見 {reachable}（必要 {maxNeed}）");

                // ポイントの数え方（性質 1）
                DiscoveryJournal.Clear();
                int p0 = DiscoveryProgress.Points(db, null);
                IngredientFact fact = IngredientFact.None;
                foreach (IngredientFact f in DiscoveryJournal.Order)
                {
                    if (f != IngredientFact.Used && (DiscoveryJournal.FactsOf(d.herb) & f) != 0) { fact = f; break; }
                }
                DiscoveryJournal.Learn(d.herb, IngredientFact.Used);
                int pUsed = DiscoveryProgress.Points(db, null);
                if (fact != IngredientFact.None) DiscoveryJournal.Learn(d.herb, fact);
                int p1 = DiscoveryProgress.Points(db, null);
                Check("性質を1つ見つけると発見 +1（使っただけでは増えない）", p0 == 0 && pUsed == 0 && p1 == DiscoveryProgress.PointsPerFact, $"{p0} → {pUsed} → {p1}");
                DiscoveryJournal.Clear();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DiscoveryJournal.Clear();
            }
        }

        /// <summary>伝説の特効薬が、届く素材だけで作れる。暦が正しく進む</summary>
        private static void FinalPotion()
        {
            Section("伝説の特効薬（作ってもゲームは終わらない。特別な薬の1つ）");
            LiquidRecord r = Make(f => { f.Pour(d.spiritWater, 100f); f.AddSolid(d.amplifyCrystal, 1f); Stir(f, 15f, true, 6f); f.Tick(10f); f.AddSolid(d.herb, 4f); f.AddSolid(d.sunPetal, 1f); Stir(f, 14f, true, 6f); f.Tick(4f); });
            Show("お手本", r);
            Check("お手本の作り方で、伝説の特効薬になる", r.specialPotionId == "special_legendary_cure", r.ToDebugString(d.settings));
            int need = 0;
            foreach (IngredientData x in new[] { d.spiritWater, d.amplifyCrystal, d.herb, d.sunPetal }) need = Mathf.Max(need, x.DiscoveriesToUnlock);
            Check("使う素材は、早めに机にそろう", need <= 6, $"いちばん遅い素材まで 発見 {need}");

            Section("暦（日付のアイキャッチ）");
            GameCalendar.MonthDay(1, 4, 1, out int m1, out int d1);
            GameCalendar.MonthDay(31, 4, 1, out int m2, out int d2);
            GameCalendar.MonthDay(275, 4, 1, out int m3, out int d3);
            GameCalendar.MonthDay(366, 4, 1, out int m4, out int d4);
            Check("1日目は、はじまりの日", m1 == 4 && d1 == 1, $"{m1}月{d1}日");
            Check("月をまたいで進む（31日目は5月1日）", m2 == 5 && d2 == 1, $"{m2}月{d2}日");
            Check("年をまたいで進む（275日目は12月31日）", m3 == 12 && d3 == 31, $"{m3}月{d3}日");
            Check("1年たつと、同じ月日に戻る", m4 == 4 && d4 == 1, $"{m4}月{d4}日");
        }

        // ------------------------------------------------------------------

        private static FlaskSimulation New()
        {
            return new FlaskSimulation(d.settings, d.Reactions, 600f);
        }

        private static LiquidRecord Make(Action<FlaskSimulation> recipe)
        {
            FlaskSimulation f = New();
            recipe(f);
            return Bottle(f);
        }

        private static LiquidRecord Bottle(FlaskSimulation f)
        {
            LiquidState state = f.Bottle(f.VolumeMl, out _);
            return state != null ? analyzer.Analyze(state) : new LiquidRecord { autoName = "（空）" };
        }

        /// <summary>手でかき混ぜるように、seconds 秒かけて turns 回まわす</summary>
        private static void Stir(FlaskSimulation f, float turns, bool clockwise, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / FlaskSimulation.StepSeconds));
            for (int i = 0; i < steps; i++)
            {
                f.Stir(turns / steps, clockwise);
                f.Tick(FlaskSimulation.StepSeconds);
            }
        }

        private static IngredientData FindIngredient(string id)
        {
            foreach (IngredientData i in d.AllIngredients)
            {
                if (i.IngredientId == id) return i;
            }
            return null;
        }

        private static float H(LiquidRecord r)
        {
            return r.Attributes[PotionAttribute.Healing];
        }

        private static void Section(string title)
        {
            report.Append("\n■ ").AppendLine(title);
        }

        private static void Show(string label, LiquidRecord r)
        {
            report.Append("   ・").Append(label).Append("： ").AppendLine(r.ToDebugString(d.settings));
        }

        private static void Check(string label, bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            report.Append(ok ? "  ○ " : "  × ").Append(label).Append("　").AppendLine(detail);
        }

        private static void Line(string text)
        {
            report.AppendLine(text);
        }
    }
}
