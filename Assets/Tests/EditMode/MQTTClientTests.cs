/// <summary>
/// Verifies the behavior of the MQTTClient class.
/// </summary>
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gimbl;
using MQTTnet;
using MQTTnet.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the MQTTClient class.</summary>
    /// <remarks>
    /// No test contacts a broker. The singleton installation, the EditorPrefs load with its loopback fallback, the
    /// routing list, and the in-process publish fallback all resolve without a connection, so the lifecycle callbacks
    /// this fixture covers, Awake and OnDestroy, are driven directly through PrivateAccess. Start runs under the player
    /// loop in the Play Mode suite, and OnApplicationQuit is left uncovered.
    /// </remarks>
    [TestFixture]
    public class MQTTClientTests
    {
        /// <summary>The EditorPrefs key holding the configured broker address.</summary>
        private const string AddressPreferenceKey = "SollertiaVR_MQTT_IP";

        /// <summary>The EditorPrefs key holding the configured broker port.</summary>
        private const string PortPreferenceKey = "SollertiaVR_MQTT_Port";

        /// <summary>The topic differing from the Motion topic only by a trailing separator.</summary>
        private const string TrailingSeparatorTopic = MQTTTopics.Motion + "/";

        /// <summary>The topic differing from the Motion topic only by case.</summary>
        private const string LowercaseTopic = "motion";

        /// <summary>The message fragment identifying the duplicate-singleton warning.</summary>
        private const string DuplicateWarningFragment = "Multiple instances found";

        /// <summary>The message fragment identifying the no-broker loopback warning.</summary>
        private const string LoopbackWarningFragment = "broker unreachable";

        /// <summary>The host objects a test created, destroyed once the test finishes.</summary>
        private List<GameObject> _hosts;

        /// <summary>The recorder capturing every warning logged while a test runs.</summary>
        private LogRecorder _recorder;

        /// <summary>Determines whether the editor stored a broker address before the test replaced it.</summary>
        private bool _hadAddressPreference;

        /// <summary>The broker address stored before the test replaced it.</summary>
        private string _savedAddressPreference;

        /// <summary>Determines whether the editor stored a broker port before the test replaced it.</summary>
        private bool _hadPortPreference;

        /// <summary>The broker port stored before the test replaced it.</summary>
        private int _savedPortPreference;

        /// <summary>Clears the singleton, starts a log recorder, and saves the broker preferences before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _hosts = new List<GameObject>();
            _recorder = new LogRecorder();

            _hadAddressPreference = EditorPrefs.HasKey(AddressPreferenceKey);
            _savedAddressPreference = EditorPrefs.GetString(AddressPreferenceKey);
            _hadPortPreference = EditorPrefs.HasKey(PortPreferenceKey);
            _savedPortPreference = EditorPrefs.GetInt(PortPreferenceKey);

            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Clears the singleton, destroys every host object, and restores the broker preferences.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);

            foreach (GameObject host in _hosts)
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
            _hosts.Clear();

            _recorder.Dispose();

            if (_hadAddressPreference)
            {
                EditorPrefs.SetString(AddressPreferenceKey, _savedAddressPreference);
            }
            else
            {
                EditorPrefs.DeleteKey(AddressPreferenceKey);
            }

            if (_hadPortPreference)
            {
                EditorPrefs.SetInt(PortPreferenceKey, _savedPortPreference);
            }
            else
            {
                EditorPrefs.DeleteKey(PortPreferenceKey);
            }
        }

        /// <summary>Verifies that Awake installs the first client as the singleton instance.</summary>
        [Test]
        public void Awake_FirstInstance_InstallsSingleton()
        {
            MQTTClient client = CreateClient("MQTT Client");

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreSame(client, MQTTClient.Instance);
        }

        /// <summary>Verifies that Awake on a second client leaves the first client installed as the singleton.
        /// </summary>
        [Test]
        public void Awake_SecondInstance_LeavesFirstInstanceInstalled()
        {
            MQTTClient firstClient = CreateClient("MQTT Client");
            MQTTClient secondClient = CreateClient("MQTT Client Duplicate");
            PrivateAccess.Invoke(firstClient, "Awake");

            PrivateAccess.Invoke(secondClient, "Awake");

            Assert.AreSame(firstClient, MQTTClient.Instance);
        }

        /// <summary>Verifies that Awake on a second client logs exactly one duplicate-instance warning.</summary>
        [Test]
        public void Awake_SecondInstance_LogsOneDuplicateInstanceWarning()
        {
            MQTTClient firstClient = CreateClient("MQTT Client");
            MQTTClient secondClient = CreateClient("MQTT Client Duplicate");
            PrivateAccess.Invoke(firstClient, "Awake");
            Assert.AreEqual(0, _recorder.CountContaining(DuplicateWarningFragment));

            PrivateAccess.Invoke(secondClient, "Awake");

            Assert.AreEqual(1, _recorder.CountContaining(DuplicateWarningFragment));
        }

        /// <summary>Verifies that Awake on a second client returns before loading the stored connection settings.
        /// </summary>
        [Test]
        public void Awake_SecondInstance_LeavesConnectionSettingsUnloaded()
        {
            EditorPrefs.SetString(AddressPreferenceKey, "192.168.10.20");
            EditorPrefs.SetInt(PortPreferenceKey, 1885);
            MQTTClient firstClient = CreateClient("MQTT Client");
            MQTTClient secondClient = CreateClient("MQTT Client Duplicate");
            PrivateAccess.Invoke(firstClient, "Awake");

            PrivateAccess.Invoke(secondClient, "Awake");

            Assert.AreEqual("127.0.0.1", secondClient.ipAddress);
            Assert.AreEqual(1883, secondClient.port);
        }

        /// <summary>Verifies that Awake applies the broker address and port stored in the editor preferences.
        /// </summary>
        [Test]
        public void Awake_ConfiguredPreferences_AppliesStoredAddressAndPort()
        {
            EditorPrefs.SetString(AddressPreferenceKey, "192.168.10.20");
            EditorPrefs.SetInt(PortPreferenceKey, 1885);
            MQTTClient client = CreateClient("MQTT Client");

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual("192.168.10.20", client.ipAddress);
            Assert.AreEqual(1885, client.port);
        }

        /// <summary>Verifies that Awake falls back to the loopback address and standard port with no stored settings.
        /// </summary>
        [Test]
        public void Awake_UnsetPreferences_FallsBackToLoopbackAddressAndStandardPort()
        {
            EditorPrefs.DeleteKey(AddressPreferenceKey);
            EditorPrefs.DeleteKey(PortPreferenceKey);
            MQTTClient client = CreateClient("MQTT Client");
            client.ipAddress = "broker.example.org";
            client.port = 9999;

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual("127.0.0.1", client.ipAddress);
            Assert.AreEqual(1883, client.port);
        }

        /// <summary>Verifies that Awake falls back to the loopback address while preserving a stored port.</summary>
        [Test]
        public void Awake_EmptyAddressPreference_FallsBackToLoopbackAndPreservesPort()
        {
            EditorPrefs.SetString(AddressPreferenceKey, string.Empty);
            EditorPrefs.SetInt(PortPreferenceKey, 1885);
            MQTTClient client = CreateClient("MQTT Client");
            client.ipAddress = "broker.example.org";

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual("127.0.0.1", client.ipAddress);
            Assert.AreEqual(1885, client.port);
        }

        /// <summary>Verifies that Awake falls back to the standard port while preserving a stored address.</summary>
        [Test]
        public void Awake_ZeroPortPreference_FallsBackToStandardPortAndPreservesAddress()
        {
            EditorPrefs.SetString(AddressPreferenceKey, "10.1.2.3");
            EditorPrefs.SetInt(PortPreferenceKey, 0);
            MQTTClient client = CreateClient("MQTT Client");
            client.port = 9999;

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual("10.1.2.3", client.ipAddress);
            Assert.AreEqual(1883, client.port);
        }

        /// <summary>Verifies that Awake preserves the smallest non-zero port.</summary>
        [Test]
        public void Awake_PortPreferenceOfOne_PreservesStoredPort()
        {
            EditorPrefs.SetString(AddressPreferenceKey, "10.1.2.3");
            EditorPrefs.SetInt(PortPreferenceKey, 1);
            MQTTClient client = CreateClient("MQTT Client");

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual(1, client.port);
        }

        /// <summary>Verifies that Awake preserves a negative stored port, because only zero selects the fallback.
        /// </summary>
        [Test]
        public void Awake_NegativePortPreference_PreservesStoredPort()
        {
            EditorPrefs.SetString(AddressPreferenceKey, "10.1.2.3");
            EditorPrefs.SetInt(PortPreferenceKey, -1);
            MQTTClient client = CreateClient("MQTT Client");

            PrivateAccess.Invoke(client, "Awake");

            Assert.AreEqual(-1, client.port);
        }

        /// <summary>Verifies that the connection attempt budget is ten seconds.</summary>
        [Test]
        public void ConnectTimeoutMilliseconds_Constant_IsTenThousandMilliseconds()
        {
            int timeout = PrivateAccess.GetStaticField<int>(typeof(MQTTClient), "ConnectTimeoutMilliseconds");

            Assert.AreEqual(10000, timeout);
        }

        /// <summary>Verifies that IsConnected reports false while no underlying client exists.</summary>
        [Test]
        public void IsConnected_NullClient_ReturnsFalse()
        {
            MQTTClient client = CreateClient("MQTT Client");

            bool connected = client.IsConnected();

            Assert.IsFalse(connected);
            Assert.IsNull(client.client);
        }

        /// <summary>Verifies that IsConnected reports false for an underlying client that never connected.</summary>
        [Test]
        public void IsConnected_CreatedButUnconnectedClient_ReturnsFalse()
        {
            MQTTClient client = CreateClient("MQTT Client");
            IMqttClient brokerClient = new MqttFactory().CreateMqttClient();
            client.client = brokerClient;

            bool connected = client.IsConnected();

            Assert.IsFalse(connected);
        }

        /// <summary>Verifies that Disconnect on a client holding no handle leaves that handle null.</summary>
        [Test]
        public void Disconnect_NullClient_LeavesHandleNull()
        {
            MQTTClient client = CreateClient("MQTT Client");

            Assert.DoesNotThrow(() => client.Disconnect());

            Assert.IsNull(client.client);
            Assert.IsFalse(client.IsConnected());
        }

        /// <summary>Verifies that Disconnect on an unconnected handle leaves that same handle installed.</summary>
        [Test]
        public void Disconnect_UnconnectedClientHandle_LeavesHandleInPlace()
        {
            MQTTClient client = CreateClient("MQTT Client");
            IMqttClient brokerClient = new MqttFactory().CreateMqttClient();
            client.client = brokerClient;

            client.Disconnect();

            Assert.AreSame(brokerClient, client.client);
            Assert.IsFalse(client.IsConnected());
        }

        /// <summary>Verifies that Subscribe records the channel in the routing list while disconnected.</summary>
        [Test]
        public void Subscribe_WhileDisconnected_AddsChannelToRoutingList()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            RecordingChannel channel = new RecordingChannel(MQTTTopics.Motion, isListener: false);
            Assert.IsFalse(client.IsConnected());

            client.Subscribe(channel, MQTTTopics.Motion, 2);

            IList channels = PrivateAccess.GetField<IList>(client, "_channelList");
            Assert.AreEqual(1, channels.Count);
            Assert.AreEqual(MQTTTopics.Motion, PrivateAccess.GetField<string>(channels[0], "topic"));
            Assert.AreSame(channel, PrivateAccess.GetField<MQTTChannel>(channels[0], "mqttChannel"));
        }

        /// <summary>Verifies that Subscribe records both channels when two subscribe to the same topic.</summary>
        [Test]
        public void Subscribe_SameTopicTwice_AddsBothChannels()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            RecordingChannel firstChannel = new RecordingChannel(MQTTTopics.Motion, isListener: false);
            RecordingChannel secondChannel = new RecordingChannel(MQTTTopics.Motion, isListener: false);

            client.Subscribe(firstChannel, MQTTTopics.Motion, 2);
            client.Subscribe(secondChannel, MQTTTopics.Motion, 2);

            IList channels = PrivateAccess.GetField<IList>(client, "_channelList");
            Assert.AreEqual(2, channels.Count);
            Assert.AreSame(firstChannel, PrivateAccess.GetField<MQTTChannel>(channels[0], "mqttChannel"));
            Assert.AreSame(secondChannel, PrivateAccess.GetField<MQTTChannel>(channels[1], "mqttChannel"));
        }

        /// <summary>Verifies that Publish routes the payload to every subscriber holding the published topic.
        /// </summary>
        [Test]
        public void Publish_MatchingTopic_RoutesPayloadToEverySubscriber()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            RecordingChannel firstChannel = new RecordingChannel(MQTTTopics.Motion);
            RecordingChannel secondChannel = new RecordingChannel(MQTTTopics.Motion);

            client.Publish(MQTTTopics.Motion, Encoding.UTF8.GetBytes("{\"movement\":1.5}"));

            Assert.AreEqual(1, firstChannel.Payloads.Count);
            Assert.AreEqual("{\"movement\":1.5}", firstChannel.Payloads[0]);
            Assert.AreEqual(1, secondChannel.Payloads.Count);
            Assert.AreEqual("{\"movement\":1.5}", secondChannel.Payloads[0]);
        }

        /// <summary>Verifies that Publish skips a subscriber whose topic carries a trailing separator.</summary>
        [Test]
        public void Publish_SubscriberTopicWithTrailingSeparator_RoutesToTheExactSubscriberOnly()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            RecordingChannel exactChannel = new RecordingChannel(MQTTTopics.Motion);
            RecordingChannel separatorChannel = new RecordingChannel(TrailingSeparatorTopic);

            client.Publish(MQTTTopics.Motion, Encoding.UTF8.GetBytes("body"));

            Assert.AreEqual(1, exactChannel.Payloads.Count);
            Assert.AreEqual(0, separatorChannel.Payloads.Count);
        }

        /// <summary>Verifies that Publish skips a subscriber whose topic differs from the published topic by case.
        /// </summary>
        [Test]
        public void Publish_SubscriberTopicDifferingByCase_RoutesToTheExactSubscriberOnly()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            Assert.AreNotEqual(MQTTTopics.Motion, LowercaseTopic);
            Assert.IsTrue(string.Equals(MQTTTopics.Motion, LowercaseTopic, StringComparison.OrdinalIgnoreCase));
            RecordingChannel exactChannel = new RecordingChannel(MQTTTopics.Motion);
            RecordingChannel lowercaseChannel = new RecordingChannel(LowercaseTopic);

            client.Publish(MQTTTopics.Motion, Encoding.UTF8.GetBytes("body"));

            Assert.AreEqual(1, exactChannel.Payloads.Count);
            Assert.AreEqual(0, lowercaseChannel.Payloads.Count);
        }

        /// <summary>Verifies that Publish delivers an empty body for a trigger message carrying no payload.</summary>
        [Test]
        public void Publish_NullPayload_DeliversEmptyBody()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            RecordingChannel channel = new RecordingChannel(MQTTTopics.SessionStart);

            client.Publish(MQTTTopics.SessionStart, null);

            Assert.AreEqual(1, channel.Payloads.Count);
            Assert.AreEqual(string.Empty, channel.Payloads[0]);
        }

        /// <summary>Verifies that the first publish on a topic warns that the broker is unreachable.</summary>
        [Test]
        public void Publish_FirstMessageOnTopic_LogsLoopbackWarning()
        {
            MQTTClient client = CreateClient("MQTT Client");

            client.Publish(MQTTTopics.Motion, null);

            Assert.AreEqual(1, _recorder.CountContaining(LoopbackWarningFragment));
        }

        /// <summary>Verifies that a second publish on an already-warned topic logs no further warning.</summary>
        [Test]
        public void Publish_SecondMessageOnSameTopic_LogsNoAdditionalWarning()
        {
            MQTTClient client = CreateClient("MQTT Client");
            client.Publish(MQTTTopics.Motion, null);

            client.Publish(MQTTTopics.Motion, null);

            Assert.AreEqual(1, _recorder.CountContaining(LoopbackWarningFragment));
        }

        /// <summary>Verifies that a publish on a topic not yet warned about logs its own warning.</summary>
        [Test]
        public void Publish_MessageOnSecondTopic_LogsAnotherWarning()
        {
            MQTTClient client = CreateClient("MQTT Client");
            client.Publish(MQTTTopics.Motion, null);

            client.Publish(MQTTTopics.Stimulus, null);

            Assert.AreEqual(2, _recorder.CountContaining(LoopbackWarningFragment));
        }

        /// <summary>Verifies that the loopback warning ledger records each published topic exactly once.</summary>
        [Test]
        public void Publish_RepeatedTopics_RecordsEachWarnedTopicOnce()
        {
            MQTTClient client = CreateClient("MQTT Client");

            client.Publish(MQTTTopics.Motion, null);
            client.Publish(MQTTTopics.Motion, null);
            client.Publish(MQTTTopics.Stimulus, null);

            HashSet<string> warnedTopics = PrivateAccess.GetField<HashSet<string>>(client, "_loopbackWarnedTopics");
            Assert.AreEqual(2, warnedTopics.Count);
            Assert.IsTrue(warnedTopics.Contains(MQTTTopics.Motion));
            Assert.IsTrue(warnedTopics.Contains(MQTTTopics.Stimulus));
        }

        /// <summary>Verifies that OnDestroy clears the singleton the destroyed client installed.</summary>
        [Test]
        public void OnDestroy_InstalledSingleton_ClearsSingleton()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);

            PrivateAccess.Invoke(client, "OnDestroy");

            Assert.IsNull(MQTTClient.Instance);
        }

        /// <summary>Verifies that OnDestroy releases the underlying client handle.</summary>
        [Test]
        public void OnDestroy_AssignedClient_ClearsClientHandle()
        {
            MQTTClient client = CreateClient("MQTT Client");
            InstallSingleton(client);
            client.client = new MqttFactory().CreateMqttClient();

            PrivateAccess.Invoke(client, "OnDestroy");

            Assert.IsNull(client.client);
        }

        /// <summary>Verifies that OnDestroy leaves a singleton another client installed in place.</summary>
        [Test]
        public void OnDestroy_ForeignSingletonInstalled_LeavesForeignSingletonInstalled()
        {
            MQTTClient installedClient = CreateClient("MQTT Client");
            MQTTClient destroyedClient = CreateClient("MQTT Client Duplicate");
            InstallSingleton(installedClient);

            PrivateAccess.Invoke(destroyedClient, "OnDestroy");

            Assert.AreSame(installedClient, MQTTClient.Instance);
        }

        /// <summary>Creates a client component on a fresh host object registered for teardown.</summary>
        /// <param name="hostName">The name given to the host object.</param>
        /// <returns>The client component the test drives.</returns>
        private MQTTClient CreateClient(string hostName)
        {
            GameObject host = new GameObject(hostName);
            _hosts.Add(host);
            return host.AddComponent<MQTTClient>();
        }

        /// <summary>Installs a client as the singleton without running its Awake callback.</summary>
        /// <param name="client">The client every channel constructed afterwards resolves.</param>
        private void InstallSingleton(MQTTClient client)
        {
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", client);
        }

        /// <summary>Records every payload routed to its topic while preserving the base channel behavior.</summary>
        private sealed class RecordingChannel : MQTTChannel
        {
            /// <summary>The payloads routed to this channel, oldest first.</summary>
            private readonly List<string> _payloads = new List<string>();

            /// <summary>The payloads routed to this channel, oldest first.</summary>
            public IReadOnlyList<string> Payloads => _payloads;

            /// <summary>Creates a channel on a topic, optionally subscribing it to the installed client.</summary>
            /// <param name="topicString">The topic this channel routes.</param>
            /// <param name="isListener">Determines whether the constructor subscribes the channel.</param>
            public RecordingChannel(string topicString, bool isListener = true)
                : base(topicString, isListener) { }

            /// <summary>Records the routed payload, then invokes the base trigger event.</summary>
            /// <param name="messageString">The routed payload string.</param>
            public override void ReceivedMessage(string messageString)
            {
                _payloads.Add(messageString);
                base.ReceivedMessage(messageString);
            }
        }

        /// <summary>Counts the warnings logged while a test runs, so a once-only warning is verifiable.</summary>
        private sealed class LogRecorder : IDisposable
        {
            /// <summary>The warning messages observed since the recorder was created, oldest first.</summary>
            private readonly List<string> _warnings = new List<string>();

            /// <summary>Subscribes the recorder to the Unity log callback.</summary>
            public LogRecorder()
            {
                Application.logMessageReceived += HandleLogMessage;
            }

            /// <summary>Counts the recorded warnings containing a message fragment.</summary>
            /// <param name="fragment">The fragment each counted warning must contain.</param>
            /// <returns>The number of recorded warnings containing the fragment.</returns>
            public int CountContaining(string fragment)
            {
                int count = 0;
                foreach (string warning in _warnings)
                {
                    if (warning.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    {
                        count++;
                    }
                }
                return count;
            }

            /// <summary>Unsubscribes the recorder from the Unity log callback.</summary>
            public void Dispose()
            {
                Application.logMessageReceived -= HandleLogMessage;
            }

            /// <summary>Records a warning message and ignores every other log type.</summary>
            /// <param name="condition">The logged message text.</param>
            /// <param name="stackTrace">The stack trace Unity captured, which the recorder ignores.</param>
            /// <param name="type">The severity of the logged message.</param>
            private void HandleLogMessage(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning)
                {
                    _warnings.Add(condition);
                }
            }
        }
    }
}
