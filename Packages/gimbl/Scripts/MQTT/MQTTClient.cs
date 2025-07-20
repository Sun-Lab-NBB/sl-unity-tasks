using System.Collections;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Net;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Threading.Tasks;
using System.Diagnostics; //remove after tests.

// Class that is the main client for connectivity with the MQTT server
namespace Gimbl
{
    public class MQTTClient : MonoBehaviour
    {
        [HideInInspector]  public string ip;
        [HideInInspector]  public int port;
        public MqttClient client;   

        private class Channel
        {
            public string topic;
            public MQTTChannel channel;
        }
        private List<Channel> channelList = new List<Channel>();
        MQTTChannel startChannel;
        MQTTChannel stopChannel;

        void Start()
        {
            // Grab Settingss.
            ip = UnityEditor.EditorPrefs.GetString("JaneliaVR_MQTT_IP");
            port = UnityEditor.EditorPrefs.GetInt("JaneliaVR_MQTT_Port");
            // Subscribe to some standard output channels.
            startChannel = new MQTTChannel("Gimbl/Session/Start", false);
            stopChannel = new MQTTChannel("Gimbl/Session/Stop", false);
            StartSession(); 
        }

        async void StartSession()
        {
            await Task.Delay(1000);
            startChannel.Send();
        }
        void OnApplicationQuit()
        {
            // Send stop
            stopChannel.Send();
            // Unsubscribe from all topics.
            if (channelList.Count>0) client.Unsubscribe(channelList.Select(x => x.topic).ToArray());
            // Clear channel list.
            channelList = new List<Channel>();
        }

        public void Connect(bool verbose)
        {
            // Connect to broker.
            IPAddress ipAdress = IPAddress.Parse(ip);
            // disable weird obsolote constructor warning.
            #pragma warning disable 618
            client = new MqttClient(ipAdress, port, false, null, null, MqttSslProtocols.None);
            // Run connect as task so we can wait for timeout (cant be programatically changed otherwise and is really long...).
            Task t = Task.Run(() =>
           {
               byte msg = client.Connect(Guid.NewGuid().ToString());
               MqttMsgConnack connack = new MqttMsgConnack(); //for debugging.
                connack.GetBytes(msg);
                // Set callback on message.
                client.MqttMsgPublishReceived += ReceivedMessage;
               if (verbose)
               {
                   UnityEngine.Debug.Log(String.Format("Succesfully connected to MQTT Broker at: {0}:{1}", ip, port));
               }
           });
            TimeSpan ts = TimeSpan.FromMilliseconds(1000);
            if (! t.Wait(ts))
            {
                UnityEngine.Debug.LogError(String.Format("Could not connect to MQTT broker at {0}:{1}", ip, port));
            }
        }

        public void Disconnect()
        {
            if (IsConnected())
            {
                client.Disconnect();
            }
        }

        public bool IsConnected()
        {
            bool isConnected = false;
            try
            {
                isConnected = client.IsConnected;
            }
            catch { }
            return isConnected;
        }

        public void Subscribe(MQTTChannel obj,string topic, byte qoslevel)
        {
            if (IsConnected())
            {
                client.Subscribe(new string[] { topic }, new byte[] { qoslevel });
                // Add topic and event pair to list.
                lock (channelList)
                {
                    channelList.Add(new Channel() { topic = topic, channel = obj });
                }
            }
        }
        public void ReceivedMessage(object sender, MqttMsgPublishEventArgs e)
        {
            // Go through topic - event pairs.
            //UnityEngine.Debug.Log(string.Format("topic: {0},msg: {1}", e.Topic, e.Message));
            lock (channelList)
            {
                foreach (Channel chn in channelList)
                {
                    if (string.Equals(e.Topic, chn.topic))
                    {
                        chn.channel.ReceivedMessage(Encoding.UTF8.GetString(e.Message));
                    }
                }
            }
        }

    }
}
