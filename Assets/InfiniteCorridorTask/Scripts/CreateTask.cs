using System;
using UnityEngine;
using UnityEditor;
using Unity.VisualScripting;
using System.IO;

public class CreateTask : MonoBehaviour
{

    [MenuItem("CreateTask/New Task")]
    public static void createTask(){

        string meta_data_path = EditorUtility.OpenFilePanel("Select Maze Spec JSON", Application.dataPath + "/InfiniteCorridorTask/Tasks/", "json").Replace(Application.dataPath, "");

        if (string.IsNullOrEmpty(meta_data_path))
        {
            Debug.LogError("No maze specification json file selected.");
            return;
        }

        string jsonString = File.ReadAllText(Application.dataPath + meta_data_path);
        MazeSpec maze_spec = JsonUtility.FromJson<MazeSpec>(jsonString);

        string prefabs_path = "Assets/InfiniteCorridorTask/Prefabs/";

        string padding_path = prefabs_path + maze_spec.padding.name + ".prefab";
        GameObject padding = AssetDatabase.LoadAssetAtPath<GameObject>(padding_path);

        if(padding == null){
            Debug.LogError("No padding found at " + padding_path);
            return;
        }
        int n_segments = maze_spec.segments.Length;

        GameObject[] segment_prefabs = new GameObject[n_segments];

        for(int i = 0; i < n_segments; i++){
            string segment_path = prefabs_path + maze_spec.segments[i].name + ".prefab";
            segment_prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(segment_path);

            if(segment_prefabs[i] == null){
                Debug.LogError("No segment found at " + segment_path);
                return;
            }
        }

        float[] measured_segment_lengths = Utility.get_segment_lengths(segment_prefabs);

        float[] segment_lengths = maze_spec.get_segment_lengths();

        float epsilon = .01f;
        for(int i = 0; i < n_segments; i++){
            if(Mathf.Abs(measured_segment_lengths[i] - segment_lengths[i]) > epsilon){
                Debug.Log($"Warning: For {maze_spec.segments[i]}, there is a mismatch between the prefab length ({measured_segment_lengths[i]}) and the sum of all the cue lengths ({segment_lengths[i]}). Using {segment_lengths[i]} for the length of the segment.");
            }
        }

        int depth = maze_spec.segments_per_corridor;

        float padding_z_shift = depth * Mathf.Min(segment_lengths) - 1;

        string new_task_name = "newTask";
        GameObject task = new GameObject(new_task_name);
        Task task_script = task.AddComponent<Task>();
        task_script.mustLick = true;
        task_script.visibleMarker = false;
        task_script.meta_data_path = meta_data_path;

        int[] corridor_segments = new int[depth];
        // int[] corridor_segments_reversed = new int[depth];
        int segment;
        float cur_corridor_x = 0;
        float corridor_x_shift = maze_spec.corridor_spacing;
        float z_shift;
        // Iterate through all possible combinations
        for (int i = 0; i < Mathf.Pow(n_segments, depth); i++)
        {
            
            // Generate the combination for the current index
            for (int j = 0; j < depth; j++) 
            {
                corridor_segments[j] = i / (int)Mathf.Pow(n_segments, depth - j - 1) % n_segments;
                // corridor_segments_reversed[depth - j - 1] = corridor_segments[j];
            }


            GameObject corridor = new GameObject($"Corridor{string.Join("", corridor_segments)}");
            corridor.transform.SetParent(task.transform);
            corridor.transform.localPosition = new Vector3(cur_corridor_x, 0, 0);

            z_shift = 0;
            for (int j = 0; j < depth; j++){
                segment = corridor_segments[j];
                GameObject instance = PrefabUtility.InstantiatePrefab(segment_prefabs[segment]) as GameObject;
                // Only the first segment in each corridor should have a reward location and reset location since the later segments are just for visual illusion
                if(j > 0){
                    Transform reward_location = instance.transform.Find("RewardLocation");
                    if (reward_location != null){
                        GameObject.DestroyImmediate(reward_location.gameObject);
                    }
                    Transform reset_location = instance.transform.Find("ResetLocation");
                    if (reset_location != null){
                        GameObject.DestroyImmediate(reset_location.gameObject);
                    }
                }
                instance.transform.SetParent(corridor.transform, false);
                instance.transform.localPosition += new Vector3(0, 0, z_shift);
                z_shift += segment_lengths[segment];
            }

            GameObject padding_instance = PrefabUtility.InstantiatePrefab(padding) as GameObject;
            padding_instance.transform.SetParent(corridor.transform, false);
            padding_instance.transform.localPosition += new Vector3(0, 0, padding_z_shift);

            cur_corridor_x += corridor_x_shift;
        }

        // Open Save File Panel for user to specify location and name of prefab
        string savePath = EditorUtility.SaveFilePanel("Save Task Prefab", Application.dataPath + "/InfiniteCorridorTask/Tasks/", new_task_name + ".prefab", "prefab");

        if (string.IsNullOrEmpty(savePath))
        {
            Debug.LogError("User did not select a save location.");
            return;
        }

        savePath = FileUtil.GetProjectRelativePath(savePath);
        PrefabUtility.SaveAsPrefabAsset(task, savePath);
        DestroyImmediate(task);
    } 

    private static void count(){
        int depth = 3; // Number of digits
        int n_segments = 2; // Number of options (0, 1)

        // Iterate through all possible combinations
        for (int i = 0; i < Mathf.Pow(n_segments, depth); i++)
        {
            // Create an array to store the current combination
            int[] combination = new int[depth];
            
            // Generate the combination for the current index
            for (int j = 0; j < depth; j++) // Leftmost number increments first
            {
                combination[j] = i / (int)Mathf.Pow(n_segments, depth - j - 1) % n_segments;
            }
            
            // Print the current combination in reverse order
            Array.Reverse(combination); // Reverse the array before printing
            Debug.Log(string.Join(", ", combination));
        }
    }
}