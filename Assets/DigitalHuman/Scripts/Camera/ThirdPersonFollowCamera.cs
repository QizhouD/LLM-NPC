using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DigitalHuman
{
    public enum CameraShot
    {
        FollowPlayer,
        FollowNpc,
        Overview
    }

    /// <summary>Which side of the digital human the follow shot sits on.</summary>
    public enum NpcCameraAngle
    {
        Front,
        Side,
        Back,
        /// <summary>Wherever the user last orbited to. Does not swing round when she turns.</summary>
        Free
    }

    /// <summary>A camera placement expressed as the point it orbits and how it orbits it.</summary>
    /// <remarks>Serializable so the camera keeps its poses through a script reload in Play mode.</remarks>
    [System.Serializable]
    public struct OrbitPose
    {
        public Vector3 Pivot;
        public float Yaw;
        public float Pitch;
        public float Distance;

        public OrbitPose(Vector3 pivot, float yaw, float pitch, float distance)
        {
            Pivot = pivot;
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
        }

        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);

        public Vector3 Position => Pivot - Rotation * Vector3.forward * Distance;

        /// <summary>
        /// Re-expresses a fixed camera placement as an orbit around the point where its view
        /// meets the ground, so a fixed shot can blend with the follow shots. Roll is dropped.
        /// </summary>
        public static OrbitPose FromPlacement(
            Vector3 position,
            Quaternion rotation,
            float groundHeight,
            float fallbackDistance = 15f)
        {
            Vector3 forward = rotation * Vector3.forward;
            float distance = forward.y < -0.05f
                ? (groundHeight - position.y) / forward.y
                : fallbackDistance;
            if (distance <= 0f)
            {
                distance = fallbackDistance;
            }

            Vector3 euler = rotation.eulerAngles;
            return new OrbitPose(
                position + forward * distance,
                euler.y,
                Mathf.DeltaAngle(0f, euler.x),
                distance);
        }
    }

    /// <summary>
    /// Third-person camera that follows the player or the digital human. The placement the
    /// camera has in the scene becomes a fixed overview shot.
    /// </summary>
    /// <remarks>
    /// Every shot is an <see cref="OrbitPose"/>. Changing shot only changes the goal pose and
    /// each number glides there, so a change of shot is a camera move rather than a cut.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class ThirdPersonFollowCamera : MonoBehaviour
    {
        private const float MinimumClearance = 0.4f;

        [Header("Targets")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform npc;
        [SerializeField] private CameraShot startShot = CameraShot.FollowPlayer;

        [Header("Player shot")]
        [Tooltip("Orbit point above the player's origin, which is the middle of the capsule.")]
        [SerializeField] private float playerPivotHeight = 0.55f;
        [Tooltip("Moves the player left of centre so the view ahead stays open.")]
        [SerializeField] private float playerShoulderOffset = 0.5f;
        [SerializeField, Min(0.5f)] private float playerDistance = 4.5f;
        [SerializeField, Range(-10f, 80f)] private float playerPitch = 14f;

        [Header("Digital human shot")]
        [Tooltip("Orbit point above her feet. Kept below the waist so her whole body clears " +
                 "the chat panel along the bottom of the screen.")]
        [SerializeField] private float npcPivotHeight = 0.9f;
        [SerializeField, Min(0.5f)] private float npcDistance = 3.8f;
        [SerializeField, Range(-10f, 80f)] private float npcPitch = 6f;
        [SerializeField] private NpcCameraAngle npcAngle = NpcCameraAngle.Front;
        [Tooltip("How far the front shot sits off her face, so it reads as three-quarter.")]
        [SerializeField, Range(0f, 90f)] private float frontShotOffset = 30f;
        [Tooltip("Seconds the camera takes to swing round after she turns.")]
        [SerializeField, Min(0.05f)] private float angleFollowTime = 0.6f;

        [Header("Smoothing")]
        [SerializeField, Min(0f)] private float pivotFollowTime = 0.12f;
        [SerializeField, Min(0f)] private float orbitFollowTime = 0.08f;
        [Tooltip("Seconds a change of shot takes to settle.")]
        [SerializeField, Min(0.1f)] private float shotChangeTime = 1f;
        [Tooltip("Seconds to ease back out once an obstruction clears. Pulling in is instant.")]
        [SerializeField, Min(0f)] private float collisionRecoverTime = 0.35f;

        [Header("Input")]
        [SerializeField] private Key followPlayerKey = Key.F1;
        [SerializeField] private Key followNpcKey = Key.F2;
        [SerializeField] private Key overviewKey = Key.F3;
        [SerializeField, Min(0f)] private float orbitDegreesPerPixel = 0.2f;
        [SerializeField, Range(0.01f, 0.5f)] private float zoomStep = 0.12f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-10f, 80f);
        [SerializeField] private Vector2 distanceLimits = new Vector2(1.2f, 40f);

        [Header("Collision")]
        [SerializeField] private LayerMask obstructionMask = ~0;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        private CameraShot shot;
        private OrbitPose overview;
        private OrbitPose playerOrbit;
        private OrbitPose npcOrbit;
        private float npcFramingYaw;
        private float npcFramingYawVelocity;

        private OrbitPose current;
        private Vector3 pivotVelocity;
        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;
        private float side;
        private float sideVelocity;
        private float shotChange;
        private float clearDistance = float.MaxValue;
        private float clearDistanceVelocity;
        private bool orbiting;

        public CameraShot Shot => shot;
        public NpcCameraAngle NpcAngle => npcAngle;
        public Transform Player => player;
        public Transform Npc => npc;

        private void Awake()
        {
            float ground = npc != null ? npc.position.y : 0f;
            overview = OrbitPose.FromPlacement(transform.position, transform.rotation, ground);
            playerOrbit = new OrbitPose(Vector3.zero, YawTowardsNpc(), playerPitch, playerDistance);
            npcOrbit = new OrbitPose(Vector3.zero, 0f, npcPitch, npcDistance);
            if (npc != null)
            {
                npcFramingYaw = YawFor(npcAngle, npc.eulerAngles.y, frontShotOffset);
            }

            shot = startShot;
            current = GoalPose();
            side = GoalSide();
        }

        public void Configure(Transform playerTarget, Transform npcTarget)
        {
            player = playerTarget;
            npc = npcTarget;
        }

        public void FollowPlayer()
        {
            ChangeShot(CameraShot.FollowPlayer);
        }

        public void FollowNpc(NpcCameraAngle angle)
        {
            if (angle == NpcCameraAngle.Free && npcAngle != NpcCameraAngle.Free)
            {
                npcOrbit.Yaw = current.Yaw;
            }

            npcAngle = angle;
            ChangeShot(CameraShot.FollowNpc);
        }

        public void ShowOverview()
        {
            ChangeShot(CameraShot.Overview);
        }

        /// <summary>
        /// Camera yaw that frames a character facing <paramref name="facingYaw"/> from the
        /// given side. <see cref="NpcCameraAngle.Free"/> is treated as from behind.
        /// </summary>
        public static float YawFor(NpcCameraAngle angle, float facingYaw, float frontOffset)
        {
            switch (angle)
            {
                case NpcCameraAngle.Front:
                    return facingYaw + 180f - frontOffset;
                case NpcCameraAngle.Side:
                    return facingYaw + 90f;
                default:
                    return facingYaw;
            }
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            ReadInput();

            if (shot == CameraShot.FollowNpc && npc != null && npcAngle != NpcCameraAngle.Free)
            {
                float framing = YawFor(npcAngle, npc.eulerAngles.y, frontShotOffset);
                npcFramingYaw = Mathf.SmoothDampAngle(
                    npcFramingYaw, framing, ref npcFramingYawVelocity,
                    angleFollowTime, Mathf.Infinity, deltaTime);
            }

            OrbitPose goal = GoalPose();
            shotChange = Mathf.MoveTowards(shotChange, 0f, deltaTime / shotChangeTime);
            float glideTime = shotChangeTime * 0.4f;
            float pivotTime = Mathf.Lerp(pivotFollowTime, glideTime, shotChange);
            float orbitTime = Mathf.Lerp(orbitFollowTime, glideTime, shotChange);

            current.Pivot = Vector3.SmoothDamp(
                current.Pivot, goal.Pivot, ref pivotVelocity, pivotTime, Mathf.Infinity, deltaTime);
            current.Yaw = Mathf.SmoothDampAngle(
                current.Yaw, goal.Yaw, ref yawVelocity, orbitTime, Mathf.Infinity, deltaTime);
            current.Pitch = Mathf.SmoothDamp(
                current.Pitch, goal.Pitch, ref pitchVelocity, orbitTime, Mathf.Infinity, deltaTime);
            current.Distance = Mathf.SmoothDamp(
                current.Distance, goal.Distance, ref distanceVelocity, orbitTime, Mathf.Infinity, deltaTime);
            side = Mathf.SmoothDamp(
                side, GoalSide(), ref sideVelocity, orbitTime, Mathf.Infinity, deltaTime);

            Place(deltaTime);
        }

        private OrbitPose GoalPose()
        {
            if (shot == CameraShot.FollowPlayer && player != null)
            {
                return new OrbitPose(
                    player.position + Vector3.up * playerPivotHeight,
                    playerOrbit.Yaw,
                    playerOrbit.Pitch,
                    playerOrbit.Distance);
            }

            if (shot == CameraShot.FollowNpc && npc != null)
            {
                float yaw = npcAngle == NpcCameraAngle.Free ? npcOrbit.Yaw : npcFramingYaw;
                return new OrbitPose(
                    npc.position + Vector3.up * npcPivotHeight,
                    yaw,
                    npcOrbit.Pitch,
                    npcOrbit.Distance);
            }

            return overview;
        }

        private float GoalSide()
        {
            return shot == CameraShot.FollowPlayer && player != null ? playerShoulderOffset : 0f;
        }

        private void Place(float deltaTime)
        {
            Quaternion rotation = current.Rotation;
            Vector3 focus = current.Pivot + rotation * Vector3.right * side;
            Vector3 back = rotation * Vector3.back;
            float clear = shot == CameraShot.Overview
                ? current.Distance
                : ClearDistanceAlong(focus, back, current.Distance);

            // Pull in at once so nothing passes through the lens, then ease back out.
            if (clear < clearDistance)
            {
                clearDistance = clear;
                clearDistanceVelocity = 0f;
            }
            else
            {
                clearDistance = Mathf.SmoothDamp(
                    clearDistance, clear, ref clearDistanceVelocity,
                    collisionRecoverTime, Mathf.Infinity, deltaTime);
            }

            float distance = Mathf.Min(current.Distance, clearDistance);
            transform.SetPositionAndRotation(focus + back * distance, rotation);
        }

        private float ClearDistanceAlong(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.SphereCastNonAlloc(
                origin, collisionRadius, direction, hits, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];

                // Zero distance means the sphere started inside the collider, which is
                // usually the followed character's own capsule.
                if (hit.distance <= 0f || BelongsToTarget(hit.collider.transform))
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, hit.distance);
            }

            return Mathf.Max(MinimumClearance, nearest);
        }

        private bool BelongsToTarget(Transform hit)
        {
            return (player != null && hit.IsChildOf(player)) ||
                   (npc != null && hit.IsChildOf(npc));
        }

        private void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (WasPressed(keyboard, followPlayerKey))
                {
                    FollowPlayer();
                }
                else if (WasPressed(keyboard, followNpcKey))
                {
                    NpcCameraAngle angle = shot == CameraShot.FollowNpc
                        ? NextAngle(npcAngle)
                        : npcAngle == NpcCameraAngle.Free ? NpcCameraAngle.Front : npcAngle;
                    FollowNpc(angle);
                }
                else if (WasPressed(keyboard, overviewKey))
                {
                    ShowOverview();
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                orbiting = !IsPointerOverUi();
            }
            else if (!mouse.rightButton.isPressed)
            {
                orbiting = false;
            }

            if (orbiting)
            {
                Vector2 delta = mouse.delta.ReadValue() * orbitDegreesPerPixel;
                if (delta.sqrMagnitude > 0f)
                {
                    Orbit(delta.x, -delta.y);
                }
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !IsPointerOverUi())
            {
                Zoom(scroll > 0f ? -1f : 1f);
            }
        }

        private void Orbit(float yawDelta, float pitchDelta)
        {
            switch (shot)
            {
                case CameraShot.FollowPlayer:
                    playerOrbit.Yaw += yawDelta;
                    playerOrbit.Pitch = ClampPitch(playerOrbit.Pitch + pitchDelta);
                    break;
                case CameraShot.FollowNpc:
                    if (npcAngle != NpcCameraAngle.Free)
                    {
                        npcOrbit.Yaw = current.Yaw;
                        npcAngle = NpcCameraAngle.Free;
                    }

                    npcOrbit.Yaw += yawDelta;
                    npcOrbit.Pitch = ClampPitch(npcOrbit.Pitch + pitchDelta);
                    break;
                default:
                    overview.Yaw += yawDelta;
                    overview.Pitch = ClampPitch(overview.Pitch + pitchDelta);
                    break;
            }
        }

        private void Zoom(float direction)
        {
            float factor = 1f + zoomStep * direction;
            switch (shot)
            {
                case CameraShot.FollowPlayer:
                    playerOrbit.Distance = ClampDistance(playerOrbit.Distance * factor);
                    break;
                case CameraShot.FollowNpc:
                    npcOrbit.Distance = ClampDistance(npcOrbit.Distance * factor);
                    break;
                default:
                    overview.Distance = ClampDistance(overview.Distance * factor);
                    break;
            }
        }

        private void ChangeShot(CameraShot next)
        {
            if (next == shot)
            {
                return;
            }

            if (next == CameraShot.FollowNpc)
            {
                // Swing round from where the camera is now, not from a stale angle.
                npcFramingYaw = current.Yaw;
                npcFramingYawVelocity = 0f;
            }

            shot = next;
            shotChange = 1f;
        }

        private float YawTowardsNpc()
        {
            if (player == null)
            {
                return transform.eulerAngles.y;
            }

            Vector3 toNpc = npc != null ? npc.position - player.position : Vector3.zero;
            toNpc.y = 0f;
            return toNpc.sqrMagnitude > 1e-4f
                ? Mathf.Atan2(toNpc.x, toNpc.z) * Mathf.Rad2Deg
                : player.eulerAngles.y;
        }

        private float ClampPitch(float pitch)
        {
            return Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        }

        private float ClampDistance(float distance)
        {
            return Mathf.Clamp(distance, distanceLimits.x, distanceLimits.y);
        }

        private static NpcCameraAngle NextAngle(NpcCameraAngle angle)
        {
            switch (angle)
            {
                case NpcCameraAngle.Front:
                    return NpcCameraAngle.Side;
                case NpcCameraAngle.Side:
                    return NpcCameraAngle.Back;
                default:
                    return NpcCameraAngle.Front;
            }
        }

        private static bool WasPressed(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        private static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private void OnValidate()
        {
            pitchLimits.y = Mathf.Max(pitchLimits.x, pitchLimits.y);
            distanceLimits.x = Mathf.Max(0.5f, distanceLimits.x);
            distanceLimits.y = Mathf.Max(distanceLimits.x, distanceLimits.y);
        }
    }
}
