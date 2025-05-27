using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gimbl;
public class RewardLocation : MonoBehaviour
{
    // Task related variables.
    private bool inArea = false; //track that actor is in reward location.
    private bool correctLick = false; // tracks if animal reported reward location correctly.
    public bool isActive = true; //track if reward location is active (only give reward once per lap). Reset by resetlocation

    // MQTT Channels.
    MQTTChannel rewardTrigger; // Signals reward dispenser.
    MQTTChannel lickTrigger; // Listens for signal from lick port.

    // Logger.
    private LoggerObject logger; // For writing messages to the log.

    private Task task;
    private Gimbl.ActorObject actor; // Temporary addition for logging

    // Temporary addition for logging
    public class MSG{
        public float z; 
    }
    private MSG actorMSG; // Temporary addition for logging

    // Start is called before the first frame update
    void Start()
    {
        task = FindObjectOfType<Task>(); // Find task object to get parameters.
        actor = task.actor; // Temporary addition for logging
        actorMSG = new MSG(); // Temporary addition for logging
        actorMSG.z = actor.transform.position.z; // Temporary addition for logging
        // Setup MQTT channels.
        rewardTrigger = new MQTTChannel("Gimbl/Reward/");
        lickTrigger = new MQTTChannel("LickPort/",true);
        lickTrigger.Event.AddListener(LickDetected);
        // Get instance of logger.
        logger = FindObjectOfType<LoggerObject>();
    }

    // Update is called once per frame
    void Update()
    {
        actorMSG.z = actor.transform.position.z; // Temporary addition for logging      
        // check for reward delivery in no-lick condition
        if (isActive && inArea && task.mustLick == false) { Reward(); }
        // Check for reward condition in must lick condition.
        if (isActive && inArea && task.mustLick == true && correctLick == true) { Reward(); }
    }
    // Gets called when actor enters collider
    public void OnTriggerEnter(Collider collider) {  inArea = true; }

    // Gets called when actor exits collider area.
    public void OnTriggerExit(Collider collider) { inArea = false; }

    private void Reward()
    {
        Debug.Log("Reward");
        GetComponent<AudioSource>().Play(); // Play sound.
        GetComponent<MeshRenderer>().enabled = false; // hide marker.
        rewardTrigger.Send(); // Send reward message over MQTT 
        // logger.logFile.Log("Reward", actorMSG); // Temporary addition for logging
        // prevent multiple rewards.
        isActive = false; 
        correctLick = false;
    }

    // Gets called on message from lickport and checks if the animal is in reward location for correct response.
    private void LickDetected()
    {
        Debug.Log("Lick!");
        // logger.logFile.Log("Lick", actorMSG); // Temporary addition for logging
        if (isActive && inArea) { correctLick = true; }
    }

}

