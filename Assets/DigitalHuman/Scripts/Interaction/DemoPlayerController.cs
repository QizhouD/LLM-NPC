using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class DemoPlayerController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float movementSpeed = 3f;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private DialogueUI dialogueUI;

        private CharacterController characterController;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (dialogueUI == null)
            {
                dialogueUI = FindFirstObjectByType<DialogueUI>();
            }
        }

        private void Update()
        {
            // Skip WASD while the command field is focused, otherwise IME and
            // typed commands are stolen by player movement.
            if (IsTypingInChat())
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            Vector3 forward = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 right = cameraTransform != null
                ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized
                : Vector3.right;

            Vector3 movement = (forward * input.y + right * input.x) * movementSpeed;
            characterController.SimpleMove(movement);
        }

        public void Configure(Transform cameraReference, float speed)
        {
            Configure(cameraReference, speed, dialogueUI);
        }

        public void Configure(
            Transform cameraReference,
            float speed,
            DialogueUI chatUi)
        {
            cameraTransform = cameraReference;
            movementSpeed = Mathf.Max(0.1f, speed);
            if (chatUi != null)
            {
                dialogueUI = chatUi;
            }
        }

        private bool IsTypingInChat()
        {
            if (dialogueUI != null && dialogueUI.IsInputFocused)
            {
                return true;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            {
                return false;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            return selected.GetComponent<TMP_InputField>() != null ||
                   selected.GetComponentInParent<TMP_InputField>() != null;
        }
    }
}
