/// <summary>
/// Provides the GuidanceZone class that tracks whether an animal has entered a guidance trigger area.
///
/// Used as a child of StimulusTriggerZone to define where guidance mode delivers automatic stimulus.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>Tracks whether the animal is inside the guidance zone collider.</summary>
    public class GuidanceZone : MonoBehaviour, IResettable
    {
        /// <summary>Determines whether the animal is currently inside this guidance zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>Records that the animal is inside the guidance zone collider.</summary>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            inZone = true;
        }

        /// <summary>Records that the animal has left the guidance zone collider.</summary>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            inZone = false;
        }

        /// <summary>Resets the guidance zone state for a new lap.</summary>
        /// <remarks>
        /// The teleport carries the animal out of the collider without an exit callback, so the lap boundary is what
        /// clears the flag.
        /// </remarks>
        public void ResetState()
        {
            inZone = false;
        }
    }
}
