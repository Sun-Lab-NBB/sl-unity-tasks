/// <summary>
/// Provides the MQTTClient class for managing connectivity with the MQTT broker.
///
/// Handles connection establishment, topic subscription, and message routing for bidirectional communication
/// between Unity and external systems like sollertia-experiment.
/// </summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using UnityEngine;

namespace Gimbl
{
    /// <summary>Manages the MQTT broker connection and routes messages to subscribed channels.</summary>
    /// <remarks>
    /// Expects a host GameObject named "MQTT Client". Connection settings (IP and port) are loaded from Unity
    /// EditorPrefs.
    /// </remarks>
    public class MQTTClient : MonoBehaviour
    {
        /// <summary>The time in milliseconds allowed for a broker connection attempt to resolve.</summary>
        /// <remarks>
        /// The budget exceeds any plausible broker handshake, so the attempt either completes or fails before the
        /// first channel subscribes. An attempt that resolves after that point leaves the client able to publish
        /// while subscribed to nothing, because every channel constructed meanwhile takes the not-connected early
        /// return in <see cref="Subscribe"/>.
        /// </remarks>
        private const int ConnectTimeoutMilliseconds = 10000;

        /// <summary>The lowest port number a broker connection can be opened on.</summary>
        private const int MinimumBrokerPort = 1;

        /// <summary>The highest port number a broker connection can be opened on.</summary>
        private const int MaximumBrokerPort = 65535;

        /// <summary>The IP address of the MQTT broker.</summary>
        /// <remarks>
        /// The initializer matches the loopback fallback applied by <see cref="Awake"/> and by
        /// <see cref="Gimbl.MainWindow.EnsureMqttDefaults"/> so a freshly-instantiated client (via <c>AddComponent</c>
        /// at editor time, before either hook has run) reports the same value the eventual fallback would assign.
        /// </remarks>
        [HideInInspector]
        public string ipAddress = "127.0.0.1";

        /// <summary>The port number of the MQTT broker.</summary>
        /// <remarks>
        /// The initializer matches the standard MQTT port fallback applied by <see cref="Awake"/> and by
        /// <see cref="Gimbl.MainWindow.EnsureMqttDefaults"/>.
        /// </remarks>
        [HideInInspector]
        public int port = 1883;

        /// <summary>The underlying MQTTnet client instance.</summary>
        private IMqttClient _client;

        /// <summary>The list of all subscribed channels for message routing.</summary>
        private List<Channel> _channelList = new List<Channel>();

        /// <summary>The topics already warned about no-broker loopback delivery, to avoid repeating it.</summary>
        private readonly HashSet<string> _loopbackWarnedTopics = new HashSet<string>();

        /// <summary>The channel for broadcasting session start events.</summary>
        private MQTTChannel _startChannel;

        /// <summary>The channel for broadcasting session stop events.</summary>
        private MQTTChannel _stopChannel;

        /// <summary>The stored handler for received MQTT application messages.</summary>
        private Func<MqttApplicationMessageReceivedEventArgs, Task> _messageReceivedHandler;

        /// <summary>The singleton instance of the MQTTClient.</summary>
        public static MQTTClient Instance { get; private set; }

        /// <summary>Registers this instance as the singleton and loads connection settings on awake.</summary>
        /// <remarks>
        /// Connection settings are loaded in Awake (rather than Start) because peer scripts such as
        /// <see cref="MQTTConnectorObject"/> trigger <see cref="Connect"/> from their OnEnable, which
        /// Unity executes after every Awake but before every Start. Reading ipAddress/port in Start
        /// would leave them empty when the connect call runs and crash MqttClientOptionsBuilder.
        /// </remarks>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                string message =
                    "Unable to register this MQTTClient as the singleton instance. The active scene must host "
                    + "exactly one MQTTClient component, but another instance is already registered, so that "
                    + "instance remains in use.";
                Debug.LogWarning(message);
                return;
            }
            Instance = this;

#if UNITY_EDITOR
            ipAddress = UnityEditor.EditorPrefs.GetString("SollertiaVR_MQTT_IP");
            port = UnityEditor.EditorPrefs.GetInt("SollertiaVR_MQTT_Port");
#endif

            // Falls back to localhost defaults so a fresh project always attempts a connection. The Task Parameters
            // window applies the same fallback when its UI is opened. Mirroring it here ensures users who have not yet
            // visited that window still get a working broker setup when mosquitto (or another local broker) is running
            // on standard ports. The port fallback covers the whole out-of-range span, because a port outside it
            // reaches the options builder as a value no socket can bind.
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = "127.0.0.1";
            }
            if (port < MinimumBrokerPort || port > MaximumBrokerPort)
            {
                port = 1883;
            }
        }

        /// <summary>Creates the session start/stop publish channels and broadcasts session start.</summary>
        private void Start()
        {
            _startChannel = new MQTTChannel(MQTTTopics.SessionStart, isListener: false);
            _stopChannel = new MQTTChannel(MQTTTopics.SessionStop, isListener: false);
            // Discards the returned Task because the broadcast is genuinely fire-and-forget. Any failure
            // is logged inside StartSessionAsync and there is no caller to observe completion.
            _ = StartSessionAsync();
        }

        /// <summary>Sends session stop message and cleans up subscriptions on application quit.</summary>
        /// <remarks>
        /// The stop broadcast is conditional on the channel existing, because a quit reached before
        /// <see cref="Start"/> created it must still run every cleanup step below the broadcast.
        /// </remarks>
        private void OnApplicationQuit()
        {
            _stopChannel?.Send();

            if (_channelList.Count > 0 && IsConnected())
            {
                MqttClientUnsubscribeOptionsBuilder unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder();
                foreach (string topic in _channelList.Select(channel => channel.topic))
                {
                    unsubscribeOptions.WithTopicFilter(topic);
                }
                _client.UnsubscribeAsync(unsubscribeOptions.Build()).GetAwaiter().GetResult();
            }

            _channelList = new List<Channel>();

            if (_client != null && _messageReceivedHandler != null)
            {
                _client.ApplicationMessageReceivedAsync -= _messageReceivedHandler;
            }

            Disconnect();

            _client?.Dispose();
            _client = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Unsubscribes the message handler and disposes the client on destroy.</summary>
        /// <remarks>
        /// Duplicates the cleanup performed by <see cref="OnApplicationQuit"/> because the two lifecycle
        /// callbacks do not always both fire. A scene transition that destroys this component without
        /// quitting the application reaches only <c>OnDestroy</c>. A process exit that bypasses scene
        /// teardown reaches only <c>OnApplicationQuit</c>. The duplicated routing reset, handler unhook,
        /// and disposal ensure the underlying <see cref="IMqttClient"/> and every routed channel are
        /// released in either path.
        /// </remarks>
        private void OnDestroy()
        {
            _channelList = new List<Channel>();

            if (_client != null && _messageReceivedHandler != null)
            {
                _client.ApplicationMessageReceivedAsync -= _messageReceivedHandler;
            }

            _client?.Dispose();
            _client = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Establishes a connection to the MQTT broker.</summary>
        /// <remarks>
        /// Each call unhooks and disposes the handle its predecessor installed before it builds the replacement, so
        /// repeated enable cycles hold one broker handle at a time.
        /// </remarks>
        /// <param name="verbose">Determines whether to log successful connection to the console.</param>
        public void Connect(bool verbose)
        {
            if (_client != null)
            {
                if (_messageReceivedHandler != null)
                {
                    _client.ApplicationMessageReceivedAsync -= _messageReceivedHandler;
                }
                _client.Dispose();
                _client = null;
            }

            MqttFactory factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            // Routes received messages to the appropriate subscribed channels.
            _messageReceivedHandler = e =>
            {
                string payload = Encoding.UTF8.GetString(
                    e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>(),
                    e.ApplicationMessage.PayloadSegment.Offset,
                    e.ApplicationMessage.PayloadSegment.Count
                );

                lock (_channelList)
                {
                    foreach (Channel channel in _channelList)
                    {
                        if (string.Equals(e.ApplicationMessage.Topic, channel.topic, StringComparison.Ordinal))
                        {
                            channel.mqttChannel.ReceivedMessage(payload);
                        }
                    }
                }

                return Task.CompletedTask;
            };
            _client.ApplicationMessageReceivedAsync += _messageReceivedHandler;

            MqttClientOptions options = new MqttClientOptionsBuilder()
                .WithTcpServer(ipAddress, port)
                .WithClientId(Guid.NewGuid().ToString())
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                .Build();

            // Waits on the connect task so a timeout and a refused connection both report before channels subscribe.
            Task connectionTask = Task.Run(() => _client.ConnectAsync(options));
            try
            {
                if (!connectionTask.Wait(ConnectTimeoutMilliseconds))
                {
                    string message =
                        $"Unable to connect to the MQTT broker at {ipAddress}:{port}. The connection attempt must "
                        + $"resolve within {ConnectTimeoutMilliseconds} milliseconds, but it did not.";
                    Debug.LogError(message);
                }
                else if (verbose)
                {
                    Debug.Log($"Successfully connected to MQTT Broker at: {ipAddress}:{port}");
                }
            }
            catch (AggregateException exception)
            {
                string message =
                    $"Unable to connect to the MQTT broker at {ipAddress}:{port}. The broker must accept an "
                    + $"MQTT 5.0 connection, but the attempt failed with: {exception.InnerException?.Message}";
                Debug.LogError(message);
            }
        }

        /// <summary>Disconnects from the MQTT broker if currently connected.</summary>
        public void Disconnect()
        {
            if (IsConnected())
            {
                _client.DisconnectAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>Checks whether the client is currently connected to the broker.</summary>
        /// <returns>True if connected, false otherwise.</returns>
        private bool IsConnected()
        {
            try
            {
                return _client != null && _client.IsConnected;
            }
            catch (Exception exception)
            {
                string message =
                    "Unable to determine whether the MQTT client is connected. The client handle must report its "
                    + $"connection state, but the check failed with: {exception.Message}";
                Debug.LogWarning(message);
                return false;
            }
        }

        /// <summary>Subscribes a channel to receive messages on the specified topic.</summary>
        /// <remarks>
        /// The channel is added to <see cref="_channelList"/> unconditionally so that <see cref="Publish"/>'s
        /// no-broker fallback can route messages locally during keyboard-only test runs. The broker-side
        /// <c>SubscribeAsync</c> only fires when the client is currently connected. A channel created while
        /// the broker is offline will receive in-process loopback messages but will <b>not</b> auto-subscribe
        /// once the broker comes online. Callers that need broker-delivered messages after a late connect
        /// must re-create the channel or trigger a new subscribe pass.
        /// </remarks>
        /// <param name="channel">The channel on which the topic delivers messages.</param>
        /// <param name="topic">The MQTT topic to subscribe to.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        internal void Subscribe(MQTTChannel channel, string topic, byte qosLevel)
        {
            lock (_channelList)
            {
                _channelList.Add(new Channel() { topic = topic, mqttChannel = channel });
            }

            if (!IsConnected())
            {
                return;
            }

            MqttQualityOfServiceLevel qualityOfServiceLevel = (MqttQualityOfServiceLevel)qosLevel;
            _client
                .SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(qualityOfServiceLevel))
                        .Build()
                )
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        string message =
                            $"Unable to subscribe to the MQTT topic '{topic}'. The broker must accept the "
                            + $"subscription request, but it failed with: {t.Exception?.InnerException?.Message}";
                        Debug.LogError(message);
                    }
                });
        }

        /// <summary>Removes a channel from the routing list, so a destroyed listener stops receiving messages.
        /// </summary>
        /// <remarks>
        /// A channel that outlives its owner keeps receiving every publish on its topic, and a typed channel keeps
        /// deserializing each payload, so a component that builds channels in Start releases them in OnDestroy. The
        /// broker-side subscription is left in place, because several channels may share one topic and the broker
        /// filter is per topic rather than per channel.
        /// </remarks>
        /// <param name="channel">The channel whose routing entries are removed.</param>
        public void Unsubscribe(MQTTChannel channel)
        {
            if (channel == null)
            {
                return;
            }

            lock (_channelList)
            {
                _channelList.RemoveAll(entry => ReferenceEquals(entry.mqttChannel, channel));
            }
        }

        /// <summary>Publishes a message to the specified topic.</summary>
        /// <param name="topic">The topic that receives the published message.</param>
        /// <param name="payload">The serialized message body, or null for a trigger message.</param>
        internal void Publish(string topic, byte[] payload)
        {
            // When the broker is unreachable (typical for keyboard-only test runs without mosquitto),
            // routes the message directly to in-process subscribers on the matching topic. Production
            // setups with a real broker reach the IsConnected() branch below and use MQTT as normal.
            if (!IsConnected())
            {
                if (_loopbackWarnedTopics.Add(topic))
                {
                    string warning =
                        $"Unable to deliver '{topic}' to the MQTT broker at {ipAddress}:{port}. The broker must "
                        + "be reachable for the message to reach sollertia-experiment, but it is not, so the "
                        + "message reaches in-process subscribers only. This is expected for keyboard-only "
                        + "testing, but a topic that works only this way has no wired experiment-side counterpart.";
                    Debug.LogWarning(warning);
                }

                string payloadString = payload == null ? string.Empty : Encoding.UTF8.GetString(payload);
                lock (_channelList)
                {
                    foreach (Channel channel in _channelList)
                    {
                        if (string.Equals(channel.topic, topic, StringComparison.Ordinal))
                        {
                            channel.mqttChannel.ReceivedMessage(payloadString);
                        }
                    }
                }
                return;
            }

            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload ?? Array.Empty<byte>())
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                .Build();

            _client
                .PublishAsync(message)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        string message =
                            $"Unable to publish to the MQTT topic '{topic}'. The broker must accept the publish "
                            + $"request, but it failed with: {t.Exception?.InnerException?.Message}";
                        Debug.LogError(message);
                    }
                });
        }

        /// <summary>Sends the session start message after a brief delay.</summary>
        /// <remarks>
        /// Failures are caught and logged inside the method, so the returned task completes successfully even when the
        /// publish fails.
        /// </remarks>
        /// <returns>A task that completes once the session-start message has been published.</returns>
        private async Task StartSessionAsync()
        {
            try
            {
                await Task.Delay(1000);
                _startChannel.Send();
            }
            catch (Exception exception)
            {
                string message =
                    "Unable to broadcast the MQTT session-start message. The start channel must publish once the "
                    + $"startup delay elapses, but the publish failed with: {exception.Message}";
                Debug.LogError(message);
            }
        }

        /// <summary>Maps a topic string to its corresponding channel handler.</summary>
        private class Channel
        {
            /// <summary>The topic on which this channel is registered.</summary>
            public string topic;

            /// <summary>The MQTTChannel instance that handles messages for this topic.</summary>
            public MQTTChannel mqttChannel;
        }
    }
}
