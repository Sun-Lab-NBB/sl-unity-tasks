/// <summary>
/// Verifies the behavior of the MQTTClient, MQTTChannel, MQTTConnectorObject, and LickStimulusSpawner classes under the
/// real Unity player loop.
/// </summary>
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using SL.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.PlayMode
{
    /// <summary>Verifies the MQTT client, channel, connector, and UI spawner behavior under the player loop.</summary>
    /// <remarks>
    /// Every test here needs something Edit Mode cannot supply, meaning Unity itself invoking Awake, OnEnable, Start,
    /// and OnDestroy, a genuine frame boundary between a publish and the Update that consumes it, or the wall-clock
    /// delay the session-start broadcast waits out. No broker participates, so the publish path always takes the
    /// in-process loopback branch and the connector's connection attempt always fails against a closed port.
    /// </remarks>
    [TestFixture]
    public class MqttClientPlayModeTests
    {
        /// <summary>The loopback address the connector tests point the client at.</summary>
        private const string UnreachableBrokerAddress = "127.0.0.1";

        /// <summary>The port the connector tests point the client at, because nothing listens on it.</summary>
        private const int UnreachableBrokerPort = 47999;

        /// <summary>The pattern matching either failure message Connect logs for an unreachable broker.</summary>
        /// <remarks>
        /// The address and port literals mirror <see cref="UnreachableBrokerAddress"/> and
        /// <see cref="UnreachableBrokerPort"/>, and the pattern stops before the exception detail so it matches
        /// both the timeout message and the refused-connection message.
        /// </remarks>
        private const string ConnectionFailurePattern = @"Could not connect to MQTT broker at 127\.0\.0\.1:47999";

        /// <summary>The pattern matching the error the connector logs when no client singleton is installed.</summary>
        private const string MissingInstancePattern = @"MQTTConnectorObject: MQTTClient\.Instance not available";

        /// <summary>The seconds to wait before the session-start delay elapses, staying short of one second.</summary>
        private const float BeforeSessionStartSeconds = 0.5f;

        /// <summary>The seconds to wait for the session-start delay to elapse, clearing one second with margin.
        /// </summary>
        private const float AfterSessionStartSeconds = 1.5f;

        /// <summary>The JSON payload published on the Motion topic by the loopback delivery tests.</summary>
        private const string MotionPayload = "{\"movement\":2.5}";

        /// <summary>The host objects a test created, destroyed once the test finishes.</summary>
        private List<GameObject> _hosts;

        /// <summary>The MQTT harnesses a test created, disposed once the test finishes.</summary>
        private List<MqttTestHarness> _harnesses;

        /// <summary>The client components a test created directly, released once the test finishes.</summary>
        private List<MQTTClient> _clients;

        /// <summary>The harness hosting the client singleton for the spawner rig.</summary>
        private MqttTestHarness _harness;

        /// <summary>The canvas the spawner parents its indicators to.</summary>
        private Canvas _canvas;

        /// <summary>The indicator prefab the spawner instantiates for a lick event.</summary>
        private GameObject _lickPrefab;

        /// <summary>The indicator prefab the spawner instantiates for a delivered stimulus.</summary>
        private GameObject _stimulusPrefab;

        /// <summary>The host object carrying the spawner under test.</summary>
        private GameObject _spawnerObject;

        /// <summary>The spawner under test.</summary>
        private LickStimulusSpawner _spawner;

        /// <summary>Clears any leftover singleton and prepares the per-test object ledgers.</summary>
        [SetUp]
        public void SetUp()
        {
            _hosts = new List<GameObject>();
            _harnesses = new List<MqttTestHarness>();
            _clients = new List<MQTTClient>();
            _harness = null;
            _canvas = null;
            _lickPrefab = null;
            _stimulusPrefab = null;
            _spawnerObject = null;
            _spawner = null;

            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Releases every broker handle, harness, and host object, then clears the singleton.</summary>
        [TearDown]
        public void TearDown()
        {
            // A client whose host never became active never receives OnDestroy, so the broker handle a connect
            // attempt assigned is released here rather than by the Unity lifecycle.
            foreach (MQTTClient client in _clients)
            {
                if (client != null && client.client != null)
                {
                    client.client.Dispose();
                    client.client = null;
                }
            }
            _clients.Clear();

            foreach (MqttTestHarness harness in _harnesses)
            {
                harness.Dispose();
            }
            _harnesses.Clear();

            foreach (GameObject host in _hosts)
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
            _hosts.Clear();

            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Verifies that Unity's own Awake call installs the activated client as the singleton.</summary>
        [Test]
        public void Awake_ComponentActivatedUnderThePlayerLoop_InstallsTheSingleton()
        {
            MQTTClient client = CreateDormantClient("MQTT Client");
            Assert.IsNull(MQTTClient.Instance);

            client.gameObject.SetActive(true);

            Assert.AreSame(client, MQTTClient.Instance);
        }

        /// <summary>Verifies that Awake opens no broker connection of its own.</summary>
        [Test]
        public void Awake_ComponentActivatedUnderThePlayerLoop_LeavesTheBrokerConnectionUnopened()
        {
            MQTTClient client = CreateDormantClient("MQTT Client");

            client.gameObject.SetActive(true);

            Assert.IsNull(client.client);
            Assert.IsFalse(client.IsConnected());
        }

        /// <summary>Verifies that activating a second client leaves the first client installed as the singleton.
        /// </summary>
        [Test]
        public void Awake_SecondComponentActivated_LeavesTheFirstClientInstalledAsTheSingleton()
        {
            MQTTClient firstClient = CreateDormantClient("MQTT Client");
            MQTTClient secondClient = CreateDormantClient("MQTT Client Duplicate");
            firstClient.gameObject.SetActive(true);

            secondClient.gameObject.SetActive(true);

            Assert.AreSame(firstClient, MQTTClient.Instance);
        }

        /// <summary>Verifies that the player loop's Start call creates the session start and stop channels.</summary>
        [UnityTest]
        public IEnumerator Start_UnderThePlayerLoop_CreatesTheSessionStartAndStopChannels()
        {
            MqttTestHarness harness = CreateRunningHarness();

            yield return null;
            yield return null;

            MQTTChannel startChannel = PrivateAccess.GetField<MQTTChannel>(harness.Client, "_startChannel");
            MQTTChannel stopChannel = PrivateAccess.GetField<MQTTChannel>(harness.Client, "_stopChannel");
            Assert.IsNotNull(startChannel);
            Assert.IsNotNull(stopChannel);
            Assert.AreEqual(MQTTTopics.SessionStart, startChannel.topic);
            Assert.AreEqual(MQTTTopics.SessionStop, stopChannel.topic);
            Assert.AreSame(harness.Client, startChannel.client);
            Assert.AreSame(harness.Client, stopChannel.client);
        }

        /// <summary>Verifies that the session channels are publish-only, so neither joins the routing list.</summary>
        [UnityTest]
        public IEnumerator Start_UnderThePlayerLoop_LeavesTheSessionChannelsOutOfTheRoutingList()
        {
            MqttTestHarness harness = CreateRunningHarness();
            int registrationsBeforeStart = RegisteredChannelCount(harness.Client);

            yield return null;
            yield return null;

            Assert.IsNotNull(PrivateAccess.GetField<MQTTChannel>(harness.Client, "_startChannel"));
            Assert.AreEqual(registrationsBeforeStart, RegisteredChannelCount(harness.Client));
        }

        /// <summary>Verifies that no session-start message is published before the one second delay elapses.</summary>
        [UnityTest]
        public IEnumerator StartSessionAsync_BeforeTheDelayElapses_PublishesNothingOnSessionStart()
        {
            MqttTestHarness harness = CreateRunningHarness();

            yield return null;
            yield return new WaitForSecondsRealtime(BeforeSessionStartSeconds);

            Assert.AreEqual(0, harness.CountOn(MQTTTopics.SessionStart));
        }

        /// <summary>Verifies that exactly one empty session-start message lands once the delay elapses.</summary>
        [UnityTest]
        public IEnumerator StartSessionAsync_AfterTheDelayElapses_PublishesOneEmptySessionStartMessage()
        {
            MqttTestHarness harness = CreateRunningHarness();

            yield return null;
            yield return new WaitForSecondsRealtime(AfterSessionStartSeconds);

            Assert.AreEqual(1, harness.CountOn(MQTTTopics.SessionStart));
            Assert.AreEqual(string.Empty, harness.LastPayloadOn(MQTTTopics.SessionStart));
            Assert.AreEqual(0, harness.CountOn(MQTTTopics.SessionStop));
        }

        /// <summary>Verifies that Unity's own OnDestroy call clears the singleton the client installed.</summary>
        [Test]
        public void OnDestroy_UnderThePlayerLoop_ClearsTheSingletonItInstalled()
        {
            MQTTClient client = CreateDormantClient("MQTT Client");
            client.gameObject.SetActive(true);
            Assert.AreSame(client, MQTTClient.Instance);

            UnityEngine.Object.DestroyImmediate(client.gameObject);

            Assert.IsNull(MQTTClient.Instance);
        }

        /// <summary>Verifies that destroying a client that lost the singleton race leaves the winner installed.
        /// </summary>
        [Test]
        public void OnDestroy_ForeignSingletonInstalled_LeavesTheForeignSingletonInstalled()
        {
            MQTTClient installedClient = CreateDormantClient("MQTT Client");
            MQTTClient destroyedClient = CreateDormantClient("MQTT Client Duplicate");
            installedClient.gameObject.SetActive(true);
            destroyedClient.gameObject.SetActive(true);

            UnityEngine.Object.DestroyImmediate(destroyedClient.gameObject);

            Assert.AreSame(installedClient, MQTTClient.Instance);
        }

        /// <summary>Verifies that the loopback publish path delivers inside the frame that published it.</summary>
        [Test]
        public void Publish_UnderThePlayerLoop_DeliversWithinThePublishingFrame()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            FrameRecordingChannel capture = new FrameRecordingChannel(MQTTTopics.Motion);
            int publishFrame = Time.frameCount;

            client.Publish(MQTTTopics.Motion, Encoding.UTF8.GetBytes(MotionPayload));

            Assert.AreEqual(1, capture.Payloads.Count);
            Assert.AreEqual(MotionPayload, capture.Payloads[0]);
            Assert.AreEqual(publishFrame, capture.Frames[0]);
        }

        /// <summary>Verifies that a channel subscribed several frames earlier still receives a later publish.</summary>
        [UnityTest]
        public IEnumerator Publish_ChannelSubscribedInAnEarlierFrame_ReceivesTheLaterPublish()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            FrameRecordingChannel capture = new FrameRecordingChannel(MQTTTopics.Motion);

            yield return null;
            yield return null;
            Assert.AreEqual(0, capture.Payloads.Count);
            int publishFrame = Time.frameCount;
            client.Publish(MQTTTopics.Motion, Encoding.UTF8.GetBytes(MotionPayload));

            yield return null;

            Assert.AreEqual(1, capture.Payloads.Count);
            Assert.AreEqual(MotionPayload, capture.Payloads[0]);
            Assert.AreEqual(publishFrame, capture.Frames[0]);
        }

        /// <summary>Verifies that a trigger channel's Send reaches a listener registered on an earlier frame.</summary>
        [UnityTest]
        public IEnumerator Send_TriggerChannel_DeliversAnEmptyPayloadAcrossAFrameBoundary()
        {
            CreateSuspendedClient("MQTT Client");
            FrameRecordingChannel listener = new FrameRecordingChannel(MQTTTopics.SessionStop);
            MQTTChannel publisher = new MQTTChannel(MQTTTopics.SessionStop, isListener: false);
            int invocations = 0;
            listener.receivedEvent.AddListener(() => invocations++);

            yield return null;
            yield return null;
            publisher.Send();

            Assert.AreEqual(1, listener.Payloads.Count);
            Assert.AreEqual(string.Empty, listener.Payloads[0]);
            Assert.AreEqual(1, invocations);
        }

        /// <summary>Verifies that a typed channel's Send reaches a listener registered on an earlier frame.</summary>
        [UnityTest]
        public IEnumerator Send_TypedChannel_DeliversTheDeserializedMessageAcrossAFrameBoundary()
        {
            CreateSuspendedClient("MQTT Client");
            MQTTChannel<StimulusTriggerZone.StimulusMessage> listener =
                new MQTTChannel<StimulusTriggerZone.StimulusMessage>(MQTTTopics.Stimulus);
            List<StimulusTriggerZone.StimulusMessage> received = new List<StimulusTriggerZone.StimulusMessage>();
            listener.receivedEvent.AddListener(received.Add);
            MQTTChannel<StimulusTriggerZone.StimulusMessage> publisher =
                new MQTTChannel<StimulusTriggerZone.StimulusMessage>(MQTTTopics.Stimulus, isListener: false);

            yield return null;
            yield return null;
            publisher.Send(
                new StimulusTriggerZone.StimulusMessage
                {
                    trialName = "AB",
                    delivered = true,
                    cause = "guidance",
                }
            );

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("AB", received[0].trialName);
            Assert.IsTrue(received[0].delivered);
            Assert.AreEqual("guidance", received[0].cause);
        }

        /// <summary>Verifies that the connector reports the missing singleton instead of connecting.</summary>
        [Test]
        public void OnEnable_WithoutTheClientSingleton_LogsTheMissingInstanceError()
        {
            GameObject host = CreateDormantHost("MQTT Connector");
            host.AddComponent<MQTTConnectorObject>();
            LogAssert.Expect(LogType.Error, new Regex(MissingInstancePattern));

            host.SetActive(true);

            Assert.IsNull(MQTTClient.Instance);
        }

        /// <summary>Verifies that an unreachable broker produces one connection failure error, not an exception.
        /// </summary>
        [Test]
        public void OnEnable_WithAnUnreachableBroker_LogsTheConnectionFailureError()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            client.ipAddress = UnreachableBrokerAddress;
            client.port = UnreachableBrokerPort;
            GameObject host = CreateDormantHost("MQTT Connector");
            host.AddComponent<MQTTConnectorObject>();
            LogAssert.Expect(LogType.Error, new Regex(ConnectionFailurePattern));

            Assert.DoesNotThrow(() => host.SetActive(true));
        }

        /// <summary>Verifies that a failed connection attempt leaves the client reporting itself disconnected.
        /// </summary>
        [Test]
        public void OnEnable_WithAnUnreachableBroker_LeavesTheClientDisconnected()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            client.ipAddress = UnreachableBrokerAddress;
            client.port = UnreachableBrokerPort;
            GameObject host = CreateDormantHost("MQTT Connector");
            host.AddComponent<MQTTConnectorObject>();
            LogAssert.Expect(LogType.Error, new Regex(ConnectionFailurePattern));

            host.SetActive(true);

            Assert.IsFalse(client.IsConnected());
        }

        /// <summary>Verifies that a failed connection attempt still leaves the broker handle and handler wired.
        /// </summary>
        [Test]
        public void OnEnable_WithAnUnreachableBroker_AssignsTheUnderlyingClientAndMessageHandler()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            client.ipAddress = UnreachableBrokerAddress;
            client.port = UnreachableBrokerPort;
            GameObject host = CreateDormantHost("MQTT Connector");
            host.AddComponent<MQTTConnectorObject>();
            LogAssert.Expect(LogType.Error, new Regex(ConnectionFailurePattern));

            host.SetActive(true);

            Assert.IsNotNull(client.client);
            Assert.IsNotNull(PrivateAccess.GetField<object>(client, "_messageReceivedHandler"));
        }

        /// <summary>Verifies that re-enabling the connector runs a second attempt on a replacement handle.</summary>
        [Test]
        public void OnEnable_ConnectorReEnabled_ReplacesTheUnderlyingClientWithASecondAttempt()
        {
            MQTTClient client = CreateSuspendedClient("MQTT Client");
            client.ipAddress = UnreachableBrokerAddress;
            client.port = UnreachableBrokerPort;
            GameObject host = CreateDormantHost("MQTT Connector");
            host.AddComponent<MQTTConnectorObject>();
            LogAssert.Expect(LogType.Error, new Regex(ConnectionFailurePattern));
            LogAssert.Expect(LogType.Error, new Regex(ConnectionFailurePattern));
            host.SetActive(true);
            System.IDisposable firstHandle = client.client;

            host.SetActive(false);
            host.SetActive(true);

            Assert.IsNotNull(client.client);
            Assert.AreNotSame(firstHandle, client.client);

            // Connect overwrites the previous handle without disposing it, so the first one is released here.
            firstHandle.Dispose();
        }

        /// <summary>Verifies that the player loop's Start call subscribes the spawner's two channels.</summary>
        [UnityTest]
        public IEnumerator Start_SpawnerUnderThePlayerLoop_SubscribesTheLickAndStimulusChannels()
        {
            BuildSpawnerRig();
            int registrationsBeforeStart = RegisteredChannelCount(_harness.Client);

            yield return null;
            yield return null;

            MQTTChannel lick = PrivateAccess.GetField<MQTTChannel>(_spawner, "_lick");
            MQTTChannel<StimulusTriggerZone.StimulusMessage> stimulus = PrivateAccess.GetField<
                MQTTChannel<StimulusTriggerZone.StimulusMessage>
            >(_spawner, "_stimulus");
            Assert.AreEqual(MQTTTopics.Interaction, lick.topic);
            Assert.AreEqual(MQTTTopics.Stimulus, stimulus.topic);
            Assert.AreEqual(registrationsBeforeStart + 2, RegisteredChannelCount(_harness.Client));
        }

        /// <summary>Verifies that an interaction only arms the spawner, leaving the canvas untouched that frame.
        /// </summary>
        [UnityTest]
        public IEnumerator Update_WithinTheFrameThatPublishesAnInteraction_SpawnsNothing()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;

            _harness.PublishTrigger(MQTTTopics.Interaction);

            Assert.IsTrue(PrivateAccess.GetField<bool>(_spawner, "_showLick"));
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that the frame after an interaction spawns exactly one lick indicator.</summary>
        [UnityTest]
        public IEnumerator Update_OnTheFrameAfterAnInteraction_SpawnsOneLickIndicator()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            _harness.PublishTrigger(MQTTTopics.Interaction);

            yield return null;

            Assert.AreEqual(1, _canvas.transform.childCount);
            Transform indicator = _canvas.transform.GetChild(0);
            Assert.IsNotNull(indicator.GetComponent<LickMessage>());
            Assert.IsNull(indicator.GetComponent<StimulusMessage>());
            Assert.IsFalse(PrivateAccess.GetField<bool>(_spawner, "_showLick"));
        }

        /// <summary>Verifies that the frame after a delivered stimulus spawns exactly one stimulus indicator.</summary>
        [UnityTest]
        public IEnumerator Update_OnTheFrameAfterADeliveredStimulus_SpawnsOneStimulusIndicator()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            PublishStimulus(delivered: true);

            yield return null;

            Assert.AreEqual(1, _canvas.transform.childCount);
            Transform indicator = _canvas.transform.GetChild(0);
            Assert.IsNotNull(indicator.GetComponent<StimulusMessage>());
            Assert.IsNull(indicator.GetComponent<LickMessage>());
            Assert.IsFalse(PrivateAccess.GetField<bool>(_spawner, "_showStimulus"));
        }

        /// <summary>Verifies that an omitted stimulus outcome spawns no indicator on the following frame.</summary>
        [UnityTest]
        public IEnumerator Update_OnTheFrameAfterAnOmittedStimulus_SpawnsNothing()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            PublishStimulus(delivered: false);

            yield return null;

            Assert.AreEqual(0, _canvas.transform.childCount);
            Assert.IsFalse(PrivateAccess.GetField<bool>(_spawner, "_showStimulus"));
        }

        /// <summary>Verifies that a lick and a delivered stimulus in one frame spawn the lick indicator first.
        /// </summary>
        [UnityTest]
        public IEnumerator Update_AfterALickAndADeliveredStimulusInOneFrame_SpawnsTheLickIndicatorFirst()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            _harness.PublishTrigger(MQTTTopics.Interaction);
            PublishStimulus(delivered: true);

            yield return null;

            Assert.AreEqual(2, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
            Assert.IsNotNull(_canvas.transform.GetChild(1).GetComponent<StimulusMessage>());
        }

        /// <summary>Verifies that interactions on two consecutive frames spawn one indicator each.</summary>
        [UnityTest]
        public IEnumerator Update_AfterInteractionsOnTwoConsecutiveFrames_SpawnsTwoLickIndicators()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            _harness.PublishTrigger(MQTTTopics.Interaction);

            yield return null;
            _harness.PublishTrigger(MQTTTopics.Interaction);
            yield return null;

            Assert.AreEqual(2, _canvas.transform.childCount);
        }

        /// <summary>Verifies that a disabled spawner records the interaction without spawning an indicator.</summary>
        [UnityTest]
        public IEnumerator Update_WhileTheSpawnerIsDisabled_SpawnsNothing()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            _spawner.enabled = false;

            _harness.PublishTrigger(MQTTTopics.Interaction);
            yield return null;

            Assert.AreEqual(0, _canvas.transform.childCount);
            Assert.IsTrue(PrivateAccess.GetField<bool>(_spawner, "_showLick"));
        }

        /// <summary>Verifies that re-enabling the spawner spawns the indicator armed while it was disabled.</summary>
        [UnityTest]
        public IEnumerator Update_AfterTheSpawnerIsEnabledAgain_SpawnsThePendingLickIndicator()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            _spawner.enabled = false;
            _harness.PublishTrigger(MQTTTopics.Interaction);
            yield return null;

            _spawner.enabled = true;
            yield return null;

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
        }

        /// <summary>Verifies that destroying the spawner stops it from observing later interaction events.</summary>
        [UnityTest]
        public IEnumerator OnDestroy_SpawnerUnderThePlayerLoop_StopsObservingLaterInteractions()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            UnityEngine.Object.DestroyImmediate(_spawnerObject);

            _harness.PublishTrigger(MQTTTopics.Interaction);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_spawner, "_showLick"));
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that destroying the spawner stops it from observing later stimulus outcomes.</summary>
        [UnityTest]
        public IEnumerator OnDestroy_SpawnerUnderThePlayerLoop_StopsObservingLaterStimulusOutcomes()
        {
            BuildSpawnerRig();
            yield return null;
            yield return null;
            UnityEngine.Object.DestroyImmediate(_spawnerObject);

            PublishStimulus(delivered: true);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_spawner, "_showStimulus"));
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Creates an inactive host object registered for teardown, so no callback has run on it.</summary>
        /// <param name="hostName">The name given to the host object.</param>
        /// <returns>The inactive host object.</returns>
        private GameObject CreateDormantHost(string hostName)
        {
            GameObject host = new GameObject(hostName);
            host.SetActive(false);
            _hosts.Add(host);
            return host;
        }

        /// <summary>Creates a client component on an inactive host, so Awake and Start have not run yet.</summary>
        /// <param name="hostName">The name given to the host object.</param>
        /// <returns>The client component the test activates itself.</returns>
        private MQTTClient CreateDormantClient(string hostName)
        {
            MQTTClient client = CreateDormantHost(hostName).AddComponent<MQTTClient>();
            _clients.Add(client);
            return client;
        }

        /// <summary>Creates a dormant client and installs it as the singleton without running any callback.</summary>
        /// <param name="hostName">The name given to the host object.</param>
        /// <returns>The client every channel constructed afterwards resolves.</returns>
        private MQTTClient CreateSuspendedClient(string hostName)
        {
            MQTTClient client = CreateDormantClient(hostName);
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", client);
            return client;
        }

        /// <summary>Creates a harness whose client never reaches Start, so no session broadcast is scheduled.</summary>
        /// <returns>The harness, whose installed client stays usable while its host object is inactive.</returns>
        private MqttTestHarness CreateSuspendedHarness()
        {
            MqttTestHarness harness = MqttTestHarness.Create();
            _harnesses.Add(harness);

            // Awake already installed the singleton, so deactivating the host keeps Start, and the one second
            // session-start broadcast it schedules, out of the frames the test observes.
            harness.Client.gameObject.SetActive(false);
            return harness;
        }

        /// <summary>Creates a harness whose client runs its full lifecycle under the player loop.</summary>
        /// <remarks>
        /// A client whose Start runs schedules a session-start broadcast one second later, so a test built on this
        /// harness leaves that broadcast pending past its own end. The loopback warning the broadcast may log after the
        /// fixture tears the client down is a warning rather than an error.
        /// </remarks>
        /// <returns>The harness, whose client reaches Start on the frame after creation.</returns>
        private MqttTestHarness CreateRunningHarness()
        {
            MqttTestHarness harness = MqttTestHarness.Create();
            _harnesses.Add(harness);
            return harness;
        }

        /// <summary>Builds the canvas, both indicator sources, and the spawner under a suspended client.</summary>
        private void BuildSpawnerRig()
        {
            _harness = CreateSuspendedHarness();

            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas));
            _hosts.Add(canvasObject);
            _canvas = canvasObject.GetComponent<Canvas>();

            // The indicator sources stay inactive so their own Start never schedules the timed self-destruction,
            // and every clone the spawner instantiates inherits that state and stays on the canvas to be counted.
            _lickPrefab = new GameObject("LickIndicator", typeof(LickMessage));
            _lickPrefab.SetActive(false);
            _hosts.Add(_lickPrefab);

            _stimulusPrefab = new GameObject("StimulusIndicator", typeof(StimulusMessage));
            _stimulusPrefab.SetActive(false);
            _hosts.Add(_stimulusPrefab);

            _spawnerObject = new GameObject("UI-Control");
            _hosts.Add(_spawnerObject);
            _spawner = _spawnerObject.AddComponent<LickStimulusSpawner>();
            _spawner.canvas = _canvas;
            _spawner.lickPrefab = _lickPrefab;
            _spawner.stimulusPrefab = _stimulusPrefab;
        }

        /// <summary>Publishes a trial outcome on the topic the spawner watches for stimuli.</summary>
        /// <param name="delivered">The delivered flag carried by the published outcome.</param>
        private void PublishStimulus(bool delivered)
        {
            StimulusTriggerZone.StimulusMessage message = new StimulusTriggerZone.StimulusMessage
            {
                trialName = "TestTrial",
                delivered = delivered,
                cause = "behavior",
            };
            _harness.Publish(MQTTTopics.Stimulus, message);
        }

        /// <summary>Returns the number of channels the client currently routes messages to.</summary>
        /// <param name="client">The client whose routing list to read.</param>
        /// <returns>The registered channel count, including any harness capture channels.</returns>
        private static int RegisteredChannelCount(MQTTClient client)
        {
            return PrivateAccess.GetField<IList>(client, "_channelList").Count;
        }

        /// <summary>Records every payload routed to its topic together with the frame it arrived on.</summary>
        private sealed class FrameRecordingChannel : MQTTChannel
        {
            /// <summary>The payloads routed to this channel, oldest first.</summary>
            private readonly List<string> _payloads = new List<string>();

            /// <summary>The frame counter value captured as each payload arrived, oldest first.</summary>
            private readonly List<int> _frames = new List<int>();

            /// <summary>The payloads routed to this channel, oldest first.</summary>
            public IReadOnlyList<string> Payloads => _payloads;

            /// <summary>The frame counter value captured as each payload arrived, oldest first.</summary>
            public IReadOnlyList<int> Frames => _frames;

            /// <summary>Creates a channel on a topic, optionally subscribing it to the installed client.</summary>
            /// <param name="topicString">The topic this channel routes.</param>
            /// <param name="isListener">Determines whether the constructor subscribes the channel.</param>
            public FrameRecordingChannel(string topicString, bool isListener = true)
                : base(topicString, isListener) { }

            /// <summary>Records the routed payload and its frame, then invokes the base trigger event.</summary>
            /// <param name="messageString">The routed payload string.</param>
            public override void ReceivedMessage(string messageString)
            {
                _payloads.Add(messageString);
                _frames.Add(Time.frameCount);
                base.ReceivedMessage(messageString);
            }
        }
    }
}
