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
            // Start mqtt client when scene starts
            GameObject.FindAnyObjectByType<Gimbl.MQTTClient>().Connect(false);
        }

    }

}