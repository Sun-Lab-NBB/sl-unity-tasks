using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResetLocation : MonoBehaviour
{
    private RewardLocation[] rewardLocations; // Array to hold all instances of RewardLocation
    private Task task;

    // Start is called before the first frame update
    void Start()
    {
        // Find all instances of RewardLocation in the scene
        rewardLocations = FindObjectsOfType<RewardLocation>();
        task = FindObjectOfType<Task>();
    }

    // Called when actor enters reset location.
    public void OnTriggerEnter(Collider collider)
    {
        // Loop through all reward locations and update their isActive state
        foreach (RewardLocation rewardLocation in rewardLocations)
        {
            // Set marker visible/invisible
            if (task.visibleMarker)
            {
                rewardLocation.GetComponent<MeshRenderer>().enabled = true;
            }
            else
            {
                rewardLocation.GetComponent<MeshRenderer>().enabled = false;
            }
            rewardLocation.isActive = true; // Activate each reward location
        }
    }
}
