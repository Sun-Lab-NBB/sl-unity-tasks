using System;
using UnityEngine;
using UnityEditor;

public class Utility : MonoBehaviour
{
    public static float[] get_segment_lengths(GameObject[] segment_prefabs){
        int n_segments = segment_prefabs.Length; 
        float[] segment_lengths = new float[n_segments]; 
        for (int i = 0; i < n_segments; i++){
            segment_lengths[i] = get_prefab_length(segment_prefabs[i]);
        }

        return segment_lengths;
    }

    public static float get_prefab_length(GameObject prefab)
    {

        // Get all Renderers in the prefab
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();

        // Calculate the combined bounds
        Bounds combinedBounds = renderers[0].bounds;

        foreach (Renderer renderer in renderers)
        {
            combinedBounds.Encapsulate(renderer.bounds);
        }

        // Return the size of the prefab
        Vector3 size = combinedBounds.size;
        return size.z;
    }

    // Creates temp instance of gameobject which is unfavorable but may be more reliable
    public static float get_prefab_length_2(GameObject prefab){
        GameObject instance = Instantiate(prefab);
        instance.SetActive(false);        
        Renderer renderer = instance.GetComponentInChildren<Renderer>();
        Vector3 size = renderer.bounds.size;
        DestroyImmediate(instance);
        return size.z;
    }

}