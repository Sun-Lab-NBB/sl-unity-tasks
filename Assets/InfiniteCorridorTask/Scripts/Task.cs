using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using Gimbl;
using UnityEditor.Search;
using UnityEngine.Rendering;
using UnityEngine.AI;
public class Task : MonoBehaviour
{

    // Some words for parts of the maze:
    //  Cue: A certain pattern on a wall
    //       This task has cues A, B, C, D which are named 1, 2, 3, 4
    //  Segment: A portion of the maze that cycles back to the start cue
    //       This task has segments 1, 2
    //          Segment 1 has the following cues: A B C
    //          Segment 2 has the following cues: A B D C       
    //  Corridor: A grouping segments
    //       This task has all 8 corridors, which includes all of the possible length three orderings of segment 1 and 2
    //          ex. Corridor 121 has the following segments: 1 2 1

    public bool mustLick = false;
    public bool visibleMarker = true;

    public Gimbl.ActorObject actor = null;

    // The track is infinite but need to specify how many random segments keep track of. The 
    // track length should always be an overestimate to how far the mouse is actually going to run.
    public float trackLength = 15000;

    // A seed for creation of random segments, a specific seed will always create the same pattern of cues.
    // If trackSeed is -1, then no seed will be used.
    public int trackSeed = -1;

    // For keeping track of where in the random sequence the mouse is.
    private int current_segment_index;

    // Each time the mouse completes a segment, it will go into a new random segment. (either 1 or 2) The segment sequence array holds the order of segments
    private int[] segment_sequence_array;

    // Holds the order of cues
    private byte[] cue_sequence_array;

    // A wrapper class for sending cue_sequence_array over MQTT
    public class SequenceMsg
    {
        public byte[] cue_sequence;
    }
    private MQTTChannel cueSequenceTrigger;
    private MQTTChannel<SequenceMsg> cueSequenceChannel;

    private MQTTChannel mustLickTrue;
    private MQTTChannel mustLickFalse;

    private MQTTChannel visibleMarkerTrue;
    private MQTTChannel visibleMarkerFalse;

    private MQTTChannel showDisplay;
    private MQTTChannel blankDisplay;

    private DisplayObject[] displayObjects;

    private int depth;

    private int n_segments;

    private MazeSpec maze_spec;

    // [System.NonSerialized]
    public string meta_data_path;

    private Dictionary<string, byte> cue_ids;
    private float[] segment_lengths;
    private float[] cue_lengths;

    private Dictionary<string, (float, float)> corridorMap;

    private List<int> cur_segment;
    private Vector3 pos;


    void OnValidate()
    {
        if (actor == null)
        {
            Gimbl.ActorObject[] all_actors = FindObjectsOfType<Gimbl.ActorObject>();
            if (all_actors.Length > 0)
            {
                actor = all_actors[0];
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("On branch gutted");

        string global_meta_data_path = Application.dataPath + meta_data_path;

        if (string.IsNullOrEmpty(meta_data_path) || !File.Exists(global_meta_data_path))
        {
            Debug.LogError("No maze specification JSON file found at the specified path.");
            return;
        }

        string jsonString = File.ReadAllText(global_meta_data_path);
        maze_spec = JsonUtility.FromJson<MazeSpec>(jsonString);

        n_segments = maze_spec.segments.Length;
        cue_ids = maze_spec.get_cue_ids();
        segment_lengths = maze_spec.get_segment_lengths();
        depth = maze_spec.segments_per_corridor;

        // To teleport the mouse correctly between corridors, you need to know when to teleport (ie when the first 
        // segment of the current corridor ends) and where to teleport (ie where the first segment of the next corridor
        // starts). Corridor map holds this info, the first float is the position of the corridor and the second float 
        // is the length of the first segment in the corridor
        corridorMap = new Dictionary<string, (float, float)>();

        int[] corridor_segments = new int[depth];
        float cur_corridor_x = 0;
        float corridor_x_shift = maze_spec.corridor_spacing;
        for (int i = 0; i < Mathf.Pow(n_segments, depth); i++)
        {
            // Generate the combination for the current index
            for (int j = 0; j < depth; j++)
            {
                corridor_segments[j] = i / (int)Mathf.Pow(n_segments, depth - j - 1) % n_segments;
                // corridor_segments_reversed[depth - j - 1] = corridor_segments[j];
            }

            corridorMap[string.Join("-", corridor_segments)] = (cur_corridor_x, segment_lengths[corridor_segments[0]]);
            cur_corridor_x += corridor_x_shift;
        }

        // Create random sequence of segments
        (segment_sequence_array, cue_sequence_array) = generateRandomMaze(trackLength, trackSeed);


        // Figure out what the first corridor is from the first three segments
        current_segment_index = 0;
        cur_segment = new List<int>(segment_sequence_array.Take(depth));


        if (actor != null)
        {
            pos = actor.transform.position;
            pos.x = corridorMap[string.Join("-", cur_segment)].Item1;
            actor.transform.position = pos;
        }

        // Create MQTT channels for sending cue sequence
        cueSequenceTrigger = new MQTTChannel("CueSequenceTrigger/", true);
        cueSequenceTrigger.Event.AddListener(OnCueSequenceTrigger);
        cueSequenceChannel = new MQTTChannel<SequenceMsg>("CueSequence/", false);

        // Create MQTT channel for toggling mustLick
        mustLickTrue = new MQTTChannel("MustLick/True/", true);
        mustLickTrue.Event.AddListener(setMustLickTrue);

        mustLickFalse = new MQTTChannel("MustLick/False/", true);
        mustLickFalse.Event.AddListener(setMustLickFalse);

        // Create MQTT channel for toggling visibleMarker
        visibleMarkerTrue = new MQTTChannel("VisibleMarker/True/");
        visibleMarkerTrue.Event.AddListener(setVisibleMarkerTrue);

        visibleMarkerFalse = new MQTTChannel("VisibleMarker/False/");
        visibleMarkerFalse.Event.AddListener(setVisibleMarkerFalse);


        // Create MQTT channels for blacking out and displaying the screen
        displayObjects = FindObjectsOfType<DisplayObject>();
        showDisplay = new MQTTChannel("Display/Show/", true);
        showDisplay.Event.AddListener(show);
        blankDisplay = new MQTTChannel("Display/Blank/", true);
        blankDisplay.Event.AddListener(blank);

    }


    // Update is called once per frame
    void Update()
    {
        if (actor != null)
        {
            pos = actor.transform.position;
            // Check if the mouse has traveled through the entire segment
            if (pos.z > corridorMap[string.Join("-", cur_segment)].Item2)
            {

                // Teleport the mouse back to the start of the corridors
                pos.z -= corridorMap[string.Join("-", cur_segment)].Item2;

                // Switch to a different corridor according to the future segments
                current_segment_index++;
                if (current_segment_index <= segment_sequence_array.Length - depth)
                {
                    cur_segment.RemoveAt(0);
                    cur_segment.Add(segment_sequence_array[current_segment_index + depth - 1]);
                }
                else
                {
                    throw new System.Exception("Mouse ran through all generated segments.");
                }

                // Teleport the mouse to the new corridor
                pos.x = corridorMap[string.Join("-", cur_segment)].Item1;
                actor.transform.position = pos;
            }
        }
        else
        {
            Debug.LogError("Actor is null.");
        }

    }

    private int SampleFromDistribution(float[] probabilities, System.Random random)
    {
        float r = (float)random.NextDouble();
        float cumulative = 0f;

        for (int i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (r < cumulative)
                return i;
        }

        return probabilities.Length - 1;
    }

    // private int StringToIndex(string input)
    // {
    //     int result = 0;
    //     foreach (char c in input.ToUpper())
    //     {
    //         result = result * 26 + (c - 'A' + 1);
    //     }
    //     return result;
    // }


    /// <summary>
    /// Generates a random sequence of maze segments based on the specified length and optional seed.
    /// </summary>
    /// <param name="length">The total desired length of the maze sequence.</param>
    /// <param name="seed">An optional seed value for the random number generator. If null or -1, a new random generator is used.</param>
    /// <returns>
    /// A tuple containing two arrays:
    /// - An integer array representing the sequence of segments in the maze.
    /// - A byte array representing the cues associated with the maze sequence.
    /// </returns>
    private (int[], byte[]) generateRandomMaze(float length, int? seed = null)
    {
        float sequence_length = 0;

        System.Random random = seed.HasValue && seed != -1 ? new System.Random(seed.Value) : new System.Random();

        List<int> segment_sequence = new List<int>();
        List<byte> cue_sequence = new List<byte>();


        int choice = random.Next(n_segments);

        while (sequence_length < length)
        {
            segment_sequence.Add(choice);

            foreach (string cue in maze_spec.segments[choice].cue_sequence)
            {
                cue_sequence.Add(cue_ids[cue]);
            }

            sequence_length += segment_lengths[choice];

            if (maze_spec.segments[choice].transition_probabilities != null)
            {
                choice = SampleFromDistribution(maze_spec.segments[choice].transition_probabilities, random);
            }
            else
            {
                choice = random.Next(n_segments);
            }
        }

        int[] segment_sequence_array = segment_sequence.ToArray();
        byte[] cue_sequence_array = cue_sequence.ToArray();

        return (segment_sequence_array, cue_sequence_array);

    }

    private void OnCueSequenceTrigger()
    {
        Debug.Log("received request for cue sequence");
        cueSequenceChannel.Send(new SequenceMsg() { cue_sequence = cue_sequence_array });
    }

    private void blank()
    {
        foreach (DisplayObject display in displayObjects)
        {
            display.Blank();
        }
    }

    private void show()
    {
        foreach (DisplayObject display in displayObjects)
        {
            display.Show();
        }
    }

    private void setMustLickTrue()
    {
        mustLick = true;
    }
    private void setMustLickFalse()
    {
        mustLick = false;
    }

    private void setVisibleMarkerTrue()
    {
        visibleMarker = true;
    }

    private void setVisibleMarkerFalse()
    {
        visibleMarker = false;
    }

    private float calculateAbsoluteDistance()
    {

        float sum = 0;
        for (int i = 0; i < current_segment_index; i++)
        {
            sum += current_segment_index;
            maze_spec.get_segment_lengths();

        }
        return 0.0F;
    }

}

