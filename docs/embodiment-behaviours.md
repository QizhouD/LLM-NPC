# Embodiment behaviours and why they are there

Every behaviour in the embodiment layer is either required by the planner contract
or chosen because it has a basis in the human-behaviour literature. This file
records which is which, so the demo can be described accurately.

## Layer order

Three components write to the same skeleton every frame, in this order:

| Order | Component | Writes |
| --- | --- | --- |
| Animator (built-in) | `DigitalHuman.controller` on `Vexa_Visual` | whole-body clip pose |
| 100 | `ProceduralHumanoidAnimator` | root bounce, torso, head, upper arms, upper legs |
| 200 | `ProceduralFaceController` | eyeballs, eyebrows, eyelashes, head and neck gaze |

Both procedural components run in `LateUpdate` and read the current local rotation
as their base, so each one adds to the result of the previous stage instead of
replacing it. The order is pinned with `[DefaultExecutionOrder]` rather than left
to Unity's arbitrary component ordering.

## Locomotion

The Vexa package ships no walk or run clip, so the stride is synthesised.

- Stride phase is **integrated** (`phase += 2π·f·speed·dt`) rather than derived
  from `Time.time`. With a varying frequency, a `Time.time`-based phase makes the
  legs jump whenever the speed changes.
- Stride frequency and amplitude both scale with normalised root speed, so
  slowing down shortens the stride instead of just playing it slower.
- Turn rate produces an inward torso and head lean. Leaning into a turn is what
  keeps the centre of mass over the support foot; without it a turning character
  reads as sliding.

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

## What this character cannot do

State these plainly rather than letting a viewer assume otherwise:

- **No lip sync and no visemes.** No blend shapes, no jaw bone. Mouth shape is
  baked into the texture.
- **No facial expression capture.** Expression is eyebrows and eyelids only.
- **No retargeted mocap.** The rig is Generic; see
  [rig-and-animation.md](rig-and-animation.md) for the Humanoid conversion
  attempt and why it was reverted.
