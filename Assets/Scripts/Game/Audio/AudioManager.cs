using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// BGM（クロスフェード）・部屋の環境音・効果音をまとめて鳴らす。
    /// どこからでも AudioManager.Se(AudioManager.Lib?.uiClick) のように呼べる。
    /// 音量：設定のマスター音量（全体）× 種類ごとの音量（BGM／環境音／効果音）。
    ///   Se … 効果音の音量で鳴らす ／ Ambient … 環境音の音量で鳴らす（お湯のコトコトなど、1回きりの環境音）
    /// 効果音は使い回しの AudioSource で鳴らす。鳴っている途中の音を横取りしない・同じ音を連打で重ねすぎない。
    /// ウィンドウが裏に回ったとき、設定で「止める」を選んでいれば、すべての音を止める（戻ったら続きから）。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] private SoundLibrary library;
        [SerializeField, Range(0f, 1f), Tooltip("BGMの基本音量（設定の BGM の音量と掛け合わせる）。夜の空気感では小さめ（BGM より環境音）")] private float bgmBase = 0.18f;
        [SerializeField, Range(0f, 1f), Tooltip("部屋の環境音の基本音量（設定の環境音の音量と掛け合わせる）")] private float ambientBase = 0.55f;
        [SerializeField, Range(0f, 0.2f), Tooltip("同じ効果音を続けて鳴らせる、いちばん短い間隔（秒）。連打で同じ音が重なって大きくなりすぎないように")] private float sameClipInterval = 0.035f;

        private const int PoolSize = 16;

        private AudioSource bgmA;
        private AudioSource bgmB;
        private AudioSource ambient;
        private readonly AudioSource[] pool = new AudioSource[PoolSize];
        /// <summary>使い回しの AudioSource ごとの、鳴り終わる時刻（実時間）</summary>
        private readonly float[] busyUntil = new float[PoolSize];
        private readonly Dictionary<AudioClip, float> lastPlayed = new Dictionary<AudioClip, float>();
        private bool bgmOnA = true;
        private Coroutine bgmFade;
        private bool backgrounded;

        public static SoundLibrary Lib => Instance != null ? Instance.library : null;

        public void Configure(SoundLibrary library)
        {
            this.library = library;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AudioManager] シーンに2つ以上あります。あとから動いた方を使います。", this);
            }
            Instance = this;
            bgmA = CreateSource("BGM_A", true);
            bgmB = CreateSource("BGM_B", true);
            ambient = CreateSource("Ambient", true);
            ambient.priority = 160; // 環境音は、ほかの音に席をゆずる
            for (int i = 0; i < PoolSize; i++) pool[i] = CreateSource("SE_" + i, false);
            GameSettings.Changed += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolumes;
            if (Instance != this) return;
            Instance = null;
            AudioListener.pause = false;
        }

        /// <summary>ウィンドウが裏に回った／戻った</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            backgrounded = !hasFocus;
            ApplyBackgroundMute();
            // 別のモニターへ移したときなど、リフレッシュレートが変わっていることがあるので、戻ったときに合わせ直す
            if (hasFocus) GameSettings.ApplyFrameRate();
        }

        // ------------------------------------------------------------------
        // 効果音
        // ------------------------------------------------------------------

        /// <summary>効果音を鳴らす（設定の効果音の音量）</summary>
        public static void Se(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (Instance == null || clip == null) return;
            Instance.PlayPooled(clip, volume * GameSettings.SeVolume, pitch);
        }

        /// <summary>1回きりの環境音を鳴らす（設定の環境音の音量。お湯のコトコト・ガラスが触れ合う音など）</summary>
        public static void Ambient(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (Instance == null || clip == null) return;
            Instance.PlayPooled(clip, volume * GameSettings.AmbientVolume, pitch);
        }

        // ------------------------------------------------------------------
        // BGM・環境音
        // ------------------------------------------------------------------

        public static void PlayBgm(AudioClip clip, float fadeSeconds = 1.5f)
        {
            if (Instance == null) return;
            Instance.CrossfadeTo(clip, fadeSeconds);
        }

        public static void PlayAmbient(AudioClip clip)
        {
            if (Instance == null) return;
            AudioSource s = Instance.ambient;
            if (s.clip == clip && s.isPlaying) return;
            s.clip = clip;
            if (clip != null) s.Play();
            else s.Stop();
        }

        private void CrossfadeTo(AudioClip clip, float seconds)
        {
            AudioSource from = bgmOnA ? bgmA : bgmB;
            if (from.clip == clip && from.isPlaying) return;
            AudioSource to = bgmOnA ? bgmB : bgmA;
            bgmOnA = !bgmOnA;
            if (bgmFade != null) StopCoroutine(bgmFade);
            bgmFade = StartCoroutine(Crossfade(from, to, clip, Mathf.Max(0.01f, seconds)));
        }

        private IEnumerator Crossfade(AudioSource from, AudioSource to, AudioClip clip, float seconds)
        {
            to.clip = clip;
            to.volume = 0f;
            if (clip != null) to.Play();
            float startFrom = from.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                // 耳に自然に聞こえるよう、なめらかな曲線で入れ替える
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                from.volume = startFrom * (1f - k);
                to.volume = BgmVolume * k;
                yield return null;
            }
            from.Stop();
            from.volume = 0f;
            to.volume = BgmVolume;
            bgmFade = null;
        }

        private float BgmVolume => bgmBase * GameSettings.BgmVolume;

        private void ApplyVolumes()
        {
            AudioListener.volume = GameSettings.MasterVolume;
            AudioSource current = bgmOnA ? bgmA : bgmB;
            if (bgmFade == null) current.volume = BgmVolume;
            ambient.volume = ambientBase * GameSettings.AmbientVolume;
            ApplyBackgroundMute();
        }

        private void ApplyBackgroundMute()
        {
            bool mute = backgrounded && GameSettings.MuteInBackground;
            if (AudioListener.pause != mute) AudioListener.pause = mute;
        }

        // ------------------------------------------------------------------

        private void PlayPooled(AudioClip clip, float volume, float pitch)
        {
            if (volume <= 0.0001f) return;
            float now = Time.unscaledTime;
            // 連打・同じフレームの重複で、同じ音が何重にもならないように
            if (sameClipInterval > 0f && lastPlayed.TryGetValue(clip, out float last) && now - last < sameClipInterval) return;
            lastPlayed[clip] = now;

            int index = FreeSource(now);
            AudioSource src = pool[index];
            src.Stop();
            src.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            src.volume = 1f;
            src.PlayOneShot(clip, volume);
            busyUntil[index] = now + clip.length / Mathf.Max(0.1f, src.pitch) + 0.02f;
        }

        /// <summary>鳴っていない AudioSource（どれも鳴っていれば、いちばん早く鳴り終わる物）</summary>
        private int FreeSource(float now)
        {
            int best = 0;
            float soonest = float.MaxValue;
            for (int i = 0; i < PoolSize; i++)
            {
                if (busyUntil[i] <= now) return i;
                if (busyUntil[i] < soonest)
                {
                    soonest = busyUntil[i];
                    best = i;
                }
            }
            return best;
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            return src;
        }
    }
}
