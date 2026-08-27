# Demo recording

The README opens with a GIF because that is the only part of this repository a
reader is guaranteed to look at. This file is the shot list, so the recording is
repeatable rather than improvised.

## Before recording

1. Set `OPENAI_API_KEY` so the dialogue is real model output, and switch
   `VoiceController`'s service field from `MockVoiceService` to
   `OpenAITtsVoiceService`. An offline-mode recording is not worth publishing.
2. On `Vexa`, enable `ResponseAnnotationDriver.logAnnotations` while rehearsing so
   you can confirm the tags are landing, then turn it off before the take.
3. Confirm `TrajectoryDebugRenderer.visible` is on. The overlay only draws while a
   plan is executing, so it will not clutter the dialogue shots.
4. Reposition the three pickup boxes so at least one sits between the player's
   position and Vexa. Without a blocking obstacle the planned path is a straight
   line and the whole point of the visualisation is lost.
5. Run `unity command run_tests` and confirm it is green. A broken take is cheaper
   to discover before recording than after.

## Shot list

| # | Shot | What it demonstrates | Duration |
| --- | --- | --- | --- |
| 1 | Walk up to Vexa, stop at the prompt | Trigger volume, idle body, blink, gaze tracking | 4 s |
| 2 | Open dialogue, ask an open question | Thinking state, **gaze aversion during model latency** | 5 s |
| 3 | Reply arrives and is spoken | Speech-driven head motion, eyebrow emotion change | 4 s |
| 4 | Say "come over here" | Tag becomes a plan request | 2 s |
| 5 | Hold on the path overlay | Planned route, control points, segment colours, obstacle circles | 4 s |
| 6 | Vexa walks the route around the box | Root motion from planned points, synthesised stride | 6 s |
| 7 | Say "stop", then "wave" | Plan abandoned, one-shot gesture, release to idle | 5 s |
| 8 | Close-up on the face | Blink asymmetry, saccades, layered eye-then-head gaze | 5 s |

Roughly 35 seconds. Shots 4 to 6 are the ones that matter: they are the only
place where the language model, the intent contract and the planner are visibly
the same system.

## Producing the files

Unity Recorder (Window > General > Recorder) writing to `Recordings/`, which is
git-ignored. Record at 1920x1080 60 fps, then:

- **GIF for the README:** shots 4 to 6 only, 800 px wide, 15 fps, under 5 MB.
  Commit it under `docs/media/`.
- **Full video:** all eight shots, uploaded rather than committed. Link it from
  the README.

Say in the caption that the trajectories come from `MockTrajectoryPlanSource`.
The README says so too, but a viewer who only watches the video will not read it.
