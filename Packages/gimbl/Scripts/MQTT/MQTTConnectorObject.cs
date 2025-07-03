using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using UnityEditor;

namespace Gimbl
{
    public class MQTTConnectorObject : MonoBehaviour
    {
        public void OnEnable()
        {
            // Logger object start mqtt client to ensure its ready.
            GameObject.FindObjectOfType<Gimbl.MQTTClient>().Connect(false);
        }

    }

}