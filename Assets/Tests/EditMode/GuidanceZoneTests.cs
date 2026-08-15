/// <summary>
/// Verifies the behavior of the GuidanceZone class.
/// </summary>
using NUnit.Framework;
using SL.Tasks;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the GuidanceZone class.</summary>
    /// <remarks>
    /// The zone is a two-line state machine over inZone, so the fixture pins each transition and then pins the
    /// consequences that matter to the parent StimulusTriggerZone. The closing tests pin the per-lap reset, because a
    /// corridor teleport carries the actor out of the collider without an exit callback.
    /// </remarks>
    [TestFixture]
    public class GuidanceZoneTests
    {
        /// <summary>Verifies that a freshly attached guidance zone reports the actor as outside it.</summary>
        [Test]
        public void InZone_FreshlyAttachedZone_IsFalse()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                Assert.IsFalse(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that entering the guidance collider marks the zone as occupied.</summary>
        [Test]
        public void OnTriggerEnter_ActorEntersTheCollider_SetsInZoneTrue()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.EnterGuidanceZone();

                Assert.IsTrue(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that leaving the guidance collider marks the zone as unoccupied.</summary>
        [Test]
        public void OnTriggerExit_ActorLeavesTheCollider_SetsInZoneFalse()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.EnterGuidanceZone();

                rig.ExitGuidanceZone();

                Assert.IsFalse(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that an exit without a matching entry leaves the parent guidance fallback silent.
        /// </summary>
        [Test]
        public void OnTriggerExit_WithoutAPriorEntry_LeavesTheParentGuidanceFallbackSilent()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();

                rig.ExitGuidanceZone();
                rig.Tick();

                Assert.IsFalse(rig.GuidanceZone.inZone);
                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that repeated entries leave the zone occupied.</summary>
        [Test]
        public void OnTriggerEnter_RepeatedEntries_LeavesInZoneTrue()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.EnterGuidanceZone();

                rig.EnterGuidanceZone();

                Assert.IsTrue(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that a re-entry after an exit marks the zone as occupied again.</summary>
        [Test]
        public void OnTriggerEnter_AfterAnExit_SetsInZoneTrueAgain()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.EnterGuidanceZone();
                rig.ExitGuidanceZone();

                rig.EnterGuidanceZone();

                Assert.IsTrue(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that a guidance zone the actor has left no longer delivers the parent stimulus.
        /// </summary>
        [Test]
        public void InZone_ClearedAfterAnExit_StopsDrivingTheParentDelivery()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.ExitGuidanceZone();

                rig.Tick();

                Assert.AreEqual(0, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }

        /// <summary>Verifies that GuidanceZone carries the per-lap reset hook Task drives.</summary>
        [Test]
        public void GuidanceZone_TypeContract_ImplementsIResettable()
        {
            Assert.IsTrue(typeof(IResettable).IsAssignableFrom(typeof(GuidanceZone)));
        }

        /// <summary>Verifies that ResetState clears the occupancy flag a teleport left latched.</summary>
        [Test]
        public void ResetState_OccupiedZone_ClearsTheOccupancyFlag()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.EnterGuidanceZone();

                rig.GuidanceZone.ResetState();

                Assert.IsFalse(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that the corridor reset enumeration reaches the guidance zone and clears it.</summary>
        [Test]
        public void ResetState_CorridorAdvance_ClearsTheGuidanceZoneOccupancy()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.Tick();

                IResettable[] resettables = (IResettable[])
                    PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");
                CollectionAssert.Contains(resettables, rig.StimulusZone);
                CollectionAssert.Contains(resettables, rig.GuidanceZone);
                rig.StimulusZone.ResetState();
                rig.GuidanceZone.ResetState();

                Assert.IsTrue(rig.StimulusZone.isActive);
                Assert.IsFalse(rig.GuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that a reset guidance zone leaves the next lap's entry frame unresolved.</summary>
        /// <remarks>
        /// A latched flag would resolve the following lap on the frame the actor enters the parent trigger zone,
        /// before the animal ever reaches the guidance region again.
        /// </remarks>
        [Test]
        public void ResetState_FollowingLap_LeavesTheGuidanceFallbackSilentOnTheEntryFrame()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Interaction()))
            {
                rig.StartComponents();
                rig.EnterStimulusZone();
                rig.EnterGuidanceZone();
                rig.Tick();
                Assert.AreEqual(1, rig.StimulusOutcomes().Count);

                rig.StimulusZone.ResetState();
                rig.GuidanceZone.ResetState();
                rig.EnterStimulusZone();
                rig.Tick();

                Assert.AreEqual(1, rig.StimulusOutcomes().Count);
                Assert.IsTrue(rig.StimulusZone.isActive);
            }
        }
    }
}
