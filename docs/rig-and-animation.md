# Rig and Animation Notes

The Vexa character ships as a **Generic** rig and stays that way. This document
records why, and what to do if you want Humanoid retargeting later.

## Current setup

| Item | Value |
| --- | --- |
| Character mesh | `Assets/Vefects/Stylized Female Character - Vexa/Meshes/Vefects_Vexa.fbx` |
| Rig type | Generic (`animationType: 2`) |
| Bone naming | `Base Human*`, for example `Base HumanPelvis`, `Base HumanLThigh` |
| Shipped clips | 25, one per `*_Vexa.fbx` under `Animations/` |
| Locomotion clips | **none** — no walk or run clip exists in the package |
| Blend shapes | **none** (`m_BlendShapeWeights: []`) |
| Jaw bone | **none** |
| Face bones | `L/REyeBall`, `L/REyeBrow`, `L/RUpperLash`, `L/RLowLash` |
| Wrist bones | `Base HumanL/RPalm`; fingers are `Base HumanL/RDigitNN` |

Two consequences drive the whole animation design:

1. **No viseme lip sync is possible.** There are no blend shapes and no jaw
   bone. Mouth shape is baked into the texture. Do not claim lip sync.
2. **No locomotion clip is available.** Walking is produced procedurally by
   `ProceduralHumanoidAnimator`, which layers additive bone offsets on top of
   whatever clip the `Animator` is playing.

Because the face bones are outside any `Animator` clip, they are free for
`ProceduralFaceController` to drive directly. Keeping the rig Generic preserves
that freedom.

### Nothing in this armature is called "Hand"

The wrist is `Base HumanRPalm` and the fingers are `Base HumanRDigitNN`, so a
bone search for `Hand` returns nothing on Vexa. The prefab still carries the
older placeholder model under `Vexa/Model/DigitalHuman_3D`, whose rig *does* use
`RightHand`, and `AgentInteraction.rightHand` was left pointing at it. A
picked-up item therefore parented to a skeleton nothing animates and hung in
mid-air while the visible arm swung past it.

`DigitalHumanSceneTool.WireHeldItemHandInScene` now resolves the wrist against a
list of spellings (`RPalm`, `RightHand`, `RHand`, ...) and searches only under
the transform that owns the *enabled* Animator, so it cannot pick the dead rig.
If you swap the character, check that list first.

## Humanoid conversion: what happened when we tried

`Assets/DigitalHuman/Scripts/Editor/RigSetupTool.cs` automates the conversion so
the experiment is repeatable. Menu: **Digital Human > Rig**.

The conversion itself works. Unity's auto-mapper resolves the non-standard
`Base Human*` names without help:

```text
animationType: Human
avatar found: True
isValid: True
isHuman: True
human bone mappings: 49
unmapped required bones: none
```

Two problems made it not worth keeping:

### 1. `CopyFromOther` destroys the shipped clips

Setting each animation FBX to `animationType = Human` with
`avatarSetup = CopyFromOther` and `sourceAvatar` pointing at the character
avatar silently drops the `AnimationClip` from 24 of the 25 files:

```text
AirDash_Vexa: NONE
AirStomp_Vexa: NONE
...
IdleChill_Vexa: [preview],IdleChill    <- the only survivor
```

Use `RigSetupTool.ListAnimationClipNames()` to verify this on any change. If you
retry the conversion, set `avatarSetup = CreateFromThisModel` on each animation
FBX instead, so every file builds its own avatar from its own embedded rig.

### 2. Auto-mapping puts `Jaw` on an eyelash bone

The auto-mapper produces several bindings that are wrong for this rig:

| Humanoid slot | Auto-mapped bone | Should be |
| --- | --- | --- |
| `Jaw` | `Base HumanRLowLash` | unmapped (no jaw exists) |
| `LeftHand` | `Base HumanLForearm2` | `Base HumanLPalm` |
| `LeftLowerArm` | `Base HumanLUpperarm3` | `Base HumanLForearm1` |
| `Left Thumb Proximal` | `Base HumanLDigit21` | `Base HumanLDigit11` |

The arm chain is `Collarbone -> Upperarm1/2/3 -> Forearm1/2/3 -> Palm -> Digits`,
where `Upperarm2/3` and `Forearm2/3` are twist bones. The mapper treats twist
bones as joints, which shortens the arm and places the hand mid-forearm.

`Jaw -> Base HumanRLowLash` is the worst one: the `Animator` would then own the
right lower eyelash and fight `ProceduralFaceController`'s blink.

## If you want Humanoid retargeting later

Motivation would be Mixamo or other external clip libraries, plus foot IK.

1. Download the clips you need from Mixamo as **FBX for Unity, without skin**
   and put them under `Assets/DigitalHuman/Animations/External/`.
2. Run **Digital Human > Rig > Convert Character To Humanoid**.
3. In the character's Avatar **Configure** window, fix the four mappings in the
   table above and clear `Jaw`.
4. Convert only the external clips to Humanoid. Leave the 25 shipped Vefects
   clips Generic, or convert them with `CreateFromThisModel`.
5. Re-verify with **Digital Human > Rig > List Animation Clip Names**.

**Digital Human > Rig > Revert Character To Generic** undoes step 2.
`git checkout -- "Assets/Vefects"` undoes everything.
