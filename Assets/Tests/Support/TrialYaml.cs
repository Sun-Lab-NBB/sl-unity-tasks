/// <summary>
/// Provides the TrialYaml builder describing one entry of a task template's trial_structures mapping.
/// </summary>
using System;
using System.Collections.Generic;
using System.Text;

namespace SL.Tests
{
    /// <summary>Builds the YAML block for a single trial structure.</summary>
    /// <remarks>
    /// A typed field set to null omits its YAML key entirely, which is how a test reaches ConfigLoader's missing-field
    /// branches.
    /// </remarks>
    public sealed class TrialYaml
    {
        /// <summary>The trial name, which becomes the mapping key under trial_structures.</summary>
        public string name = "AB";

        /// <summary>The ordered cue names, or null to omit the cue_sequence key.</summary>
        public List<string> cueSequence = new List<string> { "A", "B" };

        /// <summary>The trigger zone start boundary in centimeters, or null to omit the key.</summary>
        public float? stimulusTriggerZoneStartCm = 0f;

        /// <summary>The trigger zone end boundary in centimeters, or null to omit the key.</summary>
        public float? stimulusTriggerZoneEndCm = 30f;

        /// <summary>The stimulus boundary position in centimeters, or null to omit the key.</summary>
        public float? stimulusLocationCm = 25f;

        /// <summary>Determines whether the boundary is visible, or null to omit the key.</summary>
        public bool? showStimulusCollisionBoundary = false;

        /// <summary>The trigger type literal, or null to omit the trigger_type key.</summary>
        public string triggerType = "collision";

        /// <summary>The required occupancy duration in milliseconds, or null to omit the key.</summary>
        public float? occupancyDurationMs = null;

        /// <summary>The transition distribution over trial names, or null to omit the transitions key.</summary>
        public Dictionary<string, float> transitions = null;

        /// <summary>The literal YAML text emitted for a key, overriding whatever the typed field holds.</summary>
        /// <remarks>
        /// An entry reaches the ConfigLoader branches a well-typed value cannot express, such as a wrong-typed or
        /// malformed scalar.
        /// </remarks>
        public readonly Dictionary<string, string> rawOverrides = new Dictionary<string, string>();

        /// <summary>Creates a trial block with the supplied name and cue sequence.</summary>
        /// <param name="trialName">The trial name used as the mapping key.</param>
        /// <param name="cues">The ordered cue names comprising the trial's segment.</param>
        /// <returns>The trial block builder.</returns>
        public static TrialYaml Named(string trialName, params string[] cues)
        {
            return new TrialYaml { name = trialName, cueSequence = new List<string>(cues) };
        }

        /// <summary>Sets the trigger type and the occupancy duration the occupancy modes require.</summary>
        /// <param name="type">The trigger type literal.</param>
        /// <param name="durationMs">The occupancy duration in milliseconds, or null to leave it unset.</param>
        /// <returns>This builder, so calls chain.</returns>
        public TrialYaml WithTrigger(string type, float? durationMs = null)
        {
            triggerType = type;
            occupancyDurationMs = durationMs;
            return this;
        }

        /// <summary>Sets the transition distribution over the trial names that may follow this trial.</summary>
        /// <param name="distribution">The trial-name keyed probabilities.</param>
        /// <returns>This builder, so calls chain.</returns>
        public TrialYaml WithTransitions(Dictionary<string, float> distribution)
        {
            transitions = distribution;
            return this;
        }

        /// <summary>Appends this trial as a mapping entry under the trial_structures key.</summary>
        /// <param name="builder">The document builder the trial block is appended to.</param>
        internal void AppendTo(StringBuilder builder)
        {
            builder.AppendLine($"  {name}:");

            AppendEntry(builder, "cue_sequence", cueSequence == null ? null : RenderCueSequence(cueSequence));
            AppendEntry(
                builder,
                "stimulus_trigger_zone_start_cm",
                stimulusTriggerZoneStartCm.HasValue ? YamlScalar.Number(stimulusTriggerZoneStartCm.Value) : null
            );
            AppendEntry(
                builder,
                "stimulus_trigger_zone_end_cm",
                stimulusTriggerZoneEndCm.HasValue ? YamlScalar.Number(stimulusTriggerZoneEndCm.Value) : null
            );
            AppendEntry(
                builder,
                "stimulus_location_cm",
                stimulusLocationCm.HasValue ? YamlScalar.Number(stimulusLocationCm.Value) : null
            );
            AppendEntry(
                builder,
                "show_stimulus_collision_boundary",
                showStimulusCollisionBoundary.HasValue ? YamlScalar.Boolean(showStimulusCollisionBoundary.Value) : null
            );
            AppendEntry(builder, "trigger_type", triggerType == null ? null : YamlScalar.Text(triggerType));
            AppendEntry(
                builder,
                "occupancy_duration_ms",
                occupancyDurationMs.HasValue ? YamlScalar.Number(occupancyDurationMs.Value) : null
            );

            AppendTransitions(builder);

            foreach (KeyValuePair<string, string> entry in rawOverrides)
            {
                if (!IsTypedKey(entry.Key))
                {
                    builder.AppendLine($"    {entry.Key}: {entry.Value}");
                }
            }
        }

        /// <summary>Renders a cue sequence as an inline YAML flow sequence.</summary>
        /// <param name="cues">The ordered cue names.</param>
        /// <returns>The rendered flow sequence.</returns>
        private static string RenderCueSequence(List<string> cues)
        {
            List<string> rendered = new List<string>(cues.Count);
            foreach (string cue in cues)
            {
                rendered.Add(YamlScalar.Text(cue));
            }
            return $"[{string.Join(", ", rendered)}]";
        }

        /// <summary>Determines whether a key is one of the typed fields this builder already emits.</summary>
        /// <param name="key">The underscored YAML key.</param>
        /// <returns>True when the key names a typed field, false otherwise.</returns>
        private static bool IsTypedKey(string key)
        {
            return key switch
            {
                "cue_sequence" => true,
                "stimulus_trigger_zone_start_cm" => true,
                "stimulus_trigger_zone_end_cm" => true,
                "stimulus_location_cm" => true,
                "show_stimulus_collision_boundary" => true,
                "trigger_type" => true,
                "occupancy_duration_ms" => true,
                "transitions" => true,
                _ => false,
            };
        }

        /// <summary>Appends the transitions block, honoring a raw override for the whole key.</summary>
        /// <param name="builder">The document builder the block is appended to.</param>
        private void AppendTransitions(StringBuilder builder)
        {
            if (rawOverrides.TryGetValue("transitions", out string rawValue))
            {
                builder.AppendLine($"    transitions: {rawValue}");
                return;
            }
            if (transitions == null)
            {
                return;
            }
            if (transitions.Count == 0)
            {
                builder.AppendLine("    transitions: {}");
                return;
            }

            builder.AppendLine("    transitions:");
            foreach (KeyValuePair<string, float> transition in transitions)
            {
                builder.AppendLine($"      {transition.Key}: {YamlScalar.Number(transition.Value)}");
            }
        }

        /// <summary>Appends one key line, preferring the raw override when the key carries one.</summary>
        /// <param name="builder">The document builder the line is appended to.</param>
        /// <param name="key">The underscored YAML key.</param>
        /// <param name="renderedValue">The rendered typed value, or null when the key is omitted.</param>
        private void AppendEntry(StringBuilder builder, string key, string renderedValue)
        {
            if (rawOverrides.TryGetValue(key, out string rawValue))
            {
                builder.AppendLine($"    {key}: {rawValue}");
                return;
            }
            if (renderedValue == null)
            {
                return;
            }
            builder.AppendLine($"    {key}: {renderedValue}");
        }
    }
}
