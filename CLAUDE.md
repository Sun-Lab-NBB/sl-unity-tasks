# Claude Code Instructions

## Session start behavior

At the beginning of each coding session, before making any code changes, you should build a comprehensive understanding
of the codebase by invoking the `/explore-codebase` skill (`automation` plugin). This keeps you aligned with the Unity
architecture and the existing patterns, and it guards the MQTT contract shared with `sollertia-experiment`.

## Autonomy boundaries

`.claude/rules/autonomy-boundaries.md` autoloads alongside this file and records which corridor work is
agent-autonomous, which trigger zone work is recipe-bound, and which changes you MUST escalate to the human supervisor.

## Style guide compliance

You MUST invoke the appropriate skill before performing ANY of the following tasks:

| Task                                          | Skill to invoke   |
|-----------------------------------------------|-------------------|
| Writing or modifying C# code                  | `/csharp-style`   |
| Writing or modifying README files             | `/readme-style`   |
| Writing git commit messages                   | `/commit`         |
| Writing or modifying skill files or this file | `/skill-design`   |
| Creating or verifying project structure       | `/project-layout` |
| Auditing a completed implementation task      | `/audit-project`  |

Each skill contains a verification checklist that you MUST complete before submitting any work. Failure to invoke the
appropriate skill results in style violations that block release.

## Cross-referenced library verification

This project depends on `sollertia-shared-assets`, which relays MCP tool calls into the `McpBridge` HTTP listener, and
it exchanges MQTT 5.0 traffic with `sollertia-experiment`. Local clones of both libraries typically sit alongside this
repository in the same parent directory.

**Before writing code that interacts with a cross-referenced library, you MUST:**

1. **Check for local version**: Look for the library in the parent directory (e.g., `../sollertia-shared-assets/`,
   `../sollertia-experiment/`).
2. **Compare versions**: If a local copy exists, compare its version against the latest release or main branch on
   GitHub. Read the local `pyproject.toml` for the current version and use
   `gh api repos/Sun-Lab-NBB/{repo-name}/releases/latest` to check the latest release.
3. **Handle version mismatches**: If the local version differs, notify the user and offer either fetching the
   documentation and API details from the GitHub repository or pulling the latest changes locally before proceeding.
4. **Proceed with correct source**: Use whichever version the user selects as the authoritative reference for API
   usage, patterns, and documentation.

**Why this matters**: Skills and documentation may reference outdated APIs, so always verify against the actual
library state to prevent integration errors.

## Available skills

The sollertia marketplace ships a `unity` plugin whose skills drive the `McpBridge` relay tools and an `assets` plugin
that registers the backing `slsa mcp` server. The ataraxis marketplace ships the `automation` plugin. Install all three.

| Skill                           | Description                                                               |
|---------------------------------|---------------------------------------------------------------------------|
| `/unity-mcp-environment-setup`  | Diagnose the `localhost:8090` `McpBridge` HTTP relay                      |
| `/task-scenes`                  | List, open, and inspect Unity scenes, and enumerate Unity assets          |
| `/task-prefabs`                 | Generate, inspect, validate, and delete task prefabs from YAML templates  |
| `/zone-prefabs`                 | Clone a base zone prefab into a new trigger zone prefab via the MCP tool  |
| `/task-parameters`              | Read and write the consolidated `Window → Task Parameters` editor surface |
| `/play-mode`                    | Enter, exit, and query Editor Play Mode                                   |
| `/mqtt-contract`                | Catalog of every MQTT topic Unity publishes or subscribes to              |
| `/task-generator`               | Reference for the `CreateTask` pipeline and hand-authored zone prefabs    |
| `/gimbl-framework`              | Reference for the inlined GIMBL VR framework (Actor, MQTT, Displays)      |
| `/scene-setup`                  | Configure the Display rig, controllers, and UI feedback                   |
| `/task-templates`               | Author and validate reusable Unity `TaskTemplate` YAMLs                   |
| `/experiment-configuration`     | Author per-project experiment configurations that reference a template    |
| `/library-extension`            | Orchestrate cross-cutting changes to the shared-assets vocabulary         |
| `/assets-mcp-environment-setup` | Diagnose and resolve `slsa mcp` server connectivity issues                |
| `/explore-codebase`             | Perform in-depth codebase exploration at session start                    |
| `/csharp-style`                 | Apply Sollertia platform C# conventions (REQUIRED for C# changes)         |
| `/readme-style`                 | Apply Sollertia platform README conventions (REQUIRED for READMEs)        |
| `/project-layout`               | Apply Sollertia platform project directory structure conventions          |
| `/skill-design`                 | Generate, update, and verify skill files and this CLAUDE.md               |
| `/commit`                       | Stage all changes and create a style-compliant commit, without pushing    |
| `/pr`                           | Draft a style-compliant pull request summary for the active branch        |
| `/release`                      | Draft style-compliant release notes from the merged pull requests         |
| `/audit-project`                | Orchestrate the four audits and merge their findings                      |
| `/audit-correctness`            | Hunt active and latent bugs the test suite leaves uncaught                |
| `/audit-facts`                  | Fact-check documentation against its authoritative source code            |
| `/audit-performance`            | Audit cost, memory layout, and numeric width predictability               |
| `/audit-style`                  | Audit files against the applicable style skill checklists                 |

The `assets` plugin ships further session, project, and data-management skills that this repository does not use. You
MUST invoke `/library-extension` when adding a new `TriggerType` member or otherwise extending the shared-assets
template vocabulary, because the Python registry parity check fails at import time if a downstream entry is missing.
A new member does NOT require a `from_task_template` branch in every acquisition system, because a system may leave a
mode unmapped and a config using it then raises a clear "not mapped to a runtime trial class" error.

## MCP server

This project does not host a standalone MCP server. The `McpBridge` editor plugin
(`Assets/InfiniteCorridorTask/Scripts/Editor/McpBridge.cs`) starts an HTTP listener on `127.0.0.1:8090`, `[::1]:8090`,
and `localhost:8090` when the Unity Editor loads. The backing MCP server is `slsa mcp` from `sollertia-shared-assets`,
whose `interfaces/unity_tools.py` module relays each tool call to the bridge over HTTP. The bridge dispatches 15 tools
in `McpBridge.Dispatch`, the README's "Editor MCP Bridge" section is the catalog, and `McpBridge.cs` is the source of
truth. Four conventions bind any new tool:

- Handlers run on the editor thread after `EditorApplication.update` drains the `ConcurrentQueue`, so they may call
  Unity APIs freely.
- Every response goes through the shared `Ok(payload)` and `Error(message)` helpers, which always include a `success`
  boolean.
- `delete_asset` is bounded by `DeleteAllowedPrefixes` and `DeleteProtectedPaths` at the top of `McpBridge.cs`, and
  `delete_task` checks `DeleteProtectedPaths` itself before removing a scene, so neither tool reaches a hand-authored
  asset. Scene deletion goes through `delete_task` so the per-scene companion cascade is never bypassed, and any
  future per-scene companion joins `McpBridge.TryDeleteScenePerSceneCompanions` in the same change. Asset paths are
  built as forward-slash literals rather than through `Path.Combine`, whose Windows output fails those comparisons.
- `read_task_parameters` and `write_task_parameters` share one `AcquireSceneComponents` walk per request, so reads and
  writes see a consistent snapshot of the active scene.

For bridge connectivity issues invoke `/unity-mcp-environment-setup`, and for backing `slsa mcp` issues invoke
`/assets-mcp-environment-setup`.

## Downstream library integration

This project is one corner of the Sollertia data-acquisition triangle. Changes to MQTT topics, YAML schema, or the
bridge surface ripple through the other two libraries.

- **sollertia-experiment** (acquisition runtime). The MQTT counterparty for every topic in `MQTTTopics`. Owns the
  publish side of `CueSequenceTrigger`, `SceneNameTrigger`, `RequireInteraction`, `RequireWait`, `Motion`, and the
  hardware side of `Interaction`. Subscribes to `SessionStart`, `SessionStop`, `Stimulus`, `Delay`, `CueSequence`, and
  `SceneName`. Topic renames here require an in-lockstep update on the experiment side, and `/mqtt-contract` is the
  canonical index for both ends.
- **sollertia-shared-assets** (configuration schema and MCP relay). Owns the Python `TaskTemplate` plus the `Cue`,
  `TrialStructure`, and `VREnvironment` schema dataclasses it composes. `interfaces/unity_tools.py` is the HTTP client
  for `McpBridge`, so adding a bridge tool requires a matching `@mcp.tool()` wrapper there. Schema changes must land in
  both repositories before the templates that use them parse successfully.

## Companion library synchronization

The `TaskTemplate`, `Cue`, `TrialStructure`, and `VREnvironment` classes under `Assets/InfiniteCorridorTask/Scripts/`
are a mirror of the Python originals in `sollertia-shared-assets`, and you MUST keep them in lockstep. When the Python
schema gains a field, add a matching `[Serializable]` field to the C# class. The YAML deserializer runs
`UnderscoredNamingConvention`, so the C# member name is the camelCase counterpart of the underscored YAML key, meaning
`cue_offset_cm` becomes `cueOffsetCm`.

## Distribution model

The Unity project source, the `McpBridge` relay, and the task templates live in this repository. Claude Code skills and
MCP server registration are distributed separately through two marketplaces, so a skill edit lands in the owning
marketplace repository rather than here. A bridge tool change edits `McpBridge.cs` here plus its `@mcp.tool()` wrapper
in `sollertia-shared-assets`.

## Project context

This is **sollertia-virtual-reality**, a Unity 6 C# project that produces VR behavioral tasks for the Sollertia
mesoscope data-acquisition platform, built on the Ataraxis framework and developed in the Sun (NeuroAI) lab at Cornell
University. Tasks are infinite linear corridors built from prefabricated visual cue segments and driven over MQTT 5.0
by `sollertia-experiment`.

### Key areas

| Directory                                     | Purpose                                                             |
|-----------------------------------------------|---------------------------------------------------------------------|
| `Assets/InfiniteCorridorTask/Scripts/`        | Runtime C# (`Task`, zones, `ConfigLoader`, schema mirror classes)   |
| `Assets/InfiniteCorridorTask/Scripts/Editor/` | `CreateTask`, `McpBridge`, `TaskEditor`, `MiniJson`                 |
| `Assets/InfiniteCorridorTask/Configurations/` | YAML task templates                                                 |
| `Assets/InfiniteCorridorTask/Cues/`           | Generated cue prefabs (length-suffixed, shared across templates)    |
| `Assets/InfiniteCorridorTask/Prefabs/`        | Hand-authored zone prefabs and generated segment prefabs            |
| `Assets/InfiniteCorridorTask/Tasks/`          | Generated task prefabs (one per template)                           |
| `Assets/InfiniteCorridorTask/Materials/`      | Generated cue materials and the canonical `_CueShaderReference.mat` |
| `Assets/InfiniteCorridorTask/Textures/`       | Cue textures plus the floor and target pattern source art           |
| `Assets/UI-lick-reward/`                      | On-screen lick and stimulus feedback canvas                         |
| `Assets/Gimbl/`                               | Inlined GIMBL runtime plus the `MainWindow` Task Parameters editor  |
| `Assets/Scenes/`                              | `ExperimentTemplate.unity` plus per-task generated scenes           |
| `Assets/VRSettings/Displays/`                 | Display settings and per-scene `savedFullScreenViews` companions    |
| `Assets/Plugins/`                             | Inlined `MQTTnet.dll` and `YamlDotNet.dll`                          |
| `Assets/Tests/`                               | Edit Mode and Play Mode test assemblies plus their support helpers  |

### Architecture

- **Schema mirror**: `TaskTemplate`, `Cue`, `TrialStructure`, and `VREnvironment` mirror the Python `YamlConfig`
  classes in `sollertia-shared-assets`. `ConfigLoader.LoadTemplate` deserializes via `YamlDotNet` and validates cue
  codes, the template and trial name pattern, the `trigger_type` literal set, per-trial `cue_sequence` uniqueness, a
  positive `occupancy_duration_ms`, the transition targets and per-target probability range, and every
  `vr_environment` geometry scalar. Per-mode geometric zone validation lives in the shared-assets Python
  `TaskTemplate`, not here.
- **Task runtime**: `Task` (`Assets/InfiniteCorridorTask/Scripts/Task.cs`) keys a `_corridorMap` by a base-`trialCount`
  encoding of the current segment combination and pre-generates the random maze sequence with an optional seed. It
  teleports the actor to the next corridor once the current corridor's first segment is traversed.
- **Zone composition**: `StimulusTriggerZone` dispatches on a `TriggerMode` enum that `CreateTask` sets from the
  trial's `trigger_type`. Every mode publishes one `StimulusMessage { trialName, delivered, cause }` per trial on the
  `Stimulus` topic and adds no MQTT topics, where `cause` is `behavior` or `guidance`. `OccupancyZone` exposes a
  generic `occupancyMet` signal and the parent applies the per-mode rule. `Task.FindResettableZones` caches the
  `StimulusTriggerZone`, `OccupancyZone`, and `OccupancyGuidanceZone` instances at `Start` and the corridor advance
  drives every per-lap reset, so a standalone `IResettable` needs its own `FindObjectsByType` line there. See
  `/zone-prefabs`.
- **CreateTask pipeline**: `CreateTask.CreateFromTemplate` runs a cross-template cue-texture preflight, regenerates
  every segment prefab the template owns, reuses cue prefabs keyed by `(name, lengthCm)`, and places the zones. All
  five `trigger_type` literals dispatch onto the two hand-authored zone prefabs. See `/task-generator`.
- **Task Parameters window**: `MainWindow` (`Assets/Gimbl/Editor/MainWindow.cs`) is the only configuration surface for
  the Actor, MQTT, Display, Camera Mapping, and Task fields, and `TaskEditor` replaces the default `Task` Inspector
  with a HelpBox pointing at it. See `/task-parameters`.
- **MQTT client**: `MQTTClient` (`Assets/Gimbl/Scripts/MQTT/MQTTClient.cs`) wraps `MQTTnet` in
  `MqttProtocolVersion.V500` and falls back to `127.0.0.1:1883` when `EditorPrefs` is unset. When the broker is
  unreachable, `Publish` routes in-process so keyboard-only test runs still reach local subscribers. `McpBridge` is an
  `[InitializeOnLoad]` static class draining an `HttpListener` queue on `EditorApplication.update`.

### Extension contracts

| Extension                | Touch points                                                             | Skill              |
|--------------------------|--------------------------------------------------------------------------|--------------------|
| New task template        | YAML in `Configurations/`, materialized via `/task-prefabs`              | `/task-templates`  |
| New cue texture          | PNG in `Textures/`, referenced from a YAML `texture` field               | `/task-templates`  |
| New trigger zone type    | Zone script, prefab, `ConfigLoader` literal, `CreateTask` branch, Python | `/zone-prefabs`    |
| New MQTT topic           | `MQTTTopics` constant, plus both ends of the experiment contract         | `/mqtt-contract`   |
| New `McpBridge` tool     | `Dispatch` case, handler, `@mcp.tool()` wrapper in `unity_tools.py`      | n/a (manual)       |
| New treadmill controller | `ControllerObject` subclass and a `ControllerTypes` enum entry           | `/gimbl-framework` |

Each row's skill owns the full touch list, so invoke it before starting the extension. The `McpBridge` tool row is the
exception no skill owns: add a `Dispatch` case and a handler returning `Ok(...)` or `Error(...)`, fold a scene-touching
tool into `AcquireSceneComponents` and `BuildSnapshot`, and update the README's bridge table in the same change.

### Code standards

- Unity `6000.3.22f1` (Unity 6), compiled against the .NET Standard 2.1 API compatibility profile, Apache 2.0 licensed.
- 120 character line limit enforced by CSharpier (`.csharpierrc.yaml`), with naming, brace style, and spacing enforced
  by `.editorconfig`.
- Allman brace style, `_camelCase` private fields, PascalCase public properties and methods, camelCase Inspector
  fields, and XML documentation on every public and private member. See `/csharp-style` for the full checklist.
- Every script compiles into a named assembly declared by an `.asmdef`, because a test assembly is unable to reference
  Unity's predefined `Assembly-CSharp`. A new script folder sits inside an existing assembly's subtree or declares its
  own `.asmdef` and is referenced from the assemblies that consume it. The README's "Assembly Definitions" section is
  the catalog.

### Project-specific conventions

- **Hand-authored vs generated assets**: `Padding.prefab`, `StimulusTriggerZone.prefab`,
  `OccupancyTriggerZone.prefab`, `Materials/_CueShaderReference.mat`, `Materials/Floor.mat`, `Materials/Wall.mat`,
  `Materials/TargetMat.mat`, and `Scenes/ExperimentTemplate.unity` are hand-authored. Everything under `Cues/`, every
  segment prefab under `Prefabs/`, every `Cue_*_*cm.mat`, every prefab under `Tasks/`, and every scene other than
  `ExperimentTemplate.unity` is generated by `CreateTask`. All eight hand-authored assets are protected by
  `McpBridge.DeleteProtectedPaths`, you MUST NOT remove entries from that list, and any new asset the pipeline
  references by hardcoded path or serialized link joins the protected set in the same change.
- **Cue identity**: Cue prefabs and materials are keyed by `(cue.name, cue.length_cm)` and shared across templates.
  `CreateTask.ValidateCueDefinitionsAcrossTemplates` refuses to generate when two templates declare the same
  `(name, length)` pair with different textures, and `BuildCuePrefabs` aborts when a cached cue material was built from
  a different texture. Resolve a conflict by renaming the cue, changing its length, or unifying the textures.
- **Segment naming and regeneration**: A segment prefab is named `TemplateName-TrialName`, and `ConfigLoader` excludes
  the hyphen from both halves so the joined name splits back to exactly one owning template.
  `CreateTask.CleanGeneratedSegments` deletes the segments the template owns, after `BuildCuePrefabs` has succeeded so
  a cue abort never strips the previous generation. Cue prefabs and materials are preserved across runs.
- **MQTT topics**: Topics are flat PascalCase identifiers with no hierarchical separators, declared as
  `public const string` in `MQTTTopics.cs`, and updated on both sides of the contract in the same release. The client
  connects with `MqttProtocolVersion.V500`, so brokers must accept MQTT 5.0 connections (Mosquitto `2.0+`).
- **Inspector vs Parameters window**: The `Task` component's public fields are `[HideInInspector]`, so configure every
  task field through `MainWindow` rather than the Inspector.

### Workflow guidance

**Authoring a new task template**: invoke `/task-templates` for the schema and naming convention, place the YAML under
`Assets/InfiniteCorridorTask/Configurations/` with the `Project / Purpose / Layout / Related` header comments, then
invoke `/task-prefabs` and run `create_task_tool` to materialize the cues, segments, task prefab, and scene together.
The tool refuses to overwrite an existing scene, so pair `delete_task_tool` with `create_task_tool` to regenerate.
Finish with `inspect_prefab_tool` to spot-check the hierarchy against the template's cue and trial counts.

**Modifying a runtime zone or `Task.cs`**: invoke `/csharp-style`, and `/mqtt-contract` when the change touches MQTT.
Preserve the `IResettable` contract on any zone holding per-lap state, and register a new implementer in
`Task.FindResettableZones`.

**Modifying the `CreateTask` pipeline or `McpBridge`**: invoke `/task-generator` for the generation pipeline, and read
`McpBridge.cs` directly for the relay surface. Keep the cross-template cue-texture preflight intact, so new branches
run after it rather than before. New `delete_asset` paths require additions to `DeleteAllowedPrefixes` and new
hand-authored assets require additions to `DeleteProtectedPaths`, because updating one without the other leaves the
bridge unsafe, and a new tool also needs its `@mcp.tool()` wrapper in `unity_tools.py`.

**Reading or writing Task Parameters**: invoke `/task-parameters`, which owns `read_task_parameters_tool` and
`write_task_parameters_tool`. Always read the current snapshot first, because the response carries `options` (the
allow-list for each enum field) and `visibility` (whether each conditionally-rendered control is rendered), and writes
that violate either are rejected with a descriptive error. Editor-time writes to `task.require_interaction` and
`task.require_wait` are zone-gated, so publish on the matching MQTT topics for mid-run toggles.

**Adding or running tests**: `Assets/Tests/EditMode/` drives the private Unity lifecycle callbacks through the Support
assembly's `PrivateAccess` helper, so it stays deterministic without frames or physics. A test belongs in
`Assets/Tests/PlayMode/` when it needs real frames, real trigger callbacks, real elapsed time, or the engine-invoked
`Awake`, `OnEnable`, `Start`, and `OnDestroy` ordering. `Assets/Tests/Support/` holds the `PrivateAccess` reflection
accessor, the staged template workspace, the task template YAML builder, the in-process MQTT harness, and the trigger
zone rig that both assemblies draw on. Run the suite from `Window → General → Test Runner`, or headlessly with
`Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults out.xml`, which requires the
Editor closed on that project because Unity holds a per-project lock.

**Before committing**: run `csharpier format .`, run both test platforms, invoke `/audit-project` to run the four
audits over the changed files, and invoke `/commit` to stage and write the message. The Editor menu
`CreateTask → New Task` and a single `create_task_tool(template_name=…)` call share `CreateTask.CreateFromTemplate` and
`CreateTask.CreateSceneFromTemplate`, so the agentic and manual paths produce byte-equivalent assets.
