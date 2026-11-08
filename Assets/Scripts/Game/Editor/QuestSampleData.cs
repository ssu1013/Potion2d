using System.Collections.Generic;
using PotionFrontier.Alchemy;
using PotionFrontier.Alchemy.EditorTools;
using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// サンプルの相談データ（町の人11人分）を Assets/GameData/Quests に作る。
    /// お客さんは「〇〇を作って」とは言わず、「〇〇で困っている」と相談に来る（フェーズ5作業計画5）。
    /// すでにある相談データは上書きしない（調整はインスペクターで行える）。消した相談だけが作り直される。
    /// ただし、このメニューで作った相談のうち「版」が古いものは、新しくする：
    ///   ・版2：ヒント・条件・★（属性が「100ml あたりの濃さ」になったため）
    ///   ・版3：セリフを「相談」の形に・悩み・見立て・うわさ・作り方を教えられるか（見た目・お礼・特別なポーションの指定はそのまま）
    /// </summary>
    public static class QuestSampleData
    {
        private const string Dir = "Assets/GameData/Quests";
        private const int SampleVersion = 3;

        public sealed class Result
        {
            public QuestData tutorial;
            public QuestData final;
            public readonly List<QuestData> regular = new List<QuestData>();
        }

        private sealed class Texts
        {
            public string[] request;
            public string[] accept;
            public string[] waiting;
            public string[] success;
            public string[] wrong;
            public string[] decline;
            public string trouble;
            public string goal;
            public string hint;
            public bool acceptsRecipe;
            public string[] taught;
        }

        public static Result CreateOrUpdate(AlchemySampleDataGenerator.SampleData data)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/GameData", "Quests");
            var r = new Result();

            // ---- チュートリアル -------------------------------------------------
            r.tutorial = Quest("Quest_00_Mina", "q_mina_scrape", "ミーナ", "村の女の子",
                Look(new Color(1f, 0.86f, 0.74f), new Color(0.88f, 0.48f, 0.22f), new Color(0.96f, 0.62f, 0.68f), new Color(1f, 0.96f, 0.9f),
                     HairStyle.TwinTails, HatStyle.None, false, 0.84f, 0.95f, 1.45f),
                new Texts
                {
                    request = new[]
                    {
                        "あのね、あのね……さっき広場で転んじゃったの。",
                        "ひざをすりむいて、ひりひりするの……。錬金術師さんのところに、痛いのがなおるもの、ある？",
                    },
                    accept = new[] { "ほんと！？ ありがとう、錬金術師さん！ ここで待ってるね。" },
                    waiting = new[] { "ひざ、まだひりひりする……。" },
                    success = new[] { "わぁっ、ぜんぜん痛くない！ すごーい！", "これ、お母さんにもらったおこづかい。とっておいて！" },
                    wrong = new[] { "うーん、これをぬっても、ひりひりしたままみたい……。" },
                    decline = new[] { "そっかぁ……。じゃあ、がまんする……。" },
                    trouble = "広場で転んで、ひざをすりむいてしまった",
                    goal = "回復の力がいちばん強い液体（回復 15 以上）",
                    hint = "「薬草」を「清らかな水」に溶かすと、傷にきく液体になるらしい（水を注いで薬草を2つ入れ、フラスコの上でぐるぐると円を描いてよくかき混ぜてから、瓶に詰める）",
                    acceptsRecipe = false,
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.AtLeast, 15f),
                },
                minStars: 0, reward: 30, isFinal: false);

            // ---- ふつうの相談 -----------------------------------------------------
            r.regular.Add(Quest("Quest_01_Gald", "q_gald_forge", "ガルド", "鍛冶屋",
                Look(new Color(0.9f, 0.7f, 0.55f), new Color(0.24f, 0.15f, 0.1f), new Color(0.46f, 0.3f, 0.2f), new Color(0.42f, 0.42f, 0.47f),
                     HairStyle.Short, HatStyle.Bandana, true, 1.14f, 1.25f, 0.68f),
                new Texts
                {
                    request = new[]
                    {
                        "よう、錬金術師。鍛冶屋のガルドだ。",
                        "最近、炉の火がどうにも弱くてな。鉄がなかなか赤くならねえんだ。",
                        "炭を足しても、ふいごを踏んでもだめでよ……。錬金術で、なんとかならねえもんかな。",
                    },
                    accept = new[] { "ほんとか！ 助かるぜ。" },
                    waiting = new[] { "炉の火さえ強くなりゃあ、いい鉄が打てるんだがなあ。" },
                    success = new[] { "こいつはいい！ 手に持っただけで熱いぜ！", "これで炉もごうごう燃える。礼を受け取ってくれ。" },
                    wrong = new[] { "うーん、こいつじゃ炉の火は強くならねえな……。" },
                    decline = new[] { "そうか、忙しいところ悪かったな。" },
                    trouble = "炉の火が弱くて、鉄がなかなか赤くならない",
                    goal = "火の力が強い液体（火 30 以上・火がいちばん強い）",
                    hint = "「灯り油」に「火炎草」。火炎草は 80℃以上で温め続けると力が目覚めるらしい（H で火加減）",
                    acceptsRecipe = true,
                    taught = new[] { "なるほど、そうやって作るのか！ 温度と混ぜ方が肝なんだな。", "これなら炉の火が弱るたびに、自分で作れる。ありがとよ、先生！" },
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Fire, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Fire, ConditionType.AtLeast, 30f),
                },
                minStars: 0, reward: 60, isFinal: false));

            r.regular.Add(Quest("Quest_02_Sora", "q_sora_desert", "ソラ", "旅の商人",
                Look(new Color(0.95f, 0.78f, 0.62f), new Color(0.95f, 0.85f, 0.55f), new Color(0.86f, 0.74f, 0.5f), new Color(0.2f, 0.55f, 0.55f),
                     HairStyle.Long, HatStyle.Hood, false, 1.04f, 1f, 1.05f),
                new Texts
                {
                    request = new[]
                    {
                        "はじめまして。東の国から来た商人のソラです。",
                        "これから砂漠を越えるのですが、昼の暑さが本当にこたえて……。",
                        "体を冷やせるものがあれば心強いのですが、何か心当たりはありませんか？",
                    },
                    accept = new[] { "本当ですか！ 出発の準備をしながら待っていますね。" },
                    waiting = new[] { "砂漠の暑さを思うと、今から汗が出てきます……。" },
                    success = new[] { "ああ、すずしい……！ これなら砂漠も怖くありません。", "旅のお守りにします。お代をどうぞ。" },
                    wrong = new[] { "これは……体は冷えないようですね。" },
                    decline = new[] { "わかりました。ほかの町でも聞いてみます。" },
                    trouble = "砂漠を越えたいが、昼の暑さがこたえる",
                    goal = "氷の力が強い液体（氷 25 以上・氷がいちばん強い）",
                    hint = "「氷晶ミント」はとても熱に弱い。火は使わずに、よく混ぜて溶かすといいらしい",
                    acceptsRecipe = false,
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Ice, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Ice, ConditionType.AtLeast, 25f),
                },
                minStars: 0, reward: 60, isFinal: false));

            r.regular.Add(Quest("Quest_03_Bell", "q_bell_farm", "ベル", "農家のおばあさん",
                Look(new Color(1f, 0.85f, 0.75f), new Color(0.84f, 0.84f, 0.86f), new Color(0.45f, 0.6f, 0.35f), new Color(0.98f, 0.94f, 0.82f),
                     HairStyle.Bun, HatStyle.StrawHat, false, 0.9f, 1.12f, 1.12f),
                new Texts
                {
                    request = new[]
                    {
                        "こんにちは。町はずれで畑をやっているベルですよ。",
                        "今年はかぼちゃの育ちが悪くてねえ……収穫祭に間に合いそうにないの。",
                        "昔、作物をぐんぐん育てる「成長促進剤」という薬があったと聞いたけれど……。",
                    },
                    accept = new[] { "まあ、うれしい。ゆっくりでいいからね。" },
                    waiting = new[] { "かぼちゃたち、収穫祭までに大きくなってくれるかしら。" },
                    success = new[] { "まあまあ、なんてきれいな色！ これで畑も元気になるわ。", "少ないけれど、お礼を受け取ってちょうだいね。" },
                    wrong = new[] { "あらあら、これをまいても、かぼちゃは育たないみたいねえ。" },
                    decline = new[] { "いいのよ、気にしないで。また今度お話を聞いてね。" },
                    trouble = "かぼちゃの育ちが悪く、収穫祭に間に合いそうにない",
                    goal = "特別なポーション「成長促進剤」",
                    hint = "「花の蜜」に「陽だまりの石」を先に溶かしてから「芽吹きの種」。種は固いので、温めながらよく混ぜるといいらしい",
                    acceptsRecipe = true,
                    taught = new[] { "まあ、そんなふうに作るのねえ。メモしておかなくちゃ。", "これで来年からは、自分で畑にまけるわ。ありがとうね。" },
                },
                required: data.growthBooster,
                conditions: null,
                minStars: 0, reward: 120, isFinal: false));

            r.regular.Add(Quest("Quest_04_Luka", "q_luka_hide", "ルカ", "いたずら好きの少年",
                Look(new Color(1f, 0.84f, 0.7f), new Color(0.16f, 0.13f, 0.12f), new Color(0.35f, 0.5f, 0.88f), new Color(1f, 0.82f, 0.3f),
                     HairStyle.Short, HatStyle.Cap, false, 0.82f, 0.95f, 1.55f),
                new Texts
                {
                    request = new[]
                    {
                        "しーっ！ 声が大きいよ！",
                        "今度のかくれんぼ大会で、ぜったい見つからない方法を探してるんだ。",
                        "姿が見えなくなる薬があるって、本当？ ひみつだからね！",
                    },
                    accept = new[] { "やった！ ここで目立たないように待ってる！" },
                    waiting = new[] { "早くしないと大会が始まっちゃうよ～。" },
                    success = new[] { "すっげー！ 手が透けて見える！", "これで優勝まちがいなし！ おこづかい全部あげる！" },
                    wrong = new[] { "これじゃ透明になれないよ～。" },
                    decline = new[] { "ちぇっ。自分で木の陰にでも隠れるか……。" },
                    trouble = "かくれんぼ大会で、ぜったいに見つかりたくない",
                    goal = "特別なポーション「透明化の秘薬」",
                    hint = "「霧隠れの花」と「月影の粉」。冷たいまま反時計回りに混ぜると、不思議なことが起きるらしい",
                    acceptsRecipe = false,
                },
                required: data.invisibility,
                conditions: null,
                minStars: 0, reward: 100, isFinal: false));

            r.regular.Add(Quest("Quest_05_Emma", "q_emma_bakery", "エマ", "パン屋さん",
                Look(new Color(1f, 0.86f, 0.76f), new Color(0.55f, 0.33f, 0.18f), new Color(0.97f, 0.93f, 0.86f), new Color(0.85f, 0.32f, 0.3f),
                     HairStyle.Bob, HatStyle.Scarf, false, 1f, 1.1f, 1.2f),
                new Texts
                {
                    request = new[]
                    {
                        "こんにちは！ パン屋のエマです。",
                        "収穫祭の準備で、朝から晩までパンを焼きっぱなしなの。",
                        "もう腕も足もくたくた……。ふつうの薬じゃ追いつかないくらい疲れてて。",
                    },
                    accept = new[] { "ありがとう！ お店を抜けてきたから、なるべく早いとうれしいな。" },
                    waiting = new[] { "ふわぁ……。立ったまま眠っちゃいそう。" },
                    success = new[] { "ふわぁ……体がかるい！ これならあと百個は焼けるわ！", "これ、お礼。今度焼きたてのパンも持ってくるね。" },
                    wrong = new[] { "うーん、これだとちょっと効き目が弱いかも……。" },
                    decline = new[] { "そっか、がんばって自力で乗り切るね！" },
                    trouble = "パンを焼きっぱなしで、ふつうの薬では追いつかないほど疲れている",
                    goal = "よく効く回復の液体（回復 40 以上・回復がいちばん強い・★2 以上）",
                    hint = "先に「増幅の結晶」をよく溶かしておくと、あとから溶けた「薬草」がよく効くらしい",
                    acceptsRecipe = false,
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.AtLeast, 40f),
                },
                minStars: 2, reward: 90, isFinal: false));

            r.regular.Add(Quest("Quest_06_Noah", "q_noah_night", "ノア", "見張りの兵士",
                Look(new Color(0.98f, 0.82f, 0.68f), new Color(0.95f, 0.8f, 0.45f), new Color(0.25f, 0.3f, 0.52f), new Color(0.95f, 0.78f, 0.3f),
                     HairStyle.Short, HatStyle.Beret, false, 1.12f, 1.08f, 0.85f),
                new Texts
                {
                    request = new[]
                    {
                        "失礼する。町の門の見張りをしているノアだ。",
                        "夜の見回りで、ランタンの油がすぐ切れてしまってな。",
                        "暗い夜道では、足元もおぼつかない。何か良い知恵はないだろうか。",
                    },
                    accept = new[] { "感謝する。日が沈むまでに間に合えば十分だ。" },
                    waiting = new[] { "今夜も見回りがある。明るい夜道になればいいのだが。" },
                    success = new[] { "おお、まぶしいほどだ！ これなら夜道も安心だな。", "町を守る者として礼を言う。受け取ってくれ。" },
                    wrong = new[] { "これでは夜道は照らせそうにないな。" },
                    decline = new[] { "そうか。今夜はたいまつでしのぐとしよう。" },
                    trouble = "夜の見回りでランタンの油がすぐ切れ、夜道が暗い",
                    goal = "光の力が強い液体（光 25 以上・光がいちばん強い）",
                    hint = "「陽光の花びら」が光る。「霊泉の水」もほんのり光るらしい",
                    acceptsRecipe = true,
                    taught = new[] { "なるほど……覚えたぞ。見張りの仲間にも伝えておこう。", "これで毎晩の見回りが明るくなる。知恵を分けてくれて感謝する。" },
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Light, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Light, ConditionType.AtLeast, 25f),
                },
                minStars: 0, reward: 70, isFinal: false));

            r.regular.Add(Quest("Quest_07_Pipi", "q_pipi_windmill", "ピピ", "風車小屋の娘",
                Look(new Color(1f, 0.87f, 0.77f), new Color(0.55f, 0.76f, 0.46f), new Color(0.62f, 0.8f, 0.96f), new Color(1f, 1f, 1f),
                     HairStyle.Long, HatStyle.Beret, false, 0.92f, 0.95f, 1.35f),
                new Texts
                {
                    request = new[]
                    {
                        "こんにちはっ！ 丘の上の風車小屋のピピです！",
                        "ここ何日か風がぴたっと止んじゃって、風車が回らないの。",
                        "粉がひけなくて、町のパン屋さんも困ってて……どうしたらいいかな。",
                    },
                    accept = new[] { "わーい！ ほんとに？ おねがいね！" },
                    waiting = new[] { "風、まだ吹かないかな～。" },
                    success = new[] { "わっ、ふたを開けただけで風が！ 髪がぼさぼさ～！", "これで風車も回るよ。ありがとう！" },
                    wrong = new[] { "うーん、風は吹かないみたい……。" },
                    decline = new[] { "そっか～。風が吹くまで待ってみる！" },
                    trouble = "風が止んで風車が回らず、粉がひけない",
                    goal = "風の力が強い液体（風 25 以上・風がいちばん強い）",
                    hint = "「疾風の羽根」は溶けにくい。温めながらよく混ぜるといいらしい",
                    acceptsRecipe = true,
                    taught = new[] { "へぇ～、そうやって作るんだ！ やってみる！", "これで風が止んでも、自分で風を呼べるね。ありがとう！" },
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Wind, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Wind, ConditionType.AtLeast, 25f),
                },
                minStars: 0, reward: 70, isFinal: false));

            r.regular.Add(Quest("Quest_08_Tom", "q_tom_pests", "トム", "畑のおじさん",
                Look(new Color(0.94f, 0.76f, 0.6f), new Color(0.45f, 0.3f, 0.18f), new Color(0.4f, 0.5f, 0.72f), new Color(0.82f, 0.7f, 0.5f),
                     HairStyle.Short, HatStyle.StrawHat, true, 1.08f, 1.2f, 0.8f),
                new Texts
                {
                    request = new[]
                    {
                        "やあやあ、トムだよ。キャベツ畑をやってるんだ。",
                        "それがねえ、今年は虫がすごくて、葉っぱが穴だらけなんだよ。",
                        "畑にまいて虫を追いはらえるものが、何かないかなあ。もちろん飲まないよ！",
                    },
                    accept = new[] { "ありがたい！ キャベツたちも喜ぶよ。" },
                    waiting = new[] { "こうしてる間にも、キャベツがかじられてるんだよなあ……。" },
                    success = new[] { "おお、このいかにもな紫色！ 虫も逃げ出すぞ。", "これはお礼だよ。来年のキャベツも楽しみにしてて！" },
                    wrong = new[] { "これじゃあ虫は逃げてくれなさそうだなあ。" },
                    decline = new[] { "そうかい。手で一匹ずつとるとするか……。" },
                    trouble = "キャベツ畑に虫がわいて、葉っぱが穴だらけ",
                    goal = "毒の力が強い液体（毒 25 以上・毒がいちばん強い）",
                    hint = "「ナイトシェード」の実。ぐつぐつ煮出すと毒が強まるらしい",
                    acceptsRecipe = true,
                    taught = new[] { "ほうほう、そうやって作るのか。これなら毎年使えるねえ。", "畑仲間にも教えてあげよう。ありがとう！" },
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Poison, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Poison, ConditionType.AtLeast, 25f),
                },
                minStars: 0, reward: 80, isFinal: false));

            r.regular.Add(Quest("Quest_09_Hanna", "q_hanna_cold", "ハンナ", "郵便屋さん",
                Look(new Color(1f, 0.85f, 0.75f), new Color(0.75f, 0.28f, 0.2f), new Color(0.78f, 0.35f, 0.3f), new Color(0.98f, 0.92f, 0.78f),
                     HairStyle.Bob, HatStyle.Cap, false, 1f, 1f, 1.18f),
                new Texts
                {
                    request = new[]
                    {
                        "くしゅんっ！ ……あ、ごめんなさい。郵便屋のハンナです。",
                        "雨の中を配達していたら、すっかり体が冷えちゃって……。",
                        "体の芯からぽかぽかあたたまって、元気になれるものがあればいいんですけど……くしゅんっ。",
                    },
                    accept = new[] { "ありがとうございます……くしゅんっ。" },
                    waiting = new[] { "配達の荷物が、まだたくさん残っているんです……くしゅん。" },
                    success = new[] { "あったか～い……。鼻もすっきりしてきました！", "これで配達を続けられます。本当にありがとう！" },
                    wrong = new[] { "うーん、これはぽかぽかしないみたいです……くしゅん。" },
                    decline = new[] { "いえいえ、あたたかくして寝ることにします……。" },
                    trouble = "雨の中を配達して、体がすっかり冷えてしまった",
                    goal = "回復の力に、少し火の力も加わった液体（回復 20 以上・火 8 以上・回復がいちばん強い）",
                    hint = "「薬草」に「火炎草」をほんの少し。「灯り油」を注いでもいいらしい",
                    acceptsRecipe = false,
                },
                required: null,
                conditions: new[]
                {
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.IsDominant),
                    new AttributeCondition(PotionAttribute.Healing, ConditionType.AtLeast, 20f),
                    new AttributeCondition(PotionAttribute.Fire, ConditionType.AtLeast, 8f),
                },
                minStars: 0, reward: 90, isFinal: false));

            // ---- 最後の相談 ---------------------------------------------------------
            r.final = Quest("Quest_99_Olga", "q_olga_legend", "オルガ", "町長",
                Look(new Color(0.98f, 0.82f, 0.7f), new Color(0.92f, 0.92f, 0.94f), new Color(0.5f, 0.35f, 0.62f), new Color(0.96f, 0.8f, 0.35f),
                     HairStyle.Bun, HatStyle.None, false, 1.05f, 1.15f, 0.95f),
                new Texts
                {
                    request = new[]
                    {
                        "あなたが、町で評判の錬金術師さんね。町長のオルガです。",
                        "実は……町で、ねむり病という流行り病が広がっているの。",
                        "いろいろな薬を試したけれど、どれも効かなかった。",
                        "古い言い伝えに「伝説の特効薬」という薬があるの。もし、あなたの研究で何かわかることがあれば……。",
                    },
                    accept = new[] { "ありがとう……！ 町のみんなの希望を、あなたに託します。" },
                    waiting = new[] { "伝説の特効薬……霊泉の水が鍵になると言われているわ。" },
                    success = new[]
                    {
                        "この輝き……まちがいない、伝説の特効薬だわ！",
                        "すぐにみんなに届けます。あなたは町の恩人よ。",
                        "これは町からのお礼。これからも、この町の錬金術師でいてちょうだいね。",
                    },
                    wrong = new[] { "これは……言い伝えの薬とはちがうようね。" },
                    decline = new[] { "……そう。でも、気が変わったらいつでも声をかけて。" },
                    trouble = "町に「ねむり病」が広がり、どの薬も効かない",
                    goal = "特別なポーション「伝説の特効薬」",
                    hint = "「霊泉の水」に「増幅の結晶」を溶かし、「薬草」たっぷりと「陽光の花びら」。薬草は熱に弱いらしい",
                    acceptsRecipe = true,
                    taught = new[]
                    {
                        "この作り方なら……町の薬師たちにも作れるわ！",
                        "あなたの知恵が、町じゅうの人を救うのね。あなたは町の恩人よ。",
                        "これは町からのお礼。これからも、この町の錬金術師でいてちょうだいね。",
                    },
                },
                required: data.legendary,
                conditions: null,
                minStars: 0, reward: 500, isFinal: true);

            AssetDatabase.SaveAssets();
            return r;
        }

        private static CustomerLook Look(Color skin, Color hair, Color cloth, Color accent, HairStyle hairStyle, HatStyle hat,
                                         bool beard, float height, float width, float voice)
        {
            return new CustomerLook
            {
                skin = skin,
                hair = hair,
                cloth = cloth,
                accent = accent,
                hairStyle = hairStyle,
                hatStyle = hat,
                beard = beard,
                height = height,
                width = width,
                voicePitch = voice,
            };
        }

        private static QuestData Quest(string assetName, string id, string clientName, string title, CustomerLook look, Texts t,
                                       PotionConditionData required, AttributeCondition[] conditions, int minStars, int reward, bool isFinal)
        {
            string path = Dir + "/" + assetName + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<QuestData>(path);
            if (asset != null)
            {
                // すでにある相談は上書きしない（インスペクターで調整した内容を守る）。サンプルの版が古いときだけ、
                // セリフ（相談の形）・悩み・見立て・うわさ・条件・★を新しくする（見た目・お礼・特別なポーションの指定はそのまま）
                if (asset.SampleVersion < SampleVersion)
                {
                    asset.EditorUpdateTexts(SampleVersion, t.request, t.accept, t.waiting, t.success, t.wrong, t.decline,
                                            t.trouble, t.goal, t.hint, t.acceptsRecipe, t.taught, conditions, minStars);
                    EditorUtility.SetDirty(asset);
                }
                return asset;
            }
            asset = ScriptableObject.CreateInstance<QuestData>();
            asset.EditorSetup(id, clientName, title, look, t.request, t.accept, t.waiting, t.success, t.wrong, t.decline,
                              t.goal, t.hint, required, conditions, minStars, reward, isFinal,
                              t.trouble, t.acceptsRecipe, t.taught, SampleVersion);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
