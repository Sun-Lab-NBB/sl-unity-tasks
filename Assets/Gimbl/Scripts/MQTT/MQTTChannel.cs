/// <summary>
/// Provides the MQTTChannel classes for trigger-based and type-safe MQTT messaging.
///
/// Channels wrap the publish and subscribe surface of MQTTClient, carrying the message contract shared with
/// sollertia-experiment.
/// </summary>
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Gimbl
{
    /// <summary>Handles simple trigger-based MQTT messaging without payload data.</summary>
    public class MQTTChannel
    {
        /// <summary>The MQTT topic string for this channel.</summary>
        internal readonly string topic;

        /// <summary>The reference to the MQTTClient managing the broker connection.</summary>
        public readonly MQTTClient client;

        /// <summary>The Unity event invoked when a message is received on this channel.</summary>
        public readonly UnityEvent receivedEvent = new UnityEvent();

        /// <summary>Creates a new MQTT channel for the specified topic.</summary>
        /// <param name="topicString">The MQTT topic on which this channel subscribes or publishes.</param>
        /// <param name="isListener">Determines whether to subscribe to receive messages on this topic.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        /// <exception cref="InvalidOperationException">No <see cref="MQTTClient"/> singleton is available.</exception>
        public MQTTChannel(string topicString, bool isListener = true, byte qosLevel = 2)
        {
            topic = topicString;
            client = MQTTClient.Instance;
            if (client == null)
            {
                string message =
                    $"Unable to create the MQTT channel for topic '{topic}'. The active scene must host an "
                    + "MQTTClient component, but MQTTClient.Instance is null.";
                throw new InvalidOperationException(message);
            }

            if (isListener)
            {
                client.Subscribe(this, topic, qosLevel);
            }
        }

        /// <summary>Handles received messages by invoking the receivedEvent.</summary>
        /// <param name="messageString">The received payload, which a trigger channel ignores.</param>
        internal virtual void ReceivedMessage(string messageString)
        {
            receivedEvent.Invoke();
        }

        /// <summary>Publishes a trigger message (null payload) to this channel's topic.</summary>
        internal void Send()
        {
            client.Publish(topic, null);
        }
    }

    /// <summary>Handles typed MQTT messaging with JSON serialization for the payload.</summary>
    /// <remarks>
    /// The typed <see cref="receivedEvent"/> shadows the base <see cref="MQTTChannel.receivedEvent"/> via
    /// the <c>new</c> modifier because <see cref="UnityEngine.Events.UnityEvent"/> and
    /// <see cref="UnityEngine.Events.UnityEvent{T0}"/> are unrelated types with no shared parameterized
    /// contract. A virtual property cannot express both signatures, so the payload type would be lost
    /// under a clean override. Callers that need the deserialized payload must reference the channel as
    /// <see cref="MQTTChannel{TMessage}"/>. A base <see cref="MQTTChannel"/> reference exposes only the
    /// parameterless trigger event and will silently miss the typed callback.
    /// </remarks>
    /// <typeparam name="TMessage">The type of the message payload to serialize and deserialize.</typeparam>
    public class MQTTChannel<TMessage> : MQTTChannel
    {
        /// <summary>The typed Unity event invoked when a message is received on this channel.</summary>
        public new readonly ChannelEvent receivedEvent = new ChannelEvent();

        /// <summary>Creates a new typed MQTT channel for the specified topic.</summary>
        /// <param name="topicString">The MQTT topic on which this channel subscribes or publishes.</param>
        /// <param name="isListener">Determines whether to subscribe to receive messages on this topic.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        public MQTTChannel(string topicString, bool isListener = true, byte qosLevel = 2)
            : base(topicString, isListener, qosLevel) { }

        /// <summary>Handles received messages by deserializing JSON and invoking the typed receivedEvent.</summary>
        /// <remarks>
        /// The event is invoked outside the try block so that an exception raised by a subscriber propagates as
        /// itself, leaving the deserialization failure message for payloads the parser actually rejected.
        /// </remarks>
        /// <param name="messageString">The received JSON payload.</param>
        /// <exception cref="InvalidOperationException">The payload cannot be deserialized into TMessage.</exception>
        internal override void ReceivedMessage(string messageString)
        {
            TMessage message;
            try
            {
                message = JsonUtility.FromJson<TMessage>(messageString);
            }
            catch (Exception exception)
            {
                string failureMessage =
                    $"Unable to deserialize the payload received on topic '{topic}' into {typeof(TMessage).Name}. "
                    + $"The payload must be valid JSON, but parsing failed with: {exception.Message}";
                throw new InvalidOperationException(failureMessage, exception);
            }

            receivedEvent.Invoke(message);
        }

        /// <summary>Publishes a typed message as JSON to this channel's topic.</summary>
        /// <param name="message">The message object to serialize and publish.</param>
        public void Send(TMessage message)
        {
            client.Publish(topic, Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
        }

        /// <summary>Rejects the parameterless publish inherited from the base channel.</summary>
        /// <remarks>
        /// A typed topic carries a JSON payload, so the empty payload this overload would publish reaches every
        /// typed listener on that topic as a null message. Hiding the overload turns that silent loss into a
        /// failure at the call site, and a caller that genuinely wants a trigger message publishes it through a
        /// base <see cref="MQTTChannel"/> on its own topic.
        /// </remarks>
        /// <exception cref="NotSupportedException">Always, because the typed channel publishes typed payloads.
        /// </exception>
        public new void Send()
        {
            string message =
                $"Unable to publish a trigger message on the typed channel for topic '{topic}'. A "
                + $"MQTTChannel<{typeof(TMessage).Name}> must publish through its Send(TMessage) overload, but the "
                + "parameterless overload inherited from MQTTChannel was called.";
            throw new NotSupportedException(message);
        }

        /// <summary>The typed Unity event class for this channel.</summary>
        public class ChannelEvent : UnityEvent<TMessage> { }
    }
}
