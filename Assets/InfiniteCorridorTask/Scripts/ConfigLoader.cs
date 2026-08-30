/// <summary>
/// Provides the ConfigLoader class for loading and validating task templates from YAML files.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SL.Config
{
    /// <summary>Loads and validates task templates from YAML files.</summary>
    public static class ConfigLoader
    {
        /// <summary>The tolerance for validating that trial transition probabilities sum to 1.0.</summary>
        private const double ProbabilitySumTolerance = 0.001;

        /// <summary>
        /// Matches the template, cue, and trial names embedded in generated asset filenames. Restricts each name to
        /// ASCII letters, digits, and underscores, so the ``TemplateName-TrialName`` segment scheme, the
        /// ``Cue_{name}_{length}cm`` cue assets, and the space-joined cue sequence signature all stay unambiguous.
        /// </summary>
        /// <remarks>
        /// Excluding the hyphen from both halves is what makes the joined filename unambiguous, because a segment
        /// filename then splits at its only hyphen and resolves to exactly one owning template.
        /// </remarks>
        private static readonly Regex SegmentNameComponentPattern = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>Loads a TaskTemplate from a YAML file and derives the template name from the filename.</summary>
        /// <param name="filePath">The absolute path to the YAML template file.</param>
        /// <returns>The parsed template with templateName populated.</returns>
        /// <exception cref="FileNotFoundException">
        /// The template file at <paramref name="filePath"/> does not exist.
        /// </exception>
        /// <exception cref="FormatException">The YAML document body deserializes to null.</exception>
        /// <exception cref="InvalidDataException">
        /// The template filename stem falls outside the allowed character set, or the parsed template fails validation.
        /// </exception>
        /// <exception cref="YamlDotNet.Core.YamlException">The deserializer rejects a malformed YAML file.</exception>
        public static TaskTemplate LoadTemplate(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Template file not found: {filePath}", filePath);
            }

            string yaml = File.ReadAllText(filePath);
            TaskTemplate template = ParseTemplate(yaml);

            string templateName = Path.GetFileNameWithoutExtension(filePath);
            if (!SegmentNameComponentPattern.IsMatch(templateName))
            {
                string message =
                    $"Template filename '{templateName}' is invalid. Template names must contain only ASCII "
                    + "letters, digits, and underscores, because the generated segment prefab filename joins the "
                    + "template name and the trial name with a hyphen.";
                throw new InvalidDataException(message);
            }

            ValidateTemplate(template, filePath);

            template.templateName = templateName;

            return template;
        }

        /// <summary>Deserializes a task template from raw YAML content.</summary>
        /// <param name="yaml">The YAML content of a task template file.</param>
        /// <returns>The template the content describes.</returns>
        private static TaskTemplate ParseTemplate(string yaml)
        {
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            return deserializer.Deserialize<TaskTemplate>(yaml);
        }

        /// <summary>Validates the loaded template for required fields and data integrity.</summary>
        /// <param name="template">The template to validate.</param>
        /// <param name="filePath">The absolute path to the template file, used for resolving asset paths.</param>
        /// <exception cref="FormatException">The template is null or could not be parsed.</exception>
        /// <exception cref="InvalidDataException">The template fails one of the validation checks.</exception>
        private static void ValidateTemplate(TaskTemplate template, string filePath)
        {
            if (template == null)
            {
                throw new FormatException("Failed to parse template file.");
            }

            if (template.cues == null || template.cues.Count == 0)
            {
                throw new InvalidDataException("No cues defined in template.");
            }

            if (template.vrEnvironment == null)
            {
                throw new InvalidDataException("No VR environment configuration defined.");
            }

            ValidateVrEnvironment(template.vrEnvironment);

            if (template.trialStructures == null || template.trialStructures.Count == 0)
            {
                throw new InvalidDataException("No trial structures defined in template.");
            }

            HashSet<int> seenCodes = new HashSet<int>();
            HashSet<string> seenNames = new HashSet<string>();

            for (int cueIndex = 0; cueIndex < template.cues.Count; cueIndex++)
            {
                Cue cue = template.cues[cueIndex];

                if (cue == null)
                {
                    throw new InvalidDataException(
                        $"The cue entry at index {cueIndex} is empty. Every entry of the cues list must define a cue."
                    );
                }

                if (string.IsNullOrEmpty(cue.name))
                {
                    throw new InvalidDataException("A cue entry is missing the required 'name' field.");
                }

                // Cue names reach the generated cue prefab and material filenames verbatim, and the duplicate-sequence
                // signature below joins them with a space. A name carrying a space or a separator therefore corrupts
                // an asset path or makes two distinct sequences compare equal.
                if (!SegmentNameComponentPattern.IsMatch(cue.name))
                {
                    string message =
                        $"Cue name '{cue.name}' is invalid. Cue names must contain only ASCII letters, digits, and "
                        + "underscores, because they are embedded in generated cue asset filenames and joined into "
                        + "the per-trial cue sequence signature.";
                    throw new InvalidDataException(message);
                }

                if (cue.code < 0 || cue.code > 255)
                {
                    throw new InvalidDataException($"Cue '{cue.name}' has invalid code {cue.code}. Must be 0-255.");
                }

                if (!seenCodes.Add(cue.code))
                {
                    throw new InvalidDataException($"Duplicate cue code {cue.code} found.");
                }

                if (!seenNames.Add(cue.name))
                {
                    throw new InvalidDataException($"Duplicate cue name '{cue.name}' found.");
                }

                if (!float.IsFinite(cue.lengthCm) || cue.lengthCm <= 0f)
                {
                    throw new InvalidDataException(
                        $"Cue '{cue.name}' has invalid length {cue.lengthCm}. Must be positive and finite."
                    );
                }

                if (string.IsNullOrEmpty(cue.texture))
                {
                    throw new InvalidDataException($"Cue '{cue.name}' is missing required 'texture' field.");
                }

                string texturesDirectory = Path.Combine(Path.GetDirectoryName(filePath), "..", "Textures");
                string texturePath = Path.GetFullPath(Path.Combine(texturesDirectory, cue.texture));
                if (!File.Exists(texturePath))
                {
                    throw new InvalidDataException(
                        $"Cue '{cue.name}' references texture '{cue.texture}' but no file found at {texturePath}."
                    );
                }
            }

            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;

                if (trial == null)
                {
                    throw new InvalidDataException(
                        $"Trial '{trialName}' is empty. Every trial_structures entry must define a trial structure."
                    );
                }

                // Trial names are concatenated into segment prefab filenames (``TemplateName-TrialName.prefab``), so
                // operator-controlled punctuation, whitespace, or path separators would corrupt the generated
                // filesystem layout. Rejects them at load time before any asset path is computed downstream.
                if (!SegmentNameComponentPattern.IsMatch(trialName))
                {
                    string message =
                        $"Trial name '{trialName}' is invalid. Trial names must contain only ASCII letters, "
                        + "digits, and underscores (used in generated segment prefab filenames).";
                    throw new InvalidDataException(message);
                }

                if (trial.cueSequence == null || trial.cueSequence.Count == 0)
                {
                    throw new InvalidDataException($"Trial '{trialName}' has no cue sequence.");
                }

                foreach (string cueName in trial.cueSequence)
                {
                    if (!seenNames.Contains(cueName))
                    {
                        throw new InvalidDataException($"Trial '{trialName}' references unknown cue '{cueName}'.");
                    }
                }

                if (string.IsNullOrEmpty(trial.triggerType))
                {
                    throw new InvalidDataException($"Trial '{trialName}' is missing required 'trigger_type' field.");
                }

                if (
                    !string.Equals(trial.triggerType, "interaction", StringComparison.Ordinal)
                    && !string.Equals(trial.triggerType, "collision", StringComparison.Ordinal)
                    && !string.Equals(trial.triggerType, "occupancy_disarm", StringComparison.Ordinal)
                    && !string.Equals(trial.triggerType, "occupancy_arm", StringComparison.Ordinal)
                    && !string.Equals(trial.triggerType, "occupancy_trigger", StringComparison.Ordinal)
                )
                {
                    string message =
                        $"Trial '{trialName}' has invalid trigger_type '{trial.triggerType}'. Must be one of "
                        + "'interaction', 'collision', 'occupancy_disarm', 'occupancy_arm', 'occupancy_trigger'.";
                    throw new InvalidDataException(message);
                }

                // Null is how a template says the field is unused, so a non-occupancy trial sets occupancy_duration_ms
                // to null rather than to zero. Zero is a real duration and an invalid one, so the positive finite
                // range below binds every trial that supplies a value, whatever its trigger type. An occupancy mode
                // reads the field at runtime and therefore must supply one. This mirrors the sollertia-shared-assets
                // TrialStructure gate so the Python record and the Unity runtime agree.
                bool isOccupancy =
                    string.Equals(trial.triggerType, "occupancy_disarm", StringComparison.Ordinal)
                    || string.Equals(trial.triggerType, "occupancy_arm", StringComparison.Ordinal)
                    || string.Equals(trial.triggerType, "occupancy_trigger", StringComparison.Ordinal);

                if (isOccupancy && !trial.occupancyDurationMs.HasValue)
                {
                    string message =
                        $"Trial '{trialName}' has trigger_type '{trial.triggerType}', an occupancy mode, so "
                        + "occupancy_duration_ms is required, but it is unset.";
                    throw new InvalidDataException(message);
                }

                if (
                    trial.occupancyDurationMs.HasValue
                    && (!float.IsFinite(trial.occupancyDurationMs.Value) || trial.occupancyDurationMs.Value <= 0f)
                )
                {
                    string message =
                        $"Trial '{trialName}' has invalid occupancy_duration_ms {trial.occupancyDurationMs.Value}. "
                        + "Must be positive and finite.";
                    throw new InvalidDataException(message);
                }
            }

            // Identical cue sequences are indistinguishable to the experiment's cue-stream decomposer, which would
            // silently merge them.
            Dictionary<string, string> seenSequences = new Dictionary<string, string>();
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                string signature = string.Join(" ", trialEntry.Value.cueSequence);
                if (seenSequences.TryGetValue(signature, out string existingTrialName))
                {
                    string message =
                        $"Trials '{existingTrialName}' and '{trialName}' share an identical cue sequence. "
                        + "Each trial must have a unique cue sequence so the experiment can identify it; use "
                        + "distinct cue codes (textures may be shared) to multiplex visually identical cues.";
                    throw new InvalidDataException(message);
                }
                seenSequences[signature] = trialName;
            }

            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;

                if (!trial.HasTransitions)
                {
                    continue;
                }

                // Accumulates in double so a template listing many transition targets does not walk the running sum
                // into the tolerance through repeated single-precision rounding.
                double probabilitySum = 0d;
                foreach (KeyValuePair<string, float> transition in trial.transitions)
                {
                    if (!template.trialStructures.ContainsKey(transition.Key))
                    {
                        throw new InvalidDataException(
                            $"Trial '{trialName}' has a transition to unknown trial '{transition.Key}'."
                        );
                    }

                    // A negative weight still lets the set sum to 1.0 while removing its target from the sampled
                    // distribution, and a NaN weight passes every ordered comparison including the sum tolerance.
                    if (!float.IsFinite(transition.Value) || transition.Value < 0f || transition.Value > 1f)
                    {
                        string message =
                            $"Trial '{trialName}' has a transition to '{transition.Key}' with invalid probability "
                            + $"{transition.Value}. Must be between 0.0 and 1.0.";
                        throw new InvalidDataException(message);
                    }

                    probabilitySum += transition.Value;
                }

                if (Math.Abs(probabilitySum - 1.0) > ProbabilitySumTolerance)
                {
                    throw new InvalidDataException(
                        $"Trial '{trialName}' transition probabilities sum to {probabilitySum}, must be 1.0."
                    );
                }
            }
        }

        /// <summary>Validates the VR environment's corridor geometry scalars.</summary>
        /// <remarks>
        /// The segments_per_corridor, cm_per_unity_unit, and corridor_spacing_cm scalars divide or size downstream
        /// geometry, so a non-positive or non-finite value produces an infinite segment length, a zero-depth corridor,
        /// or a maze generation loop that never terminates. cue_offset_cm only has to be finite, because it shifts the
        /// segment origin in either direction.
        /// </remarks>
        /// <param name="environment">The VR environment block to validate.</param>
        /// <exception cref="InvalidDataException">A corridor geometry scalar is out of range.</exception>
        private static void ValidateVrEnvironment(VREnvironment environment)
        {
            if (environment.segmentsPerCorridor < 1)
            {
                throw new InvalidDataException(
                    $"Invalid segments_per_corridor {environment.segmentsPerCorridor}. Must be at least 1."
                );
            }

            if (!float.IsFinite(environment.cmPerUnityUnit) || environment.cmPerUnityUnit <= 0f)
            {
                throw new InvalidDataException(
                    $"Invalid cm_per_unity_unit {environment.cmPerUnityUnit}. Must be positive and finite."
                );
            }

            if (!float.IsFinite(environment.corridorSpacingCm) || environment.corridorSpacingCm <= 0f)
            {
                throw new InvalidDataException(
                    $"Invalid corridor_spacing_cm {environment.corridorSpacingCm}. Must be positive and finite."
                );
            }

            if (!float.IsFinite(environment.cueOffsetCm))
            {
                throw new InvalidDataException($"Invalid cue_offset_cm {environment.cueOffsetCm}. Must be finite.");
            }
        }
    }
}
