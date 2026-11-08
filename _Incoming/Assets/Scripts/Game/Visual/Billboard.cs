using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>常にカメラの方を向く（吹き出しなど）。少し上下にふわふわ動く</summary>
    public class Billboard : MonoBehaviour
    {
        [SerializeField] private float bobAmount = 0.03f;
        private Vector3 baseLocal;
        private float phase;

        private void Start()
        {
            baseLocal = transform.localPosition;
            phase = Random.value * 6f;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            phase += Time.deltaTime * 3f;
            transform.localPosition = baseLocal + Vector3.up * Mathf.Sin(phase) * bobAmount;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
        }
    }
}
