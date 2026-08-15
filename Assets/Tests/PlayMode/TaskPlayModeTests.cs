/// <summary>
/// Verifies the behavior of the Task class under the real Unity player loop.
/// </summary>
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tests.PlayMode
{
    /// <summary>Verifies the behavior of the Task class under the real Unity player loop.</summary>
    /// <remarks>
    /// Unity itself drives Awake, Start, and Update here, and every corridor advance happens across real frames. That
    /// is what separates this fixture from the Edit Mode TaskTests, which invokes the same callbacks through
    /// reflection. Reflection cannot observe the ordering the engine imposes, a component that stops receiving Update
    /// once it is disabled, or a teleport that lands between two consecutive frames.
    /// </remarks>
    [TestFixture]
    public class TaskPlayModeTests
    {
        /// <summary>The dataPath-relative directory that every staged test template is written into.</summary>
        private const string ConfigurationsDirectory = "InfiniteCorridorTask/Configurations";

        /// <summary>The name of the two-trial corridor template that the traversal tests load.</summary>
        private const string PairTemplateName = "ZZTest_PlayPair";

        /// <summary>The name of the single-trial template that the track length boundary tests load.</summary>
        private const string SingleTemplateName = "ZZTest_PlaySingle";

        /// <summary>The name of the template that fails ConfigLoader validation.</summary>
        private const string InvalidTemplateName = "ZZTest_PlayInvalid";

        /// <summary>The name of a template that is never written, used for the absent-file branch.</summary>
        private const string AbsentTemplateName = "ZZTest_PlayAbsent";

        /// <summary>The texture every staged cue references, which already ships under Textures.</summary>
        /// <remarks>
        /// Cue textures resolve as "&lt;template directory&gt;/../Textures", so a template staged under Configurations
        /// reaches the project's own Textures directory.
        /// </remarks>
        private const string StagedTextureName = "Gray Cue 2x1.png";

        /// <summary>The corridor depth every staged template declares.</summary>
        private const int PairDepth = 2;

        /// <summary>The number of trials the pair template declares.</summary>
        private const int PairTrialCount = 2;

        /// <summary>The corridor spacing of every staged template in Unity units (500 cm over 10 cm per unit).
        /// </summary>
        private const float CorridorSpacingUnity = 50f;

        /// <summary>The Unity length of the pair template's "Short" segment (100 cm over 10 cm per unit).</summary>
        private const float ShortSegmentLengthUnity = 10f;

        /// <summary>The Unity length of the pair template's "Long" segment (200 cm over 10 cm per unit).</summary>
        private const float LongSegmentLengthUnity = 20f;

        /// <summary>The byte code the staged templates assign to cue "A".</summary>
        private const int CueCodeA = 1;

        /// <summary>The byte code the staged templates assign to cue "B".</summary>
        private const int CueCodeB = 2;

        /// <summary>The seed every deterministic maze generation in this fixture uses.</summary>
        private const int PairSeed = 4242;

        /// <summary>The track length every pair-template traversal test generates its maze with.</summary>
        private const float PairTrackLength = 400f;

        /// <summary>The distance past the segment boundary the actor is placed to force one advance.</summary>
        private const float AdvanceOvershoot = 3.5f;

        /// <summary>The tolerance applied to every actor position comparison, in Unity units.</summary>
        private const float PositionTolerance = 1e-3f;

        /// <summary>The MQTT harness installed for every test, which captures every published payload.</summary>
        private MqttTestHarness _harness;

        /// <summary>The root object parenting every object a test creates.</summary>
        private GameObject _root;

        /// <summary>The actor the task under test tracks and teleports.</summary>
        private ActorObject _actor;

        /// <summary>The task under test.</summary>
        private Task _task;

        /// <summary>The stimulus zone the corridor advance must re-arm, or null when a test omits the zones.</summary>
        private StimulusTriggerZone _stimulusZone;

        /// <summary>The occupancy zone the corridor advance must re-arm, or null when a test omits the zones.</summary>
        private OccupancyZone _occupancyZone;

        /// <summary>The occupancy guidance zone the corridor advance must re-arm, or null when omitted.</summary>
        private OccupancyGuidanceZone _occupancyGuidanceZone;

        /// <summary>Writes every staged template into the project's Configurations directory.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            StageTemplate(PairTemplateName, BuildPairTemplate());
            StageTemplate(SingleTemplateName, BuildSingleTemplate());
            StageTemplate(InvalidTemplateName, BuildInvalidTemplate());
        }

        /// <summary>Deletes every staged template and the import metadata Unity may have written for it.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            DeleteStagedTemplate(PairTemplateName);
            DeleteStagedTemplate(SingleTemplateName);
            DeleteStagedTemplate(InvalidTemplateName);
        }

        /// <summary>Installs the MQTT singleton and creates the root object every test hangs its objects on.</summary>
        [SetUp]
        public void SetUp()
        {
            _harness = MqttTestHarness.Create();
            _root = new GameObject("TaskPlayModeRoot");
        }

        /// <summary>Destroys every created object and removes the MQTT singleton.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
            _actor = null;
            _task = null;
            _stimulusZone = null;
            _occupancyZone = null;
            _occupancyGuidanceZone = null;

            _harness.Dispose();
            _harness = null;
        }

        /// <summary>Verifies that Unity's own Start places the actor on its corridor before the first Update.</summary>
        [UnityTest]
        public IEnumerator Start_UnityDrivenLifecycle_PlacesTheActorOnTheStartingCorridorXPosition()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, new Vector3(7f, 3f, 0f));

            yield return null;

            int[] sequence = SegmentSequence();
            int startingKey = ExpectedCorridorKey(sequence, 0);
            Assert.IsTrue(_task.enabled);
            Assert.AreEqual(startingKey * CorridorSpacingUnity, _actor.transform.position.x, PositionTolerance);
            Assert.AreEqual(3f, _actor.transform.position.y, PositionTolerance);
            Assert.AreEqual(0f, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(startingKey, CurrentCorridorKey());
        }

        /// <summary>Verifies that a task hosted away from the origin is moved back to it on the first frame.</summary>
        [UnityTest]
        public IEnumerator Start_TaskHostedAwayFromTheOrigin_MovesTheTaskBackToTheOrigin()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);
            _task.transform.position = new Vector3(1f, 2f, 3f);

            yield return null;

            Assert.AreEqual(Vector3.zero, _task.transform.position);
            Assert.IsTrue(_task.enabled);
        }

        /// <summary>Verifies that an absent template disables the component so Unity stops calling Update.</summary>
        [UnityTest]
        public IEnumerator Start_AbsentTemplateFile_DisablesTheComponentSoUpdateStopsRunning()
        {
            CreateTask(RelativeConfigPath(AbsentTemplateName), PairTrackLength, PairSeed, Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex("configuration YAML not found"));

            yield return null;

            Assert.IsFalse(_task.enabled);

            // The early return fires before the corridor map is allocated, so a surviving Update would dereference a
            // null map rather than move the actor. The null map plus the untouched actor position together prove the
            // disabled component stopped receiving the callback.
            Assert.IsNull(PrivateAccess.GetField<Array>(_task, "_corridorMap"));

            _actor.transform.position = new Vector3(0f, 0f, 10000f);
            yield return null;
            yield return null;

            Assert.AreEqual(10000f, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(0f, _actor.transform.position.x, PositionTolerance);
        }

        /// <summary>Verifies that an unset configuration path disables the component before any frame runs.</summary>
        [UnityTest]
        public IEnumerator Start_UnsetConfigPath_DisablesTheComponentSoUpdateStopsRunning()
        {
            CreateTask(null, PairTrackLength, PairSeed, Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex("configuration YAML not found"));

            yield return null;

            Assert.IsFalse(_task.enabled);

            _actor.transform.position = new Vector3(0f, 0f, 500f);
            yield return null;

            Assert.AreEqual(500f, _actor.transform.position.z, PositionTolerance);
        }

        /// <summary>Verifies that a configuration path carrying a leading separator still resolves.</summary>
        [UnityTest]
        public IEnumerator Start_LeadingSeparatorInTheConfigPath_StillResolvesTheStagedTemplate()
        {
            CreateTask($"/{RelativeConfigPath(PairTemplateName)}", PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            Assert.IsTrue(_task.enabled);
            Assert.AreEqual(PairTrialCount, PrivateAccess.GetField<int>(_task, "_trialCount"));
            Assert.AreEqual(PairDepth, PrivateAccess.GetField<int>(_task, "_depth"));
        }

        /// <summary>Verifies that a template failing validation disables the component on the first frame.</summary>
        [UnityTest]
        public IEnumerator Start_TemplateFailingValidation_DisablesTheComponentSoUpdateStopsRunning()
        {
            CreateTask(RelativeConfigPath(InvalidTemplateName), PairTrackLength, PairSeed, Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex("Failed to load task template"));

            yield return null;

            Assert.IsFalse(_task.enabled);

            _actor.transform.position = new Vector3(0f, 0f, 500f);
            yield return null;

            Assert.AreEqual(500f, _actor.transform.position.z, PositionTolerance);
        }

        /// <summary>Verifies that a track length yielding fewer segments than the depth disables the component.
        /// </summary>
        [UnityTest]
        public IEnumerator Start_TrackLengthShorterThanTheCorridorDepth_DisablesTheComponent()
        {
            CreateTask(RelativeConfigPath(SingleTemplateName), ShortSegmentLengthUnity, PairSeed, Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex("is too short for template"));

            yield return null;

            Assert.IsFalse(_task.enabled);
            Assert.AreEqual(1, SegmentSequence().Length);
        }

        /// <summary>Verifies that a track length yielding exactly the corridor depth leaves the task enabled.</summary>
        [UnityTest]
        public IEnumerator Start_TrackLengthYieldingExactlyTheCorridorDepth_LeavesTheComponentEnabled()
        {
            CreateTask(RelativeConfigPath(SingleTemplateName), ShortSegmentLengthUnity + 1f, PairSeed, Vector3.zero);

            yield return null;

            Assert.IsTrue(_task.enabled);
            Assert.AreEqual(PairDepth, SegmentSequence().Length);
        }

        /// <summary>Verifies that crossing the first segment teleports the actor onto the next corridor.</summary>
        [UnityTest]
        public IEnumerator Update_ActorCrossesTheFirstSegment_TeleportsToTheNextCorridorLanding()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            Assert.That(firstSegmentLength, Is.EqualTo(ShortSegmentLengthUnity).Or.EqualTo(LongSegmentLengthUnity));
            float departureZ = firstSegmentLength + AdvanceOvershoot;
            _actor.transform.position = new Vector3(_actor.transform.position.x, 0f, departureZ);

            yield return null;

            int expectedKey = ExpectedCorridorKey(sequence, 1);
            Assert.AreEqual(expectedKey * CorridorSpacingUnity, _actor.transform.position.x, PositionTolerance);
            Assert.AreEqual(departureZ - firstSegmentLength, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(AdvanceOvershoot, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(expectedKey, CurrentCorridorKey());
            Assert.AreEqual(1, CurrentSegmentIndex());
        }

        /// <summary>Verifies that an actor resting exactly on the segment boundary stays in its corridor.</summary>
        [UnityTest]
        public IEnumerator Update_ActorExactlyOnTheSegmentBoundary_KeepsTheActorInTheCurrentCorridor()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            int startingKey = ExpectedCorridorKey(sequence, 0);
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            _actor.transform.position = new Vector3(_actor.transform.position.x, 0f, firstSegmentLength);

            yield return null;
            yield return null;

            Assert.AreEqual(firstSegmentLength, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(startingKey * CorridorSpacingUnity, _actor.transform.position.x, PositionTolerance);
            Assert.AreEqual(0, CurrentSegmentIndex());
        }

        /// <summary>Verifies that an actor short of the segment boundary stays in its corridor.</summary>
        [UnityTest]
        public IEnumerator Update_ActorShortOfTheSegmentBoundary_KeepsTheActorInTheCurrentCorridor()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            _actor.transform.position = new Vector3(_actor.transform.position.x, 0f, firstSegmentLength - 0.5f);

            yield return null;

            Assert.AreEqual(firstSegmentLength - 0.5f, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(0, CurrentSegmentIndex());
        }

        /// <summary>Verifies that repeated crossings walk the generated corridor sequence in order.</summary>
        [UnityTest]
        public IEnumerator Update_ThreeConsecutiveCrossings_WalksTheGeneratedCorridorSequence()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float[] segmentLengths = SegmentLengths();
            Assert.Greater(sequence.Length, PairDepth + 3);

            for (int advance = 1; advance <= 3; advance++)
            {
                float firstSegmentLength = segmentLengths[sequence[advance - 1]];
                Vector3 position = _actor.transform.position;
                _actor.transform.position = new Vector3(position.x, 0f, firstSegmentLength + AdvanceOvershoot);

                yield return null;

                int expectedKey = ExpectedCorridorKey(sequence, advance);
                Assert.AreEqual(expectedKey * CorridorSpacingUnity, _actor.transform.position.x, PositionTolerance);
                Assert.AreEqual(AdvanceOvershoot, _actor.transform.position.z, PositionTolerance);
                Assert.AreEqual(expectedKey, CurrentCorridorKey());
                Assert.AreEqual(advance, CurrentSegmentIndex());
            }
        }

        /// <summary>Verifies that a corridor advance restores the per-lap state of every zone in the scene.</summary>
        [UnityTest]
        public IEnumerator Update_CorridorAdvance_RestoresThePerLapStateOfEveryZone()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);
            CreateZoneHierarchy();

            yield return null;

            _stimulusZone.isActive = false;
            _occupancyZone.isActive = false;
            _occupancyZone.occupancyMet = true;
            _occupancyZone.inZone = true;
            _occupancyGuidanceZone.inZone = true;
            PrivateAccess.SetField(_occupancyGuidanceZone, "_hasTriggered", true);
            Assert.IsTrue(_occupancyGuidanceZone.BrakeTriggered);

            // Runs the stopwatch the advance must stop and zero, so the post-advance reading rules out a reset that
            // silently skipped the timer rather than restating the value a never-started stopwatch already holds.
            Stopwatch occupancyTimer = PrivateAccess.GetField<Stopwatch>(_occupancyZone, "_occupancyTimer");
            occupancyTimer.Start();
            Assert.IsTrue(occupancyTimer.IsRunning);

            int[] sequence = SegmentSequence();
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            _actor.transform.position = new Vector3(_actor.transform.position.x, 0f, firstSegmentLength + 1f);

            yield return null;

            Assert.IsTrue(_stimulusZone.isActive);
            Assert.IsTrue(_occupancyZone.isActive);
            Assert.IsFalse(_occupancyZone.occupancyMet);
            Assert.IsFalse(_occupancyZone.inZone);
            Assert.IsFalse(_occupancyGuidanceZone.inZone);
            Assert.IsFalse(_occupancyGuidanceZone.BrakeTriggered);
            Assert.IsFalse(occupancyTimer.IsRunning);
            Assert.AreEqual(0L, OccupancyElapsedMilliseconds());
            Assert.AreEqual(0, _harness.CountOn(MQTTTopics.Stimulus));
        }

        /// <summary>Verifies that Unity runs every zone's Start before the first corridor advance reaches them.
        /// </summary>
        [UnityTest]
        public IEnumerator Start_ZonesInScene_RunsEveryZoneStartBeforeTheFirstFrameCompletes()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);
            CreateZoneHierarchy();
            _stimulusZone.isActive = false;
            Assert.IsNull(PrivateAccess.GetField<object>(_occupancyZone, "_occupancyTimer"));

            yield return null;

            Assert.IsTrue(_stimulusZone.isActive);
            Assert.IsTrue(_stimulusZone.enabled);
            Assert.IsTrue(_occupancyGuidanceZone.enabled);
            Assert.IsNotNull(PrivateAccess.GetField<object>(_occupancyZone, "_occupancyTimer"));
            Assert.AreEqual(0L, OccupancyElapsedMilliseconds());
            IResettable[] resettables = PrivateAccess.GetField<IResettable[]>(_task, "_resettables");
            Assert.AreEqual(3, resettables.Length);
        }

        /// <summary>Verifies that clearing the actor after startup stops the corridor from advancing.</summary>
        [UnityTest]
        public IEnumerator Update_ActorClearedAfterStartup_StopsAdvancingTheCorridor()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            _actor.transform.position = new Vector3(_actor.transform.position.x, 0f, firstSegmentLength + 100f);
            _task.actor = null;

            yield return null;
            yield return null;

            Assert.AreEqual(firstSegmentLength + 100f, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(0, CurrentSegmentIndex());
            Assert.AreEqual(ExpectedCorridorKey(sequence, 0), CurrentCorridorKey());
        }

        /// <summary>Verifies that a corridor key outside the map reports an error and skips the frame.</summary>
        [UnityTest]
        public IEnumerator Update_CorridorKeyOutsideTheMap_ReportsAnErrorAndSkipsTheFrame()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int corridorCount = CorridorMapLength();
            Assert.AreEqual(PairTrialCount * PairTrialCount, corridorCount);
            PrivateAccess.SetField(_task, "_currentCorridorKey", corridorCount);
            _actor.transform.position = new Vector3(0f, 0f, 1000f);
            LogAssert.Expect(LogType.Error, new Regex("Corridor key '4' out of bounds"));

            yield return null;

            Assert.AreEqual(1000f, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(0, CurrentSegmentIndex());

            // The key stays out of range, so every later frame would report the same error. Disabling the component
            // stops the loop before the fixture teardown reaches the next frame.
            _task.enabled = false;
        }

        /// <summary>Verifies that running past the generated sequence reports an error and holds the actor.</summary>
        [UnityTest]
        public IEnumerator Update_SequenceExhausted_ReportsAnErrorAndLeavesTheActorInPlace()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float firstSegmentLength = SegmentLengths()[sequence[0]];
            float departureZ = firstSegmentLength + AdvanceOvershoot;
            PrivateAccess.SetField(_task, "_currentSegmentIndex", sequence.Length - PairDepth);
            float departureX = _actor.transform.position.x;
            _actor.transform.position = new Vector3(departureX, 0f, departureZ);
            LogAssert.Expect(LogType.Error, new Regex("Animal ran through all generated segments"));

            yield return null;

            Assert.AreEqual(departureZ, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(departureX, _actor.transform.position.x, PositionTolerance);
            Assert.AreEqual(sequence.Length - PairDepth + 1, CurrentSegmentIndex());

            // The actor is still past the boundary, so every later frame would report the same error.
            _task.enabled = false;
        }

        /// <summary>Verifies that the last reachable segment index still completes one corridor advance.</summary>
        [UnityTest]
        public IEnumerator Update_LastReachableSegmentIndex_StillCompletesTheAdvance()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            float[] segmentLengths = SegmentLengths();
            float firstSegmentLength = segmentLengths[sequence[0]];
            PrivateAccess.SetField(_task, "_currentSegmentIndex", sequence.Length - PairDepth - 1);
            _actor.transform.position = new Vector3(
                _actor.transform.position.x,
                0f,
                firstSegmentLength + AdvanceOvershoot
            );

            yield return null;

            int expectedKey = sequence[1] * PairTrialCount + sequence[sequence.Length - 1];
            Assert.AreEqual(expectedKey, CurrentCorridorKey());
            Assert.AreEqual(expectedKey * CorridorSpacingUnity, _actor.transform.position.x, PositionTolerance);
            Assert.AreEqual(AdvanceOvershoot, _actor.transform.position.z, PositionTolerance);
            Assert.AreEqual(sequence.Length - PairDepth, CurrentSegmentIndex());
        }

        /// <summary>Verifies that a scene name request is answered with the name of the running scene.</summary>
        [UnityTest]
        public IEnumerator OnSceneNameTrigger_RunningScene_RepliesWithTheLoadedSceneName()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            _harness.PublishTrigger(MQTTTopics.SceneNameTrigger);

            Assert.AreEqual(1, _harness.CountOn(MQTTTopics.SceneName));
            Task.SceneNameMessage reply = _harness.LastMessageOn<Task.SceneNameMessage>(MQTTTopics.SceneName);
            string capturedSceneName = PrivateAccess.GetField<string>(_task, "_sceneName");
            Assert.IsNotEmpty(capturedSceneName);
            Assert.AreEqual(capturedSceneName, reply.name);
            Assert.AreEqual(SceneManager.GetActiveScene().name, reply.name);
        }

        /// <summary>Verifies that a cue sequence request is answered with the flattened cue codes.</summary>
        [UnityTest]
        public IEnumerator OnCueSequenceTrigger_RunningTask_RepliesWithTheFlattenedCueSequence()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            int[] sequence = SegmentSequence();
            string[] trialNames = PrivateAccess.GetField<string[]>(_task, "_trialNames");
            List<byte> expected = new List<byte>(sequence.Length);
            for (int index = 0; index < sequence.Length; index++)
            {
                bool isShortTrial = string.Equals(trialNames[sequence[index]], "Short", StringComparison.Ordinal);
                expected.Add(isShortTrial ? (byte)CueCodeA : (byte)CueCodeB);
            }

            _harness.PublishTrigger(MQTTTopics.CueSequenceTrigger);

            Assert.AreEqual(1, _harness.CountOn(MQTTTopics.CueSequence));
            Task.SequenceMessage reply = _harness.LastMessageOn<Task.SequenceMessage>(MQTTTopics.CueSequence);
            CollectionAssert.AreEqual(expected, reply.cueSequence);
        }

        /// <summary>Verifies that the interaction requirement toggle is applied while the task is running.</summary>
        [UnityTest]
        public IEnumerator OnRequireInteraction_ToggledWhileRunning_TracksThePublishedPayload()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            Assert.IsFalse(_task.requireInteraction);

            _harness.Publish(MQTTTopics.RequireInteraction, new Task.BoolMessage { value = true });
            Assert.IsTrue(_task.requireInteraction);

            _harness.Publish(MQTTTopics.RequireInteraction, new Task.BoolMessage { value = false });
            Assert.IsFalse(_task.requireInteraction);
        }

        /// <summary>Verifies that the wait requirement toggle is applied while the task is running.</summary>
        [UnityTest]
        public IEnumerator OnRequireWait_ToggledWhileRunning_TracksThePublishedPayload()
        {
            CreateTask(RelativeConfigPath(PairTemplateName), PairTrackLength, PairSeed, Vector3.zero);

            yield return null;

            Assert.IsFalse(_task.requireWait);

            _harness.Publish(MQTTTopics.RequireWait, new Task.BoolMessage { value = true });
            Assert.IsTrue(_task.requireWait);

            _harness.Publish(MQTTTopics.RequireWait, new Task.BoolMessage { value = false });
            Assert.IsFalse(_task.requireWait);
        }

        /// <summary>Builds the two-trial corridor template whose two segments carry different lengths.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildPairTemplate()
        {
            TemplateYaml template = new TemplateYaml();
            CueYaml shortCue = CueYaml.Named("A", CueCodeA);
            shortCue.lengthCm = 100f;
            shortCue.texture = StagedTextureName;
            template.cues.Add(shortCue);
            CueYaml longCue = CueYaml.Named("B", CueCodeB);
            longCue.lengthCm = 200f;
            longCue.texture = StagedTextureName;
            template.cues.Add(longCue);
            template.trials.Add(TrialYaml.Named("Short", "A"));
            template.trials.Add(TrialYaml.Named("Long", "B"));
            template.vrEnvironment.corridorSpacingCm = 500f;
            template.vrEnvironment.segmentsPerCorridor = PairDepth;
            template.vrEnvironment.cmPerUnityUnit = 10f;
            return template;
        }

        /// <summary>Builds the single-trial template whose maze generation is fully deterministic.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildSingleTemplate()
        {
            TemplateYaml template = new TemplateYaml();
            CueYaml cue = CueYaml.Named("A", CueCodeA);
            cue.lengthCm = 100f;
            cue.texture = StagedTextureName;
            template.cues.Add(cue);
            template.trials.Add(TrialYaml.Named("Only", "A"));
            template.vrEnvironment.corridorSpacingCm = 500f;
            template.vrEnvironment.segmentsPerCorridor = PairDepth;
            template.vrEnvironment.cmPerUnityUnit = 10f;
            return template;
        }

        /// <summary>Builds a template that omits its cues section and therefore fails validation.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildInvalidTemplate()
        {
            TemplateYaml template = BuildPairTemplate();
            template.includeCuesSection = false;
            return template;
        }

        /// <summary>Returns the absolute path a staged template of the given name occupies.</summary>
        /// <remarks>
        /// Task.Start resolves its template as Path.Combine(Application.dataPath, configPath), so every staged template
        /// lives under the project's own Configurations directory.
        /// </remarks>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <returns>The absolute path of the staged template file.</returns>
        private static string AbsoluteTemplatePath(string templateName)
        {
            return Path.Combine(Application.dataPath, "InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");
        }

        /// <summary>Returns the task-relative configuration path pointing at a staged template.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <returns>The path assigned to a task's configPath field.</returns>
        private static string RelativeConfigPath(string templateName)
        {
            return $"{ConfigurationsDirectory}/{templateName}.yaml";
        }

        /// <summary>Writes a template into the project's Configurations directory.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <param name="template">The builder whose rendered document is written.</param>
        private static void StageTemplate(string templateName, TemplateYaml template)
        {
            File.WriteAllText(AbsoluteTemplatePath(templateName), template.Build());
        }

        /// <summary>Deletes a staged template and the import metadata Unity may have written for it.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        private static void DeleteStagedTemplate(string templateName)
        {
            string absolutePath = AbsoluteTemplatePath(templateName);
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

        /// <summary>
        /// Returns the base-trialCount corridor key of the segment window starting at the supplied offset, computed
        /// independently of the runtime encoder so a changed encoding fails the comparison.
        /// </summary>
        /// <param name="sequence">The generated segment index sequence.</param>
        /// <param name="offset">The index of the window's first segment.</param>
        /// <returns>The corridor key the runtime lookup must produce for that window.</returns>
        private static int ExpectedCorridorKey(int[] sequence, int offset)
        {
            int key = 0;
            for (int index = 0; index < PairDepth; index++)
            {
                key = key * PairTrialCount + sequence[offset + index];
            }
            return key;
        }

        /// <summary>Creates the actor and the task under test, leaving Unity to run their Start callbacks.</summary>
        /// <param name="configPath">The value assigned to the task's configPath field.</param>
        /// <param name="trackLength">The value assigned to the task's trackLength field.</param>
        /// <param name="trackSeed">The value assigned to the task's trackSeed field.</param>
        /// <param name="actorPosition">The world position the actor starts at.</param>
        private void CreateTask(string configPath, float trackLength, int trackSeed, Vector3 actorPosition)
        {
            GameObject actorObject = new GameObject("Actor");
            actorObject.transform.SetParent(_root.transform);
            actorObject.transform.position = actorPosition;
            _actor = actorObject.AddComponent<ActorObject>();

            GameObject taskObject = new GameObject("Task");
            taskObject.transform.SetParent(_root.transform);
            _task = taskObject.AddComponent<Task>();
            _task.configPath = configPath;
            _task.trackLength = trackLength;
            _task.trackSeed = trackSeed;
            _task.actor = _actor;
        }

        /// <summary>Creates the three-zone hierarchy the corridor advance must restore each lap.</summary>
        private void CreateZoneHierarchy()
        {
            GameObject stimulusObject = new GameObject("StimulusTriggerZone");
            stimulusObject.transform.SetParent(_root.transform);
            _stimulusZone = stimulusObject.AddComponent<StimulusTriggerZone>();
            _stimulusZone.triggerMode = TriggerMode.OccupancyArm;
            _stimulusZone.trialName = "PlayModeTrial";

            GameObject occupancyObject = new GameObject("OccupancyRegion");
            occupancyObject.transform.SetParent(stimulusObject.transform);
            _occupancyZone = occupancyObject.AddComponent<OccupancyZone>();
            _occupancyZone.occupancyDurationMs = 5000f;

            GameObject occupancyGuidanceObject = new GameObject("OccupancyGuidanceRegion");
            occupancyGuidanceObject.transform.SetParent(occupancyObject.transform);
            _occupancyGuidanceZone = occupancyGuidanceObject.AddComponent<OccupancyGuidanceZone>();
        }

        /// <summary>Returns the corridor key the task currently reads its landing position from.</summary>
        /// <returns>The cached corridor key.</returns>
        private int CurrentCorridorKey()
        {
            return PrivateAccess.GetField<int>(_task, "_currentCorridorKey");
        }

        /// <summary>Returns the task's index into the generated segment sequence.</summary>
        /// <returns>The current segment index.</returns>
        private int CurrentSegmentIndex()
        {
            return PrivateAccess.GetField<int>(_task, "_currentSegmentIndex");
        }

        /// <summary>Returns the maze's segment index sequence.</summary>
        /// <returns>The segment index sequence.</returns>
        private int[] SegmentSequence()
        {
            return PrivateAccess.GetField<int[]>(_task, "_segmentSequenceArray");
        }

        /// <summary>Returns the per-trial segment lengths in Unity units.</summary>
        /// <returns>The segment lengths, indexed positionally by trial.</returns>
        private float[] SegmentLengths()
        {
            return PrivateAccess.GetField<float[]>(_task, "_segmentLengths");
        }

        /// <summary>Returns the number of corridor combinations the task mapped at startup.</summary>
        /// <remarks>
        /// The map is an array of value tuples, so the read goes through the non-generic Array type rather than
        /// restating the tuple shape the runtime field declares.
        /// </remarks>
        /// <returns>The corridor map length.</returns>
        private int CorridorMapLength()
        {
            return PrivateAccess.GetField<Array>(_task, "_corridorMap").Length;
        }

        /// <summary>Returns the occupancy zone's elapsed timer reading in milliseconds.</summary>
        /// <returns>The elapsed milliseconds since the occupancy timer last restarted.</returns>
        private long OccupancyElapsedMilliseconds()
        {
            return (long)PrivateAccess.Invoke(_occupancyZone, "GetElapsedMilliseconds");
        }
    }
}
