using System;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DigitalHumanAnimator : MonoBehaviour
    {
        private static readonly int IsTalkingHash = Animator.StringToHash("IsTalking");
        private static readonly int IsListeningHash = Animator.StringToHash("IsListening");
        private static readonly int IsThinkingHash = Animator.StringToHash("IsThinking");

        [SerializeField] private DigitalHumanController stateController;
        [SerializeField] private Animator animator;

        public bool IsTalking => ReadBool(IsTalkingHash);
        public bool IsListening => ReadBool(IsListeningHash);
        public bool IsThinking => ReadBool(IsThinkingHash);

        private void OnEnable()
        {
            Subscribe();
            ApplyState(stateController != null
                ? stateController.CurrentState
                : DigitalHumanState.Idle);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(DigitalHumanController controller, Animator targetAnimator)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled)
            {
                Unsubscribe();
            }

            stateController = controller;
            animator = targetAnimator;

            if (wasEnabled)
            {
                Subscribe();
                ApplyState(stateController != null
                    ? stateController.CurrentState
                    : DigitalHumanState.Idle);
            }
        }

        public void ApplyState(DigitalHumanState state)
        {
            SetBool(IsTalkingHash, state == DigitalHumanState.Speaking);
            SetBool(IsListeningHash, state == DigitalHumanState.Listening);
            SetBool(IsThinkingHash, state == DigitalHumanState.Thinking);
        }

        private void HandleStateChanged(
            DigitalHumanState _,
            DigitalHumanState nextState)
        {
            ApplyState(nextState);
        }

        private void Subscribe()
        {
            if (stateController != null)
            {
                stateController.StateChanged += HandleStateChanged;
            }
        }

        private void Unsubscribe()
        {
            if (stateController != null)
            {
                stateController.StateChanged -= HandleStateChanged;
            }
        }

        private void SetBool(int parameterHash, bool value)
        {
            if (animator != null && HasBoolParameter(parameterHash))
            {
                animator.SetBool(parameterHash, value);
            }
        }

        private bool ReadBool(int parameterHash)
        {
            return animator != null &&
                   HasBoolParameter(parameterHash) &&
                   animator.GetBool(parameterHash);
        }

        private bool HasBoolParameter(int parameterHash)
        {
            if (animator == null)
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.nameHash == parameterHash &&
                    parameter.type == AnimatorControllerParameterType.Bool)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
