using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>シーンに置いた環境音（大鍋のぐつぐつ等）を、設定の効果音音量に合わせて鳴らす</summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientSoundEmitter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float baseVolume = 0.5f;

        public void Configure(float baseVolume)
        {
            this.baseVolume = baseVolume;
        }

        private void Start()
        {
            var source = GetComponent<AudioSource>();
            AudioManager.RegisterSeSource(source, baseVolume);
            if (source.clip != null && !source.isPlaying)
            {
                source.time = Random.Range(0f, source.clip.length * 0.9f); // 鳴り始めをずらす
                source.Play();
            }
        }
    }
}
