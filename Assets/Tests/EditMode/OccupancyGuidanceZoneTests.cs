/// <summary>
/// Verifies the behavior of the OccupancyGuidanceZone class.
///
/// Covers the two Start resolution guards, the collaborators and the Delay channel a successful Start establishes,
/// the three gates deciding whether a zone entry requests the brake, the per-lap latch OnTriggerExit leaves standing,
/// and the ResetState re-arm. The remaining-duration arithmetic is driven by replacing the parent OccupancyZone's
/// stopwatch with a stopped one carrying an arranged tick count, so an Edit Mode run pins every boundary of the
/// clamped subtraction without waiting on wall-clock time.
/// </summary>
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the OccupancyGuidanceZone class.</summary>
    [TestFixture]
    public class OccupancyGuidanceZoneTests
    {
        /// <summary>The name of the Mono stopwatch field accumulating elapsed timestamp ticks.</summary>
        private const string MonoElapsedFieldName = "elapsed";

        /// <summary>The name of the CoreCLR stopwatch field accumulating elapsed timestamp ticks.</summary>
        private const string CoreClrElapsedFieldName = "_elapsed";

        /// <summary>The name of the private OccupancyZone field holding the occupancy stopwatch.</summary>
        private const string OccupancyTimerFieldName = "_occupancyTimer";

        /// <summary>The name of the private OccupancyGuidanceZone field latching the per-lap brake fire.</summary>
        private const string HasTriggeredFieldName = "_hasTriggered";

        /// <summary>The name of the private OccupancyGuidanceZone field holding the resolved scene Task.</summary>
        private const string TaskFieldName = "_task";

        /// <summary>The name of the private OccupancyGuidanceZone field holding the resolved parent zone.</summary>
        private const string ParentOccupancyZoneFieldName = "_parentOccupancyZone";

        /// <summary>The name of the private OccupancyGuidanceZone field holding the brake request channel.</summary>
        private const string TriggerDelayChannelFieldName = "_triggerDelayChannel";

        /// <summary>The number of stopwatch timestamp ticks that make up one millisecond on this runtime.</summary>
        private static readonly long TimestampTicksPerMillisecond = Stopwatch.Frequency / 1000L;

        /// <summary>Verifies that Start reports a missing Task and disables the component.</summary>
        [Test]
        public void Start_NoTaskInScene_LogsErrorAndDisablesComponent()
        {
            GameObject zoneObject = new GameObject("OrphanGuidanceZone");
            try
            {
                OccupancyGuidanceZone zone = zoneObject.AddComponent<OccupancyGuidanceZone>();
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"OccupancyGuidanceZone \(OrphanGuidanceZone\): No Task found in scene\.")
                );

                PrivateAccess.Invoke(zone, "Start");

                Assert.IsFalse(zone.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
            }
        }

        /// <summary>Verifies that Start reports a missing parent OccupancyZone and disables the component.</summary>
        [Test]
        public void Start_NoParentOccupancyZone_LogsErrorAndDisablesComponent()
        {
            GameObject rootObject = new GameObject("ParentlessRig");
            try
            {
                GameObject taskObject = new GameObject("Task");
                taskObject.transform.SetParent(rootObject.transform);
                taskObject.AddComponent<Task>();

                GameObject zoneObject = new GameObject("StrandedGuidanceZone");
                zoneObject.transform.SetParent(rootObject.transform);
                OccupancyGuidanceZone zone = zoneObject.AddComponent<OccupancyGuidanceZone>();
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"OccupancyGuidanceZone \(StrandedGuidanceZone\): No parent OccupancyZone found\.")
                );

                PrivateAccess.Invoke(zone, "Start");

                Assert.IsFalse(zone.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        /// <summary>Verifies that Start leaves a fully resolved guidance zone enabled.</summary>
        [Test]
        public void Start_ValidHierarchy_LeavesComponentEnabled()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                Assert.IsTrue(rig.OccupancyGuidanceZone.enabled);
            }
        }

        /// <summary>Verifies that Start resolves the scene Task and the parent OccupancyZone it reads.</summary>
        [Test]
        public void Start_ValidHierarchy_ResolvesTheTaskAndTheParentOccupancyZone()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                Task resolvedTask = PrivateAccess.GetField<Task>(rig.OccupancyGuidanceZone, TaskFieldName);
                OccupancyZone resolvedParent = PrivateAccess.GetField<OccupancyZone>(
                    rig.OccupancyGuidanceZone,
                    ParentOccupancyZoneFieldName
                );

                Assert.AreSame(rig.Task, resolvedTask);
                Assert.AreSame(rig.OccupancyZone, resolvedParent);
            }
        }

        /// <summary>Verifies that Start opens the brake request channel on the Delay topic.</summary>
        [Test]
        public void Start_ValidHierarchy_OpensTheBrakeRequestChannelOnTheDelayTopic()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                MQTTChannel channel = PrivateAccess.GetField<MQTTChannel>(
                    rig.OccupancyGuidanceZone,
                    TriggerDelayChannelFieldName
                );

                Assert.AreEqual(MQTTTopics.Delay, channel.topic);
                Assert.IsInstanceOf<MQTTChannel<OccupancyGuidanceZone.TriggerDelayMessage>>(channel);
            }
        }

        /// <summary>Verifies that Start clears a serialized occupied and fired state through ResetState.</summary>
        [Test]
        public void Start_SerializedTriggeredState_ClearsInZoneAndBrakeTriggered()
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, 1000f);
            using (ZoneRig rig = ZoneRig.Create(options))
            {
                rig.OccupancyGuidanceZone.inZone = true;
                PrivateAccess.SetField(rig.OccupancyGuidanceZone, HasTriggeredFieldName, true);

                rig.StartComponents();

                Assert.IsFalse(rig.OccupancyGuidanceZone.inZone);
                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that BrakeTriggered reports the private per-lap fire flag rather than inZone.</summary>
        [Test]
        public void BrakeTriggered_FiredFlagSetDirectly_ReportsTrueWhileInZoneStaysFalse()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                PrivateAccess.SetField(rig.OccupancyGuidanceZone, HasTriggeredFieldName, true);

                Assert.IsTrue(rig.OccupancyGuidanceZone.BrakeTriggered);
                Assert.IsFalse(rig.OccupancyGuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that entering the guidance zone marks it occupied.</summary>
        [Test]
        public void OnTriggerEnter_GuidanceMode_SetsInZoneTrue()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();

                Assert.IsTrue(rig.OccupancyGuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that guidance mode publishes one brake request, on the Delay topic alone.</summary>
        [Test]
        public void OnTriggerEnter_GuidanceModeWithUnmetOccupancy_PublishesBrakeRequestOnDelayTopic()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1, rig.Mqtt.CountOn(MQTTTopics.Delay));
                CollectionAssert.AreEqual(new string[] { MQTTTopics.Delay }, TopicsCarryingPayloads(rig));
            }
        }

        /// <summary>Verifies that a fired brake request latches the per-lap flag.</summary>
        [Test]
        public void OnTriggerEnter_GuidanceModeWithUnmetOccupancy_LatchesBrakeTriggered()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);

                rig.EnterOccupancyGuidanceZone();

                Assert.IsTrue(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that the wait requirement suppresses the brake while still marking the zone.</summary>
        [Test]
        public void OnTriggerEnter_WaitRequired_SetsInZoneWithoutFiringBrake()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.Task.requireWait = true;

                rig.EnterOccupancyGuidanceZone();

                Assert.IsTrue(rig.OccupancyGuidanceZone.inZone);
                Assert.AreEqual(0, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that a second entry in the same lap publishes no further brake request.</summary>
        [Test]
        public void OnTriggerEnter_BrakeAlreadyFiredThisLap_PublishesExactlyOneBrakeRequest()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();
                rig.ExitOccupancyGuidanceZone();
                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.IsTrue(rig.OccupancyGuidanceZone.inZone);
            }
        }

        /// <summary>Verifies that a met occupancy requirement suppresses the brake while marking the zone.</summary>
        [Test]
        public void OnTriggerEnter_OccupancyAlreadyMet_SetsInZoneWithoutFiringBrake()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.OccupancyZone.occupancyMet = true;

                rig.EnterOccupancyGuidanceZone();

                Assert.IsTrue(rig.OccupancyGuidanceZone.inZone);
                Assert.AreEqual(0, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that leaving the guidance zone clears inZone and keeps the fired flag latched.</summary>
        [Test]
        public void OnTriggerExit_AfterBrakeFired_ClearsInZoneAndKeepsBrakeTriggered()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();

                rig.ExitOccupancyGuidanceZone();

                Assert.IsFalse(rig.OccupancyGuidanceZone.inZone);
                Assert.IsTrue(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that ResetState clears both the occupancy flag and the per-lap fire flag.</summary>
        [Test]
        public void ResetState_AfterBrakeFired_ClearsInZoneAndBrakeTriggered()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();
                Assert.IsTrue(rig.OccupancyGuidanceZone.inZone);
                Assert.IsTrue(rig.OccupancyGuidanceZone.BrakeTriggered);

                rig.OccupancyGuidanceZone.ResetState();

                Assert.IsFalse(rig.OccupancyGuidanceZone.inZone);
                Assert.IsFalse(rig.OccupancyGuidanceZone.BrakeTriggered);
            }
        }

        /// <summary>Verifies that the brake fires again on the lap following a ResetState.</summary>
        [Test]
        public void ResetState_AfterBrakeFired_AllowsTheBrakeToFireOnTheNextLap()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                rig.EnterOccupancyGuidanceZone();
                rig.ExitOccupancyGuidanceZone();

                rig.OccupancyGuidanceZone.ResetState();
                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(2, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.AreEqual(1000u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that an untouched occupancy timer publishes the whole configured duration.</summary>
        [Test]
        public void OnTriggerEnter_ZeroElapsedOccupancy_PublishesTheFullDuration()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                Assert.AreEqual(0L, rig.OccupancyElapsedMilliseconds());

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1000u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that a zero occupancy duration publishes a zero remaining duration.</summary>
        [Test]
        public void OnTriggerEnter_ZeroOccupancyDuration_PublishesZero()
        {
            using (ZoneRig rig = CreateStartedRig(0f))
            {
                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.AreEqual(0u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that a partly elapsed occupancy publishes only the remainder.</summary>
        [Test]
        public void OnTriggerEnter_PartiallyElapsedOccupancy_PublishesTheRemainder()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                SetOccupancyElapsedMilliseconds(rig, 400L);

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(600u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that an elapsed reading one millisecond short of the duration publishes one.</summary>
        [Test]
        public void OnTriggerEnter_ElapsedOneMillisecondBelowDuration_PublishesOneMillisecond()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                SetOccupancyElapsedMilliseconds(rig, 999L);

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that an elapsed reading exactly at the duration publishes zero.</summary>
        [Test]
        public void OnTriggerEnter_ElapsedEqualsDuration_PublishesZero()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                SetOccupancyElapsedMilliseconds(rig, 1000L);

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(0u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that an overrun clamps to zero instead of wrapping around the unsigned range.</summary>
        [Test]
        public void OnTriggerEnter_ElapsedBeyondDuration_PublishesZeroRatherThanAWrappedValue()
        {
            using (ZoneRig rig = CreateStartedRig(1000f))
            {
                SetOccupancyElapsedMilliseconds(rig, 4000L);

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(1, rig.Mqtt.CountOn(MQTTTopics.Delay));
                Assert.AreEqual(0u, LastDelayMilliseconds(rig));
            }
        }

        /// <summary>Verifies that a duration past the float exact-integer range keeps every millisecond.</summary>
        [Test]
        public void OnTriggerEnter_DurationBeyondFloatExactIntegerRange_PublishesTheExactMillisecondCount()
        {
            using (ZoneRig rig = CreateStartedRig(20000000f))
            {
                SetOccupancyElapsedMilliseconds(rig, 1L);

                rig.EnterOccupancyGuidanceZone();

                Assert.AreEqual(19999999u, LastDelayMilliseconds(rig));
                StringAssert.Contains("19999999", rig.Mqtt.LastPayloadOn(MQTTTopics.Delay));
            }
        }

        /// <summary>Builds an occupancy rig and runs every component's Start callback.</summary>
        /// <param name="occupancyDurationMs">The occupancy duration in milliseconds assigned to the zone.</param>
        /// <returns>The started rig, which the caller disposes.</returns>
        private static ZoneRig CreateStartedRig(float occupancyDurationMs)
        {
            ZoneRigOptions options = ZoneRigOptions.Occupancy(TriggerMode.OccupancyTrigger, occupancyDurationMs);
            ZoneRig rig = ZoneRig.Create(options);
            rig.StartComponents();
            return rig;
        }

        /// <summary>Returns every known topic the rig captured at least one payload on, in declaration order.</summary>
        /// <param name="rig">The rig whose captured payloads to survey.</param>
        /// <returns>The topics carrying at least one payload.</returns>
        private static List<string> TopicsCarryingPayloads(ZoneRig rig)
        {
            List<string> topics = new List<string>();
            foreach (string topic in MqttTestHarness.KnownTopics())
            {
                if (rig.Mqtt.CountOn(topic) > 0)
                {
                    topics.Add(topic);
                }
            }
            return topics;
        }

        /// <summary>Returns the remaining duration carried by the most recent brake request.</summary>
        /// <param name="rig">The rig whose captured Delay payloads to read.</param>
        /// <returns>The remaining duration in milliseconds.</returns>
        private static uint LastDelayMilliseconds(ZoneRig rig)
        {
            OccupancyGuidanceZone.TriggerDelayMessage message =
                rig.Mqtt.LastMessageOn<OccupancyGuidanceZone.TriggerDelayMessage>(MQTTTopics.Delay);
            return message.delayMilliseconds;
        }

        /// <summary>Replaces the occupancy timer with a stopped stopwatch reading the requested elapsed time.</summary>
        /// <remarks>
        /// The substitute stopwatch is never started, so its reading comes entirely from the accumulated tick field
        /// and an Edit Mode test pins the remaining-duration arithmetic without depending on wall-clock time.
        /// </remarks>
        /// <param name="rig">The rig whose occupancy zone timer to replace.</param>
        /// <param name="milliseconds">The elapsed reading the replacement timer reports.</param>
        private static void SetOccupancyElapsedMilliseconds(ZoneRig rig, long milliseconds)
        {
            Stopwatch timer = new Stopwatch();
            PrivateAccess.SetField(timer, ElapsedFieldName(), milliseconds * TimestampTicksPerMillisecond);
            PrivateAccess.SetField(rig.OccupancyZone, OccupancyTimerFieldName, timer);
            Assert.AreEqual(milliseconds, rig.OccupancyElapsedMilliseconds());
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
    }
}
