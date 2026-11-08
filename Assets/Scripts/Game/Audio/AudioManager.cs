using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// BGM（クロスフェード）・環境音・効果音・注ぐ音のループをまとめて鳴らす。
    /// どこからでも AudioManager.Se(AudioManager.Lib?.uiClick) のように呼べる。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] private SoundLibrary library;
        [SerializeField, Range(0f, 1f), Tooltip("BGMの基本音量（設定の音量と掛け合わせる）。夜の空気感では小さめ（BGM より環境音）")] private float bgmBase = 0.18f;
        [SerializeField, Range(0f, 1f), Tooltip("部屋の環境音の基本音量（設定の効果音の音量と掛け合わせる）")] private float ambientBase = 0.55f;

        private AudioSource bgmA;
        private AudioSource bgmB;
        private AudioSource ambient;
        private AudioSource pourSource;
        private readonly List<AudioSource> pool = new List<AudioSource>();
        private readonly List<AudioSource> pool3D = new List<AudioSource>();
        private int nextPool3D;
        private readonly List<KeyValuePair<AudioSource, float>> registered = new List<KeyValuePair<AudioSource, float>>();
        private int nextPool;
        private bool bgmOnA = true;
        private Coroutine bgmFade;
        private float pourTarget;

        public static SoundLibrary Lib => Instance != null ? Instance.library : null;

        public void Configure(SoundLibrary library)
        {
            this.library = library;
        }

        private void Awake()
        {
            Instance = this;
            bgmA = CreateSource("BGM_A", true);
            bgmB = CreateSource("BGM_B", true);
            ambient = CreateSource("Ambient", true);
            pourSource = CreateSource("PourLoop", true);
            for (int i = 0; i < 12; i++) pool.Add(CreateSource("SE_" + i, false));
            // 位置のある効果音（足音・ドアベル）は、毎回オブジェクトを作らずに使い回す
            for (int i = 0; i < 8; i++)
            {
                AudioSource src = CreateSource("SE3D_" + i, false);
                src.spatialBlend = 1f;
                src.minDistance = 1f;
                src.maxDistance = 14f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.dopplerLevel = 0f;
                pool3D.Add(src);
            }
            GameSettings.Changed += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolumes;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // 注ぐ音は急に止めず、すっと消す
            float target = pourTarget * GameSettings.SeVolume * 0.7f;
            pourSource.volume = Mathf.MoveTowards(pourSource.volume, target, Time.unscaledDeltaTime * 3f);
            if (pourSource.volume <= 0.001f && pourTarget <= 0f && pourSource.isPlaying) pourSource.Stop();
        }

        // ------------------------------------------------------------------
        // 効果音
        // ------------------------------------------------------------------

        /// <summary>2Dの効果音を鳴らす</summary>
        public static void Se(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (Instance == null || clip == null) return;
            Instance.PlayPooled(clip, volume, pitch);
        }

        /// <summary>位置のある効果音を鳴らす（遠いと小さく聞こえる）</summary>
        public static void SeAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (Instance == null || clip == null) return;
            AudioSource src = Instance.pool3D[Instance.nextPool3D];
            Instance.nextPool3D = (Instance.nextPool3D + 1) % Instance.pool3D.Count;
            src.transform.position = position;
            src.Stop();
            src.clip = clip;
            src.pitch = pitch;
            src.volume = volume * GameSettings.SeVolume;
            src.Play();
        }

        /// <summary>注ぐ音（ループ）のオン・オフ</summary>
        public static void SetPourLoop(bool on, float pitch = 1f)
        {
            if (Instance == null) return;
            Instance.pourTarget = on ? 1f : 0f;
            if (on && Lib != null && Lib.pourLoop != null)
            {
                AudioSource s = Instance.pourSource;
                if (s.clip != Lib.pourLoop) s.clip = Lib.pourLoop;
                s.pitch = pitch;
                if (!s.isPlaying) s.Play();
            }
        }

        /// <summary>シーン上の AudioSource（大鍋の環境音など）を効果音の音量設定に従わせる</summary>
        public static void RegisterSeSource(AudioSource source, float baseVolume)
        {
            if (Instance == null || source == null) return;
            Instance.registered.Add(new KeyValuePair<AudioSource, float>(source, baseVolume));
            source.volume = baseVolume * GameSettings.SeVolume;
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
            bgmFade = StartCoroutine(Crossfade(from, to, clip, seconds));
        }

        private IEnumerator Crossfade(AudioSource from, AudioSource to, AudioClip clip, float seconds)
        {
            to.clip = clip;
            to.volume = 0f;
            if (clip != null) to.Play();
            float startFrom = from.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = t / seconds;
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
            AudioSource current = bgmOnA ? bgmA : bgmB;
            if (bgmFade == null) current.volume = BgmVolume;
            ambient.volume = ambientBase * GameSettings.SeVolume;
            foreach (KeyValuePair<AudioSource, float> pair in registered)
            {
                if (pair.Key != null) pair.Key.volume = pair.Value * GameSettings.SeVolume;
            }
        }

        private void PlayPooled(AudioClip clip, float volume, float pitch)
        {
            AudioSource src = pool[nextPool];
            nextPool = (nextPool + 1) % pool.Count;
            src.pitch = pitch;
            src.volume = 1f;
            src.PlayOneShot(clip, volume * GameSettings.SeVolume);
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
