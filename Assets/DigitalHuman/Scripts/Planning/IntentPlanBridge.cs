using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Turns a locomotion intent tagged on a model reply into an actual plan request.
    /// </summary>
    /// <remarks>
    /// <see cref="ResponseAnnotationDriver"/> deliberately refuses to act on <c>walk</c> and
    /// <c>turn</c>, because the planner owns the root transform and an animation parameter
    /// alone would make the character mime walking on the spot. This is the one component that
    /// closes that gap, and it is the whole chain in miniature: player utterance to language
    /// model to bounded intent tag to plan request to root motion.
    ///
    /// Obstacles are read from the scene each time a plan is requested rather than cached, so
    /// the boxes the agent can pick up and put down are avoided wherever they currently are.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class IntentPlanBridge : MonoBehaviour
    {
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private TrajectoryPlanFollower follower;
        [Tooltip("Falls back to the LookAtController's current target, normally the player.")]
        [SerializeField] private Transform approachTarget;
        [SerializeField] private LookAtController lookAtController;

        [Header("Goal")]
        [Tooltip("How far from the target to stop. Personal space, roughly speaking.")]
        [SerializeField, Min(0.1f)] private float stopDistance = 1.6f;
        [Tooltip("Ignore a walk intent when already this close to the target.")]
        [SerializeField, Min(0.1f)] private float alreadyArrivedDistance = 2f;

        [Header("Obstacles")]
        [Tooltip("Radius reported for each pickup item the planner has to route around.")]
        [SerializeField, Min(0.05f)] private float pickupObstacleRadius = 0.45f;
        [Tooltip("Only obstacles within this range of the character are sent.")]
        [SerializeField, Min(1f)] private float obstacleSearchRadius = 12f;

        private readonly List<PlanObstacle> obstacles = new List<PlanObstacle>();

        public int LastObstacleCount { get; private set; }

        private void Awake()
        {
            if (dialogueController == null)
            {
                dialogueController = GetComponent<DialogueController>();
            }

            if (follower == null)
            {
                follower = GetComponent<TrajectoryPlanFollower>();
            }

            if (lookAtController == null)
            {
                lookAtController = GetComponent<LookAtController>();
            }
        }

        private void OnEnable()
        {
            if (dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived += HandleAnnotation;
            }
        }

        private void OnDisable()
        {
            if (dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived -= HandleAnnotation;
            }
        }

        public void Configure(
            DialogueController controller,
            TrajectoryPlanFollower planFollower,
            LookAtController lookAt,
            Transform target)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled && dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived -= HandleAnnotation;
            }

            dialogueController = controller;
            follower = planFollower;
            lookAtController = lookAt;
            approachTarget = target;

            if (wasEnabled && dialogueController != null)
            {
                dialogueController.ResponseAnnotationReceived += HandleAnnotation;
            }
        }

        private void HandleAnnotation(ResponseAnnotation annotation)
        {
            if (!annotation.HasIntent || follower == null)
            {
                return;
            }

            switch (annotation.Intent)
            {
                case MotionIntent.Walk:
                    RequestApproach(annotation.Intent);
                    break;
                case MotionIntent.Turn:
                    RequestTurnToFaceTarget();
                    break;
                case MotionIntent.Stop:
                    follower.StopFollowing();
                    break;
            }
        }

        private void RequestApproach(MotionIntent intent)
        {
            Transform target = ResolveTarget();
            if (target == null)
            {
                return;
            }

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= alreadyArrivedDistance)
            {
                // Close enough that walking would just be shuffling. Turn to face instead.
                RequestTurnToFaceTarget();
                return;
            }

            float yaw = Mathf.Atan2(toTarget.x, toTarget.z);
            PlanGoal goal = new PlanGoal(
                intent, target.position, yaw, stopDistance, "walk over to the player");
            follower.BeginPlanTo(goal, CollectObstacles(target));
        }

        private void RequestTurnToFaceTarget()
        {
            Transform target = ResolveTarget();
            if (target == null)
            {
                return;
            }

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f)
            {
                return;
            }

            float yaw = Mathf.Atan2(toTarget.x, toTarget.z);

            // Goal position is where the character already is: turn in place, do not translate.
            PlanGoal goal = new PlanGoal(
                MotionIntent.Turn, transform.position, yaw, 0f, "turn to face the player");
            follower.BeginPlanTo(goal, CollectObstacles(target));
        }

        private IReadOnlyList<PlanObstacle> CollectObstacles(Transform target)
        {
            obstacles.Clear();
            PickupItem[] items = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);
            float searchSquared = obstacleSearchRadius * obstacleSearchRadius;

            for (int i = 0; i < items.Length; i++)
            {
                PickupItem item = items[i];
                if (item == null || !item.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // A held item moves with the character and is not something to route around.
                if (item.transform.IsChildOf(transform))
                {
                    continue;
                }

                Vector3 offset = item.transform.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > searchSquared)
                {
                    continue;
                }

                obstacles.Add(new PlanObstacle(item.transform.position, pickupObstacleRadius));
            }

            LastObstacleCount = obstacles.Count;
            return obstacles;
        }

        private Transform ResolveTarget()
        {
            if (approachTarget != null)
            {
                return approachTarget;
            }

            return lookAtController != null ? lookAtController.CurrentTarget : null;
        }

        private void OnValidate()
        {
            alreadyArrivedDistance = Mathf.Max(stopDistance, alreadyArrivedDistance);
        }
    }
}
