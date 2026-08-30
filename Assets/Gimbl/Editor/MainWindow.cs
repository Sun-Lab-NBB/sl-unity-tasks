/// <summary>
/// Provides the MainWindow class for the consolidated Task Parameters editor window.
///
/// Renders the single editor window that hosts every per-scene configuration surface for Gimbl: Task, Actor, Display,
/// Camera Mapping, and MQTT.
/// </summary>
using System.Linq;
using SL.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gimbl
{
    /// <summary>Manages the consolidated Gimbl Task Parameters editor window.</summary>
    public class MainWindow : EditorWindow
    {
        /// <summary>
        /// Pre-resolved per-controller specs. The table resolves each <see cref="ControllerTypes"/> member's display
        /// name and concrete subclass once at type initialization.
        /// </summary>
        private static readonly (string DisplayName, System.Type ControllerType)[] CachedControllerSpecs =
            BuildControllerSpecs();

        /// <summary>The full-screen view manager for camera mapping.</summary>
        public FullScreenViewManager fullScreenManager;

        /// <summary>The scroll position for the window content.</summary>
        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>The MQTT client reference for configuration.</summary>
        private MQTTClient _client;

        /// <summary>Determines whether a scene change is pending after exiting play mode.</summary>
        private bool _exitPlayModeSceneChangeComing = false;

        /// <summary>The cached <see cref="Task"/> reference for the active scene, null when missing or stale.</summary>
        private Task _cachedTask;

        /// <summary>The cached <see cref="ActorObject"/> reference for the active scene.</summary>
        private ActorObject _cachedActor;

        /// <summary>The cached <see cref="DisplayObject"/> reference for the active scene.</summary>
        private DisplayObject _cachedDisplay;

        /// <summary>The cached <see cref="GuidanceZone"/> reference for the active scene.</summary>
        private GuidanceZone _cachedGuidanceZone;

        /// <summary>The cached <see cref="OccupancyZone"/> reference for the active scene.</summary>
        private OccupancyZone _cachedOccupancyZone;

        /// <summary>Initializes the scene, full-screen view manager, scene change and play mode handlers.</summary>
        private void OnEnable()
        {
            TagsAndLayers.AddTag("VRDisplay");
            fullScreenManager = new FullScreenViewManager();
            InitializeScene();
            InvalidateSceneCache();

            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Removes the scene change and play mode handlers when disabled.</summary>
        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>Renders every configuration section in order.</summary>
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(position.height),
                GUILayout.Width(position.width)
            );

            DrawActorSection();
            DrawMQTTSection();
            DrawDisplaySection();
            DrawCameraMappingSection();
            DrawTaskSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Shows the Task Parameters editor window.</summary>
        /// <remarks>
        /// The window is docked next to <c>UnityEditor.InspectorWindow</c>, resolved by assembly-qualified type name
        /// to avoid a hard reference to a private Unity type.
        /// </remarks>
        [MenuItem("Window/Task Parameters")]
        public static void ShowWindow()
        {
            OpenOrFocusWindow(focus: true);
        }

        /// <summary>
        /// Ensures the active scene contains one controller GameObject per supported ControllerTypes.
        /// </summary>
        /// <remarks>
        /// The created GameObject is named after the controller's display name, and
        /// <see cref="ControllerObject.InitiateController"/> reparents it under the scene's "Controllers" root and
        /// registers it for undo. Each created controller additionally receives a <see cref="ControllerOutput"/>
        /// whose master points at the controller. The Actor.Controller assignment is left untouched so user-chosen
        /// swaps survive auto-create.
        /// </remarks>
        public static void EnsureControllers()
        {
            GameObject controllersRoot = GameObject.Find("Controllers");
            if (controllersRoot == null)
            {
                return;
            }

            ControllerObject[] existingControllers = FindObjectsByType<ControllerObject>(FindObjectsSortMode.None);
            bool createdAny = false;

            foreach ((string displayName, System.Type controllerType) in CachedControllerSpecs)
            {
                if (controllerType == null)
                {
                    continue;
                }
                if (existingControllers.Any(existing => existing.GetType() == controllerType))
                {
                    continue;
                }

                GameObject controllerGameObject = new GameObject(displayName);
                ControllerObject controller = (ControllerObject)controllerGameObject.AddComponent(controllerType);
                controller.InitiateController();
                ControllerOutput output = controllerGameObject.AddComponent<ControllerOutput>();
                output.master = controller;
                createdAny = true;
            }

            if (createdAny)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        /// <summary>
        /// Applies the project-wide MQTT broker defaults (IP and port) to the active scene's <see cref="MQTTClient"/>
        /// component, reading from <c>EditorPrefs</c> with the standard loopback fallback. Idempotent.
        /// </summary>
        public static void EnsureMqttDefaults()
        {
            GameObject mqttClientObject = GameObject.Find("MQTT Client");
            if (mqttClientObject == null)
            {
                return;
            }
            if (!mqttClientObject.TryGetComponent(out MQTTClient client))
            {
                return;
            }

            string previousIpAddress = client.ipAddress;
            int previousPort = client.port;

            client.ipAddress = EditorPrefs.GetString("SollertiaVR_MQTT_IP");
            if (string.IsNullOrEmpty(client.ipAddress))
            {
                client.ipAddress = "127.0.0.1";
            }
            client.port = EditorPrefs.GetInt("SollertiaVR_MQTT_Port");
            if (client.port == 0)
            {
                client.port = 1883;
            }

            bool brokerChanged =
                !string.Equals(client.ipAddress, previousIpAddress, System.StringComparison.Ordinal)
                || client.port != previousPort;
            if (brokerChanged)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        /// <summary>
        /// Synchronizes the active scene's <see cref="DisplayObject.currentBrightness"/> with its referenced
        /// <see cref="DisplaySettings.brightness"/> asset value, so a fresh scene's runtime override matches the
        /// persisted asset default.
        /// </summary>
        public static void SyncDisplayBrightnessToSettings()
        {
            DisplayObject display = FindAnyObjectByType<DisplayObject>();
            if (display == null || display.settings == null)
            {
                return;
            }
            if (Mathf.Approximately(display.currentBrightness, display.settings.brightness))
            {
                return;
            }
            display.currentBrightness = display.settings.brightness;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        /// <summary>Removes every default Unity "Main Camera" GameObject from the active scene.</summary>
        /// <remarks>
        /// The auto-created Display owns the per-monitor cameras (via PerspectiveProjection) and the Actor owns the
        /// third-person tracking camera, so the Unity-default "Main Camera" left over by the new scene template
        /// renders nothing useful while still consuming display slot 0.
        /// </remarks>
        public static void RemoveDefaultMainCamera()
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cameras.Length == 0)
            {
                return;
            }
            bool anyRemoved = false;
            foreach (Camera camera in cameras)
            {
                // Destroying a parent takes its children with it, so a camera nested under an earlier match is
                // already gone by the time the loop reaches its entry in this snapshot.
                if (camera == null)
                {
                    continue;
                }
                if (
                    camera.gameObject.CompareTag("MainCamera")
                    || string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.Ordinal)
                )
                {
                    DestroyImmediate(camera.gameObject);
                    anyRemoved = true;
                }
            }
            if (anyRemoved)
            {
                Debug.Log("Removed default Main Camera (unused; Display cameras handle monitor rendering).");
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        /// <summary>Registers auto-open hooks that keep the Parameters window available across sessions.</summary>
        /// <remarks>
        /// Subscribes to scene-open and Play-Mode-enter events so closing the window does not strand the
        /// user without access to the per-scene configuration surface. Also defers a one-shot open via
        /// <see cref="EditorApplication.delayCall"/> so the window appears immediately after a script
        /// reload or editor start, once the editor finishes initializing. A batch-mode editor hosts no GUI, and
        /// opening the window there runs <see cref="InitializeScene"/> against the throwaway startup scene, whose
        /// unsaved changes then block the run on a save dialog batch mode cancels, so the hooks stay unregistered.
        /// </remarks>
        [InitializeOnLoadMethod]
        private static void RegisterAutoOpen()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            EditorSceneManager.sceneOpened += (Scene scene, OpenSceneMode mode) => EnsureWindowOpen();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    EnsureWindowOpen();
                }
            };
            EditorApplication.delayCall += EnsureWindowOpen;
        }

        /// <summary>Opens the Parameters window without stealing focus when no instance is currently open.</summary>
        private static void EnsureWindowOpen()
        {
            if (HasOpenInstances<MainWindow>())
            {
                return;
            }
            OpenOrFocusWindow(focus: false);
        }

        /// <summary>Creates or surfaces the Parameters window and pins its title to the short label.</summary>
        /// <param name="focus">Determines whether the window should take input focus on open.</param>
        private static void OpenOrFocusWindow(bool focus)
        {
            System.Type inspectorType = System.Type.GetType("UnityEditor.InspectorWindow,UnityEditor.dll");
            MainWindow window = GetWindow<MainWindow>("Parameters", focus: focus, new System.Type[] { inspectorType });
            window.titleContent = new GUIContent("Parameters");
        }

        /// <summary>
        /// Invalidates the scene-component cache on every active-scene change, and reloads camera assignments
        /// unless the change is the deferred swap from exiting Play Mode.
        /// </summary>
        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            InvalidateSceneCache();
            if (_exitPlayModeSceneChangeComing)
            {
                _exitPlayModeSceneChangeComing = false;
            }
            else
            {
                fullScreenManager.LoadCameras();
            }
        }

        /// <summary>Handles play mode transitions for full-screen view management.</summary>
        /// <param name="state">
        /// The transition the editor is entering, which selects between showing the full-screen views and arming the
        /// deferred scene swap.
        /// </param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                fullScreenManager.ShowFullScreenViews(closeOldViews: false);
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _exitPlayModeSceneChangeComing = true;
            }
        }

        /// <summary>Renders the Actor section that exposes the active scene's Actor properties.</summary>
        private void DrawActorSection()
        {
            DrawSection(
                "Actor",
                () =>
                {
                    ActorObject actor = GetCachedActor();
                    if (actor == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No Actor in the active scene. Close and reopen this window to auto-create one.",
                            MessageType.Info
                        );
                    }
                    else
                    {
                        actor.EditMenu();
                    }
                }
            );
        }

        /// <summary>Renders the MQTT section with broker IP/port and connection test.</summary>
        private void DrawMQTTSection()
        {
            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            DrawSection(
                "MQTT",
                () =>
                {
                    MQTTClient client = GetCachedClient();
                    if (client == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No MQTT Client in the active scene. Close and reopen this window to auto-create one.",
                            MessageType.Info
                        );
                        return;
                    }

                    client.ipAddress = EditorGUILayout.TextField(
                        new GUIContent(
                            "ip: ",
                            "IP address of the MQTT broker that bridges this Unity scene to the experiment hardware."
                        ),
                        client.ipAddress,
                        LayoutSettings.EditFieldOption
                    );

                    string portText = EditorGUILayout.TextField(
                        new GUIContent(
                            "port: ",
                            "TCP port of the MQTT broker that bridges this Unity scene to the experiment hardware."
                        ),
                        client.port.ToString(),
                        LayoutSettings.EditFieldOption
                    );
                    if (int.TryParse(portText, out int parsedPort))
                    {
                        client.port = parsedPort;
                    }

                    if (GUI.changed)
                    {
                        EditorPrefs.SetString("SollertiaVR_MQTT_IP", client.ipAddress);
                        EditorPrefs.SetInt("SollertiaVR_MQTT_Port", client.port);
                    }
                    if (
                        GUILayout.Button(
                            new GUIContent(
                                "Test Connection",
                                "Check whether the MQTT broker is reachable at the specified ip and port."
                            )
                        )
                    )
                    {
                        client.Connect(verbose: true);
                        client.Disconnect();
                    }
                }
            );
            GUI.enabled = true;
        }

        /// <summary>Renders the Display section for brightness and height of the active scene's Display.</summary>
        private void DrawDisplaySection()
        {
            DrawSection(
                "Display",
                () =>
                {
                    DisplayObject display = GetCachedDisplay();
                    if (display == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No Display in the active scene. Close and reopen this window to auto-create one.",
                            MessageType.Info
                        );
                        return;
                    }

                    GUIContent blankShowTooltip = new GUIContent(
                        "",
                        "Set brightness to 0 (Blank) or restore the configured brightness (Show)."
                    );
                    EditorGUILayout.BeginHorizontal();
                    if (display.currentBrightness > 0)
                    {
                        blankShowTooltip.text = "Blank Display";
                        if (GUILayout.Button(blankShowTooltip))
                        {
                            display.currentBrightness = 0;
                            EditorUtility.SetDirty(display);
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }
                    }
                    else
                    {
                        blankShowTooltip.text = "Show Display";
                        if (GUILayout.Button(blankShowTooltip))
                        {
                            display.currentBrightness = display.settings.brightness;
                            EditorUtility.SetDirty(display);
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    SerializedObject serializedSettings = new SerializedObject(display.settings);
                    float previousHeight = display.settings.heightInVR;
                    float previousBrightness = display.settings.brightness;
                    EditorGUILayout.PropertyField(
                        serializedSettings.FindProperty("brightness"),
                        includeChildren: true,
                        LayoutSettings.EditFieldOption
                    );
                    EditorGUILayout.PropertyField(
                        serializedSettings.FindProperty("heightInVR"),
                        includeChildren: true,
                        LayoutSettings.EditFieldOption
                    );
                    serializedSettings.ApplyModifiedProperties();
                    if (previousHeight != display.settings.heightInVR)
                    {
                        display.transform.localPosition = new Vector3(0, display.settings.heightInVR, 0);
                        EditorUtility.SetDirty(display);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                    if (previousBrightness != display.settings.brightness)
                    {
                        display.currentBrightness = display.settings.brightness;
                        EditorUtility.SetDirty(display);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                }
            );
        }

        /// <summary>Renders the Camera Mapping section that wires display cameras to physical monitors.</summary>
        private void DrawCameraMappingSection()
        {
            DrawSection(
                "Camera Mapping",
                () =>
                {
                    fullScreenManager.OnGUIRefreshMonitorPositions();
                    fullScreenManager.OnGUICameraObjectFields();
                    if (EditorApplication.isPlaying)
                    {
                        GUI.enabled = false;
                    }
                    fullScreenManager.OnGUIShowFullScreenViews();
                    GUI.enabled = true;
                }
            );
        }

        /// <summary>Renders the Task section that exposes per-scene Task settings.</summary>
        private void DrawTaskSection()
        {
            DrawSection(
                "Task",
                () =>
                {
                    Task task = GetCachedTask();
                    if (task == null)
                    {
                        EditorGUILayout.HelpBox("No Task component found in the current scene.", MessageType.Info);
                        return;
                    }

                    if (task.actor == null)
                    {
                        ActorObject resolvedActor = GetCachedActor();
                        if (resolvedActor != null)
                        {
                            task.actor = resolvedActor;
                            EditorUtility.SetDirty(task);
                        }
                    }

                    if (EditorApplication.isPlaying)
                    {
                        GUI.enabled = false;
                    }

                    bool hasInteractionZone = GetCachedGuidanceZone() != null;
                    bool hasOccupancyZone = GetCachedOccupancyZone() != null;

                    EditorGUI.BeginChangeCheck();
                    bool newRequireInteraction = task.requireInteraction;
                    if (hasInteractionZone)
                    {
                        newRequireInteraction = EditorGUILayout.Toggle(
                            new GUIContent(
                                "Require Interaction: ",
                                "When on, the animal must engage an interaction sensor inside the stimulus zone "
                                    + "to trigger the stimulus. When off, reaching the guidance zone automatically "
                                    + "triggers the stimulus. Addressable via MQTT at runtime."
                            ),
                            task.requireInteraction,
                            LayoutSettings.EditFieldOption
                        );
                    }
                    bool newRequireWait = task.requireWait;
                    if (hasOccupancyZone)
                    {
                        newRequireWait = EditorGUILayout.Toggle(
                            new GUIContent(
                                "Require Wait: ",
                                "When on, the animal must remain in the occupancy zone, and the zone's mode resolves "
                                    + "the trial by disarming, arming, or triggering the stimulus. When off, the VR "
                                    + "emits a warning to the experiment controller via MQTT, enabling it to interfere "
                                    + "by activating brakes. Addressable via MQTT at runtime."
                            ),
                            task.requireWait,
                            LayoutSettings.EditFieldOption
                        );
                    }
                    float newTrackLength = EditorGUILayout.FloatField(
                        new GUIContent(
                            "Track Length: ",
                            "Total length of the pre-generated random trial sequence in Unity units. "
                                + "Should overestimate the distance the animal will actually travel in a session."
                        ),
                        task.trackLength,
                        LayoutSettings.EditFieldOption
                    );
                    int newTrackSeed = EditorGUILayout.IntField(
                        new GUIContent(
                            "Track Seed: ",
                            "Seed for the random trial-sequence generator. A specific seed reproduces the same "
                                + "sequence; use -1 for a nondeterministic seed each run."
                        ),
                        task.trackSeed,
                        LayoutSettings.EditFieldOption
                    );

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(task, "Edit Task Settings");
                        task.requireInteraction = newRequireInteraction;
                        task.requireWait = newRequireWait;
                        task.trackLength = newTrackLength;
                        task.trackSeed = newTrackSeed;
                        EditorUtility.SetDirty(task);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }

                    GUI.enabled = true;
                }
            );
        }

        /// <summary>Renders a labelled vertical box surrounding the supplied body action.</summary>
        /// <remarks>
        /// Returning early from <paramref name="drawBody"/> still closes the box because the closing call lives in
        /// this helper.
        /// </remarks>
        /// <param name="title">The section heading shown above the body content.</param>
        /// <param name="drawBody">The action that emits the section's inner GUI controls.</param>
        private static void DrawSection(string title, System.Action drawBody)
        {
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle);
            EditorGUILayout.LabelField(title, LayoutSettings.SectionLabel);
            drawBody();
            EditorGUILayout.EndVertical();
        }

        /// <summary>Clears every per-scene cached component reference so the next access re-resolves.</summary>
        private void InvalidateSceneCache()
        {
            _client = null;
            _cachedTask = null;
            _cachedActor = null;
            _cachedDisplay = null;
            _cachedGuidanceZone = null;
            _cachedOccupancyZone = null;
        }

        /// <summary>
        /// Returns the cached <see cref="Task"/>, refreshing it from the scene on first access or after
        /// invalidation.
        /// </summary>
        private Task GetCachedTask()
        {
            if (_cachedTask == null)
            {
                _cachedTask = FindAnyObjectByType<Task>();
            }
            return _cachedTask;
        }

        /// <summary>Returns the cached <see cref="ActorObject"/>, refreshing it from the scene on demand.</summary>
        private ActorObject GetCachedActor()
        {
            if (_cachedActor == null)
            {
                _cachedActor = FindAnyObjectByType<ActorObject>();
            }
            return _cachedActor;
        }

        /// <summary>Returns the cached <see cref="DisplayObject"/>, refreshing it from the scene on demand.</summary>
        private DisplayObject GetCachedDisplay()
        {
            if (_cachedDisplay == null)
            {
                _cachedDisplay = FindAnyObjectByType<DisplayObject>();
            }
            return _cachedDisplay;
        }

        /// <summary>Returns the cached <see cref="GuidanceZone"/>, refreshing it from the scene on demand.</summary>
        private GuidanceZone GetCachedGuidanceZone()
        {
            if (_cachedGuidanceZone == null)
            {
                _cachedGuidanceZone = FindAnyObjectByType<GuidanceZone>();
            }
            return _cachedGuidanceZone;
        }

        /// <summary>Returns the cached <see cref="OccupancyZone"/>, refreshing it from the scene on demand.</summary>
        private OccupancyZone GetCachedOccupancyZone()
        {
            if (_cachedOccupancyZone == null)
            {
                _cachedOccupancyZone = FindAnyObjectByType<OccupancyZone>();
            }
            return _cachedOccupancyZone;
        }

        /// <summary>Returns the cached <see cref="MQTTClient"/>, refreshing it from the scene on demand.</summary>
        private MQTTClient GetCachedClient()
        {
            if (_client == null)
            {
                _client = FindAnyObjectByType<MQTTClient>();
            }
            return _client;
        }

        /// <summary>Ensures required GameObjects and folders exist in the scene.</summary>
        private void InitializeScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings"))
            {
                AssetDatabase.CreateFolder("Assets", "VRSettings");
            }
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings/Displays"))
            {
                AssetDatabase.CreateFolder("Assets/VRSettings", "Displays");
            }

            string[] defaultObjectNames = { "Actors", "Controllers", "MQTT Client" };
            foreach (string objectName in defaultObjectNames)
            {
                GameObject sceneObject = GameObject.Find(objectName);
                if (sceneObject == null)
                {
                    Debug.Log($"Creating Object: {objectName}..");
                    sceneObject = new GameObject(objectName);
                    if (string.Equals(objectName, "MQTT Client", System.StringComparison.Ordinal))
                    {
                        sceneObject.AddComponent<MQTTClient>();
                    }
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }
                switch (objectName)
                {
                    case "MQTT Client":
                        sceneObject.TryGetComponent(out _client);
                        sceneObject.hideFlags = HideFlags.HideInHierarchy;
                        break;
                    case "Controllers":
                        sceneObject.hideFlags = HideFlags.None;
                        break;
                    default:
                        break;
                }
            }

            RemoveDefaultMainCamera();
            EnsureActorAndDisplay();
            EnsureControllers();
            EnsureMqttDefaults();
        }

        /// <summary>Creates an Actor and a Display when the active scene lacks them, and links the two.</summary>
        /// <remarks>
        /// The Actor and Display models are the first prefabs found under <c>Resources/Actors/Prefabs/</c> and
        /// <c>Resources/Displays/</c>. The Actor is linked to the Display via <see cref="ActorObject.Display"/> so
        /// the projection cameras render through the Actor's view.
        /// </remarks>
        private void EnsureActorAndDisplay()
        {
            ActorObject actor = FindAnyObjectByType<ActorObject>();
            if (actor == null)
            {
                GameObject[] actorModels = Resources.LoadAll<GameObject>("Actors/Prefabs");
                string defaultModel = actorModels.Length > 0 ? actorModels[0].name : "None";
                GameObject actorGameObject = new GameObject("Actor");
                actor = actorGameObject.AddComponent<ActorObject>();
                actor.InitiateActor(defaultModel, trackCamera: true);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            DisplayObject display = FindAnyObjectByType<DisplayObject>();
            if (display == null)
            {
                GameObject[] displayModels = Resources.LoadAll<GameObject>("Displays");
                if (displayModels.Length == 0)
                {
                    string message =
                        "Unable to create the scene Display. At least one display prefab must exist under "
                        + "Resources/Displays, but that folder holds none.";
                    Debug.LogError(message);
                    return;
                }
                display = DisplayObject.Create("Display", displayModels[0].name);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            if (actor.Display != display)
            {
                actor.Display = display;
            }
        }

        /// <summary>Caches the resolved (display name, controller type) spec for every ControllerTypes value.</summary>
        /// <remarks>
        /// Logged errors for unresolved subclasses surface here, keeping the per-scene path allocation-free.
        /// </remarks>
        private static (string DisplayName, System.Type ControllerType)[] BuildControllerSpecs()
        {
            System.Reflection.Assembly controllerAssembly = typeof(ControllerObject).Assembly;
            return System
                .Enum.GetValues(typeof(ControllerTypes))
                .Cast<ControllerTypes>()
                .Select(controllerType =>
                {
                    System.Type resolvedType = controllerAssembly.GetType($"Gimbl.{controllerType}");
                    if (resolvedType == null)
                    {
                        string message =
                            $"Unable to resolve the controller type for the {controllerType} member. The "
                            + $"ControllerObject assembly must declare a 'Gimbl.{controllerType}' class, but it "
                            + "declares none.";
                        Debug.LogError(message);
                    }
                    string displayName = controllerType switch
                    {
                        ControllerTypes.LinearTreadmill => "Linear",
                        ControllerTypes.SimulatedLinearTreadmill => "Simulated Linear",
                        _ => controllerType.ToString(),
                    };
                    return (DisplayName: displayName, ControllerType: resolvedType);
                })
                .ToArray();
        }
    }
}
