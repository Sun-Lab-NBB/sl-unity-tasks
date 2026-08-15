/// <summary>
/// Verifies the behavior of the MQTTChannel and MQTTChannel&lt;TMessage&gt; classes.
/// </summary>
using System;
using System.Collections;
using System.Reflection;
using Gimbl;
using NUnit.Framework;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the MQTTChannel and MQTTChannel&lt;TMessage&gt; classes.</summary>
    /// <remarks>
    /// No broker participates. MQTTClient.Publish falls back to in-process delivery while disconnected, so a
    /// publish reaches every channel registered on the matching topic and the harness observes exactly what the
    /// production publish path produced. The first publish per topic logs a broker-unreachable warning, which
    /// the Test Framework does not require a declaration for.
    /// </remarks>
    [TestFixture]
    public class MQTTChannelTests
    {
        /// <summary>The topic used by channels that must stay outside the harness-captured topic catalog.</summary>
        private const string ProbeTopic = "ProbeTopic";

        /// <summary>The JSON payload of a ProbeMessage carrying the label "alpha" and the count 7.</summary>
        private const string ProbeJson = "{\"label\":\"alpha\",\"count\":7}";

        /// <summary>A payload JsonUtility cannot parse, used to reach the deserialization failure path.</summary>
        private const string MalformedJson = "{ this is not valid json";

        /// <summary>Clears any client singleton a previous fixture left installed.</summary>
        [SetUp]
        public void SetUp()
        {
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Clears the client singleton so a later fixture starts from a clean slate.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Verifies that constructing a channel without a client singleton throws.</summary>
        [Test]
        public void Constructor_MissingClientSingleton_ThrowsInvalidOperation()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new MQTTChannel(ProbeTopic)
            );

            StringAssert.Contains("MQTTClient.Instance not available", exception.Message);
        }

        /// <summary>Verifies that constructing a typed channel without a client singleton throws.</summary>
        [Test]
        public void Constructor_MissingClientSingletonForTypedChannel_ThrowsInvalidOperation()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new MQTTChannel<ProbeMessage>(ProbeTopic)
            );

            StringAssert.Contains("MQTTClient.Instance not available", exception.Message);
        }

        /// <summary>Verifies that a constructed channel stores its topic and the resolved client singleton.</summary>
        [Test]
        public void Constructor_ResolvedClientSingleton_PopulatesTopicAndClientFields()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);

                Assert.AreEqual(ProbeTopic, channel.topic);
                Assert.AreSame(harness.Client, channel.client);
            }
        }

        /// <summary>Verifies that a typed channel populates the same fields through its base constructor.</summary>
        [Test]
        public void Constructor_TypedChannel_PopulatesTopicAndClientFields()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);

                Assert.AreEqual(ProbeTopic, channel.topic);
                Assert.AreSame(harness.Client, channel.client);
            }
        }

        /// <summary>Verifies that a listener channel registers itself under its own topic exactly once.</summary>
        /// <remarks>
        /// The registration record is inspected rather than merely counted, because a count alone survives a
        /// constructor that hands the client the wrong topic or a channel other than itself.
        /// </remarks>
        [Test]
        public void Constructor_ListenerFlagEnabled_RegistersTheChannelWithTheClient()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                int registrationsBefore = RegisteredChannelCount(harness.Client);

                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: true);

                IList registrations = Registrations(harness.Client);
                Assert.AreEqual(registrationsBefore + 1, registrations.Count);
                object registration = registrations[registrations.Count - 1];
                Assert.AreEqual(ProbeTopic, PrivateAccess.GetField<string>(registration, "topic"));
                Assert.AreSame(channel, PrivateAccess.GetField<MQTTChannel>(registration, "mqttChannel"));
            }
        }

        /// <summary>Verifies that a publish-only channel leaves the client's routing table untouched.</summary>
        [Test]
        public void Constructor_ListenerFlagDisabled_LeavesTheChannelUnregistered()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                int registrationsBefore = RegisteredChannelCount(harness.Client);

                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);

                Assert.AreEqual(ProbeTopic, channel.topic);
                Assert.AreEqual(registrationsBefore, RegisteredChannelCount(harness.Client));
            }
        }

        /// <summary>Verifies that omitting the listener flag registers the channel, so the default is true.
        /// </summary>
        [Test]
        public void Constructor_OmittedListenerFlag_RegistersTheChannelWithTheClient()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                int registrationsBefore = RegisteredChannelCount(harness.Client);

                MQTTChannel channel = new MQTTChannel(ProbeTopic);

                Assert.AreEqual(ProbeTopic, channel.topic);
                Assert.AreEqual(registrationsBefore + 1, RegisteredChannelCount(harness.Client));
            }
        }

        /// <summary>Verifies that the base channel constructor defaults its quality of service level to two.
        /// </summary>
        /// <remarks>
        /// The subscription value itself is unobservable here, because <c>MQTTClient.Subscribe</c> takes the
        /// not-connected early return before it converts the level and the registration record keeps only the topic
        /// and the channel. The declared default is still part of the cross-repository contract, so it is pinned on
        /// the signature, which is the only surface an Edit Mode test can reach without a live broker.
        /// </remarks>
        [Test]
        public void Constructor_OmittedQosLevel_DefaultsToExactlyOnceDelivery()
        {
            ConstructorInfo[] constructors = typeof(MQTTChannel).GetConstructors();

            Assert.AreEqual(1, constructors.Length);
            ParameterInfo[] parameters = constructors[0].GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual("qosLevel", parameters[2].Name);
            Assert.AreEqual((byte)2, parameters[2].DefaultValue);
        }

        /// <summary>Verifies that the typed channel constructor defaults its quality of service level to two.
        /// </summary>
        [Test]
        public void Constructor_TypedChannelOmittedQosLevel_DefaultsToExactlyOnceDelivery()
        {
            ConstructorInfo[] constructors = typeof(MQTTChannel<ProbeMessage>).GetConstructors();

            Assert.AreEqual(1, constructors.Length);
            ParameterInfo[] parameters = constructors[0].GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual("qosLevel", parameters[2].Name);
            Assert.AreEqual((byte)2, parameters[2].DefaultValue);
        }

        /// <summary>Verifies that a listener channel receives the messages published on its own topic.</summary>
        [Test]
        public void Constructor_ListenerFlagEnabled_ReceivesMessagesPublishedOnItsTopic()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: true);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                harness.Client.Publish(ProbeTopic, null);

                Assert.AreEqual(1, invocationCount);
            }
        }

        /// <summary>Verifies that a publish-only channel receives nothing published on its own topic.</summary>
        [Test]
        public void Constructor_ListenerFlagDisabled_ReceivesNoMessagesPublishedOnItsTopic()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                harness.Client.Publish(ProbeTopic, null);

                Assert.AreEqual(0, invocationCount);
            }
        }

        /// <summary>Verifies that a listener channel receives nothing published on a different topic.</summary>
        [Test]
        public void Constructor_ListenerFlagEnabled_ReceivesNoMessagesPublishedOnAnotherTopic()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: true);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                harness.Client.Publish(MQTTTopics.Motion, null);

                Assert.AreEqual(0, invocationCount);
            }
        }

        /// <summary>Verifies that the base channel invokes its parameterless event when a message arrives.
        /// </summary>
        [Test]
        public void ReceivedMessage_BaseChannel_InvokesTheParameterlessEvent()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                channel.ReceivedMessage(ProbeJson);

                Assert.AreEqual(1, invocationCount);
            }
        }

        /// <summary>Verifies that the base channel invokes its event for a payload it never parses.</summary>
        [Test]
        public void ReceivedMessage_BaseChannelWithMalformedPayload_StillInvokesTheParameterlessEvent()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                channel.ReceivedMessage(MalformedJson);

                Assert.AreEqual(1, invocationCount);
            }
        }

        /// <summary>Verifies that the base channel invokes its event once for each message it receives.</summary>
        [Test]
        public void ReceivedMessage_BaseChannelCalledThreeTimes_InvokesTheParameterlessEventThreeTimes()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(ProbeTopic, isListener: false);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                channel.ReceivedMessage(string.Empty);
                channel.ReceivedMessage(string.Empty);
                channel.ReceivedMessage(string.Empty);

                Assert.AreEqual(3, invocationCount);
            }
        }

        /// <summary>Verifies that the base channel publishes an empty trigger payload on its own topic.</summary>
        [Test]
        public void Send_BaseChannel_PublishesAnEmptyPayloadOnItsOwnTopic()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(MQTTTopics.SessionStop, isListener: false);

                channel.Send();

                Assert.AreEqual(1, harness.CountOn(MQTTTopics.SessionStop));
                Assert.AreEqual(string.Empty, harness.LastPayloadOn(MQTTTopics.SessionStop));
                Assert.AreEqual(0, harness.CountOn(MQTTTopics.SessionStart));
            }
        }

        /// <summary>Verifies that a listener channel publishing on its own topic reaches its own event.</summary>
        [Test]
        public void Send_BaseChannelListeningOnItsOwnTopic_LoopsTheTriggerBackToItself()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel channel = new MQTTChannel(MQTTTopics.SessionStop, isListener: true);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(() => invocationCount++);

                channel.Send();

                Assert.AreEqual(1, invocationCount);
                Assert.AreEqual(1, harness.CountOn(MQTTTopics.SessionStop));
            }
        }

        /// <summary>Verifies that the typed channel deserializes the payload and invokes its typed event.</summary>
        [Test]
        public void ReceivedMessage_TypedChannelWithValidJson_InvokesTheTypedEventWithTheDeserializedMessage()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                int invocationCount = 0;
                string receivedLabel = null;
                int receivedCount = 0;
                channel.receivedEvent.AddListener(message =>
                {
                    invocationCount++;
                    receivedLabel = message.label;
                    receivedCount = message.count;
                });

                channel.ReceivedMessage(ProbeJson);

                Assert.AreEqual(1, invocationCount);
                Assert.AreEqual("alpha", receivedLabel);
                Assert.AreEqual(7, receivedCount);
            }
        }

        /// <summary>Verifies that an empty payload reaches the typed event as a null message.</summary>
        /// <remarks>
        /// JsonUtility returns null for a null or empty string instead of raising a parse error, so this payload
        /// takes neither the deserialization path nor the failure path. A base MQTTChannel publishing a trigger
        /// message on a topic a typed channel also listens on produces exactly this payload, because the publish
        /// loopback converts the null payload to an empty string.
        /// </remarks>
        [Test]
        public void ReceivedMessage_TypedChannelWithEmptyPayload_InvokesTheTypedEventWithANullMessage()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                int invocationCount = 0;
                ProbeMessage receivedMessage = new ProbeMessage { label = "unset", count = 99 };
                channel.receivedEvent.AddListener(message =>
                {
                    invocationCount++;
                    receivedMessage = message;
                });

                channel.ReceivedMessage(string.Empty);

                Assert.AreEqual(1, invocationCount);
                Assert.IsNull(receivedMessage);
            }
        }

        /// <summary>Verifies that the typed channel wraps a deserialization failure in an InvalidOperation.
        /// </summary>
        [Test]
        public void ReceivedMessage_TypedChannelWithMalformedJson_ThrowsInvalidOperationWrappingTheFailure()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    channel.ReceivedMessage(MalformedJson)
                );

                StringAssert.StartsWith(
                    "MQTTChannel<ProbeMessage>: Failed to deserialize message: ",
                    exception.Message
                );
                Assert.IsNotNull(exception.InnerException);
                StringAssert.Contains(exception.InnerException.Message, exception.Message);
            }
        }

        /// <summary>Verifies that a failed deserialization leaves the typed event uninvoked.</summary>
        [Test]
        public void ReceivedMessage_TypedChannelWithMalformedJson_LeavesTheTypedEventUninvoked()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                int invocationCount = 0;
                channel.receivedEvent.AddListener(message => invocationCount++);

                Assert.Throws<InvalidOperationException>(() => channel.ReceivedMessage(MalformedJson));

                Assert.AreEqual(0, invocationCount);
            }
        }

        /// <summary>Verifies that an exception a listener raises propagates with its own type and message.
        /// </summary>
        /// <remarks>
        /// The typed event is invoked below the try block, so a subscriber failure reaches the caller as itself
        /// rather than as a deserialization failure that misnames the JSON parser as the culprit.
        /// </remarks>
        [Test]
        public void ReceivedMessage_TypedChannelWithThrowingListener_PropagatesTheListenerException()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                channel.receivedEvent.AddListener(message => throw new NotSupportedException("listener failed"));

                NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
                    channel.ReceivedMessage(ProbeJson)
                );

                Assert.AreEqual("listener failed", exception.Message);
            }
        }

        /// <summary>Verifies that a listener failure carries no deserialization wording.</summary>
        [Test]
        public void ReceivedMessage_TypedChannelWithThrowingListener_ReportsNoDeserializationFailure()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                channel.receivedEvent.AddListener(message => throw new NotSupportedException("listener failed"));

                Exception exception = Assert.Catch<Exception>(() => channel.ReceivedMessage(ProbeJson));

                Assert.IsNotInstanceOf<InvalidOperationException>(exception);
                StringAssert.DoesNotContain("Failed to deserialize message", exception.Message);
            }
        }

        /// <summary>Verifies that the typed event is a distinct instance from the shadowed base event.</summary>
        [Test]
        public void ReceivedEvent_TypedChannel_ShadowsTheBaseEventWithADistinctInstance()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                MQTTChannel baseReference = channel;

                Assert.AreNotSame(baseReference.receivedEvent, channel.receivedEvent);
                Assert.IsInstanceOf<MQTTChannel<ProbeMessage>.ChannelEvent>(channel.receivedEvent);
            }
        }

        /// <summary>Verifies that a listener bound through a base reference never fires on a typed channel.
        /// </summary>
        /// <remarks>
        /// This is the documented shadowing hazard. A caller that binds a listener through a base MQTTChannel
        /// reference subscribes to the parameterless event, which the typed override never invokes.
        /// </remarks>
        [Test]
        public void ReceivedMessage_TypedChannelHeldAsBaseReference_InvokesOnlyTheTypedEvent()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(ProbeTopic, isListener: false);
                MQTTChannel baseReference = channel;
                int baseInvocationCount = 0;
                int typedInvocationCount = 0;
                baseReference.receivedEvent.AddListener(() => baseInvocationCount++);
                channel.receivedEvent.AddListener(message => typedInvocationCount++);

                baseReference.ReceivedMessage(ProbeJson);

                Assert.AreEqual(0, baseInvocationCount);
                Assert.AreEqual(1, typedInvocationCount);
            }
        }

        /// <summary>Verifies that the typed channel publishes the JSON serialization of its message.</summary>
        [Test]
        public void Send_TypedChannel_PublishesTheJsonSerializationOfTheMessage()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(MQTTTopics.Delay, isListener: false);
                ProbeMessage message = new ProbeMessage { label = "alpha", count = 7 };

                channel.Send(message);

                Assert.AreEqual(1, harness.CountOn(MQTTTopics.Delay));
                Assert.AreEqual(ProbeJson, harness.LastPayloadOn(MQTTTopics.Delay));
            }
        }

        /// <summary>Verifies that a typed message published on a listening channel arrives back unchanged.
        /// </summary>
        [Test]
        public void Send_TypedChannelListeningOnItsOwnTopic_LoopsTheDeserializedMessageBackToItself()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(MQTTTopics.Delay, isListener: true);
                string receivedLabel = null;
                int receivedCount = 0;
                channel.receivedEvent.AddListener(message =>
                {
                    receivedLabel = message.label;
                    receivedCount = message.count;
                });

                channel.Send(new ProbeMessage { label = "beta", count = -3 });

                Assert.AreEqual("beta", receivedLabel);
                Assert.AreEqual(-3, receivedCount);
                Assert.AreEqual("{\"label\":\"beta\",\"count\":-3}", harness.LastPayloadOn(MQTTTopics.Delay));
            }
        }

        /// <summary>Verifies that the parameterless Send on a typed channel rejects the call.</summary>
        [Test]
        public void Send_TypedChannelParameterlessOverload_ThrowsNotSupported()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(MQTTTopics.Delay, isListener: false);

                NotSupportedException exception = Assert.Throws<NotSupportedException>(() => channel.Send());

                StringAssert.Contains("must publish through its Send(TMessage) overload", exception.Message);
            }
        }

        /// <summary>Verifies that the rejected parameterless Send publishes nothing on the channel's topic.
        /// </summary>
        [Test]
        public void Send_TypedChannelParameterlessOverload_PublishesNothing()
        {
            using (MqttTestHarness harness = MqttTestHarness.Create())
            {
                MQTTChannel<ProbeMessage> channel = new MQTTChannel<ProbeMessage>(MQTTTopics.Delay, isListener: false);

                Assert.Throws<NotSupportedException>(() => channel.Send());

                Assert.AreEqual(0, harness.CountOn(MQTTTopics.Delay));
            }
        }

        /// <summary>Verifies that the typed channel declares its own parameterless Send rather than inheriting one.
        /// </summary>
        [Test]
        public void Send_TypedChannelParameterlessOverload_IsDeclaredByTheTypedChannel()
        {
            MethodInfo method = typeof(MQTTChannel<ProbeMessage>).GetMethod(
                "Send",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            );

            Assert.IsNotNull(method);
        }

        /// <summary>Returns the routing records the client currently delivers received messages to.</summary>
        /// <param name="client">The client whose channel registration list to read.</param>
        /// <returns>The registration records, oldest first.</returns>
        private static IList Registrations(MQTTClient client)
        {
            return PrivateAccess.GetField<IList>(client, "_channelList");
        }

        /// <summary>Returns the number of channels the client currently routes messages to.</summary>
        /// <param name="client">The client whose channel registration list to read.</param>
        /// <returns>The registered channel count.</returns>
        private static int RegisteredChannelCount(MQTTClient client)
        {
            return Registrations(client).Count;
        }

        /// <summary>The payload type used to exercise the typed channel's serialization round trip.</summary>
        [Serializable]
        public class ProbeMessage
        {
            /// <summary>The string field carried by the payload.</summary>
            public string label;

            /// <summary>The integer field carried by the payload.</summary>
            public int count;
        }
    }
}
