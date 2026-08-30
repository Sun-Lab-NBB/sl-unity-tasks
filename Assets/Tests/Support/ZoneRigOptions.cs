/// <summary>
/// Provides the ZoneRigOptions record selecting which zone components a ZoneRig assembles and how they start out.
/// </summary>
using SL.Tasks;

namespace SL.Tests
{
    /// <summary>Selects the composition and the initial field values of a <see cref="ZoneRig"/>.</summary>
    public sealed class ZoneRigOptions
    {
        /// <summary>The trigger mechanism assigned to the stimulus zone.</summary>
        public TriggerMode triggerMode = TriggerMode.Interaction;

        /// <summary>Determines whether a GuidanceZone child is attached under the stimulus zone.</summary>
        internal bool includeGuidanceZone = true;

        /// <summary>Determines whether an OccupancyZone child is attached under the stimulus zone.</summary>
        internal bool includeOccupancyZone = false;

        /// <summary>Determines whether an OccupancyGuidanceZone child is attached under the occupancy zone.</summary>
        public bool includeOccupancyGuidanceZone = false;

        /// <summary>Determines whether the stimulus zone object carries a MeshRenderer boundary indicator.</summary>
        public bool includeBoundaryRenderer = true;

        /// <summary>Determines whether the stimulus zone shows its boundary while it is active.</summary>
        public bool showBoundary = false;

        /// <summary>The trial name the stimulus zone publishes with every outcome.</summary>
        public string trialName = "TestTrial";

        /// <summary>The occupancy duration in milliseconds assigned to the occupancy zone.</summary>
        public float occupancyDurationMs = 1000f;

        /// <summary>Determines whether the task requires an interaction, gating interaction-mode guidance.</summary>
        public bool requireInteraction = false;

        /// <summary>Determines whether the task requires a wait, gating occupancy-mode brake guidance.</summary>
        public bool requireWait = false;

        /// <summary>
        /// Returns options composing the two-child occupancy hierarchy for an occupancy trigger mode.
        /// </summary>
        /// <param name="mode">The occupancy trigger mode assigned to the stimulus zone.</param>
        /// <param name="durationMs">The occupancy duration in milliseconds.</param>
        /// <returns>The composed options.</returns>
        public static ZoneRigOptions Occupancy(TriggerMode mode, float durationMs = 1000f)
        {
            return new ZoneRigOptions
            {
                triggerMode = mode,
                includeGuidanceZone = false,
                includeOccupancyZone = true,
                includeOccupancyGuidanceZone = true,
                occupancyDurationMs = durationMs,
            };
        }

        /// <summary>Returns options composing the interaction hierarchy with an optional guidance child.</summary>
        /// <param name="withGuidanceZone">Determines whether the GuidanceZone child is attached.</param>
        /// <returns>The composed options.</returns>
        public static ZoneRigOptions Interaction(bool withGuidanceZone = true)
        {
            return new ZoneRigOptions { triggerMode = TriggerMode.Interaction, includeGuidanceZone = withGuidanceZone };
        }

        /// <summary>Returns options composing the collision hierarchy, which carries no child zones.</summary>
        /// <returns>The composed options.</returns>
        public static ZoneRigOptions Collision()
        {
            return new ZoneRigOptions { triggerMode = TriggerMode.Collision, includeGuidanceZone = false };
        }
    }
}
