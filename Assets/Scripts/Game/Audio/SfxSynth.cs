using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 音のファイルがないときに、コードで作る静かな音（夜の工房に合う、木と弦と火の音）。
    ///   ・手ごたえ：木琴の「コン」（★・溶けきった）、目標に届いた木琴2音、ギターの「ポロロン…」（新発見）、低い弦の単音（反応）
    ///   ・環境音：ランプの火がぱちぱち燃えるループ、お湯がコトコト当たる音、ガラスが軽く触れ合う音、紙をめくる音
    ///   ・お知らせ：新しい素材が届いたときの、手紙を置くような「さっ・トン」
    /// 一度作ったら使い回す。SoundLibrary に自分の音を入れると、そちらが使われる。
    /// </summary>
    public static class SfxSynth
    {
        private const int Rate = 44100;

        private static AudioClip chime, goal, discovery, reaction, softWarning, pageTurn, lampFire, knock, glassTap, letter;

        /// <summary>木琴の「コン」（★1つ分・溶けきった。pitch を上げて音階にする）</summary>
        public static AudioClip Chime
        {
            get
            {
                if (chime == null)
                {
                    var data = new float[(int)(Rate * 0.9f)];
                    AddMarimba(data, 0f, 523.3f, 1f, 0.55f); // ド（C5）
                    chime = Make("synth_wood_chime", data);
                }
                return chime;
            }
        }

        /// <summary>目標に届いた：木琴で「ソ・ド」とやさしく2音</summary>
        public static AudioClip Goal
        {
            get
            {
                if (goal == null)
                {
                    var data = new float[(int)(Rate * 1.4f)];
                    AddMarimba(data, 0f, 392f, 0.8f, 0.6f);      // ソ（G4）
                    AddMarimba(data, 0.16f, 523.3f, 0.9f, 0.8f); // ド（C5）
                    goal = Make("synth_goal", data);
                }
                return goal;
            }
        }

        /// <summary>新しい液体・反応の発見：ナイロン弦のギターで「ポロロン…」（ゆっくり下から上へ）</summary>
        public static AudioClip Discovery
        {
            get
            {
                if (discovery == null)
                {
                    var data = new float[(int)(Rate * 3.2f)];
                    float[] notes = { 196f, 293.7f, 392f, 493.9f }; // ソ・レ・ソ・シ（G のやさしい和音）
                    for (int i = 0; i < notes.Length; i++) AddPluck(data, i * 0.11f, notes[i], 0.8f - i * 0.08f, 2.6f, 31 + i);
                    discovery = Make("synth_discovery_pluck", data);
                }
                return discovery;
            }
        }

        /// <summary>反応が起きた瞬間：低い弦の単音に、かすかな倍音</summary>
        public static AudioClip Reaction
        {
            get
            {
                if (reaction == null)
                {
                    var data = new float[(int)(Rate * 2.4f)];
                    AddPluck(data, 0f, 146.8f, 1f, 2.2f, 7);    // レ（D3）
                    AddPluck(data, 0.02f, 440f, 0.25f, 1.4f, 8); // ラ（A4）をかすかに
                    reaction = Make("synth_reaction_pluck", data);
                }
                return reaction;
            }
        }

        /// <summary>やさしい注意：低めの木琴が2つ下がる</summary>
        public static AudioClip SoftWarning
        {
            get
            {
                if (softWarning == null)
                {
                    var data = new float[(int)(Rate * 0.9f)];
                    AddMarimba(data, 0f, 329.6f, 0.8f, 0.4f);   // ミ
                    AddMarimba(data, 0.15f, 261.6f, 0.8f, 0.5f); // ド
                    softWarning = Make("synth_soft_warning", data);
                }
                return softWarning;
            }
        }

        /// <summary>紙をめくる「ぱらっ」：ざらっとした音が通り過ぎて、最後に軽く「ぱさ」</summary>
        public static AudioClip PageTurn
        {
            get
            {
                if (pageTurn == null)
                {
                    var data = new float[(int)(Rate * 0.5f)];
                    var rng = new System.Random(11);
                    float fast = 0f, slow = 0f;
                    for (int i = 0; i < data.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float n = (float)rng.NextDouble() * 2f - 1f;
                        fast += (n - fast) * 0.35f;       // 高すぎるざらつきを丸める
                        slow += (fast - slow) * 0.03f;     // 低いごろごろを抜く
                        float env = t < 0.3f ? Mathf.Pow(Mathf.Sin(Mathf.PI * t / 0.3f), 2f) * 0.55f : 0f;
                        if (t >= 0.27f) env += Mathf.Exp(-(t - 0.27f) * 45f) * 0.9f;
                        data[i] = (fast - slow) * env;
                    }
                    pageTurn = Make("synth_page_turn", data);
                }
                return pageTurn;
            }
        }

        /// <summary>
        /// ランプの火が静かに燃える音（8秒のループ。つなぎ目がわからないように重ねてある）：
        /// 低い「ごー」というやわらかな燃える音に、ときどき小さく「ぱちっ」
        /// </summary>
        public static AudioClip LampFireLoop
        {
            get
            {
                if (lampFire == null)
                {
                    int length = Rate * 8;
                    int fade = Rate / 2;
                    var raw = new float[length + fade];
                    var rng = new System.Random(5);
                    float brown = 0f, soft = 0f;
                    for (int i = 0; i < raw.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float white = (float)rng.NextDouble() * 2f - 1f;
                        brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.999f;
                        soft += (white - soft) * 0.08f;
                        // ゆっくり息をするようにふくらむ
                        float breathe = 0.75f + 0.25f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * 0.47f + 1f);
                        raw[i] = (brown * 0.5f + soft * 0.05f) * breathe;
                    }
                    // 小さな「ぱちっ」（不規則に。大きいのはまれ）
                    double at = 0.2;
                    while (at < (raw.Length / (double)Rate) - 0.05)
                    {
                        float size = (float)rng.NextDouble();
                        float amp = size < 0.85f ? 0.08f + size * 0.15f : 0.35f + (size - 0.85f) * 2f;
                        int start = (int)(at * Rate);
                        int len = (int)(Rate * (0.004f + (float)rng.NextDouble() * 0.01f));
                        float hp = 0f, prev = 0f;
                        for (int k = 0; k < len && start + k < raw.Length; k++)
                        {
                            float w = (float)rng.NextDouble() * 2f - 1f;
                            hp = 0.6f * (hp + w - prev);
                            prev = w;
                            raw[start + k] += hp * amp * Mathf.Exp(-k / (len * 0.25f));
                        }
                        at += 0.06 + rng.NextDouble() * rng.NextDouble() * 0.9;
                    }
                    var data = new float[length];
                    for (int i = 0; i < length; i++)
                    {
                        if (i < fade)
                        {
                            float k = i / (float)fade;
                            data[i] = raw[i] * k + raw[length + i] * (1f - k);
                        }
                        else data[i] = raw[i];
                    }
                    lampFire = Make("synth_lamp_fire_loop", data, 0.7f);
                }
                return lampFire;
            }
        }

        /// <summary>お湯の泡でフラスコが台に当たる「コト」（低くこもった音。pitch を少しずつ変えて鳴らす）</summary>
        public static AudioClip Knock
        {
            get
            {
                if (knock == null)
                {
                    var data = new float[(int)(Rate * 0.18f)];
                    var rng = new System.Random(3);
                    float lp = 0f;
                    for (int i = 0; i < data.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float body = Mathf.Sin(2f * Mathf.PI * 210f * t) * Mathf.Exp(-t * 38f);
                        float ring = Mathf.Sin(2f * Mathf.PI * 1180f * t) * Mathf.Exp(-t * 70f) * 0.25f;
                        lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.25f;
                        float click = lp * Mathf.Exp(-t * 400f) * 0.6f;
                        data[i] = (body + ring + click) * Mathf.Clamp01(t / 0.0015f);
                    }
                    knock = Make("synth_koto_knock", data);
                }
                return knock;
            }
        }

        /// <summary>ガラスが軽く触れ合う「ちん」（小さく・高く・すぐ消える）</summary>
        public static AudioClip GlassTap
        {
            get
            {
                if (glassTap == null)
                {
                    var data = new float[(int)(Rate * 0.5f)];
                    float[] freq = { 2637f, 4186f, 6020f };
                    float[] gain = { 1f, 0.45f, 0.18f };
                    for (int i = 0; i < data.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float s = 0f;
                        for (int k = 0; k < freq.Length; k++) s += gain[k] * Mathf.Sin(2f * Mathf.PI * freq[k] * t) * Mathf.Exp(-t * (9f + k * 8f));
                        data[i] = s * Mathf.Clamp01(t / 0.001f);
                    }
                    glassTap = Make("synth_glass_tap", data);
                }
                return glassTap;
            }
        }

        /// <summary>
        /// 新しい素材が届いた：手紙を机にすべらせる「さっ」と、そっと置く木の「トン」、
        /// 最後に遠くでかすかに木琴が1音（夜の静けさをこわさない小ささ）
        /// </summary>
        public static AudioClip LetterArrive
        {
            get
            {
                if (letter == null)
                {
                    var data = new float[(int)(Rate * 1.3f)];
                    var rng = new System.Random(23);
                    float fast = 0f, slow = 0f;
                    for (int i = 0; i < data.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float n = (float)rng.NextDouble() * 2f - 1f;
                        fast += (n - fast) * 0.22f;     // 紙のこすれ（高すぎる音を丸める）
                        slow += (fast - slow) * 0.04f;  // 低い音を抜く
                        float slide = t < 0.34f ? Mathf.Pow(Mathf.Sin(Mathf.PI * t / 0.34f), 1.6f) * 0.42f : 0f;
                        float s = (fast - slow) * slide;
                        if (t >= 0.31f)
                        {
                            float k = t - 0.31f; // 木の机にそっと置く「トン」
                            s += Mathf.Sin(2f * Mathf.PI * 172f * k) * Mathf.Exp(-k * 30f) * 0.7f;
                            s += Mathf.Sin(2f * Mathf.PI * 610f * k) * Mathf.Exp(-k * 65f) * 0.12f;
                        }
                        data[i] = s;
                    }
                    AddMarimba(data, 0.46f, 784f, 0.16f, 0.7f); // ソ（G5）を遠くにかすかに
                    letter = Make("synth_letter_arrive", data, 0.62f);
                }
                return letter;
            }
        }

        // ------------------------------------------------------------------

        /// <summary>木琴（木の板をたたく音）：基音と、木琴らしい高い倍音がすぐ消える。たたいた瞬間の「コッ」も少し</summary>
        private static void AddMarimba(float[] data, float startSeconds, float frequency, float amplitude, float decaySeconds)
        {
            float[] ratios = { 1f, 3.93f, 9.2f };
            float[] gains = { 1f, 0.16f, 0.04f };
            int start = Mathf.Max(0, (int)(startSeconds * Rate));
            for (int k = 0; k < ratios.Length; k++)
            {
                float f = frequency * ratios[k];
                if (f > Rate * 0.45f) continue;
                float decay = 4f / Mathf.Max(0.05f, decaySeconds) * (1f + k * 2.5f);
                float w = 2f * Mathf.PI * f / Rate;
                for (int i = start; i < data.Length; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t / 0.002f);
                    if (env < 0.0005f && t > 0.01f) break;
                    data[i] += amplitude * gains[k] * env * Mathf.Sin(w * (i - start));
                }
            }
            var rng = new System.Random(start + 1);
            float lp = 0f;
            for (int i = start; i < data.Length && i < start + Rate / 100; i++)
            {
                float t = (i - start) / (float)Rate;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.3f;
                data[i] += lp * amplitude * 0.12f * Mathf.Exp(-t * 600f);
            }
        }

        /// <summary>弦をはじく音（カープラス・ストロング法）。ナイロン弦のように、はじめから少し丸い音にしてある</summary>
        private static void AddPluck(float[] data, float startSeconds, float frequency, float amplitude, float decaySeconds, int seed)
        {
            int start = Mathf.Max(0, (int)(startSeconds * Rate));
            int n = Mathf.Max(2, Mathf.RoundToInt(Rate / frequency));
            var rng = new System.Random(seed);
            var ring = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.45f;
                ring[i] = lp;
            }
            float mean = 0f;
            for (int i = 0; i < n; i++) mean += ring[i];
            mean /= n;
            for (int i = 0; i < n; i++) ring[i] -= mean; // 直流分を抜く（音量をそろえるため）
            float g = Mathf.Pow(0.001f, 1f / Mathf.Max(0.1f, decaySeconds * frequency)); // 1周ごとの減り方
            int idx = 0;
            int end = Mathf.Min(data.Length, start + (int)(Rate * decaySeconds * 1.2f));
            for (int i = start; i < end; i++)
            {
                float t = (i - start) / (float)Rate;
                int next = idx + 1 == n ? 0 : idx + 1;
                float y = ring[idx];
                ring[idx] = 0.5f * (ring[idx] + ring[next]) * g;
                idx = next;
                data[i] += y * amplitude * Mathf.Clamp01(t / 0.003f);
            }
        }

        private static AudioClip Make(string name, float[] data, float peakLevel = 0.85f)
        {
            float peak = 0.0001f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float gain = peakLevel / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            chime = null;
            goal = null;
            discovery = null;
            reaction = null;
            softWarning = null;
            pageTurn = null;
            lampFire = null;
            knock = null;
            glassTap = null;
            letter = null;
        }
    }
}
