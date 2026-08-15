/// <summary>
/// Provides the OccupancyGuidanceZone class that triggers brake activation in occupancy guidance mode.
///
/// Used as a child of OccupancyZone to define where guidance mode activates the brake.
/// </summary>
using System;
using Gimbl;
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Handles occupancy guidance mode as a secondary trigger zone for OccupancyZone.
    /// When guidance mode is active and the animal enters, sends a brake activation message with the remaining
    /// duration.
    /// </summary>
    public class OccupancyGuidanceZone : MonoBehaviour, IResettable
    {
        /// <summary>The reference to the Task for checking guidance mode state.</summary>
        private Task _task;

        /// <summary>
        /// The reference to the parent OccupancyZone, used to read occupancyMet and compute the remaining
        /// occupancy duration.
        /// </summary>
        private OccupancyZone _parentOccupancyZone;

        /// <summary>The MQTT channel for sending brake activation delay messages.</summary>
        private MQTTChannel<TriggerDelayMessage> _triggerDelayChannel;

        /// <summary>Determines whether the guidance trigger has already fired this lap.</summary>
        private bool _hasTriggered = false;

        /// <summary>Determines whether the brake guidance fired during the current lap.</summary>
        public bool BrakeTriggered => _hasTriggered;

        /// <summary>Initializes references and sets up the MQTT channel.</summary>
        private void Start()
        {
            _task = FindAnyObjectByType<Task>();
            if (_task == null)
            {
                Debug.LogError($"OccupancyGuidanceZone ({gameObject.name}): No Task found in scene.");
                enabled = false;
                return;
            }

            _parentOccupancyZone = GetComponentInParent<OccupancyZone>();
            if (_parentOccupancyZone == null)
            {
                Debug.LogError($"OccupancyGuidanceZone ({gameObject.name}): No parent OccupancyZone found.");
                enabled = false;
                return;
            }

            _triggerDelayChannel = new MQTTChannel<TriggerDelayMessage>(MQTTTopics.Delay, isListener: false);

            // Establishes the first lap through the same path every later lap uses, so a serialized value can never
            // leave this zone's startup state diverging from its per-lap default.
            ResetState();
        }

        /// <summary>Requests the brake when the animal enters the guidance zone collider.</summary>
        /// <remarks>
        /// Entry in guidance mode, meaning <c>requireWait</c> is false, sends a TriggerDelay message to
        /// sollertia-experiment instructing it to lock the brake for the remaining occupancy duration. This zone
        /// occupies the downstream end of the parent's collider, so an animal reaching it is about to leave the
        /// occupancy range, and the brake holds it inside long enough for the parent's timer to complete. The brake
        /// request reads the collaborators <see cref="Start"/> resolves, so an entry reaching a zone whose resolution
        /// failed relies on the error <see cref="Start"/> already logged.
        /// </remarks>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (_task == null || _parentOccupancyZone == null)
                return;

            // The brake is what guides the animal to finish an occupancy requirement it has not yet met.
            if (!_task.requireWait && !_hasTriggered && !_parentOccupancyZone.occupancyMet)
            {
                TriggerBrakeActivation();
            }
        }

        /// <summary>Resets the guidance zone state for a new lap.</summary>
        public void ResetState()
        {
            _hasTriggered = false;
        }

        /// <summary>Sends the TriggerDelay message with remaining occupancy duration to activate the brake.</summary>
        private void TriggerBrakeActivation()
        {
            // Clamps the remaining duration to zero so an overrun never produces a negative delay. The subtraction
            // runs at long width so the millisecond count reaches the uint wire field without passing through a
            // 32-bit float, whose 24-bit mantissa holds integers exactly only below 16,777,216 milliseconds.
            long elapsedMilliseconds = _parentOccupancyZone.GetElapsedMilliseconds();
            uint remainingMilliseconds = (uint)
                Math.Max(0L, (long)_parentOccupancyZone.occupancyDurationMs - elapsedMilliseconds);

            Debug.Log($"OccupancyGuidanceZone: Triggering brake for {remainingMilliseconds}ms.");

            _triggerDelayChannel.Send(new TriggerDelayMessage { delayMilliseconds = remainingMilliseconds });
            _hasTriggered = true;
        }

        /// <summary>Wraps trigger delay duration for MQTT transmission.</summary>
        public class TriggerDelayMessage
        {
            /// <summary>The delay duration in milliseconds before the brake releases.</summary>
            public uint delayMilliseconds;
        }
    }
}
