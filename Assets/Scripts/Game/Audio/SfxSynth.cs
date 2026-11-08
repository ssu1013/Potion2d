using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 音のファイルがないときに、コードで作るやさしい効果音（鈴の「ちりん」・目標に届いた和音・発見のきらめき・反応の音・注意の音）。
    /// 一度作ったら使い回す。SoundLibrary の「調合の手ごたえ」に自分の音を入れると、そちらが使われる。
    /// </summary>
    public static class SfxSynth
    {
        private const int Rate = 44100;
        private static readonly float[] BellRatios = { 1f, 2f, 2.76f, 5.4f };
        private static readonly float[] BellGains = { 1f, 0.32f, 0.22f, 0.08f };

        private static AudioClip chime, goal, discovery, reaction, softWarning, pageTurn;

        /// <summary>鈴の「ちりん」（★1つ分。pitch を上げて音階にする）</summary>
        public static AudioClip Chime
        {
            get
            {
                if (chime == null)
                {
                    var data = new float[(int)(Rate * 1.1f)];
                    AddBell(data, 0f, 1046.5f, 1f, 0.9f); // ド（C6）
                    chime = Make("synth_chime", data);
                }
                return chime;
            }
        }

        /// <summary>目標に届いた：ド・ミ・ソ・ド の明るい分散和音</summary>
        public static AudioClip Goal
        {
            get
            {
                if (goal == null)
                {
                    var data = new float[(int)(Rate * 1.6f)];
                    float[] notes = { 784f, 987.8f, 1174.7f, 1568f }; // ソ・シ・レ・ソ
                    for (int i = 0; i < notes.Length; i++) AddBell(data, i * 0.075f, notes[i], 0.8f, 1.1f);
                    goal = Make("synth_goal", data);
                }
                return goal;
            }
        }

        /// <summary>新しい液体の発見：ゆっくりのぼる和音ときらめき</summary>
        public static AudioClip Discovery
        {
            get
            {
                if (discovery == null)
                {
                    var data = new float[(int)(Rate * 2.2f)];
                    float[] notes = { 659.3f, 830.6f, 987.8f, 1318.5f, 1661.2f }; // ミ・ソ#・シ・ミ・ソ#
                    for (int i = 0; i < notes.Length; i++) AddBell(data, i * 0.11f, notes[i], 0.75f, 1.4f);
                    var rng = new System.Random(7);
                    for (int i = 0; i < 9; i++) AddBell(data, 0.55f + (float)rng.NextDouble() * 0.9f, 2600f + (float)rng.NextDouble() * 2200f, 0.12f, 0.35f);
                    discovery = Make("synth_discovery", data);
                }
                return discovery;
            }
        }

        /// <summary>反応が起きた瞬間：ふわっと持ち上がる音と、高い鈴</summary>
        public static AudioClip Reaction
        {
            get
            {
                if (reaction == null)
                {
                    int n = (int)(Rate * 1.8f);
                    var data = new float[n];
                    // 低い音がぐっと持ち上がる（周波数が上がりながら、ふくらんで消える）
                    double phase = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;
                        float f = 180f + 520f * Mathf.Clamp01(t / 0.6f);
                        phase += 2 * Mathf.PI * f / Rate;
                        float env = Mathf.Clamp01(t / 0.35f) * Mathf.Exp(-Mathf.Max(0f, t - 0.35f) * 4f);
                        data[i] += (float)(System.Math.Sin(phase) + 0.3 * System.Math.Sin(phase * 2.01)) * env * 0.35f;
                    }
                    AddBell(data, 0.32f, 1318.5f, 0.7f, 1.2f);
                    AddBell(data, 0.40f, 1975.5f, 0.45f, 1.0f);
                    reaction = Make("synth_reaction", data);
                }
                return reaction;
            }
        }

        /// <summary>やさしい注意：低めの「ぽろん」が2つ下がる</summary>
        public static AudioClip SoftWarning
        {
            get
            {
                if (softWarning == null)
                {
                    var data = new float[(int)(Rate * 0.9f)];
                    AddBell(data, 0f, 523.3f, 0.7f, 0.45f);
                    AddBell(data, 0.14f, 415.3f, 0.7f, 0.55f);
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

        /// <summary>鐘のような倍音を、高い音ほど早く消えるように足す</summary>
        private static void AddBell(float[] data, float startSeconds, float frequency, float amplitude, float decaySeconds)
        {
            int start = Mathf.Max(0, (int)(startSeconds * Rate));
            for (int k = 0; k < BellRatios.Length; k++)
            {
                float f = frequency * BellRatios[k];
                if (f > Rate * 0.45f) continue;
                float decay = 3f / Mathf.Max(0.05f, decaySeconds) * (1f + k * 0.8f);
                float w = 2f * Mathf.PI * f / Rate;
                for (int i = start; i < data.Length; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t / 0.004f);
                    if (env < 0.0005f && t > 0.01f) break;
                    data[i] += amplitude * BellGains[k] * env * Mathf.Sin(w * (i - start));
                }
            }
        }

        private static AudioClip Make(string name, float[] data)
        {
            float peak = 0.0001f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float gain = 0.85f / peak;
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
        }
    }
}
