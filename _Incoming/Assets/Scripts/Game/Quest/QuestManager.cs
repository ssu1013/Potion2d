using System;
using System.Collections;
using System.Collections.Generic;
using PotionFrontier.Alchemy;
using UnityEngine;

namespace PotionFrontier.Game
{
    public enum CustomerPhase
    {
        /// <summary>お客さんがいない（次の来店待ち）</summary>
        None,
        /// <summary>入口からカウンターへ歩いている</summary>
        Arriving,
        /// <summary>カウンターで、相談したそうにしている</summary>
        Offering,
        /// <summary>相談に乗ると答え、力になってもらえるのを待っている</summary>
        Waiting,
        /// <summary>帰っている</summary>
        Leaving,
    }

    /// <summary>
    /// お客さんの来店と相談の流れを管理する（クラス名は、作ったシーンとのつながりを保つため Quest のまま）。
    /// 来店 → 困りごとの相談を聞く（力になりたい／今は力になれない）
    ///      → 自分の棚の瓶を「分け与える」か、レシピ本の作り方を「教える」（教えてもよい人だけ）
    ///      → 悩みが解決すればお礼（ゴールド）→ 帰る → 次のお客さん
    /// 合わない瓶を渡そうとしても、お客さんは瓶を返して、困りごとを詳しく話してくれる（何度でも試せる）。
    /// 最初は必ずチュートリアルの相談、一定数解決すると最後の相談（伝説の特効薬）が来る。
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        [Header("相談")]
        [SerializeField] private List<QuestData> quests = new List<QuestData>();
        [SerializeField, Tooltip("最初に必ず来る相談")] private QuestData tutorialQuest;
        [SerializeField, Tooltip("最後の相談（解決でゲームクリア）")] private QuestData finalQuest;
        [SerializeField, Min(0), Tooltip("この数の相談を解決すると、最後の相談が来る")] private int finalQuestAfter = 6;
        [SerializeField, Range(0f, 1f), Tooltip("作り方を教えたときのお礼（分け与えたときのお礼に掛ける割合）。瓶が減らない分、少なめ")]
        private float teachRewardRate = 0.7f;

        [Header("お店")]
        [SerializeField] private Transform outsidePoint;
        [SerializeField] private Transform doorPoint;
        [SerializeField] private Transform counterPoint;
        [SerializeField] private ShopDoor door;
        [SerializeField, Tooltip("お客さんの体に使うトゥーンマテリアル（色は実行時に変える）")] private Material customerMaterial;

        [Header("参照")]
        [SerializeField] private PlayerProgress progress;
        [SerializeField, Tooltip("棚（空なら起動時にシーンから探す）")] private PotionInventory potions;
        [SerializeField, Tooltip("調合の管理役（液体の記録。空なら起動時にシーンから探す）")] private AlchemyManager alchemy;

        [Header("テンポ")]
        [SerializeField] private float firstDelay = 3f;
        [SerializeField] private float nextDelay = 8f;
        [SerializeField] private float walkSpeed = 1.1f;

        private bool running;
        private float timer;
        private QuestData lastQuest;

        public CustomerPhase Phase { get; private set; }
        public Customer CurrentCustomer { get; private set; }
        public QuestData CurrentQuest => CurrentCustomer != null ? CurrentCustomer.Quest : null;
        public float TeachRewardRate => teachRewardRate;

        /// <summary>次に来るお客さんの相談を決めておく（自動テスト用。来店したら消える）</summary>
        public QuestData NextQuestOverride { get; set; }

        /// <summary>お客さん・相談の状態が変わったとき（HUD の相談メモ用）</summary>
        public event Action Changed;

        private void Awake()
        {
            if (potions == null) potions = FindFirstObjectByType<PotionInventory>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
        }

        public void Configure(List<QuestData> quests, QuestData tutorial, QuestData final, Transform outside, Transform doorPoint,
                              Transform counter, ShopDoor door, Material customerMaterial, PlayerProgress progress)
        {
            this.quests = quests;
            tutorialQuest = tutorial;
            finalQuest = final;
            outsidePoint = outside;
            this.doorPoint = doorPoint;
            counterPoint = counter;
            this.door = door;
            this.customerMaterial = customerMaterial;
            this.progress = progress;
        }

        public void StartFlow()
        {
            running = true;
            timer = firstDelay;
        }

        public void StopFlow()
        {
            running = false;
            StopAllCoroutines();
            if (CurrentCustomer != null) Destroy(CurrentCustomer.gameObject);
            CurrentCustomer = null;
            Phase = CustomerPhase.None;
            if (door != null) door.Close();
            Notify();
        }

        private void Update()
        {
            if (!running || Phase != CustomerPhase.None) return;
            timer -= Time.deltaTime;
            if (timer <= 0f) SpawnNext();
        }

        // ------------------------------------------------------------------
        // 来店・退店
        // ------------------------------------------------------------------

        private void SpawnNext()
        {
            QuestData quest = ChooseQuest();
            if (quest == null || outsidePoint == null || counterPoint == null)
            {
                timer = nextDelay;
                return;
            }
            lastQuest = quest;

            var go = new GameObject("Customer_" + quest.ClientName);
            go.transform.position = outsidePoint.position;
            Vector3 dir = DoorApproach - outsidePoint.position;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            var customer = go.AddComponent<Customer>();
            customer.Init(this, quest, customerMaterial);

            CurrentCustomer = customer;
            Phase = CustomerPhase.Arriving;
            Notify();
            StartCoroutine(ArriveRoutine(customer));
        }

        /// <summary>扉のすぐ外の位置（ここで扉が開くのを待つ）</summary>
        private Vector3 DoorApproach
        {
            get
            {
                Vector3 outward = outsidePoint.position - doorPoint.position;
                outward.y = 0f;
                return doorPoint.position + outward.normalized * Mathf.Min(1.2f, outward.magnitude);
            }
        }

        private IEnumerator ArriveRoutine(Customer customer)
        {
            // 外から扉の前まで歩く → ドアベルを鳴らして扉を開ける → 中に入って扉を閉める → カウンターへ
            yield return customer.WalkPath(new[] { DoorApproach }, walkSpeed, null);
            if (door != null) door.Open();
            yield return new WaitForSeconds(0.45f);
            yield return customer.WalkPath(new[] { doorPoint.position }, walkSpeed, null);
            if (door != null) StartCoroutine(door.CloseAfter(0.3f));
            yield return customer.WalkPath(new[] { counterPoint.position }, walkSpeed, null);
            yield return customer.TurnTo(counterPoint.forward);

            Phase = CustomerPhase.Offering;
            customer.SetMood(Customer.Mood.Exclaim);
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud != null) hud.ShowToast($"お客さんが来た！ 何か困っているみたい。入口のカウンターで話を聞こう");
            Notify();
        }

        private void Leave(Customer customer, Customer.Mood mood)
        {
            Phase = CustomerPhase.Leaving;
            customer.SetMood(mood);
            Notify();
            StartCoroutine(LeaveRoutine(customer));
        }

        private IEnumerator LeaveRoutine(Customer customer)
        {
            yield return new WaitForSeconds(1.4f);
            customer.SetMood(Customer.Mood.None);
            yield return customer.WalkPath(new[] { doorPoint.position }, walkSpeed, null);
            if (door != null) door.Open(false);
            yield return new WaitForSeconds(0.35f);
            yield return customer.WalkPath(new[] { DoorApproach }, walkSpeed, null);
            if (door != null) door.Close();
            yield return customer.WalkPath(new[] { outsidePoint.position }, walkSpeed, null);
            Destroy(customer.gameObject);
            CurrentCustomer = null;
            Phase = CustomerPhase.None;
            timer = nextDelay;
            Notify();
        }

        private QuestData ChooseQuest()
        {
            if (NextQuestOverride != null)
            {
                QuestData forced = NextQuestOverride;
                NextQuestOverride = null;
                return forced;
            }
            if (tutorialQuest != null && (progress == null || !progress.HasCompleted(tutorialQuest.QuestId))) return tutorialQuest;
            if (finalQuest != null && progress != null && !progress.GameCleared && progress.CompletedQuests >= finalQuestAfter) return finalQuest;

            var pool = new List<QuestData>();
            var fresh = new List<QuestData>();
            foreach (QuestData q in quests)
            {
                if (q == null || q == tutorialQuest || q == finalQuest || q == lastQuest) continue;
                pool.Add(q);
                if (progress == null || !progress.HasCompleted(q.QuestId)) fresh.Add(q);
            }
            List<QuestData> list = fresh.Count > 0 ? fresh : pool;
            if (list.Count == 0) return tutorialQuest;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        // ------------------------------------------------------------------
        // 会話（相談を聞く → 分け与える／教える）
        // ------------------------------------------------------------------

        public void GetCustomerOptions(Customer customer, PlayerInteractor player, List<InteractionOption> options)
        {
            if (customer != CurrentCustomer) return;
            switch (Phase)
            {
                case CustomerPhase.Offering:
                    options.Add(new InteractionOption(KeyCode.E, "E", "相談を聞く"));
                    break;
                case CustomerPhase.Waiting:
                    if (player.HeldPotion != null && player.HeldPotion.IsFullBottle)
                        options.Add(new InteractionOption(KeyCode.E, "E", $"「{player.HeldPotion.potionName}」を分け与える"));
                    else if (player.HeldPotion != null)
                        options.Add(new InteractionOption(KeyCode.E, "E", $"話す（開けた瓶 {player.HeldPotion.ml:0}ml は渡せない）"));
                    else
                        options.Add(new InteractionOption(KeyCode.E, "E", customer.Quest.AcceptsRecipe ? "話す（分け与える・作り方を教える）" : "話す（棚の瓶を分け与える）"));
                    break;
            }
        }

        public void TalkTo(Customer customer, PlayerInteractor player)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null || hud.IsModalOpen || customer != CurrentCustomer) return;
            QuestData q = customer.Quest;
            SoundLibrary lib = AudioManager.Lib;

            if (Phase == CustomerPhase.Offering)
            {
                // 困りごとを聞く。「作れ」とは言われない。力になれそうかは、自分の探求しだい
                var pages = new List<string>(q.RequestLines);
                pages.Add(OfferNote(q));
                Show(customer, pages, new[] { "力になりたい", "今は力になれない" }, choice =>
                {
                    if (choice == 0) Accept(customer);
                    else Decline(customer);
                });
                return;
            }

            if (Phase != CustomerPhase.Waiting) return;

            PotionStack potion = player.HeldPotion;
            if (potion != null && !potion.IsFullBottle)
            {
                // 分け与えるのは満タンの瓶（100ml）だけ。開けた瓶は調合の材料にする
                hud.ShowToast($"開けた瓶（{potion.ml:0}ml）は渡せません。満タンの瓶を持ってこよう");
                if (lib != null) AudioManager.Se(lib.warning, 0.6f);
                return;
            }
            if (potion != null)
            {
                // 手に持っている満タンの瓶を、その場で分け与える
                LiquidRecord record = FindRecord(potion.liquidId);
                if (q.IsSatisfiedBy(potion))
                {
                    player.ConsumeHeldPotion();
                    Solved(customer, record, potion.potionName, false);
                }
                else ShowWrong(customer, potion.potionName, false, null);
                return;
            }

            // 手ぶらで話す：棚の瓶を分け与える／作り方を教える／もう少し探求する／今は力になれない
            var waiting = new List<string>(q.WaitingLines);
            AddClues(waiting, q);
            ShowHelpMenu(customer, waiting);
        }

        /// <summary>最初の相談の最後に出す、地の文のまとめ</summary>
        private string OfferNote(QuestData q)
        {
            string trouble = string.IsNullOrEmpty(q.TroubleSummary) ? "" : $"{q.ClientName}さんの悩み：{q.TroubleSummary}";
            string reward = $"力になれたら、お礼に {q.RewardGold} G くれるらしい";
            return string.IsNullOrEmpty(trouble) ? $"（{reward}）" : $"（{trouble}\n　{reward}）";
        }

        /// <summary>見立て（どんな液体なら役に立ちそうか）と、うわさ（手がかり）を地の文で足す</summary>
        private static void AddClues(List<string> pages, QuestData q)
        {
            if (!string.IsNullOrEmpty(q.GoalSummary)) pages.Add("（見立て：" + q.GoalSummary + "なら、役に立ちそうだ）");
            if (!string.IsNullOrEmpty(q.Hint)) pages.Add("（うわさ：" + q.Hint + "）");
        }

        /// <summary>力になる方法を選ぶ（会話の最後のページに選択肢を出す）</summary>
        private void ShowHelpMenu(Customer customer, List<string> pages)
        {
            QuestData q = customer.Quest;
            var labels = new List<string>();
            var actions = new List<Action>();
            labels.Add("棚の瓶を分け与える");
            actions.Add(() => ShareFromBook(customer));
            if (q.AcceptsRecipe)
            {
                labels.Add("レシピ本で作り方を教える");
                actions.Add(() => TeachFromBook(customer));
            }
            labels.Add("探求してくる（待ってもらう）");
            actions.Add(() =>
            {
                AtelierHUD hud = AtelierHUD.Instance;
                if (hud != null) hud.ShowToast("相談メモ（右上）を見ながら、役に立ちそうな液体を探求しよう");
            });
            labels.Add("今は力になれない");
            actions.Add(() => Decline(customer));

            Show(customer, pages, labels.ToArray(), choice =>
            {
                if (choice >= 0 && choice < actions.Count) actions[choice]();
            });
        }

        private void Accept(Customer customer)
        {
            Phase = CustomerPhase.Waiting;
            customer.SetMood(Customer.Mood.Waiting);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.questAccept);
            Notify();
            // 「力になりたい」と答えたら、そのまま力になる方法を選べる（もう棚に役立つ瓶があれば、すぐ渡せる）
            var pages = new List<string>(customer.Quest.AcceptLines);
            AddClues(pages, customer.Quest);
            ShowHelpMenu(customer, pages);
        }

        /// <summary>レシピ本を開いて、分け与える瓶を選ぶ（やめたら、そのまま待ってもらう）</summary>
        private void ShareFromBook(Customer customer)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null) return;
            QuestData q = customer.Quest;
            hud.OpenRecipeBookFor(RecipeBookUI.Mode.Share, q, record =>
            {
                if (record == null || customer != CurrentCustomer || Phase != CustomerPhase.Waiting) return;
                PotionStack stack = potions != null ? potions.Find(record.id) : null;
                if (stack == null || stack.FullBottles < 1)
                {
                    hud.ShowToast($"「{record.DisplayName}」の満タンの瓶がありません");
                    return;
                }
                if (!q.IsSatisfiedBy(record))
                {
                    // 合わない瓶は、見せただけで返してくれる（棚の瓶は減らない）
                    ShowWrong(customer, record.DisplayName, false, null);
                    return;
                }
                PotionStack given = potions.TakeOne(stack); // 満タンの瓶を1本（100ml）
                if (given == null) return;
                Solved(customer, record, record.DisplayName, false);
            });
        }

        /// <summary>レシピ本を開いて、教える作り方を選ぶ（瓶は減らない。お礼は少なめ）</summary>
        private void TeachFromBook(Customer customer)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null) return;
            QuestData q = customer.Quest;
            hud.OpenRecipeBookFor(RecipeBookUI.Mode.Teach, q, record =>
            {
                if (record == null || customer != CurrentCustomer || Phase != CustomerPhase.Waiting) return;
                if (!record.HasRecipe)
                {
                    hud.ShowToast($"「{record.DisplayName}」は作り方の記録がないので、教えられない");
                    return;
                }
                if (!q.IsSatisfiedBy(record))
                {
                    ShowWrong(customer, record.DisplayName, true, record);
                    return;
                }
                Solved(customer, record, record.DisplayName, true);
            });
        }

        /// <summary>悩みに合わなかった（瓶は返してもらう／作り方は「それでは解決しない」）。困りごとを詳しく聞いて、また選べる</summary>
        private void ShowWrong(Customer customer, string liquidName, bool taught, LiquidRecord record)
        {
            QuestData q = customer.Quest;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.warning, 0.5f);
            var pages = new List<string>(q.WrongLines);
            pages.Add(taught
                ? $"（「{liquidName}」の作り方では、{q.ClientName}さんの悩みは解決しなさそうだ）"
                : $"（「{liquidName}」は、{q.ClientName}さんの悩みには合わなかった。瓶は返してもらった）");
            AddClues(pages, q);
            ShowHelpMenu(customer, pages);
        }

        /// <summary>悩みが解決した（taught = 作り方を教えた）</summary>
        private void Solved(Customer customer, LiquidRecord record, string liquidName, bool taught)
        {
            QuestData q = customer.Quest;
            int reward = taught ? Mathf.Max(1, Mathf.RoundToInt(q.RewardGold * teachRewardRate)) : q.RewardGold;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.questComplete);
            customer.SetMood(Customer.Mood.Happy);
            if (record != null && alchemy != null) alchemy.Liquids.NoteHelped(record.id, q.ClientName, taught);

            var pages = new List<string>(taught ? q.TaughtLines : q.SuccessLines);
            pages.Add(taught
                ? $"（レシピ本を見せながら「{liquidName}」の作り方を教えた。お礼に {reward} G を受け取った）"
                : $"（「{liquidName}」を分け与えた。お礼に {reward} G を受け取った）");
            Show(customer, pages, null, _ => Complete(customer, reward, taught));
        }

        private void Decline(Customer customer)
        {
            customer.SetMood(Customer.Mood.Sad);
            Show(customer, customer.Quest.DeclineLines, null, _ => Leave(customer, Customer.Mood.Sad));
        }

        private void Complete(Customer customer, int reward, bool taught)
        {
            QuestData q = customer.Quest;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.coins);
            if (progress != null)
            {
                progress.AddGold(reward);
                progress.MarkCompleted(q.QuestId, taught);
            }

            if (q.IsFinal && progress != null && !progress.GameCleared)
            {
                progress.MarkCleared();
                AtelierHUD hud = AtelierHUD.Instance;
                if (hud != null) hud.ShowClear();
            }
            if (GameFlow.Instance != null) GameFlow.Instance.Save();
            Leave(customer, Customer.Mood.Happy);
        }

        private LiquidRecord FindRecord(string liquidId)
        {
            return alchemy != null ? alchemy.Liquids.Get(liquidId) : null;
        }

        /// <summary>今の相談に役立ちそうな満タンの瓶が棚にあれば、その瓶（相談メモの案内用）</summary>
        public PotionStack FindShareable()
        {
            QuestData q = CurrentQuest;
            if (q == null || potions == null) return null;
            foreach (PotionStack s in potions.Stacks)
            {
                if (s.FullBottles >= 1 && q.IsSatisfiedBy(s)) return s;
            }
            return null;
        }

        /// <summary>今の相談に役立ちそうな作り方がレシピ本にあれば、その液体（教えてもよい人のときだけ）</summary>
        public LiquidRecord FindTeachable()
        {
            QuestData q = CurrentQuest;
            if (q == null || !q.AcceptsRecipe || alchemy == null) return null;
            IReadOnlyList<LiquidRecord> all = alchemy.Liquids.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].HasRecipe && q.IsSatisfiedBy(all[i])) return all[i];
            }
            return null;
        }

        private void Show(Customer customer, IReadOnlyList<string> pages, string[] choices, Action<int> onClose)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null || pages == null || pages.Count == 0)
            {
                if (onClose != null) onClose(-1);
                return;
            }

            customer.SetPortraitActive(true);
            QuestData q = customer.Quest;
            hud.ShowDialogue(new DialogueRequest
            {
                speaker = q.ClientName,
                title = q.ClientTitle,
                portrait = customer.Portrait,
                pages = new List<string>(pages),
                voicePitch = q.Look.voicePitch,
                choices = choices,
                onClose = choice =>
                {
                    if (customer != null) customer.SetPortraitActive(false);
                    if (onClose != null) onClose(choice);
                },
            });
        }

        private void Notify()
        {
            if (Changed != null) Changed();
        }
    }
}
