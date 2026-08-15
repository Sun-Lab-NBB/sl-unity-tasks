/// <summary>
/// Verifies the behavior of the TrialStructure class.
/// </summary>
using System.Collections.Generic;
using NUnit.Framework;
using SL.Config;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the TrialStructure class.</summary>
    [TestFixture]
    public class TrialStructureTests
    {
        /// <summary>
        /// Verifies that a freshly constructed trial structure carries the documented field defaults.
        /// </summary>
        [Test]
        public void Constructor_Default_MatchesDocumentedFieldDefaults()
        {
            TrialStructure trial = new TrialStructure();

            Assert.IsNull(trial.cueSequence);
            Assert.AreEqual(0.0f, trial.stimulusTriggerZoneStartCm);
            Assert.AreEqual(0.0f, trial.stimulusTriggerZoneEndCm);
            Assert.AreEqual(0.0f, trial.stimulusLocationCm);
            Assert.IsFalse(trial.showStimulusCollisionBoundary);
            Assert.IsNull(trial.triggerType);
            Assert.IsFalse(trial.occupancyDurationMs.HasValue);
            Assert.IsNull(trial.transitions);
        }

        /// <summary>Verifies that HasTransitions reports false for the default null transition map.</summary>
        [Test]
        public void HasTransitions_NullTransitions_ReturnsFalse()
        {
            TrialStructure trial = new TrialStructure { transitions = null };

            Assert.IsFalse(trial.HasTransitions);
        }

        /// <summary>Verifies that HasTransitions reports false for an allocated but empty transition map.</summary>
        [Test]
        public void HasTransitions_EmptyTransitions_ReturnsFalse()
        {
            TrialStructure trial = new TrialStructure { transitions = new Dictionary<string, float>() };

            Assert.IsFalse(trial.HasTransitions);
        }

        /// <summary>Verifies that HasTransitions reports true for a single-entry transition map.</summary>
        [Test]
        public void HasTransitions_SingleEntry_ReturnsTrue()
        {
            TrialStructure trial = new TrialStructure
            {
                transitions = new Dictionary<string, float> { { "AB", 1.0f } },
            };

            Assert.IsTrue(trial.HasTransitions);
        }

        /// <summary>Verifies that HasTransitions reports true for a multi-entry transition map.</summary>
        [Test]
        public void HasTransitions_MultipleEntries_ReturnsTrue()
        {
            TrialStructure trial = new TrialStructure
            {
                transitions = new Dictionary<string, float> { { "AB", 0.25f }, { "BC", 0.75f } },
            };

            Assert.IsTrue(trial.HasTransitions);
            Assert.AreEqual(2, trial.transitions.Count);
        }

        /// <summary>Verifies that HasTransitions keys off entry count rather than the probability values.</summary>
        [Test]
        public void HasTransitions_SingleZeroProbabilityEntry_ReturnsTrue()
        {
            TrialStructure trial = new TrialStructure
            {
                transitions = new Dictionary<string, float> { { "AB", 0.0f } },
            };

            Assert.IsTrue(trial.HasTransitions);
        }

        /// <summary>Verifies that HasTransitions reports false again once a populated map is cleared.</summary>
        [Test]
        public void HasTransitions_PopulatedMapCleared_ReturnsFalse()
        {
            TrialStructure trial = new TrialStructure
            {
                transitions = new Dictionary<string, float> { { "AB", 1.0f } },
            };
            Assert.IsTrue(trial.HasTransitions);

            trial.transitions.Clear();

            Assert.IsFalse(trial.HasTransitions);
        }

        /// <summary>Verifies that HasTransitions re-evaluates the map on each read rather than caching it.</summary>
        [Test]
        public void HasTransitions_EntryAddedAfterFirstRead_ReturnsTrue()
        {
            TrialStructure trial = new TrialStructure { transitions = new Dictionary<string, float>() };
            Assert.IsFalse(trial.HasTransitions);

            trial.transitions["AB"] = 1.0f;

            Assert.IsTrue(trial.HasTransitions);
        }

        /// <summary>
        /// Verifies that HasTransitions re-evaluates after the map reference itself is replaced by null.
        /// </summary>
        [Test]
        public void HasTransitions_MapReplacedWithNullAfterFirstRead_ReturnsFalse()
        {
            TrialStructure trial = new TrialStructure
            {
                transitions = new Dictionary<string, float> { { "AB", 1.0f } },
            };
            Assert.IsTrue(trial.HasTransitions);

            trial.transitions = null;

            Assert.IsFalse(trial.HasTransitions);
        }

        /// <summary>
        /// Verifies that an assigned occupancy duration reports a value rather than the null default.
        /// </summary>
        [Test]
        public void OccupancyDurationMs_AssignedPositiveDuration_ReportsThatValue()
        {
            TrialStructure trial = new TrialStructure { occupancyDurationMs = 250.0f };

            Assert.IsTrue(trial.occupancyDurationMs.HasValue);
            Assert.AreEqual(250.0f, trial.occupancyDurationMs.Value);
        }

        /// <summary>Verifies that an occupancy duration of zero is a present value rather than a missing one.</summary>
        [Test]
        public void OccupancyDurationMs_AssignedZero_ReportsPresentZeroValue()
        {
            TrialStructure trial = new TrialStructure { occupancyDurationMs = 0.0f };

            Assert.IsTrue(trial.occupancyDurationMs.HasValue);
            Assert.AreEqual(0.0f, trial.occupancyDurationMs.Value);
        }

        /// <summary>Verifies that clearing the occupancy duration restores the missing-value state.</summary>
        [Test]
        public void OccupancyDurationMs_ClearedAfterAssignment_ReportsNoValue()
        {
            TrialStructure trial = new TrialStructure { occupancyDurationMs = 500.0f };
            Assert.IsTrue(trial.occupancyDurationMs.HasValue);

            trial.occupancyDurationMs = null;

            Assert.IsFalse(trial.occupancyDurationMs.HasValue);
        }

        /// <summary>
        /// Verifies that the stimulus zone boundary fields retain the exact centimeter values assigned.
        /// </summary>
        [Test]
        public void ZoneGeometryFields_AssignedValues_RetainExactCentimeterValues()
        {
            TrialStructure trial = new TrialStructure
            {
                cueSequence = new List<string> { "A", "B" },
                stimulusTriggerZoneStartCm = 12.5f,
                stimulusTriggerZoneEndCm = 47.5f,
                stimulusLocationCm = 30.0f,
                showStimulusCollisionBoundary = true,
                triggerType = "occupancy_arm",
            };

            Assert.AreEqual(2, trial.cueSequence.Count);
            Assert.AreEqual("A", trial.cueSequence[0]);
            Assert.AreEqual("B", trial.cueSequence[1]);
            Assert.AreEqual(12.5f, trial.stimulusTriggerZoneStartCm);
            Assert.AreEqual(47.5f, trial.stimulusTriggerZoneEndCm);
            Assert.AreEqual(30.0f, trial.stimulusLocationCm);
            Assert.IsTrue(trial.showStimulusCollisionBoundary);
            Assert.AreEqual("occupancy_arm", trial.triggerType);
        }
    }
}
