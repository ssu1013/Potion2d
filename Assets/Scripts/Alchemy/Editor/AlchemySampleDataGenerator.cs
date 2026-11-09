using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Alchemy.EditorTools
{
    /// <summary>
    /// テスト用のサンプルデータ（設定・素材・完成条件・反応）を一括で作るメニュー（2D 版ではシーンには何も置かない）。
    /// メニュー: Tools > Potion Frontier > サンプル調合データを生成
    /// すでにある素材・完成条件は上書きしない（自分で調整した値が消えないように）。足りない物だけ作る。
    /// </summary>
    public static class AlchemySampleDataGenerator
    {
        private const string InvisibilityDescription = "飲むと数分間、姿がほとんど見えなくなる。人目につかずに何かしたい夜の、頼みの綱。";

        private const string RootDir = "Assets/GameData/Alchemy";
        private const string IngredientDir = RootDir + "/Ingredients";
        private const string ConditionDir = RootDir + "/PotionConditions";
        private const string ReactionDir = RootDir + "/Reactions";

        /// <summary>サンプルデータ一式（シーン構築ツールからも使う）</summary>
        public sealed class SampleData
        {
            public AlchemySettings settings;
            public IngredientData water;
            public IngredientData oil;
            public IngredientData honey;
            public IngredientData spiritWater;
            public IngredientData herb;
            public IngredientData fireGrass;
            public IngredientData frostMint;
            public IngredientData sproutSeed;
            public IngredientData mistFlower;
            public IngredientData nightshade;
            public IngredientData sunPetal;
            public IngredientData galeFeather;
            public IngredientData amplifyCrystal;
            public IngredientData sunStone;
            public IngredientData moonDust;
            public PotionConditionData invisibility;
            public PotionConditionData growthBooster;
            public PotionConditionData legendary;
            public ReactionData mistMoon;
            public ReactionData honeySyrup;
            // Step5：新しい性質の素材と反応
            public IngredientData amberResin;
            public IngredientData starPetal;
            public IngredientData dreamLavender;
            public IngredientData frostStone;
            public IngredientData spiralShell;
            public IngredientData volcanoAsh;
            public IngredientData beesWax;
            public IngredientData fiddlehead;
            public IngredientData fizzPowder;
            public IngredientData nightDew;
            public ReactionData frostFire;
            public ReactionData amberSun;
            public ReactionData starSpiral;
            public ReactionData waxCradle;

            public IngredientData[] BaseLiquids => new[] { water, oil, honey, spiritWater };
            public IngredientData[] MainMaterials => new[] { herb, fireGrass, frostMint, sproutSeed, mistFlower, nightshade, sunPetal, galeFeather };
            public IngredientData[] Catalysts => new[] { amplifyCrystal, sunStone, moonDust };
            public PotionConditionData[] Recipes => new[] { invisibility, growthBooster, legendary };
            public ReactionData[] Reactions => new[] { mistMoon, honeySyrup, frostFire, amberSun, starSpiral, waxCradle };
            public IngredientData[] NewIngredients => new[] { amberResin, starPetal, dreamLavender, frostStone, spiralShell, volcanoAsh,
                                                              beesWax, fiddlehead, fizzPowder, nightDew };
            public IngredientData[] AllIngredients => new[] { water, oil, honey, spiritWater, herb, fireGrass, frostMint, sproutSeed, mistFlower, nightshade, sunPetal, galeFeather, amplifyCrystal, sunStone, moonDust,
                                                             amberResin, starPetal, dreamLavender, frostStone, spiralShell, volcanoAsh,
                                                             beesWax, fiddlehead, fizzPowder, nightDew };
        }

        /// <summary>設定・素材・完成条件のアセットを、なければ作って返す（既にあれば上書きしない）</summary>
        public static SampleData CreateOrUpdateAssets()
        {
            EnsureFolder("Assets", "GameData");
            EnsureFolder("Assets/GameData", "Alchemy");
            EnsureFolder(RootDir, "Ingredients");
            EnsureFolder(RootDir, "PotionConditions");
            EnsureFolder(RootDir, "Reactions");

            // ---- 調合ルール設定（既にあれば調整済みの値を尊重して上書きしない）----
            string settingsPath = RootDir + "/AlchemySettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<AlchemySettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AlchemySettings>();
                settings.ResetToDefaults();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            // ---- ベース液体 ----
            var water = Ingredient("Base_Water", "base_water", "清らかな水", IngredientCategory.BaseLiquid,
                new Color(0.75f, 0.88f, 1.00f, 0.85f), "ごく普通の湧き水。どんな素材ともなじむ。",
                new AttributeValue[0], form: "ポーション");
            var oil = Ingredient("Base_LampOil", "base_lamp_oil", "灯り油", IngredientCategory.BaseLiquid,
                new Color(0.95f, 0.80f, 0.35f, 0.90f), "ランプ用の油。ほんのり火の気を帯びている。",
                new[] { Attr(PotionAttribute.Fire, 3f) }, form: "オイル");
            var honey = Ingredient("Base_Honey", "base_honey", "花の蜜", IngredientCategory.BaseLiquid,
                new Color(1.00f, 0.75f, 0.30f, 0.90f), "農園の花から採れる甘い蜜。",
                new[] { Attr(PotionAttribute.Healing, 3f), Attr(PotionAttribute.Growth, 2f) }, form: "シロップ");
            var spiritWater = Ingredient("Base_SpiritWater", "base_spirit_water", "霊泉の水", IngredientCategory.BaseLiquid,
                new Color(0.80f, 0.95f, 1.00f, 0.70f), "森の奥の泉で汲める神秘的な水。",
                new[] { Attr(PotionAttribute.Light, 4f) }, form: "エリクシル");

            // ---- 主素材 ----
            var herb = Ingredient("Main_Herb", "main_herb", "薬草", IngredientCategory.MainMaterial,
                new Color(0.35f, 0.75f, 0.30f), "どこにでも生える薬草。回復薬の基本。",
                new[] { Attr(PotionAttribute.Healing, 20f) });
            var fireGrass = Ingredient("Main_FireGrass", "main_fire_grass", "火炎草", IngredientCategory.MainMaterial,
                new Color(0.90f, 0.30f, 0.15f), "触ると熱い赤い草。冷気を打ち消す。",
                new[] { Attr(PotionAttribute.Fire, 18f), Attr(PotionAttribute.Ice, -5f) });
            var frostMint = Ingredient("Main_FrostMint", "main_frost_mint", "氷晶ミント", IngredientCategory.MainMaterial,
                new Color(0.60f, 0.90f, 1.00f), "霜をまとったミント。炎を打ち消す。",
                new[] { Attr(PotionAttribute.Ice, 16f), Attr(PotionAttribute.Fire, -5f), Attr(PotionAttribute.Healing, 4f) });
            var sproutSeed = Ingredient("Main_SproutSeed", "main_sprout_seed", "芽吹きの種", IngredientCategory.MainMaterial,
                new Color(0.70f, 0.85f, 0.30f), "生命力にあふれた種。",
                new[] { Attr(PotionAttribute.Growth, 22f) });
            var mistFlower = Ingredient("Main_MistFlower", "main_mist_flower", "霧隠れの花", IngredientCategory.MainMaterial,
                new Color(0.85f, 0.85f, 0.95f), "見つめていると輪郭がぼやける不思議な花。光を嫌う。",
                new[] { Attr(PotionAttribute.Invisibility, 24f), Attr(PotionAttribute.Light, -6f) });
            var nightshade = Ingredient("Main_Nightshade", "main_nightshade", "ナイトシェード", IngredientCategory.MainMaterial,
                new Color(0.45f, 0.20f, 0.55f), "毒を持つ紫の実。害虫よけに使える。",
                new[] { Attr(PotionAttribute.Poison, 20f), Attr(PotionAttribute.Healing, -4f) });
            var sunPetal = Ingredient("Main_SunPetal", "main_sun_petal", "陽光の花びら", IngredientCategory.MainMaterial,
                new Color(1.00f, 0.90f, 0.45f), "日の光をたくわえた花びら。",
                new[] { Attr(PotionAttribute.Light, 18f), Attr(PotionAttribute.Healing, 4f) });
            var galeFeather = Ingredient("Main_GaleFeather", "main_gale_feather", "疾風の羽根", IngredientCategory.MainMaterial,
                new Color(0.55f, 0.95f, 0.85f), "風を呼ぶ鳥の羽根。",
                new[] { Attr(PotionAttribute.Wind, 18f), Attr(PotionAttribute.Growth, 3f) });

            // ---- 触媒 ----
            var amplifyCrystal = Ingredient("Cat_AmplifyCrystal", "cat_amplify_crystal", "増幅の結晶", IngredientCategory.Catalyst,
                new Color(0.80f, 0.60f, 1.00f), "あらゆる属性の力を引き出す結晶。",
                new AttributeValue[0], globalPercent: 50f);
            var sunStone = Ingredient("Cat_SunStone", "cat_sun_stone", "陽だまりの石", IngredientCategory.Catalyst,
                new Color(1.00f, 0.85f, 0.50f), "ぽかぽか温かい石。植物の成長を大きく助ける。",
                new[] { Attr(PotionAttribute.Light, 5f) },
                amplifiers: new[] { new AttributeAmplifier(PotionAttribute.Growth, 80f) });
            var moonDust = Ingredient("Cat_MoonDust", "cat_moon_dust", "月影の粉", IngredientCategory.Catalyst,
                new Color(0.40f, 0.40f, 0.65f), "月の影を集めた粉。姿を隠す力を強め、光を弱める。",
                new AttributeValue[0],
                amplifiers: new[]
                {
                    new AttributeAmplifier(PotionAttribute.Invisibility, 60f),
                    new AttributeAmplifier(PotionAttribute.Light, -50f),
                });

            // ---- Step5：新しい性質の素材（溶ける温度・崩れる・香りが飛ぶ・冷える・溶ける向き・すぐ溶ける）----
            var amberResin = Ingredient("Main_AmberResin", "main_amber_resin", "琥珀樹脂", IngredientCategory.MainMaterial,
                new Color(0.93f, 0.58f, 0.16f), "古い木からしみ出た、かたいやに。温めないと溶けないが、溶けると癒やしと淡い光を放つ。",
                new[] { Attr(PotionAttribute.Healing, 14f), Attr(PotionAttribute.Light, 6f) });
            var starPetal = Ingredient("Main_StarPetal", "main_star_petal", "星の花びら", IngredientCategory.MainMaterial,
                new Color(0.82f, 0.86f, 1.00f), "夜にだけ開く花の花びら。すぐ溶けるが、とてもやわらかく、混ぜすぎると崩れて光が消える。",
                new[] { Attr(PotionAttribute.Light, 16f), Attr(PotionAttribute.Wind, 5f) });
            var dreamLavender = Ingredient("Main_DreamLavender", "main_dream_lavender", "夢見ラベンダー", IngredientCategory.MainMaterial,
                new Color(0.68f, 0.52f, 0.90f), "甘い香りの紫の花。香りに力があるので、沸騰させると飛んでしまう。",
                new[] { Attr(PotionAttribute.Invisibility, 10f), Attr(PotionAttribute.Healing, 8f) });
            var frostStone = Ingredient("Cat_FrostStone", "cat_frost_stone", "氷霜石", IngredientCategory.Catalyst,
                new Color(0.80f, 0.95f, 1.00f), "触れると指が凍える白い石。入れるとフラスコが一気に冷え、氷の力を強める。",
                new[] { Attr(PotionAttribute.Ice, 6f) },
                amplifiers: new[] { new AttributeAmplifier(PotionAttribute.Ice, 40f) });
            var spiralShell = Ingredient("Main_SpiralShell", "main_spiral_shell", "渦巻き貝", IngredientCategory.MainMaterial,
                new Color(0.96f, 0.80f, 0.72f), "左巻きの小さな貝殻。風の力を秘めている。ある向きに混ぜたときだけ、ほどけるように溶けるらしい。",
                new[] { Attr(PotionAttribute.Wind, 16f), Attr(PotionAttribute.Ice, 3f) });
            var volcanoAsh = Ingredient("Main_VolcanoAsh", "main_volcano_ash", "火山の灰", IngredientCategory.MainMaterial,
                new Color(0.46f, 0.41f, 0.39f), "火山のふもとで集めた灰。水に入れるとすぐ溶けて、火の力と土の恵みを与える。",
                new[] { Attr(PotionAttribute.Fire, 10f), Attr(PotionAttribute.Growth, 6f) });
            var beesWax = Ingredient("Main_BeesWax", "main_bees_wax", "森の蜜蝋", IngredientCategory.MainMaterial,
                new Color(0.98f, 0.84f, 0.42f), "森の蜂が作ったやわらかい蝋。ぬるめのお湯（45℃）で溶けて、育てる力と癒やしを与える。",
                new[] { Attr(PotionAttribute.Growth, 12f), Attr(PotionAttribute.Healing, 8f) });
            var fiddlehead = Ingredient("Main_Fiddlehead", "main_fiddlehead", "ぜんまい草", IngredientCategory.MainMaterial,
                new Color(0.52f, 0.70f, 0.28f), "くるりと巻いた若い芽。巻きと同じ向きに混ぜると、ほどけるように溶けるらしい。",
                new[] { Attr(PotionAttribute.Growth, 14f), Attr(PotionAttribute.Wind, 6f) });
            var fizzPowder = Ingredient("Main_FizzPowder", "main_fizz_powder", "泡雪の粉", IngredientCategory.MainMaterial,
                new Color(0.88f, 0.96f, 1.00f), "入れたとたん、しゅわっと溶ける白い粉。ただし泡の力は軽く、沸騰させると抜けてしまう。",
                new[] { Attr(PotionAttribute.Wind, 12f), Attr(PotionAttribute.Ice, 5f) });
            var nightDew = Ingredient("Base_NightDew", "base_night_dew", "月下の夜露", IngredientCategory.BaseLiquid,
                new Color(0.62f, 0.66f, 0.95f, 0.75f), "月の夜にだけ集まる露。姿を隠す力がほんのりあるが、煮立てると湯気といっしょに消えてしまう。",
                new[] { Attr(PotionAttribute.Invisibility, 6f), Attr(PotionAttribute.Ice, 2f) }, form: "エリクシル");

            // ---- 完成条件データ（特殊レシピ）----
            var invisibility = Condition("Potion_Invisibility", "special_invisibility", "透明化の秘薬",
                InvisibilityDescription,
                priority: 10, minTotal: 0f, required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Invisibility, ConditionType.AtLeast, min: 30f),
                    new AttributeCondition(PotionAttribute.Light, ConditionType.AtMost, max: 5f),
                    new AttributeCondition(PotionAttribute.Invisibility, ConditionType.IsDominant),
                },
                useCustomColor: true, customColor: new Color(0.92f, 0.92f, 1.00f, 0.30f));
            // 前の版で作った説明（「依頼の定番」「相談の頼みの綱」）は、今の言い回しに直す（1画面版には相談がない）
            if (invisibility.EditorUpgradeDescription("依頼の定番", InvisibilityDescription)) EditorUtility.SetDirty(invisibility);
            if (invisibility.EditorUpgradeDescription("相談の頼みの綱", InvisibilityDescription)) EditorUtility.SetDirty(invisibility);

            var growthBooster = Condition("Potion_GrowthBooster", "special_growth_booster", "成長促進剤",
                "畑にまくと作物の成長が大きく早まる。ファームの効率化に欠かせない。",
                priority: 10, minTotal: 0f, required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Growth, ConditionType.AtLeast, min: 40f),
                    new AttributeCondition(PotionAttribute.Growth, ConditionType.ShareAtLeast, min: 70f),
                    new AttributeCondition(PotionAttribute.Poison, ConditionType.AtMost, max: 0f),
                });

            var legendary = Condition("Potion_LegendaryCure", "special_legendary_cure", "伝説の特効薬",
                "あらゆる病を癒やすと伝えられる幻の薬。霊泉の水なしには作れない。",
                priority: 100, minTotal: 100f, required: new[] { spiritWater },
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.AtLeast, min: 60f),
                    new AttributeCondition(PotionAttribute.Light, ConditionType.AtLeast, min: 20f),
                    new AttributeCondition(PotionAttribute.Poison, ConditionType.AtMost, max: 0f),
                },
                useCustomColor: true, customColor: new Color(1.00f, 0.85f, 0.40f, 1.00f));

            // 入手・分類（値段は1単位あたり）
            Details(water, 1, "水");
            Details(oil, 1, "油", "火");
            Details(honey, 1, "農園", "花");
            Details(spiritWater, 3, "水", "森", "神秘");
            Details(herb, 1, "草", "農園");
            Details(fireGrass, 1, "草", "火");
            Details(frostMint, 2, "草", "氷");
            Details(sproutSeed, 1, "種", "農園");
            Details(mistFlower, 2, "花", "森");
            Details(nightshade, 2, "実", "毒");
            Details(sunPetal, 2, "花", "光");
            Details(galeFeather, 2, "羽根", "風");
            Details(amplifyCrystal, 3, "鉱石");
            Details(sunStone, 3, "鉱石", "光");
            Details(moonDust, 3, "粉", "神秘");
            Details(amberResin, 2, "樹脂", "森");
            Details(starPetal, 2, "花", "光", "夜");
            Details(dreamLavender, 2, "花", "香り");
            Details(frostStone, 2, "鉱石", "氷");
            Details(spiralShell, 2, "貝", "海", "風");
            Details(volcanoAsh, 1, "粉", "火", "土");
            Details(beesWax, 1, "樹脂", "蝋", "森");
            Details(fiddlehead, 2, "草", "森", "風");
            Details(fizzPowder, 1, "粉", "風", "氷");
            Details(nightDew, 2, "水", "夜", "神秘");
            // 調合机に届く時期（発見のポイント：性質1・反応2・新しい液体1）。はじめから机にある素材は GameBalance
            Unlock(volcanoAsh, 2);
            Unlock(fizzPowder, 3);
            Unlock(sunPetal, 4);
            Unlock(galeFeather, 5);
            Unlock(nightshade, 6);
            Unlock(mistFlower, 7);
            Unlock(beesWax, 8);
            Unlock(amberResin, 9);
            Unlock(dreamLavender, 10);
            Unlock(frostStone, 12);
            Unlock(starPetal, 14);
            Unlock(fiddlehead, 16);
            Unlock(spiralShell, 18);
            Unlock(nightDew, 20);
            // 見た目に変化をつける（形状・容器がまだ「自動」の素材だけ）
            Shape(moonDust, IngredientForm.Powder, ContainerStyle.Pouch);
            Shape(sproutSeed, IngredientForm.Solid, ContainerStyle.Pouch);
            Shape(amplifyCrystal, IngredientForm.Solid, ContainerStyle.Crystal);
            Shape(sunStone, IngredientForm.Solid, ContainerStyle.Crystal);
            Shape(frostStone, IngredientForm.Solid, ContainerStyle.Crystal);
            Shape(volcanoAsh, IngredientForm.Powder, ContainerStyle.Pouch);
            Shape(starPetal, IngredientForm.Solid, ContainerStyle.Jar);
            Shape(beesWax, IngredientForm.Solid, ContainerStyle.Jar);
            Shape(fiddlehead, IngredientForm.Solid, ContainerStyle.Pouch);
            Shape(fizzPowder, IngredientForm.Powder, ContainerStyle.Pouch);

            // 工程での性質（まだ「自動」のままの素材だけ）
            //   薬草：熱に弱い（70℃を超えると弱まる）→ 冷ましてから入れると強い
            //   火炎草：80℃以上で温めると火の力が目覚める
            //   氷晶ミント：とても熱に弱い（40℃）／芽吹きの種・疾風の羽根：溶けにくい（温めてよく混ぜる）
            //   ナイトシェード：煮出すと毒が強まる／増幅の結晶：溶けにくい（先に溶かしておくと、あとから溶けた素材が強まる）
            Process(herb, -1f, 70f, 0.6f, 0f, 0f);
            Process(fireGrass, -1f, -1f, -1f, 80f, 60f);
            Process(frostMint, 0.6f, 40f, 0.8f, 0f, 0f);
            Process(sproutSeed, 0.25f, -1f, -1f, 0f, 0f);
            Process(mistFlower, 0.55f, 60f, 0.7f, 0f, 0f);
            Process(nightshade, 0.4f, 0f, 0f, 90f, 40f);
            Process(sunPetal, 0.7f, 90f, 0.4f, 0f, 0f);
            Process(galeFeather, 0.25f, -1f, -1f, 0f, 0f);
            Process(amplifyCrystal, 0.2f, 0f, 0f, 0f, 0f);
            Process(sunStone, 0.3f, 0f, 0f, 70f, 50f);
            //   琥珀樹脂：65℃ にならないと溶けない（熱には強い）／星の花びら：すぐ溶けるが、3回転より多く混ぜると崩れる
            //   夢見ラベンダー：沸騰させると香りが飛ぶ／氷霜石：入れるとフラスコが冷える（溶けにくい）
            //   渦巻き貝：反時計回りに混ぜたときだけ溶ける（放っておくと少しだけ）／火山の灰：すぐ溶ける粉
            Process(amberResin, 0.5f, 0f, 0f, 0f, 0f);
            Process(starPetal, 0.9f, 80f, 0.5f, 0f, 0f);
            Process(dreamLavender, 0.7f, 0f, 0f, 0f, 0f);
            Process(frostStone, 0.4f, 0f, 0f, 0f, 0f);
            Process(spiralShell, 0.15f, 0f, 0f, 0f, 0f);
            Process(volcanoAsh, 1f, 0f, 0f, 0f, 0f);
            Traits(amberResin, 65f, 0f, 0f, 0f, StirDirection.Any);
            Traits(starPetal, 0f, 3f, 0f, 0f, StirDirection.Any);
            Traits(dreamLavender, 0f, 0f, 0.8f, 0f, StirDirection.Any);
            Traits(frostStone, 0f, 0f, 0f, 30f, StirDirection.Any);
            Traits(spiralShell, 0f, 0f, 0f, 0f, StirDirection.CounterClockwise);
            //   森の蜜蝋：45℃ で溶ける（琥珀樹脂よりぬるくてよい）／ぜんまい草：時計回りに混ぜたときだけ溶ける
            //   泡雪の粉：すぐ溶ける粉だが、沸騰で力が抜ける／月下の夜露：ベース液体だが、沸騰で力が飛ぶ
            Process(beesWax, 0.6f, 0f, 0f, 0f, 0f);
            Process(fiddlehead, 0.2f, 85f, 0.4f, 0f, 0f);
            Process(fizzPowder, 1f, 0f, 0f, 0f, 0f);
            Traits(beesWax, 45f, 0f, 0f, 0f, StirDirection.Any);
            Traits(fiddlehead, 0f, 0f, 0f, 0f, StirDirection.Clockwise);
            Traits(fizzPowder, 0f, 0f, 0.6f, 0f, StirDirection.Any);
            Traits(nightDew, 0f, 0f, 0.5f, 0f, StirDirection.Any);

            // ---- 反応（なければ作る）----
            var mistMoon = Reaction("Reaction_MistMoon", "reaction_mist_moon", "霧と月の共鳴",
                "霧隠れの花と月影の粉を、冷たいまま反時計回りに混ぜると、光がゆらいで姿を隠す力に変わる。",
                new[] { mistFlower, moonDust }, 0.3f, -100f, 35f, StirDirection.CounterClockwise, 4f, null, 2f,
                new[] { new AttributeConversion { from = PotionAttribute.Light, to = PotionAttribute.Invisibility, percent = 80f, efficiency = 1.2f } },
                new[] { new AttributeMultiplier { attribute = PotionAttribute.Invisibility, multiplier = 1.1f } },
                new[] { new TraitGain { traitId = LiquidTraits.Shimmer, strength = 0.8f } });
            var honeySyrup = Reaction("Reaction_HoneySyrup", "reaction_honey_syrup", "蜜のとろみ",
                "花の蜜を沸騰するほど熱し続けると、とろりと煮詰まり、育てる力が癒やしの力に変わる。",
                new[] { honey }, 0.5f, 95f, 200f, StirDirection.Any, 0f, null, 6f,
                new[] { new AttributeConversion { from = PotionAttribute.Growth, to = PotionAttribute.Healing, percent = 40f, efficiency = 1f } },
                new[] { new AttributeMultiplier { attribute = PotionAttribute.Healing, multiplier = 1.15f } },
                new[] { new TraitGain { traitId = LiquidTraits.Syrupy, strength = 0.9f } });

            var frostFire = Reaction("Reaction_FrostFire", "reaction_frost_fire", "霜と炎の和解",
                "氷霜石で冷やしたまま火炎草を溶かしてかき混ぜると、ぶつかり合う火と氷が、やさしい癒やしの力に変わる。",
                new[] { frostStone, fireGrass }, 0.3f, -100f, 30f, StirDirection.Any, 2f, null, 3f,
                new[]
                {
                    new AttributeConversion { from = PotionAttribute.Fire, to = PotionAttribute.Healing, percent = 50f, efficiency = 1f },
                    new AttributeConversion { from = PotionAttribute.Ice, to = PotionAttribute.Healing, percent = 50f, efficiency = 1f },
                },
                null, null);
            var amberSun = Reaction("Reaction_AmberSun", "reaction_amber_sun", "琥珀の灯",
                "溶かした琥珀樹脂と陽光の花びらを、温かいまま時計回りに混ぜると、光が癒やしを包みこむ灯になる。",
                new[] { amberResin, sunPetal }, 0.4f, 65f, 100f, StirDirection.Clockwise, 3f, null, 3f,
                null,
                new[]
                {
                    new AttributeMultiplier { attribute = PotionAttribute.Healing, multiplier = 1.3f },
                    new AttributeMultiplier { attribute = PotionAttribute.Light, multiplier = 1.2f },
                },
                new[] { new TraitGain { traitId = LiquidTraits.Shimmer, strength = 0.6f } });
            var starSpiral = Reaction("Reaction_StarSpiral", "reaction_star_spiral", "流れ星の渦",
                "星の花びらと渦巻き貝を、そっと反時計回りに混ぜると、光が風に乗って流れ星のように走る。",
                new[] { starPetal, spiralShell }, 0.3f, -100f, 60f, StirDirection.CounterClockwise, 2f, null, 2f,
                new[] { new AttributeConversion { from = PotionAttribute.Light, to = PotionAttribute.Wind, percent = 40f, efficiency = 1.3f } },
                new[] { new AttributeMultiplier { attribute = PotionAttribute.Wind, multiplier = 1.1f } },
                new[] { new TraitGain { traitId = LiquidTraits.Shimmer, strength = 0.5f } });
            var waxCradle = Reaction("Reaction_WaxCradle", "reaction_wax_cradle", "蜜蝋のゆりかご",
                "溶かした森の蜜蝋とぜんまい草を、ぬるいまま時計回りに混ぜると、芽吹く力がやさしく包まれて大きく育つ。",
                new[] { beesWax, fiddlehead }, 0.3f, 45f, 85f, StirDirection.Clockwise, 2f, null, 3f,
                null,
                new[]
                {
                    new AttributeMultiplier { attribute = PotionAttribute.Growth, multiplier = 1.3f },
                    new AttributeMultiplier { attribute = PotionAttribute.Healing, multiplier = 1.1f },
                },
                new[] { new TraitGain { traitId = LiquidTraits.Syrupy, strength = 0.5f } });

            AssetDatabase.SaveAssets();
            IngredientCatalog.EnsureDatabase();
            IngredientCatalog.Refresh(false);

            return new SampleData
            {
                settings = settings,
                water = water,
                oil = oil,
                honey = honey,
                spiritWater = spiritWater,
                herb = herb,
                fireGrass = fireGrass,
                frostMint = frostMint,
                sproutSeed = sproutSeed,
                mistFlower = mistFlower,
                nightshade = nightshade,
                sunPetal = sunPetal,
                galeFeather = galeFeather,
                amplifyCrystal = amplifyCrystal,
                sunStone = sunStone,
                moonDust = moonDust,
                invisibility = invisibility,
                growthBooster = growthBooster,
                legendary = legendary,
                mistMoon = mistMoon,
                honeySyrup = honeySyrup,
                amberResin = amberResin,
                starPetal = starPetal,
                dreamLavender = dreamLavender,
                frostStone = frostStone,
                spiralShell = spiralShell,
                volcanoAsh = volcanoAsh,
                frostFire = frostFire,
                amberSun = amberSun,
                starSpiral = starSpiral,
                beesWax = beesWax,
                fiddlehead = fiddlehead,
                fizzPowder = fizzPowder,
                nightDew = nightDew,
                waxCradle = waxCradle,
            };
        }

        [MenuItem("Tools/Potion Frontier/サンプル調合データを生成")]
        public static void Generate()
        {
            // 2D 版：データ（設定・素材・完成条件・反応）だけを、なければ作る。シーンには何も置かない。
            SampleData d = CreateOrUpdateAssets();
            int baseLiquidCount = 4;
            Debug.Log("[Potion Frontier] サンプルデータを用意しました（すでにある物は上書きしていません）。\n" +
                      $"  アセット: {RootDir}\n" +
                      $"  自動生成ポーションの理論上の種類数: {d.settings.CountProceduralVariants(baseLiquidCount)} 種類（ベース液体{baseLiquidCount}種の場合）");
        }

        // ---------------------------------------------------------------------

        private static IngredientData Ingredient(string assetName, string id, string displayName, IngredientCategory category,
                                                 Color color, string description, AttributeValue[] attributes,
                                                 float globalPercent = 0f, AttributeAmplifier[] amplifiers = null,
                                                 string form = "ポーション")
        {
            return LoadOrCreate<IngredientData>(IngredientDir + "/" + assetName + ".asset",
                data => data.EditorSetup(id, displayName, category, color, description, attributes, globalPercent, amplifiers, form));
        }

        private static PotionConditionData Condition(string assetName, string id, string potionName, string description,
                                                     int priority, float minTotal, IngredientData[] required,
                                                     AttributeCondition[] conditions,
                                                     bool useCustomColor = false, Color customColor = default)
        {
            return LoadOrCreate<PotionConditionData>(ConditionDir + "/" + assetName + ".asset",
                data => data.EditorSetup(id, potionName, description, priority, minTotal, required, conditions, useCustomColor, customColor));
        }

        /// <summary>
        /// なければ作る。すでにあるアセットは上書きしない（自分で調整した値を守るため）
        /// </summary>
        private static T LoadOrCreate<T>(string path, System.Action<T> setup) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                setup(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        /// <summary>入手・分類などの追加項目を入れる（まだ初期値のままの素材だけ）</summary>
        private static void Details(IngredientData data, int rarity, params string[] tags)
        {
            if (data == null || !data.EditorDetailsAreDefault) return;
            data.EditorSetupDetails(rarity, tags);
            EditorUtility.SetDirty(data);
        }

        private static void Traits(IngredientData data, float meltTemp, float fragileTurns, float volatility, float coolTemp, StirDirection direction)
        {
            if (data == null || !data.EditorTraitsAreDefault) return;
            data.EditorSetTraits(meltTemp, fragileTurns, volatility, coolTemp, direction);
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// 調合机に届くのに必要な発見のポイント。まだ決めていない素材（0）と、1画面版の前の「必要な実績」（1〜3）のままの素材だけ変える
        /// </summary>
        internal static void Unlock(IngredientData data, int discoveries)
        {
            if (data == null || data.DiscoveriesToUnlock == discoveries || data.DiscoveriesToUnlock > 3) return;
            data.EditorSetUnlock(discoveries);
            EditorUtility.SetDirty(data);
        }

        private static void Process(IngredientData data, float solubility, float heatLimit, float sensitivity, float activationTemp, float activationBonus)
        {
            if (data == null || !data.EditorProcessIsDefault) return;
            data.EditorSetProcess(solubility, heatLimit, sensitivity, activationTemp, activationBonus);
            EditorUtility.SetDirty(data);
        }

        private static ReactionData Reaction(string assetName, string id, string displayName, string description, IngredientData[] required,
                                             float minDissolved, float minTemp, float maxTemp, StirDirection direction, float minTurns,
                                             AttributeCondition[] conditions, float duration, AttributeConversion[] conversions,
                                             AttributeMultiplier[] multipliers, TraitGain[] traits)
        {
            return LoadOrCreate<ReactionData>(ReactionDir + "/" + assetName + ".asset",
                r => r.EditorSetup(id, displayName, description, required, minDissolved, minTemp, maxTemp, direction, minTurns,
                                   conditions, duration, conversions, multipliers, traits));
        }

        private static void Shape(IngredientData data, IngredientForm form, ContainerStyle container)
        {
            if (data == null || !data.EditorShapeIsAuto) return;
            data.EditorSetShape(form, container);
            EditorUtility.SetDirty(data);
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static AttributeValue Attr(PotionAttribute attribute, float value)
        {
            return new AttributeValue(attribute, value);
        }

        private static IngredientAmount Use(IngredientData ingredient, float amount = 1f)
        {
            return new IngredientAmount(ingredient, amount);
        }
    }
}
