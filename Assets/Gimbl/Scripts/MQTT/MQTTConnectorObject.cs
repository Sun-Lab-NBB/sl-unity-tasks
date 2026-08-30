/// <summary>
/// Provides the MQTTConnectorObject class that automatically connects to the MQTT broker on scene start.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>Automatically establishes MQTT broker connection when the scene starts.</summary>
    public class MQTTConnectorObject : MonoBehaviour
    {
        /// <summary>Connects to the MQTT broker when the object is enabled.</summary>
        private void OnEnable()
        {
            if (MQTTClient.Instance == null)
            {
                string message =
                    "Unable to connect to the MQTT broker on enable. The active scene must host an MQTTClient "
                    + "component, but MQTTClient.Instance is null.";
                Debug.LogError(message);
                return;
            }
            MQTTClient.Instance.Connect(verbose: false);
        }
    }
}
