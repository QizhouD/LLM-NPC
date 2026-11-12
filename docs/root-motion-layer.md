# Root-motion layer

Status: written on branch `feat/root-motion-layer`, paired with the branch of the
same name in the
[planner repo](https://github.com/QizhouD/language-conditioned-generative-trajectory-planning-for-real-time-digital-humans).
The C# here has not yet been compiled or run inside Unity.

## First run checklist

1. Get the branch. If it is on GitHub: `git fetch origin` then
   `git checkout feat/root-motion-layer`. From the patch file instead:
   `git checkout -b feat/root-motion-layer` then
   `git am --3way path\to\llm-npc-feat-root-motion-layer.patch`.
2. Open the project in Unity 6000.2.15f1 and wait for the import. **Fix any compile
   errors first**: this code was written without a Unity editor. If Unity rewrites
   any of the new `.meta` files, commit its version.
3. Run the EditMode tests (**Window > General > Test Runner**). The 15 tests in
   `RootMotionPlanningTests` cover everything new that does not need a NavMesh.
4. Press Play with the scene unchanged. `MockTrajectoryPlanSource` is still the
   plan source, so the demo must behave exactly as before. This confirms the
   follower changes are harmless.
5. Try the classical baseline: follow [Wiring the scene](#wiring-the-scene) but put
   `SpringPathPlanSource` in the follower's **Plan Source** slot. No Python needed.
   Ask the character to walk over; it should follow the NavMesh route around the boxes.
6. Try the learned planner: start the service (see
   [Starting the service](#starting-the-service); the planner repo's
   `docs/ROOT_MOTION_QUICKSTART.md` gets you a checkpoint), switch **Plan Source**
   to `HttpTrajectoryPlanSource`, press Play. The Console should log
   `[Planning] Plan origin: Model`.
7. Stop the service while the character is walking. It should keep walking on the
   spring fallback and the Console should log
   `[Planning] Plan origin: LocalFallbackAfterError` with the reason.

If step 6 logs `LocalFallbackAfterError` with the service running, check the
URL and port on the component, then **Project Settings > Player > Other Settings >
Allow downloads over HTTP**.

## Division of labour

| Question | Who answers | How |
| --- | --- | --- |
| Where should the character go? | Unity NavMesh | `NavMeshPathPlanner.TryBuildPath` returns path corners, trimmed by the goal's stop distance |
| How should it move along that route? | Root-motion planner (learned), or the spring follower (classical) | Timed root poses at 10 Hz for 3 s |
| Is the result safe to execute? | Unity NavMesh | `NavMeshPathPlanner.IsWalkable` ray-casts every segment of the plan |
| What does the body do? | Animation layer | Unchanged: `MotionIntentAnimator` reads speed and turn rate |

The planner bounds how far a plan strays from the route it was sent (a swept check
with an explicit error bound, see the planner repo). It cannot know whether that
corridor is walkable, so the last check happens here, against the real NavMesh. A
plan that fails is discarded and the spring follower plans instead; the character
keeps moving and the rejection is counted.

## What a request carries now

`PlanRequest` gained five optional fields; existing callers compile unchanged.

| Field | Meaning |
| --- | --- |
| `PathCorners` | NavMesh route in world space, starting at the character |
| `PathEndsAtGoal` | False for a partial path: keep moving past the last corner |
| `History` | Root poses of the last two seconds at 10 Hz, oldest first (`RootHistoryRecorder`) |
| `Gait` | `walk`, `run` or `sprint` |
| `CorridorHalfWidth` | Allowed deviation from the route, default 0.5 m |

`PlanRequestJson.Build` turns this into the body of `POST /plan`. The response is the
existing `TrajectoryPlan` contract plus `source`, `rejected_candidates`, `candidates`
and `latency_ms`. One change from the mock: plans have 31 points, the first being the
current pose at `t = 0`.

## Wiring the scene

On the character that has `TrajectoryPlanFollower`:

1. Add `RootHistoryRecorder`. The follower finds it on the same object.
2. Add `SpringPathPlanSource`.
3. Add `HttpTrajectoryPlanSource`; drag the spring source into **Fallback Source**.
4. Drag the HTTP source into the follower's **Plan Source** slot.
5. Optional: add `RootMotionMetricsLogger` and set **Run Label**.

To run the classical baseline alone, put `SpringPathPlanSource` in the follower's
**Plan Source** slot instead. `MockTrajectoryPlanSource` still works and ignores the
new fields.

`ProjectSettings` now has **Allow downloads over HTTP** set to *Allowed in
development builds* (`insecureHttpOption: 1`), because the service is plain HTTP on
localhost. Release builds are unaffected.

## Starting the service

In the planner repo, with a trained checkpoint:

```powershell
python -m gtp.rootmotion.service --checkpoint runs/rootmotion_flow/best.pt --port 8787
```

The Console logs each change of plan origin, and the metrics log's `source` column
records it per step:
`Model`, `ServiceFallback` (the service rejected all its samples and answered with
its own spring plan), `LocalFallbackAfterError` (service unreachable) or
`LocalFallbackAfterNavMeshReject`.

## Using the scene as an evaluation platform

`RootMotionMetricsLogger` writes one CSV row per control step to
`Application.persistentDataPath/rootmotion-logs/`:

```text
time,x,z,yaw,speed,acceleration,turn_rate,following,source,plan_id,path_deviation
```

Speed, acceleration and turn rate are measured from the executed transform, so they
include replanning seams and NavMesh snapping. Suggested protocol:

1. Fix a set of start and goal pairs in the scene, including routes around the
   pickup boxes and at least one with a moving target.
2. Run each route once per plan source: NavMeshAgent, spring, HTTP.
3. Compare the logs on the quantities the offline evaluator reports: speed,
   acceleration and turn-rate distributions against motion capture, path deviation,
   time to goal, and from `HttpTrajectoryPlanSource` the model, fallback and
   NavMesh-reject counts.

## Known gaps

- Not compiled or run in Unity yet. The spring follower is a line-for-line port of
  the Python version, whose behaviour is covered by tests there.
- `SpringPathPlanSource` target speeds default to the LAFAN1 medians (0.9, 2.1 and
  2.5 m/s). They are low for a game run; they are there so the baseline matches the
  offline numbers.
- The character rig still has no walk clip. Root motion quality can be measured
  now; judging it by eye needs the Humanoid conversion in
  [rig-and-animation.md](rig-and-animation.md) and a body layer that follows the root.
- No editor menu item wires these components yet; the steps above are manual.
