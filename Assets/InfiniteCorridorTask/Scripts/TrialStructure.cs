/// <summary>
/// Provides the TrialStructure class that defines the spatial configuration of a trial structure for Unity prefabs.
/// </summary>
using System;
using System.Collections.Generic;

namespace SL.Config
{
    /// <summary>
    /// Defines the spatial configuration of a trial structure for Unity prefabs.
    /// Contains the trial's cue sequence, zone positions, optional transition probabilities, and visibility settings.
    /// Mirrors the TrialStructure class from sollertia-shared-assets vr_configuration module.
    /// </summary>
    [Serializable]
    public class TrialStructure
    {
        /// <summary>The ordered sequence of cue names that comprise this trial's segment.</summary>
        public List<string> cueSequence;

        /// <summary>The position of the trial stimulus trigger zone starting boundary, in centimeters.</summary>
        public float stimulusTriggerZoneStartCm;

        /// <summary>The position of the trial stimulus trigger zone ending boundary, in centimeters.</summary>
        public float stimulusTriggerZoneEndCm;

        /// <summary>
        /// The position of the stimulus boundary. The collision, occupancy_disarm, and occupancy_arm modes
        /// fire on collision with it. The interaction and occupancy_trigger modes resolve without boundary collision.
        /// </summary>
        public float stimulusLocationCm;

        /// <summary>
        /// Determines whether the stimulus collision boundary is visible to the animal during this trial type.
        /// When true, the boundary marker is displayed in the VR environment at the stimulus location.
        /// </summary>
        public bool showStimulusCollisionBoundary = false;

        /// <summary>
        /// The trigger mode for the stimulus zone, one of "interaction", "collision", "occupancy_disarm",
        /// "occupancy_arm", or "occupancy_trigger".
        /// </summary>
        public string triggerType;

        /// <summary>
        /// The duration in milliseconds the animal must occupy the zone for occupancy trigger modes, ignored for
        /// non-occupancy trigger modes.
        /// </summary>
        /// <remarks>
        /// Null is the value a non-occupancy trial carries, because null is how a template communicates that the
        /// field is unused. Zero is a real duration rather than that signal, so it is rejected on every trial
        /// whatever its trigger type. The nullable type mirrors the sollertia-shared-assets default, so a missing
        /// value never silently resolves to a fabricated duration.
        /// </remarks>
        public float? occupancyDurationMs = null;

        /// <summary>
        /// The optional probability distribution over the trial names that may follow this trial during corridor
        /// traversal. Keys must reference other trial names defined on the same TaskTemplate. If provided, the
        /// values must sum to 1.0. Sparse: omitted keys carry implicit zero probability. When null or empty, the
        /// Task samples the next trial uniformly at random over all defined trial names.
        /// </summary>
        public Dictionary<string, float> transitions;

        /// <summary>Determines whether transition probabilities are defined for this trial.</summary>
        public bool HasTransitions => transitions != null && transitions.Count > 0;
    }
}
