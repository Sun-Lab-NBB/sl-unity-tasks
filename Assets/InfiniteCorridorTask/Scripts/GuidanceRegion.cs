using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GuidanceRegion : MonoBehaviour
{

    [HideInInspector]
    public bool inArea = false;

    void Start()
    {

    }
    void OnTriggerEnter(Collider other)
    {
        inArea = true;
    }

    void OnTriggerExit(Collider other)
    {
        inArea = false;
    }
}
