/// <summary>
/// Provides the MqttTestHarness that stands up an MQTTClient singleton and records every published payload.
/// </summary>
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Gimbl;
using UnityEngine;

namespace SL.Tests
{
    /// <summary>
    /// Hosts an MQTTClient singleton for a test and captures the payloads published on every known topic.
    /// </summary>
    /// <remarks>
    /// The harness never contacts a broker. MQTTClient.Publish falls back to in-process delivery whenever the client is
    /// disconnected, so a capture channel subscribed to a topic observes exactly what the production publish path
    /// produced. A harness publish reaches the real listener the code under test registered.
    /// </remarks>
    public sealed class MqttTestHarness : IDisposable
    {
        /// <summary>The GameObject hosting the client component.</summary>
        private readonly GameObject _host;

        /// <summary>The capture channel subscribed to each topic, keyed by topic.</summary>
        private readonly Dictionary<string, CapturingChannel> _captures = new Dictionary<string, CapturingChannel>();

        /// <summary>The client whose publish path the code under test uses.</summary>
        public MQTTClient Client { get; }

        /// <summary>Creates the host object, installs the singleton, and subscribes one capture channel per topic.
        /// </summary>
        private MqttTestHarness()
        {
            _host = new GameObject("MQTT Client");
            Client = _host.AddComponent<MQTTClient>();

            // Edit Mode never runs Awake, and Play Mode runs it before this assignment, so forcing the singleton
            // leaves both modes with the same client installed by the time a channel resolves it.
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", Client);

            foreach (string topic in KnownTopics())
            {
                _captures[topic] = new CapturingChannel(topic);
            }
        }

        /// <summary>Creates a harness whose singleton and capture channels are ready for use.</summary>
        /// <returns>The harness, which the caller disposes to remove the singleton and its host object.</returns>
        public static MqttTestHarness Create()
        {
            return new MqttTestHarness();
        }

        /// <summary>Returns every topic literal declared by <see cref="MQTTTopics"/>.</summary>
        /// <returns>The topic literals, in declaration order.</returns>
        public static IEnumerable<string> KnownTopics()
        {
            List<string> topics = new List<string>();
            FieldInfo[] fields = typeof(MQTTTopics).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields)
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                {
                    topics.Add((string)field.GetRawConstantValue());
                }
            }
            return topics;
        }

        /// <summary>Returns the number of payloads captured on a topic.</summary>
        /// <param name="topic">The topic to count.</param>
        /// <returns>The captured payload count.</returns>
        public int CountOn(string topic)
        {
            return PayloadsOn(topic).Count;
        }

        /// <summary>Returns the most recent payload captured on a topic.</summary>
        /// <param name="topic">The topic to read.</param>
        /// <returns>The most recent payload string.</returns>
        /// <exception cref="InvalidOperationException">The topic carries no captured payload.</exception>
        public string LastPayloadOn(string topic)
        {
            IReadOnlyList<string> payloads = PayloadsOn(topic);
            if (payloads.Count == 0)
            {
                string message =
                    $"Unable to read the most recent payload on topic '{topic}'. The topic must carry at least one "
                    + "captured payload, but it carries none.";
                throw new InvalidOperationException(message);
            }
            return payloads[payloads.Count - 1];
        }

        /// <summary>Deserializes the most recent payload captured on a topic.</summary>
        /// <typeparam name="TMessage">The message type the payload deserializes into.</typeparam>
        /// <param name="topic">The topic to read.</param>
        /// <returns>The deserialized message.</returns>
        public TMessage LastMessageOn<TMessage>(string topic)
        {
            return JsonUtility.FromJson<TMessage>(LastPayloadOn(topic));
        }

        /// <summary>Deserializes every payload captured on a topic, oldest first.</summary>
        /// <typeparam name="TMessage">The message type the payloads deserialize into.</typeparam>
        /// <param name="topic">The topic to read.</param>
        /// <returns>The deserialized messages.</returns>
        public List<TMessage> MessagesOn<TMessage>(string topic)
        {
            IReadOnlyList<string> payloads = PayloadsOn(topic);
            List<TMessage> messages = new List<TMessage>(payloads.Count);
            foreach (string payload in payloads)
            {
                messages.Add(JsonUtility.FromJson<TMessage>(payload));
            }
            return messages;
        }

        /// <summary>Publishes an empty trigger message on a topic through the production publish path.</summary>
        /// <param name="topic">The topic to publish on.</param>
        public void PublishTrigger(string topic)
        {
            Client.Publish(topic, null);
        }

        /// <summary>Publishes a JSON-serialized message on a topic through the production publish path.</summary>
        /// <typeparam name="TMessage">The message type to serialize.</typeparam>
        /// <param name="topic">The topic to publish on.</param>
        /// <param name="message">The message serialized into the payload.</param>
        public void Publish<TMessage>(string topic, TMessage message)
        {
            Client.Publish(topic, Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
        }

        /// <summary>Discards every captured payload, leaving the subscriptions in place.</summary>
        public void Clear()
        {
            foreach (KeyValuePair<string, CapturingChannel> entry in _captures)
            {
                entry.Value.payloads.Clear();
            }
        }

        /// <summary>Removes the singleton and destroys the host object.</summary>
        public void Dispose()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }
            PrivateAccess.SetStaticProperty(typeof(MQTTClient), "Instance", null);
        }

        /// <summary>Returns the payloads captured on a topic, oldest first.</summary>
        /// <param name="topic">The topic to read.</param>
        /// <returns>The captured payload strings.</returns>
        private IReadOnlyList<string> PayloadsOn(string topic)
        {
            return _captures.TryGetValue(topic, out CapturingChannel channel)
                ? channel.payloads
                : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>Records every payload routed to its topic while preserving the base channel behavior.</summary>
        private sealed class CapturingChannel : MQTTChannel
        {
            /// <summary>The payloads routed to this channel, oldest first.</summary>
            public readonly List<string> payloads = new List<string>();

            /// <summary>Subscribes the capture channel to a topic.</summary>
            /// <param name="topicString">The topic to subscribe to.</param>
            public CapturingChannel(string topicString)
                : base(topicString, isListener: true) { }

            /// <summary>Records the payload, then invokes the base trigger event.</summary>
            /// <param name="messageString">The routed payload string.</param>
            public override void ReceivedMessage(string messageString)
            {
                payloads.Add(messageString);
                base.ReceivedMessage(messageString);
            }
        }
    }
}
