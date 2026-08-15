/// <summary>Verifies the behavior of the StimulusTriggerZone class.</summary>
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the StimulusTriggerZone class.</summary>
    /// <remarks>
    /// The fixture walks the full trigger-mode dispatch matrix, both sides of every guard in the interaction,
    /// collision, and occupancy handlers, and the per-lap contract the zone shares with Task. That contract is exactly
    /// one StimulusMessage per resolved trial, a cause of "behavior" or "guidance", and a verbatim trial name. The two
    /// closing tests pin the IResettable implementer set, because Task discovers resettable zones by concrete type and
    /// an unregistered implementer would silently never reset.
    /// </remarks>
    [TestFixture]
    public class StimulusTriggerZoneTests
    {
        /// <summary>Verifies that collision mode delivers the stimulus on the first frame inside the zone.</summary>
        [Test]
        public void Update_CollisionModeInsideZone_PublishesDeliveredBehaviorOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
                Assert.AreEqual("TestTrial", outcomes[0].trialName);
            }
        }

        /// <summary>Verifies that a resolved zone publishes no second outcome on later frames.</summary>
        [Test]
        public void Update_CollisionModeAfterResolution_PublishesExactlyOneOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();
                rig.Tick();
                rig.Tick();

                Assert.AreEqual(1, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that interaction mode with the requirement enabled ignores a bare zone entry.</summary>
        [Test]
        public void Update_InteractionRequiredWithoutInteraction_PublishesNoOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that interaction mode delivers once the animal engages the sensor in the zone.</summary>
        [Test]
        public void Update_InteractionRequiredWithInteraction_PublishesDeliveredBehaviorOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.RaiseInteraction();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that guidance mode delivers when the animal reaches the guidance child zone.</summary>
        [Test]
        public void Update_GuidanceEnabledAndGuidanceZoneReached_PublishesGuidanceCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("guidance", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that leaving an interaction zone without interacting reports an omitted outcome.</summary>
        [Test]
        public void OnTriggerExit_InteractionRequiredWithoutInteraction_PublishesOmittedOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();
                rig.ExitStimulusZone();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsFalse(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that ResetState re-arms the zone and restores the configured boundary visibility.
        /// </summary>
        [Test]
        public void ResetState_AfterResolution_ReArmsZoneAndRestoresBoundary()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.showBoundary = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();
                Assert.IsFalse(rig.StimulusZone.isActive);
                Assert.IsFalse(rig.BoundaryRenderer.enabled);

                rig.StimulusZone.ResetState();

                Assert.IsTrue(rig.StimulusZone.isActive);
                Assert.IsTrue(rig.BoundaryRenderer.enabled);
            }
        }

        /// <summary>Verifies that Start arms the zone even when the serialized isActive value is false.</summary>
        [Test]
        public void Start_SerializedInactiveZone_ArmsZoneForTheFirstLap()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StimulusZone.isActive = false;

                rig.StartComponents();

                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that Start reports an error and disables the zone when the scene carries no Task.
        /// </summary>
        [Test]
        public void Start_NoTaskInScene_LogsAnErrorAndDisablesTheZone()
        {
            GameObject zoneObject = new GameObject("OrphanStimulusTriggerZone");
            try
            {
                StimulusTriggerZone zone = zoneObject.AddComponent<StimulusTriggerZone>();
                LogAssert.Expect(LogType.Error, new Regex("No Task found in scene"));

                PrivateAccess.Invoke(zone, "Start");

                Assert.IsFalse(zone.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
            }
        }

        /// <summary>Verifies that a zone lacking a boundary renderer still resolves its trial.</summary>
        [Test]
        public void Update_ZoneWithoutBoundaryRenderer_ResolvesTheTrialWithoutTouchingARenderer()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.includeBoundaryRenderer = false;
            options.showBoundary = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsNull(rig.BoundaryRenderer);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
                Assert.IsFalse(rig.StimulusZone.isActive);
            }
        }

        /// <summary>
        /// Verifies that a freshly attached zone carries the field-initializer defaults its Start and CreateTask
        /// both layer on top of.
        /// </summary>
        [Test]
        public void SerializedDefaults_FreshlyAttachedZone_AreArmedInteractionModeWithNoTrialName()
        {
            GameObject zoneObject = new GameObject("DefaultStimulusTriggerZone");
            try
            {
                StimulusTriggerZone zone = zoneObject.AddComponent<StimulusTriggerZone>();

                Assert.AreEqual(TriggerMode.Interaction, zone.triggerMode);
                Assert.IsFalse(zone.showBoundary);
                Assert.IsTrue(zone.isActive);
                Assert.AreEqual(string.Empty, zone.trialName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
            }
        }

        /// <summary>Verifies that Update returns immediately while the zone is inactive.</summary>
        [Test]
        public void Update_InactiveZone_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.StimulusZone.isActive = false;
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that an unmapped trigger mode resolves nothing and leaves the zone armed.</summary>
        [Test]
        public void Update_UnmappedTriggerMode_LeavesTheZoneArmedAndPublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.StimulusZone.triggerMode = (TriggerMode)99;
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that collision mode publishes nothing before the animal crosses the boundary.</summary>
        [Test]
        public void Update_CollisionModeBeforeEntry_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that a required interaction recorded outside the zone resolves nothing.</summary>
        [Test]
        public void Update_InteractionRequiredWithInteractionOutsideZone_PublishesNoOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                PrivateAccess.SetField(rig.StimulusZone, "_interactionDetectedInZone", true);
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that guidance mode still credits the animal's own interaction in the zone.</summary>
        [Test]
        public void Update_GuidanceEnabledWithInteractionInZone_PublishesBehaviorCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.RaiseInteraction();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that an interaction outranks the guidance fallback when both hold on one frame.</summary>
        [Test]
        public void Update_GuidanceEnabledWithInteractionAndGuidanceEntry_PublishesBehaviorCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.RaiseInteraction();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that guidance mode resolves nothing while neither delivery path holds.</summary>
        [Test]
        public void Update_GuidanceEnabledWithoutInteractionOrGuidanceEntry_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the guidance fallback fires from the guidance child zone alone.</summary>
        [Test]
        public void Update_GuidanceEnabledAndOnlyGuidanceZoneEntered_PublishesGuidanceCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterGuidanceZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("guidance", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that guidance mode without a guidance child zone fires on bare zone entry.</summary>
        [Test]
        public void Update_GuidanceEnabledWithoutGuidanceZone_BareEntryPublishesGuidanceCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction(withGuidanceZone: false)))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("guidance", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that guidance mode without a guidance child zone waits for the zone entry.</summary>
        [Test]
        public void Update_GuidanceEnabledWithoutGuidanceZoneBeforeEntry_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction(withGuidanceZone: false)))
            {
                rig.StartComponents();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that guidance mode publishes one outcome no matter how many frames elapse.</summary>
        [Test]
        public void Update_GuidanceEnabledAcrossManyFrames_PublishesExactlyOneOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.Tick();
                rig.Tick();
                rig.Tick();
                rig.Tick();

                Assert.AreEqual(1, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that an occupancy mode without an occupancy child zone resolves nothing.</summary>
        [Test]
        public void Update_OccupancyModeWithoutOccupancyZone_PublishesNoOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.triggerMode = TriggerMode.OccupancyDisarm;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the disarm mode delivers on the crossing while occupancy is unmet.</summary>
        [Test]
        public void Update_OccupancyDisarmOccupancyNotMet_PublishesDeliveredBehaviorOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm)))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the disarm mode omits the stimulus once occupancy has been met.</summary>
        [Test]
        public void Update_OccupancyDisarmOccupancyMet_PublishesOmittedOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsFalse(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the disarm mode waits for the boundary crossing before it resolves.</summary>
        [Test]
        public void Update_OccupancyDisarmOutsideZone_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm)))
            {
                rig.StartComponents();
                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the arm mode delivers on the crossing once occupancy has been met.</summary>
        [Test]
        public void Update_OccupancyArmOccupancyMet_PublishesDeliveredBehaviorOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the arm mode omits the stimulus while occupancy is unmet.</summary>
        [Test]
        public void Update_OccupancyArmOccupancyNotMet_PublishesOmittedOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsFalse(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the arm mode waits for the boundary crossing before it resolves.</summary>
        [Test]
        public void Update_OccupancyArmOutsideZone_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.Tick();

                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the trigger mode delivers on met occupancy without a boundary crossing.</summary>
        [Test]
        public void Update_OccupancyTriggerOccupancyMet_PublishesDeliveredOutcomeOutsideTheZone()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.Tick();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_inZone"));
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the trigger mode ignores the boundary crossing while occupancy is unmet.</summary>
        [Test]
        public void Update_OccupancyTriggerOccupancyNotMetInsideZone_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger)))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the trigger mode publishes one outcome no matter how many frames elapse.</summary>
        [Test]
        public void Update_OccupancyTriggerAcrossManyFrames_PublishesExactlyOneOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.Tick();
                rig.Tick();
                rig.Tick();

                Assert.AreEqual(1, rig.StimulusOutcomes().Count);
            }
        }

        /// <summary>Verifies that an occupancy outcome reports guidance once the brake fired this lap.</summary>
        [Test]
        public void Update_OccupancyArmWithBrakeGuidance_PublishesGuidanceCause()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterOccupancyGuidanceZone();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsTrue(rig.OccupancyGuidanceZone.BrakeTriggered);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("guidance", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that an occupancy outcome reports behavior while the brake stayed silent.</summary>
        [Test]
        public void Update_OccupancyArmWithWaitRequiredGuidanceEntry_PublishesBehaviorCause()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f);
            options.requireWait = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterOccupancyGuidanceZone();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that an occupancy outcome reports behavior with no occupancy guidance zone.</summary>
        [Test]
        public void Update_OccupancyArmWithoutOccupancyGuidanceZone_PublishesBehaviorCause()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f);
            options.includeOccupancyGuidanceZone = false;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.IsNull(rig.OccupancyGuidanceZone);
                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that an interaction recorded after the last Update still delivers on the exit.</summary>
        [Test]
        public void OnTriggerExit_InteractionRecordedAfterTheLastUpdate_PublishesDeliveredOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();
                rig.RaiseInteraction();
                rig.ExitStimulusZone();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
            }
        }

        /// <summary>Verifies that the exit of an already resolved interaction trial publishes nothing.</summary>
        [Test]
        public void OnTriggerExit_AfterTheTrialResolved_PublishesNoSecondOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.RaiseInteraction();
                rig.Tick();
                rig.ExitStimulusZone();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsTrue(outcomes[0].delivered);
            }
        }

        /// <summary>Verifies that a collision-mode exit clears the in-zone flag without publishing.</summary>
        [Test]
        public void OnTriggerExit_CollisionMode_ClearsTheInZoneFlagWithoutPublishing()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.ExitStimulusZone();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_inZone"));
                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that an occupancy-mode exit resolves nothing on the outward crossing.</summary>
        [Test]
        public void OnTriggerExit_OccupancyMode_PublishesNoOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterStimulusZone();
                rig.ExitStimulusZone();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that a guidance-mode exit before any Update still reports an omitted outcome.</summary>
        [Test]
        public void OnTriggerExit_GuidanceEnabledWithoutInteraction_PublishesOmittedBehaviorOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();

                rig.ExitStimulusZone();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsFalse(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
                Assert.IsFalse(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that an interaction-mode exit with no matching entry still resolves the trial.</summary>
        [Test]
        public void OnTriggerExit_WithoutAMatchingEntry_PublishesOmittedBehaviorOutcome()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();

                rig.ExitStimulusZone();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.IsFalse(outcomes[0].delivered);
                Assert.AreEqual("behavior", outcomes[0].cause);
                Assert.IsFalse(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that entering the trigger collider marks the actor as inside the zone.</summary>
        [Test]
        public void OnTriggerEnter_ActorEntersTheCollider_MarksTheActorInsideTheZone()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();

                rig.EnterStimulusZone();

                Assert.IsTrue(PrivateAccess.GetField<bool>(rig.StimulusZone, "_inZone"));
            }
        }

        /// <summary>Verifies that an interaction outside the zone is not recorded.</summary>
        [Test]
        public void OnInteractionDetected_OutsideTheZone_DoesNotRecordTheInteraction()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();

                rig.RaiseInteraction();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that an interaction after the trial resolved is not recorded.</summary>
        [Test]
        public void OnInteractionDetected_ResolvedZone_DoesNotRecordTheInteraction()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction(withGuidanceZone: false)))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();
                Assert.IsFalse(rig.StimulusZone.isActive);

                rig.RaiseInteraction();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that a collision-mode zone ignores an interaction raised inside it.</summary>
        [Test]
        public void OnInteractionDetected_CollisionMode_DoesNotRecordTheInteraction()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();

                rig.RaiseInteraction();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that an interaction inside an armed interaction zone is recorded.</summary>
        [Test]
        public void OnInteractionDetected_ArmedInteractionZone_RecordsTheInteraction()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();

                rig.RaiseInteraction();

                Assert.IsTrue(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that resolving a trial hides the boundary and clears the interaction flag.</summary>
        [Test]
        public void TriggerStimulus_OnResolution_HidesTheBoundaryAndClearsTheInteractionFlag()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            options.showBoundary = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.RaiseInteraction();
                Assert.IsTrue(rig.BoundaryRenderer.enabled);

                rig.Tick();

                Assert.IsFalse(rig.BoundaryRenderer.enabled);
                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
                Assert.IsFalse(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that the published payload carries the three stimulus contract fields.</summary>
        [Test]
        public void TriggerStimulus_DeliveredOutcome_PublishesTheContractPayloadOnTheStimulusTopic()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.trialName = "Trial_Alpha";
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(1, rig.Mqtt.CountOn(MQTTTopics.Stimulus));
                string payload = rig.Mqtt.LastPayloadOn(MQTTTopics.Stimulus);
                StringAssert.Contains("\"trialName\":\"Trial_Alpha\"", payload);
                StringAssert.Contains("\"delivered\":true", payload);
                StringAssert.Contains("\"cause\":\"behavior\"", payload);
            }
        }

        /// <summary>Verifies that an omitted outcome echoes the trial name and reports delivered false.</summary>
        [Test]
        public void TriggerStimulus_OmittedOutcome_EchoesTheTrialNameWithDeliveredFalse()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm, 0f);
            options.trialName = "Occupancy_Trial_02";
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.EnterStimulusZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(1, outcomes.Count);
                Assert.AreEqual("Occupancy_Trial_02", outcomes[0].trialName);
                StringAssert.Contains("\"delivered\":false", rig.Mqtt.LastPayloadOn(MQTTTopics.Stimulus));
            }
        }

        /// <summary>Verifies that ResetState clears the recorded interaction and the in-zone flag.</summary>
        [Test]
        public void ResetState_AfterAnInteractionInTheZone_ClearsTheInteractionAndInZoneFlags()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.RaiseInteraction();

                rig.StimulusZone.ResetState();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_inZone"));
                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that ResetState re-hides the boundary of a zone configured to keep it hidden.</summary>
        [Test]
        public void ResetState_BoundaryConfiguredHidden_LeavesTheRendererDisabled()
        {
            ZoneRigOptions options = ZoneRigOptions.Collision();
            options.showBoundary = false;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.BoundaryRenderer.enabled = true;

                rig.StimulusZone.ResetState();

                Assert.IsFalse(rig.BoundaryRenderer.enabled);
            }
        }

        /// <summary>Verifies that a re-armed zone resolves a second trial on the following lap.</summary>
        [Test]
        public void ResetState_FollowingLap_PublishesASecondOutcome()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.Tick();

                rig.StimulusZone.ResetState();
                rig.EnterStimulusZone();
                rig.Tick();

                List<StimulusTriggerZone.StimulusMessage> outcomes = rig.StimulusOutcomes();
                Assert.AreEqual(2, outcomes.Count);
                Assert.IsTrue(outcomes[1].delivered);
                Assert.AreEqual("behavior", outcomes[1].cause);
            }
        }

        /// <summary>Verifies that OnDestroy detaches the interaction listener the zone registered.</summary>
        [Test]
        public void OnDestroy_AfterStart_StopsRecordingInteractions()
        {
            ZoneRigOptions options = ZoneRigOptions.Interaction();
            options.requireInteraction = true;
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                PrivateAccess.Invoke(rig.StimulusZone, "OnDestroy");

                rig.RaiseInteraction();

                Assert.IsFalse(PrivateAccess.GetField<bool>(rig.StimulusZone, "_interactionDetectedInZone"));
            }
        }

        /// <summary>Verifies that OnDestroy tolerates a zone whose Start never created its channels.</summary>
        [Test]
        public void OnDestroy_BeforeStart_DoesNotThrow()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Collision()))
            {
                Assert.DoesNotThrow(() => PrivateAccess.Invoke(rig.StimulusZone, "OnDestroy"));
            }
        }

        /// <summary>Verifies that the runtime assembly declares exactly the three expected IResettable types.</summary>
        [Test]
        public void IResettable_RuntimeAssembly_DeclaresExactlyTheRegisteredImplementers()
        {
            List<string> implementers = new List<string>();
            foreach (Type type in typeof(IResettable).Assembly.GetTypes())
            {
                if (typeof(IResettable).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                {
                    implementers.Add(type.Name);
                }
            }
            implementers.Sort(StringComparer.Ordinal);

            string[] expected = new string[] { "OccupancyGuidanceZone", "OccupancyZone", "StimulusTriggerZone" };
            CollectionAssert.AreEqual(expected, implementers);
        }

        /// <summary>Verifies that the Task reset enumeration discovers every zone implementing IResettable.</summary>
        [Test]
        public void FindResettableZones_OccupancyHierarchy_DiscoversEveryImplementer()
        {
            // Measures the scene before the rig exists, because Edit Mode fixtures share one scene and an absolute
            // count would make this test depend on what every other fixture left behind.
            IResettable[] baseline = (IResettable[])PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm, 0f)))
            {
                IResettable[] resettables = (IResettable[])
                    PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");

                Assert.AreEqual(baseline.Length + 3, resettables.Length);
                CollectionAssert.Contains(resettables, rig.StimulusZone);
                CollectionAssert.Contains(resettables, rig.OccupancyZone);
                CollectionAssert.Contains(resettables, rig.OccupancyGuidanceZone);
            }
        }
    }
}
