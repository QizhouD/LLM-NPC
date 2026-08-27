#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using DigitalHuman.Planning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// The one maintained scene tool. Replaces the 27 single-use bootstrap scripts that
    /// now live in <c>Tools/LegacyEditorBootstrap/</c>.
    /// </summary>
    public static class DigitalHumanSceneTool
    {
        [MenuItem("Digital Human/Scene/Report Hierarchy")]
        public static string ReportHierarchy()
        {
            Scene scene = SceneManager.GetActiveScene();
            StringBuilder report = new StringBuilder();
            report.Append("scene: ").AppendLine(scene.name);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AppendObject(report, root.transform, 0);
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Scene/Report Digital Human Components")]
        public static string ReportDigitalHumanComponents()
        {
            StringBuilder report = new StringBuilder();
            List<MonoBehaviour> owned = new List<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in
                     Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null)
                {
                    continue;
                }

                string ns = behaviour.GetType().Namespace;
                if (ns != null && ns.StartsWith("DigitalHuman"))
                {
                    owned.Add(behaviour);
                }
            }

            owned.Sort((a, b) => string.Compare(
                Path(a.transform) + a.GetType().Name,
                Path(b.transform) + b.GetType().Name,
                System.StringComparison.Ordinal));

            foreach (MonoBehaviour behaviour in owned)
            {
                report.Append(Path(behaviour.transform))
                    .Append(" :: ")
                    .AppendLine(behaviour.GetType().Name);
            }

            string result = report.Length == 0
                ? "no DigitalHuman components found in the open scene"
                : report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Scene/Report Animators")]
        public static string ReportAnimators()
        {
            StringBuilder report = new StringBuilder();
            foreach (Animator animator in
                     Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                report.Append(Path(animator.transform))
                    .Append(" controller=")
                    .Append(animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : "<none>")
                    .Append(" avatar=")
                    .Append(animator.avatar != null ? animator.avatar.name : "<none>")
                    .Append(" isHuman=")
                    .AppendLine((animator.avatar != null && animator.avatar.isHuman).ToString());
            }

            string result = report.Length == 0
                ? "no Animator in the open scene"
                : report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private const string BasePrefabPath = "Assets/DigitalHuman/Prefabs/DigitalHuman.prefab";
        private const string VexaPrefabPath = "Assets/Prefabs/Characters/Vexa.prefab";
        private const string VisualChildName = "Vexa_Visual";

        [MenuItem("Digital Human/Scene/Wire Animation Stack")]
        public static string WireAnimationStack()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine(WireBasePrefab());
            report.AppendLine(WireVexaPrefab());
            report.AppendLine(WireChatProvidersInScene());
            report.AppendLine(WireHeldItemHandInScene());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private static string WireBasePrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
            if (contents == null)
            {
                return $"base: FAILED to load {BasePrefabPath}";
            }

            StringBuilder report = new StringBuilder("base: ");
            try
            {
                MotionIntentAnimator intent = contents.GetComponent<MotionIntentAnimator>();
                if (intent == null)
                {
                    intent = contents.AddComponent<MotionIntentAnimator>();
                    report.Append("added MotionIntentAnimator; ");
                }
                else
                {
                    report.Append("MotionIntentAnimator present; ");
                }

                intent.Configure(
                    contents.GetComponentInChildren<Animator>(true),
                    contents.GetComponent<DigitalHumanController>());

                ProceduralHumanoidAnimator procedural =
                    contents.GetComponent<ProceduralHumanoidAnimator>();
                if (procedural != null)
                {
                    procedural.ConfigureIntentSource(
                        intent,
                        contents.GetComponentInChildren<Animator>(true));
                    report.Append("linked ProceduralHumanoidAnimator; ");
                }

                report.Append(WireChatProviders(contents));
                report.Append("ok");

                PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return report.ToString();
        }

        /// <summary>
        /// Repoints the held-item hand at Vexa's own hand bone.
        /// </summary>
        /// <remarks>
        /// The legacy bootstrap wired this to <c>Model/DigitalHuman_3D/Rig/.../RightHand</c>,
        /// a bone on the placeholder capsule model that predates the Vexa mesh. Parenting a
        /// picked-up item there attaches it to a skeleton nothing animates, so the item hangs
        /// motionless while the visible arm swings past it.
        /// </remarks>
        private static string WireHeldItemHand(GameObject contents, Transform visual)
        {
            AgentInteraction interaction = contents.GetComponent<AgentInteraction>();
            if (interaction == null || visual == null)
            {
                return string.Empty;
            }

            Transform hand = FindBone(visual, RightWristFragments);
            if (hand == null)
            {
                return "WARNING no right wrist bone found under the visual; ";
            }

            interaction.Configure(contents.GetComponent<AgentMovement>(), hand);
            return $"held-item hand = {hand.name}; ";
        }

        /// <summary>
        /// Same repoint as <see cref="WireHeldItemHand"/> but against the scene instance, which
        /// is where the stale reference is actually serialised as a prefab override.
        /// </summary>
        [MenuItem("Digital Human/Scene/Wire Held Item Hand In Scene")]
        public static string WireHeldItemHandInScene()
        {
            StringBuilder report = new StringBuilder();
            AgentInteraction[] interactions =
                Object.FindObjectsByType<AgentInteraction>(FindObjectsSortMode.None);

            if (interactions.Length == 0)
            {
                return "no AgentInteraction in the open scene";
            }

            foreach (AgentInteraction interaction in interactions)
            {
                GameObject owner = interaction.gameObject;
                Transform searchRoot = ResolveVisualRoot(owner);

                Transform hand = FindBone(searchRoot, RightWristFragments);
                if (hand == null)
                {
                    report.Append(owner.name).AppendLine(": WARNING no right wrist bone found");
                    continue;
                }

                interaction.Configure(owner.GetComponent<AgentMovement>(), hand);
                EditorUtility.SetDirty(interaction);
                report.Append(owner.name).Append(": held-item hand = ").AppendLine(Path(hand));
                report.Append(owner.name).Append(": ").AppendLine(MatchGroundSpeedToStride(owner));
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        /// <summary>
        /// Brings the NavMeshAgent's speed in line with the stride the procedural animator
        /// synthesises and the speed the mock planner cruises at.
        /// </summary>
        /// <remarks>
        /// The stride frequency and amplitude are calibrated for 1.4 m/s. Driving the body
        /// across the floor at the agent's default 2.6 m/s makes the feet skate, which reads
        /// as "there is no walk animation" even once the stride is running.
        /// </remarks>
        private static string MatchGroundSpeedToStride(GameObject owner)
        {
            const float strideReferenceSpeed = 1.4f;

            NavMeshAgent agent = owner.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                return "no NavMeshAgent to retune";
            }

            if (Mathf.Approximately(agent.speed, strideReferenceSpeed))
            {
                return $"ground speed already {strideReferenceSpeed:F2} m/s";
            }

            float previous = agent.speed;
            agent.speed = strideReferenceSpeed;
            EditorUtility.SetDirty(agent);
            return $"ground speed {previous:F2} -> {strideReferenceSpeed:F2} m/s";
        }

        /// <summary>
        /// Sends the agent to the farthest reachable point it can find, so locomotion runs
        /// long enough to sample. Aiming a fixed distance ahead is no good: off-mesh targets
        /// get clamped to the nearest edge, which can land inside the stopping distance and
        /// count as an instant arrival.
        /// </summary>
        [MenuItem("Digital Human/Scene/Test Walk To Farthest Point")]
        public static string TestWalkToFarthestPoint()
        {
            AgentMovement movement = Object.FindAnyObjectByType<AgentMovement>();
            if (movement == null)
            {
                return "no AgentMovement in the open scene";
            }

            NavMeshAgent agent = movement.NavMeshAgent;
            if (agent == null || !agent.isOnNavMesh)
            {
                return "agent is not on a NavMesh";
            }

            Vector3 origin = movement.transform.position;
            Vector3 best = origin;
            float bestDistance = 0f;
            NavMeshPath path = new NavMeshPath();

            for (int i = 0; i < 24; i++)
            {
                float angle = i / 24f * Mathf.PI * 2f;
                Vector3 probe = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 12f, NavMesh.AllAreas) ||
                    !agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, hit.position);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = hit.position;
                }
            }

            if (bestDistance <= agent.stoppingDistance + 0.5f)
            {
                return $"no reachable point beyond the stopping distance " +
                       $"(best was {bestDistance:F2}m); the baked NavMesh may be tiny";
            }

            bool started = movement.MoveTo(best);
            return $"MoveTo({best:F2}) returned {started}, {bestDistance:F2}m away";
        }

        /// <summary>
        /// Samples the values that decide whether the character looks like it is walking, so
        /// a claim about locomotion can be checked instead of assumed.
        /// </summary>
        [MenuItem("Digital Human/Scene/Report Locomotion State")]
        public static string ReportLocomotionState()
        {
            MotionIntentAnimator intent = Object.FindAnyObjectByType<MotionIntentAnimator>();
            if (intent == null)
            {
                return "no MotionIntentAnimator in the open scene";
            }

            Transform root = intent.transform;
            Transform visual = ResolveVisualRoot(root.gameObject);
            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();

            StringBuilder report = new StringBuilder();
            report.Append("intent=").Append(intent.CurrentIntent)
                .Append(" Speed=").Append(intent.Speed.ToString("F3"))
                .Append(" TurnRate=").Append(intent.TurnRate.ToString("F3"))
                .AppendLine();
            report.Append("root y=").Append(root.position.y.ToString("F4"))
                .Append("  visual localPosition=").Append(visual.localPosition.ToString("F4"))
                .AppendLine();

            if (agent != null)
            {
                report.Append("agent velocity=")
                    .Append(agent.velocity.magnitude.ToString("F3"))
                    .Append(" remaining=").Append(agent.remainingDistance.ToString("F3"))
                    .Append(" speedCap=").Append(agent.speed.ToString("F2"))
                    .AppendLine();
                report.Append("agent enabled=").Append(agent.enabled)
                    .Append(" isStopped=").Append(agent.isStopped)
                    .Append(" updatePosition=").Append(agent.updatePosition)
                    .Append(" onNavMesh=").Append(agent.isOnNavMesh)
                    .Append(" pathStatus=").Append(agent.pathStatus)
                    .Append(" hasPath=").Append(agent.hasPath)
                    .AppendLine();
                report.Append("agent destination=").Append(agent.destination.ToString("F2"))
                    .Append(" position=").Append(root.position.ToString("F2"))
                    .AppendLine();
            }

            TrajectoryPlanFollower follower =
                root.GetComponent<Planning.TrajectoryPlanFollower>();
            if (follower != null)
            {
                report.Append("follower enabled=").Append(follower.enabled)
                    .Append(" isFollowing=").Append(follower.IsFollowing)
                    .AppendLine();
            }

            Animator animator = visual.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                report.Append("animator Speed param=")
                    .Append(animator.GetFloat(MotionIntentAnimator.SpeedParameter).ToString("F3"))
                    .AppendLine();
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        /// <summary>
        /// Picks the transform whose subtree holds the skeleton that is actually posed. The
        /// object keeps a disabled Animator at its root alongside the live one on the visual
        /// child, and the old placeholder model is still parented under it, so an unqualified
        /// bone search can easily land on the wrong skeleton.
        /// </summary>
        private static Transform ResolveVisualRoot(GameObject owner)
        {
            foreach (Animator animator in owner.GetComponentsInChildren<Animator>(true))
            {
                if (animator.enabled && animator.runtimeAnimatorController != null)
                {
                    return animator.transform;
                }
            }

            return owner.transform;
        }

        /// <summary>
        /// Right wrist bone names across the rigs this project has carried. Vexa's artist
        /// named the wrist "Base HumanRPalm" and the fingers "Base HumanRDigitNN", so nothing
        /// in that armature contains "Hand" at all; the "Hand" spellings match the older
        /// placeholder rig and anything imported from Mixamo.
        /// </summary>
        private static readonly string[] RightWristFragments =
        {
            "RPalm", "RightHand", "RHand", "Hand_R", "R_Hand", "Wrist_R", "RWrist"
        };

        /// <summary>
        /// Finds the shallowest bone matching the earliest fragment that matches at all.
        /// Shallowest wins so a wrist pattern cannot land on a finger joint below it.
        /// </summary>
        private static Transform FindBone(Transform root, params string[] nameFragments)
        {
            foreach (string fragment in nameFragments)
            {
                Transform best = null;
                int bestDepth = int.MaxValue;
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name.IndexOf(
                            fragment, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    int depth = 0;
                    for (Transform cursor = candidate; cursor != root; cursor = cursor.parent)
                    {
                        depth++;
                    }

                    if (depth < bestDepth)
                    {
                        bestDepth = depth;
                        best = candidate;
                    }
                }

                if (best != null)
                {
                    return best;
                }
            }

            return null;
        }

        /// <summary>
        /// Makes the DeepSeek backend available on every object that owns an
        /// <see cref="AIConversationManager"/>, without changing which provider is selected.
        /// The existing OpenAI wiring and the project default are left untouched.
        /// </summary>
        private static string WireChatProviders(GameObject contents)
        {
            AIConversationManager conversation = contents.GetComponent<AIConversationManager>();
            if (conversation == null)
            {
                return string.Empty;
            }

            StringBuilder report = new StringBuilder();
            DeepSeekLlmService deepSeek = contents.GetComponent<DeepSeekLlmService>();
            if (deepSeek == null)
            {
                deepSeek = contents.AddComponent<DeepSeekLlmService>();
                report.Append("added DeepSeekLlmService; ");
            }

            conversation.SetDeepSeekProvider(deepSeek);
            report.Append($"chat provider stays {conversation.Provider}; ");
            return report.ToString();
        }

        /// <summary>
        /// Reports whether the Editor process can see each API key, without revealing one.
        /// Worth having as a tool because the Editor inherits its environment from whatever
        /// launched it, so a key set after Unity Hub started is invisible until Hub restarts —
        /// which otherwise looks identical to a missing key.
        /// </summary>
        /// <summary>
        /// Dumps the transform references the embodiment layer actually holds. These come from
        /// the legacy bootstrap scripts rather than from <see cref="WireAnimationStack"/>, so
        /// they are the part most likely to be wrong and the least visible in code.
        /// </summary>
        [MenuItem("Digital Human/Scene/Report Embodiment Wiring")]
        public static string ReportEmbodimentWiring()
        {
            StringBuilder report = new StringBuilder();

            ProceduralHumanoidAnimator procedural =
                Object.FindAnyObjectByType<ProceduralHumanoidAnimator>();
            if (procedural == null)
            {
                report.AppendLine("no ProceduralHumanoidAnimator in the open scene");
            }
            else
            {
                Transform owner = procedural.transform;
                report.Append("ProceduralHumanoidAnimator on ").AppendLine(Path(owner));
                foreach (string field in new[]
                         {
                             "characterRoot", "torso", "head",
                             "leftUpperArm", "rightUpperArm",
                             "leftUpperLeg", "rightUpperLeg",
                             "intentAnimator", "animator", "movement"
                         })
                {
                    report.Append("  ").Append(field).Append(" = ")
                        .AppendLine(DescribeReference(procedural, field, owner));
                }
            }

            AgentInteraction interaction = Object.FindAnyObjectByType<AgentInteraction>();
            if (interaction != null)
            {
                report.Append("AgentInteraction.rightHand = ")
                    .AppendLine(DescribeReference(
                        interaction, "rightHand", interaction.transform));
            }

            NavMeshAgent agent = Object.FindAnyObjectByType<NavMeshAgent>();
            if (agent != null)
            {
                report.Append("NavMeshAgent on ").Append(Path(agent.transform))
                    .Append(" enabled=").Append(agent.enabled)
                    .Append(" baseOffset=").Append(agent.baseOffset)
                    .AppendLine();
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        private static string DescribeReference(
            Object target,
            string propertyPath,
            Transform relativeTo)
        {
            SerializedProperty property =
                new SerializedObject(target).FindProperty(propertyPath);
            if (property == null)
            {
                return "<no such field>";
            }

            Object value = property.objectReferenceValue;
            if (value == null)
            {
                return "<unassigned>";
            }

            if (value is Transform transform)
            {
                string marker = transform == relativeTo ? "  <-- SAME OBJECT AS OWNER" : string.Empty;
                return Path(transform) + marker;
            }

            if (value is Component component)
            {
                return $"{value.GetType().Name} on {Path(component.transform)}";
            }

            return value.GetType().Name;
        }

        [MenuItem("Digital Human/Report API Key Status")]
        public static string ReportApiKeyStatus()
        {
            string[] variableNames =
            {
                OpenAILLMService.DefaultApiKeyEnvironmentVariable,
                DeepSeekLlmService.DefaultApiKeyEnvironmentVariable
            };

            StringBuilder report = new StringBuilder();
            foreach (string variableName in variableNames)
            {
                report.Append(variableName).Append(": ")
                    .AppendLine(ApiKeyEditorStore.DescribeSource(variableName));
            }

            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        [MenuItem("Digital Human/Scene/Report Chat Providers")]
        public static string ReportChatProviders()
        {
            StringBuilder report = new StringBuilder();
            AIConversationManager[] managers =
                Object.FindObjectsByType<AIConversationManager>(FindObjectsSortMode.None);

            foreach (AIConversationManager conversation in managers)
            {
                GameObject owner = conversation.gameObject;
                report.Append(owner.name)
                    .Append(" prefabType=").Append(PrefabUtility.GetPrefabAssetType(owner))
                    .Append(" provider=").Append(conversation.Provider)
                    .Append(" hasBackend=").Append(conversation.HasActiveBackend)
                    .Append(" openAiComponents=")
                    .Append(owner.GetComponents<OpenAILLMService>().Length)
                    .Append(" deepSeekComponents=")
                    .Append(owner.GetComponents<DeepSeekLlmService>().Length)
                    .Append(" conversationManagers=")
                    .Append(owner.GetComponents<AIConversationManager>().Length)
                    .Append(" serialized[openAi=")
                    .Append(DescribeSerializedReference(conversation, "responseProviderComponent"))
                    .Append(" deepSeek=")
                    .Append(DescribeSerializedReference(conversation, "deepSeekProviderComponent"))
                    .Append(']')
                    .AppendLine();
            }

            string result = managers.Length == 0
                ? "no AIConversationManager in the open scene"
                : report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        /// <summary>
        /// Reads a serialized object reference by name. The runtime resolution happens in
        /// <c>Awake</c>, so at edit time the serialized slot is the only thing worth reporting.
        /// </summary>
        private static string DescribeSerializedReference(Object target, string propertyPath)
        {
            SerializedProperty property =
                new SerializedObject(target).FindProperty(propertyPath);
            if (property == null)
            {
                return "<no such field>";
            }

            Object value = property.objectReferenceValue;
            return value == null ? "<unassigned>" : value.GetType().Name;
        }

        /// <summary>
        /// Adds the DeepSeek backend to conversation managers that live on the scene instance
        /// rather than on a prefab asset. The scene's NPC adds its conversation components as
        /// instance-level additions, so prefab-only wiring never reaches them.
        /// </summary>
        [MenuItem("Digital Human/Scene/Wire Chat Providers In Scene")]
        public static string WireChatProvidersInScene()
        {
            StringBuilder report = new StringBuilder();
            AIConversationManager[] managers =
                Object.FindObjectsByType<AIConversationManager>(FindObjectsSortMode.None);

            if (managers.Length == 0)
            {
                report.Append("no AIConversationManager in the open scene");
            }

            for (int i = 0; i < managers.Length; i++)
            {
                AIConversationManager conversation = managers[i];
                GameObject owner = conversation.gameObject;
                report.Append(owner.name).Append(": ");

                DeepSeekLlmService deepSeek = owner.GetComponent<DeepSeekLlmService>();
                if (deepSeek == null)
                {
                    deepSeek = Undo.AddComponent<DeepSeekLlmService>(owner);
                    report.Append("added DeepSeekLlmService; ");
                }

                conversation.SetDeepSeekProvider(deepSeek);
                EditorUtility.SetDirty(conversation);
                EditorUtility.SetDirty(owner);
                report.AppendLine($"provider stays {conversation.Provider}");
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            string result = report.ToString().TrimEnd();
            Debug.Log(result);
            return result;
        }

        /// <summary>
        /// Adds the planning path alongside the NavMesh one. The mock source is the default
        /// because every real planner export is currently a fallback; see the README.
        /// </summary>
        private static string WirePlanningStack(GameObject contents, MotionIntentAnimator intent)
        {
            StringBuilder report = new StringBuilder();

            MockTrajectoryPlanSource mockSource = contents.GetComponent<MockTrajectoryPlanSource>();
            if (mockSource == null)
            {
                mockSource = contents.AddComponent<MockTrajectoryPlanSource>();
                report.Append("added MockTrajectoryPlanSource; ");
            }

            TrajectoryPlanFollower follower = contents.GetComponent<TrajectoryPlanFollower>();
            if (follower == null)
            {
                follower = contents.AddComponent<TrajectoryPlanFollower>();
                report.Append("added TrajectoryPlanFollower; ");
            }

            follower.Configure(mockSource, intent, contents.GetComponent<AgentMovement>());

            TrajectoryDebugRenderer debugRenderer =
                contents.GetComponent<TrajectoryDebugRenderer>();
            if (debugRenderer == null)
            {
                debugRenderer = contents.AddComponent<TrajectoryDebugRenderer>();
                report.Append("added TrajectoryDebugRenderer; ");
            }

            debugRenderer.Configure(follower);

            IntentPlanBridge bridge = contents.GetComponent<IntentPlanBridge>();
            if (bridge == null)
            {
                bridge = contents.AddComponent<IntentPlanBridge>();
                report.Append("added IntentPlanBridge; ");
            }

            // The approach target is left null so the bridge follows the LookAtController's
            // current target, which the interaction trigger sets to whoever walked up.
            bridge.Configure(
                contents.GetComponent<DialogueController>(),
                follower,
                contents.GetComponent<LookAtController>(),
                null);

            return report.ToString();
        }

        private static string WireVexaPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(VexaPrefabPath);
            if (contents == null)
            {
                return $"vexa: FAILED to load {VexaPrefabPath}";
            }

            StringBuilder report = new StringBuilder("vexa: ");
            try
            {
                Transform visual = contents.transform.Find(VisualChildName);
                if (visual == null)
                {
                    return $"vexa: FAILED, no child named {VisualChildName}";
                }

                RuntimeAnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        AnimatorControllerBuilder.ControllerPath);

                // Vexa clips address bones as "Armature/Base Human...", relative to the
                // model root. The Animator therefore has to sit on Vexa_Visual, not on the
                // NPC root, or nothing binds.
                Animator visualAnimator = visual.GetComponent<Animator>();
                if (visualAnimator == null)
                {
                    visualAnimator = visual.gameObject.AddComponent<Animator>();
                    report.Append($"added Animator on {VisualChildName}; ");
                }
                else
                {
                    report.Append($"Animator on {VisualChildName} present; ");
                }

                visualAnimator.runtimeAnimatorController = controller;
                visualAnimator.applyRootMotion = false;
                visualAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Animator rootAnimator = contents.GetComponent<Animator>();
                if (rootAnimator != null)
                {
                    rootAnimator.runtimeAnimatorController = null;
                    rootAnimator.enabled = false;
                    report.Append("disabled root Animator; ");
                }

                MotionIntentAnimator intent = contents.GetComponent<MotionIntentAnimator>();
                if (intent == null)
                {
                    intent = contents.AddComponent<MotionIntentAnimator>();
                    report.Append("added MotionIntentAnimator; ");
                }

                intent.Configure(visualAnimator, contents.GetComponent<DigitalHumanController>());

                ProceduralHumanoidAnimator procedural =
                    contents.GetComponent<ProceduralHumanoidAnimator>();
                if (procedural != null)
                {
                    procedural.ConfigureIntentSource(intent, visualAnimator);
                    report.Append("linked ProceduralHumanoidAnimator; ");
                }

                ProceduralFaceController face = contents.GetComponent<ProceduralFaceController>();
                if (face == null)
                {
                    face = contents.AddComponent<ProceduralFaceController>();
                    report.Append("added ProceduralFaceController; ");
                }

                face.AutoBind(
                    visual,
                    contents.GetComponent<DigitalHumanController>(),
                    contents.GetComponent<LookAtController>(),
                    null,
                    out string faceReport);
                report.Append(faceReport).Append("; ");

                ResponseAnnotationDriver driver =
                    contents.GetComponent<ResponseAnnotationDriver>();
                if (driver == null)
                {
                    driver = contents.AddComponent<ResponseAnnotationDriver>();
                    report.Append("added ResponseAnnotationDriver; ");
                }

                driver.Configure(contents.GetComponent<DialogueController>(), intent, face);

                SpeechHeadMotion speechMotion = contents.GetComponent<SpeechHeadMotion>();
                if (speechMotion == null)
                {
                    speechMotion = contents.AddComponent<SpeechHeadMotion>();
                    report.Append("added SpeechHeadMotion; ");
                }

                speechMotion.Configure(contents.GetComponentInChildren<AudioSource>(), visual);

                report.Append(WirePlanningStack(contents, intent));
                report.Append(WireChatProviders(contents));
                report.Append(WireHeldItemHand(contents, visual));

                report.Append("ok");

                PrefabUtility.SaveAsPrefabAsset(contents, VexaPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return report.ToString();
        }

        private static void AppendObject(StringBuilder report, Transform transform, int depth)
        {
            report.Append(new string(' ', depth * 2)).Append(transform.name);
            Component[] components = transform.GetComponents<Component>();
            if (components.Length > 1)
            {
                report.Append("  [");
                bool first = true;
                foreach (Component component in components)
                {
                    if (component is Transform)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        report.Append(", ");
                    }

                    report.Append(component == null ? "MISSING_SCRIPT" : component.GetType().Name);
                    first = false;
                }

                report.Append(']');
            }

            report.AppendLine();

            // Bone hierarchies are long and not useful here.
            if (transform.name.StartsWith("Base Human") || transform.name == "Armature")
            {
                return;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                AppendObject(report, transform.GetChild(i), depth + 1);
            }
        }

        private static string Path(Transform transform)
        {
            if (transform.parent == null)
            {
                return transform.name;
            }

            return Path(transform.parent) + "/" + transform.name;
        }
    }
}
#endif
