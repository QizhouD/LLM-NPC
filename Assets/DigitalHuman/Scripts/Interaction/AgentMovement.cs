using System;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AgentMovement : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent navMeshAgent;
        [SerializeField] private LookAtController facePlayerController;
        [SerializeField, Min(0.05f)] private float arrivalTolerance = 0.12f;
        [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2.5f;

        private const float PreciseArrivalTolerance = 0.04f;

        private bool facingOverrideActive;
        private bool restoreFacePlayer;
        private float defaultStoppingDistance = -1f;
        private float activeArrivalTolerance = -1f;

        public NavMeshAgent NavMeshAgent => navMeshAgent;
        public bool IsMoving { get; private set; }
        public bool HasReachedDestination { get; private set; }
        public Vector3 RequestedDestination { get; private set; }
        public Vector3 ResolvedDestination { get; private set; }

        public event Action<Vector3> DestinationReached;
        public event Action<string> MovementFailed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            Stop();
            EndFacingOverride();
        }

        private void Update()
        {
            if (!IsMoving || navMeshAgent == null || !navMeshAgent.isOnNavMesh)
            {
                return;
            }

            if (navMeshAgent.pathPending)
            {
                return;
            }

            if (navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                FailMovement("NavMesh could not calculate a valid path.");
                return;
            }

            float tolerance = activeArrivalTolerance > 0f ? activeArrivalTolerance : arrivalTolerance;
            float threshold = Mathf.Max(
                navMeshAgent.stoppingDistance + tolerance,
                tolerance);
            if (navMeshAgent.remainingDistance > threshold ||
                navMeshAgent.velocity.sqrMagnitude > 0.01f)
            {
                return;
            }

            IsMoving = false;
            HasReachedDestination = true;
            navMeshAgent.isStopped = true;
            RestoreStoppingDistance();
            DestinationReached?.Invoke(transform.position);
            Debug.Log(
                $"[AgentNPC] Destination reached at {transform.position}.",
                this);
        }

        public void Configure(
            NavMeshAgent agent,
            LookAtController lookAtController,
            float tolerance = 0.12f,
            float sampleRadius = 2.5f)
        {
            navMeshAgent = agent;
            facePlayerController = lookAtController;
            arrivalTolerance = Mathf.Max(0.05f, tolerance);
            navMeshSampleRadius = Mathf.Max(0.1f, sampleRadius);
        }

        public bool MoveTo(Vector3 destination)
        {
            return MoveTo(destination, -1f);
        }

        /// <summary>
        /// Walks to <paramref name="destination"/> and stops within
        /// <paramref name="stoppingDistance"/> of it, or within the agent's own stopping
        /// distance when that is negative. The agent's value is tuned for keeping a polite
        /// distance from the player, which leaves the body too far away to reach an object.
        /// </summary>
        public bool MoveTo(Vector3 destination, float stoppingDistance)
        {
            ResolveReferences();
            RequestedDestination = destination;
            HasReachedDestination = false;

            if (navMeshAgent == null)
            {
                return FailMovement("NavMeshAgent is missing.");
            }

            RestoreStoppingDistance();
            if (stoppingDistance >= 0f)
            {
                defaultStoppingDistance = navMeshAgent.stoppingDistance;
                navMeshAgent.stoppingDistance = stoppingDistance;
                activeArrivalTolerance = PreciseArrivalTolerance;
            }

            if (!EnsurePlacedOnNavMesh())
            {
                return FailMovement("NPC is not positioned on a NavMesh.");
            }

            if (!NavMesh.SamplePosition(
                    destination,
                    out NavMeshHit destinationHit,
                    navMeshSampleRadius,
                    navMeshAgent.areaMask))
            {
                return FailMovement(
                    $"No NavMesh position was found near {destination}.");
            }

            ResolvedDestination = destinationHit.position;
            navMeshAgent.isStopped = false;
            navMeshAgent.updateRotation = true;
            if (!navMeshAgent.SetDestination(ResolvedDestination))
            {
                return FailMovement("NavMeshAgent rejected the destination.");
            }

            IsMoving = true;
            Debug.Log(
                $"[AgentNPC] MoveTo requested: {ResolvedDestination}.",
                this);
            return true;
        }

        public void Stop()
        {
            IsMoving = false;
            RestoreStoppingDistance();
            if (navMeshAgent == null || !navMeshAgent.isOnNavMesh)
            {
                return;
            }

            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }

        public void BeginFacingOverride()
        {
            if (facingOverrideActive)
            {
                return;
            }

            facingOverrideActive = true;
            restoreFacePlayer = facePlayerController != null &&
                                facePlayerController.enabled;
            if (facePlayerController != null)
            {
                facePlayerController.enabled = false;
            }
        }

        public void EndFacingOverride()
        {
            if (!facingOverrideActive)
            {
                return;
            }

            facingOverrideActive = false;
            if (facePlayerController != null)
            {
                facePlayerController.enabled = restoreFacePlayer;
            }
        }

        private bool EnsurePlacedOnNavMesh()
        {
            if (navMeshAgent.isOnNavMesh)
            {
                return true;
            }

            if (!NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit hit,
                    navMeshSampleRadius,
                    navMeshAgent.areaMask))
            {
                return false;
            }

            return navMeshAgent.Warp(hit.position);
        }

        private void RestoreStoppingDistance()
        {
            if (defaultStoppingDistance >= 0f && navMeshAgent != null)
            {
                navMeshAgent.stoppingDistance = defaultStoppingDistance;
            }

            defaultStoppingDistance = -1f;
            activeArrivalTolerance = -1f;
        }

        private bool FailMovement(string reason)
        {
            IsMoving = false;
            HasReachedDestination = false;
            RestoreStoppingDistance();
            MovementFailed?.Invoke(reason);
            Debug.LogError($"[AgentNPC] Movement failed: {reason}", this);
            return false;
        }

        private void ResolveReferences()
        {
            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }

            if (facePlayerController == null)
            {
                facePlayerController = GetComponent<LookAtController>();
            }
        }

        private void OnValidate()
        {
            arrivalTolerance = Mathf.Max(0.05f, arrivalTolerance);
            navMeshSampleRadius = Mathf.Max(0.1f, navMeshSampleRadius);
        }
    }
}
