using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewRewardLocation : MonoBehaviour
{

    
    void Start()
    {
        Debug.Log("Hello World");
    }
    void OnTriggerEnter(Collider other)
    {
        Debug.Log("trigger enter");
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log("trigger exit");
    }
}
