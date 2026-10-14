using System.Collections;
using System.Collections.Generic;
using System.Text;
using DigitalHuman.Planning;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigitalHuman
{
    /// <summary>
    /// Plays a scripted conversation through the real chat box. Each line is typed into the
    /// input field and sent the way a player would send it, so every reply, tag and action on
    /// screen comes from the normal pipeline. Made for recording repeatable takes.
    /// </summary>
    /// <remarks>
    /// The script format is described on <see cref="ChatDemoScript"/>. After each line the
    /// director waits for the reply, then for the body to come to rest: no command running,
    /// no plan being followed, no NavMesh route, no gesture and no speech.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ChatDemoDirector : MonoBehaviour
    {
        private const float SettleQuietSeconds = 0.4f;
        private const float StartGraceSeconds = 0.3f;
        private const float PlayKeyDelay = 0.5f;

        [Header("Script")]
        [SerializeField] private TextAsset script;
        [SerializeField] private Key playKey = Key.F5;
        [SerializeField] private Key stopKey = Key.F6;
        [SerializeField] private bool playOnStart;
        [SerializeField, Min(0f)] private float playOnStartDelay = 2f;

        [Header("Scene")]
        [SerializeField] private DialogueUI chat;
        [SerializeField] private DialogueController npcDialogue;
        [SerializeField] private ThirdPersonFollowCamera followCamera;
        [SerializeField] private Transform player;

        [Header("Pacing")]
        [SerializeField, Range(2f, 60f)] private float charactersPerSecond = 16f;
        [Tooltip("Pause between a line being fully typed and being sent, so a viewer can read it.")]
        [SerializeField, Min(0f)] private float pauseBeforeSend = 0.5f;
        [Tooltip("Pause after each action finishes before the next line is typed.")]
        [SerializeField, Min(0f)] private float holdAfterAction = 1.2f;
        [Tooltip("Longest wait for a reply or an action before the script moves on anyway.")]
        [SerializeField, Min(1f)] private float stepTimeout = 45f;

        [Header("Demo only")]
        [Tooltip("While the script runs, 'come over here' walks to the player from anywhere. " +
                 "Otherwise EVA only approaches a player who is inside her interaction range.")]
        [SerializeField] private bool approachPlayerFromAnywhere = true;

        private AgentCommandController commands;
        private AgentMovement movement;
        private TrajectoryPlanFollower planFollower;
        private IntentPlanBridge planBridge;
        private MotionIntentAnimator intentAnimator;
        private VoiceController voice;
        private Coroutine run;
        private bool approachOverridden;
        private Transform approachTargetBeforeRun;

        public bool IsRunning => run != null;
        public string CurrentStep { get; private set; } = string.Empty;
        public int StepsCompleted { get; private set; }
        public int StepCount { get; private set; }

        private void Start()
        {
            if (playOnStart)
            {
                Play(playOnStartDelay);
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (!IsRunning && WasPressed(keyboard, playKey))
            {
                Play(PlayKeyDelay);
            }
            else if (IsRunning && WasPressed(keyboard, stopKey))
            {
                Stop();
            }
        }

        public void Configure(
            TextAsset demoScript,
            DialogueUI chatUi,
            DialogueController dialogue,
            ThirdPersonFollowCamera camera,
            Transform playerTransform)
        {
            script = demoScript;
            chat = chatUi;
            npcDialogue = dialogue;
            followCamera = camera;
            player = playerTransform;
        }

        public bool Play(float delay = 0f)
        {
            if (IsRunning)
            {
                return false;
            }

            ResolveReferences();
            if (script == null || chat == null || npcDialogue == null)
            {
                Debug.LogWarning(
                    "[Demo] Needs a script, the chat UI and EVA's DialogueController.", this);
                return false;
            }

            List<string> errors = new List<string>();
            List<DemoStep> steps = ChatDemoScript.Parse(script.text, errors);
            foreach (string error in errors)
            {
                Debug.LogWarning($"[Demo] {script.name}, {error}. Skipped.", this);
            }

            run = StartCoroutine(Run(steps, delay));
            return true;
        }

        public void Stop()
        {
            if (run != null)
            {
                StopCoroutine(run);
                run = null;
                CurrentStep = "stopped";
                Debug.Log("[Demo] Stopped.", this);
            }

            RestoreApproachTarget();
        }

        private IEnumerator Run(List<DemoStep> steps, float delay)
        {
            StepCount = steps.Count;
            StepsCompleted = 0;
            CurrentStep = "starting";

            // Always yield once: a run that finished inside StartCoroutine would clear `run`
            // before Play assigned it, and IsRunning would stay true.
            yield return delay > 0f ? new WaitForSeconds(delay) : null;

            Debug.Log(
                $"[Demo] Playing '{script.name}', {steps.Count} steps. {stopKey} stops it.", this);
            OverrideApproachTarget();
            if (!npcDialogue.IsDialogueActive)
            {
                npcDialogue.StartDialogue();
            }

            float hold = holdAfterAction;
            float typingSpeed = charactersPerSecond;
            bool waitForAction = true;
            for (int i = 0; i < steps.Count; i++)
            {
                DemoStep step = steps[i];
                CurrentStep = step.ToString();
                switch (step.Kind)
                {
                    case DemoStepKind.Say:
                        yield return Send(step.Text, typingSpeed);
                        if (waitForAction)
                        {
                            yield return WaitForNpcToSettle(step);
                            if (hold > 0f)
                            {
                                yield return new WaitForSeconds(hold);
                            }
                        }

                        waitForAction = true;
                        break;
                    case DemoStepKind.Wait:
                        yield return new WaitForSeconds(step.Value);
                        break;
                    case DemoStepKind.Camera:
                        MoveCamera(step);
                        break;
                    case DemoStepKind.Hold:
                        hold = step.Value;
                        break;
                    case DemoStepKind.Typing:
                        typingSpeed = step.Value;
                        break;
                    case DemoStepKind.NoWait:
                        waitForAction = false;
                        break;
                }

                StepsCompleted = i + 1;
            }

            RestoreApproachTarget();
            CurrentStep = "finished";
            run = null;
            Debug.Log("[Demo] Finished.", this);
        }

        private IEnumerator Send(string line, float typingSpeed)
        {
            yield return WaitFor(CanType, "the chat box to accept input");

            TMP_InputField input = chat.PlayerInputField;
            chat.FocusInput();
            yield return null;

            StringBuilder typed = new StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                typed.Append(line[i]);
                input.text = typed.ToString();
                input.MoveTextEnd(false);
                yield return new WaitForSeconds(KeystrokeDelay(line[i], typingSpeed));
            }

            if (pauseBeforeSend > 0f)
            {
                yield return new WaitForSeconds(pauseBeforeSend);
            }

            chat.RequestSubmit();
            yield return WaitFor(() => !npcDialogue.IsBusy, "EVA's reply");
        }

        private bool CanType()
        {
            TMP_InputField input = chat.PlayerInputField;
            return npcDialogue.IsDialogueActive && !npcDialogue.IsBusy &&
                   input != null && input.interactable;
        }

        private IEnumerator WaitForNpcToSettle(DemoStep step)
        {
            yield return new WaitForSeconds(StartGraceSeconds);
            float deadline = Time.time + stepTimeout;
            float quietSince = Time.time;
            while (Time.time < deadline)
            {
                if (IsNpcBusy())
                {
                    quietSince = Time.time;
                }
                else if (Time.time - quietSince >= SettleQuietSeconds)
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning(
                $"[Demo] {step} was still playing after {stepTimeout:0} s; moving on.", this);
        }

        private bool IsNpcBusy()
        {
            return npcDialogue.IsBusy ||
                   (commands != null && commands.IsExecutingCommand) ||
                   (planFollower != null && planFollower.IsFollowing) ||
                   (movement != null && movement.IsMoving) ||
                   (intentAnimator != null && intentAnimator.IsPlayingGesture) ||
                   (voice != null && voice.IsSpeaking);
        }

        private IEnumerator WaitFor(System.Func<bool> condition, string what)
        {
            float deadline = Time.time + stepTimeout;
            while (!condition())
            {
                if (Time.time > deadline)
                {
                    Debug.LogWarning(
                        $"[Demo] Gave up waiting for {what} after {stepTimeout:0} s.", this);
                    yield break;
                }

                yield return null;
            }
        }

        private void MoveCamera(DemoStep step)
        {
            if (followCamera == null)
            {
                return;
            }

            switch (step.Shot)
            {
                case CameraShot.FollowPlayer:
                    followCamera.FollowPlayer();
                    break;
                case CameraShot.FollowNpc:
                    followCamera.FollowNpc(step.Angle);
                    break;
                default:
                    followCamera.ShowOverview();
                    break;
            }
        }

        private void OverrideApproachTarget()
        {
            if (!approachPlayerFromAnywhere || approachOverridden ||
                planBridge == null || player == null)
            {
                return;
            }

            approachTargetBeforeRun = planBridge.ApproachTarget;
            planBridge.SetApproachTarget(player);
            approachOverridden = true;
        }

        private void RestoreApproachTarget()
        {
            if (!approachOverridden)
            {
                return;
            }

            if (planBridge != null)
            {
                planBridge.SetApproachTarget(approachTargetBeforeRun);
            }

            approachTargetBeforeRun = null;
            approachOverridden = false;
        }

        private void ResolveReferences()
        {
            if (chat == null)
            {
                chat = FindFirstObjectByType<DialogueUI>();
            }

            if (npcDialogue == null)
            {
                npcDialogue = FindFirstObjectByType<DialogueController>();
            }

            if (followCamera == null)
            {
                followCamera = FindFirstObjectByType<ThirdPersonFollowCamera>();
            }

            if (player == null)
            {
                DemoPlayerController playerController = FindFirstObjectByType<DemoPlayerController>();
                if (playerController != null)
                {
                    player = playerController.transform;
                }
            }

            if (npcDialogue == null)
            {
                return;
            }

            GameObject npc = npcDialogue.gameObject;
            commands = npc.GetComponent<AgentCommandController>();
            movement = npc.GetComponent<AgentMovement>();
            planFollower = npc.GetComponent<TrajectoryPlanFollower>();
            planBridge = npc.GetComponent<IntentPlanBridge>();
            intentAnimator = npc.GetComponent<MotionIntentAnimator>();
            voice = npc.GetComponent<VoiceController>();
        }

        private static float KeystrokeDelay(char typed, float charactersPerSecond)
        {
            float delay = Random.Range(0.7f, 1.3f) / charactersPerSecond;
            if (typed == ' ')
            {
                return delay * 1.3f;
            }

            return IsSentencePunctuation(typed) ? delay * 3f : delay;
        }

        private static bool IsSentencePunctuation(char character)
        {
            switch (character)
            {
                case '.':
                case ',':
                case '!':
                case '?':
                case '，':
                case '。':
                case '！':
                case '？':
                    return true;
                default:
                    return false;
            }
        }

        private static bool WasPressed(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        private void OnValidate()
        {
            charactersPerSecond = Mathf.Max(2f, charactersPerSecond);
            stepTimeout = Mathf.Max(1f, stepTimeout);
        }
    }
}
