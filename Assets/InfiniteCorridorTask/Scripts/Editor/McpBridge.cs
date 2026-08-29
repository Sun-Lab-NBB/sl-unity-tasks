/// <summary>
/// Provides the McpBridge editor plugin that exposes Unity Editor operations to external MCP relay servers.
///
/// Starts an HTTP listener on localhost when the Editor loads, accepting JSON tool call requests from the
/// sollertia-virtual-reality MCP relay.
/// </summary>
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Gimbl;
using SL.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks
{
    /// <summary>
    /// Bridges external MCP relay requests to Unity Editor API calls over an HTTP listener.
    /// Initialized automatically when the Editor loads via <see cref="InitializeOnLoadAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Each request specifies a tool name and arguments, and the bridge dispatches to the corresponding Unity Editor
    /// API and returns a JSON result.
    /// </remarks>
    [InitializeOnLoad]
    public static class McpBridge
    {
        /// <summary>The port on which the bridge listens for incoming HTTP requests.</summary>
        private const int Port = 8090;

        /// <summary>The shared error-protocol prefix returned by <see cref="CreateTask.CreateFromTemplate"/>.</summary>
        private const string CreateTaskErrorPrefix = "error: ";

        /// <summary>The lowest broker port number the mqtt section accepts.</summary>
        private const int MinimumBrokerPort = 0;

        /// <summary>The highest broker port number the mqtt section accepts.</summary>
        private const int MaximumBrokerPort = 65535;

        /// <summary>The number of Unity log entries the console buffer retains before evicting the oldest.</summary>
        private const int ConsoleBufferCapacity = 500;

        /// <summary>The number of console entries a read_console call returns when it requests no limit.</summary>
        private const int DefaultConsoleReadLimit = 100;

        /// <summary>
        /// The set of project-relative directory prefixes under which non-scene assets may be deleted via
        /// <c>delete_asset</c>.
        /// </summary>
        /// <remarks>
        /// Scenes are intentionally absent. They are deleted exclusively through <see cref="DestroyTask"/>,
        /// which also cascade-deletes the per-scene <c>savedFullScreenViews</c> companion. Adding a scenes
        /// entry here would let scene paths bypass that cascade.
        /// </remarks>
        private static readonly string[] DeleteAllowedPrefixes =
        {
            "Assets/InfiniteCorridorTask/Tasks/",
            "Assets/InfiniteCorridorTask/Prefabs/",
            "Assets/InfiniteCorridorTask/Cues/",
            "Assets/InfiniteCorridorTask/Materials/",
        };

        /// <summary>The set of hand-authored asset paths that are protected from deletion.</summary>
        /// <remarks>
        /// Covers every hand-authored asset that the CreateTask pipeline (or the generated zone prefabs themselves)
        /// load by hardcoded path or by serialized reference. Removing any one of these breaks task generation or
        /// leaves a regenerated prefab rendering with a missing material, so the bridge refuses to delete them even
        /// when they sit under an allowed prefix.
        /// </remarks>
        private static readonly HashSet<string> DeleteProtectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/Padding.prefab",
            "Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat",
            "Assets/InfiniteCorridorTask/Materials/Floor.mat",
            "Assets/InfiniteCorridorTask/Materials/Wall.mat",
            "Assets/InfiniteCorridorTask/Materials/TargetMat.mat",
            "Assets/Scenes/ExperimentTemplate.unity",
        };

        /// <summary>The canonical hand-authored zone prefabs that may serve as a clone source.</summary>
        /// <remarks>
        /// Restricting the source to the two protected base prefabs keeps every generated zone descended from a
        /// known-good, hand-authored structure, so the handler validates against a fixed shape and the
        /// hand-authored-versus-generated boundary stays crisp. A third sanctioned base would be added here.
        /// </remarks>
        private static readonly HashSet<string> CloneSourcePrefabs = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab",
        };

        /// <summary>The HTTP listener instance.</summary>
        private static readonly HttpListener Listener = new HttpListener();

        /// <summary>The queue of HTTP requests captured on the listener thread, drained on the editor thread.</summary>
        private static readonly ConcurrentQueue<HttpListenerContext> PendingContexts =
            new ConcurrentQueue<HttpListenerContext>();

        /// <summary>The Unity log entries captured on the logging thread, read on the editor thread.</summary>
        private static readonly ConcurrentQueue<Dictionary<string, object>> ConsoleEntries =
            new ConcurrentQueue<Dictionary<string, object>>();

        /// <summary>The number of log entries captured since the Editor loaded, used to number each entry.</summary>
        private static long _consoleSequence;

        /// <summary>The number of log entries the capacity bound has evicted from the console buffer.</summary>
        private static long _consoleDropped;

        /// <summary>
        /// The <see cref="FullScreenViewManager"/> built for the active scene when the Parameters window is closed,
        /// reused across requests and cleared on every active-scene change.
        /// </summary>
        /// <remarks>
        /// Constructing a manager runs <see cref="Monitor.EnumerateMonitors"/>, which spawns an OS process on Linux
        /// and macOS and opens one short-lived popup window per detected monitor. Caching keeps that work to once per
        /// scene rather than once per Task Parameters request. Monitors re-detected mid-session are picked up through
        /// the <c>refresh_monitors</c> tool or the Camera Mapping refresh button, which share
        /// <see cref="FullScreenViewManager.RefreshMonitorPositions"/>.
        /// </remarks>
        private static FullScreenViewManager _cachedFullScreenManager;

        /// <summary>Starts the HTTP listener and registers the editor update and scene-change callbacks.</summary>
        static McpBridge()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (Scene oldScene, Scene newScene) =>
                _cachedFullScreenManager = null;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;

            try
            {
                // Registers all three loopback hostnames because HttpListener performs exact host-header matching: a
                // client requesting "localhost" is rejected by 127.0.0.1 and [::1] prefixes, even though they resolve
                // to the same socket. The explicit numeric prefixes also work around Mono's IPv6-only resolution of
                // "localhost".
                Listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                Listener.Prefixes.Add($"http://[::1]:{Port}/");
                Listener.Prefixes.Add($"http://localhost:{Port}/");
                Listener.Start();
                Listener.BeginGetContext(OnContextReceived, null);
                EditorApplication.update += Poll;
                string message =
                    $"McpBridge: Listening on http://127.0.0.1:{Port}/, http://[::1]:{Port}/, "
                    + $"and http://localhost:{Port}/";
                Debug.Log(message);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: Failed to start HTTP listener: {exception.Message}");
            }
        }

        /// <summary>Thread-pool callback that captures a completed request and re-arms the listener.</summary>
        /// <param name="asyncResult">The asynchronous result for the completed BeginGetContext call.</param>
        private static void OnContextReceived(IAsyncResult asyncResult)
        {
            if (Listener == null || !Listener.IsListening)
            {
                return;
            }

            try
            {
                HttpListenerContext context = Listener.EndGetContext(asyncResult);
                PendingContexts.Enqueue(context);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: EndGetContext failed: {exception.Message}");
            }

            try
            {
                Listener.BeginGetContext(OnContextReceived, null);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: Failed to re-arm listener: {exception.Message}");
            }
        }

        /// <summary>Captures one Unity log entry into the bounded console buffer.</summary>
        /// <remarks>
        /// Subscribed to the threaded log callback rather than its main-thread counterpart because the bridge
        /// itself logs from the listener thread in <see cref="OnContextReceived"/>, and those failures are the
        /// ones an agent most needs to read. The buffer is therefore a concurrent queue read on the editor
        /// thread, the same boundary <see cref="PendingContexts"/> crosses in the opposite direction.
        /// </remarks>
        /// <param name="condition">The log message text.</param>
        /// <param name="stackTrace">The stack trace Unity captured alongside the message.</param>
        /// <param name="type">The severity Unity assigned to the message.</param>
        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            ConsoleEntries.Enqueue(
                new Dictionary<string, object>
                {
                    { "sequence", System.Threading.Interlocked.Increment(ref _consoleSequence) },
                    { "type", type.ToString() },
                    { "message", condition },
                    { "stack_trace", stackTrace },
                }
            );

            while (ConsoleEntries.Count > ConsoleBufferCapacity && ConsoleEntries.TryDequeue(out _))
            {
                System.Threading.Interlocked.Increment(ref _consoleDropped);
            }
        }

        /// <summary>Drains queued HTTP requests on the editor thread and dispatches each one.</summary>
        private static void Poll()
        {
            while (PendingContexts.TryDequeue(out HttpListenerContext context))
            {
                HandleRequest(context);
            }
        }

        /// <summary>
        /// Reads the request body, dispatches to the appropriate tool handler, and writes the response.
        /// </summary>
        /// <param name="context">
        /// The HTTP listener context containing the request and response objects.
        /// </param>
        private static void HandleRequest(HttpListenerContext context)
        {
            string responseJson;

            try
            {
                using StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();

                Dictionary<string, object> request = MiniJson.Deserialize(body);
                // Uses TryGetValue + null check so a JSON-null value for "tool" does not NRE on ToString().
                // A missing or non-dictionary "args" value falls back to an empty dict so dispatched tools
                // always receive a well-formed arguments parameter.
                string tool =
                    request.TryGetValue("tool", out object toolObject) && toolObject != null
                        ? toolObject.ToString()
                        : string.Empty;
                Dictionary<string, object> arguments =
                    request.TryGetValue("args", out object argsObject)
                    && argsObject is Dictionary<string, object> argumentsDictionary
                        ? argumentsDictionary
                        : new Dictionary<string, object>();

                responseJson = Dispatch(tool, arguments);
            }
            catch (Exception exception)
            {
                responseJson = Error($"Bridge error: {exception.Message}");
            }

            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch (Exception exception)
            {
                // The client drops the connection once its own request timeout expires, which the 30 second bound in
                // the Python relay makes routine for a long generation. Aborting releases the context and its socket,
                // which an escaping exception would leak for the rest of the Editor session.
                Debug.LogWarning($"McpBridge: Failed to deliver response: {exception.Message}");
                context.Response.Abort();
            }
        }

        /// <summary>Routes a tool call to the appropriate handler method.</summary>
        /// <param name="tool">The tool name to dispatch.</param>
        /// <param name="arguments">The tool arguments as a string-keyed dictionary.</param>
        /// <returns>A JSON response string.</returns>
        private static string Dispatch(string tool, Dictionary<string, object> arguments)
        {
            return tool switch
            {
                "create_task" => GenerateTask(arguments),
                "delete_task" => DestroyTask(arguments),
                "inspect_prefab" => InspectPrefab(arguments),
                "clone_zone_prefab" => CloneZonePrefab(arguments),
                "delete_asset" => DeleteAsset(arguments),
                "list_assets" => ListAssets(arguments),
                "refresh_assets" => RefreshAssets(),
                "list_scenes" => ListScenes(),
                "open_scene" => OpenScene(arguments),
                "save_scene" => SaveScene(),
                "inspect_scene" => InspectScene(),
                "enter_play_mode" => EnterPlayMode(),
                "exit_play_mode" => ExitPlayMode(),
                "get_play_state" => GetPlayState(),
                "read_task_parameters" => ReadTaskParameters(),
                "write_task_parameters" => WriteTaskParameters(arguments),
                "refresh_monitors" => RefreshMonitors(),
                "read_console" => ReadConsole(arguments),
                _ => Error($"Unknown tool: {tool}"),
            };
        }

        /// <summary>
        /// Generates a Task end-to-end from a YAML template: builds the task prefab and the matching scene
        /// in one call by chaining <see cref="CreateTask.CreateFromTemplate"/> and
        /// <see cref="CreateTask.CreateSceneFromTemplate"/>.
        /// </summary>
        /// <remarks>
        /// Mirrors the <c>CreateTask/New Task</c> Editor menu so the agentic and manual paths produce
        /// byte-equivalent assets. The prefab lands at <c>Assets/InfiniteCorridorTask/Tasks/&lt;template&gt;.prefab</c>
        /// and the scene at <c>Assets/Scenes/&lt;template&gt;.unity</c>. Both paths are auto-resolved from the
        /// template basename to eliminate the agentic surface's need to manage them separately. Refuses to
        /// clobber an existing scene at the resolved path so an automated client never silently destroys a
        /// hand-edited scene. Use <c>delete_task</c> first to regenerate. The prefab itself is always
        /// regenerated because the template is authoritative.
        /// </remarks>
        /// <param name="arguments">The tool arguments containing template_name and optional unsaved_changes.</param>
        /// <returns>A JSON response with the generated prefab and scene paths or an error message.</returns>
        private static string GenerateTask(Dictionary<string, object> arguments)
        {
            string templateName = GetString(arguments, "template_name");
            string unsavedChanges = GetString(arguments, "unsaved_changes", defaultValue: "");

            if (string.IsNullOrEmpty(templateName))
            {
                return Error("Missing required argument: template_name");
            }

            string absoluteTemplatePath = Path.Combine(
                Application.dataPath,
                "InfiniteCorridorTask",
                "Configurations",
                $"{templateName}.yaml"
            );

            if (!File.Exists(absoluteTemplatePath))
            {
                return Error($"Template not found: {absoluteTemplatePath}");
            }

            // The path is stored on the Task component and resolved at runtime as ``Path.Combine(Application.dataPath,
            // configPath)``. A leading ``/`` would make Path.Combine treat the value as absolute on Linux/macOS and
            // discard the data path.
            string relativeConfigPath = Path.Combine("InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");

            // AssetDatabase paths are forward-slash by contract, so these are built as literals rather than through
            // Path.Combine, whose backslash output on Windows would not match the paths Unity reports back.
            string prefabSavePath = $"Assets/InfiniteCorridorTask/Tasks/{templateName}.prefab";
            string sceneSavePath = $"Assets/Scenes/{templateName}.unity";

            // Refuses to clobber an existing scene before generating the prefab so a regeneration cycle
            // is an explicit two-step action: delete_task first, then create_task. Checking up front
            // avoids leaving a regenerated prefab behind without the matching scene on overwrite refusal.
            // The AssetDatabase resolves the project-relative path against the project root, so the answer holds
            // whatever working directory the Editor process runs under.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sceneSavePath) != null)
            {
                string message = $"Scene already exists at: {sceneSavePath}. Call delete_task first to regenerate.";
                return Error(message);
            }

            // Scene generation opens the new scene, which discards unsaved edits in the active one. Resolving the
            // policy before any asset is written keeps this tool's contract identical to open_scene's.
            string handlingError = HandleUnsavedChanges(unsavedChanges);
            if (handlingError != null)
            {
                return Error(handlingError);
            }

            // Ensures the Tasks output directory exists before CreateFromTemplate writes the prefab.
            string tasksDirectory = Path.GetDirectoryName(prefabSavePath);
            if (!string.IsNullOrEmpty(tasksDirectory) && !AssetDatabase.IsValidFolder(tasksDirectory))
            {
                string parent = Path.GetDirectoryName(tasksDirectory);
                string folder = Path.GetFileName(tasksDirectory);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                {
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }

            string prefabResult = CreateTask.CreateFromTemplate(
                absoluteTemplatePath,
                relativeConfigPath,
                prefabSavePath
            );

            if (prefabResult.StartsWith(CreateTaskErrorPrefix, StringComparison.Ordinal))
            {
                return Error(prefabResult.Substring(CreateTaskErrorPrefix.Length).Trim());
            }

            CreateTask.SceneCreationResult sceneResult = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: sceneSavePath,
                taskPrefabPath: prefabSavePath,
                overwriteExisting: false
            );

            if (!sceneResult.Success)
            {
                string message =
                    $"Prefab generated at {prefabSavePath} but scene creation failed: {sceneResult.Message}";
                return Error(message);
            }

            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "message", sceneResult.Message },
                { "template_name", templateName },
                { "prefab_path", prefabSavePath },
                { "scene_path", sceneSavePath },
                { "simulated_controller_added", sceneResult.SimulatedControllerAdded },
            };

            return Ok(response);
        }

        /// <summary>
        /// Removes every Unity artifact that <see cref="GenerateTask"/> produces for a given template in a
        /// single call: the scene plus its <c>savedFullScreenViews</c> companion, the task prefab, and
        /// every segment prefab this template owns in the Configurations catalog.
        /// </summary>
        /// <remarks>
        /// Mirrors <c>create_task</c> so the two tools cover the full lifecycle of a task's generated
        /// artifacts. Cue prefabs and cue materials are intentionally **not** removed because they are
        /// shared across every template that declares a matching <c>(name, length_cm)</c> identity. Deleting them would
        /// corrupt sibling tasks. Use <c>delete_asset</c> for individual cue cleanup. The template YAML is also
        /// preserved as the source of truth. A companion the cascade could not remove is reported under
        /// <c>companion_delete_failed</c>, because the scene it belonged to is already gone and the orphan is only
        /// recoverable by hand.
        /// </remarks>
        /// <param name="arguments">The tool arguments containing template_name.</param>
        /// <returns>A JSON response listing every deleted path or an error message.</returns>
        private static string DestroyTask(Dictionary<string, object> arguments)
        {
            string templateName = GetString(arguments, "template_name");

            if (string.IsNullOrEmpty(templateName))
            {
                return Error("Missing required argument: template_name");
            }

            // AssetDatabase paths are forward-slash by contract, so these are built as literals rather than through
            // Path.Combine, whose backslash output on Windows would silently fail the comparisons below.
            string scenePath = $"Assets/Scenes/{templateName}.unity";
            string prefabPath = $"Assets/InfiniteCorridorTask/Tasks/{templateName}.prefab";
            string segmentPrefix = $"Assets/InfiniteCorridorTask/Prefabs/{templateName}-";

            // Scene deletion is the one delete path that never consults IsDeleteAllowed, so the protected set is
            // checked here directly. Without it a template name matching a hand-authored asset removes that asset.
            // The scene half is the half the current protected set can fire, because every protected path sits under
            // Scenes, Prefabs, or Materials. The prefab half is standing defense for the first hand-authored asset to
            // land under Tasks, which joins DeleteProtectedPaths in the same change that introduces it.
            if (DeleteProtectedPaths.Contains(scenePath) || DeleteProtectedPaths.Contains(prefabPath))
            {
                string message =
                    $"Refusing to delete task '{templateName}'. Its scene or task prefab is a protected "
                    + "hand-authored asset that the generation pipeline loads by hardcoded path.";
                return Error(message);
            }

            List<string> deletedPaths = new List<string>();
            string companionDeleted = null;
            string companionError = null;

            // Deletes the scene first so Unity can release the active-scene lock before any prefab the
            // scene instantiates is removed. The active-scene swap below is part of this same delete flow.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath) != null)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (string.Equals(activeScene.path, scenePath, StringComparison.Ordinal))
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/ExperimentTemplate.unity", OpenSceneMode.Single);
                }
                if (AssetDatabase.DeleteAsset(scenePath))
                {
                    deletedPaths.Add(scenePath);
                    companionDeleted = TryDeleteScenePerSceneCompanions(scenePath, out companionError);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null)
            {
                if (AssetDatabase.DeleteAsset(prefabPath))
                {
                    deletedPaths.Add(prefabPath);
                }
            }

            // Sweeps every segment prefab this template owns. Segment prefabs are named ``TemplateName-TrialName``
            // and ConfigLoader excludes the hyphen from both halves, so this prefix matches exactly the segments of
            // this template even where another template basename nests it. The sweep is by prefix rather than by
            // current trial name so it also reclaims orphans left by trials since removed from the template.
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/InfiniteCorridorTask/Prefabs" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(segmentPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (AssetDatabase.DeleteAsset(path))
                {
                    deletedPaths.Add(path);
                }
            }

            AssetDatabase.Refresh();

            if (deletedPaths.Count == 0)
            {
                return Error($"No artifacts found for template '{templateName}'.");
            }

            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "message", $"Deleted task: {templateName}" },
                { "template_name", templateName },
                { "deleted_paths", deletedPaths },
                { "deleted", true },
            };
            if (companionDeleted != null)
            {
                response["companion_deleted"] = companionDeleted;
            }
            if (companionError != null)
            {
                response["companion_delete_failed"] = companionError;
            }
            return Ok(response);
        }

        /// <summary>Reads a prefab and returns its hierarchy, components, and BoxCollider details.</summary>
        /// <param name="arguments">The tool arguments containing prefab_path.</param>
        /// <returns>A JSON response with the prefab hierarchy or an error message.</returns>
        private static string InspectPrefab(Dictionary<string, object> arguments)
        {
            string prefabPath = GetString(arguments, "prefab_path");

            if (string.IsNullOrEmpty(prefabPath))
            {
                return Error("Missing required argument: prefab_path");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return Error($"Prefab not found at: {prefabPath}");
            }

            Dictionary<string, object> hierarchy = InspectGameObject(prefab);

            return Ok(new Dictionary<string, object> { { "prefab_path", prefabPath }, { "hierarchy", hierarchy } });
        }

        /// <summary>Clones a canonical zone prefab into a new trigger-zone prefab.</summary>
        /// <remarks>
        /// Performs the prefab-authoring step of adding a new trigger zone through Unity's serialization layer, so
        /// fileIDs, script references, and parent-child wiring are assigned by Unity and stay consistent. The
        /// requested MonoBehaviour scripts must already be authored and compiled. The handler only produces the
        /// prefab. Wiring it into ConfigLoader, CreateTask, the protected-path set, and the Python TriggerType
        /// registry remains the documented recipe. Unity names the new prefab's root after the destination filename.
        /// </remarks>
        /// <param name="arguments">
        /// The tool arguments: source_prefab, destination_prefab, and optional root_script, regions, and overwrite.
        /// </param>
        /// <returns>A JSON response with the destination path and resulting hierarchy, or an error message.</returns>
        private static string CloneZonePrefab(Dictionary<string, object> arguments)
        {
            string sourcePrefab = GetString(arguments, "source_prefab");
            string destinationPrefab = GetString(arguments, "destination_prefab");
            string rootScript = GetString(arguments, "root_script");
            bool overwrite = GetBool(arguments, "overwrite", defaultValue: false);

            if (string.IsNullOrEmpty(sourcePrefab) || string.IsNullOrEmpty(destinationPrefab))
            {
                return Error("Missing required arguments: source_prefab and destination_prefab.");
            }

            if (!CloneSourcePrefabs.Contains(sourcePrefab))
            {
                string allowed = string.Join(", ", CloneSourcePrefabs);
                return Error($"source_prefab must be a canonical base zone prefab ({allowed}).");
            }

            string destinationError = ValidateCloneDestination(destinationPrefab);
            if (destinationError != null)
            {
                return Error(destinationError);
            }

            bool destinationExists = AssetDatabase.LoadAssetAtPath<GameObject>(destinationPrefab) != null;
            if (destinationExists && !overwrite)
            {
                return Error($"A prefab already exists at '{destinationPrefab}'. Pass overwrite=true to replace it.");
            }

            // Resolves requested scripts up front so a bad name fails before any asset is written, which includes
            // the overwrite delete below. Resolving after it would destroy the existing prefab and replace nothing.
            string resolveError = ResolveCloneScripts(
                rootScript,
                GetList(arguments, "regions"),
                out Type rootScriptType,
                out List<(Dictionary<string, object> Spec, Type ScriptType)> regionEdits
            );
            if (resolveError != null)
            {
                return Error(resolveError);
            }

            if (destinationExists)
            {
                AssetDatabase.DeleteAsset(destinationPrefab);
            }

            if (!AssetDatabase.CopyAsset(sourcePrefab, destinationPrefab))
            {
                return Error($"Failed to copy '{sourcePrefab}' to '{destinationPrefab}'.");
            }

            string editError = null;
            GameObject root = PrefabUtility.LoadPrefabContents(destinationPrefab);
            try
            {
                if (rootScriptType != null)
                {
                    editError = SwapZoneScript(
                        root,
                        rootScriptType,
                        requireBaseType: typeof(StimulusTriggerZone),
                        fields: null
                    );
                }

                for (int i = 0; editError == null && i < regionEdits.Count; i++)
                {
                    editError = ApplyRegionEdit(root, regionEdits[i]);
                }

                if (editError == null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, destinationPrefab);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (editError != null)
            {
                AssetDatabase.DeleteAsset(destinationPrefab);
                return Error(editError);
            }

            AssetDatabase.Refresh();

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(destinationPrefab);
            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "destination_prefab", destinationPrefab },
                { "hierarchy", InspectGameObject(saved) },
                {
                    "warning",
                    "Prefab created. Still required to make it usable: add the path to "
                        + "McpBridge.DeleteProtectedPaths, add a Place...Zone branch in CreateTask, "
                        + "accept the new trigger_type literal in ConfigLoader, and register the "
                        + "TriggerType member in sollertia-shared-assets."
                },
            };
            return Ok(response);
        }

        /// <summary>Resolves the root and region script names to compiled types before any asset is written.</summary>
        /// <param name="rootScript">The root script type name, or null to keep the source root script.</param>
        /// <param name="regions">The raw region edit specifications from the request.</param>
        /// <param name="rootScriptType">The resolved root script type, or null when none was requested.</param>
        /// <param name="regionEdits">Validated region specs paired with resolved script types.</param>
        /// <returns>An error message when a name fails to resolve or a region is malformed, otherwise null.</returns>
        private static string ResolveCloneScripts(
            string rootScript,
            List<object> regions,
            out Type rootScriptType,
            out List<(Dictionary<string, object> Spec, Type ScriptType)> regionEdits
        )
        {
            rootScriptType = null;
            regionEdits = new List<(Dictionary<string, object>, Type)>();

            if (!string.IsNullOrEmpty(rootScript))
            {
                string error = ResolveMonoBehaviourType(rootScript, out rootScriptType);
                if (error != null)
                {
                    return error;
                }
            }

            foreach (object regionObject in regions)
            {
                if (regionObject is not Dictionary<string, object> spec)
                {
                    return "Each entry in 'regions' must be an object.";
                }

                if (string.IsNullOrEmpty(GetString(spec, "match")))
                {
                    return "Each region edit must specify 'match' (the name of the region to modify).";
                }

                Type scriptType = null;
                string scriptName = GetString(spec, "script");
                if (!string.IsNullOrEmpty(scriptName))
                {
                    string error = ResolveMonoBehaviourType(scriptName, out scriptType);
                    if (error != null)
                    {
                        return error;
                    }
                }

                regionEdits.Add((spec, scriptType));
            }

            return null;
        }

        /// <summary>Resolves a MonoBehaviour type by its simple name across compiled assemblies.</summary>
        /// <param name="typeName">The simple class name to resolve.</param>
        /// <param name="resolved">The resolved type on success, otherwise null.</param>
        /// <returns>An error message when the name is unknown or ambiguous, otherwise null.</returns>
        private static string ResolveMonoBehaviourType(string typeName, out Type resolved)
        {
            resolved = null;
            List<Type> matches = TypeCache
                .GetTypesDerivedFrom<MonoBehaviour>()
                .Where(type => string.Equals(type.Name, typeName, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                return $"Script type '{typeName}' not found. Author the script and let the project compile.";
            }

            if (matches.Count > 1)
            {
                return $"Script type '{typeName}' is ambiguous ({matches.Count} matches). Use a unique class name.";
            }

            // AddComponent returns null for an abstract type, which the swap path would then hand to SerializedObject
            // and abort past its own rollback, leaving a half-authored prefab behind.
            if (matches[0].IsAbstract)
            {
                return $"Script type '{typeName}' is abstract. Name a concrete MonoBehaviour subclass.";
            }

            resolved = matches[0];
            return null;
        }

        /// <summary>Validates that a clone destination is a safe, unprotected path under Prefabs/.</summary>
        /// <param name="destinationPrefab">The requested destination asset path.</param>
        /// <returns>An error message when the path is unsafe, misplaced, or protected, otherwise null.</returns>
        private static string ValidateCloneDestination(string destinationPrefab)
        {
            if (destinationPrefab.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(destinationPrefab))
            {
                return $"Invalid destination_prefab '{destinationPrefab}': traversal and absolute paths are rejected.";
            }

            if (!destinationPrefab.StartsWith("Assets/InfiniteCorridorTask/Prefabs/", StringComparison.Ordinal))
            {
                return "destination_prefab must be under Assets/InfiniteCorridorTask/Prefabs/.";
            }

            if (!destinationPrefab.EndsWith(".prefab", StringComparison.Ordinal))
            {
                return "destination_prefab must end with .prefab.";
            }

            if (DeleteProtectedPaths.Contains(destinationPrefab) || CloneSourcePrefabs.Contains(destinationPrefab))
            {
                return $"destination_prefab '{destinationPrefab}' is a protected base prefab.";
            }

            return null;
        }

        /// <summary>Applies one region edit (rename, script swap, field overrides) to a cloned prefab.</summary>
        /// <param name="root">The root GameObject of the loaded prefab contents.</param>
        /// <param name="edit">The validated region specification paired with its resolved script type.</param>
        /// <returns>An error message when the region cannot be located or edited, otherwise null.</returns>
        private static string ApplyRegionEdit(GameObject root, (Dictionary<string, object> Spec, Type ScriptType) edit)
        {
            GameObject region = FindUniqueDescendant(root, GetString(edit.Spec, "match"), out string findError);
            if (findError != null)
            {
                return findError;
            }

            string rename = GetString(edit.Spec, "rename");
            if (!string.IsNullOrEmpty(rename))
            {
                region.name = rename;
            }

            Dictionary<string, object> fields = GetDictionary(edit.Spec, "fields");

            if (edit.ScriptType != null)
            {
                return SwapZoneScript(region, edit.ScriptType, requireBaseType: null, fields: fields);
            }

            if (fields.Count > 0)
            {
                MonoBehaviour modifier = FindSingleZoneModifier(region, out string modifierError);
                if (modifierError != null)
                {
                    return modifierError;
                }

                return ApplyFieldOverrides(modifier, fields);
            }

            return null;
        }

        /// <summary>Replaces a GameObject's single modifier script, preserving shared field values.</summary>
        /// <param name="target">The GameObject whose modifier script is replaced.</param>
        /// <param name="scriptType">The replacement MonoBehaviour type.</param>
        /// <param name="requireBaseType">A base type the replacement must derive from, or null to allow any.</param>
        /// <param name="fields">Field overrides to apply after the swap, or null to apply none.</param>
        /// <returns>An error message when the swap or overrides fail, otherwise null.</returns>
        private static string SwapZoneScript(
            GameObject target,
            Type scriptType,
            Type requireBaseType,
            Dictionary<string, object> fields
        )
        {
            if (requireBaseType != null && !requireBaseType.IsAssignableFrom(scriptType))
            {
                return $"Root script '{scriptType.Name}' must derive from {requireBaseType.Name}.";
            }

            MonoBehaviour existing = FindSingleZoneModifier(target, out string modifierError);
            if (modifierError != null)
            {
                return modifierError;
            }

            Component added = target.AddComponent(scriptType);
            CopyMatchingSerializedFields(existing, added);
            UnityEngine.Object.DestroyImmediate(existing, allowDestroyingAssets: true);

            if (fields != null && fields.Count > 0)
            {
                return ApplyFieldOverrides(added, fields);
            }

            return null;
        }

        /// <summary>Finds the single modifier MonoBehaviour on a GameObject.</summary>
        /// <param name="target">The GameObject to inspect.</param>
        /// <param name="error">An error message when the modifier count is not exactly one, otherwise null.</param>
        /// <returns>The single MonoBehaviour, or null when the count is not exactly one.</returns>
        private static MonoBehaviour FindSingleZoneModifier(GameObject target, out string error)
        {
            error = null;
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            if (behaviours.Length != 1)
            {
                error = $"Expected exactly one modifier script on '{target.name}', but found {behaviours.Length}.";
                return null;
            }

            return behaviours[0];
        }

        /// <summary>Finds the single named descendant GameObject, rejecting an absent or ambiguous match.</summary>
        /// <param name="root">The root GameObject to search beneath.</param>
        /// <param name="name">The descendant name to match.</param>
        /// <param name="error">An error message when the match count is not exactly one, otherwise null.</param>
        /// <returns>The matched GameObject, or null when the match count is not exactly one.</returns>
        private static GameObject FindUniqueDescendant(GameObject root, string name, out string error)
        {
            error = null;
            List<Transform> matches = root.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(child => child != root.transform && string.Equals(child.name, name, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                error = $"No region named '{name}' was found under the cloned prefab.";
                return null;
            }

            if (matches.Count > 1)
            {
                error = $"Region name '{name}' is ambiguous ({matches.Count} matches) under the cloned prefab.";
                return null;
            }

            return matches[0].gameObject;
        }

        /// <summary>Copies serialized values between two components for every property they share by path.</summary>
        /// <param name="from">The source whose serialized property values are read.</param>
        /// <param name="to">The destination that receives the values of the properties it also declares.</param>
        private static void CopyMatchingSerializedFields(Component from, Component to)
        {
            SerializedObject source = new SerializedObject(from);
            SerializedObject destination = new SerializedObject(to);

            SerializedProperty property = source.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (string.Equals(property.name, "m_Script", StringComparison.Ordinal))
                {
                    continue;
                }

                if (destination.FindProperty(property.propertyPath) != null)
                {
                    destination.CopyFromSerializedProperty(property);
                }
            }

            destination.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Applies field overrides onto a component, rejecting unknown or mistyped fields.</summary>
        /// <param name="target">The component whose serialized fields are overridden.</param>
        /// <param name="fields">The field-name to value map to apply.</param>
        /// <returns>An error message when a field is unknown or cannot be assigned, otherwise null.</returns>
        private static string ApplyFieldOverrides(Component target, Dictionary<string, object> fields)
        {
            SerializedObject serialized = new SerializedObject(target);
            foreach (KeyValuePair<string, object> field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field.Key);
                if (property == null)
                {
                    return $"Field '{field.Key}' does not exist on {target.GetType().Name}.";
                }

                string error = SetSerializedProperty(property, field.Value);
                if (error != null)
                {
                    return error;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return null;
        }

        /// <summary>Assigns a boxed value to a serialized property, matching its type.</summary>
        /// <param name="property">The serialized property to assign.</param>
        /// <param name="value">The boxed value from the request payload.</param>
        /// <returns>An error message when the type is unsupported or the conversion fails, otherwise null.</returns>
        private static string SetSerializedProperty(SerializedProperty property, object value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return null;
                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return null;
                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return null;
                    case SerializedPropertyType.String:
                        property.stringValue = value.ToString();
                        return null;
                    case SerializedPropertyType.Enum:
                        property.intValue = Convert.ToInt32(value);
                        return null;
                    default:
                        return $"Field '{property.name}' has unsupported type {property.propertyType}.";
                }
            }
            catch (Exception exception)
                when (exception is FormatException
                    || exception is InvalidCastException
                    || exception is OverflowException
                )
            {
                return $"Failed to set field '{property.name}': {exception.Message}";
            }
        }

        /// <summary>Retrieves a boolean value from the arguments dictionary with an optional default.</summary>
        /// <param name="arguments">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="defaultValue">The default value when the key is absent or unparseable.</param>
        /// <returns>The parsed boolean value, or the default.</returns>
        private static bool GetBool(Dictionary<string, object> arguments, string key, bool defaultValue = false)
        {
            if (arguments.TryGetValue(key, out object value) && value != null)
            {
                if (value is bool boolValue)
                {
                    return boolValue;
                }

                if (bool.TryParse(value.ToString(), out bool parsed))
                {
                    return parsed;
                }
            }

            return defaultValue;
        }

        /// <summary>Retrieves a list value from the arguments dictionary, or an empty list when absent.</summary>
        /// <param name="arguments">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The list value, or an empty list when the key is absent or not a list.</returns>
        private static List<object> GetList(Dictionary<string, object> arguments, string key)
        {
            if (arguments.TryGetValue(key, out object value) && value is List<object> list)
            {
                return list;
            }

            return new List<object>();
        }

        /// <summary>Retrieves a nested object from the arguments dictionary, or empty when absent.</summary>
        /// <param name="arguments">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The dictionary value, or an empty dictionary when the key is absent or not an object.</returns>
        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> arguments, string key)
        {
            if (arguments.TryGetValue(key, out object value) && value is Dictionary<string, object> dictionary)
            {
                return dictionary;
            }

            return new Dictionary<string, object>();
        }

        /// <summary>Deletes a Unity asset within an allowed directory and refreshes the AssetDatabase.</summary>
        /// <remarks>
        /// Scoped to regenerable non-scene assets, primarily cue prefabs and cue materials that the
        /// <see cref="GenerateTask"/> pipeline shares across templates and therefore cannot scrub per-task. Scene
        /// deletion is handled exclusively by <see cref="DestroyTask"/>, which removes the scene plus its
        /// <c>savedFullScreenViews</c> companion atomically. Scene paths submitted here are rejected with a pointer at
        /// <c>delete_task</c> so scene cleanup never bypasses the companion cascade.
        /// </remarks>
        /// <param name="arguments">The tool arguments containing asset_path.</param>
        /// <returns>A JSON response confirming deletion or an error message.</returns>
        private static string DeleteAsset(Dictionary<string, object> arguments)
        {
            string assetPath = GetString(arguments, "asset_path");

            if (string.IsNullOrEmpty(assetPath))
            {
                return Error("Missing required argument: asset_path");
            }

            if (
                assetPath.StartsWith("Assets/Scenes/", StringComparison.Ordinal)
                && assetPath.EndsWith(".unity", StringComparison.Ordinal)
            )
            {
                string message =
                    $"Refusing to delete scene '{assetPath}' via delete_asset. Use delete_task to remove a "
                    + "task's scene together with its task prefab and segment prefabs in one atomic call.";
                return Error(message);
            }

            if (!IsDeleteAllowed(assetPath))
            {
                string allowedRoots = string.Join(", ", DeleteAllowedPrefixes);
                string message =
                    $"Refusing to delete '{assetPath}'. Deletion is permitted only for individual assets under: "
                    + $"{allowedRoots}. Hand-authored prefabs and the experiment template scene are protected.";
                return Error(message);
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
            {
                return Error($"Asset not found at: {assetPath}");
            }

            bool deleted = AssetDatabase.DeleteAsset(assetPath);
            if (!deleted)
            {
                return Error($"Failed to delete asset at: {assetPath}");
            }

            AssetDatabase.Refresh();

            return Ok(
                new Dictionary<string, object>
                {
                    { "message", $"Deleted asset: {assetPath}" },
                    { "asset_path", assetPath },
                    { "deleted", true },
                }
            );
        }

        /// <summary>Deletes per-scene companion assets when a scene under Assets/Scenes/ is removed.</summary>
        /// <remarks>
        /// Bypasses the standard <see cref="IsDeleteAllowed"/> prefix check because the companion path is
        /// derived from the just-validated scene path, never user-supplied. Currently covers the saved
        /// full-screen-views asset, and every new per-scene companion asset is added to this method.
        /// </remarks>
        /// <param name="scenePath">The project-relative path of the scene that was just deleted.</param>
        /// <param name="error">A message naming the companion left orphaned by a refused deletion, otherwise null.
        /// </param>
        /// <returns>The companion path that was deleted, or null when no companion was deleted.</returns>
        private static string TryDeleteScenePerSceneCompanions(string scenePath, out string error)
        {
            error = null;
            if (
                !scenePath.StartsWith("Assets/Scenes/", StringComparison.Ordinal)
                || !scenePath.EndsWith(".unity", StringComparison.Ordinal)
            )
            {
                return null;
            }
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string companionPath = $"Assets/VRSettings/Displays/{sceneName}-savedFullScreenViews.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath) == null)
            {
                return null;
            }
            if (AssetDatabase.DeleteAsset(companionPath))
            {
                return companionPath;
            }

            error =
                $"Unable to delete the per-scene companion asset at {companionPath}. The companion must be "
                + "removable for the scene deletion to complete, but Unity refused the deletion, so the companion "
                + "is now orphaned and has to be removed by hand.";
            return null;
        }

        /// <summary>
        /// Lists Unity assets of a given type filter (e.g., "Prefab", "Scene", "Material").
        /// </summary>
        /// <param name="arguments">The tool arguments containing optional asset_type and search_path filters.</param>
        /// <returns>A JSON response with matching asset paths.</returns>
        private static string ListAssets(Dictionary<string, object> arguments)
        {
            string assetType = GetString(arguments, "asset_type", defaultValue: "Prefab");
            string searchPath = GetString(arguments, "search_path", defaultValue: "Assets/InfiniteCorridorTask");

            string[] guids = AssetDatabase.FindAssets($"t:{assetType}", new[] { searchPath });
            List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToList();

            return Ok(
                new Dictionary<string, object>
                {
                    { "asset_type", assetType },
                    { "search_path", searchPath },
                    { "assets", paths },
                }
            );
        }

        /// <summary>Imports pending asset changes and reports whether a script compilation followed.</summary>
        /// <remarks>
        /// The agentic counterpart of the Editor's automatic refresh on focus. A headless Editor never regains
        /// focus, so a C# file written from outside stays uncompiled and its type stays unresolvable until this
        /// runs. Compilation is queued rather than immediate, so a true is_compiling means the domain reload has
        /// not finished and the caller polls get_play_state until it reports a state other than compiling.
        /// </remarks>
        /// <returns>A JSON response with the post-import compilation state.</returns>
        private static string RefreshAssets()
        {
            AssetDatabase.Refresh();

            return Ok(
                new Dictionary<string, object>
                {
                    { "message", "Imported pending asset changes." },
                    { "is_compiling", EditorApplication.isCompiling },
                    { "is_updating", EditorApplication.isUpdating },
                }
            );
        }

        /// <summary>Lists all scene assets in the project.</summary>
        /// <returns>A JSON response with all scene paths and the active scene.</returns>
        private static string ListScenes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToList();

            string activeScene = SceneManager.GetActiveScene().path;

            return Ok(new Dictionary<string, object> { { "scenes", paths }, { "active_scene", activeScene } });
        }

        /// <summary>Opens a scene in the Editor after applying the unsaved-changes policy.</summary>
        /// <param name="arguments">The tool arguments containing scene_path and optional unsaved_changes.</param>
        /// <returns>A JSON response confirming the scene was opened or an error message.</returns>
        private static string OpenScene(Dictionary<string, object> arguments)
        {
            string scenePath = GetString(arguments, "scene_path");
            string unsavedChanges = GetString(arguments, "unsaved_changes", defaultValue: "");

            if (string.IsNullOrEmpty(scenePath))
            {
                return Error("Missing required argument: scene_path");
            }

            // The AssetDatabase resolves the project-relative path against the project root, so the answer holds
            // whatever working directory the Editor process runs under, and typing the lookup to SceneAsset keeps a
            // folder or a non-scene asset out of the scene that EditorSceneManager is asked to open.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return Error($"Scene not found at: {scenePath}");
            }

            string handlingError = HandleUnsavedChanges(unsavedChanges);
            if (handlingError != null)
            {
                return Error(handlingError);
            }

            EditorSceneManager.OpenScene(scenePath);

            return Ok(
                new Dictionary<string, object>
                {
                    { "message", $"Opened scene: {scenePath}" },
                    { "scene_path", scenePath },
                }
            );
        }

        /// <summary>Saves the active scene to its existing asset path.</summary>
        /// <remarks>
        /// Clears the dirty flag that every write_task_parameters call sets, which the play-mode preflight
        /// requires. An unsaved scene has no asset path to save to and is rejected rather than routed into a
        /// save dialog, because the bridge answers a headless caller that cannot dismiss one. Play Mode is
        /// likewise rejected, since edits made there are discarded on exit and saving them is never intended.
        /// </remarks>
        /// <returns>A JSON response with the saved path and the post-save dirty state, or an error message.</returns>
        private static string SaveScene()
        {
            if (EditorApplication.isPlaying)
            {
                return Error("Cannot save the active scene while the Editor is in Play Mode.");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                string message =
                    $"Cannot save the active scene '{activeScene.name}': it has never been saved, so it has no "
                    + "asset path. Generate it through create_task or save it once by hand first.";
                return Error(message);
            }

            if (!EditorSceneManager.SaveScene(activeScene))
            {
                return Error($"Unable to save the active scene to: {activeScene.path}");
            }

            return Ok(
                new Dictionary<string, object>
                {
                    { "message", $"Saved scene: {activeScene.path}" },
                    { "scene_path", activeScene.path },
                    { "is_dirty", activeScene.isDirty },
                }
            );
        }

        /// <summary>Inspects the active scene and returns its root GameObject hierarchy.</summary>
        /// <returns>A JSON response with scene metadata and the recursive root object hierarchies.</returns>
        private static string InspectScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();

            List<Dictionary<string, object>> rootHierarchies = new List<Dictionary<string, object>>();
            foreach (GameObject rootObject in rootObjects)
            {
                rootHierarchies.Add(InspectGameObject(rootObject));
            }

            return Ok(
                new Dictionary<string, object>
                {
                    { "scene_path", activeScene.path },
                    { "scene_name", activeScene.name },
                    { "is_dirty", activeScene.isDirty },
                    { "root_objects", rootHierarchies },
                }
            );
        }

        /// <summary>Enters Play Mode in the Editor.</summary>
        /// <returns>A JSON response with the current play state.</returns>
        private static string EnterPlayMode()
        {
            if (EditorApplication.isPlaying)
            {
                return Ok(
                    new Dictionary<string, object> { { "message", "Already in Play Mode." }, { "state", "playing" } }
                );
            }

            EditorApplication.EnterPlaymode();

            return Ok(
                new Dictionary<string, object>
                {
                    { "message", "Entering Play Mode." },
                    { "state", "entering_play_mode" },
                }
            );
        }

        /// <summary>Exits Play Mode in the Editor.</summary>
        /// <returns>A JSON response with the current play state.</returns>
        private static string ExitPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                return Ok(new Dictionary<string, object> { { "message", "Not in Play Mode." }, { "state", "edit" } });
            }

            EditorApplication.ExitPlaymode();

            return Ok(
                new Dictionary<string, object> { { "message", "Exiting Play Mode." }, { "state", "exiting_play_mode" } }
            );
        }

        /// <summary>Returns the current Editor play state.</summary>
        /// <returns>A JSON response with the current state and active scene name.</returns>
        private static string GetPlayState()
        {
            string state =
                EditorApplication.isPlaying ? "playing"
                : EditorApplication.isCompiling ? "compiling"
                : "edit";

            return Ok(
                new Dictionary<string, object>
                {
                    { "state", state },
                    { "active_scene", SceneManager.GetActiveScene().name },
                }
            );
        }

        /// <summary>
        /// Returns a single-scan snapshot of every Task Parameters field plus options and visibility.
        /// </summary>
        /// <remarks>
        /// State, options, and visibility are derived from a single scene walk so an agent that reads,
        /// modifies, and writes back values does not race against a separate enumeration pass. Cameras are
        /// filtered to match the GUI dropdown (Main Camera excluded). Monitor mapping is sourced from the
        /// open Parameters window's FullScreenViewManager when available, falling back to a fresh manager
        /// loaded from <c>savedFullScreenViews.asset</c> when the window is closed.
        /// </remarks>
        /// <returns>A JSON response with state, options, and visibility nested dictionaries.</returns>
        private static string ReadTaskParameters()
        {
            return Ok(BuildSnapshot(AcquireSceneComponents()));
        }

        /// <summary>Applies the supplied parameter subset and returns the post-write snapshot.</summary>
        /// <remarks>
        /// Each section is optional and individual fields within a section are also optional. The whole request is
        /// validated before any section applies, so a rejected value leaves the scene untouched rather than partly
        /// written. Validation rejects values outside the enumeration reported by <see cref="ReadTaskParameters"/>,
        /// and rejects require_interaction / require_wait writes when the corresponding zone is absent from the
        /// scene so the agent contract matches the GUI's conditional rendering. Mutations flow through the same code
        /// paths the Parameters window uses, including <see cref="EditorUtility.SetDirty"/> on touched assets and a
        /// final <see cref="EditorSceneManager.MarkSceneDirty"/> when any write succeeded.
        /// </remarks>
        /// <param name="arguments">
        /// The dispatched tool arguments. Optional top-level keys are <c>actor</c>, <c>mqtt</c>, <c>display</c>,
        /// <c>camera_mapping</c>, and <c>task</c>, each carrying the field subset to write.
        /// </param>
        /// <returns>A JSON response carrying the post-write snapshot from <see cref="ReadTaskParameters"/>.</returns>
        private static string WriteTaskParameters(Dictionary<string, object> arguments)
        {
            SceneComponents components = AcquireSceneComponents();

            string validationError = ValidateTaskParameterWrites(arguments, components);
            if (validationError != null)
            {
                return Error(validationError);
            }

            bool dirty = false;
            ApplyActorSection(arguments, components, ref dirty);
            ApplyMqttSection(arguments, components, ref dirty);
            ApplyDisplaySection(arguments, components, ref dirty);
            ApplyCameraMappingSection(arguments, components, ref dirty);
            ApplyTaskSection(arguments, components, ref dirty);

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return Ok(BuildSnapshot(components));
        }

        /// <summary>Re-detects the system monitors and returns the post-refresh snapshot.</summary>
        /// <remarks>
        /// The agentic counterpart of the Camera Mapping window's Refresh Monitor Positions button, sharing
        /// <see cref="FullScreenViewManager.RefreshMonitorPositions"/> so both paths re-detect identically. Existing
        /// camera assignments carry across by monitor index, and the refreshed list is not persisted to the
        /// per-scene companion asset until a camera assignment is written. Call this after changing the physical
        /// monitor arrangement, because the bridge otherwise reuses one monitor enumeration per scene.
        /// </remarks>
        /// <returns>A JSON response in the same snapshot shape <see cref="ReadTaskParameters"/> returns.</returns>
        private static string RefreshMonitors()
        {
            SceneComponents components = AcquireSceneComponents();
            components.FullScreenManager.RefreshMonitorPositions();
            return Ok(BuildSnapshot(components));
        }

        /// <summary>Returns the buffered Unity log entries, oldest first, after applying the filters.</summary>
        /// <remarks>
        /// The buffer holds the last <see cref="ConsoleBufferCapacity"/> entries logged since the Editor loaded,
        /// so it answers what this session logged rather than what the Console window currently displays. Poll it
        /// by passing the previous response's next_sequence back as since_sequence, which returns only entries
        /// logged after that point, oldest first, so repeated polls walk the buffer without skipping an entry. A
        /// call that omits since_sequence instead returns the newest matching entries, which is what a diagnosis
        /// after a failure needs. Entries go missing through two channels. A dropped count that grew since the
        /// previous call means the capacity bound evicted entries, which a caller lost only if its own polling
        /// fell behind. A matched count above count means limit truncated that many further matching entries
        /// out of this response.
        /// </remarks>
        /// <param name="arguments">The tool arguments containing optional level, limit, and since_sequence.</param>
        /// <returns>A JSON response with the matching entries or an error message.</returns>
        private static string ReadConsole(Dictionary<string, object> arguments)
        {
            string level = GetString(arguments, "level", defaultValue: "all");
            if (!IsKnownConsoleLevel(level))
            {
                return Error($"Unknown level: {level}. The accepted values are all, log, warning, and error.");
            }

            int limit = DefaultConsoleReadLimit;
            if (arguments.TryGetValue("limit", out object limitObject) && limitObject != null)
            {
                if (!TryConvertInt(limitObject, out limit))
                {
                    return Error($"The limit argument must be an integer, but it is '{limitObject}'.");
                }

                if (limit < 1)
                {
                    return Error($"The limit argument must be at least 1, but it is {limit}.");
                }
            }

            int sinceSequence = 0;
            bool polling = false;
            if (arguments.TryGetValue("since_sequence", out object sinceObject) && sinceObject != null)
            {
                if (!TryConvertInt(sinceObject, out sinceSequence) || sinceSequence < 0)
                {
                    string message =
                        $"The since_sequence argument must be a non-negative integer, but it is '{sinceObject}'.";
                    return Error(message);
                }

                polling = true;
            }

            Dictionary<string, object>[] buffered = ConsoleEntries.ToArray();
            List<Dictionary<string, object>> matching = buffered
                .Where(entry => Convert.ToInt64(entry["sequence"]) > sinceSequence)
                .Where(entry => MatchesConsoleLevel((string)entry["type"], level))
                .ToList();
            // A polling caller drains forward from its checkpoint so no entry is skipped between calls, while a
            // one-shot caller takes the newest entries, which is what a diagnosis after a failure needs.
            List<Dictionary<string, object>> returned = polling
                ? matching.Take(limit).ToList()
                : matching.Skip(Math.Max(0, matching.Count - limit)).ToList();

            // Scans for the maximum rather than reading the tail, because assigning a sequence and enqueueing an
            // entry are not one atomic step, so two logging threads can leave the queue out of sequence order.
            long bufferedMaximum =
                buffered.Length == 0 ? sinceSequence : buffered.Max(entry => Convert.ToInt64(entry["sequence"]));
            long nextSequence =
                polling && returned.Count > 0
                    ? Convert.ToInt64(returned[returned.Count - 1]["sequence"])
                    : bufferedMaximum;

            return Ok(
                new Dictionary<string, object>
                {
                    { "entries", returned },
                    { "count", returned.Count },
                    { "matched", matching.Count },
                    { "next_sequence", nextSequence },
                    { "dropped", System.Threading.Interlocked.Read(ref _consoleDropped) },
                    { "capacity", ConsoleBufferCapacity },
                }
            );
        }

        /// <summary>Reports whether a level filter names a severity group that read_console accepts.</summary>
        /// <param name="level">The requested level filter.</param>
        /// <returns>True when the filter is one of the four accepted values.</returns>
        private static bool IsKnownConsoleLevel(string level)
        {
            return string.Equals(level, "all", StringComparison.Ordinal)
                || string.Equals(level, "log", StringComparison.Ordinal)
                || string.Equals(level, "warning", StringComparison.Ordinal)
                || string.Equals(level, "error", StringComparison.Ordinal);
        }

        /// <summary>Reports whether a captured entry's severity falls inside the requested level group.</summary>
        /// <remarks>
        /// The error group covers Error, Exception, and Assert together, because the three mean the same thing
        /// to a caller diagnosing a failed run and Unity's own Console collapses them onto one toggle.
        /// </remarks>
        /// <param name="type">The serialized log type carried by the entry.</param>
        /// <param name="level">The requested level filter.</param>
        /// <returns>True when the entry belongs in the filtered result.</returns>
        private static bool MatchesConsoleLevel(string type, string level)
        {
            if (string.Equals(level, "all", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(level, "error", StringComparison.Ordinal))
            {
                return string.Equals(type, nameof(LogType.Error), StringComparison.Ordinal)
                    || string.Equals(type, nameof(LogType.Exception), StringComparison.Ordinal)
                    || string.Equals(type, nameof(LogType.Assert), StringComparison.Ordinal);
            }

            if (string.Equals(level, "warning", StringComparison.Ordinal))
            {
                return string.Equals(type, nameof(LogType.Warning), StringComparison.Ordinal);
            }

            return string.Equals(type, nameof(LogType.Log), StringComparison.Ordinal);
        }

        /// <summary>
        /// Performs the single scene walk shared by <see cref="ReadTaskParameters"/> and
        /// <see cref="WriteTaskParameters"/>.
        /// </summary>
        /// <returns>A snapshot of every component the Task Parameters endpoints consume.</returns>
        private static SceneComponents AcquireSceneComponents()
        {
            return new SceneComponents
            {
                Actor = UnityEngine.Object.FindAnyObjectByType<ActorObject>(),
                Display = UnityEngine.Object.FindAnyObjectByType<DisplayObject>(),
                Task = UnityEngine.Object.FindAnyObjectByType<Task>(),
                Client = UnityEngine.Object.FindAnyObjectByType<MQTTClient>(),
                Controllers = UnityEngine.Object.FindObjectsByType<ControllerOutput>(FindObjectsSortMode.None),
                DisplayCameras = GetDisplayCameras(),
                HasInteractionZone = UnityEngine.Object.FindAnyObjectByType<GuidanceZone>() != null,
                HasOccupancyZone = UnityEngine.Object.FindAnyObjectByType<OccupancyZone>() != null,
                FullScreenManager = AcquireFullScreenManager(),
            };
        }

        /// <summary>Returns every assignable display camera in the active scene (Main Camera excluded).</summary>
        /// <remarks>
        /// Mirrors the filter applied by <see cref="FullScreenViewManager"/>'s dropdown so the agent surface
        /// and the GUI agree on which cameras can be bound to monitors.
        /// </remarks>
        private static Camera[] GetDisplayCameras()
        {
            return UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", StringComparison.Ordinal)
                )
                .ToArray();
        }

        /// <summary>Returns every valid actor model name (every Resources/Actors/Prefabs entry plus "None").</summary>
        private static string[] GetValidActorModels()
        {
            return Resources
                .LoadAll<GameObject>("Actors/Prefabs")
                .Select(prefab => prefab.name)
                .Append("None")
                .ToArray();
        }

        /// <summary>
        /// Builds the nested state/options/visibility dictionary that <see cref="ReadTaskParameters"/> returns.
        /// </summary>
        /// <remarks>
        /// Takes a pre-acquired <see cref="SceneComponents"/> rather than re-walking the scene so the
        /// post-write response from <see cref="WriteTaskParameters"/> reuses the same component references
        /// it already validated against, avoiding a third scene scan per request.
        /// </remarks>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <returns>The response payload ready for <see cref="Ok"/>.</returns>
        private static Dictionary<string, object> BuildSnapshot(SceneComponents components)
        {
            Dictionary<string, object> actorState = null;
            if (components.Actor != null)
            {
                string currentModel = "None";
                foreach (Transform child in components.Actor.transform)
                {
                    if (child.name.StartsWith("Model ", StringComparison.Ordinal))
                    {
                        currentModel = child.name.Substring("Model ".Length);
                        break;
                    }
                }
                actorState = new Dictionary<string, object>
                {
                    { "model", currentModel },
                    {
                        "controller",
                        components.Actor.Controller == null ? "None" : components.Actor.Controller.gameObject.name
                    },
                };
            }

            Dictionary<string, object> mqttState =
                components.Client == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "ip", components.Client.ipAddress },
                        { "port", components.Client.port },
                    };

            Dictionary<string, object> displayState =
                components.Display == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "current_brightness", components.Display.currentBrightness },
                        {
                            "brightness",
                            components.Display.settings != null ? components.Display.settings.brightness : 100f
                        },
                        {
                            "height_in_vr",
                            components.Display.settings != null ? components.Display.settings.heightInVR : 0f
                        },
                    };

            List<Dictionary<string, object>> cameraMappingState = new List<Dictionary<string, object>>();
            for (int monitorIndex = 0; monitorIndex < components.FullScreenManager.monitors.Count; monitorIndex++)
            {
                Monitor monitor = components.FullScreenManager.monitors[monitorIndex];
                Camera assigned = (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);
                cameraMappingState.Add(
                    new Dictionary<string, object>
                    {
                        { "monitor", monitorIndex + 1 },
                        { "left", monitor.left },
                        { "top", monitor.top },
                        { "camera", assigned == null ? "None" : assigned.name },
                    }
                );
            }

            Dictionary<string, object> taskState =
                components.Task == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "require_interaction", components.Task.requireInteraction },
                        { "require_wait", components.Task.requireWait },
                        { "track_length", components.Task.trackLength },
                        { "track_seed", components.Task.trackSeed },
                        { "actor", components.Task.actor == null ? null : components.Task.actor.gameObject.name },
                        { "config_path", components.Task.configPath },
                    };

            List<string> modelOptions = new List<string>(GetValidActorModels());

            List<string> controllerOptions = new List<string> { "None" };
            controllerOptions.AddRange(components.Controllers.Select(controller => controller.gameObject.name));

            List<string> cameraOptions = new List<string> { "None" };
            cameraOptions.AddRange(components.DisplayCameras.Select(camera => camera.name));

            return new Dictionary<string, object>
            {
                {
                    "state",
                    new Dictionary<string, object>
                    {
                        { "actor", actorState },
                        { "mqtt", mqttState },
                        { "display", displayState },
                        { "camera_mapping", cameraMappingState },
                        { "task", taskState },
                    }
                },
                {
                    "options",
                    new Dictionary<string, object>
                    {
                        {
                            "actor",
                            new Dictionary<string, object>
                            {
                                { "model", modelOptions },
                                { "controller", controllerOptions },
                            }
                        },
                        {
                            "camera_mapping",
                            new Dictionary<string, object> { { "camera", cameraOptions } }
                        },
                    }
                },
                {
                    "visibility",
                    new Dictionary<string, object>
                    {
                        {
                            "task",
                            new Dictionary<string, object>
                            {
                                { "require_interaction", components.HasInteractionZone },
                                { "require_wait", components.HasOccupancyZone },
                            }
                        },
                    }
                },
            };
        }

        /// <summary>Runs every write_task_parameters check against the request without mutating the scene.</summary>
        /// <remarks>
        /// The sections apply in sequence and have no rollback, so a check firing mid-apply would leave the scene
        /// partly written, the persisted EditorPrefs and companion-asset writes committed, and the response still
        /// reporting failure. Validating the whole request first is what makes the tool atomic.
        /// </remarks>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <returns>An error message when any section is invalid, otherwise null.</returns>
        private static string ValidateTaskParameterWrites(
            Dictionary<string, object> arguments,
            SceneComponents components
        )
        {
            if (TryGetSection(arguments, "actor", out Dictionary<string, object> actorArgs) && components.Actor != null)
            {
                if (actorArgs.TryGetValue("model", out object modelObject) && modelObject is string newModel)
                {
                    string[] validModels = GetValidActorModels();
                    if (!validModels.Contains(newModel))
                    {
                        return $"Invalid model '{newModel}'. Valid: {string.Join(", ", validModels)}";
                    }
                }

                if (
                    actorArgs.TryGetValue("controller", out object controllerObject)
                    && controllerObject is string newController
                    && !string.Equals(newController, "None", StringComparison.Ordinal)
                    && components.Controllers.All(controller => controller.gameObject.name != newController)
                )
                {
                    string controllerNames = string.Join(
                        ", ",
                        components.Controllers.Select(controller => controller.gameObject.name)
                    );
                    return $"Invalid controller '{newController}'. Valid: None, {controllerNames}";
                }
            }

            if (
                TryGetSection(arguments, "mqtt", out Dictionary<string, object> mqttArgs)
                && components.Client != null
                && mqttArgs.TryGetValue("port", out object portObject)
                && !IsBrokerPortInRange(portObject)
            )
            {
                return $"Invalid port '{portObject}'. Must be a whole number between {MinimumBrokerPort} and "
                    + $"{MaximumBrokerPort}.";
            }

            if (
                TryGetSection(arguments, "display", out Dictionary<string, object> displayArgs)
                && components.Display != null
            )
            {
                string displayError =
                    ValidateFiniteFloat(displayArgs, "current_brightness")
                    ?? ValidateFiniteFloat(displayArgs, "brightness")
                    ?? ValidateFiniteFloat(displayArgs, "height_in_vr");
                if (displayError != null)
                {
                    return displayError;
                }
            }

            string cameraMappingError = ValidateCameraMappingWrites(arguments, components);
            if (cameraMappingError != null)
            {
                return cameraMappingError;
            }

            return ValidateTaskSectionWrites(arguments, components);
        }

        /// <summary>Checks the camera_mapping rows against the detected monitors and assignable cameras.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <returns>An error message when a row is invalid, otherwise null.</returns>
        private static string ValidateCameraMappingWrites(
            Dictionary<string, object> arguments,
            SceneComponents components
        )
        {
            if (
                !arguments.TryGetValue("camera_mapping", out object cameraMappingObject)
                || cameraMappingObject is not List<object> cameraMappingList
            )
            {
                return null;
            }

            // Writing an assignment list built from zero detected monitors would clear the scene's persisted
            // mapping, so a host whose monitor enumeration failed refuses the write rather than erasing it.
            if (components.FullScreenManager.monitors.Count == 0)
            {
                return "Cannot write camera_mapping: no monitors were detected on this host. Run refresh_monitors "
                    + "after resolving monitor enumeration, because writing now would clear the saved assignments.";
            }

            foreach (object row in cameraMappingList)
            {
                if (row is not Dictionary<string, object> rowDictionary)
                {
                    continue;
                }
                if (!rowDictionary.TryGetValue("monitor", out object monitorObject))
                {
                    return "Invalid camera_mapping row. Every row must carry a 'monitor' key holding the one-based "
                        + "number of the monitor it assigns, but this row carries none.";
                }
                if (!TryConvertInt(monitorObject, out int monitorNumber))
                {
                    return $"Invalid monitor '{monitorObject}'. Must be a whole number.";
                }

                int monitorIndex = monitorNumber - 1;
                if (monitorIndex < 0 || monitorIndex >= components.FullScreenManager.monitors.Count)
                {
                    return $"Invalid monitor index {monitorNumber}; scene has "
                        + $"{components.FullScreenManager.monitors.Count} monitors.";
                }

                if (
                    !rowDictionary.TryGetValue("camera", out object cameraObject)
                    || cameraObject is not string cameraName
                )
                {
                    continue;
                }
                if (
                    !string.Equals(cameraName, "None", StringComparison.Ordinal)
                    && components.DisplayCameras.All(camera => camera.name != cameraName)
                )
                {
                    return $"Invalid camera '{cameraName}' for monitor {monitorNumber}. Valid: None, "
                        + string.Join(", ", components.DisplayCameras.Select(camera => camera.name));
                }
            }

            return null;
        }

        /// <summary>Checks the task subsection against the zones present and the runtime's accepted ranges.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <returns>An error message when a field is invalid, otherwise null.</returns>
        private static string ValidateTaskSectionWrites(
            Dictionary<string, object> arguments,
            SceneComponents components
        )
        {
            if (!TryGetSection(arguments, "task", out Dictionary<string, object> taskArgs) || components.Task == null)
            {
                return null;
            }

            if (taskArgs.ContainsKey("require_interaction") && !components.HasInteractionZone)
            {
                return "Cannot set require_interaction: the active scene has no GuidanceZone, so the control is "
                    + "hidden in the Parameters window and the flag has no runtime effect.";
            }
            if (taskArgs.ContainsKey("require_wait") && !components.HasOccupancyZone)
            {
                return "Cannot set require_wait: the active scene has no OccupancyZone, so the control is "
                    + "hidden in the Parameters window and the flag has no runtime effect.";
            }

            string toggleError =
                ValidateBoolean(taskArgs, "require_interaction") ?? ValidateBoolean(taskArgs, "require_wait");
            if (toggleError != null)
            {
                return toggleError;
            }

            if (taskArgs.TryGetValue("track_length", out object trackLengthObject))
            {
                if (!TryConvertSingle(trackLengthObject, out float newTrackLength) || newTrackLength <= 0f)
                {
                    return $"Invalid track_length '{trackLengthObject}'. Must be a positive, finite number of "
                        + "Unity units long enough to fill one corridor.";
                }
            }

            if (
                taskArgs.TryGetValue("track_seed", out object trackSeedObject) && !TryConvertInt(trackSeedObject, out _)
            )
            {
                return $"Invalid track_seed '{trackSeedObject}'. Must be a whole number.";
            }

            return null;
        }

        /// <summary>Determines whether a port value converts to a number a broker can be reached on.</summary>
        /// <remarks>
        /// Bounding the write matters because the value reaches both the scene's client and the EditorPrefs entry the
        /// client reloads from, so a port outside the range would survive the session that wrote it.
        /// </remarks>
        /// <param name="value">The boxed value from the request payload.</param>
        /// <returns>True when the value converts to an integer inside the accepted broker port range.</returns>
        private static bool IsBrokerPortInRange(object value)
        {
            return TryConvertInt(value, out int port) && port >= MinimumBrokerPort && port <= MaximumBrokerPort;
        }

        /// <summary>Checks that an optional toggle field converts to a boolean.</summary>
        /// <param name="section">The section dictionary holding the field.</param>
        /// <param name="key">The field name to check.</param>
        /// <returns>An error message when the field is present and unconvertible, otherwise null.</returns>
        private static string ValidateBoolean(Dictionary<string, object> section, string key)
        {
            if (!section.TryGetValue(key, out object value))
            {
                return null;
            }

            try
            {
                Convert.ToBoolean(value);
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidCastException)
            {
                return $"Invalid {key} '{value}'. Must be true or false.";
            }

            return null;
        }

        /// <summary>Checks that an optional numeric field converts to a finite single-precision value.</summary>
        /// <param name="section">The section dictionary holding the field.</param>
        /// <param name="key">The field name to check.</param>
        /// <returns>An error message when the field is present and unconvertible, otherwise null.</returns>
        private static string ValidateFiniteFloat(Dictionary<string, object> section, string key)
        {
            if (section.TryGetValue(key, out object value) && !TryConvertSingle(value, out _))
            {
                return $"Invalid {key} '{value}'. Must be a finite number.";
            }
            return null;
        }

        /// <summary>Converts a boxed argument value to a finite single-precision number.</summary>
        /// <param name="value">The boxed value from the request payload.</param>
        /// <param name="converted">The converted value on success, otherwise zero.</param>
        /// <returns>True when the value converts to a finite float.</returns>
        private static bool TryConvertSingle(object value, out float converted)
        {
            converted = 0f;
            try
            {
                converted = Convert.ToSingle(value);
            }
            catch (Exception exception)
                when (exception is FormatException
                    || exception is InvalidCastException
                    || exception is OverflowException
                )
            {
                return false;
            }
            return float.IsFinite(converted);
        }

        /// <summary>Converts a boxed argument value to a 32-bit signed integer.</summary>
        /// <param name="value">The boxed value from the request payload.</param>
        /// <param name="converted">The converted value on success, otherwise zero.</param>
        /// <returns>True when the value converts to an integer.</returns>
        private static bool TryConvertInt(object value, out int converted)
        {
            converted = 0;
            try
            {
                converted = Convert.ToInt32(value);
            }
            catch (Exception exception)
                when (exception is FormatException
                    || exception is InvalidCastException
                    || exception is OverflowException
                )
            {
                return false;
            }
            return true;
        }

        /// <summary>Applies any "actor" subsection from <paramref name="arguments"/>.</summary>
        /// <remarks>Runs only after <see cref="ValidateTaskParameterWrites"/> accepted every value it reads.</remarks>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <param name="dirty">Set to true when this section writes to the scene.</param>
        private static void ApplyActorSection(
            Dictionary<string, object> arguments,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (
                !TryGetSection(arguments, "actor", out Dictionary<string, object> actorArgs)
                || components.Actor == null
            )
            {
                return;
            }
            if (actorArgs.TryGetValue("model", out object modelObject) && modelObject is string newModel)
            {
                components.Actor.SetModel(newModel);
                dirty = true;
            }
            if (
                actorArgs.TryGetValue("controller", out object controllerObject)
                && controllerObject is string newController
            )
            {
                components.Actor.Controller = string.Equals(newController, "None", StringComparison.Ordinal)
                    ? null
                    : components.Controllers.First(controller => controller.gameObject.name == newController);
                dirty = true;
            }
        }

        /// <summary>Applies any "mqtt" subsection from <paramref name="arguments"/>.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <param name="dirty">Set to true when this section writes to the scene.</param>
        private static void ApplyMqttSection(
            Dictionary<string, object> arguments,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (!TryGetSection(arguments, "mqtt", out Dictionary<string, object> mqttArgs) || components.Client == null)
            {
                return;
            }
            if (mqttArgs.TryGetValue("ip", out object ipObject) && ipObject is string newIp)
            {
                components.Client.ipAddress = newIp;
                EditorPrefs.SetString("SollertiaVR_MQTT_IP", newIp);
                dirty = true;
            }
            if (mqttArgs.TryGetValue("port", out object portObject))
            {
                int newPort = Convert.ToInt32(portObject);
                components.Client.port = newPort;
                EditorPrefs.SetInt("SollertiaVR_MQTT_Port", newPort);
                dirty = true;
            }
        }

        /// <summary>Applies any "display" subsection from <paramref name="arguments"/>.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <param name="dirty">Set to true when this section writes to the scene.</param>
        private static void ApplyDisplaySection(
            Dictionary<string, object> arguments,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (
                !TryGetSection(arguments, "display", out Dictionary<string, object> displayArgs)
                || components.Display == null
            )
            {
                return;
            }
            if (displayArgs.TryGetValue("current_brightness", out object currentBrightnessObject))
            {
                components.Display.currentBrightness = Convert.ToSingle(currentBrightnessObject);
                dirty = true;
            }
            if (components.Display.settings != null)
            {
                if (displayArgs.TryGetValue("brightness", out object brightnessObject))
                {
                    components.Display.settings.brightness = Convert.ToSingle(brightnessObject);
                    EditorUtility.SetDirty(components.Display.settings);
                    dirty = true;
                }
                if (displayArgs.TryGetValue("height_in_vr", out object heightObject))
                {
                    components.Display.settings.heightInVR = Convert.ToSingle(heightObject);
                    components.Display.transform.localPosition = new Vector3(
                        0,
                        components.Display.settings.heightInVR,
                        0
                    );
                    EditorUtility.SetDirty(components.Display.settings);
                    dirty = true;
                }
            }
        }

        /// <summary>Applies any "camera_mapping" subsection from <paramref name="arguments"/>.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <param name="dirty">Set to true when this section writes to the scene.</param>
        private static void ApplyCameraMappingSection(
            Dictionary<string, object> arguments,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (
                !arguments.TryGetValue("camera_mapping", out object cameraMappingObject)
                || cameraMappingObject is not List<object> cameraMappingList
            )
            {
                return;
            }

            FullScreenViewManager fullScreenManager = components.FullScreenManager;
            bool assigned = false;
            foreach (object row in cameraMappingList)
            {
                if (row is not Dictionary<string, object> rowDictionary)
                {
                    continue;
                }
                if (!rowDictionary.TryGetValue("monitor", out object monitorObject))
                {
                    continue;
                }
                if (
                    !rowDictionary.TryGetValue("camera", out object cameraObject)
                    || cameraObject is not string cameraName
                )
                {
                    continue;
                }

                int monitorIndex = Convert.ToInt32(monitorObject) - 1;
                fullScreenManager.monitors[monitorIndex].cameraEntityId = string.Equals(
                    cameraName,
                    "None",
                    StringComparison.Ordinal
                )
                    ? EntityId.None
                    : components.DisplayCameras.First(camera => camera.name == cameraName).GetEntityId();
                assigned = true;
            }

            // A list whose every row was skipped assigns nothing, so persisting it would rewrite the per-scene
            // companion asset and dirty the scene on behalf of a request that changed no assignment.
            if (!assigned)
            {
                return;
            }

            fullScreenManager.SaveCameras();
            dirty = true;
        }

        /// <summary>Applies any "task" subsection from <paramref name="arguments"/>.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <param name="dirty">Set to true when this section writes to the scene.</param>
        private static void ApplyTaskSection(
            Dictionary<string, object> arguments,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (!TryGetSection(arguments, "task", out Dictionary<string, object> taskArgs) || components.Task == null)
            {
                return;
            }

            Undo.RecordObject(components.Task, "Write Task Parameters");
            if (taskArgs.TryGetValue("require_interaction", out object requireInteractionObject))
            {
                components.Task.requireInteraction = Convert.ToBoolean(requireInteractionObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("require_wait", out object requireWaitObject))
            {
                components.Task.requireWait = Convert.ToBoolean(requireWaitObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("track_length", out object trackLengthObject))
            {
                components.Task.trackLength = Convert.ToSingle(trackLengthObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("track_seed", out object trackSeedObject))
            {
                components.Task.trackSeed = Convert.ToInt32(trackSeedObject);
                dirty = true;
            }
            EditorUtility.SetDirty(components.Task);
        }

        /// <summary>Extracts a sub-dictionary at the given key, returning false when it is absent.</summary>
        /// <param name="arguments">The dispatched tool arguments.</param>
        /// <param name="key">The top-level section key to look up.</param>
        /// <param name="section">The extracted sub-dictionary when present, otherwise null.</param>
        /// <returns>True when the section was found and is a dictionary.</returns>
        private static bool TryGetSection(
            Dictionary<string, object> arguments,
            string key,
            out Dictionary<string, object> section
        )
        {
            section = null;
            if (arguments.TryGetValue(key, out object value) && value is Dictionary<string, object> dictionary)
            {
                section = dictionary;
                return true;
            }
            return false;
        }

        /// <summary>Reuses the open Parameters window's FullScreenViewManager, else the cached per-scene one.</summary>
        /// <remarks>
        /// Sharing the instance keeps the open Parameters tab in sync with API writes without an explicit
        /// reload round-trip. Falling back to <see cref="_cachedFullScreenManager"/> (with cameras loaded from the
        /// saved asset) lets the bridge serve scenes where the window is currently closed, at one construction per
        /// scene rather than one per request. Uses <see cref="Resources.FindObjectsOfTypeAll{T}()"/> to locate an
        /// existing window instance without creating a new one as a side effect. The constructor already calls
        /// <see cref="FullScreenViewManager.LoadCameras"/>, so no second load runs here.
        /// </remarks>
        /// <returns>A FullScreenViewManager whose monitor list reflects the current scene state.</returns>
        private static FullScreenViewManager AcquireFullScreenManager()
        {
            MainWindow window = Resources.FindObjectsOfTypeAll<MainWindow>().FirstOrDefault();
            if (window != null && window.fullScreenManager != null)
            {
                return window.fullScreenManager;
            }
            return _cachedFullScreenManager ??= new FullScreenViewManager();
        }

        /// <summary>Determines whether the given asset path is permitted for deletion.</summary>
        /// <param name="assetPath">The project-relative asset path to check.</param>
        /// <returns>True when the path lies under an allowed prefix and is not in the protected set.</returns>
        private static bool IsDeleteAllowed(string assetPath)
        {
            // Rejects path traversal sequences, absolute paths, and directory targets to bound the blast radius.
            if (
                assetPath.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(assetPath)
                || assetPath.EndsWith("/", StringComparison.Ordinal)
            )
            {
                return false;
            }

            if (DeleteProtectedPaths.Contains(assetPath))
            {
                return false;
            }

            // Every allowed prefix ends in a separator, so the only path a prefix match can equal is the directory
            // itself, which the trailing-separator rejection above already refuses. A match here therefore always
            // names an asset inside an allowed root.
            foreach (string prefix in DeleteAllowedPrefixes)
            {
                if (assetPath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Resolves how to handle unsaved changes in the active scene before switching scenes.</summary>
        /// <param name="unsavedChanges">
        /// The handling policy: "save" persists the active scene, "discard" abandons unsaved edits, and an
        /// empty string leaves the policy unspecified so the caller can prompt the user.
        /// </param>
        /// <returns>
        /// An error message when the active scene is dirty and no policy was provided, otherwise null.
        /// </returns>
        private static string HandleUnsavedChanges(string unsavedChanges)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.isDirty)
            {
                return null;
            }

            if (string.Equals(unsavedChanges, "save", StringComparison.Ordinal))
            {
                EditorSceneManager.SaveOpenScenes();
                return null;
            }

            if (string.Equals(unsavedChanges, "discard", StringComparison.Ordinal))
            {
                return null;
            }

            string message =
                $"Active scene '{activeScene.path}' has unsaved changes. Specify unsaved_changes='save' to "
                + "persist the current scene before switching, or unsaved_changes='discard' to abandon the "
                + "edits. Ask the user which behavior they prefer before retrying.";
            return message;
        }

        /// <summary>Recursively inspects a GameObject and returns its hierarchy as a dictionary.</summary>
        /// <param name="gameObject">The subtree root, descended into depth-first.</param>
        /// <returns>A dictionary describing the GameObject's transform, components, and children.</returns>
        private static Dictionary<string, object> InspectGameObject(GameObject gameObject)
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "name", gameObject.name },
                { "active_self", gameObject.activeSelf },
                { "position", FormatVector3(gameObject.transform.localPosition) },
                { "rotation", FormatVector3(gameObject.transform.localEulerAngles) },
                { "scale", FormatVector3(gameObject.transform.localScale) },
            };

            Component[] components = gameObject.GetComponents<Component>();
            List<string> componentNames = components
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToList();
            result["components"] = componentNames;
            // Carries the per-component enabled flag beside the existing type-name list rather than replacing it,
            // because a disabled Task or a disabled boundary MeshRenderer is the symptom behind most runtime
            // bailouts and the name list alone cannot express it.
            result["component_states"] = components
                .Where(component => component != null)
                .Select(component => new Dictionary<string, object>
                {
                    { "type", component.GetType().Name },
                    { "enabled", GetComponentEnabled(component) },
                })
                .ToList();

            BoxCollider collider = gameObject.GetComponent<BoxCollider>();
            if (collider != null)
            {
                result["collider_center"] = FormatVector3(collider.center);
                result["collider_size"] = FormatVector3(collider.size);
                result["collider_is_trigger"] = collider.isTrigger;
            }

            List<Dictionary<string, object>> children = new List<Dictionary<string, object>>();
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                children.Add(InspectGameObject(gameObject.transform.GetChild(i).gameObject));
            }

            if (children.Count > 0)
            {
                result["children"] = children;
            }

            return result;
        }

        /// <summary>Returns the enabled flag of a component, or null when its type carries no such flag.</summary>
        /// <remarks>
        /// Unity spreads the flag across three unrelated base types instead of declaring it on Component, so the
        /// three are matched separately. A Transform or a MeshFilter matches none of them and reports null,
        /// which distinguishes "cannot be disabled" from "is disabled" at the call site.
        /// </remarks>
        /// <param name="component">The component whose enabled state is read.</param>
        /// <returns>The boxed flag, or null for a component type that cannot be disabled.</returns>
        private static object GetComponentEnabled(Component component)
        {
            return component switch
            {
                Behaviour behaviour => (object)behaviour.enabled,
                Collider collider => collider.enabled,
                Renderer renderer => renderer.enabled,
                _ => null,
            };
        }

        /// <summary>Formats a Vector3 as a serializable dictionary.</summary>
        /// <param name="vector">The value whose components become the dictionary entries.</param>
        /// <returns>A dictionary with x, y, and z keys.</returns>
        private static Dictionary<string, float> FormatVector3(Vector3 vector)
        {
            return new Dictionary<string, float>
            {
                { "x", vector.x },
                { "y", vector.y },
                { "z", vector.z },
            };
        }

        /// <summary>Retrieves a string value from the arguments dictionary with an optional default.</summary>
        /// <param name="arguments">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="defaultValue">The default value if the key is not found.</param>
        /// <returns>The string value, or the default if not found.</returns>
        private static string GetString(Dictionary<string, object> arguments, string key, string defaultValue = null)
        {
            if (arguments.ContainsKey(key) && arguments[key] != null)
            {
                return arguments[key].ToString();
            }

            return defaultValue;
        }

        /// <summary>Constructs a success JSON response.</summary>
        /// <param name="payload">The response payload dictionary.</param>
        /// <returns>A JSON string with success set to true.</returns>
        private static string Ok(Dictionary<string, object> payload)
        {
            payload["success"] = true;
            return MiniJson.Serialize(payload);
        }

        /// <summary>Constructs an error JSON response.</summary>
        /// <param name="message">The error message.</param>
        /// <returns>A JSON string with success set to false and the error message.</returns>
        private static string Error(string message)
        {
            return MiniJson.Serialize(new Dictionary<string, object> { { "success", false }, { "error", message } });
        }

        /// <summary>
        /// Aggregates the per-scene component references read by both <see cref="ReadTaskParameters"/> and
        /// <see cref="WriteTaskParameters"/>. Built once per request via <see cref="AcquireSceneComponents"/>
        /// so each tool invocation walks the scene exactly once, regardless of how many sections the writer
        /// touches.
        /// </summary>
        private struct SceneComponents
        {
            /// <summary>The active scene's actor, or null when absent.</summary>
            public ActorObject Actor;

            /// <summary>The active scene's display, or null when absent.</summary>
            public DisplayObject Display;

            /// <summary>The active scene's Task component, or null when absent.</summary>
            public Task Task;

            /// <summary>The active scene's MQTT client singleton, or null when absent.</summary>
            public MQTTClient Client;

            /// <summary>Every <see cref="ControllerOutput"/> in the active scene.</summary>
            public ControllerOutput[] Controllers;

            /// <summary>Every assignable display camera (MainCamera excluded) in the active scene.</summary>
            public Camera[] DisplayCameras;

            /// <summary>Determines whether the scene contains at least one <see cref="GuidanceZone"/>.</summary>
            public bool HasInteractionZone;

            /// <summary>Determines whether the scene contains at least one <see cref="OccupancyZone"/>.</summary>
            public bool HasOccupancyZone;

            /// <summary>
            /// The shared <see cref="FullScreenViewManager"/> used by camera-mapping reads and writes.
            /// </summary>
            public FullScreenViewManager FullScreenManager;
        }
    }
}
