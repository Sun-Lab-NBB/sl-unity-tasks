/// <summary>
/// Verifies the behavior of the McpBridge dispatch surface, its response envelope, the scene and asset tools, and
/// the delete guards that bound which project assets the bridge may remove.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the McpBridge class.</summary>
    [TestFixture]
    public class McpBridgeTests
    {
        /// <summary>The hand-authored stimulus trigger zone prefab used as an inspection fixture.</summary>
        private const string StimulusZonePath = "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab";

        /// <summary>The hand-authored occupancy trigger zone prefab protected from deletion.</summary>
        private const string OccupancyZonePath = "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab";

        /// <summary>The hand-authored experiment template scene protected from deletion.</summary>
        private const string TemplateScenePath = "Assets/Scenes/ExperimentTemplate.unity";

        /// <summary>The per-scene saved views companion belonging to the protected experiment template.</summary>
        private const string TemplateCompanionPath =
            "Assets/VRSettings/Displays/ExperimentTemplate-savedFullScreenViews.asset";

        /// <summary>The hand-authored floor material copied whenever a test needs a throwaway asset.</summary>
        private const string FloorMaterialPath = "Assets/InfiniteCorridorTask/Materials/Floor.mat";

        /// <summary>The Configurations basename of a template that exists in the project.</summary>
        private const string ExistingTemplateName = "MF_Reward_Base";

        /// <summary>The Configurations basename of a template the project ships no scene or task prefab for.</summary>
        private const string UnbuiltTemplateName = "SSO_Connection_Base";

        /// <summary>The project-relative asset paths created by the running test, removed during teardown.</summary>
        private readonly List<string> _createdAssets = new List<string>();

        /// <summary>The active scene path recorded before the running test, restored during teardown.</summary>
        private string _initialScenePath;

        /// <summary>Records the active scene and clears the per-test cleanup ledgers.</summary>
        [SetUp]
        public void SetUp()
        {
            _createdAssets.Clear();
            _initialScenePath = SceneManager.GetActiveScene().path;
        }

        /// <summary>Restores the active scene and removes every asset and object the test created.</summary>
        [TearDown]
        public void TearDown()
        {
            RestoreActiveScene();
            CloseTemporaryScenes();

            for (int index = _createdAssets.Count - 1; index >= 0; index--)
            {
                string path = _createdAssets[index];
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            AssetDatabase.Refresh();
            DiscardActiveSceneEdits();
        }

        /// <summary>Verifies that Error returns the documented two-key failure envelope.</summary>
        [Test]
        public void Error_Message_SerializesSuccessFalseWithTheMessage()
        {
            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Error", "Boom");

            Assert.AreEqual("{\"success\":false,\"error\":\"Boom\"}", json);
        }

        /// <summary>Verifies that Error escapes a quoted message so the envelope round-trips.</summary>
        [Test]
        public void Error_MessageContainingQuotes_RoundTripsThroughTheParser()
        {
            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Error", "Missing \"tool\" key");

            Dictionary<string, object> parsed = MiniJson.Deserialize(json);
            Assert.AreEqual(false, parsed["success"]);
            Assert.AreEqual("Missing \"tool\" key", parsed["error"]);
        }

        /// <summary>Verifies that Ok on an empty payload emits nothing but the success flag.</summary>
        [Test]
        public void Ok_EmptyPayload_SerializesSuccessTrueOnly()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();

            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Ok", payload);

            Assert.AreEqual("{\"success\":true}", json);
        }

        /// <summary>Verifies that Ok appends the success flag to the payload it was handed.</summary>
        [Test]
        public void Ok_PopulatedPayload_KeepsPayloadFieldsAndAddsSuccessTrue()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "message", "Deleted asset: A" },
                { "count", 2 },
            };

            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Ok", payload);

            Dictionary<string, object> parsed = MiniJson.Deserialize(json);
            Assert.AreEqual(3, parsed.Count);
            Assert.AreEqual("Deleted asset: A", parsed["message"]);
            Assert.AreEqual(2L, parsed["count"]);
            Assert.AreEqual(true, parsed["success"]);
            Assert.AreEqual(true, payload["success"]);
        }

        /// <summary>Verifies that Ok overrides a success flag the payload already carries.</summary>
        [Test]
        public void Ok_PayloadDeclaringSuccessFalse_OverwritesItWithTrue()
        {
            Dictionary<string, object> payload = new Dictionary<string, object> { { "success", false } };

            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Ok", payload);

            Assert.AreEqual("{\"success\":true}", json);
        }

        /// <summary>Verifies that each covered tool name resolves to a handler, not to the fallback.</summary>
        /// <remarks>
        /// The cases cover thirteen of the eighteen dispatched names. enter_play_mode is excluded because dispatching
        /// it here would leave the Editor in Play Mode for the rest of the run, so McpBridgePlayModeTests covers it
        /// from inside the player loop, where the handler takes its already-playing branch. read_task_parameters,
        /// write_task_parameters, and refresh_monitors are excluded because McpBridgeTaskParametersTests dispatches
        /// them against the FullScreenViewManager fixture their handlers need. save_scene is excluded because a bare
        /// dispatch writes whichever scene the run happens to have open, so it is covered below against a throwaway
        /// scene this fixture stages and deletes.
        /// </remarks>
        [TestCase("create_task")]
        [TestCase("delete_task")]
        [TestCase("inspect_prefab")]
        [TestCase("clone_zone_prefab")]
        [TestCase("delete_asset")]
        [TestCase("list_assets")]
        [TestCase("refresh_assets")]
        [TestCase("list_scenes")]
        [TestCase("open_scene")]
        [TestCase("inspect_scene")]
        [TestCase("exit_play_mode")]
        [TestCase("get_play_state")]
        [TestCase("read_console")]
        public void Dispatch_DeclaredToolName_DoesNotFallThroughToUnknownTool(string tool)
        {
            Dictionary<string, object> response = CallTool(tool);

            Assert.IsTrue(response.ContainsKey("success"), $"The '{tool}' response is not a bridge envelope.");
            string error = response.TryGetValue("error", out object value) ? (string)value : string.Empty;
            Assert.AreNotEqual($"Unknown tool: {tool}", error);
        }

        /// <summary>Verifies that an undeclared tool name returns the unknown-tool error envelope.</summary>
        [Test]
        public void Dispatch_UndeclaredToolName_ReturnsUnknownToolError()
        {
            Dictionary<string, object> response = CallTool("not_a_bridge_tool");

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("Unknown tool: not_a_bridge_tool", response["error"]);
        }

        /// <summary>Verifies that an empty tool name is reported as unknown rather than defaulting to a tool.</summary>
        [Test]
        public void Dispatch_EmptyToolName_ReturnsUnknownToolError()
        {
            Dictionary<string, object> response = CallTool(string.Empty);

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("Unknown tool: ", response["error"]);
        }

        /// <summary>Verifies that tool names are matched case-sensitively.</summary>
        [Test]
        public void Dispatch_ToolNameInWrongCase_ReturnsUnknownToolError()
        {
            Dictionary<string, object> response = CallTool("List_Scenes");

            Assert.AreEqual("Unknown tool: List_Scenes", response["error"]);
        }

        /// <summary>Verifies that create_task without a template name reports the missing argument.</summary>
        [Test]
        public void Dispatch_CreateTaskWithoutTemplateName_ReportsMissingArgument()
        {
            Dictionary<string, object> response = CallTool("create_task");

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("Missing required argument: template_name", response["error"]);
        }

        /// <summary>Verifies that delete_task with an empty template name reports the missing argument.</summary>
        [Test]
        public void Dispatch_DeleteTaskWithEmptyTemplateName_ReportsMissingArgument()
        {
            Dictionary<string, object> response = CallTool(
                "delete_task",
                BuildArguments("template_name", string.Empty)
            );

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("Missing required argument: template_name", response["error"]);
        }

        /// <summary>Verifies that inspect_prefab without a prefab path reports the missing argument.</summary>
        [Test]
        public void Dispatch_InspectPrefabWithoutPrefabPath_ReportsMissingArgument()
        {
            Dictionary<string, object> response = CallTool("inspect_prefab");

            Assert.AreEqual("Missing required argument: prefab_path", response["error"]);
        }

        /// <summary>Verifies that delete_asset without an asset path reports the missing argument.</summary>
        [Test]
        public void Dispatch_DeleteAssetWithoutAssetPath_ReportsMissingArgument()
        {
            Dictionary<string, object> response = CallTool("delete_asset");

            Assert.AreEqual("Missing required argument: asset_path", response["error"]);
        }

        /// <summary>Verifies that open_scene without a scene path reports the missing argument.</summary>
        [Test]
        public void Dispatch_OpenSceneWithoutScenePath_ReportsMissingArgument()
        {
            Dictionary<string, object> response = CallTool("open_scene");

            Assert.AreEqual("Missing required argument: scene_path", response["error"]);
        }

        /// <summary>Verifies that clone_zone_prefab without both path arguments reports them together.</summary>
        [Test]
        public void Dispatch_CloneZonePrefabWithoutPaths_ReportsBothMissingArguments()
        {
            Dictionary<string, object> response = CallTool("clone_zone_prefab");

            Assert.AreEqual("Missing required arguments: source_prefab and destination_prefab.", response["error"]);
        }

        /// <summary>Verifies that get_play_state reports the edit state outside Play Mode.</summary>
        [Test]
        public void GetPlayState_OutsidePlayMode_ReportsEditStateAndActiveSceneName()
        {
            CreateActiveSceneAsset("Assets/Scenes/ZZTest_PlayState.unity");

            Dictionary<string, object> response = AssertSucceeded(CallTool("get_play_state"));

            Assert.AreEqual("edit", response["state"]);
            Assert.AreEqual("ZZTest_PlayState", response["active_scene"]);
        }

        /// <summary>Verifies that exit_play_mode outside Play Mode reports the no-op result.</summary>
        [Test]
        public void ExitPlayMode_OutsidePlayMode_ReportsTheNoOpResult()
        {
            Dictionary<string, object> response = AssertSucceeded(CallTool("exit_play_mode"));

            Assert.AreEqual("Not in Play Mode.", response["message"]);
            Assert.AreEqual("edit", response["state"]);
        }

        /// <summary>Verifies that GetString falls back to the default when the key is absent.</summary>
        [Test]
        public void GetString_AbsentKey_ReturnsTheDefaultValue()
        {
            Dictionary<string, object> arguments = new Dictionary<string, object>();

            object value = PrivateAccess.InvokeStatic(typeof(McpBridge), "GetString", arguments, "asset_path", "Fall");

            Assert.AreEqual("Fall", value);
        }

        /// <summary>Verifies that GetString falls back to the default when the value is JSON null.</summary>
        [Test]
        public void GetString_NullValue_ReturnsTheDefaultValue()
        {
            Dictionary<string, object> arguments = new Dictionary<string, object> { { "asset_path", null } };

            object value = PrivateAccess.InvokeStatic(typeof(McpBridge), "GetString", arguments, "asset_path", "Fall");

            Assert.AreEqual("Fall", value);
        }

        /// <summary>Verifies that GetString stringifies a non-string argument value.</summary>
        [Test]
        public void GetString_NumericValue_ReturnsItsStringRepresentation()
        {
            Dictionary<string, object> arguments = new Dictionary<string, object> { { "asset_path", 42L } };

            object value = PrivateAccess.InvokeStatic(typeof(McpBridge), "GetString", arguments, "asset_path", "Fall");

            Assert.AreEqual("42", value);
        }

        /// <summary>Verifies that list_assets applies the documented default type and search path.</summary>
        [Test]
        public void ListAssets_NoArguments_EchoesTheDefaultFiltersAndListsTheZonePrefabs()
        {
            Dictionary<string, object> response = AssertSucceeded(CallTool("list_assets"));

            Assert.AreEqual("Prefab", response["asset_type"]);
            Assert.AreEqual("Assets/InfiniteCorridorTask", response["search_path"]);
            List<string> assets = ReadStringList(response["assets"]);
            Assert.Contains(StimulusZonePath, assets);
            Assert.Contains(OccupancyZonePath, assets);
            Assert.Contains("Assets/InfiniteCorridorTask/Prefabs/Padding.prefab", assets);
        }

        /// <summary>Verifies that list_assets returns the matching paths sorted by path.</summary>
        [Test]
        public void ListAssets_ExplicitFilters_ReturnsTheMatchingPathsInSortedOrder()
        {
            string beta = CreateMaterialAsset("Assets/InfiniteCorridorTask/Cues/ZZTest_Beta.mat");
            string alpha = CreateMaterialAsset("Assets/InfiniteCorridorTask/Cues/ZZTest_Alpha.mat");
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "asset_type", "Material" },
                { "search_path", "Assets/InfiniteCorridorTask/Cues" },
            };

            Dictionary<string, object> response = AssertSucceeded(CallTool("list_assets", arguments));

            Assert.AreEqual("Material", response["asset_type"]);
            Assert.AreEqual("Assets/InfiniteCorridorTask/Cues", response["search_path"]);
            CollectionAssert.AreEqual(new List<string> { alpha, beta }, ReadStringList(response["assets"]));
        }

        /// <summary>Verifies that list_assets returns an empty list when no asset matches the filter.</summary>
        [Test]
        public void ListAssets_TypeWithNoMatches_ReturnsAnEmptyAssetList()
        {
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "asset_type", "AudioClip" },
                { "search_path", "Assets/InfiniteCorridorTask" },
            };

            Dictionary<string, object> response = AssertSucceeded(CallTool("list_assets", arguments));

            Assert.AreEqual("AudioClip", response["asset_type"]);
            Assert.AreEqual(0, ReadStringList(response["assets"]).Count);
        }

        /// <summary>Verifies that list_scenes reports every scene asset plus the active scene path.</summary>
        [Test]
        public void ListScenes_ProjectScenes_ListsEverySceneAssetAndTheActiveScene()
        {
            string temporaryScene = CreateActiveSceneAsset("Assets/Scenes/ZZTest_Listed.unity");

            Dictionary<string, object> response = AssertSucceeded(CallTool("list_scenes"));

            List<string> scenes = ReadStringList(response["scenes"]);
            Assert.Contains(TemplateScenePath, scenes);
            Assert.Contains(temporaryScene, scenes);
            Assert.AreEqual("Assets/Scenes/ZZTest_Listed.unity", response["active_scene"]);
        }

        /// <summary>Verifies that inspect_prefab rejects a path that holds no prefab.</summary>
        [Test]
        public void InspectPrefab_UnknownPath_ReportsThePrefabAsNotFound()
        {
            string missing = "Assets/InfiniteCorridorTask/Prefabs/ZZTest_Absent.prefab";

            Dictionary<string, object> response = CallTool("inspect_prefab", BuildArguments("prefab_path", missing));

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual($"Prefab not found at: {missing}", response["error"]);
        }

        /// <summary>Verifies that inspect_prefab reports the transform, components, and collider per node.</summary>
        [Test]
        public void InspectPrefab_HandAuthoredStimulusZone_ReportsTheFullHierarchy()
        {
            Dictionary<string, object> arguments = BuildArguments("prefab_path", StimulusZonePath);

            Dictionary<string, object> response = AssertSucceeded(CallTool("inspect_prefab", arguments));

            Assert.AreEqual(StimulusZonePath, response["prefab_path"]);
            Dictionary<string, object> root = ReadSection(response["hierarchy"]);
            Assert.AreEqual("StimulusTriggerZone", root["name"]);
            AssertVector(root["position"], 0f, 0.505f, 0f);
            AssertVector(root["rotation"], 0f, 0f, 0f);
            AssertVector(root["scale"], 1f, 1f, 1f);
            CollectionAssert.AreEqual(
                new List<string>
                {
                    "Transform",
                    "MeshFilter",
                    "MeshRenderer",
                    "MeshCollider",
                    "BoxCollider",
                    "StimulusTriggerZone",
                },
                ReadStringList(root["components"])
            );
            AssertVector(root["collider_size"], 1f, 1f, 1.4f);
            AssertVector(root["collider_center"], 0f, 0f, -0.3675f);
            Assert.AreEqual(true, root["collider_is_trigger"]);

            List<object> children = (List<object>)root["children"];
            Assert.AreEqual(1, children.Count);
            Dictionary<string, object> guidance = ReadSection(children[0]);
            Assert.AreEqual("GuidanceRegion", guidance["name"]);
            CollectionAssert.AreEqual(
                new List<string> { "Transform", "BoxCollider", "GuidanceZone" },
                ReadStringList(guidance["components"])
            );
            AssertVector(guidance["position"], 0f, 0f, 0f);
            AssertVector(guidance["collider_size"], 1f, 1f, 0.3325f);
            AssertVector(guidance["collider_center"], 0f, 0f, 0.16625f);
            Assert.IsFalse(guidance.ContainsKey("children"));
        }

        /// <summary>Verifies that a childless prefab without a BoxCollider reports only the base keys.</summary>
        [Test]
        public void InspectPrefab_PrefabWithoutColliderOrChildren_OmitsTheOptionalKeys()
        {
            string prefabPath = CreatePrefabAsset(
                "Assets/InfiniteCorridorTask/Prefabs/ZZTest_Bare.prefab",
                "ZZTest_Bare"
            );
            Dictionary<string, object> arguments = BuildArguments("prefab_path", prefabPath);

            Dictionary<string, object> response = AssertSucceeded(CallTool("inspect_prefab", arguments));

            Dictionary<string, object> root = ReadSection(response["hierarchy"]);
            CollectionAssert.AreEquivalent(
                new List<string>
                {
                    "name",
                    "active_self",
                    "position",
                    "rotation",
                    "scale",
                    "components",
                    "component_states",
                },
                root.Keys.ToList()
            );
            Assert.AreEqual("ZZTest_Bare", root["name"]);
            Assert.AreEqual(true, root["active_self"]);
            CollectionAssert.AreEqual(new List<string> { "Transform" }, ReadStringList(root["components"]));
        }

        /// <summary>Verifies that inspect_prefab reports the enabled flag beside each component type.</summary>
        [Test]
        public void InspectPrefab_HandAuthoredStimulusZone_ReportsThePerComponentEnabledFlags()
        {
            Dictionary<string, object> arguments = BuildArguments("prefab_path", StimulusZonePath);

            Dictionary<string, object> response = AssertSucceeded(CallTool("inspect_prefab", arguments));

            Dictionary<string, object> root = ReadSection(response["hierarchy"]);
            List<object> states = (List<object>)root["component_states"];
            CollectionAssert.AreEqual(
                ReadStringList(root["components"]),
                states.Select(entry => (string)ReadSection(entry)["type"]).ToList()
            );

            Dictionary<string, object> transform = ReadSection(states[0]);
            Assert.AreEqual("Transform", transform["type"]);
            Assert.IsNull(transform["enabled"], "A Transform carries no enabled flag and must report null.");

            Dictionary<string, object> zone = ReadSection(states[states.Count - 1]);
            Assert.AreEqual("StimulusTriggerZone", zone["type"]);
            Assert.AreEqual(true, zone["enabled"]);
        }

        /// <summary>Verifies that inspect_scene reports a freshly saved active scene as clean.</summary>
        [Test]
        public void InspectScene_SavedActiveScene_ReportsThePathNameAndCleanState()
        {
            string scenePath = CreateActiveSceneAsset("Assets/Scenes/ZZTest_Inspected.unity");

            Dictionary<string, object> response = AssertSucceeded(CallTool("inspect_scene"));

            Assert.AreEqual(scenePath, response["scene_path"]);
            Assert.AreEqual("ZZTest_Inspected", response["scene_name"]);
            Assert.AreEqual(false, response["is_dirty"]);
            List<object> roots = (List<object>)response["root_objects"];
            Assert.AreEqual(1, roots.Count);
            Dictionary<string, object> root = ReadSection(roots[0]);
            Assert.AreEqual("ZZTest_Root", root["name"]);
            AssertVector(root["position"], 1f, 2f, 3f);
            CollectionAssert.AreEqual(
                new List<string> { "Transform", "BoxCollider" },
                ReadStringList(root["components"])
            );
            AssertVector(root["collider_size"], 4f, 5f, 6f);
            AssertVector(root["collider_center"], 0f, 0.5f, 0f);
            Assert.AreEqual(true, root["collider_is_trigger"]);
            List<object> children = (List<object>)root["children"];
            Assert.AreEqual(1, children.Count);
            Assert.AreEqual("ZZTest_Child", ReadSection(children[0])["name"]);
            AssertVector(ReadSection(children[0])["scale"], 2f, 2f, 2f);
        }

        /// <summary>Verifies that inspect_scene reports an edited active scene as dirty.</summary>
        [Test]
        public void InspectScene_ModifiedActiveScene_ReportsTheDirtyFlag()
        {
            CreateActiveSceneAsset("Assets/Scenes/ZZTest_Dirtied.unity");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Dictionary<string, object> response = AssertSucceeded(CallTool("inspect_scene"));

            Assert.AreEqual(true, response["is_dirty"]);
        }

        /// <summary>Verifies that save_scene clears the dirty flag a scene edit set.</summary>
        [Test]
        public void SaveScene_DirtiedActiveScene_ClearsTheDirtyFlag()
        {
            string scenePath = CreateActiveSceneAsset("Assets/Scenes/ZZTest_Saved.unity");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Dictionary<string, object> response = AssertSucceeded(CallTool("save_scene"));

            Assert.AreEqual(scenePath, response["scene_path"]);
            Assert.AreEqual(false, response["is_dirty"]);
            Assert.AreEqual($"Saved scene: {scenePath}", response["message"]);
            Assert.AreEqual(false, SceneManager.GetActiveScene().isDirty);
        }

        /// <summary>Verifies that save_scene refuses an active scene that was never saved to an asset path.</summary>
        [Test]
        public void SaveScene_UntitledActiveScene_ReportsTheMissingAssetPath()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Dictionary<string, object> response = CallTool("save_scene");

            Assert.AreEqual(false, response["success"]);
            StringAssert.Contains("it has never been saved", (string)response["error"]);
        }

        /// <summary>Verifies that refresh_assets reports the post-import compilation state.</summary>
        [Test]
        public void RefreshAssets_AfterImport_ReportsTheCompilationState()
        {
            Dictionary<string, object> response = AssertSucceeded(CallTool("refresh_assets"));

            Assert.AreEqual("Imported pending asset changes.", response["message"]);
            Assert.AreEqual(EditorApplication.isCompiling, response["is_compiling"]);
            Assert.AreEqual(EditorApplication.isUpdating, response["is_updating"]);
        }

        /// <summary>Verifies that read_console returns a logged message with its severity and stack trace.</summary>
        [Test]
        public void ReadConsole_AfterLogging_ReturnsTheCapturedEntry()
        {
            string marker = "ZZTest_ConsoleMarker";
            Debug.Log(marker);

            Dictionary<string, object> response = AssertSucceeded(CallTool("read_console"));

            List<object> entries = (List<object>)response["entries"];
            Dictionary<string, object> captured = entries
                .Select(ReadSection)
                .Last(entry => string.Equals((string)entry["message"], marker, StringComparison.Ordinal));
            Assert.AreEqual("Log", captured["type"]);
            Assert.IsNotNull(captured["stack_trace"]);
            int capacity = PrivateAccess.GetStaticField<int>(typeof(McpBridge), "ConsoleBufferCapacity");
            Assert.AreEqual(capacity, Convert.ToInt32(response["capacity"]));
        }

        /// <summary>Verifies that read_console returns only the entries logged after a given sequence number.</summary>
        [Test]
        public void ReadConsole_WithSinceSequence_ReturnsOnlyTheNewerEntries()
        {
            Debug.Log("ZZTest_ConsoleBefore");
            Dictionary<string, object> first = AssertSucceeded(CallTool("read_console"));
            long checkpoint = Convert.ToInt64(first["next_sequence"]);
            Debug.Log("ZZTest_ConsoleAfter");

            Dictionary<string, object> response = AssertSucceeded(
                CallTool("read_console", BuildArguments("since_sequence", checkpoint))
            );

            List<string> messages = ((List<object>)response["entries"])
                .Select(entry => (string)ReadSection(entry)["message"])
                .ToList();
            CollectionAssert.Contains(messages, "ZZTest_ConsoleAfter");
            CollectionAssert.DoesNotContain(messages, "ZZTest_ConsoleBefore");
        }

        /// <summary>Verifies that polling drains forward without skipping an entry the limit truncated.</summary>
        [Test]
        public void ReadConsole_PollingBelowTheBacklog_DrainsEveryEntryInOrder()
        {
            const string Marker = "ZZTest_ConsoleDrain";
            long checkpoint = Convert.ToInt64(AssertSucceeded(CallTool("read_console"))["next_sequence"]);
            List<string> logged = new List<string>();
            for (int index = 0; index < 6; index++)
            {
                logged.Add($"{Marker}{index}");
                Debug.Log(logged[index]);
            }

            // Polls with a limit below the backlog, so a run that returned the newest entries and advanced past
            // the rest would drop the earlier markers instead of handing them back on the following poll.
            List<string> drained = new List<string>();
            for (int poll = 0; poll < 5; poll++)
            {
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "since_sequence", checkpoint },
                    { "limit", 2 },
                };
                Dictionary<string, object> response = AssertSucceeded(CallTool("read_console", arguments));
                foreach (object entry in (List<object>)response["entries"])
                {
                    string message = (string)ReadSection(entry)["message"];
                    if (message.StartsWith(Marker, StringComparison.Ordinal))
                    {
                        drained.Add(message);
                    }
                }

                checkpoint = Convert.ToInt64(response["next_sequence"]);
            }

            CollectionAssert.AreEqual(logged, drained);
        }

        /// <summary>Verifies that read_console filters out the severities the level argument excludes.</summary>
        [Test]
        public void ReadConsole_WithWarningLevel_ExcludesPlainLogEntries()
        {
            Debug.Log("ZZTest_ConsoleFilteredOut");
            Debug.LogWarning("ZZTest_ConsoleWarning");

            Dictionary<string, object> response = AssertSucceeded(
                CallTool("read_console", BuildArguments("level", "warning"))
            );

            List<Dictionary<string, object>> entries = ((List<object>)response["entries"]).Select(ReadSection).ToList();
            CollectionAssert.AreEquivalent(
                new List<string> { "Warning" },
                entries.Select(entry => (string)entry["type"]).Distinct().ToList()
            );
            CollectionAssert.Contains(
                entries.Select(entry => (string)entry["message"]).ToList(),
                "ZZTest_ConsoleWarning"
            );
        }

        /// <summary>Verifies that read_console rejects a level filter it does not define.</summary>
        [Test]
        public void ReadConsole_UnknownLevel_ReportsTheAcceptedValues()
        {
            Dictionary<string, object> response = CallTool("read_console", BuildArguments("level", "fatal"));

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual(
                "Unknown level: fatal. The accepted values are all, log, warning, and error.",
                response["error"]
            );
        }

        /// <summary>Verifies that read_console rejects a limit below one.</summary>
        [Test]
        public void ReadConsole_ZeroLimit_ReportsTheMinimum()
        {
            Dictionary<string, object> response = CallTool("read_console", BuildArguments("limit", 0));

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("The limit argument must be at least 1, but it is 0.", response["error"]);
        }

        /// <summary>Verifies that open_scene rejects a path that holds no scene file.</summary>
        [Test]
        public void OpenScene_UnknownPath_ReportsTheSceneAsNotFound()
        {
            string missing = "Assets/Scenes/ZZTest_Absent.unity";

            Dictionary<string, object> response = CallTool("open_scene", BuildArguments("scene_path", missing));

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual($"Scene not found at: {missing}", response["error"]);
        }

        /// <summary>Verifies that open_scene opens the requested scene and echoes its path.</summary>
        [Test]
        public void OpenScene_ExistingScene_OpensItAndEchoesThePath()
        {
            string scenePath = CreateSceneAsset("Assets/Scenes/ZZTest_Opened.unity");
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "scene_path", scenePath },
                { "unsaved_changes", "discard" },
            };

            Dictionary<string, object> response = AssertSucceeded(CallTool("open_scene", arguments));

            Assert.AreEqual($"Opened scene: {scenePath}", response["message"]);
            Assert.AreEqual(scenePath, response["scene_path"]);
            Assert.AreEqual(scenePath, SceneManager.GetActiveScene().path);
        }

        /// <summary>Verifies that open_scene rejects a file the AssetDatabase holds no scene for.</summary>
        /// <remarks>
        /// The probe file sits at the project root, outside Assets, so the file system resolves the relative path
        /// against the Editor's working directory while the AssetDatabase holds nothing for it. Resolving the
        /// argument through the AssetDatabase is what makes the answer independent of that working directory.
        /// </remarks>
        [Test]
        public void OpenScene_FileOutsideTheAssetDatabase_ReportsTheSceneAsNotFound()
        {
            string probeName = "ZZTest_WorkingDirectoryProbe.unity";
            string probePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, probeName);
            File.WriteAllText(probePath, string.Empty);

            try
            {
                Dictionary<string, object> response = CallTool("open_scene", BuildArguments("scene_path", probeName));

                Assert.AreEqual(false, response["success"]);
                Assert.AreEqual($"Scene not found at: {probeName}", response["error"]);
            }
            finally
            {
                File.Delete(probePath);
            }
        }

        /// <summary>Verifies that a clean active scene needs no unsaved-changes policy.</summary>
        [Test]
        public void HandleUnsavedChanges_CleanActiveScene_ReturnsNoError()
        {
            CreateActiveSceneAsset("Assets/Scenes/ZZTest_Clean.unity");

            object error = PrivateAccess.InvokeStatic(typeof(McpBridge), "HandleUnsavedChanges", string.Empty);

            Assert.IsNull(error);
        }

        /// <summary>Verifies that a dirty active scene without a policy returns the guidance message.</summary>
        [Test]
        public void HandleUnsavedChanges_DirtySceneWithoutPolicy_ReturnsTheGuidanceMessage()
        {
            string scenePath = CreateActiveSceneAsset("Assets/Scenes/ZZTest_Unsaved.unity");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            object error = PrivateAccess.InvokeStatic(typeof(McpBridge), "HandleUnsavedChanges", string.Empty);

            StringAssert.Contains($"Active scene '{scenePath}' has unsaved changes", (string)error);
            StringAssert.Contains("unsaved_changes='save'", (string)error);
            StringAssert.Contains("unsaved_changes='discard'", (string)error);
        }

        /// <summary>Verifies that the discard policy accepts a dirty active scene without saving it.</summary>
        [Test]
        public void HandleUnsavedChanges_DirtySceneWithDiscardPolicy_ReturnsNoError()
        {
            CreateActiveSceneAsset("Assets/Scenes/ZZTest_Discarded.unity");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            object error = PrivateAccess.InvokeStatic(typeof(McpBridge), "HandleUnsavedChanges", "discard");

            Assert.IsNull(error);
        }

        /// <summary>Verifies that the unsaved-changes policy is matched case-sensitively.</summary>
        [Test]
        public void HandleUnsavedChanges_DirtySceneWithMiscasedPolicy_ReturnsTheGuidanceMessage()
        {
            CreateActiveSceneAsset("Assets/Scenes/ZZTest_Miscased.unity");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            object error = PrivateAccess.InvokeStatic(typeof(McpBridge), "HandleUnsavedChanges", "Discard");

            StringAssert.Contains("has unsaved changes", (string)error);
        }

        /// <summary>Verifies that delete_asset treats an empty asset path as a missing argument.</summary>
        [Test]
        public void DeleteAsset_EmptyAssetPath_ReportsTheMissingArgument()
        {
            Dictionary<string, object> arguments = BuildArguments("asset_path", string.Empty);

            Dictionary<string, object> response = CallTool("delete_asset", arguments);

            Assert.AreEqual("Missing required argument: asset_path", response["error"]);
        }

        /// <summary>Verifies that delete_asset refuses a scene and redirects the caller to delete_task.</summary>
        [Test]
        public void DeleteAsset_ScenePath_RefusesAndPointsAtDeleteTask()
        {
            Dictionary<string, object> arguments = BuildArguments("asset_path", TemplateScenePath);

            Dictionary<string, object> response = CallTool("delete_asset", arguments);

            Assert.AreEqual(false, response["success"]);
            string error = (string)response["error"];
            StringAssert.StartsWith($"Refusing to delete scene '{TemplateScenePath}' via delete_asset.", error);
            StringAssert.Contains("Use delete_task", error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TemplateScenePath));
        }

        /// <summary>Verifies that a non-scene file in the Scenes folder falls through to the prefix guard.</summary>
        [Test]
        public void DeleteAsset_NonSceneFileInScenesFolder_FallsThroughToThePrefixRefusal()
        {
            Dictionary<string, object> arguments = BuildArguments("asset_path", "Assets/Scenes/ZZTest_Notes.txt");

            Dictionary<string, object> response = CallTool("delete_asset", arguments);

            Assert.AreEqual(false, response["success"]);
            StringAssert.Contains("Deletion is permitted only for individual assets under", (string)response["error"]);
        }

        /// <summary>Verifies that a scene file outside the Scenes folder falls through to the prefix guard.</summary>
        [Test]
        public void DeleteAsset_SceneFileOutsideScenesFolder_FallsThroughToThePrefixRefusal()
        {
            Dictionary<string, object> arguments = BuildArguments("asset_path", "Assets/Gimbl/ZZTest_Elsewhere.unity");

            Dictionary<string, object> response = CallTool("delete_asset", arguments);

            Assert.AreEqual(false, response["success"]);
            StringAssert.Contains("Deletion is permitted only for individual assets under", (string)response["error"]);
        }

        /// <summary>Verifies that the prefix refusal names every allowed deletion root in order.</summary>
        [Test]
        public void DeleteAsset_PathOutsideEveryAllowedPrefix_ListsEveryAllowedRoot()
        {
            string outside = "Assets/InfiniteCorridorTask/Textures/ZZTest_Texture.png";
            Dictionary<string, object> arguments = BuildArguments("asset_path", outside);

            Dictionary<string, object> response = CallTool("delete_asset", arguments);

            string expectedRoots =
                "Assets/InfiniteCorridorTask/Tasks/, "
                + "Assets/InfiniteCorridorTask/Prefabs/, "
                + "Assets/InfiniteCorridorTask/Cues/, "
                + "Assets/InfiniteCorridorTask/Materials/";
            string error = (string)response["error"];
            StringAssert.StartsWith($"Refusing to delete '{outside}'.", error);
            StringAssert.Contains(expectedRoots, error);
            StringAssert.Contains("Hand-authored prefabs and the experiment template scene are protected.", error);
        }

        /// <summary>Verifies that each protected hand-authored asset is refused despite its allowed prefix.</summary>
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/Padding.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/Floor.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/Wall.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/TargetMat.mat")]
        public void DeleteAsset_ProtectedHandAuthoredAsset_RefusesWithoutDeletingIt(string assetPath)
        {
            Dictionary<string, object> response = CallTool("delete_asset", BuildArguments("asset_path", assetPath));

            Assert.AreEqual(false, response["success"]);
            StringAssert.StartsWith($"Refusing to delete '{assetPath}'.", (string)response["error"]);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
        }

        /// <summary>Verifies that an allowed path holding no asset is reported as not found.</summary>
        [Test]
        public void DeleteAsset_AllowedPathWithoutAnAsset_ReportsTheAssetAsNotFound()
        {
            string missing = "Assets/InfiniteCorridorTask/Cues/ZZTest_Missing.prefab";

            Dictionary<string, object> response = CallTool("delete_asset", BuildArguments("asset_path", missing));

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual($"Asset not found at: {missing}", response["error"]);
        }

        /// <summary>Verifies that a generated asset under each allowed root is deleted and confirmed.</summary>
        [TestCase("Assets/InfiniteCorridorTask/Tasks/ZZTest_Deletable.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/ZZTest_Deletable.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Cues/ZZTest_Deletable.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/ZZTest_Deletable.mat")]
        public void DeleteAsset_GeneratedAssetUnderAnAllowedRoot_DeletesItAndConfirms(string assetPath)
        {
            CreateMaterialAsset(assetPath);

            Dictionary<string, object> response = AssertSucceeded(
                CallTool("delete_asset", BuildArguments("asset_path", assetPath))
            );

            Assert.AreEqual($"Deleted asset: {assetPath}", response["message"]);
            Assert.AreEqual(assetPath, response["asset_path"]);
            Assert.AreEqual(true, response["deleted"]);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
        }

        /// <summary>Verifies that an unsafe, misplaced, or protected path is not deletable.</summary>
        /// <remarks>
        /// The four bare allowed roots are covered because the prefix loop accepts every path that starts with a
        /// root, so the directory target itself is refused by the trailing-separator rejection alone.
        /// </remarks>
        [TestCase("Assets/InfiniteCorridorTask/Cues/../../../Packages/manifest.json")]
        [TestCase("/Assets/InfiniteCorridorTask/Cues/ZZTest_Rooted.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Tasks/")]
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/")]
        [TestCase("Assets/InfiniteCorridorTask/Cues/")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/")]
        [TestCase("Assets/InfiniteCorridorTask/Cuesx/ZZTest_Neighbor.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Textures/ZZTest_Texture.png")]
        [TestCase("Assets/Scenes/ExperimentTemplate.unity")]
        [TestCase("Assets/Gimbl/Scripts/MQTT/MQTTClient.cs")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat")]
        public void IsDeleteAllowed_UnsafeOrUnlistedPath_ReturnsFalse(string assetPath)
        {
            bool allowed = (bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", assetPath);

            Assert.IsFalse(allowed);
        }

        /// <summary>Verifies that a regenerable asset under each allowed prefix is deletable.</summary>
        [TestCase("Assets/InfiniteCorridorTask/Tasks/A.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Prefabs/A.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Cues/A.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Materials/A.mat")]
        [TestCase("Assets/InfiniteCorridorTask/Cues/Nested/A.prefab")]
        public void IsDeleteAllowed_RegenerableAssetUnderAnAllowedPrefix_ReturnsTrue(string assetPath)
        {
            bool allowed = (bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", assetPath);

            Assert.IsTrue(allowed);
        }

        /// <summary>Verifies that the prefix comparison only accepts forward-slash separated paths.</summary>
        [TestCase("Assets\\InfiniteCorridorTask\\Cues\\ZZTest_Windows.prefab")]
        [TestCase("Assets/InfiniteCorridorTask/Cues\\ZZTest_Mixed.prefab")]
        public void IsDeleteAllowed_BackslashSeparatedPath_ReturnsFalse(string assetPath)
        {
            bool allowed = (bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", assetPath);

            Assert.IsFalse(allowed);
        }

        /// <summary>Verifies that the protected set is matched by exact ordinal path rather than by prefix.</summary>
        [Test]
        public void IsDeleteAllowed_NeighborOfAProtectedAsset_ReturnsTrue()
        {
            string protectedPath = "Assets/InfiniteCorridorTask/Materials/Floor.mat";
            string neighborPath = "Assets/InfiniteCorridorTask/Materials/Floor2.mat";
            string miscasedPath = "Assets/InfiniteCorridorTask/Materials/floor.mat";

            Assert.IsFalse((bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", protectedPath));
            Assert.IsTrue((bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", neighborPath));
            Assert.IsTrue((bool)PrivateAccess.InvokeStatic(typeof(McpBridge), "IsDeleteAllowed", miscasedPath));
        }

        /// <summary>Verifies that delete_task rejects a template whose scene is a protected asset.</summary>
        [Test]
        public void DeleteTask_ProtectedTemplateScene_RefusesWithoutDeletingAnything()
        {
            Dictionary<string, object> arguments = BuildArguments("template_name", "ExperimentTemplate");

            Dictionary<string, object> response = CallTool("delete_task", arguments);

            Assert.AreEqual(false, response["success"]);
            string error = (string)response["error"];
            StringAssert.StartsWith("Refusing to delete task 'ExperimentTemplate'.", error);
            StringAssert.Contains("protected hand-authored asset", error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TemplateScenePath));
            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TemplateCompanionPath),
                "The protected scene's companion asset must survive the refusal."
            );
        }

        /// <summary>Verifies that a task prefab in the protected set is refused by the delete_task guard.</summary>
        /// <remarks>
        /// The project ships no hand-authored asset under Tasks, so the task prefab half of the guard answers only
        /// once one lands there. Installing that future state for the length of one call is what keeps the half a
        /// live defense rather than an unreachable branch.
        /// </remarks>
        [Test]
        public void DeleteTask_TaskPrefabInTheProtectedSet_RefusesWithoutDeletingAnything()
        {
            string prefabPath = CreatePrefabAsset(
                "Assets/InfiniteCorridorTask/Tasks/ZZTest_Guarded.prefab",
                "ZZTest_Guarded"
            );
            HashSet<string> protectedPaths = PrivateAccess.GetStaticField<HashSet<string>>(
                typeof(McpBridge),
                "DeleteProtectedPaths"
            );
            protectedPaths.Add(prefabPath);

            try
            {
                Dictionary<string, object> response = CallTool(
                    "delete_task",
                    BuildArguments("template_name", "ZZTest_Guarded")
                );

                Assert.AreEqual(false, response["success"]);
                StringAssert.StartsWith("Refusing to delete task 'ZZTest_Guarded'.", (string)response["error"]);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath));
            }
            finally
            {
                protectedPaths.Remove(prefabPath);
            }
        }

        /// <summary>Verifies that delete_task reports a template that owns no generated artifact.</summary>
        [Test]
        public void DeleteTask_TemplateWithoutArtifacts_ReportsThatNothingWasFound()
        {
            Dictionary<string, object> arguments = BuildArguments("template_name", "ZZTest_Unknown");

            Dictionary<string, object> response = CallTool("delete_task", arguments);

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual("No artifacts found for template 'ZZTest_Unknown'.", response["error"]);
        }

        /// <summary>Verifies that delete_task removes the scene, companion, task prefab, and owned segments.</summary>
        [Test]
        public void DeleteTask_GeneratedArtifacts_DeletesTheWholeCascadeButSparesOtherTemplates()
        {
            string scenePath = CreateSceneAsset("Assets/Scenes/ZZTest_Cascade.unity");
            string companionPath = CreateCompanionAsset("ZZTest_Cascade");
            string taskPath = CreatePrefabAsset(
                "Assets/InfiniteCorridorTask/Tasks/ZZTest_Cascade.prefab",
                "ZZTest_Cascade"
            );
            string segmentPath = CreatePrefabAsset(
                "Assets/InfiniteCorridorTask/Prefabs/ZZTest_Cascade-Trial.prefab",
                "ZZTest_Cascade-Trial"
            );
            string siblingPath = CreatePrefabAsset(
                "Assets/InfiniteCorridorTask/Prefabs/ZZTest_Cascade2-Trial.prefab",
                "ZZTest_Cascade2-Trial"
            );

            Dictionary<string, object> response = AssertSucceeded(
                CallTool("delete_task", BuildArguments("template_name", "ZZTest_Cascade"))
            );

            Assert.AreEqual("Deleted task: ZZTest_Cascade", response["message"]);
            Assert.AreEqual("ZZTest_Cascade", response["template_name"]);
            Assert.AreEqual(true, response["deleted"]);
            Assert.AreEqual(companionPath, response["companion_deleted"]);
            Assert.IsFalse(response.ContainsKey("companion_delete_failed"));
            CollectionAssert.AreEqual(
                new List<string> { scenePath, taskPath, segmentPath },
                ReadStringList(response["deleted_paths"])
            );
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(siblingPath));
        }

        /// <summary>Verifies that delete_task omits the companion field when the scene has no companion.</summary>
        [Test]
        public void DeleteTask_SceneWithoutCompanion_OmitsTheCompanionField()
        {
            string scenePath = CreateSceneAsset("Assets/Scenes/ZZTest_Lonely.unity");

            Dictionary<string, object> response = AssertSucceeded(
                CallTool("delete_task", BuildArguments("template_name", "ZZTest_Lonely"))
            );

            CollectionAssert.AreEqual(new List<string> { scenePath }, ReadStringList(response["deleted_paths"]));
            Assert.IsFalse(response.ContainsKey("companion_deleted"));
            Assert.IsFalse(response.ContainsKey("companion_delete_failed"));
        }

        /// <summary>Verifies that the companion cascade ignores a path outside the Scenes folder.</summary>
        [Test]
        public void TryDeleteScenePerSceneCompanions_PathOutsideScenesFolder_ReturnsNullWithoutAnError()
        {
            string companionPath = CreateCompanionAsset("ZZTest_Outside");

            string deleted = RunCompanionCascade("Assets/Gimbl/ZZTest_Outside.unity", out string error);

            Assert.IsNull(deleted);
            Assert.IsNull(error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath));
        }

        /// <summary>Verifies that the companion cascade ignores a path without the scene extension.</summary>
        [Test]
        public void TryDeleteScenePerSceneCompanions_PathWithoutSceneExtension_ReturnsNullWithoutAnError()
        {
            string companionPath = CreateCompanionAsset("ZZTest_Extension");

            string deleted = RunCompanionCascade("Assets/Scenes/ZZTest_Extension.prefab", out string error);

            Assert.IsNull(deleted);
            Assert.IsNull(error);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath));
        }

        /// <summary>Verifies that the companion cascade only touches the companion named after the scene.</summary>
        [Test]
        public void TryDeleteScenePerSceneCompanions_WithoutACompanionAsset_ReturnsNullAndSparesOtherCompanions()
        {
            string foreignCompanion = CreateCompanionAsset("ZZTest_ForeignScene");

            string deleted = RunCompanionCascade("Assets/Scenes/ZZTest_NoCompanion.unity", out string error);

            Assert.IsNull(deleted);
            Assert.IsNull(
                error,
                "A scene that owns no companion has nothing to orphan, so the cascade reports no failure."
            );
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(foreignCompanion));
        }

        /// <summary>Verifies that the companion cascade deletes the saved views asset and returns its path.</summary>
        [Test]
        public void TryDeleteScenePerSceneCompanions_WithACompanionAsset_DeletesItAndReturnsThePath()
        {
            string companionPath = CreateCompanionAsset("ZZTest_Companion");

            string deleted = RunCompanionCascade("Assets/Scenes/ZZTest_Companion.unity", out string error);

            Assert.AreEqual("Assets/VRSettings/Displays/ZZTest_Companion-savedFullScreenViews.asset", deleted);
            Assert.IsNull(error);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath));
        }

        /// <summary>Verifies that create_task reports the resolved path of a template that does not exist.</summary>
        [Test]
        public void CreateTask_UnknownTemplate_ReportsTheResolvedTemplatePath()
        {
            Dictionary<string, object> arguments = BuildArguments("template_name", "ZZTest_NoSuchTemplate");

            Dictionary<string, object> response = CallTool("create_task", arguments);

            Assert.AreEqual(false, response["success"]);
            string error = (string)response["error"];
            StringAssert.StartsWith("Template not found: ", error);
            StringAssert.Contains("ZZTest_NoSuchTemplate.yaml", error);
            StringAssert.Contains("Configurations", error);
        }

        /// <summary>Verifies that create_task refuses to clobber an existing scene before writing anything.</summary>
        [Test]
        public void CreateTask_SceneAlreadyExists_RefusesBeforeGeneratingThePrefab()
        {
            string scenePath = CreateSceneAsset($"Assets/Scenes/{ExistingTemplateName}.unity");
            string prefabPath = $"Assets/InfiniteCorridorTask/Tasks/{ExistingTemplateName}.prefab";

            Dictionary<string, object> response = CallTool(
                "create_task",
                BuildArguments("template_name", ExistingTemplateName)
            );

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual(
                $"Scene already exists at: {scenePath}. Call delete_task first to regenerate.",
                response["error"]
            );
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath));
        }

        /// <summary>Verifies that create_task sees the existing scene from an unrelated working directory.</summary>
        /// <remarks>
        /// The refusal resolves the scene through the AssetDatabase, which anchors a project-relative path to the
        /// project root. A file system probe would instead resolve it against the process working directory and
        /// report the scene as absent, generating the prefab and the scene over the one already there.
        /// </remarks>
        [Test]
        public void CreateTask_SceneCheckedFromAnotherWorkingDirectory_StillRefusesToClobberIt()
        {
            string scenePath = CreateSceneAsset($"Assets/Scenes/{UnbuiltTemplateName}.unity");
            string prefabPath = $"Assets/InfiniteCorridorTask/Tasks/{UnbuiltTemplateName}.prefab";
            string workingDirectory = Directory.GetCurrentDirectory();
            Dictionary<string, object> response;

            Directory.SetCurrentDirectory(Path.GetTempPath());
            try
            {
                response = CallTool("create_task", BuildArguments("template_name", UnbuiltTemplateName));
            }
            finally
            {
                Directory.SetCurrentDirectory(workingDirectory);
            }

            Assert.AreEqual(false, response["success"]);
            Assert.AreEqual(
                $"Scene already exists at: {scenePath}. Call delete_task first to regenerate.",
                response["error"]
            );
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath));
        }

        /// <summary>Dispatches a bridge tool call and deserializes the JSON response envelope.</summary>
        /// <param name="tool">The tool name handed to the dispatcher.</param>
        /// <param name="arguments">The tool arguments handed to the dispatcher.</param>
        /// <returns>The deserialized response envelope.</returns>
        private static Dictionary<string, object> CallTool(string tool, Dictionary<string, object> arguments)
        {
            object response = PrivateAccess.InvokeStatic(typeof(McpBridge), "Dispatch", tool, arguments);
            return MiniJson.Deserialize((string)response);
        }

        /// <summary>Dispatches a bridge tool call that carries no arguments.</summary>
        /// <param name="tool">The tool name handed to the dispatcher.</param>
        /// <returns>The deserialized response envelope.</returns>
        private static Dictionary<string, object> CallTool(string tool)
        {
            return CallTool(tool, new Dictionary<string, object>());
        }

        /// <summary>Runs the per-scene companion cascade and reports both values it answers with.</summary>
        /// <param name="scenePath">The project-relative scene path handed to the cascade.</param>
        /// <param name="error">The orphaned companion message, or null when the cascade reported none.</param>
        /// <returns>The companion path the cascade deleted, or null when it deleted none.</returns>
        private static string RunCompanionCascade(string scenePath, out string error)
        {
            // Holds the argument array the invocation writes the out parameter back into.
            object[] arguments = new object[] { scenePath, null };
            object deleted = PrivateAccess.InvokeStatic(
                typeof(McpBridge),
                "TryDeleteScenePerSceneCompanions",
                arguments
            );
            error = (string)arguments[1];
            return (string)deleted;
        }

        /// <summary>Builds a single-entry tool argument dictionary.</summary>
        /// <param name="key">The argument name.</param>
        /// <param name="value">The argument value.</param>
        /// <returns>The argument dictionary.</returns>
        private static Dictionary<string, object> BuildArguments(string key, object value)
        {
            return new Dictionary<string, object> { { key, value } };
        }

        /// <summary>Asserts that a response reports success and returns it for further assertions.</summary>
        /// <param name="response">The deserialized response envelope.</param>
        /// <returns>The same response envelope.</returns>
        private static Dictionary<string, object> AssertSucceeded(Dictionary<string, object> response)
        {
            object error = response.TryGetValue("error", out object value) ? value : "none";
            Assert.AreEqual(true, response["success"], $"The tool call failed with error: {error}");
            return response;
        }

        /// <summary>Converts a deserialized JSON array into a list of strings.</summary>
        /// <param name="array">The deserialized array value.</param>
        /// <returns>The array entries as strings.</returns>
        private static List<string> ReadStringList(object array)
        {
            return ((List<object>)array).Select(entry => (string)entry).ToList();
        }

        /// <summary>Casts a deserialized JSON object to its dictionary representation.</summary>
        /// <param name="value">The deserialized object value.</param>
        /// <returns>The value as a dictionary.</returns>
        private static Dictionary<string, object> ReadSection(object value)
        {
            return (Dictionary<string, object>)value;
        }

        /// <summary>Asserts that a serialized vector carries the expected component values.</summary>
        /// <param name="vector">The deserialized vector dictionary.</param>
        /// <param name="x">The expected x component.</param>
        /// <param name="y">The expected y component.</param>
        /// <param name="z">The expected z component.</param>
        private static void AssertVector(object vector, float x, float y, float z)
        {
            Dictionary<string, object> components = ReadSection(vector);
            Assert.AreEqual(3, components.Count);
            Assert.AreEqual(x, Convert.ToSingle(components["x"]), 1e-4f);
            Assert.AreEqual(y, Convert.ToSingle(components["y"]), 1e-4f);
            Assert.AreEqual(z, Convert.ToSingle(components["z"]), 1e-4f);
        }

        /// <summary>Stages a throwaway material asset by copying the hand-authored floor material.</summary>
        /// <param name="assetPath">The project-relative path the copy is written to.</param>
        /// <returns>The staged asset path.</returns>
        private string CreateMaterialAsset(string assetPath)
        {
            _createdAssets.Add(assetPath);
            bool copied = AssetDatabase.CopyAsset(FloorMaterialPath, assetPath);
            Assert.IsTrue(copied, $"Failed to stage the throwaway material at {assetPath}.");
            return assetPath;
        }

        /// <summary>Stages a throwaway prefab asset holding a single empty root object.</summary>
        /// <remarks>
        /// Callers pass the destination file's basename as the root name, because Unity derives a saved prefab's
        /// root object name from the asset file name. Passing a different name would make the reported hierarchy
        /// name depend on that undocumented detail rather than on the inspection code under test.
        /// </remarks>
        /// <param name="assetPath">The project-relative path the prefab is written to.</param>
        /// <param name="rootName">The name given to the prefab root object, matching the file basename.</param>
        /// <returns>The staged asset path.</returns>
        private string CreatePrefabAsset(string assetPath, string rootName)
        {
            GameObject source = new GameObject(rootName);
            try
            {
                _createdAssets.Add(assetPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(source, assetPath);
                Assert.IsNotNull(saved, $"Failed to stage the throwaway prefab at {assetPath}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
            return assetPath;
        }

        /// <summary>Stages a throwaway per-scene saved full screen views companion asset.</summary>
        /// <param name="sceneName">The scene basename the companion belongs to.</param>
        /// <returns>The staged asset path.</returns>
        private string CreateCompanionAsset(string sceneName)
        {
            string companionPath = $"Assets/VRSettings/Displays/{sceneName}-savedFullScreenViews.asset";
            _createdAssets.Add(companionPath);
            FullScreenViewsSaved companion = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
            AssetDatabase.CreateAsset(companion, companionPath);
            return companionPath;
        }

        /// <summary>Stages an empty scene asset without disturbing the active scene.</summary>
        /// <param name="scenePath">The project-relative path the scene is saved to.</param>
        /// <returns>The staged scene path.</returns>
        private string CreateSceneAsset(string scenePath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _createdAssets.Add(scenePath);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, scenePath), $"Failed to save {scenePath}.");

            // Leaves a throwaway scene active so the staged asset is not also the scene under inspection. The
            // fixture teardown restores whichever scene the run had open before it.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return scenePath;
        }

        /// <summary>Stages a saved scene holding one inspectable root object and makes it the active scene.</summary>
        /// <param name="scenePath">The project-relative path the scene is saved to.</param>
        /// <returns>The staged scene path.</returns>
        private string CreateActiveSceneAsset(string scenePath)
        {
            // Replaces the active scene rather than opening additively, because an untitled scene counts as unsaved
            // whether or not it carries edits, and Unity refuses an additive open while one is loaded. The fixture
            // teardown restores whichever scene the run had open before it.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _createdAssets.Add(scenePath);

            // Populates the staged scene before saving so it reports as clean afterwards.
            GameObject root = new GameObject("ZZTest_Root");
            root.transform.localPosition = new Vector3(1f, 2f, 3f);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(4f, 5f, 6f);
            collider.center = new Vector3(0f, 0.5f, 0f);
            collider.isTrigger = true;
            GameObject child = new GameObject("ZZTest_Child");
            child.transform.SetParent(root.transform);
            child.transform.localScale = new Vector3(2f, 2f, 2f);

            Assert.IsTrue(EditorSceneManager.SaveScene(scene, scenePath), $"Failed to save {scenePath}.");
            return scenePath;
        }

        /// <summary>Reopens the scene that was active before the test whenever the test replaced it.</summary>
        private void RestoreActiveScene()
        {
            string currentPath = SceneManager.GetActiveScene().path;
            if (string.Equals(currentPath, _initialScenePath, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(_initialScenePath) && File.Exists(_initialScenePath))
            {
                EditorSceneManager.OpenScene(_initialScenePath, OpenSceneMode.Single);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>Reloads a dirtied active scene from disk so the next test starts from a clean scene.</summary>
        /// <remarks>
        /// Staging a prefab source object creates and destroys a GameObject in the active scene, which raises the
        /// scene's dirty flag even though the test leaves no object behind. Reloading from disk clears the flag
        /// without saving, so a later test that reads is_dirty or the unsaved-changes policy is not polluted.
        /// </remarks>
        private static void DiscardActiveSceneEdits()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.isDirty && !string.IsNullOrEmpty(active.path) && File.Exists(active.path))
            {
                EditorSceneManager.OpenScene(active.path, OpenSceneMode.Single);
            }
        }

        /// <summary>Closes every additively loaded scene the test staged so its asset can be deleted.</summary>
        private void CloseTemporaryScenes()
        {
            for (int index = SceneManager.sceneCount - 1; index >= 0 && SceneManager.sceneCount > 1; index--)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (_createdAssets.Contains(scene.path))
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }
    }
}
