/// <summary>
/// Provides the GuidanceZone class that tracks whether an animal has entered a guidance trigger area.
///
/// Used as a child of StimulusTriggerZone to define where guidance mode delivers automatic stimulus.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Tracks whether the animal is inside the guidance zone collider.
    /// Used by parent StimulusTriggerZone to determine when to deliver automatic stimulus in guidance mode.
    /// </summary>
    public class GuidanceZone : MonoBehaviour, IResettable
    {
        /// <summary>Determines whether the animal is currently inside this guidance zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>Sets the zone state to active when the animal enters the guidance zone collider.</summary>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            inZone = true;
        }

        /// <summary>Sets the zone state to inactive when the animal exits the guidance zone collider.</summary>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            inZone = false;
        }

        /// <summary>Resets the guidance zone state for a new lap.</summary>
        /// <remarks>
        /// Invoked by <see cref="Task"/> when the actor advances into the next corridor. The teleport carries the
        /// animal out of the collider without an exit callback, so the lap boundary is what clears the flag.
        /// </remarks>
        public void ResetState()
        {
            inZone = false;
        }
    }
}
