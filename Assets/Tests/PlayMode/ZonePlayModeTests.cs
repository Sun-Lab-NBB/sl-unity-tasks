/// <summary>
/// Verifies the behavior of the trigger zone hierarchy under the real Unity player loop.
/// </summary>
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tests.PlayMode
{
    /// <summary>Verifies the behavior of the trigger zone hierarchy under the real Unity player loop.</summary>
    /// <remarks>
    /// The Edit Mode zone suites invoke OnTriggerEnter, OnTriggerExit, Start, and Update through reflection, which
    /// makes the state machines deterministic but leaves three things unobservable. The first two are the trigger
    /// callbacks Unity's own physics raises when a Rigidbody-carrying actor sweeps through a trigger collider, and the
    /// Stopwatch readings an occupancy requirement accumulates over wall-clock time. The third is the Awake and Start
    /// ordering the engine imposes on a freshly created hierarchy. This fixture covers exactly those three.
    /// </remarks>
    [TestFixture]
    public class ZonePlayModeTests
    {
        /// <summary>The dataPath-relative directory the staged template is written into.</summary>
        private const string ConfigurationsDirectory = "InfiniteCorridorTask/Configurations";

        /// <summary>The name of the template every rig's task loads so its Start succeeds quietly.</summary>
        private const string ZoneTemplateName = "ZZTest_PlayZone";

        /// <summary>The texture the staged cue references, which already ships under Textures.</summary>
        private const string StagedTextureName = "Gray Cue 2x1.png";

        /// <summary>The track length the staged task generates its maze with, in Unity units.</summary>
        private const float ZoneTrackLength = 100f;

        /// <summary>The seed the staged task generates its maze with.</summary>
        private const int ZoneSeed = 4242;

        /// <summary>The width and height of every zone collider, in Unity units.</summary>
        private const float ZoneCrossSection = 10f;

        /// <summary>The world z position of the stimulus zone collider's center.</summary>
        private const float StimulusZoneCenterZ = 10f;

        /// <summary>The depth of the stimulus zone collider, which spans z 8 through 12.</summary>
        private const float StimulusZoneDepth = 4f;

        /// <summary>The world z position of the guidance zone collider's center.</summary>
        private const float GuidanceZoneCenterZ = 11f;

        /// <summary>The depth of the guidance zone collider, which spans z 10 through 12.</summary>
        private const float GuidanceZoneDepth = 2f;

        /// <summary>The world z position of the occupancy zone collider's center.</summary>
        private const float OccupancyZoneCenterZ = 4f;

        /// <summary>The depth of the occupancy zone collider, which spans z 2 through 6.</summary>
        private const float OccupancyZoneDepth = 4f;

        /// <summary>The world z position of the occupancy guidance zone collider's center.</summary>
        private const float OccupancyGuidanceZoneCenterZ = 5f;

        /// <summary>The depth of the occupancy guidance zone collider, which spans z 4 through 6.</summary>
        private const float OccupancyGuidanceZoneDepth = 2f;

        /// <summary>The actor z position that overlaps no zone collider at all.</summary>
        private const float OutsideEveryZoneZ = -10f;

        /// <summary>The actor z position whose leading face stops half a unit short of the stimulus collider.</summary>
        private const float JustShortOfStimulusZoneZ = 7f;

        /// <summary>The actor z position whose trailing face clears the stimulus collider by half a unit.</summary>
        private const float JustPastStimulusZoneZ = 13f;

        /// <summary>The actor z position inside the stimulus collider but short of the guidance collider.</summary>
        private const float InsideStimulusZoneZ = 9f;

        /// <summary>The actor z position inside both the stimulus collider and the guidance collider.</summary>
        private const float InsideGuidanceZoneZ = 11f;

        /// <summary>The actor z position inside the occupancy collider but short of its guidance collider.</summary>
        private const float InsideOccupancyZoneZ = 3f;

        /// <summary>The actor z position inside both the occupancy collider and its guidance collider.</summary>
        private const float InsideOccupancyGuidanceZoneZ = 5f;

        /// <summary>The occupancy requirement a test satisfies by waiting it out, in milliseconds.</summary>
        /// <remarks>
        /// The value sits far enough above a frame time that the check taken on the frame after the actor enters the
        /// zone reads an unmet requirement even on a slow editor frame. It also sits far enough below
        /// <see cref="OccupancyMetWaitSeconds"/> that the wait always outlasts it.
        /// </remarks>
        private const float ShortOccupancyMilliseconds = 250f;

        /// <summary>The occupancy requirement no test in this fixture ever satisfies, in milliseconds.</summary>
        private const float LongOccupancyMilliseconds = 2000f;

        /// <summary>The wall-clock wait that outlasts <see cref="ShortOccupancyMilliseconds"/>, in seconds.</summary>
        private const float OccupancyMetWaitSeconds = 0.45f;

        /// <summary>The wall-clock wait that falls well short of <see cref="LongOccupancyMilliseconds"/>.</summary>
        private const float PartialOccupancyWaitSeconds = 0.3f;

        /// <summary>
        /// The lower bound in milliseconds that <see cref="PartialOccupancyWaitSeconds"/> is guaranteed to have
        /// accumulated on the occupancy stopwatch, kept below the nominal wait to absorb timer granularity.
        /// </summary>
        private const long PartialOccupancyFloorMilliseconds = 250L;

        /// <summary>The zone hierarchy and MQTT harness under test.</summary>
        private ZoneRig _rig;

        /// <summary>The Rigidbody-carrying object Unity's physics sweeps through the trigger colliders.</summary>
        private GameObject _actorObject;

        /// <summary>An extra object a single test creates outside the rig, or null when no test created one.</summary>
        private GameObject _detachedObject;

        /// <summary>Writes the staged template into the project's Configurations directory.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            File.WriteAllText(AbsoluteTemplatePath(), BuildZoneTemplate().Build());
        }

        /// <summary>Deletes the staged template and the import metadata Unity may have written for it.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            string absolutePath = AbsoluteTemplatePath();
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
            string metaPath = $"{absolutePath}.meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        /// <summary>Destroys the rig, the physics actor, and any detached object the test created.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_detachedObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_detachedObject);
            }
            _detachedObject = null;

            if (_actorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_actorObject);
            }
            _actorObject = null;

            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>Verifies that Unity's physics raises the entry that resolves a collision-mode trial.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_PhysicsSweepsTheActorIntoTheCollider_CollisionModeDelivers()
        {
            BuildRig(ZoneRigOptions.Collision());

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;

            Assert.IsTrue(PrivateAccess.GetField<bool>(_rig.StimulusZone, "_inZone"));
            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsTrue(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
            Assert.AreEqual("TestTrial", outcomes[0].trialName);
            Assert.IsFalse(_rig.StimulusZone.isActive);
        }

        /// <summary>Verifies that a physics-driven exit adds no second outcome to a collision-mode trial.</summary>
        [UnityTest]
        public IEnumerator OnTriggerExit_PhysicsSweepsTheActorOut_CollisionModeAddsNoSecondOutcome()
        {
            BuildRig(ZoneRigOptions.Collision());

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;
            yield return MoveActorTo(OutsideEveryZoneZ);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_rig.StimulusZone, "_inZone"));
            Assert.AreEqual(1, _rig.StimulusOutcomes().Count);
        }

        /// <summary>Verifies that an actor swept past a collider without overlapping it resolves nothing.</summary>
        [UnityTest]
        public IEnumerator Update_ActorNeverOverlapsACollider_PublishesNoOutcome()
        {
            BuildRig(ZoneRigOptions.Collision());

            yield return null;

            // Stops half a unit short of the collider's near face, then clears its far face by the same margin.
            // Discrete detection means the teleport between the two never sweeps the collider, so a zone that fired
            // here would be reporting an overlap that the geometry does not produce.
            yield return MoveActorTo(JustShortOfStimulusZoneZ);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_rig.StimulusZone, "_inZone"));
            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);

            yield return MoveActorTo(JustPastStimulusZoneZ);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_rig.StimulusZone, "_inZone"));
            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);
            Assert.IsTrue(_rig.StimulusZone.isActive);
        }

        /// <summary>Verifies that a physics-driven exit without an interaction reports the omitted outcome.</summary>
        [UnityTest]
        public IEnumerator OnTriggerExit_PhysicsExitWithoutAnInteraction_PublishesTheOmittedOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            BuildRig(options);

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;
            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);

            yield return MoveActorTo(OutsideEveryZoneZ);
            yield return null;

            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsFalse(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
        }

        /// <summary>Verifies that an interaction raised while physics reports the actor inside delivers.</summary>
        [UnityTest]
        public IEnumerator OnInteractionDetected_RaisedWhilePhysicsReportsTheActorInside_Delivers()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            BuildRig(options);

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;
            _rig.RaiseInteraction();

            yield return null;

            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsTrue(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
        }

        /// <summary>Verifies that reaching the guidance collider under physics delivers the guided outcome.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_PhysicsReachesTheGuidanceCollider_DeliversWithTheGuidanceCause()
        {
            BuildRig(ZoneRigOptions.Interaction());

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;
            Assert.IsFalse(_rig.GuidanceZone.inZone);
            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);

            yield return MoveActorTo(InsideGuidanceZoneZ);
            yield return null;

            Assert.IsTrue(_rig.GuidanceZone.inZone);
            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsTrue(outcomes[0].delivered);
            Assert.AreEqual("guidance", outcomes[0].cause);
        }

        /// <summary>Verifies that the guidance flag tracks the physics entry and exit of its own collider.</summary>
        [UnityTest]
        public IEnumerator OnTriggerExit_PhysicsLeavesTheGuidanceCollider_ClearsTheGuidanceFlag()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            BuildRig(options);

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            Assert.IsFalse(_rig.GuidanceZone.inZone);

            yield return MoveActorTo(InsideGuidanceZoneZ);
            Assert.IsTrue(_rig.GuidanceZone.inZone);

            yield return MoveActorTo(InsideStimulusZoneZ);
            Assert.IsFalse(_rig.GuidanceZone.inZone);

            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);
        }

        /// <summary>Verifies that occupying the zone for the real required duration meets the requirement.</summary>
        [UnityTest]
        public IEnumerator Update_ActorOccupiesForTheRequiredDuration_MeetsTheOccupancyRequirement()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return null;
            Assert.IsTrue(_rig.OccupancyZone.inZone);
            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);

            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return null;

            Assert.IsTrue(_rig.OccupancyZone.occupancyMet);
            Assert.GreaterOrEqual(_rig.OccupancyElapsedMilliseconds(), (long)ShortOccupancyMilliseconds);
        }

        /// <summary>Verifies that leaving before the real required duration leaves the requirement unmet.</summary>
        [UnityTest]
        public IEnumerator Update_ActorLeavesBeforeTheRequiredDuration_LeavesTheRequirementUnmet()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, LongOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return new WaitForSeconds(PartialOccupancyWaitSeconds);
            yield return MoveActorTo(OutsideEveryZoneZ);
            yield return null;

            Assert.IsFalse(_rig.OccupancyZone.inZone);
            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            long elapsedAtExit = _rig.OccupancyElapsedMilliseconds();
            Assert.GreaterOrEqual(elapsedAtExit, PartialOccupancyFloorMilliseconds);
            Assert.Less(elapsedAtExit, (long)LongOccupancyMilliseconds);

            // The stopwatch stopped on the exit, so no amount of further wall-clock time can meet the requirement.
            yield return new WaitForSeconds(PartialOccupancyWaitSeconds);
            yield return null;

            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            Assert.AreEqual(elapsedAtExit, _rig.OccupancyElapsedMilliseconds());
        }

        /// <summary>Verifies that a met occupancy followed by the boundary crossing arms the delivery.</summary>
        [UnityTest]
        public IEnumerator Update_OccupancyMetThenBoundaryCrossed_ArmModeDeliversTheStimulus()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return null;
            Assert.IsTrue(_rig.OccupancyZone.occupancyMet);

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;

            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsTrue(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
            Assert.IsFalse(_rig.OccupancyGuidanceZone.BrakeTriggered);
        }

        /// <summary>Verifies that an unmet occupancy followed by the boundary crossing omits the stimulus.</summary>
        [UnityTest]
        public IEnumerator Update_OccupancyUnmetThenBoundaryCrossed_ArmModeOmitsTheStimulus()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, LongOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;

            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsFalse(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
        }

        /// <summary>Verifies that a met occupancy followed by the boundary crossing disarms the delivery.</summary>
        [UnityTest]
        public IEnumerator Update_OccupancyMetThenBoundaryCrossed_DisarmModeOmitsTheStimulus()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return null;
            Assert.IsTrue(_rig.OccupancyZone.occupancyMet);

            yield return MoveActorTo(InsideStimulusZoneZ);
            yield return null;

            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsFalse(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
        }

        /// <summary>Verifies that the trigger mode delivers on real elapsed occupancy alone.</summary>
        [UnityTest]
        public IEnumerator Update_OccupancyMet_TriggerModeDeliversWithoutABoundaryCrossing()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return null;
            Assert.AreEqual(0, _rig.StimulusOutcomes().Count);

            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return null;

            Assert.IsFalse(PrivateAccess.GetField<bool>(_rig.StimulusZone, "_inZone"));
            List<StimulusTriggerZone.StimulusMessage> outcomes = _rig.StimulusOutcomes();
            Assert.AreEqual(1, outcomes.Count);
            Assert.IsTrue(outcomes[0].delivered);
            Assert.AreEqual("behavior", outcomes[0].cause);
        }

        /// <summary>Verifies that the brake delay reports the duration the real stopwatch has left to run.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_GuidanceReachedMidOccupancy_PublishesTheRemainingDuration()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, LongOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return new WaitForSeconds(PartialOccupancyWaitSeconds);
            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            yield return null;

            Assert.IsTrue(_rig.OccupancyGuidanceZone.BrakeTriggered);
            Assert.AreEqual(1, _rig.Mqtt.CountOn(MQTTTopics.Delay));

            OccupancyGuidanceZone.TriggerDelayMessage delay =
                _rig.Mqtt.LastMessageOn<OccupancyGuidanceZone.TriggerDelayMessage>(MQTTTopics.Delay);
            long remaining = delay.delayMilliseconds;

            // The stopwatch keeps running after the brake fired, so the reading taken now is an upper bound on the
            // elapsed time the zone subtracted, which makes the published remainder its matching lower bound.
            long elapsedAfterTrigger = _rig.OccupancyElapsedMilliseconds();
            Assert.GreaterOrEqual(remaining, (long)LongOccupancyMilliseconds - elapsedAfterTrigger);
            Assert.LessOrEqual(remaining, (long)LongOccupancyMilliseconds - PartialOccupancyFloorMilliseconds);
            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
        }

        /// <summary>Verifies that an occupancy overrun clamps the published brake delay to zero.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_OccupancyOverrun_ClampsTheRemainingDurationToZero()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);

            // Suspending the occupancy zone leaves its stopwatch running while Update stops latching the
            // requirement, which is the only way real elapsed time can overrun a duration that is still unmet.
            _rig.OccupancyZone.isActive = false;

            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            yield return null;

            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            Assert.Greater(_rig.OccupancyElapsedMilliseconds(), (long)ShortOccupancyMilliseconds);
            Assert.AreEqual(1, _rig.Mqtt.CountOn(MQTTTopics.Delay));

            OccupancyGuidanceZone.TriggerDelayMessage delay =
                _rig.Mqtt.LastMessageOn<OccupancyGuidanceZone.TriggerDelayMessage>(MQTTTopics.Delay);
            Assert.AreEqual(0L, (long)delay.delayMilliseconds);
        }

        /// <summary>Verifies that re-entering the guidance collider in one lap publishes no second delay.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_GuidanceReachedTwiceInOneLap_PublishesExactlyOneDelay()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, LongOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            Assert.AreEqual(1, _rig.Mqtt.CountOn(MQTTTopics.Delay));

            yield return MoveActorTo(InsideOccupancyZoneZ);

            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            yield return null;

            Assert.IsTrue(_rig.OccupancyGuidanceZone.BrakeTriggered);
            Assert.AreEqual(1, _rig.Mqtt.CountOn(MQTTTopics.Delay));
        }

        /// <summary>Verifies that a required wait suppresses the brake delay the guidance zone would send.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_WaitRequired_PublishesNoBrakeDelay()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, LongOccupancyMilliseconds);
            options.requireWait = true;
            BuildRig(options);

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            yield return null;

            Assert.IsFalse(_rig.OccupancyGuidanceZone.BrakeTriggered);
            Assert.AreEqual(0, _rig.Mqtt.CountOn(MQTTTopics.Delay));
        }

        /// <summary>Verifies that an already met occupancy suppresses the brake delay.</summary>
        [UnityTest]
        public IEnumerator OnTriggerEnter_OccupancyAlreadyMet_PublishesNoBrakeDelay()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            yield return null;

            yield return MoveActorTo(InsideOccupancyZoneZ);
            yield return new WaitForSeconds(OccupancyMetWaitSeconds);
            yield return null;
            Assert.IsTrue(_rig.OccupancyZone.occupancyMet);

            yield return MoveActorTo(InsideOccupancyGuidanceZoneZ);
            yield return null;

            Assert.IsFalse(_rig.OccupancyGuidanceZone.BrakeTriggered);
            Assert.AreEqual(0, _rig.Mqtt.CountOn(MQTTTopics.Delay));
        }

        /// <summary>Verifies that the occupancy stopwatch is stopped and zeroed through the first Update.</summary>
        [UnityTest]
        public IEnumerator Start_UnityDrivenLifecycle_KeepsTheOccupancyTimerStoppedThroughTheFirstUpdate()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));
            Stopwatch occupancyTimer = PrivateAccess.GetField<Stopwatch>(_rig.OccupancyZone, "_occupancyTimer");
            Assert.IsNotNull(occupancyTimer);

            yield return null;

            Assert.IsFalse(occupancyTimer.IsRunning);
            Assert.AreEqual(0L, _rig.OccupancyElapsedMilliseconds());
            Assert.IsTrue(_rig.OccupancyZone.isActive);
            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            Assert.IsFalse(_rig.OccupancyZone.inZone);
        }

        /// <summary>Verifies that a per-lap reset arriving before Unity runs Start restores the defaults.</summary>
        [UnityTest]
        public IEnumerator ResetState_BeforeUnityRunsStart_RestoresThePerLapDefaults()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            // Diverges each field from its default, so the assertions observe the reset restoring them rather than
            // restating the values the rig already left in place.
            _rig.OccupancyZone.isActive = false;
            _rig.OccupancyZone.occupancyMet = true;
            _rig.OccupancyZone.inZone = true;

            Assert.DoesNotThrow(() => _rig.OccupancyZone.ResetState());

            Assert.IsTrue(_rig.OccupancyZone.isActive);
            Assert.IsFalse(_rig.OccupancyZone.occupancyMet);
            Assert.IsFalse(_rig.OccupancyZone.inZone);

            yield return null;

            Assert.AreEqual(0L, _rig.OccupancyElapsedMilliseconds());
        }

        /// <summary>Verifies that Unity's own Start arms a stimulus zone whose serialized state was inactive.</summary>
        [UnityTest]
        public IEnumerator Start_SerializedInactiveStimulusZone_IsArmedByTheFirstFrame()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.showBoundary = true;
            BuildRig(options);
            _rig.StimulusZone.isActive = false;
            _rig.BoundaryRenderer.enabled = false;

            yield return null;

            Assert.IsTrue(_rig.StimulusZone.isActive);
            Assert.IsTrue(_rig.BoundaryRenderer.enabled);
            Assert.AreSame(
                _rig.BoundaryRenderer,
                PrivateAccess.GetField<MeshRenderer>(_rig.StimulusZone, "_boundaryRenderer")
            );
        }

        /// <summary>Verifies that Unity's own Start resolves the child zones out of the real hierarchy.</summary>
        [UnityTest]
        public IEnumerator Start_UnityDrivenLifecycle_ResolvesTheChildZonesFromTheHierarchy()
        {
            BuildRig(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, ShortOccupancyMilliseconds));

            yield return null;

            Assert.AreSame(
                _rig.OccupancyZone,
                PrivateAccess.GetField<OccupancyZone>(_rig.StimulusZone, "_occupancyZone")
            );
            Assert.AreSame(
                _rig.OccupancyGuidanceZone,
                PrivateAccess.GetField<OccupancyGuidanceZone>(_rig.StimulusZone, "_occupancyGuidanceZone")
            );
            Assert.IsNull(PrivateAccess.GetField<GuidanceZone>(_rig.StimulusZone, "_guidanceZone"));
            Assert.AreSame(_rig.Task, PrivateAccess.GetField<Task>(_rig.StimulusZone, "_task"));
        }

        /// <summary>Verifies that a guidance zone Unity starts without a parent occupancy zone disables itself.
        /// </summary>
        [UnityTest]
        public IEnumerator Start_OccupancyGuidanceZoneWithoutAParentOccupancyZone_DisablesItself()
        {
            BuildRig(ZoneRigOptions.Collision());
            _detachedObject = new GameObject("DetachedOccupancyGuidanceZone");
            OccupancyGuidanceZone detachedZone = _detachedObject.AddComponent<OccupancyGuidanceZone>();
            LogAssert.Expect(LogType.Error, new Regex("No parent OccupancyZone found"));

            yield return null;

            Assert.IsFalse(detachedZone.enabled);
            Assert.AreEqual(0, _rig.Mqtt.CountOn(MQTTTopics.Delay));
        }

        /// <summary>Builds the single-trial template every rig's task loads.</summary>
        /// <returns>A builder whose rendered document carries one cue and the single trial that names it.</returns>
        private static TemplateYaml BuildZoneTemplate()
        {
            TemplateYaml template = new TemplateYaml();
            CueYaml cue = CueYaml.Named("A", 1);
            cue.lengthCm = 100f;
            cue.texture = StagedTextureName;
            template.cues.Add(cue);
            template.trials.Add(TrialYaml.Named("Only", "A"));
            template.vrEnvironment.corridorSpacingCm = 500f;
            template.vrEnvironment.segmentsPerCorridor = 1;
            template.vrEnvironment.cmPerUnityUnit = 10f;
            return template;
        }

        /// <summary>Returns the absolute path the staged template occupies.</summary>
        /// <remarks>
        /// Task.Start resolves its template as Path.Combine(Application.dataPath, configPath), so the staged template
        /// lives under the project's own Configurations directory.
        /// </remarks>
        /// <returns>The absolute path of the staged template file.</returns>
        private static string AbsoluteTemplatePath()
        {
            return Path.Combine(
                Application.dataPath,
                "InfiniteCorridorTask",
                "Configurations",
                $"{ZoneTemplateName}.yaml"
            );
        }

        /// <summary>Resizes and repositions a zone's trigger collider so the actor can enter it alone.</summary>
        /// <param name="zone">The zone whose collider is placed.</param>
        /// <param name="centerZ">The world z position of the collider's center.</param>
        /// <param name="depth">The collider's extent along z.</param>
        private static void PlaceZone(Component zone, float centerZ, float depth)
        {
            zone.transform.position = new Vector3(0f, 0f, centerZ);
            Assert.IsTrue(
                zone.TryGetComponent(out BoxCollider collider),
                $"Unable to place zone '{zone.name}'. The zone must carry a BoxCollider, but none is attached."
            );
            collider.center = Vector3.zero;
            collider.size = new Vector3(ZoneCrossSection, ZoneCrossSection, depth);
        }

        /// <summary>
        /// Creates the zone hierarchy, points its task at the staged template, spreads the colliders along z, and
        /// creates the Rigidbody-carrying actor outside every one of them.
        /// </summary>
        /// <remarks>
        /// The task never moves the actor, because its actor reference stays null and Update returns on that check.
        /// </remarks>
        /// <param name="options">The composition and initial field values of the rig.</param>
        private void BuildRig(ZoneRigOptions options)
        {
            _rig = ZoneRig.Create(options);
            _rig.Task.configPath = $"{ConfigurationsDirectory}/{ZoneTemplateName}.yaml";
            _rig.Task.trackLength = ZoneTrackLength;
            _rig.Task.trackSeed = ZoneSeed;
            _rig.Task.actor = null;

            PlaceZone(_rig.StimulusZone, StimulusZoneCenterZ, StimulusZoneDepth);
            if (_rig.GuidanceZone != null)
            {
                PlaceZone(_rig.GuidanceZone, GuidanceZoneCenterZ, GuidanceZoneDepth);
            }
            if (_rig.OccupancyZone != null)
            {
                PlaceZone(_rig.OccupancyZone, OccupancyZoneCenterZ, OccupancyZoneDepth);
            }
            if (_rig.OccupancyGuidanceZone != null)
            {
                PlaceZone(_rig.OccupancyGuidanceZone, OccupancyGuidanceZoneCenterZ, OccupancyGuidanceZoneDepth);
            }

            _actorObject = new GameObject("PhysicsActor");
            _actorObject.transform.position = new Vector3(0f, 0f, OutsideEveryZoneZ);
            BoxCollider actorCollider = _actorObject.AddComponent<BoxCollider>();
            actorCollider.size = Vector3.one;
            Rigidbody actorBody = _actorObject.AddComponent<Rigidbody>();
            actorBody.isKinematic = true;
            actorBody.useGravity = false;

            // Discrete detection keeps a teleport from sweeping through the colliders that lie between the departure
            // and the destination, so a move that skips a zone genuinely never enters it.
            actorBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            actorBody.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Sweeps the actor to a world z position and lets Unity's physics raise its trigger events.</summary>
        /// <remarks>
        /// The transform sync makes the moved collider visible to the next simulation step, and waiting on two fixed
        /// updates guarantees that step and its trigger callbacks ran before the caller resumes.
        /// </remarks>
        /// <param name="z">The world z position the actor moves to.</param>
        /// <returns>The enumerator the calling test yields on.</returns>
        private IEnumerator MoveActorTo(float z)
        {
            _actorObject.transform.position = new Vector3(0f, 0f, z);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }
    }
}
