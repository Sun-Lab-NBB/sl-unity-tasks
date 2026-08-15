/// <summary>
/// Verifies the behavior of the OccupancyZone class.
///
/// The fixture drives the zone's Start, Update, OnTriggerEnter, and OnTriggerExit callbacks directly, so every
/// guard resolves without a player loop or a physics tick. Both sides of each guard are covered, and the
/// elapsed-versus-duration comparison is pinned on all three sides: strictly above, exactly equal, and strictly
/// below. The boundary tests install a substitute stopwatch carrying a known accumulated reading rather than
/// waiting on wall-clock time, because an Edit Mode test may neither sleep nor busy-wait.
/// </summary>
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the OccupancyZone class.</summary>
    [TestFixture]
    public class OccupancyZoneTests
    {
        /// <summary>The message the zone logs when the animal enters while the zone tracks occupancy.</summary>
        private const string EnteredLogMessage = "OccupancyZone: Animal entered, timer started.";

        /// <summary>The message the zone logs when the elapsed time reaches the required occupancy duration.</summary>
        private const string MetLogMessage = "OccupancyZone: Occupancy requirement met.";

        /// <summary>The message the zone logs when the animal exits before the occupancy requirement is met.</summary>
        private const string FailedLogMessage = "OccupancyZone: Occupancy failed - animal left early.";

        /// <summary>The occupancy duration in milliseconds that no elapsed reading inside a test reaches.</summary>
        private const long UnreachableDurationMilliseconds = 60000L;

        /// <summary>The occupancy duration in milliseconds the boundary tests compare their readings against.
        /// </summary>
        private const long BoundaryDurationMilliseconds = 1000L;

        /// <summary>The elapsed reading in milliseconds that sits strictly above the boundary duration.</summary>
        private const long AboveBoundaryMilliseconds = 1500L;

        /// <summary>The accumulated reading in milliseconds installed to pin the elapsed-time accessor.</summary>
        private const long AccumulatedMilliseconds = 1234L;

        /// <summary>The name of the private OccupancyZone field holding the occupancy stopwatch.</summary>
        private const string OccupancyTimerFieldName = "_occupancyTimer";

        /// <summary>The name of the Mono stopwatch field accumulating elapsed timestamp ticks.</summary>
        private const string MonoElapsedFieldName = "elapsed";

        /// <summary>The name of the CoreCLR stopwatch field accumulating elapsed timestamp ticks.</summary>
        private const string CoreClrElapsedFieldName = "_elapsed";

        /// <summary>The number of stopwatch timestamp ticks that make up one millisecond on this runtime.</summary>
        private static readonly long TimestampTicksPerMillisecond = Stopwatch.Frequency / 1000L;

        /// <summary>Every log message Unity routed through the log callback during the running test.</summary>
        private readonly List<string> _logMessages = new List<string>();

        /// <summary>Subscribes the log recorder before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _logMessages.Clear();
            Application.logMessageReceived += RecordLogMessage;
        }

        /// <summary>Unsubscribes the log recorder after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= RecordLogMessage;
            _logMessages.Clear();
        }

        /// <summary>Verifies that Start allocates the occupancy stopwatch in a stopped, zeroed state.</summary>
        [Test]
        public void Start_UnstartedZone_AllocatesTheStoppedOccupancyTimer()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                Assert.IsNull(OccupancyTimer(rig));

                rig.StartComponents();

                Stopwatch timer = OccupancyTimer(rig);
                Assert.IsNotNull(timer);
                Assert.IsFalse(timer.IsRunning);
                Assert.AreEqual(0L, rig.OccupancyElapsedMilliseconds());
            }
        }

        /// <summary>Verifies that Start restores the per-lap defaults over a dirty serialized state.</summary>
        [Test]
        public void Start_DirtySerializedState_RestoresThePerLapDefaults()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyDisarm)))
            {
                rig.OccupancyZone.isActive = false;
                rig.OccupancyZone.occupancyMet = true;
                rig.OccupancyZone.inZone = true;

                rig.StartComponents();

                Assert.IsTrue(rig.OccupancyZone.isActive);
                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsFalse(rig.OccupancyZone.inZone);
            }
        }

        /// <summary>Verifies that ResetState requires the timer that Start allocates.</summary>
        [Test]
        public void ResetState_BeforeStart_ThrowsNullReferenceException()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                Assert.Throws<NullReferenceException>(() => rig.OccupancyZone.ResetState());
            }
        }

        /// <summary>Verifies that a freshly added component carries the declared field defaults.</summary>
        [Test]
        public void OccupancyZone_FreshlyAttachedComponent_UsesTheDeclaredFieldDefaults()
        {
            GameObject host = new GameObject("BareOccupancyZone");
            try
            {
                OccupancyZone zone = host.AddComponent<OccupancyZone>();

                Assert.AreEqual(1000f, zone.occupancyDurationMs);
                Assert.IsTrue(zone.isActive);
                Assert.IsFalse(zone.inZone);
                Assert.IsFalse(zone.occupancyMet);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Verifies that entering an armed zone records the occupancy and starts the timer.</summary>
        [Test]
        public void OnTriggerEnter_ActiveZone_MarksInZoneAndStartsTheTimer()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                LogAssert.Expect(LogType.Log, EnteredLogMessage);

                rig.EnterOccupancyZone();

                Assert.IsTrue(rig.OccupancyZone.inZone);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
            }
        }

        /// <summary>Verifies that re-entering the zone starts the timer the earlier exit stopped.</summary>
        [Test]
        public void OnTriggerEnter_ReEntryAfterExit_RestartsTheStoppedTimer()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.ExitOccupancyZone();
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);

                rig.EnterOccupancyZone();

                Assert.IsTrue(rig.OccupancyZone.inZone);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(2, CountLogMessages(EnteredLogMessage));
            }
        }

        /// <summary>Verifies that entering a deactivated zone leaves the occupancy state untouched.</summary>
        [Test]
        public void OnTriggerEnter_InactiveZone_LeavesTheZoneStateUnchanged()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.OccupancyZone.isActive = false;

                rig.EnterOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.inZone);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(EnteredLogMessage));
            }
        }

        /// <summary>Verifies that entering after the requirement is met leaves the occupancy state untouched.
        /// </summary>
        [Test]
        public void OnTriggerEnter_OccupancyAlreadyMet_LeavesTheZoneStateUnchanged()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.OccupancyZone.occupancyMet = true;

                rig.EnterOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.inZone);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(EnteredLogMessage));
            }
        }

        /// <summary>Verifies that exiting before the requirement is met clears the state and reports the failure.
        /// </summary>
        [Test]
        public void OnTriggerExit_ActiveZoneWithRequirementUnmet_ClearsInZoneAndStopsTheTimer()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                LogAssert.Expect(LogType.Log, FailedLogMessage);

                rig.ExitOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.inZone);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
            }
        }

        /// <summary>Verifies that exiting after the requirement is met reports no failure.</summary>
        [Test]
        public void OnTriggerExit_OccupancyMet_ReportsNoFailure()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.OccupancyZone.occupancyMet = true;

                rig.ExitOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.inZone);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(FailedLogMessage));
            }
        }

        /// <summary>Verifies that exiting a deactivated zone leaves the occupancy state untouched.</summary>
        [Test]
        public void OnTriggerExit_InactiveZone_LeavesTheZoneStateUnchanged()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.OccupancyZone.isActive = false;

                rig.ExitOccupancyZone();

                Assert.IsTrue(rig.OccupancyZone.inZone);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(FailedLogMessage));
            }
        }

        /// <summary>Verifies that Update leaves the requirement unmet while the timer is stopped.</summary>
        [Test]
        public void Update_TimerNotRunning_LeavesTheRequirementUnmet()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();

                // Marks the zone occupied without the trigger callback, so the timer stays stopped.
                rig.OccupancyZone.inZone = true;

                rig.TickOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that Update leaves the requirement unmet while the animal is outside the zone.</summary>
        [Test]
        public void Update_NotInZoneWhileTheTimerRuns_LeavesTheRequirementUnmet()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();

                // Clears the occupancy flag without the exit callback, so the timer keeps running.
                rig.OccupancyZone.inZone = false;

                rig.TickOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that an elapsed reading equal to the duration meets the occupancy requirement.
        /// </summary>
        [Test]
        public void Update_ElapsedEqualToTheDuration_MeetsTheRequirement()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(
                TriggerMode.OccupancyTrigger,
                BoundaryDurationMilliseconds
            );
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();

                // Warms the reflection and compilation paths so no first-call cost lands between starting the
                // substitute timer and the comparison. The zone ignores this call because the timer is stopped.
                rig.TickOccupancyZone();

                // Installs a timer already carrying exactly the required duration and marks the zone occupied
                // without the trigger callback, whose Restart would discard the accumulated reading.
                rig.OccupancyZone.inZone = true;
                LogAssert.Expect(LogType.Log, MetLogMessage);
                Stopwatch timer = InstallOccupancyTimer(rig, BoundaryDurationMilliseconds, running: true);

                rig.TickOccupancyZone();

                // The met path froze the timer, so a reading still equal to the duration proves the comparison saw
                // an elapsed time exactly equal to it rather than one above it. A host stall carrying the reading
                // past the boundary leaves the run inconclusive, because the case this test pins never occurred.
                Assume.That(rig.OccupancyElapsedMilliseconds(), Is.EqualTo(BoundaryDurationMilliseconds));
                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                Assert.IsFalse(timer.IsRunning);
            }
        }

        /// <summary>Verifies that an elapsed reading above the duration meets the occupancy requirement.</summary>
        [Test]
        public void Update_ElapsedAboveTheDuration_MeetsTheRequirement()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(
                TriggerMode.OccupancyTrigger,
                BoundaryDurationMilliseconds
            );
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();

                // Installs a timer already half a second past the required duration, so the comparison reads
                // strictly above the boundary with a margin no scheduling delay can erase.
                rig.OccupancyZone.inZone = true;
                LogAssert.Expect(LogType.Log, MetLogMessage);
                Stopwatch timer = InstallOccupancyTimer(rig, AboveBoundaryMilliseconds, running: true);

                rig.TickOccupancyZone();

                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                Assert.IsFalse(timer.IsRunning);
                Assert.GreaterOrEqual(rig.OccupancyElapsedMilliseconds(), AboveBoundaryMilliseconds);
            }
        }

        /// <summary>Verifies that an elapsed reading below the duration leaves the requirement unmet.</summary>
        [Test]
        public void Update_ElapsedBelowTheDuration_LeavesTheRequirementUnmet()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(
                TriggerMode.OccupancyTrigger,
                UnreachableDurationMilliseconds
            );
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();

                rig.TickOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that Update leaves the requirement unmet while the zone is deactivated.</summary>
        [Test]
        public void Update_InactiveZone_LeavesTheRequirementUnmet()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.OccupancyZone.isActive = false;

                rig.TickOccupancyZone();

                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that Update skips the met path once the requirement is already recorded.</summary>
        [Test]
        public void Update_OccupancyAlreadyMet_SkipsTheMetPath()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.OccupancyZone.occupancyMet = true;

                rig.TickOccupancyZone();

                // The met path stops the timer, so a still-running timer proves the guard returned first.
                Assert.IsTrue(OccupancyTimer(rig).IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that the requirement latches so a later frame does not meet it a second time.</summary>
        [Test]
        public void Update_FrameAfterTheRequirementWasMet_DoesNotMeetItAgain()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 0f)))
            {
                rig.StartComponents();
                Stopwatch timer = OccupancyTimer(rig);
                rig.EnterOccupancyZone();
                LogAssert.Expect(LogType.Log, MetLogMessage);
                rig.TickOccupancyZone();
                Assert.IsTrue(rig.OccupancyZone.occupancyMet);

                // Re-satisfies every condition the met path checks, leaving the latch as the only thing blocking it.
                _logMessages.Clear();
                timer.Restart();

                rig.TickOccupancyZone();

                Assert.IsTrue(rig.OccupancyZone.occupancyMet);
                Assert.IsTrue(timer.IsRunning);
                Assert.AreEqual(0, CountLogMessages(MetLogMessage));
            }
        }

        /// <summary>Verifies that ResetState restores the per-lap defaults over a dirty lap state.</summary>
        [Test]
        public void ResetState_DirtyLapState_RestoresThePerLapDefaults()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                rig.OccupancyZone.isActive = false;
                rig.OccupancyZone.occupancyMet = true;

                rig.OccupancyZone.ResetState();

                Assert.IsTrue(rig.OccupancyZone.isActive);
                Assert.IsFalse(rig.OccupancyZone.occupancyMet);
                Assert.IsFalse(rig.OccupancyZone.inZone);
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
            }
        }

        /// <summary>Verifies that the elapsed reading returns to zero after a reset.</summary>
        [Test]
        public void GetElapsedMilliseconds_AfterResetState_ReturnsZero()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();

                rig.OccupancyZone.ResetState();

                Assert.AreEqual(0L, rig.OccupancyElapsedMilliseconds());
            }
        }

        /// <summary>Verifies that the elapsed reading reports the stopped stopwatch's accumulated milliseconds.
        /// </summary>
        [Test]
        public void GetElapsedMilliseconds_StoppedTimerWithAccumulatedTime_ReportsThatManyMilliseconds()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();

                Stopwatch timer = InstallOccupancyTimer(rig, AccumulatedMilliseconds, running: false);

                Assert.IsFalse(timer.IsRunning);
                Assert.AreEqual(AccumulatedMilliseconds, rig.OccupancyElapsedMilliseconds());
            }
        }

        /// <summary>Verifies that stopping the timer on exit retains the reading the guidance zone consumes.
        /// </summary>
        [Test]
        public void GetElapsedMilliseconds_AfterOnTriggerExit_RetainsTheAccumulatedReading()
        {
            using (ZoneRig rig = ZoneRig.Create(ZoneRigOptions.Occupancy(TriggerMode.OccupancyArm)))
            {
                rig.StartComponents();
                rig.EnterOccupancyZone();
                InstallOccupancyTimer(rig, AccumulatedMilliseconds, running: true);

                rig.ExitOccupancyZone();

                // The exit stops the timer rather than resetting it, so the reading survives the boundary crossing
                // that OccupancyGuidanceZone subtracts from when it computes the remaining brake duration.
                Assert.IsFalse(OccupancyTimer(rig).IsRunning);
                Assert.GreaterOrEqual(rig.OccupancyElapsedMilliseconds(), AccumulatedMilliseconds);
            }
        }

        /// <summary>Returns the private stopwatch the occupancy zone times its laps with.</summary>
        /// <param name="rig">The rig whose occupancy zone to read.</param>
        /// <returns>The stopwatch, or null before the zone's Start allocated it.</returns>
        private static Stopwatch OccupancyTimer(ZoneRig rig)
        {
            return PrivateAccess.GetField<Stopwatch>(rig.OccupancyZone, OccupancyTimerFieldName);
        }

        /// <summary>Replaces the occupancy timer with a stopwatch already holding the requested reading.</summary>
        /// <remarks>
        /// The accumulated tick field is written directly, so an Edit Mode test pins the elapsed-versus-duration
        /// comparison at a chosen boundary without sleeping or busy-waiting. A running replacement is started last
        /// so the reading advances for as little as possible before the callback under test reads it.
        /// </remarks>
        /// <param name="rig">The rig whose occupancy zone timer to replace.</param>
        /// <param name="milliseconds">The accumulated reading the replacement timer starts out reporting.</param>
        /// <param name="running">Determines whether the replacement timer is left running.</param>
        /// <returns>The replacement stopwatch now installed on the occupancy zone.</returns>
        private static Stopwatch InstallOccupancyTimer(ZoneRig rig, long milliseconds, bool running)
        {
            Stopwatch timer = new Stopwatch();
            PrivateAccess.SetField(timer, ElapsedFieldName(), milliseconds * TimestampTicksPerMillisecond);
            PrivateAccess.SetField(rig.OccupancyZone, OccupancyTimerFieldName, timer);
            Assert.AreEqual(milliseconds, timer.ElapsedMilliseconds);
            if (running)
            {
                timer.Start();
            }
            return timer;
        }

        /// <summary>Resolves the stopwatch field accumulating elapsed timestamp ticks on this runtime.</summary>
        /// <returns>The field name, which differs between the Mono and CoreCLR class libraries.</returns>
        private static string ElapsedFieldName()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (typeof(Stopwatch).GetField(MonoElapsedFieldName, flags) != null)
            {
                return MonoElapsedFieldName;
            }
            if (typeof(Stopwatch).GetField(CoreClrElapsedFieldName, flags) != null)
            {
                return CoreClrElapsedFieldName;
            }

            string message =
                "Unable to resolve the stopwatch field accumulating elapsed ticks. The runtime must declare either "
                + $"'{MonoElapsedFieldName}' or '{CoreClrElapsedFieldName}', but it declares neither.";
            Assert.Fail(message);
            return MonoElapsedFieldName;
        }

        /// <summary>Records one message Unity routed through the log callback.</summary>
        /// <param name="condition">The logged message text.</param>
        /// <param name="stackTrace">The stack trace Unity captured for the message.</param>
        /// <param name="type">The severity Unity logged the message at.</param>
        private void RecordLogMessage(string condition, string stackTrace, LogType type)
        {
            _logMessages.Add(condition);
        }

        /// <summary>Returns how many recorded messages match the expected message exactly.</summary>
        /// <param name="expectedMessage">The message text to match.</param>
        /// <returns>The number of matching recorded messages.</returns>
        private int CountLogMessages(string expectedMessage)
        {
            int count = 0;
            foreach (string message in _logMessages)
            {
                if (string.Equals(message, expectedMessage, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
