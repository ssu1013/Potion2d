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
    /// お客さんの来店と相談の流れ（2D 版。シーンの GameSystems に付ける。クラス名は 3D 版と同じ）。
    /// 来店 → カウンターのお客さんをクリックして困りごとを聞く（力になりたい／今は力になれない）
    ///      → 棚の瓶を「分け与える」か、レシピ本の作り方を「教える」（教えてもよい人だけ）
    ///      → 悩みが解決すればお礼（ゴールド）→ 帰る → 次のお客さん
    /// 合わない瓶は、見せただけで返してくれる（何度でも試せる）。最初は必ずチュートリアルの相談、
    /// 一定数解決すると最後の相談（伝説の特効薬）が来て、解決するとクリア。
    /// （3D 版 QuestManager の流れをそのまま移し、歩くお客さんと扉を 2D に替えた）
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        [Header("相談")]
        [SerializeField] private List<QuestData> quests = new List<QuestData>();
        [SerializeField, Tooltip("最初に必ず来る相談")] private QuestData tutorialQuest;
        [SerializeField, Tooltip("最後の相談（解決でゲームクリア）")] private QuestData finalQuest;
        [SerializeField, Min(0), Tooltip("この数の相談を解決すると、最後の相談が来る")] private int finalQuestAfter = 6;
        [SerializeField, Range(0f, 1f), Tooltip("作り方を教えたときのお礼（分け与えたときのお礼に掛ける割合）")] private float teachRewardRate = 0.7f;

        [Header("お店（2D）")]
        [SerializeField, Tooltip("お客さんが現れる所（部屋の左の外）")] private Transform outsidePoint;
        [SerializeField, Tooltip("お客さんが立つ所（カウンターの前）")] private Transform counterPoint;
        [SerializeField, Tooltip("お客さんを置く親（部屋）")] private Transform customerParent;

        [Header("参照（空ならシーンから探す）")]
        [SerializeField] private PlayerProgress progress;
        [SerializeField] private PotionInventory potions;
        [SerializeField] private AlchemyManager alchemy;

        [Header("テンポ")]
        [SerializeField] private float firstDelay = 3f;
        [SerializeField] private float nextDelay = 8f;
        [SerializeField, Tooltip("歩く速さ（ワールド単位/秒）")] private float walkSpeed = 2.4f;

        private bool running;
        private float timer;
        private QuestData lastQuest;

        public CustomerPhase Phase { get; private set; }
        public Customer2D CurrentCustomer { get; private set; }
        public QuestData CurrentQuest => CurrentCustomer != null ? CurrentCustomer.Quest : null;
        public float TeachRewardRate => teachRewardRate;

        /// <summary>お客さん・相談の状態が変わったとき（相談メモ用）</summary>
        public event Action Changed;

        private void Awake()
        {
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            if (potions == null) potions = FindFirstObjectByType<PotionInventory>();
            if (alchemy == null) alchemy = FindFirstObjectByType<AlchemyManager>();
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
            Notify();
        }

        /// <summary>次のお客さんを、待ち時間なしで呼ぶ（自動テスト・確認用。お客さんがいるときは何もしない）</summary>
        public void SpawnNow()
        {
            if (!running || Phase != CustomerPhase.None) return;
            timer = 0f;
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
            Customer2D customer = Customer2D.Create(quest, outsidePoint.position, customerParent);
            CurrentCustomer = customer;
            Phase = CustomerPhase.Arriving;
            Notify();
            StartCoroutine(ArriveRoutine(customer));
        }

        private IEnumerator ArriveRoutine(Customer2D customer)
        {
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.doorBell, 0.8f);
            yield return customer.WalkTo(counterPoint.position.x, walkSpeed);
            Phase = CustomerPhase.Offering;
            AtelierHUD hud = AtelierHUD.Instance;
            bool inBrew = Game2D.AtelierView2D.Current != null && Game2D.AtelierView2D.Current.InBrew;
            bool atCounter = !inBrew && Game2D.AtelierView2D.Current != null && Game2D.AtelierView2D.Current.Stop == 0;
            if (hud != null) hud.ShowToast(inBrew
                ? "お客さんが来た！ 何か困っているみたい（Esc で部屋に戻って、カウンターで話を聞こう）"
                : atCounter ? "お客さんが来た！ 何か困っているみたい。お客さんをクリックして話を聞こう"
                : "お客さんが来た！ 何か困っているみたい（左の「＜」でカウンターへ行って、話を聞こう）");
            Notify();
        }

        private void Leave(Customer2D customer, Customer2D.MoodKind mood)
        {
            Phase = CustomerPhase.Leaving;
            customer.SetMood(mood);
            Notify();
            StartCoroutine(LeaveRoutine(customer));
        }

        private IEnumerator LeaveRoutine(Customer2D customer)
        {
            yield return new WaitForSeconds(1.4f);
            customer.SetMood(Customer2D.MoodKind.None);
            yield return customer.WalkTo(outsidePoint.position.x, walkSpeed);
            Destroy(customer.gameObject);
            CurrentCustomer = null;
            Phase = CustomerPhase.None;
            timer = nextDelay;
            Notify();
        }

        private QuestData ChooseQuest()
        {
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

        /// <summary>カウンターのお客さんをクリックしたとき（AtelierView2D から呼ぶ）</summary>
        public void TalkTo(Customer2D customer)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null || hud.IsModalOpen || customer == null || customer != CurrentCustomer) return;
            QuestData q = customer.Quest;

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

            // 力になる方法を選ぶ：棚の瓶を分け与える／作り方を教える／もう少し探求する／今は力になれない
            var waiting = new List<string>(q.WaitingLines);
            AddClues(waiting, q);
            ShowHelpMenu(customer, waiting);
        }

        /// <summary>最初の相談の最後に出す、地の文のまとめ</summary>
        private static string OfferNote(QuestData q)
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

        private void ShowHelpMenu(Customer2D customer, List<string> pages)
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
                if (hud != null) hud.ShowToast("相談メモ（右上）を見ながら、役に立ちそうな液体を探求しよう（作業台をクリック）");
            });
            labels.Add("今は力になれない");
            actions.Add(() => Decline(customer));

            Show(customer, pages, labels.ToArray(), choice =>
            {
                if (choice >= 0 && choice < actions.Count) actions[choice]();
            });
        }

        private void Accept(Customer2D customer)
        {
            Phase = CustomerPhase.Waiting;
            customer.SetMood(Customer2D.MoodKind.Waiting);
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.questAccept);
            Notify();
            var pages = new List<string>(customer.Quest.AcceptLines);
            AddClues(pages, customer.Quest);
            ShowHelpMenu(customer, pages);
        }

        /// <summary>レシピ本を開いて、分け与える瓶を選ぶ（やめたら、そのまま待ってもらう）</summary>
        private void ShareFromBook(Customer2D customer)
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
                    ShowWrong(customer, record.DisplayName, false);
                    return;
                }
                PotionStack given = potions.TakeOne(stack); // 満タンの瓶を1本（100ml）
                if (given == null) return;
                Solved(customer, record, record.DisplayName, false);
            });
        }

        /// <summary>レシピ本を開いて、教える作り方を選ぶ（瓶は減らない。お礼は少なめ）</summary>
        private void TeachFromBook(Customer2D customer)
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
                    ShowWrong(customer, record.DisplayName, true);
                    return;
                }
                Solved(customer, record, record.DisplayName, true);
            });
        }

        /// <summary>悩みに合わなかった（瓶は返してもらう／作り方では解決しない）。困りごとを詳しく聞いて、また選べる</summary>
        private void ShowWrong(Customer2D customer, string liquidName, bool taught)
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

        private void Solved(Customer2D customer, LiquidRecord record, string liquidName, bool taught)
        {
            QuestData q = customer.Quest;
            int reward = taught ? Mathf.Max(1, Mathf.RoundToInt(q.RewardGold * teachRewardRate)) : q.RewardGold;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null) AudioManager.Se(lib.questComplete);
            customer.SetMood(Customer2D.MoodKind.Happy);
            if (record != null && alchemy != null) alchemy.Liquids.NoteHelped(record.id, q.ClientName, taught);

            var pages = new List<string>(taught ? q.TaughtLines : q.SuccessLines);
            pages.Add(taught
                ? $"（レシピ本を見せながら「{liquidName}」の作り方を教えた。お礼に {reward} G を受け取った）"
                : $"（「{liquidName}」を分け与えた。お礼に {reward} G を受け取った）");
            Show(customer, pages, null, _ => Complete(customer, reward, taught));
        }

        private void Decline(Customer2D customer)
        {
            customer.SetMood(Customer2D.MoodKind.Sad);
            Show(customer, customer.Quest.DeclineLines, null, _ => Leave(customer, Customer2D.MoodKind.Sad));
        }

        private void Complete(Customer2D customer, int reward, bool taught)
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
            Leave(customer, Customer2D.MoodKind.Happy);
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

        private void Show(Customer2D customer, IReadOnlyList<string> pages, string[] choices, Action<int> onClose)
        {
            AtelierHUD hud = AtelierHUD.Instance;
            if (hud == null || pages == null || pages.Count == 0)
            {
                if (onClose != null) onClose(-1);
                return;
            }
            QuestData q = customer.Quest;
            hud.ShowDialogue(new DialogueRequest
            {
                speaker = q.ClientName,
                title = q.ClientTitle,
                portrait = customer.Portrait,
                pages = new List<string>(pages),
                voicePitch = q.Look != null ? q.Look.voicePitch : 1f,
                choices = choices,
                onClose = onClose,
            });
        }

        private void Notify()
        {
            if (Changed != null) Changed();
        }
    }
}
