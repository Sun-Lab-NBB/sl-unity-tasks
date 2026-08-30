**Authoring a new task template**: invoke `assets:task-templates` for the schema and naming convention, then place the
YAML under `Assets/InfiniteCorridorTask/Configurations/` with the `Project / Purpose / Layout / Related` header
comments. Invoke `/task-prefabs` and run `create_task_tool` to materialize the cues, segments, task prefab, and scene
together. The tool refuses to overwrite an existing scene, so pair `delete_task_tool` with `create_task_tool` to
regenerate. Finish with `inspect_prefab_tool` to spot-check the hierarchy against the template's cue and trial counts.

**Modifying a runtime zone or `Task.cs`**: invoke `/csharp-style`, and `/mqtt-contract` when the change touches MQTT.
Preserve the `IResettable` contract on any zone holding per-lap state, and register a new implementer in
`Task.FindResettableZones`.

**Modifying the `CreateTask` pipeline or `McpBridge`**: invoke `/task-generator` for the generation pipeline and
`/unity-mcp-environment-setup` for the relay surface. Keep the cross-template cue-texture preflight intact, so new
branches run after it rather than before. New `delete_asset` paths require additions to `DeleteAllowedPrefixes` and new
hand-authored assets require additions to `DeleteProtectedPaths`, because updating one without the other leaves the
bridge unsafe, and a new tool also needs its `@mcp.tool()` wrapper in `unity_tools.py`.

**Reading or writing Task Parameters**: invoke `/task-parameters`, which owns `read_task_parameters_tool`,
`write_task_parameters_tool`, and `refresh_monitors_tool`. Always read the current snapshot first, because the response
carries `options` (the allow-list for each enum field) and `visibility` (whether each conditionally-rendered control is
rendered), and writes that violate either are rejected with a descriptive error. Editor-time writes to
`task.require_interaction` and `task.require_wait` are zone-gated, so publish on the matching MQTT topics for mid-run
toggles.

**Adding or running tests**: invoke `/unity-tests`, which owns the suite, the Support helpers, the assembly catalog,
and the fixtures that pin the enum, topic, protected-asset, and bridge-tool contracts. `Assets/Tests/EditMode/` drives
the private Unity lifecycle callbacks through the Support assembly's `PrivateAccess` helper, so it stays deterministic
without frames or physics. A test belongs in `Assets/Tests/PlayMode/` when it needs real frames, real trigger
callbacks, real elapsed time, or the engine-invoked `Awake`, `OnEnable`, `Start`, and `OnDestroy` ordering.
`Assets/Tests/Support/` holds the `PrivateAccess` reflection accessor, the task template YAML builder, the in-process
MQTT harness, the trigger zone rig that both assemblies share, and the staged template workspace that the Edit Mode
`ConfigLoader` fixture uses. Run the suite from `Window → General → Test Runner`, or headlessly with
`Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults out.xml`, which requires the
Editor closed on that project because Unity holds a per-project lock.

**Before committing**: run `csharpier format .`, run both test platforms, invoke `/audit-project` to run the four
audits over the changed files, and invoke `/commit` to stage and write the message. The Editor menu
`CreateTask → New Task` and a single `create_task_tool(template_name=…)` call share `CreateTask.CreateFromTemplate` and
`CreateTask.CreateSceneFromTemplate`, so the agentic and manual paths produce byte-equivalent assets.
