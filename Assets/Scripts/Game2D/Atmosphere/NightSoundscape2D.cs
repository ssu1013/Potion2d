using PotionFrontier.Game;
using UnityEngine;

namespace PotionFrontier.Game2D
{
    /// <summary>
    /// 夜の環境音（シーンに1つ。「Step6：夜の空気感」で置かれる）。
    /// ランプの火が静かに燃える音を、部屋でも調合台でもずっと小さく鳴らす（調合台では手元のランプが近いので少し大きく）。
    /// お湯のコトコト・ガラスが触れ合う音は、フラスコの温度に合わせて FlaskEffects2D が鳴らす。
    /// 音は「ここ → SoundLibrary の夜の環境音 → コードで作った音」の順に探す。音量は設定の効果音の音量と掛け合わせる。
    /// </summary>
    public class NightSoundscape2D : MonoBehaviour
    {
        [Header("ランプの火が静かに燃える音")]
        [SerializeField, Tooltip("空なら SoundLibrary の lampFireLoop → コードで作った音")] private AudioClip fireLoop;
        [SerializeField, Range(0f, 1f), Tooltip("遊んでいる間の大きさ（本を開いていても、そのまま鳴り続ける）")] private float fireVolumeBrew = 0.34f;
        [SerializeField, Range(0f, 1f), Tooltip("タイトル・一時停止中の大きさ")] private float fireVolumeQuiet = 0.12f;
        [SerializeField, Min(0.05f), Tooltip("大きさが変わる速さ（1秒あたり）")] private float fadeSpeed = 0.4f;

        private AudioSource fire;

        public static NightSoundscape2D Current { get; private set; }
        /// <summary>火の音が鳴っているか（自動テスト用）</summary>
        public bool FirePlaying => fire != null && fire.isPlaying && fire.clip != null;
        public float FireVolume => fire != null ? fire.volume : 0f;

        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            fire = gameObject.AddComponent<AudioSource>();
            fire.clip = ResolveFire();
            fire.loop = true;
            fire.playOnAwake = false;
            fire.spatialBlend = 0f;
            fire.priority = 180; // 環境音なので、ほかの音に席をゆずる
            fire.volume = 0f;
            if (fire.clip != null)
            {
                fire.time = Random.Range(0f, fire.clip.length * 0.9f);
                fire.Play();
            }
        }

        private AudioClip ResolveFire()
        {
            if (fireLoop != null) return fireLoop;
            SoundLibrary lib = AudioManager.Lib;
            if (lib != null && lib.lampFireLoop != null) return lib.lampFireLoop;
            return SfxSynth.LampFireLoop;
        }

        private void Update()
        {
            if (fire == null || fire.clip == null) return;
            GameFlow flow = GameFlow.Instance;
            bool quiet = flow == null || flow.Mode != GameMode.Playing || flow.IsPaused;
            float want = quiet ? fireVolumeQuiet : fireVolumeBrew;
            want *= GameSettings.SeVolume;
            fire.volume = Mathf.MoveTowards(fire.volume, want, Time.unscaledDeltaTime * fadeSpeed);
            if (!fire.isPlaying && want > 0f) fire.Play();
        }
    }
}
