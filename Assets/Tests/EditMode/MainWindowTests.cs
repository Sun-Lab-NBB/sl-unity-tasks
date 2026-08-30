/// <summary>
/// Verifies the behavior of the MainWindow class.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// <summary>Verifies the behavior of the MainWindow class.</summary>
    /// <remarks>
    /// Every test starts from a fresh empty scene, because each helper under test resolves its collaborators
    /// through GameObject.Find or FindAnyObjectByType, and whichever scene the editor happened to have open
    /// would otherwise decide the outcome. The window instance is created once on first demand and reused,
    /// because its OnEnable enumerates the system monitors, opens a probe window on each one, and initializes
    /// the scene. The drawing code is unreachable from a headless editor, so only the static and per-scene
    /// helpers are covered here.
    /// </remarks>
    [TestFixture]
    public class MainWindowTests
    {
        /// <summary>The EditorPrefs key holding the project-wide MQTT broker address.</summary>
        private const string BrokerAddressKey = "SollertiaVR_MQTT_IP";

        /// <summary>The EditorPrefs key holding the project-wide MQTT broker port.</summary>
        private const string BrokerPortKey = "SollertiaVR_MQTT_Port";

        /// <summary>The address the window falls back to when no broker address is stored.</summary>
        private const string LoopbackAddress = "127.0.0.1";

        /// <summary>The port the window falls back to when no broker port is stored.</summary>
        private const int DefaultBrokerPort = 1883;

        /// <summary>The broker address a test stores in EditorPrefs when it exercises the non-empty path.</summary>
        private const string StoredAddress = "192.168.10.20";

        /// <summary>The broker port a test stores in EditorPrefs when it exercises the non-zero path.</summary>
        private const int StoredPort = 1884;

        /// <summary>The objects a single test created, destroyed once that test completes.</summary>
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        /// <summary>The window under test, created on first demand and shared by the later tests.</summary>
        private MainWindow _window;

        /// <summary>The asset path of the scene the editor had open before the fixture ran.</summary>
        private string _originalScenePath;

        /// <summary>Determines whether EditorPrefs held a broker address before the fixture ran.</summary>
        private bool _hadStoredAddress;

        /// <summary>The broker address EditorPrefs held before the fixture ran.</summary>
        private string _previousAddress;

        /// <summary>Determines whether EditorPrefs held a broker port before the fixture ran.</summary>
        private bool _hadStoredPort;

        /// <summary>The broker port EditorPrefs held before the fixture ran.</summary>
        private int _previousPort;

        /// <summary>Captures the editor preferences and the open scene the fixture overwrites.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            _hadStoredAddress = EditorPrefs.HasKey(BrokerAddressKey);
            _previousAddress = EditorPrefs.GetString(BrokerAddressKey);
            _hadStoredPort = EditorPrefs.HasKey(BrokerPortKey);
            _previousPort = EditorPrefs.GetInt(BrokerPortKey);
        }

        /// <summary>Destroys the shared window and restores the captured preferences and scene.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_window != null)
            {
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (_hadStoredAddress)
            {
                EditorPrefs.SetString(BrokerAddressKey, _previousAddress);
            }
            else
            {
                EditorPrefs.DeleteKey(BrokerAddressKey);
            }

            if (_hadStoredPort)
            {
                EditorPrefs.SetInt(BrokerPortKey, _previousPort);
            }
            else
            {
                EditorPrefs.DeleteKey(BrokerPortKey);
            }

            RestoreOriginalScene();
        }

        /// <summary>Opens a fresh empty scene and clears the state the shared window carries between tests.</summary>
        [SetUp]
        public void SetUp()
        {
            OpenEmptyScene();
            if (_window != null)
            {
                PrivateAccess.SetField(_window, "_exitPlayModeSceneChangeComing", false);
                PrivateAccess.Invoke(_window, "InvalidateSceneCache");
            }
        }

        /// <summary>Destroys every object the completed test created.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }
            _createdObjects.Clear();
        }

        /// <summary>Verifies that BuildControllerSpecs resolves exactly one spec per ControllerTypes member.</summary>
        [Test]
        public void BuildControllerSpecs_EveryEnumMember_ResolvesOneSpecPerMember()
        {
            (string DisplayName, Type ControllerType)[] specs = BuildSpecs();

            Assert.AreEqual(Enum.GetValues(typeof(ControllerTypes)).Length, specs.Length);
            Assert.AreEqual(2, specs.Length);
            Assert.IsNotNull(specs[0].ControllerType);
            Assert.IsNotNull(specs[1].ControllerType);
        }

        /// <summary>Verifies that BuildControllerSpecs maps the LinearTreadmill member to the Linear label.</summary>
        [Test]
        public void BuildControllerSpecs_LinearTreadmillMember_ResolvesLinearLabelAndConcreteType()
        {
            (string DisplayName, Type ControllerType)[] specs = BuildSpecs();

            Assert.AreEqual("Linear", specs[(int)ControllerTypes.LinearTreadmill].DisplayName);
            Assert.AreEqual(typeof(LinearTreadmill), specs[(int)ControllerTypes.LinearTreadmill].ControllerType);
        }

        /// <summary>Verifies that BuildControllerSpecs maps the simulated member to its two-word label.</summary>
        [Test]
        public void BuildControllerSpecs_SimulatedMember_ResolvesSimulatedLinearLabelAndConcreteType()
        {
            (string DisplayName, Type ControllerType)[] specs = BuildSpecs();
            int simulatedIndex = (int)ControllerTypes.SimulatedLinearTreadmill;

            Assert.AreEqual("Simulated Linear", specs[simulatedIndex].DisplayName);
            Assert.AreEqual(typeof(SimulatedLinearTreadmill), specs[simulatedIndex].ControllerType);
        }

        /// <summary>Verifies that the cached spec table matches a freshly built one entry for entry.</summary>
        [Test]
        public void CachedControllerSpecs_TypeInitialization_MatchesFreshlyBuiltSpecs()
        {
            (string DisplayName, Type ControllerType)[] cached = CachedSpecs();
            (string DisplayName, Type ControllerType)[] rebuilt = BuildSpecs();

            Assert.AreEqual(rebuilt.Length, cached.Length);
            for (int specIndex = 0; specIndex < rebuilt.Length; specIndex++)
            {
                Assert.AreEqual(rebuilt[specIndex].DisplayName, cached[specIndex].DisplayName);
                Assert.AreEqual(rebuilt[specIndex].ControllerType, cached[specIndex].ControllerType);
            }
        }

        /// <summary>Verifies that EnsureControllers creates nothing when the scene lacks a Controllers root.</summary>
        [Test]
        public void EnsureControllers_NoControllersRoot_CreatesNoControllers()
        {
            MainWindow.EnsureControllers();

            Assert.AreEqual(0, SceneControllers().Length);
            Assert.AreEqual(0, SceneControllerOutputs().Length);
        }

        /// <summary>Verifies that EnsureControllers creates one wired controller per cached spec.</summary>
        [Test]
        public void EnsureControllers_EmptyControllersRoot_CreatesOneControllerPerSpec()
        {
            GameObject controllersRoot = Track(new GameObject("Controllers"));

            MainWindow.EnsureControllers();

            ControllerObject[] controllers = SceneControllers();
            Assert.AreEqual(2, controllers.Length);
            ControllerObject linear = controllers.First(controller => controller.GetType() == typeof(LinearTreadmill));
            ControllerObject simulated = controllers.First(controller =>
                controller.GetType() == typeof(SimulatedLinearTreadmill)
            );
            Assert.AreEqual("Linear", linear.gameObject.name);
            Assert.AreEqual("Simulated Linear", simulated.gameObject.name);
            Assert.AreEqual(controllersRoot.transform, linear.transform.parent);
            Assert.AreEqual(controllersRoot.transform, simulated.transform.parent);
            Assert.AreEqual(linear, linear.GetComponent<ControllerOutput>().master);
            Assert.AreEqual(simulated, simulated.GetComponent<ControllerOutput>().master);
        }

        /// <summary>Verifies that a second EnsureControllers call adds no further controllers.</summary>
        [Test]
        public void EnsureControllers_SecondCall_CreatesNoAdditionalControllers()
        {
            Track(new GameObject("Controllers"));
            MainWindow.EnsureControllers();
            ControllerObject[] firstPass = SceneControllers();

            MainWindow.EnsureControllers();

            ControllerObject[] secondPass = SceneControllers();
            Assert.AreEqual(2, firstPass.Length);
            Assert.AreEqual(2, secondPass.Length);
            Assert.AreEqual(2, SceneControllerOutputs().Length);
        }

        /// <summary>Verifies that an existing simulated controller only suppresses its own exact type.</summary>
        [Test]
        public void EnsureControllers_ExistingSimulatedController_CreatesOnlyTheBaseController()
        {
            Track(new GameObject("Controllers"));
            SimulatedLinearTreadmill existing = CreateComponent<SimulatedLinearTreadmill>("Existing Simulated");

            MainWindow.EnsureControllers();

            ControllerObject[] controllers = SceneControllers();
            Assert.AreEqual(2, controllers.Length);
            Assert.AreEqual(1, controllers.Count(controller => controller.GetType() == typeof(LinearTreadmill)));
            Assert.AreEqual(
                1,
                controllers.Count(controller => controller.GetType() == typeof(SimulatedLinearTreadmill))
            );
            Assert.AreEqual("Existing Simulated", existing.gameObject.name);
            Assert.AreEqual(1, SceneControllerOutputs().Length);
        }

        /// <summary>Verifies that an existing base controller leaves only the simulated controller missing.</summary>
        [Test]
        public void EnsureControllers_ExistingBaseController_CreatesOnlyTheSimulatedController()
        {
            Track(new GameObject("Controllers"));
            LinearTreadmill existing = CreateComponent<LinearTreadmill>("Existing Linear");

            MainWindow.EnsureControllers();

            ControllerObject[] controllers = SceneControllers();
            Assert.AreEqual(2, controllers.Length);
            Assert.AreEqual(1, controllers.Count(controller => controller.GetType() == typeof(LinearTreadmill)));
            Assert.AreEqual(
                1,
                controllers.Count(controller => controller.GetType() == typeof(SimulatedLinearTreadmill))
            );
            Assert.AreEqual("Existing Linear", existing.gameObject.name);
            Assert.AreEqual(1, SceneControllerOutputs().Length);
        }

        /// <summary>Verifies that EnsureMqttDefaults ignores a client whose object carries another name.</summary>
        [Test]
        public void EnsureMqttDefaults_NoMqttClientObject_LeavesTheOtherClientUntouched()
        {
            StoreBrokerPreferences(StoredAddress, StoredPort);
            MQTTClient client = CreateComponent<MQTTClient>("Broker");
            client.ipAddress = "10.20.30.40";
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual("10.20.30.40", client.ipAddress);
            Assert.AreEqual(9999, client.port);
        }

        /// <summary>Verifies that EnsureMqttDefaults ignores a named object that carries no client component.</summary>
        [Test]
        public void EnsureMqttDefaults_MqttClientObjectWithoutComponent_LeavesTheOtherClientUntouched()
        {
            StoreBrokerPreferences(StoredAddress, StoredPort);
            Track(new GameObject("MQTT Client"));
            MQTTClient client = CreateComponent<MQTTClient>("Broker");
            client.ipAddress = "10.20.30.40";
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual("10.20.30.40", client.ipAddress);
            Assert.AreEqual(9999, client.port);
        }

        /// <summary>Verifies that EnsureMqttDefaults applies both loopback fallbacks when no value is stored.</summary>
        [Test]
        public void EnsureMqttDefaults_UnsetPreferences_AppliesTheLoopbackDefaults()
        {
            ClearBrokerPreferences();
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.ipAddress = "10.20.30.40";
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(LoopbackAddress, client.ipAddress);
            Assert.AreEqual(DefaultBrokerPort, client.port);
        }

        /// <summary>Verifies that an empty stored address falls back while the stored port survives.</summary>
        [Test]
        public void EnsureMqttDefaults_EmptyStoredAddress_AppliesTheLoopbackAddressOnly()
        {
            StoreBrokerPreferences(string.Empty, StoredPort);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.ipAddress = "10.20.30.40";

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(LoopbackAddress, client.ipAddress);
            Assert.AreEqual(StoredPort, client.port);
        }

        /// <summary>Verifies that a stored address and port reach the client verbatim.</summary>
        [Test]
        public void EnsureMqttDefaults_StoredAddressAndPort_AppliesTheStoredValues()
        {
            StoreBrokerPreferences(StoredAddress, StoredPort);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.ipAddress = "10.20.30.40";
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(StoredAddress, client.ipAddress);
            Assert.AreEqual(StoredPort, client.port);
        }

        /// <summary>Verifies that a stored port of zero falls back while the stored address survives.</summary>
        [Test]
        public void EnsureMqttDefaults_ZeroStoredPort_AppliesTheDefaultPortOnly()
        {
            StoreBrokerPreferences(StoredAddress, 0);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(StoredAddress, client.ipAddress);
            Assert.AreEqual(DefaultBrokerPort, client.port);
        }

        /// <summary>Verifies that the smallest non-zero stored port survives the synchronization.</summary>
        [Test]
        public void EnsureMqttDefaults_StoredPortOfOne_PreservesTheStoredPort()
        {
            StoreBrokerPreferences(StoredAddress, 1);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(1, client.port);
        }

        /// <summary>Verifies that a negative stored port survives the synchronization.</summary>
        [Test]
        public void EnsureMqttDefaults_NegativeStoredPort_PreservesTheStoredPort()
        {
            StoreBrokerPreferences(StoredAddress, -1);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            client.port = 9999;

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(-1, client.port);
        }

        /// <summary>Verifies that a second EnsureMqttDefaults call leaves the applied values unchanged.</summary>
        [Test]
        public void EnsureMqttDefaults_SecondCall_LeavesTheAppliedValuesUnchanged()
        {
            StoreBrokerPreferences(StoredAddress, StoredPort);
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");
            MainWindow.EnsureMqttDefaults();

            MainWindow.EnsureMqttDefaults();

            Assert.AreEqual(StoredAddress, client.ipAddress);
            Assert.AreEqual(StoredPort, client.port);
        }

        /// <summary>Verifies that SyncDisplayBrightnessToSettings tolerates a scene without a display.</summary>
        [Test]
        public void SyncDisplayBrightnessToSettings_NoDisplayInScene_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MainWindow.SyncDisplayBrightnessToSettings());
        }

        /// <summary>Verifies that a display without a settings asset keeps its current brightness.</summary>
        [Test]
        public void SyncDisplayBrightnessToSettings_MissingSettings_LeavesTheCurrentBrightness()
        {
            DisplayObject display = CreateDisplayWithoutSettings(12.5f);

            MainWindow.SyncDisplayBrightnessToSettings();

            Assert.AreEqual(12.5f, display.currentBrightness, 0f);
        }

        /// <summary>Verifies that a display whose brightness differs adopts the settings brightness.</summary>
        [Test]
        public void SyncDisplayBrightnessToSettings_BrightnessDiffers_AdoptsTheSettingsBrightness()
        {
            DisplayObject display = CreateDisplayWithSettings(currentBrightness: 100f, settingsBrightness: 75f);

            MainWindow.SyncDisplayBrightnessToSettings();

            Assert.AreEqual(75f, display.currentBrightness, 0f);
        }

        /// <summary>Verifies that a brightness inside the approximate-equality band is left alone.</summary>
        [Test]
        public void SyncDisplayBrightnessToSettings_ApproximatelyEqualBrightness_LeavesTheCurrentBrightness()
        {
            DisplayObject display = CreateDisplayWithSettings(currentBrightness: 50.00001f, settingsBrightness: 50f);

            MainWindow.SyncDisplayBrightnessToSettings();

            Assert.AreEqual(50.00001f, display.currentBrightness, 0f);
            Assert.AreNotEqual(50f, display.currentBrightness);
        }

        /// <summary>Verifies that an already matching brightness survives the synchronization unchanged.</summary>
        [Test]
        public void SyncDisplayBrightnessToSettings_EqualBrightness_LeavesTheCurrentBrightness()
        {
            DisplayObject display = CreateDisplayWithSettings(currentBrightness: 50f, settingsBrightness: 50f);

            MainWindow.SyncDisplayBrightnessToSettings();

            Assert.AreEqual(50f, display.currentBrightness, 0f);
        }

        /// <summary>Verifies that a camera that is neither tagged nor named as the default survives.</summary>
        [Test]
        public void RemoveDefaultMainCamera_UnrelatedSceneCamera_LeavesItInTheScene()
        {
            Camera sceneCamera = CreateComponent<Camera>("Gameplay Camera");

            PrivateAccess.InvokeStatic(typeof(MainWindow), "RemoveDefaultMainCamera");

            Assert.IsTrue(sceneCamera != null);
            Assert.IsNotNull(GameObject.Find("Gameplay Camera"));
        }

        /// <summary>Verifies that a camera named Main Camera is removed even while it carries no tag.</summary>
        [Test]
        public void RemoveDefaultMainCamera_CameraNamedMainCamera_RemovesIt()
        {
            Camera defaultCamera = CreateComponent<Camera>("Main Camera");

            PrivateAccess.InvokeStatic(typeof(MainWindow), "RemoveDefaultMainCamera");

            Assert.IsTrue(defaultCamera == null);
            Assert.IsNull(GameObject.Find("Main Camera"));
        }

        /// <summary>Verifies that a camera tagged MainCamera is removed even under another object name.</summary>
        [Test]
        public void RemoveDefaultMainCamera_CameraTaggedMainCamera_RemovesIt()
        {
            Camera taggedCamera = CreateComponent<Camera>("Primary Camera");
            taggedCamera.gameObject.tag = "MainCamera";

            PrivateAccess.InvokeStatic(typeof(MainWindow), "RemoveDefaultMainCamera");

            Assert.IsTrue(taggedCamera == null);
            Assert.IsNull(GameObject.Find("Primary Camera"));
        }

        /// <summary>Verifies that an object named Main Camera without a camera component is left in place.</summary>
        [Test]
        public void RemoveDefaultMainCamera_NamedObjectWithoutCamera_LeavesEveryObjectInPlace()
        {
            GameObject namedObject = Track(new GameObject("Main Camera"));
            Camera sceneCamera = CreateComponent<Camera>("Gameplay Camera");

            PrivateAccess.InvokeStatic(typeof(MainWindow), "RemoveDefaultMainCamera");

            Assert.IsTrue(namedObject != null);
            Assert.IsTrue(sceneCamera != null);
            Assert.IsNotNull(GameObject.Find("Main Camera"));
        }

        /// <summary>Verifies that RegisterAutoOpen subscribes no deferred open hook in a batch-mode editor.</summary>
        [Test]
        public void RegisterAutoOpen_BatchModeEditor_RegistersNoDeferredOpenHook()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore(
                    "Outside batch mode RegisterAutoOpen subscribes permanent editor hooks that would open the "
                        + "window during later tests, so only the guarded batch-mode path is exercised."
                );
            }
            Delegate hooksBeforeRegistration = ReadDelayCallHooks();

            PrivateAccess.InvokeStatic(typeof(MainWindow), "RegisterAutoOpen");

            Assert.AreEqual(hooksBeforeRegistration, ReadDelayCallHooks());
        }

        /// <summary>Verifies that InitializeScene populates an empty scene with the default object set.</summary>
        [Test]
        public void InitializeScene_EmptyScene_CreatesTheDefaultSceneObjects()
        {
            MainWindow window = RequireWindow();

            PrivateAccess.Invoke(window, "InitializeScene");

            Assert.IsNotNull(GameObject.Find("Actors"));
            Assert.IsNotNull(GameObject.Find("Controllers"));
            GameObject clientObject = GameObject.Find("MQTT Client");
            Assert.IsNotNull(clientObject);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<ActorObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<DisplayObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(2, SceneControllers().Length);
            Assert.AreEqual(2, SceneControllerOutputs().Length);
            Assert.AreEqual(
                clientObject.GetComponent<MQTTClient>(),
                PrivateAccess.GetField<MQTTClient>(window, "_client")
            );
        }

        /// <summary>Verifies that InitializeScene hides only the MQTT Client object from the hierarchy.</summary>
        [Test]
        public void InitializeScene_EmptyScene_HidesOnlyTheMqttClientObject()
        {
            MainWindow window = RequireWindow();

            PrivateAccess.Invoke(window, "InitializeScene");

            Assert.AreEqual(HideFlags.HideInHierarchy, GameObject.Find("MQTT Client").hideFlags);
            Assert.AreEqual(HideFlags.None, GameObject.Find("Controllers").hideFlags);
            Assert.AreEqual(HideFlags.None, GameObject.Find("Actors").hideFlags);
        }

        /// <summary>Verifies that a second InitializeScene call duplicates none of the default objects.</summary>
        [Test]
        public void InitializeScene_SecondCall_CreatesNoDuplicateObjects()
        {
            MainWindow window = RequireWindow();
            PrivateAccess.Invoke(window, "InitializeScene");
            GameObject firstActorsRoot = GameObject.Find("Actors");

            PrivateAccess.Invoke(window, "InitializeScene");

            Assert.AreEqual(firstActorsRoot, GameObject.Find("Actors"));
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<ActorObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<DisplayObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<MQTTClient>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(2, SceneControllers().Length);
        }

        /// <summary>Verifies that InitializeScene applies the stored broker settings to the created client.</summary>
        [Test]
        public void InitializeScene_StoredBrokerPreferences_AppliesThemToTheCreatedClient()
        {
            MainWindow window = RequireWindow();
            StoreBrokerPreferences(StoredAddress, StoredPort);

            PrivateAccess.Invoke(window, "InitializeScene");

            MQTTClient client = GameObject.Find("MQTT Client").GetComponent<MQTTClient>();
            Assert.AreEqual(StoredAddress, client.ipAddress);
            Assert.AreEqual(StoredPort, client.port);
        }

        /// <summary>Verifies that EnsureActorAndDisplay creates one actor and one display, wired together.</summary>
        [Test]
        public void EnsureActorAndDisplay_EmptyScene_CreatesTheLinkedActorAndDisplay()
        {
            MainWindow window = RequireWindow();
            Track(new GameObject("Actors"));

            PrivateAccess.Invoke(window, "EnsureActorAndDisplay");

            ActorObject[] actors = UnityEngine.Object.FindObjectsByType<ActorObject>(FindObjectsSortMode.None);
            DisplayObject[] displays = UnityEngine.Object.FindObjectsByType<DisplayObject>(FindObjectsSortMode.None);
            Assert.AreEqual(1, actors.Length);
            Assert.AreEqual(1, displays.Length);
            Assert.AreEqual("Actor", actors[0].gameObject.name);
            Assert.AreEqual("Display", displays[0].gameObject.name);
            Assert.AreEqual(displays[0], actors[0].Display);
            Assert.AreEqual(actors[0].transform, displays[0].transform.parent);
        }

        /// <summary>Verifies that a second EnsureActorAndDisplay call creates no second actor or display.</summary>
        [Test]
        public void EnsureActorAndDisplay_SecondCall_KeepsOneActorAndOneDisplay()
        {
            MainWindow window = RequireWindow();
            Track(new GameObject("Actors"));
            PrivateAccess.Invoke(window, "EnsureActorAndDisplay");

            PrivateAccess.Invoke(window, "EnsureActorAndDisplay");

            ActorObject[] actors = UnityEngine.Object.FindObjectsByType<ActorObject>(FindObjectsSortMode.None);
            DisplayObject[] displays = UnityEngine.Object.FindObjectsByType<DisplayObject>(FindObjectsSortMode.None);
            Assert.AreEqual(1, actors.Length);
            Assert.AreEqual(1, displays.Length);
            Assert.AreEqual(displays[0], actors[0].Display);
        }

        /// <summary>Verifies that EnsureActorAndDisplay links the pre-existing actor and display in place.</summary>
        [Test]
        public void EnsureActorAndDisplay_ExistingActorAndDisplay_LinksThemWithoutCreatingMore()
        {
            MainWindow window = RequireWindow();
            ActorObject actor = CreateComponent<ActorObject>("ExistingActor");
            DisplayObject display = CreateComponent<DisplayObject>("ExistingDisplay");

            PrivateAccess.Invoke(window, "EnsureActorAndDisplay");

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<ActorObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<DisplayObject>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(display, actor.Display);
            Assert.AreEqual(actor.transform, display.transform.parent);
        }

        /// <summary>Verifies that GetCachedTask resolves the scene's Task and stores it in the cache.</summary>
        [Test]
        public void GetCachedTask_TaskInScene_ReturnsAndCachesTheSceneTask()
        {
            MainWindow window = RequireWindow();
            Task task = CreateComponent<Task>("Task");

            Task resolved = (Task)PrivateAccess.Invoke(window, "GetCachedTask");

            Assert.AreEqual(task, resolved);
            Assert.AreEqual(task, PrivateAccess.GetField<Task>(window, "_cachedTask"));
        }

        /// <summary>Verifies that GetCachedTask returns the cached instance without re-querying the scene.</summary>
        [Test]
        public void GetCachedTask_PopulatedCache_ReturnsTheCachedInstance()
        {
            MainWindow window = RequireWindow();
            Task sceneTask = CreateComponent<Task>("SceneTask");
            GameObject cachedHost = Track(new GameObject("CachedTask"));
            cachedHost.SetActive(false);
            Task cachedTask = cachedHost.AddComponent<Task>();
            PrivateAccess.SetField(window, "_cachedTask", cachedTask);

            Task resolved = (Task)PrivateAccess.Invoke(window, "GetCachedTask");

            Assert.AreEqual(cachedTask, resolved);
            Assert.AreNotEqual(sceneTask, resolved);
        }

        /// <summary>Verifies that GetCachedTask re-resolves the scene Task after the cache is invalidated.</summary>
        [Test]
        public void GetCachedTask_AfterInvalidateSceneCache_ResolvesTheReplacementTask()
        {
            MainWindow window = RequireWindow();
            Task firstTask = CreateComponent<Task>("FirstTask");
            PrivateAccess.Invoke(window, "GetCachedTask");
            Task secondTask = CreateComponent<Task>("SecondTask");
            Assert.AreEqual(firstTask, (Task)PrivateAccess.Invoke(window, "GetCachedTask"));
            firstTask.gameObject.SetActive(false);

            PrivateAccess.Invoke(window, "InvalidateSceneCache");

            Assert.AreEqual(secondTask, (Task)PrivateAccess.Invoke(window, "GetCachedTask"));
        }

        /// <summary>Verifies that InvalidateSceneCache clears every cached per-scene reference.</summary>
        [Test]
        public void InvalidateSceneCache_PopulatedCaches_ClearsEveryReference()
        {
            MainWindow window = RequireWindow();
            PrivateAccess.SetField(window, "_client", CreateComponent<MQTTClient>("MQTT Client"));
            PrivateAccess.SetField(window, "_cachedTask", CreateComponent<Task>("Task"));
            PrivateAccess.SetField(window, "_cachedActor", CreateComponent<ActorObject>("Actor"));
            PrivateAccess.SetField(window, "_cachedDisplay", CreateComponent<DisplayObject>("Display"));
            PrivateAccess.SetField(window, "_cachedGuidanceZone", CreateComponent<GuidanceZone>("GuidanceRegion"));
            PrivateAccess.SetField(window, "_cachedOccupancyZone", CreateComponent<OccupancyZone>("OccupancyRegion"));

            PrivateAccess.Invoke(window, "InvalidateSceneCache");

            Assert.IsNull(PrivateAccess.GetField<MQTTClient>(window, "_client"));
            Assert.IsNull(PrivateAccess.GetField<Task>(window, "_cachedTask"));
            Assert.IsNull(PrivateAccess.GetField<ActorObject>(window, "_cachedActor"));
            Assert.IsNull(PrivateAccess.GetField<DisplayObject>(window, "_cachedDisplay"));
            Assert.IsNull(PrivateAccess.GetField<GuidanceZone>(window, "_cachedGuidanceZone"));
            Assert.IsNull(PrivateAccess.GetField<OccupancyZone>(window, "_cachedOccupancyZone"));
        }

        /// <summary>Verifies that GetCachedActor resolves the scene's actor and stores it in the cache.</summary>
        [Test]
        public void GetCachedActor_ActorInScene_ReturnsAndCachesTheSceneActor()
        {
            MainWindow window = RequireWindow();
            ActorObject actor = CreateComponent<ActorObject>("Actor");

            ActorObject resolved = (ActorObject)PrivateAccess.Invoke(window, "GetCachedActor");

            Assert.AreEqual(actor, resolved);
            Assert.AreEqual(actor, PrivateAccess.GetField<ActorObject>(window, "_cachedActor"));
        }

        /// <summary>Verifies that GetCachedDisplay resolves the scene's display and stores it in the cache.</summary>
        [Test]
        public void GetCachedDisplay_DisplayInScene_ReturnsAndCachesTheSceneDisplay()
        {
            MainWindow window = RequireWindow();
            DisplayObject display = CreateComponent<DisplayObject>("Display");

            DisplayObject resolved = (DisplayObject)PrivateAccess.Invoke(window, "GetCachedDisplay");

            Assert.AreEqual(display, resolved);
            Assert.AreEqual(display, PrivateAccess.GetField<DisplayObject>(window, "_cachedDisplay"));
        }

        /// <summary>Verifies that GetCachedGuidanceZone resolves the scene's guidance zone.</summary>
        [Test]
        public void GetCachedGuidanceZone_GuidanceZoneInScene_ReturnsAndCachesTheSceneZone()
        {
            MainWindow window = RequireWindow();
            GuidanceZone zone = CreateComponent<GuidanceZone>("GuidanceRegion");

            GuidanceZone resolved = (GuidanceZone)PrivateAccess.Invoke(window, "GetCachedGuidanceZone");

            Assert.AreEqual(zone, resolved);
            Assert.AreEqual(zone, PrivateAccess.GetField<GuidanceZone>(window, "_cachedGuidanceZone"));
        }

        /// <summary>Verifies that GetCachedOccupancyZone resolves the scene's occupancy zone.</summary>
        [Test]
        public void GetCachedOccupancyZone_OccupancyZoneInScene_ReturnsAndCachesTheSceneZone()
        {
            MainWindow window = RequireWindow();
            OccupancyZone zone = CreateComponent<OccupancyZone>("OccupancyRegion");

            OccupancyZone resolved = (OccupancyZone)PrivateAccess.Invoke(window, "GetCachedOccupancyZone");

            Assert.AreEqual(zone, resolved);
            Assert.AreEqual(zone, PrivateAccess.GetField<OccupancyZone>(window, "_cachedOccupancyZone"));
        }

        /// <summary>Verifies that GetCachedClient resolves the scene's MQTT client and caches it.</summary>
        [Test]
        public void GetCachedClient_ClientInScene_ReturnsAndCachesTheSceneClient()
        {
            MainWindow window = RequireWindow();
            MQTTClient client = CreateComponent<MQTTClient>("MQTT Client");

            MQTTClient resolved = (MQTTClient)PrivateAccess.Invoke(window, "GetCachedClient");

            Assert.AreEqual(client, resolved);
            Assert.AreEqual(client, PrivateAccess.GetField<MQTTClient>(window, "_client"));
        }

        /// <summary>Verifies that leaving Play Mode arms the deferred scene-change flag.</summary>
        [Test]
        public void OnPlayModeStateChanged_ExitingPlayMode_ArmsTheDeferredSceneChangeFlag()
        {
            MainWindow window = RequireWindow();

            PrivateAccess.Invoke(window, "OnPlayModeStateChanged", PlayModeStateChange.ExitingPlayMode);

            Assert.IsTrue(PrivateAccess.GetField<bool>(window, "_exitPlayModeSceneChangeComing"));
        }

        /// <summary>Verifies that entering Edit Mode leaves the deferred scene-change flag unset.</summary>
        [Test]
        public void OnPlayModeStateChanged_EnteredEditMode_LeavesTheDeferredSceneChangeFlagUnset()
        {
            MainWindow window = RequireWindow();

            PrivateAccess.Invoke(window, "OnPlayModeStateChanged", PlayModeStateChange.EnteredEditMode);

            Assert.IsFalse(PrivateAccess.GetField<bool>(window, "_exitPlayModeSceneChangeComing"));
        }

        /// <summary>Verifies that the deferred Play Mode swap clears the flag without reloading cameras.</summary>
        [Test]
        public void OnActiveSceneChanged_DeferredPlayModeSwap_ClearsTheFlagWithoutReloadingCameras()
        {
            MainWindow window = RequireWindow();
            FullScreenViewsSaved sentinel = Track(ScriptableObject.CreateInstance<FullScreenViewsSaved>());
            PrivateAccess.SetField(window.fullScreenManager, "_savedFullScreenViews", sentinel);
            PrivateAccess.SetField(window, "_exitPlayModeSceneChangeComing", true);
            Scene activeScene = SceneManager.GetActiveScene();

            PrivateAccess.Invoke(window, "OnActiveSceneChanged", activeScene, activeScene);

            Assert.IsFalse(PrivateAccess.GetField<bool>(window, "_exitPlayModeSceneChangeComing"));
            FullScreenViewsSaved retained = PrivateAccess.GetField<FullScreenViewsSaved>(
                window.fullScreenManager,
                "_savedFullScreenViews"
            );
            Assert.AreSame(sentinel, retained);
        }

        /// <summary>Verifies that an ordinary active-scene change reloads the camera assignments.</summary>
        [Test]
        public void OnActiveSceneChanged_NoDeferredPlayModeSwap_ReloadsTheCameraAssignments()
        {
            MainWindow window = RequireWindow();
            FullScreenViewsSaved sentinel = Track(ScriptableObject.CreateInstance<FullScreenViewsSaved>());
            PrivateAccess.SetField(window.fullScreenManager, "_savedFullScreenViews", sentinel);
            PrivateAccess.SetField(window, "_exitPlayModeSceneChangeComing", false);
            Scene activeScene = SceneManager.GetActiveScene();

            PrivateAccess.Invoke(window, "OnActiveSceneChanged", activeScene, activeScene);

            FullScreenViewsSaved reloaded = PrivateAccess.GetField<FullScreenViewsSaved>(
                window.fullScreenManager,
                "_savedFullScreenViews"
            );
            Assert.AreNotSame(sentinel, reloaded);
        }

        /// <summary>Verifies that an active-scene change invalidates the cached per-scene references.</summary>
        [Test]
        public void OnActiveSceneChanged_PopulatedCaches_InvalidatesThem()
        {
            MainWindow window = RequireWindow();
            PrivateAccess.SetField(window, "_cachedTask", CreateComponent<Task>("Task"));
            Scene activeScene = SceneManager.GetActiveScene();

            PrivateAccess.Invoke(window, "OnActiveSceneChanged", activeScene, activeScene);

            Assert.IsNull(PrivateAccess.GetField<Task>(window, "_cachedTask"));
        }

        /// <summary>Returns the shared window, creating it against a scene the caller has not populated yet.</summary>
        /// <remarks>
        /// Creating the window runs its OnEnable, which initializes the active scene, so the call replaces the scene
        /// afterwards to hand the caller the empty scene it expects. Every test that needs the window therefore
        /// requests it before it creates any object of its own. OnEnable also builds a
        /// <see cref="FullScreenViewManager"/>, whose monitor enumeration opens one short-lived popup window per
        /// detected monitor, and a headless editor logs a graphics-device error for each. The construction therefore
        /// runs with log failures suppressed and restores the setting before the caller resumes.
        /// </remarks>
        /// <returns>The window instance shared by the fixture.</returns>
        private MainWindow RequireWindow()
        {
            if (_window == null)
            {
                bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    _window = ScriptableObject.CreateInstance<MainWindow>();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnoreSetting;
                }
                OpenEmptyScene();
                PrivateAccess.Invoke(_window, "InvalidateSceneCache");
            }
            return _window;
        }

        /// <summary>Builds a fresh controller spec table through the private factory under test.</summary>
        private static (string DisplayName, Type ControllerType)[] BuildSpecs()
        {
            return ((string DisplayName, Type ControllerType)[])
                PrivateAccess.InvokeStatic(typeof(MainWindow), "BuildControllerSpecs");
        }

        /// <summary>Reads the controller spec table cached at type initialization.</summary>
        private static (string DisplayName, Type ControllerType)[] CachedSpecs()
        {
            return ((string DisplayName, Type ControllerType)[])
                PrivateAccess.GetStaticField<object>(typeof(MainWindow), "CachedControllerSpecs");
        }

        /// <summary>Returns every active controller component in the loaded scenes.</summary>
        private static ControllerObject[] SceneControllers()
        {
            return UnityEngine.Object.FindObjectsByType<ControllerObject>(FindObjectsSortMode.None);
        }

        /// <summary>Returns every active controller output component in the loaded scenes.</summary>
        private static ControllerOutput[] SceneControllerOutputs()
        {
            return UnityEngine.Object.FindObjectsByType<ControllerOutput>(FindObjectsSortMode.None);
        }

        /// <summary>Reads the multicast delegate holding the editor's pending deferred callbacks.</summary>
        /// <returns>The pending delayCall delegate, which is null when nothing is queued.</returns>
        private static Delegate ReadDelayCallHooks()
        {
            FieldInfo delayCallField = typeof(EditorApplication).GetField(
                "delayCall",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (delayCallField == null || !typeof(Delegate).IsAssignableFrom(delayCallField.FieldType))
            {
                Assert.Ignore(
                    "This editor build does not expose EditorApplication.delayCall as a delegate field, so the "
                        + "deferred open hook registration cannot be observed."
                );
            }
            return (Delegate)delayCallField.GetValue(null);
        }

        /// <summary>Replaces the active scene with a fresh empty one.</summary>
        private static void OpenEmptyScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>Writes both broker preference keys.</summary>
        /// <param name="address">The broker address to store.</param>
        /// <param name="port">The broker port to store.</param>
        private static void StoreBrokerPreferences(string address, int port)
        {
            EditorPrefs.SetString(BrokerAddressKey, address);
            EditorPrefs.SetInt(BrokerPortKey, port);
        }

        /// <summary>Removes both broker preference keys.</summary>
        private static void ClearBrokerPreferences()
        {
            EditorPrefs.DeleteKey(BrokerAddressKey);
            EditorPrefs.DeleteKey(BrokerPortKey);
        }

        /// <summary>Reopens the scene the fixture found open, or leaves a clean empty scene behind.</summary>
        private void RestoreOriginalScene()
        {
            if (!string.IsNullOrEmpty(_originalScenePath) && File.Exists(_originalScenePath))
            {
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
                return;
            }
            OpenEmptyScene();
        }

        /// <summary>Creates a tracked GameObject carrying the requested component.</summary>
        /// <typeparam name="TComponent">The component type to attach to the created object.</typeparam>
        /// <param name="objectName">The name assigned to the created GameObject.</param>
        private TComponent CreateComponent<TComponent>(string objectName)
            where TComponent : Component
        {
            GameObject host = Track(new GameObject(objectName));
            return host.AddComponent<TComponent>();
        }

        /// <summary>Creates a tracked display backed by a tracked settings instance.</summary>
        /// <param name="currentBrightness">The runtime brightness override assigned to the display.</param>
        /// <param name="settingsBrightness">The persisted brightness assigned to the settings instance.</param>
        private DisplayObject CreateDisplayWithSettings(float currentBrightness, float settingsBrightness)
        {
            DisplayObject display = CreateComponent<DisplayObject>("Display");
            DisplaySettings settings = Track(ScriptableObject.CreateInstance<DisplaySettings>());
            settings.brightness = settingsBrightness;
            display.settings = settings;
            display.currentBrightness = currentBrightness;
            return display;
        }

        /// <summary>Creates a tracked display that references no settings instance.</summary>
        /// <param name="currentBrightness">The runtime brightness override assigned to the display.</param>
        private DisplayObject CreateDisplayWithoutSettings(float currentBrightness)
        {
            DisplayObject display = CreateComponent<DisplayObject>("Display");
            display.settings = null;
            display.currentBrightness = currentBrightness;
            return display;
        }

        /// <summary>Registers an object for destruction once the running test completes.</summary>
        /// <typeparam name="TObject">The Unity object type being tracked.</typeparam>
        /// <param name="createdObject">The object the test created.</param>
        private TObject Track<TObject>(TObject createdObject)
            where TObject : UnityEngine.Object
        {
            _createdObjects.Add(createdObject);
            return createdObject;
        }
    }
}
