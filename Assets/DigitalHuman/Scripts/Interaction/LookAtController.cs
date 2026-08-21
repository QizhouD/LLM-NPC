using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class LookAtController : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionDetector interactionDetector;
        [SerializeField] private Transform rotationRoot;
        [SerializeField, Min(0.1f)] private float turnSpeed = 5f;

        public Transform CurrentTarget { get; private set; }

        private void Awake()
        {
            if (rotationRoot == null)
            {
                rotationRoot = transform;
            }
        }

        private void OnEnable()
        {
            Subscribe();
            if (interactionDetector != null && interactionDetector.CanInteract)
            {
                CurrentTarget = interactionDetector.CurrentPlayer;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            CurrentTarget = null;
        }

        private void Update()
        {
            if (CurrentTarget == null || rotationRoot == null)
            {
                return;
            }

            Vector3 planarDirection = CurrentTarget.position - rotationRoot.position;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
            float interpolation = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
            rotationRoot.rotation = Quaternion.Slerp(rotationRoot.rotation, targetRotation, interpolation);
        }

        public void Configure(
            PlayerInteractionDetector detector,
            Transform rootToRotate,
            float speed)
        {
            Unsubscribe();
            interactionDetector = detector;
            rotationRoot = rootToRotate != null ? rootToRotate : transform;
            turnSpeed = Mathf.Max(0.1f, speed);

            if (isActiveAndEnabled)
            {
                Subscribe();
                CurrentTarget = interactionDetector != null && interactionDetector.CanInteract
                    ? interactionDetector.CurrentPlayer
                    : null;
            }
        }

        private void Subscribe()
        {
            if (interactionDetector == null)
            {
                return;
            }

            interactionDetector.PlayerEnteredRange -= HandlePlayerEntered;
            interactionDetector.PlayerExitedRange -= HandlePlayerExited;
            interactionDetector.PlayerEnteredRange += HandlePlayerEntered;
            interactionDetector.PlayerExitedRange += HandlePlayerExited;
        }

        private void Unsubscribe()
        {
            if (interactionDetector == null)
            {
                return;
            }

            interactionDetector.PlayerEnteredRange -= HandlePlayerEntered;
            interactionDetector.PlayerExitedRange -= HandlePlayerExited;
        }

        private void HandlePlayerEntered(Transform player)
        {
            CurrentTarget = player;
        }

        private void HandlePlayerExited(Transform player)
        {
            if (CurrentTarget == player)
            {
                CurrentTarget = null;
            }
        }
    }
}
