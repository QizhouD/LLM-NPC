# Embodiment behaviours and why they are there

Every behaviour in the embodiment layer is either required by the planner contract
or chosen because it has a basis in the human-behaviour literature. This file
records which is which, so the demo can be described accurately.

## Layer order

Three components write to the same skeleton every frame, in this order:

| Order | Component | Writes |
| --- | --- | --- |
| Animator (built-in) | `DigitalHuman.controller` on `Vexa_Visual` | whole-body clip pose |
| 100 | `ProceduralHumanoidAnimator` | root height, torso, head, upper arms, upper legs, knees |
| 200 | `ProceduralFaceController` | eyeballs, eyebrows, eyelashes, head and neck gaze |
| 300 | `SpeechHeadMotion` | head and neck, driven by voice loudness |

All three procedural components run in `LateUpdate` and add to the result of the
previous stage instead of replacing it. The order is pinned with
`[DefaultExecutionOrder]` rather than left to Unity's arbitrary component
ordering.

### Why the additive layers go through `AdditiveChannel`

Reading a bone, adding an offset and writing it back is only stable while
something else rewrites that bone every frame. An Animator writes only the bones
the current clip has curves for, and never its own transform. For every other
bone the read-back **is last frame's output**, so treating it as the base folds
the previous offset in and the bone gains one offset per frame.

This shipped as a bug: the walk bounce was a non-negative offset on the visual
root, so it accumulated and lifted the character off the floor while walking,
which also made the only visible motion vertical. `AdditiveRotationChannel` and
`AdditivePositionChannel` close the hole by remembering what they wrote and
adopting a new base only when the current value differs from it.
`AdditiveChannelTests` pins the invariant: a constant offset held for 600 frames
must still measure as one offset.

## Locomotion

The Vexa package ships no walk or run clip, so the stride is synthesised:
`StrideGait` holds the geometry and `ProceduralHumanoidAnimator` applies it.

- Hip, knee and shoulder swings turn about the **body's** left-right axis,
  converted into each bone's frame. On the Vexa armature local X runs along each
  limb, so an offset about local X twists a leg in place instead of swinging it.
  This shipped as a bug: thigh swing measured 0° and the feet moved 5 mm while
  the toes yawed ±27°, so the legs never opened into a stride.
- Stride phase advances with **distance covered**, not time:
  `phase += speed·dt / strideLength`, with `strideLength = 4·legLength·sin(amplitude)`.
  During stance the foot's offset from the hip falls at a constant rate, so the
  planted foot moves back exactly as fast as the body moves forward and stays on
  the floor at any speed. The old fixed 2.4 Hz cadence covered 0.58 m per cycle,
  which read as shuffling. Integrating, rather than deriving the phase from
  `Time.time`, also keeps the legs from jumping when the speed changes.
- Swing returns the foot along a cubic whose end slopes match stance, so the hip
  does not snap at heel strike or toe off. The knee bends only during swing, and
  the hips drop while the straight stance leg is pitched away from vertical, as
  an inverted pendulum would. `hipDropScale` takes only part of that drop because
  heel and toe roll lengthen a real leg.
- Hip amplitude scales with the square root of normalised speed and cadence
  follows from it, so slowing down shortens the step and slows the cadence
  together instead of just playing the same stride slower.
- Turn rate produces an inward torso and head lean. Leaning into a turn is what
  keeps the centre of mass over the support foot; without it a turning character
  reads as sliding.
- Three speeds should agree: the stride's `referenceSpeed`, the mock planner's
  `cruiseSpeed`, and the `NavMeshAgent.speed`. All are 1.4 m/s. The stride stops
  lengthening at `referenceSpeed` and only the cadence rises beyond it, so the
  agent's Unity default of 2.6 m/s, a jog, plays a walk at three steps a second.
- The mock planner replans every second and starts each plan from the speed the
  body already has. Starting from its 0.35 m/s floor instead made the body surge
  and stall once a second while the legs kept walking.

The character can be driven along either of two paths, and both have to feed the
Animator. `TrajectoryPlanFollower` calls `SetMotion` while following a plan;
otherwise `MotionIntentAnimator` falls back to the `NavMeshAgent`'s own velocity
after `plannerSampleTimeout`. Without that fallback the `Speed` parameter sits at
zero for the whole NavMesh path, which leaves the blend tree and the stride in
idle while the body slides across the floor.

This is a placeholder for real locomotion clips, not a research contribution. The
blend tree already has a `walk` slot wired for an imported clip; see
[rig-and-animation.md](rig-and-animation.md).

## Blinking

Inter-blink intervals are drawn from an exponential distribution (a Poisson
process) with a mean of 4 s at rest and 2.4 s while speaking. Spontaneous blink
rate does rise measurably during speech, and a fixed interval is one of the most
reliable ways to make a face look mechanical.

Closing occupies the first 35% of the blink and opening the remaining 65%,
because eyelid closure is faster than reopening.

## Gaze

- **Eye range** is clamped to ±32° yaw and ±22° pitch. Beyond roughly this range
  a person recruits the head rather than rotating the eyes further, and unclamped
  eyeballs rotate into the skull.
- **Head follow** is a fraction of eye yaw (0.35) with the neck taking a further
  fraction (0.2), giving the layered eye-then-head onset that real gaze shifts
  have.
- **Saccades** add small offsets (about ±2°) at intervals between 0.18 s and
  0.7 s. Gaze is never perfectly still; a locked stare is uncanny.
- Gaze angles are measured against the **body** forward, not the head forward.
  Measuring against the head would feed head follow back into its own input and
  produce a slow drift.

## Gaze aversion

The character averts its gaze while in the `Thinking` state, and briefly at
random while `Speaking`.

This is the one behaviour chosen specifically for its research basis rather than
its looks. Gaze aversion increases with cognitive load, and averting gaze
measurably improves performance on demanding questions: looking at a face is
itself a competing cognitive load. Modelling "thinking" as *looking away* rather
than as a spinner or a canned thinking animation is the interesting move, because
it makes the model's latency legible as a behaviour instead of as a UI artifact.

Relevant background: Argyle and Cook on gaze and mutual gaze, and
Doherty-Sneddon and Phelps on gaze aversion as a cognitive-load response.

## Eyebrow emotion

Five bounded values: `neutral`, `curious`, `happy`, `concerned`, `surprised`.

The vocabulary is small on purpose. With no blend shapes and no jaw, eyebrows and
eyelids are the entire available expressive channel, and any value the language
model emits has to map to a pose those bones can actually reach. Anything outside
the vocabulary degrades to `neutral`.

Brow rotations are mirrored on the right side so the pair moves symmetrically on
a mirrored skeleton.

## Speech-driven head motion

`SpeechHeadMotion` reads the RMS of the AudioSource's current output buffer
through `GetOutputData` and turns it into head motion: loudness contributes a
downward nod, and a Perlin field gated by that same loudness adds yaw and roll.

This is the **substitute** for lip sync, not an approximation of it. Since the
mouth cannot move at all, the head carries the whole "this body is producing this
sound" signal. The envelope is smoothed asymmetrically — a 60 ms attack so
syllable onsets survive, a 220 ms release so the tail does not chatter.

`Envelope` is exposed so an upper-body gesture layer can be weighted by speech
intensity later.

## Voice

`OpenAITtsVoiceService` requests `/v1/audio/speech` with
`response_format: "pcm"`, which returns headerless signed 16-bit little-endian
mono at 24 kHz. Decoding that is a loop over the byte array into a float buffer,
which is why it was chosen over MP3 or WAV: no codec dependency and no temp file.
The key comes from `OPENAI_API_KEY` in the environment, the same convention
`OpenAILLMService` uses, so nothing secret is ever in the project.

`MockVoiceService` remains the default in the prefab. It generates a silent clip
of the right length, which is enough to exercise the state machine without a key
or a network call.

## What this character cannot do

State these plainly rather than letting a viewer assume otherwise:

- **No lip sync and no visemes.** No blend shapes, no jaw bone. Mouth shape is
  baked into the texture.
- **No facial expression capture.** Expression is eyebrows and eyelids only.
- **No retargeted mocap.** The rig is Generic; see
  [rig-and-animation.md](rig-and-animation.md) for the Humanoid conversion
  attempt and why it was reverted.
