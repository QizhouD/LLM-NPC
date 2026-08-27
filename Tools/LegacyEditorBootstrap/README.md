# Legacy editor bootstrap scripts

These 27 one-off `[MenuItem]`-less editor scripts were used to build
`Assets/Scenes/SampleScene.unity` incrementally (`Step3` through `Step18`, plus
the agent demo, environment, ground, generated-art and remote-UI passes). Each
one asserts its own step and was run once.

They live outside `Assets/` so Unity does not compile them. They are kept only
as a record of how the scene was assembled. The scene itself is the source of
truth now.

Do not move them back without reviewing them first: they reference component
APIs that have since changed, in particular `ProceduralHumanoidAnimator` and
`DigitalHumanAnimator`.
