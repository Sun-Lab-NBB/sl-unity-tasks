/// <summary>
/// Verifies the serialized contract of the TriggerMode enumeration.
/// </summary>
using System;
using NUnit.Framework;
using SL.Tasks;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the serialized contract of the TriggerMode enumeration.</summary>
    /// <remarks>
    /// CreateTask writes the ordinal of each member into the generated prefabs, so a reordered or renamed member
    /// silently repoints every already-generated zone at a different trigger mechanism. Each ordinal is therefore
    /// pinned individually rather than through the enumeration order alone.
    /// </remarks>
    [TestFixture]
    public class TriggerModeTests
    {
        /// <summary>Verifies that the Interaction member serializes as ordinal zero.</summary>
        [Test]
        public void Interaction_SerializedOrdinal_IsZero()
        {
            Assert.AreEqual(0, (int)TriggerMode.Interaction);
        }

        /// <summary>Verifies that the Collision member serializes as ordinal one.</summary>
        [Test]
        public void Collision_SerializedOrdinal_IsOne()
        {
            Assert.AreEqual(1, (int)TriggerMode.Collision);
        }

        /// <summary>Verifies that the OccupancyDisarm member serializes as ordinal two.</summary>
        [Test]
        public void OccupancyDisarm_SerializedOrdinal_IsTwo()
        {
            Assert.AreEqual(2, (int)TriggerMode.OccupancyDisarm);
        }

        /// <summary>Verifies that the OccupancyArm member serializes as ordinal three.</summary>
        [Test]
        public void OccupancyArm_SerializedOrdinal_IsThree()
        {
            Assert.AreEqual(3, (int)TriggerMode.OccupancyArm);
        }

        /// <summary>Verifies that the OccupancyTrigger member serializes as ordinal four.</summary>
        [Test]
        public void OccupancyTrigger_SerializedOrdinal_IsFour()
        {
            Assert.AreEqual(4, (int)TriggerMode.OccupancyTrigger);
        }

        /// <summary>Verifies that the enumeration declares exactly five members.</summary>
        [Test]
        public void GetValues_TriggerModeEnumeration_DeclaresExactlyFiveMembers()
        {
            Assert.AreEqual(5, Enum.GetValues(typeof(TriggerMode)).Length);
        }

        /// <summary>Verifies that the member names match the declared order the ordinals encode.</summary>
        [Test]
        public void GetNames_TriggerModeEnumeration_MatchesTheDeclaredOrder()
        {
            string[] expected = new string[]
            {
                "Interaction",
                "Collision",
                "OccupancyDisarm",
                "OccupancyArm",
                "OccupancyTrigger",
            };

            CollectionAssert.AreEqual(expected, Enum.GetNames(typeof(TriggerMode)));
        }

        /// <summary>Verifies that an unconfigured trigger mode field defaults to Interaction.</summary>
        [Test]
        public void Default_UnassignedTriggerMode_IsInteraction()
        {
            Assert.AreEqual(TriggerMode.Interaction, default(TriggerMode));
        }

        /// <summary>Verifies that the enumeration serializes through the 32-bit integer backing store.</summary>
        [Test]
        public void GetUnderlyingType_TriggerModeEnumeration_IsInt32()
        {
            Assert.AreEqual(typeof(int), Enum.GetUnderlyingType(typeof(TriggerMode)));
        }

        /// <summary>Verifies that the declared ordinal range spans zero through four inclusive.</summary>
        [Test]
        public void IsDefined_OrdinalsAroundTheDeclaredRange_AcceptsZeroThroughFourOnly()
        {
            Assert.IsFalse(Enum.IsDefined(typeof(TriggerMode), -1));
            Assert.IsTrue(Enum.IsDefined(typeof(TriggerMode), 0));
            Assert.IsTrue(Enum.IsDefined(typeof(TriggerMode), 4));
            Assert.IsFalse(Enum.IsDefined(typeof(TriggerMode), 5));
        }
    }
}
