/// <summary>
/// Verifies the Task Parameters, play state, and monitor surfaces of the McpBridge editor plugin.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the task parameter, scene snapshot, play state, and monitor handlers of McpBridge.</summary>
    /// <remarks>
    /// Every test drives the bridge through its Dispatch entry point or through the private handler that backs a tool,
    /// against a scene the fixture builds from scratch, or a named project scene the test opens itself. No assertion
    /// depends on whichever scene happened to be open. The FullScreenViewManager the bridge resolves is installed by
    /// the fixture with a synthetic monitor list. That manager is either an open Parameters window's manager or one
    /// built without running the enumerating constructor, so the camera mapping surface is deterministic and real
    /// monitor enumeration runs in the refresh tests alone.
    /// </remarks>
    [TestFixture]
    public class McpBridgeTaskParametersTests
    {
        /// <summary>The EditorPrefs key the mqtt section writes the broker address to.</summary>
        private const string IpPreferenceKey = "SollertiaVR_MQTT_IP";

        /// <summary>The EditorPrefs key the mqtt section writes the broker port to.</summary>
        private const string PortPreferenceKey = "SollertiaVR_MQTT_Port";

        /// <summary>The hand-authored scene used to pin the play state tool's active scene name.</summary>
        private const string TemplateScenePath = "Assets/Scenes/ExperimentTemplate.unity";

        /// <summary>The monitor coordinate no detected monitor can report, used by the refresh test.</summary>
        private const int SentinelCoordinate = 987654;

        /// <summary>The path of the scene open before the fixture replaced it, restored at the end.</summary>
        private string _originalScenePath;

        /// <summary>The manager cached by the bridge before the fixture replaced it.</summary>
        private FullScreenViewManager _originalCachedManager;

        /// <summary>The manager installed into the bridge cache so no request constructs a new one.</summary>
        private FullScreenViewManager _sharedManager;

        /// <summary>The manager the bridge resolves for the running test.</summary>
        private FullScreenViewManager _manager;

        /// <summary>The monitor list the resolved manager carried before the running test replaced it.</summary>
        private List<Monitor> _originalMonitors;

        /// <summary>The synthetic two-monitor list installed for the running test.</summary>
        private List<Monitor> _syntheticMonitors;

        /// <summary>The saved camera assignment asset detached from the manager for the running test.</summary>
        private FullScreenViewsSaved _originalSavedViews;

        /// <summary>Every object the running test created, destroyed during TearDown.</summary>
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        /// <summary>Determines whether the broker address preference existed before the fixture ran.</summary>
        private bool _hadIpPreference;

        /// <summary>The broker address preference value captured before the fixture ran.</summary>
        private string _originalIpPreference;

        /// <summary>Determines whether the broker port preference existed before the fixture ran.</summary>
        private bool _hadPortPreference;

        /// <summary>The broker port preference value captured before the fixture ran.</summary>
        private int _originalPortPreference;

        /// <summary>Records the editor state the fixture replaces and resolves the shared monitor manager.</summary>
        /// <remarks>
        /// The bridge clears its cached manager on every active scene change, so the cached value is captured before
        /// the first scene swap. The shared manager is reused by every test, which keeps monitor enumeration to at
        /// most one run for the whole fixture rather than one run per request.
        /// </remarks>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            _originalCachedManager = PrivateAccess.GetStaticField<FullScreenViewManager>(
                typeof(McpBridge),
                "_cachedFullScreenManager"
            );

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _sharedManager = ResolveSharedManager();
        }

        /// <summary>Restores the scene that was open and the manager the bridge had cached.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (!string.IsNullOrEmpty(_originalScenePath) && File.Exists(_originalScenePath))
            {
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            // Restored after the scene swap, because the swap itself clears the bridge's cached manager.
            PrivateAccess.SetStaticField(typeof(McpBridge), "_cachedFullScreenManager", _originalCachedManager);
        }

        /// <summary>Opens an empty scene and installs the synthetic monitor list and preference backups.</summary>
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrivateAccess.SetStaticField(typeof(McpBridge), "_cachedFullScreenManager", _sharedManager);

            _manager = (FullScreenViewManager)PrivateAccess.InvokeStatic(typeof(McpBridge), "AcquireFullScreenManager");
            _originalMonitors = _manager.monitors;
            _syntheticMonitors = new List<Monitor>
            {
                CreateMonitor(left: 0, top: 0, width: 1920, height: 1080),
                CreateMonitor(left: 1920, top: 0, width: 2560, height: 1440),
            };
            _manager.monitors = _syntheticMonitors;

            // Detaching the saved-views asset makes SaveCameras a no-op, so no camera mapping write reaches a
            // per-scene companion asset in the project.
            _originalSavedViews = PrivateAccess.GetField<FullScreenViewsSaved>(_manager, "_savedFullScreenViews");
            PrivateAccess.SetField(_manager, "_savedFullScreenViews", null);

            _hadIpPreference = EditorPrefs.HasKey(IpPreferenceKey);
            _originalIpPreference = EditorPrefs.GetString(IpPreferenceKey);
            _hadPortPreference = EditorPrefs.HasKey(PortPreferenceKey);
            _originalPortPreference = EditorPrefs.GetInt(PortPreferenceKey);
        }

        /// <summary>Destroys everything the test created and restores the manager and preference state.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object created in _createdObjects)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }
            _createdObjects.Clear();

            _manager.monitors = _originalMonitors;
            PrivateAccess.SetField(_manager, "_savedFullScreenViews", _originalSavedViews);

            if (_hadIpPreference)
            {
                EditorPrefs.SetString(IpPreferenceKey, _originalIpPreference);
            }
            else
            {
                EditorPrefs.DeleteKey(IpPreferenceKey);
            }

            if (_hadPortPreference)
            {
                EditorPrefs.SetInt(PortPreferenceKey, _originalPortPreference);
            }
            else
            {
                EditorPrefs.DeleteKey(PortPreferenceKey);
            }
        }

        /// <summary>Verifies that the scene walk resolves every component the Task Parameters surface reads.</summary>
        [Test]
        public void AcquireSceneComponents_PopulatedScene_ResolvesEveryComponent()
        {
            ActorObject actor = CreateActor();
            DisplayObject display = CreateDisplay(withSettings: true);
            Task task = CreateTask();
            MQTTClient client = CreateClient();
            CreateController("Simulated");
            CreateController("Treadmill");
            CreateCamera("Left View");
            CreateGuidanceZone();
            CreateOccupancyZone();

            object components = PrivateAccess.InvokeStatic(typeof(McpBridge), "AcquireSceneComponents");

            Assert.AreSame(actor, PrivateAccess.GetField<ActorObject>(components, "Actor"));
            Assert.AreSame(display, PrivateAccess.GetField<DisplayObject>(components, "Display"));
            Assert.AreSame(task, PrivateAccess.GetField<Task>(components, "Task"));
            Assert.AreSame(client, PrivateAccess.GetField<MQTTClient>(components, "Client"));
            Assert.AreEqual(2, PrivateAccess.GetField<ControllerOutput[]>(components, "Controllers").Length);
            Assert.AreEqual(1, PrivateAccess.GetField<Camera[]>(components, "DisplayCameras").Length);
            Assert.IsTrue(PrivateAccess.GetField<bool>(components, "HasInteractionZone"));
            Assert.IsTrue(PrivateAccess.GetField<bool>(components, "HasOccupancyZone"));
            Assert.AreSame(_manager, PrivateAccess.GetField<FullScreenViewManager>(components, "FullScreenManager"));
        }

        /// <summary>Verifies that the scene walk reports every component absent for an empty scene.</summary>
        [Test]
        public void AcquireSceneComponents_EmptyScene_ReportsEveryComponentAbsent()
        {
            object components = PrivateAccess.InvokeStatic(typeof(McpBridge), "AcquireSceneComponents");

            Assert.IsNull(PrivateAccess.GetField<ActorObject>(components, "Actor"));
            Assert.IsNull(PrivateAccess.GetField<DisplayObject>(components, "Display"));
            Assert.IsNull(PrivateAccess.GetField<Task>(components, "Task"));
            Assert.IsNull(PrivateAccess.GetField<MQTTClient>(components, "Client"));
            Assert.AreEqual(0, PrivateAccess.GetField<ControllerOutput[]>(components, "Controllers").Length);
            Assert.AreEqual(0, PrivateAccess.GetField<Camera[]>(components, "DisplayCameras").Length);
            Assert.IsFalse(PrivateAccess.GetField<bool>(components, "HasInteractionZone"));
            Assert.IsFalse(PrivateAccess.GetField<bool>(components, "HasOccupancyZone"));
        }

        /// <summary>Verifies that the display camera filter drops the Main Camera by tag and by name.</summary>
        [Test]
        public void GetDisplayCameras_MainCameraByTagOrByName_ExcludesBothFromTheResult()
        {
            CreateCamera("Left View");
            CreateCamera("Main Camera");
            Camera tagged = CreateCamera("Tagged View");
            tagged.gameObject.tag = "MainCamera";

            Camera[] cameras = (Camera[])PrivateAccess.InvokeStatic(typeof(McpBridge), "GetDisplayCameras");

            Assert.AreEqual(1, cameras.Length);
            Assert.AreEqual("Left View", cameras[0].name);
        }

        /// <summary>Verifies that the actor model options list the Resources prefabs before the None entry.</summary>
        [Test]
        public void GetValidActorModels_ProjectResources_ListsEveryPrefabThenNone()
        {
            string[] models = (string[])PrivateAccess.InvokeStatic(typeof(McpBridge), "GetValidActorModels");

            CollectionAssert.AreEqual(new[] { "Rodent", "None" }, models);
        }

        /// <summary>Verifies that two requests in the same scene share one FullScreenViewManager instance.</summary>
        [Test]
        public void AcquireFullScreenManager_SecondRequest_ReturnsTheSameManagerInstance()
        {
            object first = PrivateAccess.InvokeStatic(typeof(McpBridge), "AcquireFullScreenManager");
            object second = PrivateAccess.InvokeStatic(typeof(McpBridge), "AcquireFullScreenManager");

            Assert.AreSame(first, second);
            Assert.AreSame(_manager, first);
        }

        /// <summary>Verifies that an empty scene reports a null state section for every absent component.</summary>
        [Test]
        public void ReadTaskParameters_EmptyScene_ReportsNullStateSections()
        {
            Dictionary<string, object> response = Read();

            Dictionary<string, object> state = GetNestedObject(response, "state");
            Assert.IsNull(state["actor"]);
            Assert.IsNull(state["mqtt"]);
            Assert.IsNull(state["display"]);
            Assert.IsNull(state["task"]);
        }

        /// <summary>Verifies that the controller and camera options of an empty scene carry None alone.</summary>
        [Test]
        public void ReadTaskParameters_EmptyScene_ReportsOptionsCarryingNoneAlone()
        {
            Dictionary<string, object> response = Read();

            Dictionary<string, object> options = GetNestedObject(response, "options");
            CollectionAssert.AreEqual(
                new[] { "None" },
                ReadStringList(GetNestedObject(options, "actor")["controller"])
            );
            CollectionAssert.AreEqual(
                new[] { "None" },
                ReadStringList(GetNestedObject(options, "camera_mapping")["camera"])
            );
        }

        /// <summary>Verifies that the actor model is reported as the child name without its prefix.</summary>
        [Test]
        public void ReadTaskParameters_ActorWithModelChild_ReportsTheNameWithoutThePrefix()
        {
            ActorObject actor = CreateActor();
            AddChild(actor.gameObject, "Model Rodent");

            Dictionary<string, object> response = Read();

            Assert.AreEqual("Rodent", GetNestedObject(GetNestedObject(response, "state"), "actor")["model"]);
        }

        /// <summary>Verifies that an actor whose children carry no model prefix reports the None model.</summary>
        [Test]
        public void ReadTaskParameters_ActorWithoutModelChild_ReportsTheNoneModel()
        {
            ActorObject actor = CreateActor();
            AddChild(actor.gameObject, "Modelling Clay");

            Dictionary<string, object> response = Read();

            Assert.AreEqual("None", GetNestedObject(GetNestedObject(response, "state"), "actor")["model"]);
        }

        /// <summary>Verifies that the first model child wins when the actor carries more than one.</summary>
        [Test]
        public void ReadTaskParameters_ActorWithTwoModelChildren_ReportsTheFirstChild()
        {
            ActorObject actor = CreateActor();
            AddChild(actor.gameObject, "Model First");
            AddChild(actor.gameObject, "Model Second");

            Dictionary<string, object> response = Read();

            Assert.AreEqual("First", GetNestedObject(GetNestedObject(response, "state"), "actor")["model"]);
        }

        /// <summary>Verifies that an actor with no controller assigned reports the None controller.</summary>
        [Test]
        public void ReadTaskParameters_ActorWithoutController_ReportsTheNoneController()
        {
            CreateActor();
            CreateController("Simulated");

            Dictionary<string, object> response = Read();

            Assert.AreEqual("None", GetNestedObject(GetNestedObject(response, "state"), "actor")["controller"]);
        }

        /// <summary>Verifies that an assigned controller is reported by its GameObject name.</summary>
        [Test]
        public void ReadTaskParameters_ActorWithController_ReportsTheControllerObjectName()
        {
            ActorObject actor = CreateActor();
            actor.Controller = CreateController("Simulated");

            Dictionary<string, object> response = Read();

            Assert.AreEqual("Simulated", GetNestedObject(GetNestedObject(response, "state"), "actor")["controller"]);
        }

        /// <summary>Verifies that the controller options lead with None and then list every scene controller.</summary>
        [Test]
        public void ReadTaskParameters_TwoControllers_ReportsOptionsLedByNone()
        {
            CreateController("Simulated");
            CreateController("Treadmill");

            Dictionary<string, object> response = Read();

            List<string> controllers = ReadStringList(
                GetNestedObject(GetNestedObject(response, "options"), "actor")["controller"]
            );
            Assert.AreEqual(3, controllers.Count);
            Assert.AreEqual("None", controllers[0]);
            CollectionAssert.AreEquivalent(new[] { "None", "Simulated", "Treadmill" }, controllers);
        }

        /// <summary>Verifies that the camera options lead with None and then list every assignable camera.</summary>
        [Test]
        public void ReadTaskParameters_DisplayCameraPresent_ReportsCameraOptionsLedByNone()
        {
            CreateCamera("Left View");
            CreateCamera("Main Camera");

            Dictionary<string, object> response = Read();

            List<string> cameras = ReadStringList(
                GetNestedObject(GetNestedObject(response, "options"), "camera_mapping")["camera"]
            );
            CollectionAssert.AreEqual(new[] { "None", "Left View" }, cameras);
        }

        /// <summary>Verifies that the model options reported by a read match the valid actor model list.</summary>
        [Test]
        public void ReadTaskParameters_AnyScene_ReportsTheActorModelOptions()
        {
            Dictionary<string, object> response = Read();

            List<string> models = ReadStringList(
                GetNestedObject(GetNestedObject(response, "options"), "actor")["model"]
            );
            CollectionAssert.AreEqual(new[] { "Rodent", "None" }, models);
        }

        /// <summary>Verifies that the mqtt state reports the client's broker address and port.</summary>
        [Test]
        public void ReadTaskParameters_MqttClientPresent_ReportsAddressAndPort()
        {
            MQTTClient client = CreateClient();
            client.ipAddress = "10.0.0.5";
            client.port = 1884;

            Dictionary<string, object> response = Read();

            Dictionary<string, object> mqtt = GetNestedObject(GetNestedObject(response, "state"), "mqtt");
            Assert.AreEqual("10.0.0.5", mqtt["ip"]);
            Assert.AreEqual(1884d, ReadNumber(mqtt["port"]));
        }

        /// <summary>Verifies that a display with settings reports the settings brightness and VR height.</summary>
        [Test]
        public void ReadTaskParameters_DisplayWithSettings_ReportsTheSettingsValues()
        {
            DisplayObject display = CreateDisplay(withSettings: true);
            display.currentBrightness = 12.5f;
            display.settings.brightness = 42.5f;
            display.settings.heightInVR = 0.75f;

            Dictionary<string, object> response = Read();

            Dictionary<string, object> state = GetNestedObject(GetNestedObject(response, "state"), "display");
            Assert.AreEqual(12.5d, ReadNumber(state["current_brightness"]));
            Assert.AreEqual(42.5d, ReadNumber(state["brightness"]));
            Assert.AreEqual(0.75d, ReadNumber(state["height_in_vr"]));
        }

        /// <summary>Verifies that a display without settings reports the documented fallback values.</summary>
        [Test]
        public void ReadTaskParameters_DisplayWithoutSettings_ReportsTheFallbackValues()
        {
            DisplayObject display = CreateDisplay(withSettings: false);
            display.currentBrightness = 12.5f;

            Dictionary<string, object> response = Read();

            Dictionary<string, object> state = GetNestedObject(GetNestedObject(response, "state"), "display");
            Assert.AreEqual(12.5d, ReadNumber(state["current_brightness"]));
            Assert.AreEqual(100d, ReadNumber(state["brightness"]));
            Assert.AreEqual(0d, ReadNumber(state["height_in_vr"]));

            // Destroyed inside the test body because the Parameters window dereferences display.settings
            // whenever it repaints, which a settings-less display outliving this method would fault on.
            UnityEngine.Object.DestroyImmediate(display.gameObject);
        }

        /// <summary>Verifies that the task state reports all six task fields at their component values.</summary>
        [Test]
        public void ReadTaskParameters_TaskPresent_ReportsEveryTaskField()
        {
            ActorObject actor = CreateActor();
            Task task = CreateTask();
            task.requireInteraction = true;
            task.requireWait = false;
            task.trackLength = 1234.5f;
            task.trackSeed = 7;
            task.actor = actor;
            task.configPath = "Configurations/ZZTest_Snapshot.yaml";

            Dictionary<string, object> response = Read();

            Dictionary<string, object> state = GetNestedObject(GetNestedObject(response, "state"), "task");
            Assert.AreEqual(true, state["require_interaction"]);
            Assert.AreEqual(false, state["require_wait"]);
            Assert.AreEqual(1234.5d, ReadNumber(state["track_length"]));
            Assert.AreEqual(7d, ReadNumber(state["track_seed"]));
            Assert.AreEqual(actor.gameObject.name, state["actor"]);
            Assert.AreEqual("Configurations/ZZTest_Snapshot.yaml", state["config_path"]);
        }

        /// <summary>Verifies that the task snapshot reports a null actor when the reference is unassigned.</summary>
        [Test]
        public void ReadTaskParameters_TaskWithoutActor_ReportsANullActorName()
        {
            Task task = CreateTask();
            task.actor = null;

            Dictionary<string, object> response = Read();

            Dictionary<string, object> state = GetNestedObject(GetNestedObject(response, "state"), "task");
            Assert.IsNull(state["actor"]);
        }

        /// <summary>Verifies that the camera mapping reports one one-based row per detected monitor.</summary>
        [Test]
        public void ReadTaskParameters_DetectedMonitors_ReportsOneBasedRowPerMonitor()
        {
            Dictionary<string, object> response = Read();

            List<Dictionary<string, object>> rows = ReadRowList(GetNestedObject(response, "state")["camera_mapping"]);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(1d, ReadNumber(rows[0]["monitor"]));
            Assert.AreEqual(0d, ReadNumber(rows[0]["left"]));
            Assert.AreEqual(0d, ReadNumber(rows[0]["top"]));
            Assert.AreEqual("None", rows[0]["camera"]);
            Assert.AreEqual(2d, ReadNumber(rows[1]["monitor"]));
            Assert.AreEqual(1920d, ReadNumber(rows[1]["left"]));
            Assert.AreEqual("None", rows[1]["camera"]);
        }

        /// <summary>Verifies that an assigned monitor reports the bound camera by name.</summary>
        [Test]
        public void ReadTaskParameters_MonitorBoundToCamera_ReportsTheCameraName()
        {
            Camera camera = CreateCamera("Left View");
            _syntheticMonitors[0].cameraEntityId = camera.GetEntityId();

            Dictionary<string, object> response = Read();

            List<Dictionary<string, object>> rows = ReadRowList(GetNestedObject(response, "state")["camera_mapping"]);
            Assert.AreEqual("Left View", rows[0]["camera"]);
            Assert.AreEqual("None", rows[1]["camera"]);
        }

        /// <summary>Verifies that both zone-gated controls report visible when both zones are present.</summary>
        [Test]
        public void ReadTaskParameters_BothZonesPresent_ReportsBothControlsVisible()
        {
            CreateTask();
            CreateGuidanceZone();
            CreateOccupancyZone();

            Dictionary<string, object> response = Read();

            Dictionary<string, object> visibility = GetNestedObject(GetNestedObject(response, "visibility"), "task");
            Assert.AreEqual(true, visibility["require_interaction"]);
            Assert.AreEqual(true, visibility["require_wait"]);
        }

        /// <summary>Verifies that both zone-gated controls report hidden when neither zone is present.</summary>
        [Test]
        public void ReadTaskParameters_NoZonesPresent_ReportsBothControlsHidden()
        {
            CreateTask();

            Dictionary<string, object> response = Read();

            Dictionary<string, object> visibility = GetNestedObject(GetNestedObject(response, "visibility"), "task");
            Assert.AreEqual(false, visibility["require_interaction"]);
            Assert.AreEqual(false, visibility["require_wait"]);
        }

        /// <summary>Verifies that each zone-gated control reports its own zone's presence independently.</summary>
        [Test]
        public void ReadTaskParameters_GuidanceZoneOnly_ReportsOnlyTheInteractionControlVisible()
        {
            CreateTask();
            CreateGuidanceZone();

            Dictionary<string, object> response = Read();

            Dictionary<string, object> visibility = GetNestedObject(GetNestedObject(response, "visibility"), "task");
            Assert.AreEqual(true, visibility["require_interaction"]);
            Assert.AreEqual(false, visibility["require_wait"]);
        }

        /// <summary>Verifies that a model outside the valid model list is rejected with the valid list.</summary>
        [Test]
        public void WriteTaskParameters_UnknownActorModel_ReturnsTheInvalidModelError()
        {
            CreateActor();

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "model", "Giraffe"));

            string error = ErrorOf(response);
            StringAssert.Contains("Invalid model 'Giraffe'", error);
            StringAssert.Contains("Rodent", error);
            StringAssert.Contains("None", error);
        }

        /// <summary>Verifies that an actor section is ignored outright when the scene carries no actor.</summary>
        [Test]
        public void WriteTaskParameters_UnknownActorModelWithoutActor_IsIgnored()
        {
            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "model", "Giraffe"));

            AssertSucceeded(response);
            Assert.IsNull(GetNestedObject(response, "state")["actor"]);
        }

        /// <summary>Verifies that a non-string model value is neither validated nor applied.</summary>
        [Test]
        public void WriteTaskParameters_NonStringActorModel_IsIgnored()
        {
            ActorObject actor = CreateActor();
            AddChild(actor.gameObject, "Model Rodent");

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "model", 7L));

            AssertSucceeded(response);
            Assert.AreEqual("Rodent", GetNestedObject(GetNestedObject(response, "state"), "actor")["model"]);
        }

        /// <summary>Verifies that writing a valid model instantiates the matching model child.</summary>
        [Test]
        public void WriteTaskParameters_ValidActorModel_InstantiatesTheModelChild()
        {
            CreateActor();

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "model", "Rodent"));

            AssertSucceeded(response);
            Assert.AreEqual("Rodent", GetNestedObject(GetNestedObject(response, "state"), "actor")["model"]);
            Assert.AreEqual("Rodent", GetNestedObject(GetNestedObject(Read(), "state"), "actor")["model"]);
        }

        /// <summary>Verifies that a controller name matching no scene controller is rejected.</summary>
        [Test]
        public void WriteTaskParameters_UnknownController_ReturnsTheInvalidControllerError()
        {
            CreateActor();
            CreateController("Simulated");

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "controller", "Missing"));

            string error = ErrorOf(response);
            StringAssert.Contains("Invalid controller 'Missing'", error);
            StringAssert.Contains("Valid: None, Simulated", error);
        }

        /// <summary>Verifies that the None controller clears the actor's controller reference.</summary>
        [Test]
        public void WriteTaskParameters_NoneController_ClearsTheActorController()
        {
            ActorObject actor = CreateActor();
            actor.Controller = CreateController("Simulated");

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "controller", "None"));

            AssertSucceeded(response);
            Assert.IsNull(actor.Controller);
            Assert.AreEqual("None", GetNestedObject(GetNestedObject(response, "state"), "actor")["controller"]);
        }

        /// <summary>Verifies that a named controller is bound to the actor by GameObject name.</summary>
        [Test]
        public void WriteTaskParameters_NamedController_BindsTheMatchingController()
        {
            ActorObject actor = CreateActor();
            CreateController("Simulated");
            ControllerOutput treadmill = CreateController("Treadmill");

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "controller", "Treadmill"));

            AssertSucceeded(response);
            Assert.AreSame(treadmill, actor.Controller);
            Assert.AreEqual("Treadmill", GetNestedObject(GetNestedObject(response, "state"), "actor")["controller"]);
        }

        /// <summary>Verifies that a port that is not a whole number is rejected before anything is written.</summary>
        [Test]
        public void WriteTaskParameters_NonNumericPort_ReturnsTheInvalidPortError()
        {
            CreateClient();

            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "port", "eighteen"));

            StringAssert.Contains(
                "Invalid port 'eighteen'. Must be a whole number between 0 and 65535.",
                ErrorOf(response)
            );
        }

        /// <summary>Verifies that a port outside the 32-bit integer range is rejected.</summary>
        [Test]
        public void WriteTaskParameters_PortAboveIntegerRange_ReturnsTheInvalidPortError()
        {
            CreateClient();

            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "port", 3000000000L));

            StringAssert.Contains("Invalid port '3000000000'", ErrorOf(response));
        }

        /// <summary>Verifies that a port outside the broker port range leaves the client and the preference alone.
        /// </summary>
        [TestCase(-1L)]
        [TestCase(65536L)]
        public void WriteTaskParameters_PortOutsideTheBrokerRange_ReturnsTheInvalidPortError(long port)
        {
            MQTTClient client = CreateClient();
            EditorPrefs.SetInt(PortPreferenceKey, 1883);

            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "port", port));

            StringAssert.Contains(
                $"Invalid port '{port}'. Must be a whole number between 0 and 65535.",
                ErrorOf(response)
            );
            Assert.AreEqual(1883, client.port);
            Assert.AreEqual(1883, EditorPrefs.GetInt(PortPreferenceKey));
        }

        /// <summary>Verifies that both ends of the broker port range are accepted and written.</summary>
        [TestCase(0L)]
        [TestCase(65535L)]
        public void WriteTaskParameters_PortsAtBothRangeEnds_WritesTheClientAndThePreference(long port)
        {
            MQTTClient client = CreateClient();

            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "port", port));

            AssertSucceeded(response);
            Assert.AreEqual((int)port, client.port);
            Assert.AreEqual((int)port, EditorPrefs.GetInt(PortPreferenceKey));
        }

        /// <summary>Verifies that the mqtt section writes both the client fields and the editor preferences.</summary>
        [Test]
        public void WriteTaskParameters_AddressAndPort_WritesTheClientAndThePreferences()
        {
            MQTTClient client = CreateClient();

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "mqtt",
                    new Dictionary<string, object> { { "ip", "10.0.0.5" }, { "port", 1885L } }
                },
            };
            Dictionary<string, object> response = Write(arguments);

            AssertSucceeded(response);
            Assert.AreEqual("10.0.0.5", client.ipAddress);
            Assert.AreEqual(1885, client.port);
            Assert.AreEqual("10.0.0.5", EditorPrefs.GetString(IpPreferenceKey));
            Assert.AreEqual(1885, EditorPrefs.GetInt(PortPreferenceKey));
        }

        /// <summary>Verifies that a non-string broker address is neither validated nor applied.</summary>
        [Test]
        public void WriteTaskParameters_NonStringAddress_IsIgnored()
        {
            MQTTClient client = CreateClient();

            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "ip", 5L));

            AssertSucceeded(response);
            Assert.AreEqual("127.0.0.1", client.ipAddress);
        }

        /// <summary>Verifies that an mqtt section is ignored outright when the scene carries no client.</summary>
        [Test]
        public void WriteTaskParameters_MqttSectionWithoutClient_IsIgnored()
        {
            Dictionary<string, object> response = Write(BuildSectionArguments("mqtt", "port", "eighteen"));

            AssertSucceeded(response);
            Assert.IsNull(GetNestedObject(response, "state")["mqtt"]);
        }

        /// <summary>Verifies that a non-finite current brightness is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonFiniteCurrentBrightness_ReturnsTheInvalidError()
        {
            CreateDisplay(withSettings: true);

            Dictionary<string, object> response = Write(
                BuildSectionArguments("display", "current_brightness", float.NaN)
            );

            string error = ErrorOf(response);
            StringAssert.Contains("Invalid current_brightness", error);
            StringAssert.Contains("Must be a finite number.", error);
        }

        /// <summary>Verifies that a non-numeric brightness is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonNumericBrightness_ReturnsTheInvalidError()
        {
            CreateDisplay(withSettings: true);

            Dictionary<string, object> response = Write(BuildSectionArguments("display", "brightness", "bright"));

            StringAssert.Contains("Invalid brightness 'bright'. Must be a finite number.", ErrorOf(response));
        }

        /// <summary>Verifies that a non-numeric VR height is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonNumericHeightInVr_ReturnsTheInvalidError()
        {
            CreateDisplay(withSettings: true);

            Dictionary<string, object> response = Write(BuildSectionArguments("display", "height_in_vr", "tall"));

            StringAssert.Contains("Invalid height_in_vr 'tall'. Must be a finite number.", ErrorOf(response));
        }

        /// <summary>Verifies that the display section writes the settings values and the VR height offset.</summary>
        [Test]
        public void WriteTaskParameters_DisplayValues_WritesSettingsAndLocalPosition()
        {
            DisplayObject display = CreateDisplay(withSettings: true);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "display",
                    new Dictionary<string, object>
                    {
                        { "current_brightness", 12.5d },
                        { "brightness", 42.5d },
                        { "height_in_vr", 0.75d },
                    }
                },
            };
            Dictionary<string, object> response = Write(arguments);

            AssertSucceeded(response);
            Assert.AreEqual(12.5f, display.currentBrightness, 1e-6f);
            Assert.AreEqual(42.5f, display.settings.brightness, 1e-6f);
            Assert.AreEqual(0.75f, display.settings.heightInVR, 1e-6f);
            Assert.AreEqual(0.75f, display.transform.localPosition.y, 1e-6f);
        }

        /// <summary>Verifies that a display without settings takes only the current brightness write.</summary>
        [Test]
        public void WriteTaskParameters_DisplayWithoutSettings_WritesOnlyCurrentBrightness()
        {
            DisplayObject display = CreateDisplay(withSettings: false);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "display",
                    new Dictionary<string, object> { { "current_brightness", 12.5d }, { "brightness", 42.5d } }
                },
            };
            Dictionary<string, object> response = Write(arguments);

            AssertSucceeded(response);
            Assert.AreEqual(12.5f, display.currentBrightness, 1e-6f);
            Assert.AreEqual(
                100d,
                ReadNumber(GetNestedObject(GetNestedObject(response, "state"), "display")["brightness"])
            );
            Assert.AreEqual(0f, display.transform.localPosition.y, 1e-6f);

            // Destroyed inside the test body because the Parameters window dereferences display.settings
            // whenever it repaints, which a settings-less display outliving this method would fault on.
            UnityEngine.Object.DestroyImmediate(display.gameObject);
        }

        /// <summary>Verifies that a display section is ignored outright when the scene carries no display.</summary>
        [Test]
        public void WriteTaskParameters_DisplaySectionWithoutDisplay_IsIgnored()
        {
            Dictionary<string, object> response = Write(BuildSectionArguments("display", "brightness", "bright"));

            AssertSucceeded(response);
            Assert.IsNull(GetNestedObject(response, "state")["display"]);
        }

        /// <summary>Verifies that a camera mapping write is refused when no monitor was detected.</summary>
        [Test]
        public void WriteTaskParameters_CameraMappingWithoutMonitors_RefusesToClearTheAssignments()
        {
            _manager.monitors = new List<Monitor>();

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(new Dictionary<string, object>()));

            string error = ErrorOf(response);
            StringAssert.Contains("no monitors were detected", error);
            StringAssert.Contains("refresh_monitors", error);
        }

        /// <summary>Verifies that a monitor value that is not a whole number is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonNumericMonitor_ReturnsTheInvalidMonitorError()
        {
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", "first" } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            StringAssert.Contains("Invalid monitor 'first'. Must be a whole number.", ErrorOf(response));
        }

        /// <summary>Verifies that the monitor number below the one-based range is rejected.</summary>
        [Test]
        public void WriteTaskParameters_MonitorBelowRange_ReturnsTheInvalidMonitorIndexError()
        {
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", 0L } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            StringAssert.Contains("Invalid monitor index 0; scene has 2 monitors.", ErrorOf(response));
        }

        /// <summary>Verifies that the monitor number one past the detected count is rejected.</summary>
        [Test]
        public void WriteTaskParameters_MonitorAboveRange_ReturnsTheInvalidMonitorIndexError()
        {
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", 3L } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            StringAssert.Contains("Invalid monitor index 3; scene has 2 monitors.", ErrorOf(response));
        }

        /// <summary>Verifies that both ends of the one-based monitor range are accepted and assigned.</summary>
        [Test]
        public void WriteTaskParameters_MonitorsAtBothRangeEnds_AssignsEveryRow()
        {
            CreateCamera("Left View");
            CreateCamera("Right View");

            Dictionary<string, object> first = new Dictionary<string, object>
            {
                { "monitor", 1L },
                { "camera", "Left View" },
            };
            Dictionary<string, object> second = new Dictionary<string, object>
            {
                { "monitor", 2L },
                { "camera", "Right View" },
            };
            Dictionary<string, object> response = Write(BuildCameraMappingArguments(first, second));

            AssertSucceeded(response);
            List<Dictionary<string, object>> rows = ReadRowList(GetNestedObject(response, "state")["camera_mapping"]);
            Assert.AreEqual("Left View", rows[0]["camera"]);
            Assert.AreEqual("Right View", rows[1]["camera"]);
        }

        /// <summary>Verifies that a camera name matching no assignable camera is rejected.</summary>
        [Test]
        public void WriteTaskParameters_UnknownCamera_ReturnsTheInvalidCameraError()
        {
            CreateCamera("Left View");
            Dictionary<string, object> row = new Dictionary<string, object>
            {
                { "monitor", 2L },
                { "camera", "Ghost View" },
            };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            string error = ErrorOf(response);
            StringAssert.Contains("Invalid camera 'Ghost View' for monitor 2.", error);
            StringAssert.Contains("Valid: None, Left View", error);
        }

        /// <summary>Verifies that the None camera clears the monitor's existing assignment.</summary>
        [Test]
        public void WriteTaskParameters_NoneCamera_ClearsTheMonitorAssignment()
        {
            Camera camera = CreateCamera("Left View");
            _syntheticMonitors[0].cameraEntityId = camera.GetEntityId();
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", 1L }, { "camera", "None" } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            AssertSucceeded(response);
            Assert.AreEqual("None", ReadRowList(GetNestedObject(response, "state")["camera_mapping"])[0]["camera"]);
        }

        /// <summary>Verifies that a row carrying no camera key leaves the monitor's assignment untouched.</summary>
        [Test]
        public void WriteTaskParameters_RowWithoutCamera_LeavesTheAssignmentUntouched()
        {
            Camera camera = CreateCamera("Left View");
            _syntheticMonitors[0].cameraEntityId = camera.GetEntityId();
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", 1L } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            AssertSucceeded(response);
            Assert.AreEqual(
                "Left View",
                ReadRowList(GetNestedObject(response, "state")["camera_mapping"])[0]["camera"]
            );
        }

        /// <summary>Verifies that a row carrying a camera but no monitor is reported rather than dropped.</summary>
        /// <remarks>
        /// Such a row names no monitor to assign the camera to, so applying it is impossible. Reporting it tells the
        /// caller the assignment never happened, where skipping it would answer with success and change nothing.
        /// </remarks>
        [Test]
        public void WriteTaskParameters_RowWithoutMonitor_ReturnsTheMissingMonitorError()
        {
            Camera camera = CreateCamera("Left View");
            _syntheticMonitors[0].cameraEntityId = camera.GetEntityId();
            Dictionary<string, object> row = new Dictionary<string, object> { { "camera", "Left View" } };

            Dictionary<string, object> response = Write(BuildCameraMappingArguments(row));

            string error = ErrorOf(response);
            StringAssert.Contains("Invalid camera_mapping row.", error);
            StringAssert.Contains("'monitor'", error);
            Assert.AreEqual("Left View", ReadRowList(GetNestedObject(Read(), "state")["camera_mapping"])[0]["camera"]);
        }

        /// <summary>Verifies that a row carrying neither key is reported the same way.</summary>
        [Test]
        public void WriteTaskParameters_EmptyCameraMappingRow_ReturnsTheMissingMonitorError()
        {
            Dictionary<string, object> response = Write(BuildCameraMappingArguments(new Dictionary<string, object>()));

            StringAssert.Contains("Invalid camera_mapping row.", ErrorOf(response));
        }

        /// <summary>Verifies that a camera mapping entry that is not an object is skipped.</summary>
        [Test]
        public void WriteTaskParameters_NonObjectCameraMappingRow_IsSkipped()
        {
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "camera_mapping",
                    new List<object> { "not-a-row" }
                },
            };

            Dictionary<string, object> response = Write(arguments);

            AssertSucceeded(response);
            Assert.AreEqual(2, ReadRowList(GetNestedObject(response, "state")["camera_mapping"]).Count);
        }

        /// <summary>Verifies that a camera mapping list whose rows assign nothing leaves the scene clean.</summary>
        /// <remarks>
        /// A dirty scene is the Editor's prompt to save, and the same branch rewrites the per-scene companion asset,
        /// so a request that changed no assignment has to leave both untouched.
        /// </remarks>
        [Test]
        public void WriteTaskParameters_CameraMappingRowsAssigningNothing_LeavesTheSceneClean()
        {
            Camera camera = CreateCamera("Left View");
            _syntheticMonitors[0].cameraEntityId = camera.GetEntityId();
            Dictionary<string, object> row = new Dictionary<string, object> { { "monitor", 1L } };
            ClearActiveSceneDirtiness();

            AssertSucceeded(Write(BuildCameraMappingArguments(row)));

            Assert.IsFalse(
                SceneManager.GetActiveScene().isDirty,
                "A row carrying no camera assigns nothing, so the write must not dirty the scene."
            );
        }

        /// <summary>Verifies that an empty camera mapping list leaves the scene clean.</summary>
        [Test]
        public void WriteTaskParameters_EmptyCameraMappingList_LeavesTheSceneClean()
        {
            ClearActiveSceneDirtiness();

            AssertSucceeded(Write(BuildCameraMappingArguments()));

            Assert.IsFalse(
                SceneManager.GetActiveScene().isDirty,
                "An empty list assigns nothing, so the write must not dirty the scene."
            );
        }

        /// <summary>Verifies that a camera mapping row that assigns a camera dirties the scene.</summary>
        [Test]
        public void WriteTaskParameters_CameraMappingRowThatAssigns_DirtiesTheScene()
        {
            CreateCamera("Left View");
            Dictionary<string, object> row = new Dictionary<string, object>
            {
                { "monitor", 1L },
                { "camera", "Left View" },
            };
            ClearActiveSceneDirtiness();

            AssertSucceeded(Write(BuildCameraMappingArguments(row)));

            Assert.IsTrue(
                SceneManager.GetActiveScene().isDirty,
                "An applied assignment changes the scene, so the write must dirty it."
            );
        }

        /// <summary>Verifies that a camera mapping value that is not a list is ignored outright.</summary>
        [Test]
        public void WriteTaskParameters_CameraMappingThatIsNotAList_IsIgnored()
        {
            _manager.monitors = new List<Monitor>();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "camera_mapping", "not-a-list" },
            };

            Dictionary<string, object> response = Write(arguments);

            AssertSucceeded(response);
            Assert.AreEqual(0, ReadRowList(GetNestedObject(response, "state")["camera_mapping"]).Count);
        }

        /// <summary>Verifies that the interaction toggle is refused when the scene has no guidance zone.</summary>
        [Test]
        public void WriteTaskParameters_RequireInteractionWithoutGuidanceZone_ReturnsTheHiddenControlError()
        {
            CreateTask();
            CreateOccupancyZone();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "require_interaction", true));

            string error = ErrorOf(response);
            StringAssert.Contains("Cannot set require_interaction", error);
            StringAssert.Contains("no GuidanceZone", error);
        }

        /// <summary>Verifies that the wait toggle is refused when the scene has no occupancy zone.</summary>
        [Test]
        public void WriteTaskParameters_RequireWaitWithoutOccupancyZone_ReturnsTheHiddenControlError()
        {
            CreateTask();
            CreateGuidanceZone();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "require_wait", true));

            string error = ErrorOf(response);
            StringAssert.Contains("Cannot set require_wait", error);
            StringAssert.Contains("no OccupancyZone", error);
        }

        /// <summary>Verifies that the interaction toggle is written when the guidance zone is present.</summary>
        [Test]
        public void WriteTaskParameters_RequireInteractionWithGuidanceZone_WritesTheFlag()
        {
            Task task = CreateTask();
            CreateGuidanceZone();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "require_interaction", true));

            AssertSucceeded(response);
            Assert.IsTrue(task.requireInteraction);
            Assert.AreEqual(true, GetNestedObject(GetNestedObject(response, "state"), "task")["require_interaction"]);
        }

        /// <summary>Verifies that the wait toggle is written when the occupancy zone is present.</summary>
        [Test]
        public void WriteTaskParameters_RequireWaitWithOccupancyZone_WritesTheFlag()
        {
            Task task = CreateTask();
            CreateOccupancyZone();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "require_wait", true));

            AssertSucceeded(response);
            Assert.IsTrue(task.requireWait);
            Assert.AreEqual(true, GetNestedObject(GetNestedObject(response, "state"), "task")["require_wait"]);
        }

        /// <summary>Verifies that a toggle value that is not a boolean is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonBooleanRequireInteraction_ReturnsTheInvalidToggleError()
        {
            Task task = CreateTask();
            CreateGuidanceZone();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "require_interaction", "maybe"));

            StringAssert.Contains("Invalid require_interaction 'maybe'. Must be true or false.", ErrorOf(response));
            Assert.IsFalse(task.requireInteraction);
        }

        /// <summary>Verifies that a track length of zero is rejected at the positive-value boundary.</summary>
        [Test]
        public void WriteTaskParameters_ZeroTrackLength_ReturnsTheInvalidTrackLengthError()
        {
            CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", 0d));

            StringAssert.Contains("Invalid track_length '0'", ErrorOf(response));
        }

        /// <summary>Verifies that a track length just above zero is accepted at the boundary.</summary>
        [Test]
        public void WriteTaskParameters_TrackLengthJustAboveZero_WritesTheValue()
        {
            Task task = CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", 0.5d));

            AssertSucceeded(response);
            Assert.AreEqual(0.5f, task.trackLength, 1e-6f);
        }

        /// <summary>Verifies that a negative track length is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NegativeTrackLength_ReturnsTheInvalidTrackLengthError()
        {
            CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", -0.5d));

            StringAssert.Contains("Invalid track_length", ErrorOf(response));
        }

        /// <summary>Verifies that a track length overflowing single precision is rejected as non-finite.</summary>
        [Test]
        public void WriteTaskParameters_TrackLengthAboveSinglePrecision_ReturnsTheInvalidTrackLengthError()
        {
            CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", 1e40d));

            StringAssert.Contains("Invalid track_length", ErrorOf(response));
        }

        /// <summary>Verifies that a track length that cannot convert to a number at all is rejected.</summary>
        [Test]
        public void WriteTaskParameters_UnconvertibleTrackLength_ReturnsTheInvalidTrackLengthError()
        {
            CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", new object()));

            StringAssert.Contains("Invalid track_length", ErrorOf(response));
        }

        /// <summary>Verifies that a track seed that is not a whole number is rejected.</summary>
        [Test]
        public void WriteTaskParameters_NonNumericTrackSeed_ReturnsTheInvalidTrackSeedError()
        {
            CreateTask();

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_seed", "seed"));

            StringAssert.Contains("Invalid track_seed 'seed'. Must be a whole number.", ErrorOf(response));
        }

        /// <summary>Verifies that the random-seed sentinel is accepted as a track seed.</summary>
        [Test]
        public void WriteTaskParameters_RandomSeedSentinel_WritesTheSentinel()
        {
            Task task = CreateTask();
            task.trackSeed = 7;

            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_seed", -1L));

            AssertSucceeded(response);
            Assert.AreEqual(Task.RandomSeedSentinel, task.trackSeed);
        }

        /// <summary>Verifies that a task section is ignored outright when the scene carries no task.</summary>
        [Test]
        public void WriteTaskParameters_TaskSectionWithoutTask_IsIgnored()
        {
            Dictionary<string, object> response = Write(BuildSectionArguments("task", "track_length", -5d));

            AssertSucceeded(response);
            Assert.IsNull(GetNestedObject(response, "state")["task"]);
        }

        /// <summary>Verifies that a section value that is not an object is ignored outright.</summary>
        [Test]
        public void WriteTaskParameters_TaskSectionThatIsNotAnObject_IsIgnored()
        {
            Task task = CreateTask();
            task.trackSeed = 7;

            Dictionary<string, object> response = Write(new Dictionary<string, object> { { "task", "not-a-section" } });

            AssertSucceeded(response);
            Assert.AreEqual(7d, ReadNumber(GetNestedObject(GetNestedObject(response, "state"), "task")["track_seed"]));
        }

        /// <summary>Verifies that a rejected request leaves every earlier section unwritten.</summary>
        [Test]
        public void WriteTaskParameters_InvalidTaskValueBesideValidMqttValue_LeavesTheSceneUntouched()
        {
            MQTTClient client = CreateClient();
            CreateTask();

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "mqtt",
                    new Dictionary<string, object> { { "ip", "10.0.0.5" } }
                },
                {
                    "task",
                    new Dictionary<string, object> { { "track_seed", "seed" } }
                },
            };
            Dictionary<string, object> response = Write(arguments);

            StringAssert.Contains("Invalid track_seed", ErrorOf(response));
            Assert.AreEqual("127.0.0.1", client.ipAddress);
            Assert.AreEqual("127.0.0.1", GetNestedObject(GetNestedObject(Read(), "state"), "mqtt")["ip"]);
        }

        /// <summary>Verifies that an accepted write is observable in the snapshot the next read returns.</summary>
        [Test]
        public void WriteTaskParameters_AcceptedWrite_IsObservableInTheNextRead()
        {
            CreateTask();

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                {
                    "task",
                    new Dictionary<string, object> { { "track_length", 1234.5d }, { "track_seed", 7L } }
                },
            };
            AssertSucceeded(Write(arguments));

            Dictionary<string, object> state = GetNestedObject(GetNestedObject(Read(), "state"), "task");
            Assert.AreEqual(1234.5d, ReadNumber(state["track_length"]));
            Assert.AreEqual(7d, ReadNumber(state["track_seed"]));
        }

        /// <summary>Verifies that the write response reports the scene as the single pre-write walk saw it.</summary>
        /// <remarks>
        /// Writing the None model destroys every model child of the actor, which removes the OccupancyZone this test
        /// parents under one. The visibility flag in the response is therefore true only when the response was built
        /// from the component snapshot acquired before the write, and the following read proves the zone really left.
        /// </remarks>
        [Test]
        public void WriteTaskParameters_WriteThatChangesTheScene_ReportsThePreWriteWalk()
        {
            ActorObject actor = CreateActor();
            CreateTask();
            GameObject ghost = AddChild(actor.gameObject, "Model Ghost");
            ghost.AddComponent<OccupancyZone>();
            Assert.AreEqual(true, GetNestedObject(GetNestedObject(Read(), "visibility"), "task")["require_wait"]);

            Dictionary<string, object> response = Write(BuildSectionArguments("actor", "model", "None"));

            AssertSucceeded(response);
            Assert.AreEqual(true, GetNestedObject(GetNestedObject(response, "visibility"), "task")["require_wait"]);
            Assert.AreEqual(false, GetNestedObject(GetNestedObject(Read(), "visibility"), "task")["require_wait"]);
        }

        /// <summary>Verifies that the play state tool reports the edit state and the active scene name.</summary>
        [Test]
        public void GetPlayState_NotPlaying_ReportsTheEditStateAndTheSceneName()
        {
            EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Single);

            // An open Parameters window reloads its manager's camera assignments on every active-scene change, which
            // re-attaches the saved-views asset of the scene just opened. Reapplying the detach SetUp performed keeps
            // any later camera write off the hand-authored companion, and it is a no-op when that window is closed.
            PrivateAccess.SetField(_manager, "_savedFullScreenViews", null);

            Dictionary<string, object> response = Dispatch("get_play_state", new Dictionary<string, object>());

            Assert.AreEqual(true, response["success"]);
            Assert.AreEqual("edit", response["state"]);
            Assert.AreEqual("ExperimentTemplate", response["active_scene"]);
        }

        /// <summary>Verifies that the play state tool reports the state and scene keys and nothing else.</summary>
        /// <remarks>
        /// The reported scene name itself is pinned by the sibling test that opens a named scene, because comparing
        /// the response against the same expression the handler evaluates would assert nothing.
        /// </remarks>
        [Test]
        public void GetPlayState_NotPlaying_ReportsOnlyTheStateAndSceneKeys()
        {
            Dictionary<string, object> response = Dispatch("get_play_state", new Dictionary<string, object>());

            CollectionAssert.AreEquivalent(new[] { "state", "active_scene", "success" }, response.Keys.ToList());
        }

        /// <summary>Verifies that exiting play mode outside play mode reports the no-op result.</summary>
        [Test]
        public void ExitPlayMode_NotPlaying_ReportsTheNoOpResult()
        {
            Dictionary<string, object> response = Dispatch("exit_play_mode", new Dictionary<string, object>());

            Assert.AreEqual(true, response["success"]);
            Assert.AreEqual("Not in Play Mode.", response["message"]);
            Assert.AreEqual("edit", response["state"]);
        }

        /// <summary>Verifies that the monitor refresh drops the installed list and reports the detected one.</summary>
        /// <remarks>
        /// The detected monitor count is a property of the host, so the count itself is never pinned. What is pinned
        /// is that the sentinel monitor the test installs, whose coordinates no physical monitor can report, is gone
        /// from both the manager and the response afterwards, and that every reported row matches the refreshed list.
        /// A refresh that failed to re-detect, or a response built from the pre-refresh list, still carries it.
        /// </remarks>
        [Test]
        public void RefreshMonitors_ActiveScene_ReplacesTheMonitorListAndReportsIt()
        {
            _manager.monitors = new List<Monitor>
            {
                CreateMonitor(left: SentinelCoordinate, top: SentinelCoordinate, width: 100, height: 100),
            };

            Dictionary<string, object> response = DispatchEnumeratingMonitors("refresh_monitors");

            Assert.AreEqual(true, response["success"]);
            Assert.IsFalse(
                _manager.monitors.Any(monitor => monitor.left == SentinelCoordinate),
                "The refresh left the installed sentinel monitor in the manager's list."
            );
            List<Dictionary<string, object>> rows = ReadRowList(GetNestedObject(response, "state")["camera_mapping"]);
            Assert.AreEqual(_manager.monitors.Count, rows.Count);
            for (int index = 0; index < rows.Count; index++)
            {
                Assert.AreEqual(index + 1d, ReadNumber(rows[index]["monitor"]));
                Assert.AreEqual((double)_manager.monitors[index].left, ReadNumber(rows[index]["left"]));
                Assert.AreEqual((double)_manager.monitors[index].top, ReadNumber(rows[index]["top"]));
            }
        }

        /// <summary>Verifies that the monitor refresh answers in the same shape a read answers in.</summary>
        [Test]
        public void RefreshMonitors_ActiveScene_AnswersInTheSnapshotShape()
        {
            CreateTask();

            Dictionary<string, object> response = DispatchEnumeratingMonitors("refresh_monitors");

            Assert.AreEqual(true, response["success"]);
            CollectionAssert.AreEquivalent(
                new[] { "state", "options", "visibility", "success" },
                response.Keys.ToList()
            );
            Assert.AreEqual(
                false,
                GetNestedObject(GetNestedObject(response, "visibility"), "task")["require_interaction"]
            );
            CollectionAssert.AreEqual(
                new[] { "Rodent", "None" },
                ReadStringList(GetNestedObject(GetNestedObject(response, "options"), "actor")["model"])
            );
        }

        /// <summary>Verifies that the read tool answers with the state, options, and visibility sections.</summary>
        [Test]
        public void ReadTaskParameters_AnyScene_AnswersWithTheThreeSnapshotSections()
        {
            Dictionary<string, object> response = Read();

            CollectionAssert.AreEquivalent(
                new[] { "state", "options", "visibility", "success" },
                response.Keys.ToList()
            );
            CollectionAssert.AreEquivalent(
                new[] { "actor", "mqtt", "display", "camera_mapping", "task" },
                GetNestedObject(response, "state").Keys.ToList()
            );
        }

        /// <summary>Resolves the manager the fixture shares, preferring one an open Parameters window owns.</summary>
        /// <remarks>
        /// The public constructor runs Monitor.EnumerateMonitors, which spawns an OS subprocess on Linux and macOS and
        /// opens one popup EditorWindow per detected monitor, so the fallback builds the manager without running any
        /// constructor. The resulting instance carries an empty monitor list and a null saved-views field, which is
        /// exactly the state every test installs anyway, and it keeps SaveCameras a no-op for the whole fixture.
        /// </remarks>
        /// <returns>The manager installed into the bridge cache for the whole fixture.</returns>
        private static FullScreenViewManager ResolveSharedManager()
        {
            MainWindow window = Resources.FindObjectsOfTypeAll<MainWindow>().FirstOrDefault();
            if (window != null && window.fullScreenManager != null)
            {
                return window.fullScreenManager;
            }

            FullScreenViewManager manager = (FullScreenViewManager)
                FormatterServices.GetUninitializedObject(typeof(FullScreenViewManager));
            manager.monitors = new List<Monitor>();
            return manager;
        }

        /// <summary>Builds a monitor through its non-public constructor.</summary>
        /// <param name="left">The left edge of the monitor in pixels.</param>
        /// <param name="top">The top edge of the monitor in pixels.</param>
        /// <param name="width">The width of the monitor in pixels.</param>
        /// <param name="height">The height of the monitor in pixels.</param>
        /// <returns>The constructed monitor, unassigned to any camera.</returns>
        private static Monitor CreateMonitor(int left, int top, int width, int height)
        {
            return (Monitor)
                Activator.CreateInstance(
                    typeof(Monitor),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { left, top, width, height },
                    null
                );
        }

        /// <summary>Clears the active scene's dirty flag, so a later assertion observes this test's writes alone.
        /// </summary>
        /// <remarks>
        /// EditorSceneManager.ClearSceneDirtiness is internal, so the fixture reaches it the same way it reaches
        /// every other non-public member it drives. Saving the scene would clear the flag too, and it would also
        /// write the fixture's throwaway objects to disk.
        /// </remarks>
        private static void ClearActiveSceneDirtiness()
        {
            PrivateAccess.InvokeStatic(
                typeof(EditorSceneManager),
                "ClearSceneDirtiness",
                SceneManager.GetActiveScene()
            );
        }

        /// <summary>Routes a tool call through the bridge dispatcher and parses its JSON answer.</summary>
        /// <param name="tool">The tool name to dispatch.</param>
        /// <param name="arguments">The tool arguments.</param>
        /// <returns>The parsed response payload.</returns>
        private static Dictionary<string, object> Dispatch(string tool, Dictionary<string, object> arguments)
        {
            string json = (string)PrivateAccess.InvokeStatic(typeof(McpBridge), "Dispatch", tool, arguments);
            return MiniJson.Deserialize(json);
        }

        /// <summary>Dispatches a tool whose handler enumerates monitors, tolerating the headless graphics errors.
        /// </summary>
        /// <remarks>
        /// Monitor enumeration opens one short-lived popup window per detected monitor, and an editor running without
        /// a graphics device logs an error for each, so the dispatch runs with log failures suppressed. The setting is
        /// restored before the assertions run, so the test still fails on an unexpected error of its own.
        /// </remarks>
        /// <param name="tool">The tool name to dispatch.</param>
        /// <returns>The parsed response payload.</returns>
        private static Dictionary<string, object> DispatchEnumeratingMonitors(string tool)
        {
            bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                return Dispatch(tool, new Dictionary<string, object>());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreSetting;
            }
        }

        /// <summary>Reads the current Task Parameters snapshot.</summary>
        /// <returns>The parsed response payload.</returns>
        private static Dictionary<string, object> Read()
        {
            return Dispatch("read_task_parameters", new Dictionary<string, object>());
        }

        /// <summary>Writes the supplied Task Parameters subset.</summary>
        /// <param name="arguments">The section subset to write.</param>
        /// <returns>The parsed response payload.</returns>
        private static Dictionary<string, object> Write(Dictionary<string, object> arguments)
        {
            return Dispatch("write_task_parameters", arguments);
        }

        /// <summary>Builds a single-field write request for one section.</summary>
        /// <param name="section">The top-level section key.</param>
        /// <param name="key">The field name inside the section.</param>
        /// <param name="value">The field value to write.</param>
        /// <returns>The assembled arguments dictionary.</returns>
        private static Dictionary<string, object> BuildSectionArguments(string section, string key, object value)
        {
            return new Dictionary<string, object>
            {
                {
                    section,
                    new Dictionary<string, object> { { key, value } }
                },
            };
        }

        /// <summary>Builds a camera mapping write request from the supplied rows.</summary>
        /// <param name="rows">The rows to place in the camera_mapping list.</param>
        /// <returns>The assembled arguments dictionary.</returns>
        private static Dictionary<string, object> BuildCameraMappingArguments(params Dictionary<string, object>[] rows)
        {
            return new Dictionary<string, object> { { "camera_mapping", rows.Cast<object>().ToList() } };
        }

        /// <summary>Returns the nested dictionary stored at the supplied key.</summary>
        /// <param name="parent">The dictionary to read from.</param>
        /// <param name="key">The key holding the nested dictionary.</param>
        /// <returns>The nested dictionary.</returns>
        private static Dictionary<string, object> GetNestedObject(Dictionary<string, object> parent, string key)
        {
            Assert.IsNotNull(parent[key], $"Expected a nested object at '{key}'.");
            return (Dictionary<string, object>)parent[key];
        }

        /// <summary>Returns the parsed string list stored in a JSON array value.</summary>
        /// <param name="value">The parsed JSON array.</param>
        /// <returns>The list of strings it carries.</returns>
        private static List<string> ReadStringList(object value)
        {
            return ((List<object>)value).Select(item => (string)item).ToList();
        }

        /// <summary>Returns the parsed object list stored in a JSON array value.</summary>
        /// <param name="value">The parsed JSON array.</param>
        /// <returns>The list of dictionaries it carries.</returns>
        private static List<Dictionary<string, object>> ReadRowList(object value)
        {
            return ((List<object>)value).Select(item => (Dictionary<string, object>)item).ToList();
        }

        /// <summary>Returns a parsed JSON number as a double regardless of its integral or real form.</summary>
        /// <param name="value">The parsed JSON number.</param>
        /// <returns>The numeric value.</returns>
        private static double ReadNumber(object value)
        {
            return value is long integral ? integral : (double)value;
        }

        /// <summary>Asserts that a response reports success, quoting its error message when it does not.</summary>
        /// <param name="response">The parsed response payload.</param>
        private static void AssertSucceeded(Dictionary<string, object> response)
        {
            string message = response.TryGetValue("error", out object error) ? (string)error : "no error reported";
            Assert.AreEqual(true, response["success"], message);
        }

        /// <summary>Asserts that a response reports failure and returns its error message.</summary>
        /// <param name="response">The parsed response payload.</param>
        /// <returns>The reported error message.</returns>
        private static string ErrorOf(Dictionary<string, object> response)
        {
            Assert.AreEqual(false, response["success"], "Expected the request to be rejected.");
            return (string)response["error"];
        }

        /// <summary>Creates a tracked GameObject in the active scene.</summary>
        /// <param name="name">The name assigned to the new GameObject.</param>
        /// <returns>The created GameObject.</returns>
        private GameObject CreateObject(string name)
        {
            GameObject created = new GameObject(name);
            _createdObjects.Add(created);
            return created;
        }

        /// <summary>Creates a tracked child GameObject under the supplied parent.</summary>
        /// <param name="parent">The GameObject that receives the new child.</param>
        /// <param name="name">The name assigned to the new child.</param>
        /// <returns>The created child GameObject.</returns>
        private GameObject AddChild(GameObject parent, string name)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }

        /// <summary>Creates the scene's actor.</summary>
        /// <returns>The created actor.</returns>
        private ActorObject CreateActor()
        {
            return CreateObject("Actor").AddComponent<ActorObject>();
        }

        /// <summary>Creates the scene's task component.</summary>
        /// <returns>The created task.</returns>
        private Task CreateTask()
        {
            return CreateObject("Task").AddComponent<Task>();
        }

        /// <summary>Creates the scene's MQTT client.</summary>
        /// <returns>The created client.</returns>
        private MQTTClient CreateClient()
        {
            return CreateObject("MQTT Client").AddComponent<MQTTClient>();
        }

        /// <summary>Creates the scene's display, optionally backed by an in-memory settings asset.</summary>
        /// <param name="withSettings">Determines whether the display receives a settings object.</param>
        /// <returns>The created display.</returns>
        private DisplayObject CreateDisplay(bool withSettings)
        {
            DisplayObject display = CreateObject("Display").AddComponent<DisplayObject>();
            if (withSettings)
            {
                DisplaySettings settings = ScriptableObject.CreateInstance<DisplaySettings>();
                _createdObjects.Add(settings);
                display.settings = settings;
            }
            return display;
        }

        /// <summary>Creates a controller output the actor may bind to.</summary>
        /// <param name="name">The name assigned to the controller GameObject.</param>
        /// <returns>The created controller output.</returns>
        private ControllerOutput CreateController(string name)
        {
            return CreateObject(name).AddComponent<ControllerOutput>();
        }

        /// <summary>Creates a camera in the active scene.</summary>
        /// <param name="name">The name assigned to the camera GameObject.</param>
        /// <returns>The created camera.</returns>
        private Camera CreateCamera(string name)
        {
            return CreateObject(name).AddComponent<Camera>();
        }

        /// <summary>Creates a guidance zone, which makes the interaction toggle visible.</summary>
        /// <returns>The created guidance zone.</returns>
        private GuidanceZone CreateGuidanceZone()
        {
            return CreateObject("GuidanceRegion").AddComponent<GuidanceZone>();
        }

        /// <summary>Creates an occupancy zone, which makes the wait toggle visible.</summary>
        /// <returns>The created occupancy zone.</returns>
        private OccupancyZone CreateOccupancyZone()
        {
            return CreateObject("OccupancyRegion").AddComponent<OccupancyZone>();
        }
    }
}
