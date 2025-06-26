using System;
using System.Collections.Generic;
using UnityEngine;

// Represents the entire JSON structure
[Serializable]
public class MazeSpec
{
    public Cue[] cues;
    public Segment[] segments;
    public Padding padding;

    public float corridor_spacing;
    public int segments_per_corridor;

    public void validate()
    {
        if (segments == null)
        {
            Debug.LogError("No segments specified.");
        }

        foreach (Segment segment in segments)
        {
            segment.validate();
        }

        if (padding == null)
        {
            Debug.LogError("No padding specified.");
        }

        padding.validate();

        if (corridor_spacing == 0f)
        {
            Debug.LogError("No corridor_spacing specified.");
        }

        if (segments_per_corridor == 0)
        {
            Debug.LogError("No segments_per_corridor specified.");
        }

    }

    // A dictionary mapping cue names to ids
    // The id of the cue is equivalent to the index it is at in cues
    public Dictionary<string, byte> get_cue_ids()
    {
        Dictionary<string, byte> cue_ids = new Dictionary<string, byte>();
        for (byte i = 0; i < cues.Length; i++)
        {
            cue_ids.Add(cues[i].name, i);
        }
        return cue_ids;
    }

    public float[] get_segment_lengths()
    {
        Dictionary<string, byte> cue_ids = get_cue_ids();
        float[] segment_lengths = new float[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            foreach (string cue in segments[i].cue_sequence)
            {
                segment_lengths[i] += cues[cue_ids[cue]].length;
            }
        }
        return segment_lengths;
    }

    public float[] get_cue_lengths()
    {
        Dictionary<string, byte> cue_ids = get_cue_ids();
        float[] cue_lengths = new float[cues.Length];
        for (int i = 0; i < cues.Length; i++)
        {
            cue_lengths[i] = cues[i].length;
        }
        return cue_lengths;
    }
}

[Serializable]
public class Cue
{
    public string name;
    public float length;
}


[Serializable]
public class Segment
{
    public string name;
    public string[] cue_sequence;
    public float[] transition_probabilities; //Optional

    public void validate(){
        if(name == null){
            Debug.LogError("A segment is missing a name.");
        }

        if(cue_sequence == null){
            Debug.LogError($"The segment named {name} is missing a cue sequence.");
        }

        if(transition_probabilities != null){
            float cum = 0f;
            foreach (float p in transition_probabilities){
                cum += p;
            }
            float epsilon = .001f;
            if(cum <= 1 - epsilon || cum >= 1 + epsilon){
                Debug.LogError($"The segment named {name} has transition probabilities whose sum is not close enough to 1");
            }
        }
    }
}

[Serializable]
public class Padding
{
    public string name;

    public void validate(){
        if(name == null){
            Debug.LogError("Padding is missing a name.");
        }
    }
}


