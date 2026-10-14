using System;
using System.Collections;
using DigitalHuman.Planning;
using UnityEngine;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AgentCommandController : MonoBehaviour
    {
        private const string ObstacleObjectName = "NavigationObstacle";

        private enum CommandKind
        {
            Move,
            Pickup,
            Drop,
            Place,
            Jump
        }

        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private AgentMovement movement;
        [SerializeField] private AgentInteraction interaction;
        [SerializeField, Min(2f)] private float commandTimeout = 18f;
        [SerializeField, Min(0f)] private float acknowledgementDelay = 0.2f;
        [SerializeField, Min(0.5f)] private float jumpTimeout = 3f;

        private MotionIntentAnimator intentAnimator;
        private TrajectoryPlanFollower planFollower;
        private Coroutine activeCommand;
        private bool announceAcknowledgementsInternal = true;

        public AgentNpcState CurrentState { get; private set; } = AgentNpcState.Idle;
        public bool IsExecutingCommand => activeCommand != null;
        public GameObject CurrentTarget { get; private set; }
        public GameObject HeldItem => interaction != null ? interaction.HeldItem : null;

        public event Action<AgentNpcState, AgentNpcState> StateChanged;
        public event Action<string> CommandAccepted;
        public event Action<string> CommandCompleted;
        public event Action<string> CommandFailed;

        private void Awake()
        {
            ResolveReferences();
            EnsurePlaceSurface();
        }

        private void OnDisable()
        {
            if (activeCommand != null)
            {
                StopCoroutine(activeCommand);
                activeCommand = null;
                interaction?.CancelAction();
            }

            movement?.Stop();
            movement?.EndFacingOverride();
            SetState(AgentNpcState.Idle);
        }

        public void Configure(
            DialogueController controller,
            AgentMovement agentMovement,
            AgentInteraction agentInteraction)
        {
            dialogueController = controller;
            movement = agentMovement;
            interaction = agentInteraction;
        }

        /// <summary>
        /// Whether the keyword parser recognises <paramref name="command"/>, without acting on
        /// it. Lets the dialogue controller send the message to the model first and still know
        /// that a world action is available.
        /// </summary>
        public bool CanHandle(string command)
        {
            return TryParse(Normalize(command), out _, out _, out _);
        }

        public bool ExecuteCommand(string command)
        {
            return ExecuteCommand(command, true);
        }

        /// <summary>
        /// Executes a parsed command. When <paramref name="announceAcknowledgements"/> is
        /// false the "Okay, I'll ..." and success lines are logged but not spoken, so they do
        /// not overwrite a reply the language model already produced. Failures always speak.
        /// </summary>
        public bool ExecuteCommand(string command, bool announceAcknowledgements)
        {
            string normalized = Normalize(command);
            if (!TryParse(
                    normalized,
                    out CommandKind kind,
                    out string itemName,
                    out string destinationName))
            {
                return false;
            }

            announceAcknowledgementsInternal = announceAcknowledgements;
            ResolveReferences();
            EnsurePlaceSurface();
            if (activeCommand != null)
            {
                Respond("I'm still carrying out the last command. Please wait.", false);
                return true;
            }

            // A plan started by an earlier walk tag still owns the root, and would drag the
            // body one way while the NavMesh route below pulls it another.
            if (planFollower != null && planFollower.IsFollowing)
            {
                planFollower.StopFollowing();
            }

            if (kind == CommandKind.Jump)
            {
                if (intentAnimator == null || !intentAnimator.CanJump)
                {
                    const string unable = "I can't jump right now.";
                    Respond(unable, false);
                    CommandFailed?.Invoke(unable);
                    return true;
                }

                activeCommand = StartCoroutine(ExecuteJumpRoutine(command.Trim()));
                return true;
            }

            if (kind == CommandKind.Drop)
            {
                activeCommand = StartCoroutine(
                    ExecuteDropRoutine(command.Trim(), itemName));
                return true;
            }

            if (kind == CommandKind.Place)
            {
                activeCommand = StartCoroutine(
                    ExecutePlaceRoutine(command.Trim(), itemName, destinationName));
                return true;
            }

            string targetName = itemName ?? destinationName;
            GameObject target = FindTarget(targetName);
            if (target == null)
            {
                string message = $"I couldn't find the {FormatTarget(targetName)}.";
                Respond(message, false);
                CommandFailed?.Invoke(message);
                return true;
            }

            PickupItem pickupItem = target.GetComponent<PickupItem>();
            if (kind == CommandKind.Pickup && pickupItem != null &&
                pickupItem.IsPickedUp)
            {
                string message = $"The {pickupItem.DisplayName} is already in my hands.";
                Respond(message, false);
                CommandCompleted?.Invoke(message);
                return true;
            }

            activeCommand = StartCoroutine(
                ExecuteMoveOrPickupRoutine(command.Trim(), kind, target));
            return true;
        }

        private IEnumerator ExecuteMoveOrPickupRoutine(
            string originalCommand,
            CommandKind kind,
            GameObject target)
        {
            CurrentTarget = target;
            PickupItem item = target.GetComponent<PickupItem>();
            string displayName = item != null
                ? item.DisplayName
                : FormatTarget(target.name);
            string acknowledgement = kind == CommandKind.Pickup
                ? $"Okay, I'll pick up the {displayName}."
                : $"Okay, I'll go to the {displayName}.";

            SetState(AgentNpcState.Talking);
            Respond(acknowledgement, true);
            CommandAccepted?.Invoke(originalCommand);
            if (acknowledgementDelay > 0f)
            {
                yield return new WaitForSeconds(acknowledgementDelay);
            }

            movement.BeginFacingOverride();
            yield return MoveToAndWait(target, displayName);
            if (activeCommand == null)
            {
                yield break;
            }

            SetState(AgentNpcState.Interacting);
            yield return interaction.FaceTarget(target);

            if (kind == CommandKind.Pickup)
            {
                yield return interaction.PickupRoutine(target);
                if (!interaction.LastActionSucceeded)
                {
                    FailCurrentCommand(
                        $"I reached the {displayName}, but I couldn't pick it up.");
                    yield break;
                }
            }

            movement.EndFacingOverride();
            SetState(AgentNpcState.Talking);
            string success = kind == CommandKind.Pickup
                ? $"I've picked up the {displayName}."
                : $"I've arrived at the {displayName}.";
            Respond(success, true);
            CommandCompleted?.Invoke(success);
            yield return new WaitForSeconds(0.2f);
            FinishCommand();
        }

        private IEnumerator ExecuteJumpRoutine(string originalCommand)
        {
            SetState(AgentNpcState.Talking);
            Respond("Okay, watch this.", true);
            CommandAccepted?.Invoke(originalCommand);
            if (acknowledgementDelay > 0f)
            {
                yield return new WaitForSeconds(acknowledgementDelay);
            }

            movement?.Stop();
            SetState(AgentNpcState.Interacting);
            intentAnimator.TriggerJump();

            // One frame for the Animator to take the trigger, then wait out the clip.
            yield return null;
            float deadline = Time.time + jumpTimeout;
            while (intentAnimator.IsPlayingGesture && Time.time < deadline)
            {
                yield return null;
            }

            SetState(AgentNpcState.Talking);
            const string success = "Ta-da!";
            Respond(success, true);
            CommandCompleted?.Invoke(success);
            yield return new WaitForSeconds(0.2f);
            FinishCommand();
        }

        private IEnumerator ExecuteDropRoutine(string originalCommand, string itemName)
        {
            GameObject held = interaction != null ? interaction.HeldItem : null;
            if (held == null)
            {
                FailCurrentCommand("I'm not holding anything to drop.");
                yield break;
            }

            PickupItem heldItem = held.GetComponent<PickupItem>();
            string displayName = heldItem != null
                ? heldItem.DisplayName
                : FormatTarget(held.name);
            if (!MatchesHeldItem(held, itemName))
            {
                FailCurrentCommand($"I'm holding the {displayName}, not that item.");
                yield break;
            }

            SetState(AgentNpcState.Talking);
            Respond($"Okay, I'll drop the {displayName}.", true);
            CommandAccepted?.Invoke(originalCommand);
            if (acknowledgementDelay > 0f)
            {
                yield return new WaitForSeconds(acknowledgementDelay);
            }

            SetState(AgentNpcState.Interacting);
            yield return interaction.DropRoutine();
            if (!interaction.LastActionSucceeded)
            {
                FailCurrentCommand($"I couldn't drop the {displayName}.");
                yield break;
            }

            SetState(AgentNpcState.Talking);
            string success = $"I've dropped the {displayName}.";
            Respond(success, true);
            CommandCompleted?.Invoke(success);
            yield return new WaitForSeconds(0.2f);
            FinishCommand();
        }

        private IEnumerator ExecutePlaceRoutine(
            string originalCommand,
            string itemName,
            string destinationName)
        {
            PlaceSurface surface = FindPlaceSurface(destinationName);
            if (surface == null)
            {
                FailCurrentCommand("I couldn't find a place to put that.");
                yield break;
            }

            string surfaceName = surface.DisplayName;
            GameObject itemToPlace = ResolveItemForPlace(itemName);
            if (itemToPlace == null && !string.IsNullOrEmpty(itemName))
            {
                itemToPlace = FindTarget(itemName);
            }

            if (itemToPlace == null && interaction.HeldItem == null)
            {
                FailCurrentCommand("I'm not holding anything, and I don't know which box to place.");
                yield break;
            }

            PickupItem pickup = itemToPlace != null
                ? itemToPlace.GetComponent<PickupItem>()
                : null;
            string itemDisplay = pickup != null
                ? pickup.DisplayName
                : itemToPlace != null
                    ? FormatTarget(itemToPlace.name)
                    : "box";

            if (surface.Contains(itemToPlace))
            {
                string already = $"The {itemDisplay} is already on the {surfaceName}.";
                Respond(already, false);
                CommandCompleted?.Invoke(already);
                FinishCommand();
                yield break;
            }

            SetState(AgentNpcState.Talking);
            Respond($"Okay, I'll put the {itemDisplay} on the {surfaceName}.", true);
            CommandAccepted?.Invoke(originalCommand);
            if (acknowledgementDelay > 0f)
            {
                yield return new WaitForSeconds(acknowledgementDelay);
            }

            movement.BeginFacingOverride();
            if (interaction.HeldItem != itemToPlace)
            {
                if (itemToPlace == null)
                {
                    FailCurrentCommand("I couldn't find that box.");
                    yield break;
                }

                yield return MoveToAndWait(itemToPlace, itemDisplay);
                if (activeCommand == null)
                {
                    yield break;
                }

                SetState(AgentNpcState.Interacting);
                yield return interaction.FaceTarget(itemToPlace);
                yield return interaction.PickupRoutine(itemToPlace);
                if (!interaction.LastActionSucceeded)
                {
                    FailCurrentCommand(
                        $"I reached the {itemDisplay}, but I couldn't pick it up.");
                    yield break;
                }
            }

            yield return MoveToAndWait(surface.gameObject, surfaceName);
            if (activeCommand == null)
            {
                yield break;
            }

            SetState(AgentNpcState.Interacting);
            yield return interaction.PlaceRoutine(surface);
            if (!interaction.LastActionSucceeded)
            {
                FailCurrentCommand(
                    $"I reached the {surfaceName}, but I couldn't place the {itemDisplay}.");
                yield break;
            }

            movement.EndFacingOverride();
            SetState(AgentNpcState.Talking);
            string success = $"I've put the {itemDisplay} on the {surfaceName}.";
            Respond(success, true);
            CommandCompleted?.Invoke(success);
            yield return new WaitForSeconds(0.2f);
            FinishCommand();
        }

        private IEnumerator MoveToAndWait(GameObject target, string displayName)
        {
            SetState(AgentNpcState.Moving);
            if (!interaction.MoveToTarget(target))
            {
                FailCurrentCommand($"I can't find a path to the {displayName}.");
                yield break;
            }

            float deadline = Time.time + commandTimeout;
            while (movement.IsMoving && Time.time < deadline)
            {
                yield return null;
            }

            if (movement.IsMoving || !movement.HasReachedDestination)
            {
                movement.Stop();
                FailCurrentCommand($"I timed out on the way to the {displayName}.");
            }
        }

        private void FailCurrentCommand(string message)
        {
            movement?.Stop();
            movement?.EndFacingOverride();
            Respond(message, true);
            CommandFailed?.Invoke(message);
            CurrentTarget = null;
            activeCommand = null;
            SetState(AgentNpcState.Idle);
        }

        private void FinishCommand()
        {
            movement?.EndFacingOverride();
            CurrentTarget = null;
            activeCommand = null;
            SetState(AgentNpcState.Idle);
        }

        /// <summary>
        /// Speaks a line. <paramref name="isAcknowledgement"/> marks progress and success
        /// lines, which are suppressed when the language model already answered; failures and
        /// notices pass it as false and are always spoken.
        /// </summary>
        private void Respond(string message, bool isAcknowledgement)
        {
            bool speak = !isAcknowledgement || announceAcknowledgementsInternal;
            if (speak && dialogueController != null && dialogueController.IsDialogueActive)
            {
                dialogueController.ReceiveNpcResponse(message);
            }

            if (isAcknowledgement)
            {
                Debug.Log($"[AgentNPC] {message}", this);
            }
        }

        private GameObject ResolveItemForPlace(string itemName)
        {
            GameObject held = interaction != null ? interaction.HeldItem : null;
            if (string.IsNullOrEmpty(itemName))
            {
                return held;
            }

            if (MatchesHeldItem(held, itemName))
            {
                return held;
            }

            return null;
        }

        private static bool MatchesHeldItem(GameObject held, string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return true;
            }

            return held != null &&
                   held.name.Equals(itemName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParse(
            string normalized,
            out CommandKind kind,
            out string itemName,
            out string destinationName)
        {
            kind = CommandKind.Move;
            itemName = DetectBox(normalized);
            destinationName = DetectDestination(normalized);

            bool hasPlaceVerb = ContainsAny(
                normalized,
                "puton",
                "placeon",
                "onto",
                "ontothe",
                "ontheobstacle",
                "ontheblock");
            bool mentionsPutOrPlace = ContainsAny(normalized, "put", "place", "set");
            bool mentionsDrop = ContainsAny(normalized, "drop", "putdown", "setdown", "letgo", "release");

            if (destinationName != null &&
                (hasPlaceVerb || mentionsPutOrPlace || mentionsDrop))
            {
                kind = CommandKind.Place;
                return true;
            }

            if (mentionsDrop)
            {
                kind = CommandKind.Drop;
                return true;
            }

            if (ContainsAny(normalized, "jump", "跳"))
            {
                kind = CommandKind.Jump;
                return true;
            }

            if (itemName == null && destinationName == null)
            {
                return false;
            }

            if (itemName != null &&
                (ContainsAny(
                     normalized,
                     "pickup",
                     "pick",
                     "grab",
                     "take",
                     "collect",
                     "拾取",
                     "拿起") ||
                 normalized.StartsWith("拿")))
            {
                kind = CommandKind.Pickup;
                return true;
            }

            if (ContainsAny(
                    normalized,
                    "walk",
                    "move",
                    "approach",
                    "goto",
                    "headto",
                    "走到",
                    "前往") ||
                normalized.StartsWith("go") ||
                normalized.StartsWith("come") ||
                normalized.StartsWith("去"))
            {
                kind = CommandKind.Move;
                if (itemName == null)
                {
                    itemName = destinationName;
                }

                return true;
            }

            return false;
        }

        private static string DetectBox(string normalized)
        {
            if (ContainsAny(normalized, "redbox", "redcrate", "红箱子", "红色箱子"))
            {
                return "RedBox";
            }

            if (ContainsAny(normalized, "bluebox", "bluecrate", "蓝箱子", "蓝色箱子"))
            {
                return "BlueBox";
            }

            if (ContainsAny(normalized, "greenbox", "greencrate", "绿箱子", "绿色箱子"))
            {
                return "GreenBox";
            }

            return null;
        }

        private static string DetectDestination(string normalized)
        {
            if (ContainsAny(
                    normalized,
                    "navigationobstacle",
                    "obstacle",
                    "theblock",
                    "greyblock",
                    "grayblock",
                    "thecube",
                    "pedestal"))
            {
                return ObstacleObjectName;
            }

            return null;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (text.Contains(tokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatTarget(string targetName)
        {
            switch (targetName)
            {
                case "RedBox":
                    return "red box";
                case "BlueBox":
                    return "blue box";
                case "GreenBox":
                    return "green box";
                case ObstacleObjectName:
                    return "obstacle";
                default:
                    return string.IsNullOrWhiteSpace(targetName)
                        ? "target"
                        : targetName;
            }
        }

        private static string Normalize(string command)
        {
            return string.IsNullOrWhiteSpace(command)
                ? string.Empty
                : command.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        }

        private static GameObject FindTarget(string targetName)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            PickupItem[] items = UnityEngine.Object.FindObjectsByType<PickupItem>(
                FindObjectsSortMode.None);
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].gameObject.name.Equals(
                    targetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return items[i].gameObject;
                }
            }

            return GameObject.Find(targetName);
        }

        private static PlaceSurface FindPlaceSurface(string destinationName)
        {
            PlaceSurface[] surfaces = UnityEngine.Object.FindObjectsByType<PlaceSurface>(
                FindObjectsSortMode.None);
            if (surfaces.Length == 1)
            {
                return surfaces[0];
            }

            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i].gameObject.name.Equals(
                    destinationName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return surfaces[i];
                }
            }

            GameObject named = GameObject.Find(ObstacleObjectName);
            return named != null ? named.GetComponent<PlaceSurface>() : null;
        }

        private static void EnsurePlaceSurface()
        {
            GameObject obstacle = GameObject.Find(ObstacleObjectName);
            if (obstacle == null)
            {
                return;
            }

            PlaceSurface surface = obstacle.GetComponent<PlaceSurface>();
            if (surface == null)
            {
                surface = obstacle.AddComponent<PlaceSurface>();
            }

            surface.Configure("obstacle");
        }

        private void SetState(AgentNpcState next)
        {
            if (CurrentState == next)
            {
                return;
            }

            AgentNpcState previous = CurrentState;
            CurrentState = next;
            Debug.Log($"[AgentNPC] State: {previous} -> {next}", this);
            StateChanged?.Invoke(previous, next);
        }

        private void ResolveReferences()
        {
            if (dialogueController == null)
            {
                dialogueController = GetComponent<DialogueController>();
            }

            if (movement == null)
            {
                movement = GetComponent<AgentMovement>();
            }

            if (interaction == null)
            {
                interaction = GetComponent<AgentInteraction>();
            }

            if (intentAnimator == null)
            {
                intentAnimator = GetComponent<MotionIntentAnimator>();
            }

            if (planFollower == null)
            {
                planFollower = GetComponent<TrajectoryPlanFollower>();
            }
        }

        private void OnValidate()
        {
            commandTimeout = Mathf.Max(2f, commandTimeout);
            acknowledgementDelay = Mathf.Max(0f, acknowledgementDelay);
            jumpTimeout = Mathf.Max(0.5f, jumpTimeout);
        }
    }
}
