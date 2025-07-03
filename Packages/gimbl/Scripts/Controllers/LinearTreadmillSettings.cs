using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gimbl
{
    [System.Serializable]
    public class LinearTreadmillSettings : ScriptableObject
    {
        public string deviceName = "LinearTreadmill";
        public bool isActive = true;
        public bool loopPath = false;
        public string[] buttonTopics;
        public GamepadSettings gamepadSettings;

    }
}

