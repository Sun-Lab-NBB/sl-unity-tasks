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

    private GuidanceRegion GuidanceRegionScript;

    // MQTT Channels.
    MQTTChannel rewardTrigger; // Signals reward dispenser.
    MQTTChannel lickTrigger; // Listens for signal from lick port.

    private Task task;


    // Start is called before the first frame update
    void Start()
    {
        task = FindAnyObjectByType<Task>(); // Find task object to get parameters.

        // Look for a guidance region script in children game objects. This guidance region delays the reward in 
        // guidance mode. This gives the mouse the opportunity to get the reward from licking manually even in guidance
        // mode.
        GuidanceRegionScript = GetComponentInChildren<GuidanceRegion>();

        // Setup MQTT channels.
            rewardTrigger = new MQTTChannel("Gimbl/Reward/");
        lickTrigger = new MQTTChannel("LickPort/", true);
        lickTrigger.Event.AddListener(LickDetected);
    }

    // Update is called once per frame
    void Update()
    {
        if (task.mustLick) //Guidance mode disabled, mouse must lick for reward
        {
            // Mouse must lick in the reward region to receive reward
            if (isActive && inArea && correctLick)
            {
                Reward();
            }
        }
        else if (GuidanceRegionScript != null) //Guidance mode with special guidance region where the reward is delivered
        {
            if (isActive && inArea && correctLick) // The mouse can lick recieve a reward by licking anywhere in the reward region, just like when guidance is disabled
            {
                Reward();
            }
            else if (isActive && GuidanceRegionScript.inArea) // If the mouse gets to the guidance region and hasn't licked, give it a reward anyway
            {
                Reward();
            }
        }
        else // Guidance mode but no special guidance region
        {
            if (isActive && inArea) // mouse gets reward as soon as it enters the reward region
            {
                Reward();
            }
        }

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
        // prevent multiple rewards.
        isActive = false; 
        correctLick = false;
    }

    // Gets called on message from lickport and checks if the animal is in reward location for correct response.
    private void LickDetected()
    {
        Debug.Log("Lick!");
        if (isActive && inArea) { correctLick = true; }
    }

}

