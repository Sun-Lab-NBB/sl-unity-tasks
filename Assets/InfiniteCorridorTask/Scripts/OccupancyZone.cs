/// <summary>
/// Provides the OccupancyZone class that tracks whether an animal has occupied a zone for a required duration. The
/// occupancy_disarm, occupancy_arm, and occupancy_trigger modes of the parent StimulusTriggerZone read the tracked
/// state.
/// </summary>
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tasks
{
    /// <summary>
    /// Tracks animal occupancy duration within a zone and exposes whether the occupancy requirement was met.
    /// </summary>
    /// <remarks>
    /// The occupancy mode specifies how a stimulus is triggered rather than which stimulus is delivered, because any
    /// stimulus type pairs with any trigger mode.
    /// </remarks>
    public class OccupancyZone : MonoBehaviour, IResettable
    {
        /// <summary>
        /// The duration in milliseconds that the animal must occupy the zone to meet the occupancy requirement.
        /// Set at task creation time from the task template.
        /// </summary>
        public float occupancyDurationMs = 1000f;

        /// <summary>
        /// Determines whether the animal is inside this zone while it is actively tracking occupancy. Only set true
        /// when the zone is active and occupancy is not yet met, so it stays false on entries after occupancy is met.
        /// </summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>
        /// Determines whether the animal has met the occupancy requirement (occupied for the required duration). Once
        /// set, it latches the zone so Update and OnTriggerEnter short-circuit, limiting firing to once per lap until
        /// the corridor advance clears it at lap start.
        /// </summary>
        [HideInInspector]
        public bool occupancyMet = false;

        /// <summary>Determines whether this zone tracks occupancy. Reset to true at each corridor advance.</summary>
        public bool isActive = true;

        /// <summary>The high-precision stopwatch for accurate millisecond timing.</summary>
        /// <remarks>
        /// The field initializer allocates the stopwatch, so every callback and every external caller reaches a usable
        /// timer from the moment the component is constructed rather than from the moment Unity runs Start.
        /// </remarks>
        private readonly Stopwatch _occupancyTimer = new Stopwatch();

        /// <summary>Establishes the first lap's state.</summary>
        /// <remarks>
        /// The first lap runs through the same reset path every later lap uses, so a serialized value can never leave
        /// this zone's startup state diverging from its per-lap default.
        /// </remarks>
        private void Start()
        {
            ResetState();
        }

        /// <summary>Checks if the occupancy duration has been met while the animal is in the zone.</summary>
        /// <remarks>
        /// The requirement is met once the animal has stayed in the zone for <see cref="occupancyDurationMs"/> without
        /// leaving, because each entry restarts the timer.
        /// </remarks>
        private void Update()
        {
            if (!isActive || occupancyMet)
                return;

            if (_occupancyTimer.IsRunning && inZone)
            {
                if (_occupancyTimer.ElapsedMilliseconds >= occupancyDurationMs)
                {
                    OnOccupancyMet();
                }
            }
        }

        /// <summary>Starts the occupancy timer when the animal enters the zone collider.</summary>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (!isActive || occupancyMet)
                return;

            inZone = true;
            _occupancyTimer.Restart();
            Debug.Log("OccupancyZone: Animal entered, timer started.");
        }

        /// <summary>Stops the timer and checks the result when the animal exits the zone collider.</summary>
        /// <remarks>
        /// The occupancy flag clears ahead of the activity guard, so a zone deactivated while the animal stood inside
        /// it records the departure. An exit before the required duration elapses leaves <see cref="occupancyMet"/>
        /// false.
        /// </remarks>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            inZone = false;

            if (!isActive)
                return;

            _occupancyTimer.Stop();

            if (!occupancyMet)
            {
                OnOccupancyFailed();
            }
        }

        /// <summary>Resets the occupancy zone state for a new lap.</summary>
        public void ResetState()
        {
            isActive = true;
            occupancyMet = false;
            inZone = false;
            _occupancyTimer.Reset();
        }

        /// <summary>Returns the elapsed time in milliseconds since the occupancy timer started.</summary>
        internal long GetElapsedMilliseconds()
        {
            return _occupancyTimer.ElapsedMilliseconds;
        }

        /// <summary>Marks the occupancy requirement as met once the animal has occupied the zone long enough.</summary>
        /// <remarks>
        /// The timer stops ahead of the logging call, so the retained reading measures occupancy alone.
        /// </remarks>
        private void OnOccupancyMet()
        {
            _occupancyTimer.Stop();
            occupancyMet = true;
            Debug.Log("OccupancyZone: Occupancy requirement met.");
        }

        /// <summary>Logs a message when the animal leaves the zone before meeting the occupancy requirement.</summary>
        private void OnOccupancyFailed()
        {
            Debug.Log("OccupancyZone: Occupancy failed - animal left early.");
        }
    }
}
