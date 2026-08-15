/// <summary>
/// Verifies the behavior of the LickStimulusSpawner class.
/// </summary>
using System;
using System.Collections;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using SL.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the LickStimulusSpawner class.</summary>
    [TestFixture]
    public class LickStimulusSpawnerTests
    {
        /// <summary>The MQTT harness hosting the client singleton and capturing every published payload.</summary>
        private MqttTestHarness _harness;

        /// <summary>The root object parenting every object a test creates.</summary>
        private GameObject _root;

        /// <summary>The canvas the spawner parents its indicators to.</summary>
        private Canvas _canvas;

        /// <summary>The indicator prefab the spawner instantiates for a lick event.</summary>
        private GameObject _lickPrefab;

        /// <summary>The indicator prefab the spawner instantiates for a delivered stimulus.</summary>
        private GameObject _stimulusPrefab;

        /// <summary>The spawner under test.</summary>
        private LickStimulusSpawner _spawner;

        /// <summary>Installs the MQTT client and builds the canvas, indicator prefabs, and spawner.</summary>
        [SetUp]
        public void SetUp()
        {
            _harness = MqttTestHarness.Create();

            _root = new GameObject("LickStimulusSpawnerRig");

            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas));
            canvasObject.transform.SetParent(_root.transform);
            _canvas = canvasObject.GetComponent<Canvas>();

            _lickPrefab = new GameObject("LickIndicator", typeof(LickMessage));
            _lickPrefab.transform.SetParent(_root.transform);

            _stimulusPrefab = new GameObject("StimulusIndicator", typeof(StimulusMessage));
            _stimulusPrefab.transform.SetParent(_root.transform);

            GameObject spawnerObject = new GameObject("UI-Control");
            spawnerObject.transform.SetParent(_root.transform);
            _spawner = spawnerObject.AddComponent<LickStimulusSpawner>();
            _spawner.canvas = _canvas;
            _spawner.lickPrefab = _lickPrefab;
            _spawner.stimulusPrefab = _stimulusPrefab;
        }

        /// <summary>Destroys every created object and removes the MQTT client singleton.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            _harness.Dispose();
        }

        /// <summary>Verifies that Start refuses to build its channels while no MQTT client is installed.</summary>
        [Test]
        public void Start_WithoutMqttClient_ThrowsInvalidOperation()
        {
            _harness.Dispose();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => InvokeStart());

            StringAssert.Contains("MQTTClient.Instance not available", exception.Message);
        }

        /// <summary>Verifies that Start subscribes the lick channel to the Interaction topic.</summary>
        [Test]
        public void Start_WithMqttClient_SubscribesLickChannelToInteractionTopic()
        {
            InvokeStart();

            MQTTChannel lick = PrivateAccess.GetField<MQTTChannel>(_spawner, "_lick");
            Assert.AreEqual(MQTTTopics.Interaction, lick.topic);
            Assert.AreSame(_harness.Client, lick.client);
        }

        /// <summary>Verifies that Start subscribes the typed stimulus channel to the Stimulus topic.</summary>
        [Test]
        public void Start_WithMqttClient_SubscribesStimulusChannelToStimulusTopic()
        {
            InvokeStart();

            MQTTChannel<StimulusTriggerZone.StimulusMessage> stimulus = PrivateAccess.GetField<
                MQTTChannel<StimulusTriggerZone.StimulusMessage>
            >(_spawner, "_stimulus");
            Assert.AreEqual(MQTTTopics.Stimulus, stimulus.topic);
            Assert.AreSame(_harness.Client, stimulus.client);
        }

        /// <summary>Verifies that Start registers exactly the two listener channels on the client.</summary>
        [Test]
        public void Start_WithMqttClient_RegistersTwoClientSubscriptions()
        {
            int baseline = ClientSubscriptionCount();

            InvokeStart();

            Assert.AreEqual(baseline + 2, ClientSubscriptionCount());
        }

        /// <summary>Verifies that Update spawns nothing while neither event has arrived.</summary>
        [Test]
        public void Update_WithoutAnyEvent_SpawnsNothing()
        {
            InvokeStart();

            InvokeUpdate();

            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that a lick event spawns exactly one lick indicator on the canvas.</summary>
        [Test]
        public void Update_AfterLickEvent_SpawnsOneLickIndicatorOnCanvas()
        {
            InvokeStart();
            PublishLick();

            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Transform indicator = _canvas.transform.GetChild(0);
            Assert.IsNotNull(indicator.GetComponent<LickMessage>());
            Assert.IsNull(indicator.GetComponent<StimulusMessage>());
        }

        /// <summary>Verifies that the spawned lick indicator carries the default one second lifetime.</summary>
        [Test]
        public void Update_AfterLickEvent_SpawnsIndicatorCarryingDefaultLifetime()
        {
            InvokeStart();
            PublishLick();

            InvokeUpdate();

            LickMessage indicator = _canvas.transform.GetChild(0).GetComponent<LickMessage>();
            Assert.AreEqual(1.0f, indicator.destroyTime, 1e-6f);
        }

        /// <summary>Verifies that a delivered stimulus spawns exactly one stimulus indicator on the canvas.
        /// </summary>
        [Test]
        public void Update_AfterDeliveredStimulus_SpawnsOneStimulusIndicatorOnCanvas()
        {
            InvokeStart();
            PublishStimulus(delivered: true);

            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Transform indicator = _canvas.transform.GetChild(0);
            Assert.IsNotNull(indicator.GetComponent<StimulusMessage>());
            Assert.IsNull(indicator.GetComponent<LickMessage>());
        }

        /// <summary>Verifies that the spawned stimulus indicator carries the default four second lifetime.
        /// </summary>
        [Test]
        public void Update_AfterDeliveredStimulus_SpawnsIndicatorCarryingDefaultLifetime()
        {
            InvokeStart();
            PublishStimulus(delivered: true);

            InvokeUpdate();

            StimulusMessage indicator = _canvas.transform.GetChild(0).GetComponent<StimulusMessage>();
            Assert.AreEqual(4.0f, indicator.destroyTime, 1e-6f);
        }

        /// <summary>Verifies that an omitted stimulus outcome spawns no indicator at all.</summary>
        [Test]
        public void Update_AfterOmittedStimulus_SpawnsNothing()
        {
            InvokeStart();
            PublishStimulus(delivered: false);

            InvokeUpdate();

            Assert.AreEqual(0, _canvas.transform.childCount);
            Assert.AreEqual(0, PendingStimulusCount());
        }

        /// <summary>Verifies that an omitted outcome arriving after a delivered one leaves the count standing.
        /// </summary>
        [Test]
        public void Update_DeliveredThenOmittedStimulus_StillSpawnsTheStimulusIndicator()
        {
            InvokeStart();
            PublishStimulus(delivered: true);
            PublishStimulus(delivered: false);

            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<StimulusMessage>());
        }

        /// <summary>Verifies that a lick and a delivered stimulus spawn the lick indicator first.</summary>
        [Test]
        public void Update_AfterLickAndDeliveredStimulus_SpawnsLickIndicatorBeforeStimulusIndicator()
        {
            InvokeStart();
            PublishLick();
            PublishStimulus(delivered: true);

            InvokeUpdate();

            Assert.AreEqual(2, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
            Assert.IsNotNull(_canvas.transform.GetChild(1).GetComponent<StimulusMessage>());
        }

        /// <summary>Verifies that three licks arriving inside one frame each spawn their own indicator.</summary>
        [Test]
        public void Update_RepeatedLickEventsBeforeOneFrame_SpawnsOneIndicatorPerEvent()
        {
            InvokeStart();
            PublishLick();
            PublishLick();
            PublishLick();

            InvokeUpdate();

            Assert.AreEqual(3, _canvas.transform.childCount);
        }

        /// <summary>Verifies that two delivered stimuli inside one frame each spawn their own indicator.</summary>
        [Test]
        public void Update_RepeatedStimulusEventsBeforeOneFrame_SpawnsOneIndicatorPerEvent()
        {
            InvokeStart();
            PublishStimulus(delivered: true);
            PublishStimulus(delivered: true);

            InvokeUpdate();

            Assert.AreEqual(2, _canvas.transform.childCount);
        }

        /// <summary>Verifies that each lick event raises the pending count the following Update consumes.
        /// </summary>
        [Test]
        public void OnLick_ThreeEventsBeforeUpdate_RaisesThePendingCountToThree()
        {
            InvokeStart();

            PublishLick();
            PublishLick();
            PublishLick();

            Assert.AreEqual(3, PendingLickCount());
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that the pending count is consumed, so a later frame spawns no second indicator.
        /// </summary>
        [Test]
        public void Update_SecondFrameAfterSpawn_SpawnsNoAdditionalIndicator()
        {
            InvokeStart();
            PublishLick();
            InvokeUpdate();

            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.AreEqual(0, PendingLickCount());
        }

        /// <summary>Verifies that a lick arriving after a spawned indicator re-arms the spawner.</summary>
        [Test]
        public void Update_LickEventAfterPreviousSpawn_SpawnsAnotherIndicator()
        {
            InvokeStart();
            PublishLick();
            InvokeUpdate();

            PublishLick();
            InvokeUpdate();

            Assert.AreEqual(2, _canvas.transform.childCount);
        }

        /// <summary>Verifies that OnDestroy stops the spawner from reacting to further lick events.</summary>
        [Test]
        public void OnDestroy_AfterStart_StopsRespondingToLickEvents()
        {
            InvokeStart();
            InvokeOnDestroy();

            PublishLick();

            Assert.AreEqual(0, PendingLickCount());
            InvokeUpdate();
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that OnDestroy stops the spawner from reacting to further stimulus outcomes.
        /// </summary>
        [Test]
        public void OnDestroy_AfterStart_StopsRespondingToStimulusEvents()
        {
            InvokeStart();
            InvokeOnDestroy();

            PublishStimulus(delivered: true);

            Assert.AreEqual(0, PendingStimulusCount());
            InvokeUpdate();
            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that OnDestroy removes both channels from the client routing list.</summary>
        [Test]
        public void OnDestroy_AfterStart_RemovesTheClientSubscriptions()
        {
            int baseline = ClientSubscriptionCount();
            InvokeStart();

            InvokeOnDestroy();

            Assert.AreEqual(baseline, ClientSubscriptionCount());
        }

        /// <summary>Verifies that a second OnDestroy removes no further entry from the client routing list.
        /// </summary>
        [Test]
        public void OnDestroy_CalledTwice_LeavesTheClientRoutingListUnchanged()
        {
            int baseline = ClientSubscriptionCount();
            InvokeStart();
            InvokeOnDestroy();

            InvokeOnDestroy();

            Assert.AreEqual(baseline, ClientSubscriptionCount());
        }

        /// <summary>Verifies that repeated Start and OnDestroy cycles leave no routing entry behind.</summary>
        [Test]
        public void OnDestroy_AcrossThreeStartCycles_LeavesNoAccumulatedSubscriptions()
        {
            int baseline = ClientSubscriptionCount();

            for (int cycle = 0; cycle < 3; cycle++)
            {
                InvokeStart();
                InvokeOnDestroy();
            }

            Assert.AreEqual(baseline, ClientSubscriptionCount());
        }

        /// <summary>Verifies that OnDestroy tolerates a component whose Start never ran.</summary>
        [Test]
        public void OnDestroy_BeforeStart_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => InvokeOnDestroy());
        }

        /// <summary>Verifies that a pending indicator with no canvas assigned reports the unassigned reference.
        /// </summary>
        [Test]
        public void Update_CanvasMissing_ReportsTheErrorAndSpawnsNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex("The canvas field must reference an assigned object"));
            InvokeStart();
            _spawner.canvas = null;
            PublishLick();

            Assert.DoesNotThrow(() => InvokeUpdate());

            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that a pending indicator with no prefab assigned reports the unassigned reference.
        /// </summary>
        [Test]
        public void Update_LickPrefabMissing_ReportsTheErrorAndSpawnsNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex("The prefab field must reference an assigned object"));
            InvokeStart();
            _spawner.lickPrefab = null;
            PublishLick();

            Assert.DoesNotThrow(() => InvokeUpdate());

            Assert.AreEqual(0, _canvas.transform.childCount);
        }

        /// <summary>Verifies that an unassigned prefab holds the pending count for a later frame.</summary>
        [Test]
        public void Update_LickPrefabMissing_KeepsThePendingCount()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unable to spawn the lick indicator"));
            InvokeStart();
            _spawner.lickPrefab = null;
            PublishLick();

            InvokeUpdate();

            Assert.AreEqual(1, PendingLickCount());
        }

        /// <summary>Verifies that assigning the prefab spawns the indicator held while it was unassigned.</summary>
        [Test]
        public void Update_LickPrefabAssignedAfterMissingFrame_SpawnsTheHeldIndicator()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unable to spawn the lick indicator"));
            InvokeStart();
            _spawner.lickPrefab = null;
            PublishLick();
            InvokeUpdate();

            _spawner.lickPrefab = _lickPrefab;
            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
        }

        /// <summary>Verifies that assigning the canvas spawns the indicator held while it was unassigned.</summary>
        [Test]
        public void Update_CanvasAssignedAfterMissingFrame_SpawnsTheHeldIndicator()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unable to spawn the lick indicator"));
            InvokeStart();
            _spawner.canvas = null;
            PublishLick();
            InvokeUpdate();

            _spawner.canvas = _canvas;
            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
        }

        /// <summary>Verifies that an unassigned reference is reported once however many frames retry it.</summary>
        [Test]
        public void Update_CanvasMissingAcrossThreeFrames_ReportsTheErrorOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unable to spawn the lick indicator"));
            InvokeStart();
            _spawner.canvas = null;
            PublishLick();

            InvokeUpdate();
            InvokeUpdate();
            InvokeUpdate();

            Assert.AreEqual(1, PendingLickCount());
        }

        /// <summary>Verifies that an unassigned stimulus prefab leaves the lick indicator spawning normally.
        /// </summary>
        [Test]
        public void Update_StimulusPrefabMissing_StillSpawnsTheLickIndicator()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unable to spawn the stimulus indicator"));
            InvokeStart();
            _spawner.stimulusPrefab = null;
            PublishLick();
            PublishStimulus(delivered: true);

            InvokeUpdate();

            Assert.AreEqual(1, _canvas.transform.childCount);
            Assert.IsNotNull(_canvas.transform.GetChild(0).GetComponent<LickMessage>());
            Assert.AreEqual(1, PendingStimulusCount());
        }

        /// <summary>Runs the spawner's Start callback.</summary>
        private void InvokeStart()
        {
            PrivateAccess.Invoke(_spawner, "Start");
        }

        /// <summary>Runs the spawner's Update callback for one simulated frame.</summary>
        private void InvokeUpdate()
        {
            PrivateAccess.Invoke(_spawner, "Update");
        }

        /// <summary>Runs the spawner's OnDestroy callback.</summary>
        private void InvokeOnDestroy()
        {
            PrivateAccess.Invoke(_spawner, "OnDestroy");
        }

        /// <summary>Publishes an interaction trigger on the topic the spawner watches for licks.</summary>
        private void PublishLick()
        {
            _harness.PublishTrigger(MQTTTopics.Interaction);
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

        /// <summary>Returns the number of lick indicators the spawner holds for its next Update.</summary>
        /// <returns>The pending lick indicator count.</returns>
        private int PendingLickCount()
        {
            return PrivateAccess.GetField<int>(_spawner, "_pendingLickCount");
        }

        /// <summary>Returns the number of stimulus indicators the spawner holds for its next Update.</summary>
        /// <returns>The pending stimulus indicator count.</returns>
        private int PendingStimulusCount()
        {
            return PrivateAccess.GetField<int>(_spawner, "_pendingStimulusCount");
        }

        /// <summary>Returns the number of channels currently registered on the MQTT client.</summary>
        /// <returns>The registered channel count, including the harness capture channels.</returns>
        private int ClientSubscriptionCount()
        {
            return PrivateAccess.GetField<IList>(_harness.Client, "_channelList").Count;
        }
    }
}
