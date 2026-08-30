/// <summary>
/// Provides the CreateTask class that generates Task prefabs and matching test scenes from YAML configuration
/// files via the Unity Editor menu. Mirrors the agentic create_task pipeline in a single Editor entry point
/// so a YAML edit produces a runnable scene without leaving the Editor.
/// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gimbl;
using SL.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks
{
    /// <summary>
    /// Creates Task prefabs from task template files via Unity Editor.
    /// Generates all corridor combinations by instantiating segment prefabs and configuring zones.
    /// </summary>
    internal static class CreateTask
    {
        /// <summary>The tolerance for comparing measured prefab lengths against configured lengths.</summary>
        private const float LengthComparisonEpsilon = 0.01f;

        /// <summary>The project-relative folder where generated task prefabs are saved.</summary>
        private const string TasksFolder = "Assets/InfiniteCorridorTask/Tasks";

        /// <summary>The project-relative folder where generated task scenes are saved.</summary>
        private const string ScenesFolder = "Assets/Scenes";

        /// <summary>
        /// The project-relative path to the canonical scene template that scene generation copies from.
        /// </summary>
        /// <remarks>
        /// The template is hand-authored and contains the Display rig, Actor, and any other scene-wide infrastructure
        /// that every task scene needs. ``McpBridge`` protects this path from deletion via its protected-paths list so
        /// a regenerated scene always has a known-good source. Updating the path here requires a matching update to the
        /// protected-paths list in <c>McpBridge.DeleteProtectedPaths</c>.
        /// </remarks>
        private const string TemplateScenePath = "Assets/Scenes/ExperimentTemplate.unity";

        /// <summary>The project-relative root folder for every InfiniteCorridorTask-owned asset.</summary>
        private const string BaseFolder = "Assets/InfiniteCorridorTask";

        /// <summary>The project-relative folder containing per-task YAML configuration templates.</summary>
        private const string ConfigurationsFolder = BaseFolder + "/Configurations";

        /// <summary>The project-relative folder holding generated and hand-authored segment prefabs.</summary>
        private const string PrefabsFolder = BaseFolder + "/Prefabs";

        /// <summary>The project-relative folder holding shared cue prefabs.</summary>
        private const string CuesFolder = BaseFolder + "/Cues";

        /// <summary>The project-relative folder holding shared cue, floor, and wall materials.</summary>
        private const string MaterialsFolder = BaseFolder + "/Materials";

        /// <summary>The project-relative folder holding cue textures referenced by templates.</summary>
        private const string TexturesFolder = BaseFolder + "/Textures";

        /// <summary>The canonical reference material whose shader is reused by all generated cue materials.</summary>
        /// <remarks>
        /// This material lives in source control and is protected from deletion via the McpBridge's protected-paths
        /// list. Its shader (built-in fileID 10708, a legacy diffuse variant) renders both walls of a cue correctly
        /// even when the Right wall uses a negative geometry scale to mirror its texture. The Standard shader breaks
        /// under negative scales, and Unlit shaders drop lighting altogether.
        /// </remarks>
        private const string CueShaderReferenceMaterialPath = MaterialsFolder + "/_CueShaderReference.mat";

        /// <summary>The vertical offset for trigger-zone GameObjects, slightly above the segment floor.</summary>
        private const float ZoneVerticalOffset = 0.505f;

        /// <summary>The vertical center for cue walls and segment walls.</summary>
        private const float WallVerticalCenter = 0.5f;

        /// <summary>
        /// The Z-axis depth of guidance-zone box colliders in interaction and occupancy zones, and of the
        /// thin boundary wall collider in collision zones.
        /// </summary>
        private const float GuidanceColliderDepth = 0.4f;

        /// <summary>
        /// Formats a cue's centimeter length as the suffix used in cue prefab and material filenames.
        /// Returns a culture-invariant string with up to two decimals and no trailing zeros (e.g., "30", "37.5").
        /// </summary>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <returns>The length label used inside ``Cue_{name}_{label}cm`` asset filenames.</returns>
        private static string FormatCueLengthLabel(float lengthCm) =>
            lengthCm.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Computes the canonical prefab name for a trial's segment as ``TemplateName-TrialName``. The template name
        /// comes from the YAML filename (without extension) and the trial name is the key under ``trial_structures``.
        /// </summary>
        /// <remarks>
        /// <see cref="ConfigLoader"/> restricts both halves to ASCII letters, digits, and underscores, so the hyphen
        /// separator appears exactly once and the joined name splits back into one template and one trial. That makes
        /// a segment globally unique across templates even where one template basename nests another.
        /// </remarks>
        /// <param name="template">The task template owning the trial, which supplies the template name.</param>
        /// <param name="trialName">The trial key under ``trial_structures``.</param>
        /// <returns>The canonical segment prefab name (without the ``.prefab`` extension).</returns>
        private static string CanonicalSegmentName(TaskTemplate template, string trialName) =>
            $"{template.templateName}-{trialName}";

        /// <summary>
        /// Creates a new Task prefab and a matching scene from a selected YAML configuration file. Save paths and asset
        /// names are auto-resolved from the template filename so the menu flow matches the MCP-driven pipeline: the
        /// prefab lands at ``Assets/InfiniteCorridorTask/Tasks/{templateName}.prefab`` and the scene at
        /// ``Assets/Scenes/{templateName}.unity``. The user is only prompted for the template selection and, when an
        /// existing prefab or scene would be overwritten, a single confirmation dialog before any mutation occurs.
        /// </summary>
        /// <remarks>
        /// Rejects template selections outside ``Assets/InfiniteCorridorTask/Configurations/`` so the Editor surface
        /// matches the MCP surface (which is already hard-coded to that folder) and so the cross-template cue-texture
        /// preflight in <see cref="CreateFromTemplate"/> sees every template that can drive generation. Constraining
        /// the menu also keeps the runtime-resolved ``relativeConfigPath`` stored on the Task component well-formed: a
        /// YAML selected from outside ``Application.dataPath`` would otherwise yield a malformed path that breaks the
        /// runtime template lookup later. The scene generation step is the final phase so the user sees the prefab
        /// result before scene work begins, and so a failed prefab build short-circuits before any scene is touched.
        /// </remarks>
        [MenuItem("CreateTask/New Task")]
        public static void CreateNewTask()
        {
            // Normalizes to forward slashes so the prefix check works uniformly on Windows, where ``Path.Combine``
            // returns mixed separators but ``EditorUtility.OpenFilePanel`` returns forward slashes per Unity's
            // documented behavior.
            string dataPath = Application.dataPath.Replace('\\', '/');
            string configurationsDirectory = Path.Combine(dataPath, "InfiniteCorridorTask", "Configurations")
                .Replace('\\', '/');

            string absoluteSelectedPath = EditorUtility
                .OpenFilePanel("Select Task Template YAML", configurationsDirectory, "yaml,yml")
                .Replace('\\', '/');

            if (string.IsNullOrEmpty(absoluteSelectedPath))
            {
                string message =
                    "Unable to generate a task. A configuration YAML file must be selected, but the file panel "
                    + "returned no selection.";
                Debug.LogError(message);
                return;
            }

            // Enforces that templates live under ``Configurations/``. A trailing slash is required so a
            // sibling directory whose name begins with ``Configurations`` cannot satisfy the prefix.
            string configurationsPrefix = configurationsDirectory.TrimEnd('/') + "/";
            if (!absoluteSelectedPath.StartsWith(configurationsPrefix, StringComparison.Ordinal))
            {
                string message =
                    "Unable to generate from the selected template. The template must sit under the canonical "
                    + $"Configurations directory '{configurationsDirectory}', but it is at "
                    + $"'{absoluteSelectedPath}'. Move the template into Configurations/ before generating, "
                    + "because only files under that folder are visible to MCP-driven generation and to the "
                    + "cross-template cue-texture preflight.";
                Debug.LogError(message);
                return;
            }

            // Auto-resolves every downstream path from the template filename. The template name is also the
            // prefab basename, the scene basename, and the segment-prefab prefix, matching the MCP flow's
            // conventions so menu-generated and agent-generated assets are byte-equivalent.
            string templateName = Path.GetFileNameWithoutExtension(absoluteSelectedPath);
            string configPath = absoluteSelectedPath.Substring(dataPath.Length).TrimStart('/');
            string prefabSavePath = Path.Combine(TasksFolder, $"{templateName}.prefab").Replace('\\', '/');
            string sceneSavePath = Path.Combine(ScenesFolder, $"{templateName}.unity").Replace('\\', '/');

            // Confirms overwrite up front when either auto-resolved target already exists. Doing this before any
            // mutation lets the user cancel without leaving the project in a half-regenerated state and keeps the
            // destructive nature of the flow visible. Auto-resolution removes the OS file-panel's built-in overwrite
            // prompt, so the flow surfaces it explicitly.
            List<string> existingTargets = new List<string>();
            if (File.Exists(prefabSavePath))
            {
                existingTargets.Add(prefabSavePath);
            }
            if (File.Exists(sceneSavePath))
            {
                existingTargets.Add(sceneSavePath);
            }
            if (existingTargets.Count > 0)
            {
                string dialogMessage =
                    $"The following assets will be replaced:\n\n  {string.Join("\n  ", existingTargets)}"
                    + "\n\nAny hand-edits to those files will be lost. Continue?";
                bool proceed = EditorUtility.DisplayDialog(
                    title: $"Regenerate '{templateName}' Assets",
                    message: dialogMessage,
                    ok: "Replace",
                    cancel: "Cancel"
                );
                if (!proceed)
                {
                    Debug.Log("Task generation cancelled.");
                    return;
                }
            }

            // Ensures the Tasks output folder exists before CreateFromTemplate writes the prefab. The Scenes folder is
            // part of the project skeleton and is assumed to exist. If it does not, CreateSceneFromTemplate's CopyAsset
            // call will surface the failure.
            if (!AssetDatabase.IsValidFolder(TasksFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Tasks");
            }

            string absoluteTemplatePath = Path.Combine(Application.dataPath, configPath);
            string prefabResult = CreateFromTemplate(absoluteTemplatePath, configPath, prefabSavePath);
            Debug.Log(prefabResult);

            // Skips scene generation when prefab creation failed. The scene step depends on the prefab
            // existing on disk and would otherwise emit a confusing "task prefab not found" warning.
            if (!prefabResult.StartsWith("success:", StringComparison.Ordinal))
            {
                return;
            }

            // Defers to Unity's built-in unsaved-changes dialog before opening the new scene. Returning false means the
            // user pressed Cancel, which aborts the scene step alone. The already-generated prefab is left in place so
            // a follow-up run can finish the scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                string message =
                    $"Scene generation cancelled — unsaved changes in the active scene were not handled. "
                    + $"The prefab is at {prefabSavePath}; rerun the menu to regenerate the scene.";
                Debug.Log(message);
                return;
            }

            SceneCreationResult sceneResult = CreateSceneFromTemplate(
                sceneSavePath: sceneSavePath,
                taskPrefabPath: prefabSavePath,
                overwriteExisting: true
            );

            if (!sceneResult.Success)
            {
                Debug.LogError($"error: {sceneResult.Message}");
                return;
            }

            Debug.Log($"success: {sceneResult.Message}");
        }

        /// <summary>Creates a Task prefab from a YAML template file and saves it to the specified path.</summary>
        /// <param name="absoluteTemplatePath">The absolute path to the YAML template file.</param>
        /// <param name="relativeConfigPath">
        /// The config path relative to Application.dataPath, stored on the Task component for runtime loading.
        /// </param>
        /// <param name="savePath">
        /// The project-relative path where the prefab will be saved (e.g., "Assets/.../Task.prefab").
        /// </param>
        /// <returns>A status message describing success or the error encountered.</returns>
        public static string CreateFromTemplate(string absoluteTemplatePath, string relativeConfigPath, string savePath)
        {
            // Runs the cross-template cue-texture preflight before any mutation. Cue prefabs are shared
            // across templates by ``(name, lengthCm)`` only, so two templates that declare a cue with the
            // same name and length but different textures would silently overwrite each other depending
            // on generation order. The preflight aborts here, before ``CleanGeneratedSegments`` or any
            // cue/segment build runs, so the project state stays consistent until the conflict is resolved.
            if (!ValidateCueDefinitionsAcrossTemplates(out string preflightError))
            {
                return $"error: {preflightError}";
            }

            TaskTemplate template;
            try
            {
                template = ConfigLoader.LoadTemplate(absoluteTemplatePath);
            }
            catch (Exception exception)
            {
                return $"error: {exception.Message}";
            }

            string lengthError = ValidateTrackLengthCoversCorridor(template);
            if (lengthError != null)
            {
                return $"error: {lengthError}";
            }

            string missingAssetError = ValidateHandAuthoredAssets(template);
            if (missingAssetError != null)
            {
                return $"error: {missingAssetError}";
            }

            // Builds cues before wiping the previous segments, because a missing texture or a conflicting cached
            // material aborts here and a wipe that ran first would leave the existing task prefab and scene
            // referencing segments this call is unable to rebuild.
            if (!BuildCuePrefabs(template))
            {
                return "error: Unable to generate the task. Every cue prefab the template declares must build, "
                    + "but at least one failed. The preceding error names the cue.";
            }

            // Wipes any segment prefabs this template previously generated so trial-parameter edits never result in
            // stale segment geometry surviving under an unchanged ``TemplateName-TrialName`` filename. Cue prefabs and
            // materials are deliberately preserved because they are shared across templates by cue name and length.
            // ``BuildCuePrefabs`` rebuilds only the cues that are still missing.
            CleanGeneratedSegments(template);

            if (!BuildSegmentPrefabs(template))
            {
                return "error: Unable to generate the task. Every segment prefab the template declares must "
                    + "build, but at least one failed. The preceding error names the segment.";
            }

            string paddingPath = Path.Combine(PrefabsFolder, $"{template.vrEnvironment.paddingPrefabName}.prefab");
            GameObject padding = AssetDatabase.LoadAssetAtPath<GameObject>(paddingPath);

            if (padding == null)
            {
                return "error: Unable to assemble the corridor. The padding prefab must exist at "
                    + $"'{paddingPath}', but it is missing.";
            }

            string[] trialNames = template.GetTrialNames();
            int trialCount = trialNames.Length;

            // Loads segment prefabs by their canonical ``TemplateName-TrialName`` filename.
            GameObject[] segmentPrefabs = new GameObject[trialCount];
            TrialStructure[] trials = new TrialStructure[trialCount];
            for (int i = 0; i < trialCount; i++)
            {
                trials[i] = template.trialStructures[trialNames[i]];
                string canonicalName = CanonicalSegmentName(template, trialNames[i]);
                string segmentPath = Path.Combine(PrefabsFolder, $"{canonicalName}.prefab");
                segmentPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(segmentPath);

                if (segmentPrefabs[i] == null)
                {
                    return "error: Unable to assemble the corridor. The segment prefab for trial "
                        + $"'{trialNames[i]}' must exist at '{segmentPath}', but it is missing.";
                }
            }

            float[] measuredSegmentLengths = new float[segmentPrefabs.Length];
            for (int i = 0; i < segmentPrefabs.Length; i++)
            {
                measuredSegmentLengths[i] = Utility.GetPrefabLength(segmentPrefabs[i]);
            }
            float[] segmentLengths = template.GetSegmentLengthsUnity();

            for (int i = 0; i < trialCount; i++)
            {
                if (Mathf.Abs(measuredSegmentLengths[i] - segmentLengths[i]) > LengthComparisonEpsilon)
                {
                    string message =
                        $"Unable to reconcile the measured length of trial {trialNames[i]}. The prefab length "
                        + $"({measuredSegmentLengths[i]}) must match the sum of all cue lengths "
                        + $"({segmentLengths[i]}), but the two differ. Using {segmentLengths[i]} for the length "
                        + "of the segment.";
                    Debug.LogWarning(message);
                }
            }

            int depth = template.vrEnvironment.segmentsPerCorridor;
            float cueOffsetUnity = template.vrEnvironment.CueOffsetUnity;

            string taskName = Path.GetFileNameWithoutExtension(savePath);
            GameObject taskGameObject = new GameObject(taskName);
            Task taskScript = taskGameObject.AddComponent<Task>();
            taskScript.requireInteraction = true;
            taskScript.configPath = relativeConfigPath;

            int[] corridorSegments = new int[depth];
            float currentCorridorX = 0;
            float corridorXShift = template.vrEnvironment.CorridorSpacingUnity;

            for (int i = 0; i < Mathf.Pow(trialCount, depth); i++)
            {
                for (int j = 0; j < depth; j++)
                {
                    corridorSegments[j] = i / (int)Mathf.Pow(trialCount, depth - j - 1) % trialCount;
                }

                GameObject corridor = new GameObject($"Corridor{string.Join("", corridorSegments)}");
                corridor.transform.SetParent(taskGameObject.transform);
                corridor.transform.localPosition = new Vector3(currentCorridorX, 0, 0);

                float zShift = 0;
                for (int j = 0; j < depth; j++)
                {
                    int segment = corridorSegments[j];
                    GameObject instance = PrefabUtility.InstantiatePrefab(segmentPrefabs[segment]) as GameObject;

                    // Only the first segment in each corridor carries a stimulus trigger zone, since the later
                    // segments exist for the visual illusion of depth.
                    if (j > 0)
                    {
                        StimulusTriggerZone stimulusTriggerZone =
                            instance.GetComponentInChildren<StimulusTriggerZone>();
                        if (stimulusTriggerZone != null)
                        {
                            UnityEngine.Object.DestroyImmediate(stimulusTriggerZone.gameObject);
                        }
                    }
                    else
                    {
                        // For the first segment, applies the visibility the segment's own trial declares.
                        StimulusTriggerZone stimulusTriggerZone =
                            instance.GetComponentInChildren<StimulusTriggerZone>();
                        if (stimulusTriggerZone != null)
                        {
                            ApplyBoundaryVisibility(
                                stimulusTriggerZone,
                                showBoundary: trials[segment].showStimulusCollisionBoundary
                            );
                        }
                    }

                    instance.transform.SetParent(corridor.transform, worldPositionStays: false);
                    instance.transform.localPosition += new Vector3(0, 0, zShift);
                    zShift += segmentLengths[segment];
                }

                // Anchors the padding to this corridor's own accumulated length so that corridors mixing
                // trials of different lengths butt their padding against the true corridor end. Segment
                // prefabs place their origin at -cueOffsetUnity, so a corridor spanning zShift ends at
                // zShift - cueOffsetUnity.
                GameObject paddingInstance = PrefabUtility.InstantiatePrefab(padding) as GameObject;
                paddingInstance.transform.SetParent(corridor.transform, worldPositionStays: false);
                paddingInstance.transform.localPosition += new Vector3(0, 0, zShift - cueOffsetUnity);

                currentCorridorX += corridorXShift;
            }

            PrefabUtility.SaveAsPrefabAsset(taskGameObject, savePath);
            UnityEngine.Object.DestroyImmediate(taskGameObject);

            return $"success: Task prefab saved to {savePath}";
        }

        /// <summary>
        /// Creates a new scene by copying the canonical experiment template scene, optionally instantiating a task
        /// prefab into it, and ensuring every supported controller (LinearTreadmill and SimulatedLinearTreadmill) is
        /// present in the scene. Either real or keyboard input can then drive the scene out of the box. The new scene
        /// is opened in the Editor and saved on disk before the call returns. Callers are responsible for resolving any
        /// unsaved changes in the currently open scene before invoking this method.
        /// </summary>
        /// <param name="sceneSavePath">The project-relative path where the new scene file is written.</param>
        /// <param name="taskPrefabPath">
        /// The project-relative path to a task prefab to instantiate in the scene, or an empty string to
        /// create the scene without any task prefab. A non-empty path that does not resolve to a loadable
        /// prefab still yields a successful result with <see cref="SceneCreationResult.TaskPrefabNotFound"/>
        /// set so callers can surface the discrepancy without rolling back the scene.
        /// </param>
        /// <param name="overwriteExisting">
        /// When true, an existing scene at <paramref name="sceneSavePath"/> is deleted before the template is copied.
        /// Use this from interactive flows that have already confirmed the overwrite with the user.
        /// </param>
        /// <returns>A <see cref="SceneCreationResult"/> describing the outcome.</returns>
        public static SceneCreationResult CreateSceneFromTemplate(
            string sceneSavePath,
            string taskPrefabPath,
            bool overwriteExisting
        )
        {
            SceneCreationResult result = new SceneCreationResult();

            if (string.IsNullOrEmpty(sceneSavePath))
            {
                result.Message =
                    "Unable to create the scene. The scene save path must name a project-relative scene file, "
                    + "but it is null or empty.";
                return result;
            }

            if (!File.Exists(TemplateScenePath))
            {
                result.Message =
                    $"Unable to create the scene. The template scene must exist at {TemplateScenePath}, but no "
                    + "asset is present there.";
                return result;
            }

            if (File.Exists(sceneSavePath))
            {
                if (!overwriteExisting)
                {
                    result.Message =
                        "Unable to create the scene. The save path must be free, but a scene already exists "
                        + $"at {sceneSavePath}.";
                    return result;
                }

                if (!AssetDatabase.DeleteAsset(sceneSavePath))
                {
                    result.Message =
                        "Unable to overwrite the scene. AssetDatabase must delete the existing scene at "
                        + $"{sceneSavePath}, but it refused the deletion.";
                    return result;
                }
            }

            if (!AssetDatabase.CopyAsset(TemplateScenePath, sceneSavePath))
            {
                result.Message =
                    "Unable to create the scene. AssetDatabase must copy the template scene from "
                    + $"{TemplateScenePath} to {sceneSavePath}, but the copy failed.";
                return result;
            }
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(sceneSavePath);

            // Instantiates the task prefab when one was requested. A missing prefab is reported as a
            // non-fatal warning so the rest of the pipeline (controller add, scene save) still runs and the
            // user is left with a usable scene that just lacks the task hierarchy.
            if (!string.IsNullOrEmpty(taskPrefabPath))
            {
                GameObject taskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(taskPrefabPath);
                if (taskPrefab != null)
                {
                    PrefabUtility.InstantiatePrefab(taskPrefab);
                }
                else
                {
                    result.TaskPrefabNotFound = true;
                }
            }

            Scene activeScene = SceneManager.GetActiveScene();
            bool simulatedExistedBeforeEnsure = Resources
                .FindObjectsOfTypeAll<SimulatedLinearTreadmill>()
                .Any(existing => existing.gameObject.scene == activeScene);
            MainWindow.EnsureControllers();
            // Applies defaults synchronously so the new scene is fully defaulted before this method returns.
            MainWindow.EnsureMqttDefaults();
            MainWindow.SyncDisplayBrightnessToSettings();
            // Strips the template scene's leftover "Main Camera", the one InitializeScene step this method
            // would otherwise miss. Running it here leaves the generated scene on the camera set the Display and
            // Actor use, with no manual pass through Window > Task Parameters.
            MainWindow.RemoveDefaultMainCamera();
            result.SimulatedControllerAdded = !simulatedExistedBeforeEnsure;

            EditorSceneManager.SaveScene(activeScene);

            result.Success = true;
            if (result.TaskPrefabNotFound)
            {
                result.Message = $"Scene saved to {sceneSavePath} but task prefab was not found at: {taskPrefabPath}";
            }
            else
            {
                result.Message = $"Scene saved to {sceneSavePath}";
            }
            return result;
        }

        /// <summary>
        /// Scans every template under ``Assets/InfiniteCorridorTask/Configurations/`` and verifies that no
        /// two templates declare a cue with the same ``(name, lengthCm)`` identity but different textures.
        /// </summary>
        /// <remarks>
        /// Cue prefabs and materials are filesystem-keyed as ``Cue_{name}_{length}cm`` and are deliberately shared
        /// across templates so generation stays cheap. The shared-asset model breaks down only when two templates
        /// declare the same cue identity with conflicting textures. Whichever template runs first wins, the second
        /// template silently inherits the wrong texture, and downstream prefabs look correct on disk while rendering
        /// the wrong cue at runtime. The preflight closes that hole by failing the generation request before any cue
        /// prefab is written. The check is cheap (one YAML deserialization per template, and the catalog is small) and
        /// runs on every generation call so drift introduced between runs is caught at the earliest possible moment.
        /// </remarks>
        /// <param name="errorMessage">
        /// Receives every detected conflict, or the load failure that aborted the scan.
        /// </param>
        /// <returns>
        /// True when every template loads and no conflicts are detected, false when a template fails to load or a
        /// conflict is found.
        /// </returns>
        private static bool ValidateCueDefinitionsAcrossTemplates(out string errorMessage)
        {
            errorMessage = null;

            string configurationsDirectory = Path.Combine(
                Application.dataPath,
                "InfiniteCorridorTask",
                "Configurations"
            );

            if (!Directory.Exists(configurationsDirectory))
            {
                // No configurations folder yet means there are no templates to compare. The per-template load path will
                // surface a clearer error if the folder is missing for the active request.
                return true;
            }

            string[] templateFiles = Directory
                .GetFiles(configurationsDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(configurationsDirectory, "*.yml", SearchOption.TopDirectoryOnly))
                .ToArray();

            // Maps a canonical cue identity (``cueName|lengthLabel``) to the list of templates that
            // declare it, capturing each declaration's texture. A list (rather than a single slot) lets
            // the reporter list every contributing template when three or more templates collide on the
            // same identity, instead of silently dropping later declarations.
            Dictionary<string, List<(string Texture, string TemplateName)>> cueDefinitions =
                new Dictionary<string, List<(string, string)>>();

            foreach (string templateFile in templateFiles)
            {
                TaskTemplate template;
                try
                {
                    template = ConfigLoader.LoadTemplate(templateFile);
                }
                catch (Exception exception)
                {
                    errorMessage =
                        "Unable to run the cross-template cue-texture preflight. Every template under "
                        + $"Configurations must load, but '{templateFile}' failed with: {exception.Message}";
                    return false;
                }

                foreach (Cue cue in template.cues)
                {
                    string key = $"{cue.name}|{FormatCueLengthLabel(cue.lengthCm)}cm";
                    if (!cueDefinitions.TryGetValue(key, out List<(string, string)> declarations))
                    {
                        declarations = new List<(string, string)>();
                        cueDefinitions[key] = declarations;
                    }
                    declarations.Add((cue.texture, template.templateName));
                }
            }

            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, List<(string Texture, string TemplateName)>> entry in cueDefinitions)
            {
                HashSet<string> distinctTextures = new HashSet<string>(
                    entry.Value.Select(declaration => declaration.Texture),
                    StringComparer.Ordinal
                );

                if (distinctTextures.Count <= 1)
                {
                    continue;
                }

                string details = string.Join(
                    ", ",
                    entry.Value.Select(declaration => $"{declaration.TemplateName} -> '{declaration.Texture}'")
                );
                string identity = entry.Key.Replace("|", " at ");
                conflicts.Add($"Cue '{identity}': {details}");
            }

            if (conflicts.Count == 0)
            {
                return true;
            }

            errorMessage =
                "Unable to generate. Each cue identity must declare one texture across the Configurations "
                + "catalog, but the identities below declare more than one. Rename the cue, change its length, "
                + "or unify the textures before regenerating:\n  - "
                + string.Join("\n  - ", conflicts);
            return false;
        }

        /// <summary>
        /// Confirms the template declares positive segment lengths short enough for the default track length to fill
        /// one corridor.
        /// </summary>
        /// <remarks>
        /// A generated task prefab always starts at <see cref="Task.DefaultTrackLength"/>, so a template whose
        /// segments outrun it produces a maze shorter than the corridor depth and a task that disables itself on the
        /// first Play Mode entry. Reporting it here surfaces the problem while the operator is still generating.
        /// </remarks>
        /// <param name="template">The task template supplying the segment lengths and the corridor depth.</param>
        /// <returns>
        /// An error message when a segment length is not positive or the default track length cannot fill a corridor,
        /// otherwise null.
        /// </returns>
        private static string ValidateTrackLengthCoversCorridor(TaskTemplate template)
        {
            float longestSegmentUnity = template.GetSegmentLengthsUnity().Max();
            int depth = template.vrEnvironment.segmentsPerCorridor;

            // Guards the division below, which turns a non-positive longest segment into an infinite segment count
            // that clears every corridor depth and lets an unbuildable template through.
            if (longestSegmentUnity <= 0f)
            {
                return $"Unable to generate from template '{template.templateName}'. Every segment length must "
                    + $"be positive, but the longest segment measures {longestSegmentUnity} Unity units. Give "
                    + "each trial a cue sequence whose cue lengths sum above zero before generating.";
            }

            int worstCaseSegmentCount = Mathf.FloorToInt(Task.DefaultTrackLength / longestSegmentUnity);

            if (worstCaseSegmentCount >= depth)
            {
                return null;
            }

            return $"Unable to generate from template '{template.templateName}'. The default track length "
                + $"{Task.DefaultTrackLength} must cover the segments_per_corridor value of {depth}, but the "
                + $"longest segment of {longestSegmentUnity} Unity units yields at most "
                + $"{worstCaseSegmentCount}. Shorten the longest cue sequence or raise Track Length in Window > "
                + "Task Parameters before generating.";
        }

        /// <summary>
        /// Confirms every hand-authored asset the build consumes is present before any asset is written.
        /// </summary>
        /// <remarks>
        /// The segment and corridor builds each abort on a missing hand-authored input, and both run after
        /// <see cref="CleanGeneratedSegments"/> has removed the previous generation. Checking here keeps that wipe
        /// from stranding the existing task prefab and scene on segments the aborted call cannot rebuild.
        /// </remarks>
        /// <param name="template">The loaded task template, which names the padding prefab.</param>
        /// <returns>An error message naming every missing asset, otherwise null.</returns>
        private static string ValidateHandAuthoredAssets(TaskTemplate template)
        {
            string[] requiredPaths = BuildRequiredHandAuthoredPaths(template);

            List<string> missingPaths = new List<string>();
            foreach (string requiredPath in requiredPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(requiredPath) == null)
                {
                    missingPaths.Add(requiredPath);
                }
            }

            if (missingPaths.Count == 0)
            {
                return null;
            }

            return "Unable to generate the task. Every hand-authored asset the pipeline references must exist, "
                + "but these are missing from the project:\n  - "
                + string.Join("\n  - ", missingPaths)
                + "\nRestore them from version control before generating.";
        }

        /// <summary>Returns the hand-authored asset paths a generation run resolves before writing any asset.</summary>
        /// <param name="template">The loaded task template, which names the padding prefab.</param>
        /// <returns>The project-relative path of every hand-authored asset the build consumes.</returns>
        private static string[] BuildRequiredHandAuthoredPaths(TaskTemplate template)
        {
            return new[]
            {
                Path.Combine(MaterialsFolder, "Floor.mat"),
                Path.Combine(MaterialsFolder, "Wall.mat"),
                Path.Combine(PrefabsFolder, "StimulusTriggerZone.prefab"),
                Path.Combine(PrefabsFolder, "OccupancyTriggerZone.prefab"),
                Path.Combine(PrefabsFolder, $"{template.vrEnvironment.paddingPrefabName}.prefab"),
                CueShaderReferenceMaterialPath,
            };
        }

        /// <summary>
        /// Deletes every segment prefab the supplied template claims ownership of so the subsequent build always
        /// produces a fresh segment tree, even if trial parameters changed under an unchanged trial name. Cue prefabs
        /// and cue materials are intentionally **not** removed. They are keyed by cue name and length only and are
        /// shared by every template that declares a matching cue, so deleting them here would clobber assets owned by
        /// other templates and invalidate their segment prefabs' cue references. Hand-authored prefabs (Padding,
        /// StimulusTriggerZone, OccupancyTriggerZone) are never derived from template data and are therefore also left
        /// untouched.
        /// </summary>
        /// <remarks>
        /// Deletes by exact canonical name rather than by prefix, which the hyphen separator in
        /// <see cref="CanonicalSegmentName"/> makes unambiguous even where one template basename nests another.
        /// </remarks>
        /// <param name="template">The template whose owned segment prefabs are removed.</param>
        private static void CleanGeneratedSegments(TaskTemplate template)
        {
            // The final AssetDatabase.SaveAssets and Refresh in BuildSegmentPrefabs flush these deletions along with
            // the segment writes that follow, keeping the pipeline to a single project-wide reimport.
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string segmentName = CanonicalSegmentName(template, trialEntry.Key);
                AssetDatabase.DeleteAsset(Path.Combine(PrefabsFolder, $"{segmentName}.prefab"));
            }
        }

        /// <summary>
        /// Resolves the shader used by generated cue materials, preferring the committed reference material at
        /// <see cref="CueShaderReferenceMaterialPath"/>. Falls back to any pre-existing hand-authored ``Cue*.mat``
        /// material if the reference is missing, then to ``Shader.Find("Legacy Shaders/Diffuse")``, and finally to the
        /// default Standard shader. The fallbacks exist for resilience, and the committed reference material is the
        /// canonical source.
        /// </summary>
        /// <param name="materialsPath">The directory under which to search for fallback materials.</param>
        /// <returns>The shader to use for newly generated cue materials.</returns>
        private static Shader LoadReferenceCueShader(string materialsPath)
        {
            Material reference = AssetDatabase.LoadAssetAtPath<Material>(CueShaderReferenceMaterialPath);
            if (reference != null && reference.shader != null)
            {
                return reference.shader;
            }
            string message =
                "Unable to load the canonical cue shader reference. The material at "
                + $"'{CueShaderReferenceMaterialPath}' must exist, but it is missing, so the shader falls back "
                + "to a hand-authored Cue*.mat material or Shader.Find. Restore the reference material to "
                + "guarantee consistent cue rendering across machines.";
            Debug.LogWarning(message);

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { materialsPath.TrimEnd('/') });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (
                    fileName.StartsWith("Cue", StringComparison.Ordinal)
                    && !fileName.StartsWith("Cue_", StringComparison.Ordinal)
                )
                {
                    Material fallback = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (fallback != null && fallback.shader != null)
                    {
                        return fallback.shader;
                    }
                }
            }
            return Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
        }

        /// <summary>
        /// Creates cue prefabs and accompanying materials for cues that do not yet exist under the ``Cues/`` and
        /// ``Materials/`` folders. Cue assets are deliberately shared across templates by cue name and length, so this
        /// method is idempotent: a cue already on disk is left untouched and reused by every template that declares it.
        /// </summary>
        /// <param name="template">The task template supplying the cue definitions and the unit scale.</param>
        /// <returns>True if all required cue prefabs were built or already exist, false on error.</returns>
        private static bool BuildCuePrefabs(TaskTemplate template)
        {
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;

            // Reuses the reference material's shader; see CueShaderReferenceMaterialPath for the rationale.
            Shader cueShader = LoadReferenceCueShader(MaterialsFolder + "/");

            if (!AssetDatabase.IsValidFolder(CuesFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Cues");
            }

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            foreach (Cue cue in template.cues)
            {
                // Encodes the cue length in the asset filenames so cues that share a letter across templates
                // (e.g., A at 30 cm in MF vs A at 40 cm in SSO) resolve to distinct prefabs and materials.
                string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                string cueAssetStem = $"Cue_{cue.name}_{lengthLabel}cm";
                string cuePrefabPath = Path.Combine(CuesFolder, $"{cueAssetStem}.prefab");
                string materialPath = Path.Combine(MaterialsFolder, $"{cueAssetStem}.mat");

                // Loads the shared texture once for both material variants. The load happens before the cached-asset
                // checks below so a template that changes a cue's texture is caught rather than served stale assets.
                Texture2D cueTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Path.Combine(TexturesFolder, cue.texture)
                );
                if (cueTexture == null)
                {
                    string message =
                        $"Unable to build the cue prefab for '{cue.name}'. The texture must exist under "
                        + $"{TexturesFolder}, but '{cue.texture}' failed to load.";
                    Debug.LogError(message);
                    return false;
                }

                // Cue assets are keyed by name and length alone, so the cached prefab and material survive a texture
                // change and would render the previous texture. Both assets are shared with every other template that
                // declares this cue identity, so rebuilding them here would silently alter those tasks too. Aborting
                // names the offending cue and leaves the resolution to the template author.
                Material cueMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (cueMaterial != null && cueMaterial.GetTexture("_MainTex") != cueTexture)
                {
                    string message =
                        $"Unable to build cue '{cue.name}' at {cue.lengthCm} cm. The cached material "
                        + $"'{cueAssetStem}.mat' must be built from the declared texture '{cue.texture}', but it "
                        + "was built from a different texture. Cue assets are shared across every template that "
                        + $"declares this cue identity. Delete '{cueAssetStem}.prefab' and '{cueAssetStem}.mat' "
                        + "to rebuild them for every template, or give this cue a distinct name or length so it "
                        + "occupies its own asset slot.";
                    Debug.LogError(message);
                    return false;
                }

                // A cue prefab references its material by GUID, so a material deleted from under a surviving prefab
                // leaves that prefab rendering untextured. Skipping requires both assets, and a missing material
                // rebuilds the prefab alongside it so the new material is the one the renderers point at.
                bool cuePrefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath) != null;
                if (cuePrefabExists && cueMaterial != null)
                {
                    continue;
                }

                // Retires the surviving prefab so the rebuild below writes a fresh asset. SaveAsPrefabAsset merges
                // into an asset already occupying the path whose root carries the same name, and that merge keeps
                // the matched wall renderers on their deleted material, so both walls reach disk with none at all.
                if (cuePrefabExists)
                {
                    AssetDatabase.DeleteAsset(cuePrefabPath);
                }

                float lengthUnity = cue.LengthUnity(cmPerUnit);

                // Builds the material on the reference shader; see CueShaderReferenceMaterialPath for the rationale.
                if (cueMaterial == null)
                {
                    cueMaterial = new Material(cueShader);
                    cueMaterial.name = cueAssetStem;
                    cueMaterial.SetTexture("_MainTex", cueTexture);
                    AssetDatabase.CreateAsset(cueMaterial, materialPath);
                }

                // The Right wall uses a negative X scale to mirror the texture along the horizontal axis so directional
                // patterns read forward from both sides of the corridor. See CueShaderReferenceMaterialPath for why the
                // shader keeps that wall correctly lit under the inverted geometry.
                GameObject cueGameObject = new GameObject(cueAssetStem);

                GameObject right = new GameObject("Right");
                right.transform.SetParent(cueGameObject.transform);
                right.transform.localPosition = new Vector3(0.49f, WallVerticalCenter, lengthUnity / 2f);
                right.transform.localRotation = Quaternion.Euler(0, 90, 0);
                right.transform.localScale = new Vector3(-lengthUnity, 1, 1);
                right.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                right.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                GameObject left = new GameObject("Left");
                left.transform.SetParent(cueGameObject.transform);
                left.transform.localPosition = new Vector3(-0.49f, WallVerticalCenter, lengthUnity / 2f);
                left.transform.localRotation = Quaternion.Euler(0, -90, 0);
                left.transform.localScale = new Vector3(lengthUnity, 1, 1);
                left.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                left.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                PrefabUtility.SaveAsPrefabAsset(cueGameObject, cuePrefabPath);
                UnityEngine.Object.DestroyImmediate(cueGameObject);

                Debug.Log($"Created the cue prefab at {cuePrefabPath}.");
            }

            // The cue prefabs are immediately discoverable via AssetDatabase.LoadAssetAtPath because
            // PrefabUtility.SaveAsPrefabAsset registers each new asset on the spot. The project-wide SaveAssets +
            // Refresh that BuildSegmentPrefabs runs at the end of the pipeline flushes every cue and segment write
            // together.
            return true;
        }

        /// <summary>
        /// Creates a segment prefab for every trial structure declared by the template, naming each one
        /// ``TemplateName-TrialName.prefab``. Each segment prefab contains cue instances, floor, walls, and the
        /// trigger zone derived from the trial structure. Callers must invoke ``ValidateHandAuthoredAssets`` and
        /// then ``CleanGeneratedSegments`` first, because this method unconditionally writes to the segment prefab
        /// path and assumes nothing exists at that location.
        /// </summary>
        /// <param name="template">The task template supplying the trial structures and environment geometry.</param>
        /// <returns>True if all segment prefabs were built successfully, false on error.</returns>
        private static bool BuildSegmentPrefabs(TaskTemplate template)
        {
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;
            float cueOffsetUnity = template.vrEnvironment.CueOffsetUnity;
            Dictionary<string, Cue> cueMap = template.GetCueByName();

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Mesh planeMesh = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");

            Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Path.Combine(MaterialsFolder, "Floor.mat")
            );
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(MaterialsFolder, "Wall.mat"));

            if (floorMaterial == null || wallMaterial == null)
            {
                string message =
                    "Unable to build the segment prefabs. Floor.mat and Wall.mat must both exist under "
                    + $"{MaterialsFolder}, but at least one of them is missing.";
                Debug.LogError(message);
                return false;
            }

            GameObject stimulusZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(PrefabsFolder, "StimulusTriggerZone.prefab")
            );
            GameObject occupancyZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(PrefabsFolder, "OccupancyTriggerZone.prefab")
            );

            // A missing zone prefab would otherwise fall through every trigger_type branch below and save a segment
            // with no trigger zone at all, producing a task that reports success and never publishes a stimulus.
            if (stimulusZonePrefab == null || occupancyZonePrefab == null)
            {
                string message =
                    "Unable to build the segment prefabs. StimulusTriggerZone.prefab and "
                    + $"OccupancyTriggerZone.prefab must both exist under {PrefabsFolder}, but at least one of "
                    + "them is missing. Restore both hand-authored zone prefabs before generating.";
                Debug.LogError(message);
                return false;
            }

            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;
                string canonicalSegmentName = CanonicalSegmentName(template, trialName);
                string segmentPrefabPath = Path.Combine(PrefabsFolder, $"{canonicalSegmentName}.prefab");

                float totalLengthUnity = trial.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit));

                // The root takes the canonical prefab name so the in-prefab m_Name matches the filename, matching the
                // cue-side convention.
                GameObject segmentGameObject = new GameObject(canonicalSegmentName);
                segmentGameObject.transform.localPosition = new Vector3(0, 0, -cueOffsetUnity);

                float cumulativeZ = 0f;
                foreach (string cueName in trial.cueSequence)
                {
                    Cue cue = cueMap[cueName];
                    float cueLengthUnity = cue.LengthUnity(cmPerUnit);

                    string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                    string cuePrefabPath = Path.Combine(CuesFolder, $"Cue_{cueName}_{lengthLabel}cm.prefab");
                    GameObject cuePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath);

                    if (cuePrefab == null)
                    {
                        string message =
                            $"Unable to build the segment prefab for trial '{trialName}'. The cue prefab must "
                            + $"exist at '{cuePrefabPath}', but it is missing.";
                        Debug.LogError(message);
                        UnityEngine.Object.DestroyImmediate(segmentGameObject);
                        return false;
                    }

                    GameObject cueInstance = PrefabUtility.InstantiatePrefab(cuePrefab) as GameObject;
                    cueInstance.name = $"Cue{cueName}";
                    cueInstance.transform.SetParent(segmentGameObject.transform);
                    cueInstance.transform.localPosition = new Vector3(0, 0, cumulativeZ);

                    cumulativeZ += cueLengthUnity;
                }

                GameObject floor = new GameObject("Floor");
                floor.transform.SetParent(segmentGameObject.transform);
                floor.transform.localPosition = new Vector3(0, 0, totalLengthUnity / 2f);
                floor.transform.localScale = new Vector3(0.1f, 1, totalLengthUnity / 10f);
                floor.AddComponent<MeshFilter>().sharedMesh = planeMesh;
                floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;

                GameObject walls = new GameObject("Walls");
                walls.transform.SetParent(segmentGameObject.transform);
                walls.transform.localPosition = Vector3.zero;

                GameObject leftWall = new GameObject("LeftWall");
                leftWall.transform.SetParent(walls.transform);
                leftWall.transform.localPosition = new Vector3(-0.5f, WallVerticalCenter, totalLengthUnity / 2f);
                leftWall.transform.localRotation = Quaternion.Euler(0, -90, 0);
                leftWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                leftWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                leftWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                GameObject rightWall = new GameObject("RightWall");
                rightWall.transform.SetParent(walls.transform);
                rightWall.transform.localPosition = new Vector3(0.5f, WallVerticalCenter, totalLengthUnity / 2f);
                rightWall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                rightWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                rightWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                rightWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                float zoneStartUnity = trial.stimulusTriggerZoneStartCm / cmPerUnit;
                float zoneEndUnity = trial.stimulusTriggerZoneEndCm / cmPerUnit;
                float zoneCenterUnity = (zoneStartUnity + zoneEndUnity) / 2f;
                float zoneSizeUnity = zoneEndUnity - zoneStartUnity;
                float stimulusLocationUnity = trial.stimulusLocationCm / cmPerUnit;

                if (string.Equals(trial.triggerType, "interaction", StringComparison.Ordinal))
                {
                    PlaceInteractionZone(
                        parent: segmentGameObject,
                        zonePrefab: stimulusZonePrefab,
                        trialName: trialName,
                        zoneCenterUnity: zoneCenterUnity,
                        zoneSizeUnity: zoneSizeUnity,
                        stimulusLocationUnity: stimulusLocationUnity,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }
                else if (string.Equals(trial.triggerType, "collision", StringComparison.Ordinal))
                {
                    PlaceCollisionZone(
                        parent: segmentGameObject,
                        zonePrefab: stimulusZonePrefab,
                        trialName: trialName,
                        stimulusLocationUnity: stimulusLocationUnity,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }
                else if (
                    string.Equals(trial.triggerType, "occupancy_disarm", StringComparison.Ordinal)
                    || string.Equals(trial.triggerType, "occupancy_arm", StringComparison.Ordinal)
                    || string.Equals(trial.triggerType, "occupancy_trigger", StringComparison.Ordinal)
                )
                {
                    PlaceOccupancyZone(
                        parent: segmentGameObject,
                        zonePrefab: occupancyZonePrefab,
                        trialName: trialName,
                        triggerMode: ResolveOccupancyTriggerMode(trial.triggerType),
                        zoneCenterUnity: zoneCenterUnity,
                        zoneSizeUnity: zoneSizeUnity,
                        stimulusLocationUnity: stimulusLocationUnity,
                        // Reached only on the occupancy branch, where ConfigLoader has already required a value.
                        occupancyDurationMs: trial.occupancyDurationMs.Value,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }
                else
                {
                    // Refuses the trial rather than saving a zoneless segment, which reports success and then never
                    // publishes a stimulus. ConfigLoader gates the literal set, so this is reached by a literal
                    // added there without a matching branch here.
                    string message =
                        $"Unable to place a trigger zone for trial '{trialName}'. The trigger_type must be one "
                        + "of interaction, collision, occupancy_disarm, occupancy_arm, or occupancy_trigger, but "
                        + $"the template declares '{trial.triggerType}'.";
                    Debug.LogError(message);
                    UnityEngine.Object.DestroyImmediate(segmentGameObject);
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(segmentGameObject, segmentPrefabPath);
                UnityEngine.Object.DestroyImmediate(segmentGameObject);

                Debug.Log($"Created the segment prefab at {segmentPrefabPath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Instantiates and configures a StimulusTriggerZone (interaction mode) within a segment.
        /// Positions the root collider to span the trigger zone and starts the GuidanceRegion at the stimulus
        /// location, so entering the region under guidance delivers the stimulus at that exact location.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The StimulusTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name, published as the stimulus identifier when fired.</param>
        /// <param name="zoneCenterUnity">The center position of the trigger zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the trigger zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The segment-local Z position where the stimulus fires.</param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceInteractionZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, zoneCenterUnity);

            ConfigureRootZoneCollider(zone, zoneSizeUnity);

            GuidanceZone guidanceZone = zone.GetComponentInChildren<GuidanceZone>();
            if (guidanceZone != null)
            {
                if (guidanceZone.TryGetComponent(out BoxCollider guidanceCollider))
                {
                    // Anchors the guidance region's leading edge on the stimulus location, so an animal running
                    // under guidance receives the stimulus at exactly the location the template declares. The
                    // region extends forward from there, and its far edge carries no behavioral meaning.
                    guidanceCollider.size = new Vector3(1, 1, GuidanceColliderDepth);
                    guidanceCollider.center = new Vector3(
                        0,
                        0,
                        stimulusLocationUnity - zoneCenterUnity + GuidanceColliderDepth / 2f
                    );
                }
            }

            if (zone.TryGetComponent(out StimulusTriggerZone stimulusZone))
            {
                stimulusZone.triggerMode = TriggerMode.Interaction;
                ApplyBoundaryVisibility(stimulusZone, showBoundary: showBoundary);
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>Maps an occupancy trigger_type literal to its matching occupancy TriggerMode.</summary>
        /// <param name="triggerType">The trial's trigger_type string (an occupancy literal).</param>
        /// <returns>The matching occupancy TriggerMode, defaulting to OccupancyDisarm.</returns>
        private static TriggerMode ResolveOccupancyTriggerMode(string triggerType) =>
            triggerType switch
            {
                "occupancy_arm" => TriggerMode.OccupancyArm,
                "occupancy_trigger" => TriggerMode.OccupancyTrigger,
                _ => TriggerMode.OccupancyDisarm,
            };

        /// <summary>
        /// Instantiates and configures a StimulusTriggerZone in collision mode within a segment. Reuses the
        /// interaction prefab, strips its GuidanceRegion child, and positions the root collider as a thin wall
        /// starting at the stimulus location. Crossing that wall fires the stimulus unconditionally.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The StimulusTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name, published as the stimulus identifier when fired.</param>
        /// <param name="stimulusLocationUnity">The wall (stimulus) location in Unity units.</param>
        /// <param name="showBoundary">Determines whether the wall is visible.</param>
        private static void PlaceCollisionZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            // Anchors the wall's leading edge on the stimulus location, matching where the interaction guidance
            // region and the occupancy root both begin, so every mode fires at the location the template declares.
            // ConfigureRootZoneCollider centers the collider on the zone origin, so the origin carries the offset.
            float wallCenterUnity = stimulusLocationUnity + GuidanceColliderDepth / 2f;

            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, wallCenterUnity);

            ConfigureRootZoneCollider(zone, GuidanceColliderDepth);

            // Collision has no sensor or guidance, so removes the interaction prefab's GuidanceRegion child.
            GuidanceZone guidanceZone = zone.GetComponentInChildren<GuidanceZone>();
            if (guidanceZone != null)
            {
                UnityEngine.Object.DestroyImmediate(guidanceZone.gameObject);
            }

            if (zone.TryGetComponent(out StimulusTriggerZone stimulusZone))
            {
                stimulusZone.triggerMode = TriggerMode.Collision;
                ApplyBoundaryVisibility(stimulusZone, showBoundary: showBoundary);
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>
        /// Instantiates and configures an OccupancyTriggerZone within a segment.
        /// The root is centered half a zone length past the stimulus boundary, so its collider spans forward from that
        /// boundary across the zone size.
        /// The OccupancyRegion child covers the start-to-end range where the animal must wait.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The OccupancyTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name, published as the stimulus identifier when fired.</param>
        /// <param name="triggerMode">The occupancy trigger mode (disarm, arm, or trigger) applied to the zone.</param>
        /// <param name="zoneCenterUnity">The center position of the occupancy zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the occupancy zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The segment-local Z position where the stimulus fires.</param>
        /// <param name="occupancyDurationMs">
        /// The occupancy duration in milliseconds applied to the OccupancyZone.
        /// </param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceOccupancyZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            TriggerMode triggerMode,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            float occupancyDurationMs,
            bool showBoundary
        )
        {
            // Centers the root so its collider spans forward from the stimulus location across the zone size.
            float rootZ = stimulusLocationUnity + zoneSizeUnity / 2f;

            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, rootZ);

            ConfigureRootZoneCollider(zone, zoneSizeUnity);

            float occupancyCenterOffset = zoneCenterUnity - rootZ;

            OccupancyZone occupancyZone = zone.GetComponentInChildren<OccupancyZone>();
            if (occupancyZone != null)
            {
                occupancyZone.occupancyDurationMs = occupancyDurationMs;

                if (occupancyZone.TryGetComponent(out BoxCollider occupancyCollider))
                {
                    occupancyCollider.size = new Vector3(1, 1, zoneSizeUnity);
                    occupancyCollider.center = new Vector3(0, 0, occupancyCenterOffset);
                }
            }

            OccupancyGuidanceZone occupancyGuidanceZone = zone.GetComponentInChildren<OccupancyGuidanceZone>();
            if (occupancyGuidanceZone != null)
            {
                if (occupancyGuidanceZone.TryGetComponent(out BoxCollider occupancyGuidanceCollider))
                {
                    occupancyGuidanceCollider.size = new Vector3(1, 1, GuidanceColliderDepth);
                    occupancyGuidanceCollider.center = new Vector3(
                        0,
                        0,
                        occupancyCenterOffset + zoneSizeUnity / 2f - GuidanceColliderDepth / 2f
                    );
                }
            }

            if (zone.TryGetComponent(out StimulusTriggerZone stimulusZone))
            {
                stimulusZone.triggerMode = triggerMode;
                ApplyBoundaryVisibility(stimulusZone, showBoundary: showBoundary);
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>
        /// Resizes a zone GameObject's root <see cref="BoxCollider"/> to span the supplied length and recenters it
        /// on the local origin, so every generated trigger zone carries identical root-collider geometry.
        /// </summary>
        /// <param name="zone">The zone GameObject whose root BoxCollider is being adjusted.</param>
        /// <param name="zoneSizeUnity">The desired Z-axis length of the zone in Unity units.</param>
        private static void ConfigureRootZoneCollider(GameObject zone, float zoneSizeUnity)
        {
            if (zone.TryGetComponent(out BoxCollider rootCollider))
            {
                rootCollider.size = new Vector3(1, 1, zoneSizeUnity);
                rootCollider.center = Vector3.zero;
            }
        }

        /// <summary>
        /// Writes a trial's boundary visibility onto a stimulus trigger zone and onto the boundary quad renderer
        /// sharing its GameObject.
        /// </summary>
        /// <remarks>
        /// Both hand-authored zone prefabs ship that renderer enabled and <see cref="StimulusTriggerZone"/> only
        /// reconciles it from <c>Start</c>. A generated asset whose renderer is left alone therefore draws the boundary
        /// across the whole corridor cross-section in the Scene view no matter what its trial declares.
        /// </remarks>
        /// <param name="stimulusZone">The stimulus trigger zone the visibility is written to.</param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void ApplyBoundaryVisibility(StimulusTriggerZone stimulusZone, bool showBoundary)
        {
            stimulusZone.showBoundary = showBoundary;
            if (stimulusZone.TryGetComponent(out MeshRenderer boundaryRenderer))
            {
                boundaryRenderer.enabled = showBoundary;
            }
        }

        /// <summary>
        /// Reports the outcome of <see cref="CreateSceneFromTemplate"/>. Returned in lieu of a string-prefix protocol
        /// because the result carries facts that callers route differently: success or error, whether the requested
        /// task prefab was found, and whether a SimulatedLinearTreadmill was added.
        /// </summary>
        public class SceneCreationResult
        {
            /// <summary>Determines whether the scene file was successfully created and saved.</summary>
            public bool Success { get; set; }

            /// <summary>The human-readable description of the outcome, including any error detail.</summary>
            public string Message { get; set; }

            /// <summary>Determines whether a SimulatedLinearTreadmill GameObject was added to the new scene.</summary>
            public bool SimulatedControllerAdded { get; set; }

            /// <summary>
            /// Determines whether a non-empty task prefab path was supplied while no asset loaded from it, with the
            /// scene still created and saved.
            /// </summary>
            public bool TaskPrefabNotFound { get; set; }
        }
    }
}
