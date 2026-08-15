/// <summary>
/// Verifies the behavior of the GuidanceZone class.
///
/// The zone is a two-line state machine over inZone, so the fixture pins each transition and then pins the two
/// consequences that matter to the parent StimulusTriggerZone. The guidance fallback stays silent while the flag
/// is clear, and the flag survives a corridor advance because GuidanceZone implements no per-lap reset hook and
/// Task's reset enumeration therefore never reaches it.
/// </summary>
using NUnit.Framework;
using SL.Tasks;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the GuidanceZone class.</summary>
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

        /// <summary>Verifies that GuidanceZone carries no per-lap reset hook for Task to drive.</summary>
        [Test]
        public void GuidanceZone_TypeContract_DoesNotImplementIResettable()
        {
            Assert.IsFalse(typeof(IResettable).IsAssignableFrom(typeof(GuidanceZone)));
        }

        /// <summary>Verifies that the corridor reset enumeration skips the guidance zone and leaves it occupied.
        /// </summary>
        [Test]
        public void ResetState_CorridorAdvance_LeavesTheGuidanceZoneOccupancyLatched()
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
                CollectionAssert.DoesNotContain(resettables, rig.GuidanceZone);
                rig.StimulusZone.ResetState();

                Assert.IsTrue(rig.StimulusZone.isActive);
                Assert.IsTrue(rig.GuidanceZone.inZone);
            }
        }
    }
}
