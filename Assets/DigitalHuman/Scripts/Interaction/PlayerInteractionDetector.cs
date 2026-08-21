using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PlayerInteractionDetector : MonoBehaviour
    {
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private Key interactionKey = Key.E;

        public bool CanInteract { get; private set; }
        public Transform CurrentPlayer { get; private set; }

        public event Action<Transform> PlayerEnteredRange;
        public event Action<Transform> PlayerExitedRange;
        public event Action<Transform> InteractionRequested;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (CanInteract && keyboard != null && keyboard[interactionKey].wasPressedThisFrame)
            {
                TryInteract();
            }
        }

        public bool TryInteract()
        {
            if (!CanInteract || CurrentPlayer == null)
            {
                return false;
            }

            Debug.Log("[DigitalHuman] Player requested interaction.", this);
            InteractionRequested?.Invoke(CurrentPlayer);
            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Transform player = ResolvePlayer(other);
            if (player == null || player == CurrentPlayer)
            {
                return;
            }

            CurrentPlayer = player;
            CanInteract = true;
            Debug.Log("[DigitalHuman] Player entered interaction range.", this);
            PlayerEnteredRange?.Invoke(player);
        }

        private void OnTriggerExit(Collider other)
        {
            Transform player = ResolvePlayer(other);
            if (player == null || player != CurrentPlayer)
            {
                return;
            }

            Transform exitingPlayer = CurrentPlayer;
            CurrentPlayer = null;
            CanInteract = false;
            Debug.Log("[DigitalHuman] Player exited interaction range.", this);
            PlayerExitedRange?.Invoke(exitingPlayer);
        }

        private void OnDisable()
        {
            CurrentPlayer = null;
            CanInteract = false;
        }

        private Transform ResolvePlayer(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                return other.transform;
            }

            Rigidbody attachedRigidbody = other.attachedRigidbody;
            return attachedRigidbody != null && attachedRigidbody.CompareTag(playerTag)
                ? attachedRigidbody.transform
                : null;
        }

        private void OnValidate()
        {
            Collider trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }
    }
}
