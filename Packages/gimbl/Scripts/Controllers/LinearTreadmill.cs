using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gimbl;
using System.Linq;
using UnityEditor;
using System;
namespace Gimbl
{
    public class LinearTreadmill : Gimbl.ControllerObject
    {
        public LinearTreadmillSettings settings;


        // Messaging classes.
        public class MSG
        {
            public float movement;
        }
        public class StatusMsg
        {
            public bool status;
        }
        public MQTTChannel<StatusMsg> statusChannel;

        public class LinearLogMsg
        {
            public string name;
            public int move = new int();
        }
        public LinearLogMsg logMsg = new LinearLogMsg();

        private LoggerObject logger;

        // Logging of key changed settings.
        public class KeyLinearSettings
        {
            public string name;
            public bool isActive;
            public LinearTreadmillSettings.LinearGain gain = new LinearTreadmillSettings.LinearGain();
        }
        public KeyLinearSettings logSettings;


        // Movement variables.
        private float newInput;
        private float moved;
        private Vector3 pos;
        private Quaternion newRot;

        void OnEnable()
        {
            // Get instance of logger.
            logger = FindObjectOfType<LoggerObject>();
        }

        void Start()
        {
            if (this.GetType() == typeof(LinearTreadmill))
            {
                // Setup Listener.

                MQTTChannel<MSG> channel = new MQTTChannel<MSG>(string.Format("{0}/Data", settings.deviceName));
                channel.Event.AddListener(OnMessage);
                
                // Start treadmill.
                statusChannel = new MQTTChannel<StatusMsg>(string.Format("{0}/Status", settings.deviceName), false);
                statusChannel.Send(new StatusMsg() { status = true });
            }
            // Setup tracking of settings changes.
            logSettings = new KeyLinearSettings();
            LogLinearSettings();
        }
        public void Update()
        {
            ProcessMovement();
            CheckLinearSettings();
        }

        public void LogLinearSettings()
        {
            logSettings.name = name;
            logSettings.gain.forward = settings.gain.forward;
            logSettings.gain.backward = settings.gain.backward;
            logSettings.isActive = settings.isActive;
            logger.logFile.Log<KeyLinearSettings>("Linear Controller Settings", logSettings);
        }
        public void CheckLinearSettings()
        {
            if (logSettings.name != name || logSettings.gain.forward != settings.gain.forward || logSettings.gain.backward != settings.gain.backward ||
                logSettings.isActive != settings.isActive)
            {
                LogLinearSettings();
            }
        }
        public void ProcessMovement()
        {

            if (Actor != null && settings.isActive)
            {
                moved = movement.Sum().x; // Accumulate all input since the last frame
                
                // Gain
                if (moved > 0) { moved *= settings.gain.forward; }
                else { moved *= settings.gain.backward; }

                // Current position.
                pos = Actor.transform.position;
                newRot = Actor.transform.rotation;

                pos[2] = pos[2] + (moved);

                //update position.
                if (Actor.isActive)
                {
                    Actor.transform.position = pos;
                    Actor.transform.rotation = newRot;
                }
            }

            // log. 
            if (settings.enableLogging && settings.isActive)
            {
                // Log raw input controller.
                logMsg.name = name;
                logMsg.move = (int)(movement.Sum().x * 1000);
                logger.logFile.Log("Linear Controller", logMsg);

            }

            // Clear buffer.
            movement.Clear();
        }

        void OnApplicationQuit()
        {
            if (this.GetType() == typeof(LinearTreadmill))
            {
                statusChannel.Send(new StatusMsg() { status = false });
            }
        }

        public void OnMessage(MSG msg)
        {
            lock (movement) { movement.Add(msg.movement, 0, 0); }
        }


        public override void LinkSettings(string assetPath = "")
        {
            LinearTreadmillSettings asset;
            if (assetPath == "")
            {
                asset = ScriptableObject.CreateInstance<LinearTreadmillSettings>();
                UnityEditor.AssetDatabase.CreateAsset(asset, string.Format("Assets/VRSettings/Controllers/{0}.asset", this.gameObject.name));
            }
            else
            {
                asset = (LinearTreadmillSettings)UnityEditor.AssetDatabase.LoadAssetAtPath(assetPath, typeof(LinearTreadmillSettings));
            }
            settings = asset;
        }
        public override void EditMenu()
        {
            SerializedObject serializedObject = new SerializedObject(settings);
            if (this.GetType() == typeof(SimulatedLinearTreadmill))
            {
                ControllerMenuTitle(settings.isActive, "Simulated Linear Treadmill");
                EditorGUILayout.LabelField("Device", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                    // Select controller.
                    EditorGUILayout.BeginHorizontal(LayoutSettings.editFieldOp);
                    if (EditorApplication.isPlaying) GUI.enabled = false;
                    if (settings.gamepadSettings.selectedGamepad >= deviceNames.Length) settings.gamepadSettings.selectedGamepad = 0;
                    settings.gamepadSettings.selectedGamepad = EditorGUILayout.Popup(settings.gamepadSettings.selectedGamepad, deviceNames);
                    if (GUILayout.Button("Rescan Devices")) { deviceNames = Gamepad.GetDeviceNames(); }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("buttonTopics"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("isActive"), new GUIContent("Active"), LayoutSettings.editFieldOp);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLogging"), new GUIContent("Log Input"), LayoutSettings.editFieldOp);
                EditorGUI.indentLevel--;
                GUI.enabled = true;
            }
            else {
                ControllerMenuTitle(settings.isActive, "Linear Treadmill");
                EditorGUILayout.LabelField("Device", EditorStyles.boldLabel);
                if (EditorApplication.isPlaying) GUI.enabled = false;
                EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("isActive"), new GUIContent("Active"), LayoutSettings.editFieldOp);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("deviceName"),new GUIContent("MQTT Name"), LayoutSettings.editFieldOp);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLogging"), new GUIContent("Log Input"), true, LayoutSettings.editFieldOp);
                EditorGUI.indentLevel--;
                GUI.enabled = true;
            }
        }
    }

}
