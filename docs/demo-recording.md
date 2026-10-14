# Demo recording

The README opens with a GIF because that is the only part of this repository a
reader is guaranteed to look at. This file is the shot list, so the recording is
repeatable rather than improvised.

## Before recording

1. Give the provider selected on Vexa's `AIConversationManager` a key so the dialogue
   is real model output. SampleScene uses DeepSeek, so that is `DEEPSEEK_API_KEY`,
   either as an environment variable or pasted into `Digital Human > API Keys`. Replies
   that start with `[offline mode]` mean no key was found. For a voice, switch
   `VoiceController`'s service field from `MockVoiceService` to
   `OpenAITtsVoiceService`, which reads `OPENAI_API_KEY`. An offline-mode recording is
   not worth publishing.
2. On `Vexa`, enable `ResponseAnnotationDriver.logAnnotations` while rehearsing so
   you can confirm the tags are landing, then turn it off before the take.
3. Confirm `TrajectoryDebugRenderer.visible` is on. The overlay only draws while a
   plan is executing, so it will not clutter the dialogue shots.
4. Reposition the three pickup boxes so at least one sits between the player's
   position and Vexa. Without a blocking obstacle the planned path is a straight
   line and the whole point of the visualisation is lost.
5. Run `unity command run_tests` and confirm it is green. A broken take is cheaper
   to discover before recording than after.

## Scripted take

`ChatDemoDirector`, on the `DemoDirector` object in SampleScene, plays
`Assets/DigitalHuman/Demo/FullActionDemo.txt` through the real chat box. Every line is
typed into the input field and sent the way a player would send it, so the replies, tags
and actions on screen all come from the normal pipeline. After each line the director
waits for the reply, then for the body to come to rest (no command, plan, NavMesh route,
gesture or speech) before it types the next one.

- **Run:** press **F5** anywhere in the editor, or use
  `Digital Human > Demo > Play Full Action Demo`. It opens SampleScene if another scene is
  open, enters Play mode and starts the script. Pressed in Play mode, it restarts Play mode
  first, so every take starts from the scene as saved. **F6** stops the script and leaves
  Play mode running.
- **Covers:** a wave, an open question (thinking state, gaze aversion), a jump, picking up
  the green box and dropping it, fetching the red box and putting it on the obstacle,
  turning to face the player, a planned walk with the path overlay that is stopped
  mid-route and then resumed, the blue box picked up with different wording, a concerned
  face, and a goodbye wave. About a minute and a half offline; a live model adds its
  latency to every line.
- **Editing:** plain text, one chat line per row. Rows starting with `#` are comments, and
  rows starting with `@` are stage directions that never reach the chat box:

  | Direction | Effect |
  | --- | --- |
  | `@camera overview`, `@camera player`, `@camera npc front\|side\|back\|free` | Moves the camera. Shot changes glide rather than cut. |
  | `@wait 2.5` | Pause. |
  | `@hold 1.2` | Pause after every finished action from here on. |
  | `@typing 16` | Typing speed in characters per second. |
  | `@nowait` | Send the next line without waiting for its action, for example to interrupt a walk with "Stop!". |

  A malformed direction is logged with its line number and skipped.
- **Demo-only behaviour:** while the script runs, "come over here" walks to the player
  from anywhere. Normally Vexa only approaches a player inside her 3 m interaction range,
  which from the default spawn is a walk of about a metre. Untick
  `approachPlayerFromAnywhere` on the director to record the normal behaviour.

## Camera

`ThirdPersonFollowCamera` on Main Camera:

| Input | Effect |
| --- | --- |
| F1 | Follow the player, over the shoulder. |
| F2 | Follow Vexa. Press again to cycle front, side and back. |
| F3 | The scene's original fixed overview. |
| Right-drag | Orbit. On Vexa this switches to a free angle until F2 is pressed. |
| Scroll | Zoom. |

The camera pulls in when a wall or box comes between it and its target, and ignores the
player and Vexa themselves.

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
