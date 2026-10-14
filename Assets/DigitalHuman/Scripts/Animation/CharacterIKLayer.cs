using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Last pass over the finished pose: keeps the feet on the floor, lowers the body into a
    /// crouch, and puts the hands on a held item or up in a wave.
    /// </summary>
    /// <remarks>
    /// Runs after the Animator and <see cref="ProceduralHumanoidAnimator"/> and edits their
    /// result rather than adding to it. Every bone it touches is put back the next frame
    /// before the Animator runs: a culled Animator writes nothing, and the procedural layer
    /// would then take this layer's output for a fresh base pose and add onto it.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CharacterIKLayer : MonoBehaviour
    {
        // Bone names of the Biped skeleton the Vexa rig is built on.
        private const string BonePrefix = "Base Human";
        private const string PelvisName = BonePrefix + "Pelvis";
        private static readonly string[] SpineNames =
        {
            BonePrefix + "Spine1", BonePrefix + "Spine2", BonePrefix + "Spine3"
        };

        private const float PelvisLeanShare = 0.35f;
        private const float MaxGroundShift = 0.1f;
        private const float GroundShiftSpeed = 0.5f;

        // A lowest sole up to PlantedLift above where the rig stands counts as standing and is
        // pulled onto the floor; from AirborneLift up, as in a jump, it is left alone.
        private const float PlantedLift = 0.02f;
        private const float AirborneLift = 0.05f;
        private const float ReachMargin = 0.97f;
        private const int CrouchSearchSteps = 20;

        private static readonly int WaveStateHash =
            Animator.StringToHash(MotionIntentAnimator.WaveStateName);

        [SerializeField] private Animator animator;

        [Header("Ground")]
        [Tooltip("Lower the skeleton onto the floor under the character and keep the soles above it.")]
        [SerializeField] private bool alignToGround = true;
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        [Header("Crouch")]
        [Tooltip("Metres the pelvis drops at a full crouch.")]
        [SerializeField, Range(0f, 0.7f)] private float crouchDrop = 0.48f;
        [Tooltip("Metres the hips move back at a full crouch, to keep the weight over the feet.")]
        [SerializeField, Range(0f, 0.3f)] private float crouchHipBack = 0.12f;
        [Tooltip("Degrees the body bends forward at a full crouch, split between pelvis and spine.")]
        [SerializeField, Range(0f, 90f)] private float crouchLean = 62f;
        [Tooltip("Degrees the body leans in when reaching up and forward, as onto a high surface.")]
        [SerializeField, Range(0f, 40f)] private float reachUpLean = 16f;

        [Header("Holding")]
        [Tooltip("Where on the item's sides the palms go, as a share of its half height above the centre.")]
        [SerializeField, Range(-1f, 1f)] private float gripHeight = 0.55f;
        [Tooltip("Degrees the fingers point down from horizontal along the item's sides.")]
        [SerializeField, Range(0f, 60f)] private float gripFingerDroop = 25f;

        [Header("Wave")]
        [Tooltip("Palm centre relative to the right shoulder: right, up, forward, in metres.")]
        [SerializeField] private Vector3 waveHandOffset = new Vector3(0.2f, 0.3f, 0.1f);
        [SerializeField, Min(0.1f)] private float waveFrequency = 1.8f;
        [Tooltip("Metres the hand travels to each side.")]
        [SerializeField, Range(0f, 0.2f)] private float waveSwing = 0.07f;
        [Tooltip("Degrees the hand tilts at each end of the swing.")]
        [SerializeField, Range(0f, 45f)] private float waveTilt = 18f;

        private sealed class Arm
        {
            public Transform Upper;
            public Transform Lower;
            public Transform[] Twist;
            public Transform Palm;
            public Vector3 FingerLocal;
            public Vector3 PalmNormalLocal;
            public Vector3 ContactLocal;
            public float Reach;
            public Vector3 ShoulderRest;
            public bool IsRight;
            public Quaternion[] Saved;
        }

        private sealed class Leg
        {
            public Transform Thigh;
            public Transform Calf;
            public Transform Foot;
            public Transform Toes;
            public Vector3 HeelLocal;
            public Vector3 ToeLocal;
            public bool IsRight;
        }

        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        private Transform pelvis;
        private Transform[] spine;
        private Arm rightArm;
        private Arm leftArm;
        private Leg rightLeg;
        private Leg leftLeg;
        private Vector3 pelvisRest;
        private float soleRest;

        // Not kept over a script reload in Play mode, so the bones are bound again afterwards.
        [System.NonSerialized] private bool bound;
        [System.NonSerialized] private bool bindFailed;

        private Transform[] posedBones;
        private Quaternion[] posedRotations;
        private Vector3 posedPelvisPosition;
        [System.NonSerialized] private bool hasPosed;

        private Transform carryAnchor;
        private Transform holdItem;
        private Vector3 rightGripLocal;
        private Vector3 leftGripLocal;
        private Vector3 rightFaceLocal;
        private Vector3 forwardLocal;
        private float groundShift;

        public bool IsReady => bound || Bind();
        public float WaveWeight { get; private set; }

        /// <summary>0 standing, 1 full crouch. Set by whoever is animating an action.</summary>
        public float CrouchAmount { get; set; }

        /// <summary>0 upright, 1 leaning in to reach up and forward.</summary>
        public float ReachUpAmount { get; set; }

        /// <summary>How far the hands have moved from the animated pose onto the held item.</summary>
        public float HoldWeight { get; set; }

        /// <summary>
        /// Unscaled child of the character root that carried items are parented to, so they
        /// keep their size and follow the body's height.
        /// </summary>
        public Transform CarryAnchor
        {
            get
            {
                if (carryAnchor == null)
                {
                    carryAnchor = new GameObject("CarryAnchor").transform;
                    carryAnchor.SetParent(transform, false);
                }

                return carryAnchor;
            }
        }

        private void Awake()
        {
            Bind();
        }

        private void OnDisable()
        {
            RestorePose();
            WaveWeight = 0f;
        }

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
            bound = false;
            bindFailed = false;
        }

        /// <summary>
        /// Starts tracking <paramref name="item"/> for the hands. The palms go on the pair of
        /// side faces that point most nearly left and right of the character right now.
        /// </summary>
        public void BeginHold(Transform item)
        {
            holdItem = item;
            if (item == null)
            {
                return;
            }

            Bounds bounds = LocalBounds(item);
            bool useX = Mathf.Abs(Vector3.Dot(item.right, transform.right)) >=
                        Mathf.Abs(Vector3.Dot(item.forward, transform.right));
            Vector3 lateral = useX ? Vector3.right : Vector3.forward;
            Vector3 depth = useX ? Vector3.forward : Vector3.right;
            if (Vector3.Dot(item.TransformDirection(lateral), transform.right) < 0f)
            {
                lateral = -lateral;
            }

            if (Vector3.Dot(item.TransformDirection(depth), transform.forward) < 0f)
            {
                depth = -depth;
            }

            float halfWidth = Mathf.Abs(Vector3.Dot(bounds.extents, lateral));
            Vector3 lift = Vector3.up * (bounds.extents.y * gripHeight);
            rightGripLocal = bounds.center + lateral * halfWidth + lift;
            leftGripLocal = bounds.center - lateral * halfWidth + lift;
            rightFaceLocal = lateral;
            forwardLocal = depth;
        }

        public void EndHold()
        {
            holdItem = null;
            HoldWeight = 0f;
        }

        /// <summary>
        /// Smallest crouch at which both palms can reach the held item where it is now.
        /// 1 when even a full crouch falls short.
        /// </summary>
        public float CrouchToReachHold()
        {
            if (holdItem == null || !IsReady)
            {
                return 0f;
            }

            Vector3 right = holdItem.TransformPoint(rightGripLocal);
            Vector3 left = holdItem.TransformPoint(leftGripLocal);
            for (int step = 0; step <= CrouchSearchSteps; step++)
            {
                float amount = step / (float)CrouchSearchSteps;
                if (CanReach(rightArm, right, amount) && CanReach(leftArm, left, amount))
                {
                    return amount;
                }
            }

            return 1f;
        }

        /// <summary>
        /// Points <paramref name="end"/> at <paramref name="target"/> by rotating
        /// <paramref name="upper"/> and <paramref name="lower"/>, bending towards
        /// <paramref name="pole"/>. An out-of-reach target straightens the limb towards it.
        /// </summary>
        public static void SolveTwoBone(
            Transform upper,
            Transform lower,
            Transform end,
            Vector3 target,
            Vector3 pole)
        {
            Vector3 a = upper.position;
            Vector3 b = lower.position;
            Vector3 c = end.position;
            float upperLength = Vector3.Distance(a, b);
            float lowerLength = Vector3.Distance(b, c);
            if (upperLength < 1e-5f || lowerLength < 1e-5f)
            {
                return;
            }

            Vector3 toTarget = target - a;
            float reach = Mathf.Clamp(
                toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 1e-4f,
                upperLength + lowerLength - 1e-4f);
            Vector3 aim = toTarget.sqrMagnitude > 1e-10f ? toTarget.normalized : (c - a).normalized;

            Vector3 bend = Vector3.ProjectOnPlane(pole - a, aim);
            if (bend.sqrMagnitude < 1e-10f)
            {
                bend = Vector3.ProjectOnPlane(b - a, aim);
            }

            if (bend.sqrMagnitude < 1e-10f)
            {
                bend = Vector3.ProjectOnPlane(Vector3.forward, aim);
            }

            if (bend.sqrMagnitude < 1e-10f)
            {
                bend = Vector3.ProjectOnPlane(Vector3.right, aim);
            }

            bend.Normalize();
            float along = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) /
                          (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 joint = a + aim * along + bend * height;
            Vector3 tip = a + aim * reach;

            upper.rotation = Quaternion.FromToRotation(b - a, joint - a) * upper.rotation;
            Vector3 jointNow = lower.position;
            lower.rotation = Quaternion.FromToRotation(end.position - jointNow, tip - jointNow) *
                             lower.rotation;
        }

        private void Update()
        {
            if (hasPosed)
            {
                RestorePose();
            }
        }

        private void LateUpdate()
        {
            if (!IsReady)
            {
                return;
            }

            CapturePose();
            float delta = Time.deltaTime;
            Vector3 up = Vector3.up;

            float pelvisHeight = transform.InverseTransformPoint(pelvis.position).y;
            if (carryAnchor != null)
            {
                float bob = holdItem != null ? pelvisHeight - pelvisRest.y : 0f;
                carryAnchor.localPosition = new Vector3(0f, bob, 0f);
            }

            bool grounded = TryProbeFloor(out float floorY);
            float shiftTarget = 0f;
            if (grounded)
            {
                // The procedural breath and stride move the whole rig up and down; keep the
                // lowest foot on the floor through that while the character is standing.
                float restSole = transform.TransformPoint(0f, soleRest, 0f).y;
                float lift = LowestSole() - restSole;
                float planted = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    (lift - PlantedLift) / (AirborneLift - PlantedLift));
                shiftTarget = Mathf.Clamp(
                    floorY - restSole - lift * planted,
                    -MaxGroundShift,
                    MaxGroundShift);
            }

            groundShift = Mathf.MoveTowards(groundShift, shiftTarget, GroundShiftSpeed * delta);
            if (Mathf.Abs(groundShift) > 1e-5f)
            {
                pelvis.position += up * groundShift;
            }

            Vector3 rightFoot = rightLeg.Foot.position;
            Vector3 leftFoot = leftLeg.Foot.position;
            Quaternion rightFootRotation = rightLeg.Foot.rotation;
            Quaternion leftFootRotation = leftLeg.Foot.rotation;
            bool rightLifted = grounded && LiftAboveFloor(rightLeg, floorY, ref rightFoot);
            bool leftLifted = grounded && LiftAboveFloor(leftLeg, floorY, ref leftFoot);

            float crouch = Mathf.Clamp01(CrouchAmount);
            float reachUp = Mathf.Clamp01(ReachUpAmount);
            bool bodyMoved = crouch > 0.001f || reachUp > 0.001f;
            if (bodyMoved)
            {
                PoseBody(crouch, reachUp);
            }

            if (bodyMoved || rightLifted)
            {
                SolveLeg(rightLeg, rightFoot, rightFootRotation);
            }

            if (bodyMoved || leftLifted)
            {
                SolveLeg(leftLeg, leftFoot, leftFootRotation);
            }

            float hold = holdItem != null ? Mathf.Clamp01(HoldWeight) : 0f;
            if (hold > 0.001f)
            {
                SolveHold(hold);
            }

            float waveTarget = ReadWaveEnvelope(out float waveTime) * (1f - hold);
            WaveWeight = Mathf.MoveTowards(WaveWeight, waveTarget, 6f * delta);
            if (WaveWeight > 0.001f)
            {
                SolveWave(waveTime, WaveWeight);
            }
        }

        private void PoseBody(float crouch, float reachUp)
        {
            Vector3 right = transform.right;
            pelvis.position += -Vector3.up * (crouchDrop * crouch) -
                               transform.forward * (crouchHipBack * crouch);

            float lean = crouchLean * crouch + reachUpLean * reachUp;
            if (lean < 0.01f)
            {
                return;
            }

            pelvis.rotation = Quaternion.AngleAxis(lean * PelvisLeanShare, right) * pelvis.rotation;
            Quaternion spineBend = Quaternion.AngleAxis(
                lean * (1f - PelvisLeanShare) / spine.Length,
                right);
            for (int i = 0; i < spine.Length; i++)
            {
                spine[i].rotation = spineBend * spine[i].rotation;
            }
        }

        private void SolveLeg(Leg leg, Vector3 ankle, Quaternion footRotation)
        {
            Vector3 side = leg.IsRight ? transform.right : -transform.right;
            Vector3 pole = leg.Calf.position + transform.forward * 0.6f + side * 0.12f;
            SolveTwoBone(leg.Thigh, leg.Calf, leg.Foot, ankle, pole);
            leg.Foot.rotation = footRotation;
        }

        private float LowestSole()
        {
            return Mathf.Min(
                Mathf.Min(
                    rightLeg.Foot.TransformPoint(rightLeg.HeelLocal).y,
                    rightLeg.Toes.TransformPoint(rightLeg.ToeLocal).y),
                Mathf.Min(
                    leftLeg.Foot.TransformPoint(leftLeg.HeelLocal).y,
                    leftLeg.Toes.TransformPoint(leftLeg.ToeLocal).y));
        }

        /// <summary>Raises the foot target so neither heel nor toe ends up under the floor.</summary>
        private bool LiftAboveFloor(Leg leg, float floorY, ref Vector3 ankle)
        {
            if (!alignToGround)
            {
                return false;
            }

            float heel = leg.Foot.TransformPoint(leg.HeelLocal).y;
            float toe = leg.Toes.TransformPoint(leg.ToeLocal).y;
            float sink = floorY - Mathf.Min(heel, toe);
            if (sink <= 0.001f)
            {
                return false;
            }

            ankle += Vector3.up * sink;
            return true;
        }

        private void SolveHold(float weight)
        {
            Vector3 itemUp = holdItem.up;
            Vector3 along = holdItem.TransformDirection(forwardLocal);
            float droop = gripFingerDroop * Mathf.Deg2Rad;
            Vector3 fingers = (along * Mathf.Cos(droop) - itemUp * Mathf.Sin(droop)).normalized;
            Vector3 rightFace = holdItem.TransformDirection(rightFaceLocal);
            Vector3 down = -Vector3.up;
            Vector3 back = -transform.forward;

            Vector3 rightShoulder = rightArm.Upper.position;
            SolveArm(
                rightArm,
                holdItem.TransformPoint(rightGripLocal),
                HandRotation(rightArm, fingers, -rightFace),
                rightShoulder + down * 0.35f + transform.right * 0.3f + back * 0.15f,
                weight);

            Vector3 leftShoulder = leftArm.Upper.position;
            SolveArm(
                leftArm,
                holdItem.TransformPoint(leftGripLocal),
                HandRotation(leftArm, fingers, rightFace),
                leftShoulder + down * 0.35f - transform.right * 0.3f + back * 0.15f,
                weight);
        }

        private void SolveWave(float time, float weight)
        {
            Vector3 right = transform.right;
            Vector3 up = Vector3.up;
            Vector3 forward = transform.forward;
            float swing = Mathf.Sin(time * waveFrequency * 2f * Mathf.PI);
            Vector3 shoulder = rightArm.Upper.position;
            Vector3 palm = shoulder +
                           right * (waveHandOffset.x + waveSwing * swing) +
                           up * waveHandOffset.y +
                           forward * waveHandOffset.z;
            Vector3 fingers = Quaternion.AngleAxis(-waveTilt * swing, forward) * up;
            SolveArm(
                rightArm,
                palm,
                HandRotation(rightArm, fingers, forward),
                shoulder + right * 0.5f - up * 0.35f - forward * 0.15f,
                weight);
        }

        /// <summary>
        /// Puts the palm centre of <paramref name="arm"/> at <paramref name="contact"/> with the
        /// given orientation, then blends from the animated arm by <paramref name="weight"/>.
        /// </summary>
        private void SolveArm(Arm arm, Vector3 contact, Quaternion hand, Vector3 pole, float weight)
        {
            arm.Saved[0] = arm.Upper.localRotation;
            arm.Saved[1] = arm.Lower.localRotation;
            for (int i = 0; i < arm.Twist.Length; i++)
            {
                arm.Saved[2 + i] = arm.Twist[i].localRotation;
            }

            arm.Saved[arm.Saved.Length - 1] = arm.Palm.localRotation;

            SolveTwoBone(arm.Upper, arm.Lower, arm.Palm, contact - hand * arm.ContactLocal, pole);
            SpreadTwist(arm, hand);
            arm.Palm.rotation = hand;

            if (weight >= 0.999f)
            {
                return;
            }

            arm.Upper.localRotation = Quaternion.Slerp(arm.Saved[0], arm.Upper.localRotation, weight);
            arm.Lower.localRotation = Quaternion.Slerp(arm.Saved[1], arm.Lower.localRotation, weight);
            for (int i = 0; i < arm.Twist.Length; i++)
            {
                arm.Twist[i].localRotation =
                    Quaternion.Slerp(arm.Saved[2 + i], arm.Twist[i].localRotation, weight);
            }

            arm.Palm.localRotation =
                Quaternion.Slerp(arm.Saved[arm.Saved.Length - 1], arm.Palm.localRotation, weight);
        }

        /// <summary>
        /// Shares the forearm roll a new hand orientation needs across the forearm twist
        /// bones, so the skin turns along the forearm instead of wringing at the wrist.
        /// </summary>
        private static void SpreadTwist(Arm arm, Quaternion hand)
        {
            if (arm.Twist.Length == 0)
            {
                return;
            }

            Vector3 axis = (arm.Palm.position - arm.Lower.position).normalized;
            Quaternion delta = hand * Quaternion.Inverse(arm.Palm.rotation);
            Vector3 imaginary = new Vector3(delta.x, delta.y, delta.z);
            Vector3 along = Vector3.Dot(imaginary, axis) * axis;
            Quaternion twist = new Quaternion(along.x, along.y, along.z, delta.w);
            float magnitude = Mathf.Sqrt(
                twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
            {
                return;
            }

            float angle = 2f * Mathf.Atan2(
                Vector3.Dot(along, axis) / magnitude,
                twist.w / magnitude) * Mathf.Rad2Deg;
            angle = Mathf.DeltaAngle(0f, angle);
            Quaternion share = Quaternion.AngleAxis(angle / (arm.Twist.Length + 1), axis);
            for (int i = 0; i < arm.Twist.Length; i++)
            {
                arm.Twist[i].rotation = share * arm.Twist[i].rotation;
            }
        }

        private static Quaternion HandRotation(Arm arm, Vector3 fingers, Vector3 palmNormal)
        {
            return Quaternion.LookRotation(fingers, palmNormal) *
                   Quaternion.Inverse(Quaternion.LookRotation(arm.FingerLocal, arm.PalmNormalLocal));
        }

        /// <summary>
        /// 0 to 1 while the Animator plays the wave state, easing in and out so the arm
        /// leaves and returns to the animated pose smoothly.
        /// </summary>
        private float ReadWaveEnvelope(out float stateTime)
        {
            stateTime = 0f;
            if (animator == null || !animator.isActiveAndEnabled ||
                animator.runtimeAnimatorController == null)
            {
                return 0f;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != WaveStateHash)
            {
                if (!animator.IsInTransition(0))
                {
                    return 0f;
                }

                state = animator.GetNextAnimatorStateInfo(0);
                if (state.shortNameHash != WaveStateHash)
                {
                    return 0f;
                }
            }

            float progress = state.normalizedTime;
            stateTime = progress * state.length;
            float rise = Mathf.SmoothStep(0f, 1f, progress / 0.12f);
            float fall = Mathf.SmoothStep(0f, 1f, (progress - 0.68f) / 0.16f);
            return rise * (1f - fall);
        }

        private bool CanReach(Arm arm, Vector3 contact, float crouch)
        {
            Vector3 hip = pelvisRest + new Vector3(0f, -crouchDrop * crouch, -crouchHipBack * crouch);
            Quaternion lean = Quaternion.AngleAxis(crouchLean * crouch, Vector3.right);
            Vector3 shoulder = transform.TransformPoint(hip + lean * (arm.ShoulderRest - pelvisRest)) +
                               Vector3.up * groundShift;
            return Vector3.Distance(shoulder, contact) <= arm.Reach * ReachMargin;
        }

        private bool TryProbeFloor(out float floorY)
        {
            floorY = 0f;
            if (!alignToGround)
            {
                return false;
            }

            Vector3 origin = transform.position + Vector3.up * 0.6f;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                groundHits,
                1.6f,
                groundLayers,
                QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = groundHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform) ||
                    (holdItem != null && hit.transform.IsChildOf(holdItem)))
                {
                    continue;
                }

                if (groundHits[i].distance < nearest)
                {
                    nearest = groundHits[i].distance;
                    floorY = groundHits[i].point.y;
                    found = true;
                }
            }

            return found;
        }

        private void CapturePose()
        {
            for (int i = 0; i < posedBones.Length; i++)
            {
                posedRotations[i] = posedBones[i].localRotation;
            }

            posedPelvisPosition = pelvis.localPosition;
            hasPosed = true;
        }

        private void RestorePose()
        {
            if (!hasPosed || posedBones == null)
            {
                return;
            }

            for (int i = 0; i < posedBones.Length; i++)
            {
                if (posedBones[i] != null)
                {
                    posedBones[i].localRotation = posedRotations[i];
                }
            }

            if (pelvis != null)
            {
                pelvis.localPosition = posedPelvisPosition;
            }

            hasPosed = false;
        }

        private bool Bind()
        {
            if (bound)
            {
                return true;
            }

            if (bindFailed)
            {
                return false;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            Transform root = animator != null ? animator.transform : transform;
            pelvis = FindBone(root, PelvisName);
            spine = new Transform[SpineNames.Length];
            for (int i = 0; i < SpineNames.Length; i++)
            {
                spine[i] = FindBone(root, SpineNames[i]);
            }

            rightArm = BuildArm(root, "R");
            leftArm = BuildArm(root, "L");
            rightLeg = BuildLeg(root, "R");
            leftLeg = BuildLeg(root, "L");

            if (pelvis == null || System.Array.IndexOf(spine, null) >= 0 ||
                rightArm == null || leftArm == null || rightLeg == null || leftLeg == null)
            {
                bindFailed = true;
                Debug.LogWarning(
                    $"[CharacterIKLayer] {name}: no Biped skeleton under {root.name}, IK disabled.",
                    this);
                return false;
            }

            // Measured from the pose the rig is in before anything animates it, which stands
            // on the model root. That root may sit above the character root, as Vexa_Visual
            // does, so the floor is matched to it rather than to the character root.
            float soleHeight = root.position.y;
            CalibrateLeg(rightLeg, soleHeight);
            CalibrateLeg(leftLeg, soleHeight);
            soleRest = transform.InverseTransformPoint(root.position).y;
            pelvisRest = transform.InverseTransformPoint(pelvis.position);
            rightArm.ShoulderRest = transform.InverseTransformPoint(rightArm.Upper.position);
            leftArm.ShoulderRest = transform.InverseTransformPoint(leftArm.Upper.position);

            posedBones = new[]
            {
                pelvis, spine[0], spine[1], spine[2],
                rightLeg.Thigh, rightLeg.Calf, rightLeg.Foot,
                leftLeg.Thigh, leftLeg.Calf, leftLeg.Foot,
                rightArm.Upper, rightArm.Lower, rightArm.Twist[0], rightArm.Twist[1], rightArm.Palm,
                leftArm.Upper, leftArm.Lower, leftArm.Twist[0], leftArm.Twist[1], leftArm.Palm
            };
            posedRotations = new Quaternion[posedBones.Length];
            bound = true;
            return true;
        }

        private Arm BuildArm(Transform root, string side)
        {
            string prefix = BonePrefix + side;
            Transform upper = FindBone(root, prefix + "Upperarm1");
            Transform lower = FindBone(root, prefix + "Forearm1");
            Transform twistA = FindBone(root, prefix + "Forearm2");
            Transform twistB = FindBone(root, prefix + "Forearm3");
            Transform palm = FindBone(root, prefix + "Palm");
            Transform index = FindBone(root, prefix + "Digit21");
            Transform pinky = FindBone(root, prefix + "Digit51");
            if (upper == null || lower == null || twistA == null || twistB == null ||
                palm == null || index == null || pinky == null)
            {
                return null;
            }

            bool isRight = side == "R";
            Vector3 knuckles = (index.position + pinky.position) * 0.5f;
            Vector3 fingers = (knuckles - palm.position).normalized;
            Vector3 across = (index.position - pinky.position).normalized;
            Vector3 palmNormal = Vector3.Cross(fingers, across).normalized * (isRight ? 1f : -1f);
            Vector3 contact = palm.position + (knuckles - palm.position) * 0.6f + palmNormal * 0.012f;

            return new Arm
            {
                Upper = upper,
                Lower = lower,
                Twist = new[] { twistA, twistB },
                Palm = palm,
                FingerLocal = Quaternion.Inverse(palm.rotation) * fingers,
                PalmNormalLocal = Quaternion.Inverse(palm.rotation) * palmNormal,
                ContactLocal = palm.InverseTransformPoint(contact),
                Reach = Vector3.Distance(upper.position, lower.position) +
                        Vector3.Distance(lower.position, palm.position) +
                        Vector3.Distance(palm.position, contact),
                IsRight = isRight,
                Saved = new Quaternion[5]
            };
        }

        private static Leg BuildLeg(Transform root, string side)
        {
            string prefix = BonePrefix + side;
            Leg leg = new Leg
            {
                Thigh = FindBone(root, prefix + "Thigh"),
                Calf = FindBone(root, prefix + "Calf"),
                Foot = FindBone(root, prefix + "Foot"),
                Toes = FindBone(root, prefix + "Toes"),
                IsRight = side == "R"
            };
            return leg.Thigh != null && leg.Calf != null && leg.Foot != null && leg.Toes != null
                ? leg
                : null;
        }

        private void CalibrateLeg(Leg leg, float soleHeight)
        {
            Vector3 heel = leg.Foot.position - transform.forward * 0.02f;
            heel.y = soleHeight;
            Vector3 toe = leg.Toes.position + transform.forward * 0.05f;
            toe.y = soleHeight;
            leg.HeelLocal = leg.Foot.InverseTransformPoint(heel);
            leg.ToeLocal = leg.Toes.InverseTransformPoint(toe);
        }

        private static Bounds LocalBounds(Transform item)
        {
            BoxCollider box = item.GetComponent<BoxCollider>();
            if (box != null)
            {
                return new Bounds(box.center, box.size);
            }

            MeshFilter mesh = item.GetComponent<MeshFilter>();
            if (mesh != null && mesh.sharedMesh != null)
            {
                return mesh.sharedMesh.bounds;
            }

            return new Bounds(Vector3.zero, Vector3.one);
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
