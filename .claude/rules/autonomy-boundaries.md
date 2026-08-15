# Autonomy boundaries

This project supports exactly one VR paradigm, the **infinite linear corridor**. The boundary below records whether an
author-derived recipe exists, not whether you are capable. Past the boundary you MUST consult the human supervisor and
work in a generative, collaborative mode (co-design, get sign-off, co-implement) rather than executing autonomously.

**Within-corridor work is agent-autonomous.** A new corridor task variant runs end to end through templates and the
`McpBridge` relay without human intervention. Author the YAML template, materialize it with `create_task_tool` (see
`/task-templates` and `/task-prefabs`), and select any of the five existing `trigger_type` modes per trial
(`interaction`, `collision`, `occupancy_disarm`, `occupancy_arm`, `occupancy_trigger`). Then configure the generated
scene through `Window → Task Parameters` (see `/scene-setup` and `/task-parameters`), inspect the prefabs, and drive
Play Mode.

**New trigger zone types are agent-led but recipe-bound.** The recipe spans the `/zone-prefabs` clone workflow and its
worked examples, the `/task-generator` pipeline edits, and the `/library-extension` Python `TriggerType` registration.
It holds as long as the new mode is a zone modifier on a copied zone prefab whose root subclasses `StimulusTriggerZone`
and publishes the standard `Stimulus` event. This tier authors C# and clones a base prefab with
`clone_zone_prefab_tool`, so it MUST be verified with `inspect_prefab_tool`.

**Beyond the recipe, you MUST escalate to the human supervisor.** The items below have no author-derived recipe, so
treat them as collaborative, human-supervised work and do NOT attempt them autonomously:
- A new VR paradigm or topology (T-maze, Y-maze, open field, branching or 2D mazes), or any change to `Task.cs`
  traversal mechanics, the corridor encoding, or `CreateTask` corridor assembly. The invariants are baked into
  `Task.cs` (forward-only Z traversal, single-axis teleport, base-`trialCount` encoding) and `CreateTask.cs` (segment
  concatenation along Z, corridor spacing along X).
- A new scene topology or Display rig other than the corridor scene `create_task_tool` copies from
  `ExperimentTemplate.unity`.
- A trigger behavior that cannot be expressed as a zone modifier publishing the standard `Stimulus` event, meaning one
  that needs a new MQTT topic, new `Task.cs` runtime mechanics, or geometry outside a single corridor segment.
- A new `TaskTemplate` or `VREnvironment` field or class, which is a coordinated two-repo schema change with the Python
  originals in `sollertia-shared-assets`.

**New cue textures hand off cleanly to the user.** You cannot author PNG or other binary texture assets. When a template
needs a texture that is not already under `Assets/InfiniteCorridorTask/Textures/`, you MUST stop and state the intended
cue `name`, `code`, `length_cm`, and target filename. Then let the user supply the asset and loop you back to finish
generation. You MUST NOT let generation dead-end in a `Failed to load texture` error.
