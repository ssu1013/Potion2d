using System.Collections;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>お店の入口の扉。開くときにドアベルが鳴る</summary>
    public class ShopDoor : MonoBehaviour
    {
        [SerializeField, Tooltip("回転させる扉の板（蝶番の位置が原点）")] private Transform leaf;
        [SerializeField] private float openAngle = -95f;
        [SerializeField] private float speed = 3.5f;

        private float target;
        private float current;

        public void Configure(Transform leaf, float openAngle)
        {
            this.leaf = leaf;
            this.openAngle = openAngle;
        }

        public void Open(bool ring = true)
        {
            target = 1f;
            SoundLibrary lib = AudioManager.Lib;
            if (ring && lib != null) AudioManager.SeAt(lib.doorBell, transform.position + Vector3.up * 2f, 0.9f);
        }

        public void Close()
        {
            target = 0f;
        }

        public IEnumerator CloseAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Close();
        }

        private void Update()
        {
            if (leaf == null) return;
            current = Mathf.MoveTowards(current, target, Time.deltaTime * speed);
            float eased = current * current * (3f - 2f * current);
            leaf.localRotation = Quaternion.Euler(0f, openAngle * eased, 0f);
        }
    }
}
