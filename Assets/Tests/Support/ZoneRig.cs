/// <summary>
/// Provides the ZoneRig that assembles a trigger zone hierarchy and drives its Unity callbacks from a test.
/// </summary>
using System;
using Gimbl;
using SL.Tasks;
using UnityEngine;

namespace SL.Tests
{
    /// <summary>
    /// Assembles a Task and trigger zone hierarchy and exposes the transitions a test drives against it.
    /// </summary>
    /// <remarks>
    /// The hierarchy mirrors the hand-authored StimulusTriggerZone and OccupancyTriggerZone prefabs, because every zone
    /// resolves its collaborators through GetComponentInChildren or GetComponentInParent at Start. The drive methods
    /// invoke the private lifecycle and trigger callbacks directly, so an Edit Mode test advances the state machine one
    /// deterministic step at a time without a player loop or a physics tick. Every zone callback ignores its Collider
    /// argument, so the rig passes null.
    /// </remarks>
    public sealed class ZoneRig : IDisposable
    {
        /// <summary>The root object parenting every object the rig creates.</summary>
        private readonly GameObject _root;

        /// <summary>The MQTT harness capturing everything the zones publish.</summary>
        public MqttTestHarness Mqtt { get; }

        /// <summary>The task supplying the requireInteraction and requireWait toggles.</summary>
        public Task Task { get; }

        /// <summary>The stimulus zone under test.</summary>
        public StimulusTriggerZone StimulusZone { get; }

        /// <summary>The guidance child zone, or null when the options excluded it.</summary>
        public GuidanceZone GuidanceZone { get; }

        /// <summary>The occupancy child zone, or null when the options excluded it.</summary>
        public OccupancyZone OccupancyZone { get; }

        /// <summary>The occupancy guidance grandchild zone, or null when the options excluded it.</summary>
        public OccupancyGuidanceZone OccupancyGuidanceZone { get; }

        /// <summary>The boundary indicator renderer, or null when the options excluded it.</summary>
        public MeshRenderer BoundaryRenderer { get; }

        /// <summary>Builds the hierarchy the options describe.</summary>
        /// <param name="options">The composition and initial field values.</param>
        private ZoneRig(ZoneRigOptions options)
        {
            Mqtt = MqttTestHarness.Create();

            _root = new GameObject("ZoneRig");

            GameObject taskObject = new GameObject("Task");
            taskObject.transform.SetParent(_root.transform);
            Task = taskObject.AddComponent<Task>();
            Task.requireInteraction = options.requireInteraction;
            Task.requireWait = options.requireWait;

            GameObject stimulusObject = new GameObject("StimulusTriggerZone");
            stimulusObject.transform.SetParent(_root.transform);
            stimulusObject.AddComponent<BoxCollider>().isTrigger = true;
            if (options.includeBoundaryRenderer)
            {
                BoundaryRenderer = stimulusObject.AddComponent<MeshRenderer>();
            }
            StimulusZone = stimulusObject.AddComponent<StimulusTriggerZone>();
            StimulusZone.triggerMode = options.triggerMode;
            StimulusZone.showBoundary = options.showBoundary;
            StimulusZone.trialName = options.trialName;

            if (options.includeGuidanceZone)
            {
                GameObject guidanceObject = new GameObject("GuidanceRegion");
                guidanceObject.transform.SetParent(stimulusObject.transform);
                guidanceObject.AddComponent<BoxCollider>().isTrigger = true;
                GuidanceZone = guidanceObject.AddComponent<GuidanceZone>();
            }

            if (options.includeOccupancyZone)
            {
                GameObject occupancyObject = new GameObject("OccupancyRegion");
                occupancyObject.transform.SetParent(stimulusObject.transform);
                occupancyObject.AddComponent<BoxCollider>().isTrigger = true;
                OccupancyZone = occupancyObject.AddComponent<OccupancyZone>();
                OccupancyZone.occupancyDurationMs = options.occupancyDurationMs;

                if (options.includeOccupancyGuidanceZone)
                {
                    GameObject occupancyGuidanceObject = new GameObject("OccupancyGuidanceRegion");
                    occupancyGuidanceObject.transform.SetParent(occupancyObject.transform);
                    occupancyGuidanceObject.AddComponent<BoxCollider>().isTrigger = true;
                    OccupancyGuidanceZone = occupancyGuidanceObject.AddComponent<OccupancyGuidanceZone>();
                }
            }
        }

        /// <summary>Builds a rig from the supplied options.</summary>
        /// <param name="options">The composition and initial field values.</param>
        /// <returns>The rig, which the caller disposes to destroy every object it created.</returns>
        public static ZoneRig Create(ZoneRigOptions options)
        {
            return new ZoneRig(options);
        }

        /// <summary>Runs each zone's Start in an order Unity is able to produce.</summary>
        /// <remarks>
        /// The occupancy zone starts first because its Start allocates the stopwatch that the occupancy guidance zone
        /// reads, and the stimulus zone starts last because its Start resolves both child zones.
        /// </remarks>
        public void StartComponents()
        {
            if (OccupancyZone != null)
            {
                PrivateAccess.Invoke(OccupancyZone, "Start");
            }
            if (OccupancyGuidanceZone != null)
            {
                PrivateAccess.Invoke(OccupancyGuidanceZone, "Start");
            }
            PrivateAccess.Invoke(StimulusZone, "Start");
        }

        /// <summary>Runs one simulated frame: the occupancy zone's Update, then the stimulus zone's.</summary>
        public void Tick()
        {
            if (OccupancyZone != null)
            {
                PrivateAccess.Invoke(OccupancyZone, "Update");
            }
            PrivateAccess.Invoke(StimulusZone, "Update");
        }

        /// <summary>Runs the stimulus zone's Update alone.</summary>
        public void TickStimulusZone()
        {
            PrivateAccess.Invoke(StimulusZone, "Update");
        }

        /// <summary>Runs the occupancy zone's Update alone.</summary>
        public void TickOccupancyZone()
        {
            PrivateAccess.Invoke(RequireOccupancyZone(), "Update");
        }

        /// <summary>Drives the stimulus zone's OnTriggerEnter.</summary>
        public void EnterStimulusZone()
        {
            PrivateAccess.Invoke(StimulusZone, "OnTriggerEnter", new object[] { null });
        }

        /// <summary>Drives the stimulus zone's OnTriggerExit.</summary>
        public void ExitStimulusZone()
        {
            PrivateAccess.Invoke(StimulusZone, "OnTriggerExit", new object[] { null });
        }

        /// <summary>Drives the guidance zone's OnTriggerEnter.</summary>
        public void EnterGuidanceZone()
        {
            PrivateAccess.Invoke(RequireGuidanceZone(), "OnTriggerEnter", new object[] { null });
        }

        /// <summary>Drives the guidance zone's OnTriggerExit.</summary>
        public void ExitGuidanceZone()
        {
            PrivateAccess.Invoke(RequireGuidanceZone(), "OnTriggerExit", new object[] { null });
        }

        /// <summary>Drives the occupancy zone's OnTriggerEnter.</summary>
        public void EnterOccupancyZone()
        {
            PrivateAccess.Invoke(RequireOccupancyZone(), "OnTriggerEnter", new object[] { null });
        }

        /// <summary>Drives the occupancy zone's OnTriggerExit.</summary>
        public void ExitOccupancyZone()
        {
            PrivateAccess.Invoke(RequireOccupancyZone(), "OnTriggerExit", new object[] { null });
        }

        /// <summary>Drives the occupancy guidance zone's OnTriggerEnter.</summary>
        public void EnterOccupancyGuidanceZone()
        {
            PrivateAccess.Invoke(RequireOccupancyGuidanceZone(), "OnTriggerEnter", new object[] { null });
        }

        /// <summary>Drives the occupancy guidance zone's OnTriggerExit.</summary>
        public void ExitOccupancyGuidanceZone()
        {
            PrivateAccess.Invoke(RequireOccupancyGuidanceZone(), "OnTriggerExit", new object[] { null });
        }

        /// <summary>Publishes an interaction event on the topic the stimulus zone listens to.</summary>
        public void RaiseInteraction()
        {
            Mqtt.PublishTrigger(MQTTTopics.Interaction);
        }

        /// <summary>Returns the occupancy zone's elapsed timer reading in milliseconds.</summary>
        /// <returns>The elapsed milliseconds since the occupancy timer last restarted.</returns>
        public long OccupancyElapsedMilliseconds()
        {
            return (long)PrivateAccess.Invoke(RequireOccupancyZone(), "GetElapsedMilliseconds");
        }

        /// <summary>Returns every stimulus outcome the stimulus zone has published, oldest first.</summary>
        /// <returns>The published outcomes.</returns>
        public System.Collections.Generic.List<StimulusTriggerZone.StimulusMessage> StimulusOutcomes()
        {
            return Mqtt.MessagesOn<StimulusTriggerZone.StimulusMessage>(MQTTTopics.Stimulus);
        }

        /// <summary>Destroys every object the rig created and removes the MQTT singleton.</summary>
        public void Dispose()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            Mqtt.Dispose();
        }

        /// <summary>Returns the guidance zone, rejecting a rig that was composed without one.</summary>
        /// <returns>The guidance zone.</returns>
        /// <exception cref="InvalidOperationException">The rig options did not set includeGuidanceZone.</exception>
        private GuidanceZone RequireGuidanceZone()
        {
            if (GuidanceZone == null)
            {
                string message =
                    "Unable to drive the guidance zone. The rig options must set includeGuidanceZone, but the rig was "
                    + "composed without one.";
                throw new InvalidOperationException(message);
            }
            return GuidanceZone;
        }

        /// <summary>Returns the occupancy zone, rejecting a rig that was composed without one.</summary>
        /// <returns>The occupancy zone.</returns>
        /// <exception cref="InvalidOperationException">The rig options did not set includeOccupancyZone.</exception>
        private OccupancyZone RequireOccupancyZone()
        {
            if (OccupancyZone == null)
            {
                string message =
                    "Unable to drive the occupancy zone. The rig options must set includeOccupancyZone, but the rig "
                    + "was composed without one.";
                throw new InvalidOperationException(message);
            }
            return OccupancyZone;
        }

        /// <summary>Returns the occupancy guidance zone, rejecting a rig that was composed without one.</summary>
        /// <returns>The occupancy guidance zone.</returns>
        /// <exception cref="InvalidOperationException">
        /// The rig options did not set includeOccupancyGuidanceZone.
        /// </exception>
        private OccupancyGuidanceZone RequireOccupancyGuidanceZone()
        {
            if (OccupancyGuidanceZone == null)
            {
                string message =
                    "Unable to drive the occupancy guidance zone. The rig options must set "
                    + "includeOccupancyGuidanceZone, but the rig was composed without one.";
                throw new InvalidOperationException(message);
            }
            return OccupancyGuidanceZone;
        }
    }
}
