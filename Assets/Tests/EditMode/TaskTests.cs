/// <summary>
/// Verifies the behavior of the Task class.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using SL.Config;
using SL.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the Task class.</summary>
    /// <remarks>
    /// Task.Start resolves its template as Path.Combine(Application.dataPath, configPath), so every template a test
    /// loads is staged inside the project's own Configurations directory under a "ZZTest_" name and deleted again in
    /// TearDown.
    /// </remarks>
    [TestFixture]
    public class TaskTests
    {
        /// <summary>The dataPath-relative directory that every staged test template is written into.</summary>
        private const string ConfigurationsDirectory = "InfiniteCorridorTask/Configurations";

        /// <summary>The name of the two-trial corridor template that most tests load.</summary>
        private const string PairTemplateName = "ZZTest_Pair";

        /// <summary>The name of the single-trial, single-segment corridor template.</summary>
        private const string SingleTemplateName = "ZZTest_Single";

        /// <summary>The name of the template whose transitions absorb every walk into one trial.</summary>
        private const string AbsorbingTemplateName = "ZZTest_Absorbing";

        /// <summary>The name of the template that fails ConfigLoader validation.</summary>
        private const string InvalidTemplateName = "ZZTest_Invalid";

        /// <summary>The name of the template whose corridor depth overruns the corridor map allocation limit.</summary>
        private const string DeepTemplateName = "ZZTest_Deep";

        /// <summary>The texture every staged cue references, which already ships under Textures.</summary>
        /// <remarks>
        /// ConfigLoader resolves a cue texture as "&lt;template directory&gt;/../Textures", so a template staged in the
        /// Configurations directory reaches the project's own texture folder.
        /// </remarks>
        private const string StagedTextureName = "Gray Cue 2x1.png";

        /// <summary>The corridor depth the pair template declares.</summary>
        private const int PairDepth = 3;

        /// <summary>The number of trials the pair template declares.</summary>
        private const int PairTrialCount = 2;

        /// <summary>The number of corridor map entries the pair template produces (2 raised to the depth).</summary>
        private const int PairCorridorCount = 8;

        /// <summary>The corridor depth whose two-trial combination count overruns the corridor map limit.</summary>
        private const int ExcessiveCorridorDepth = 29;

        /// <summary>The corridor spacing of every staged template in Unity units (20 cm over 10 cm per unit).</summary>
        private const float CorridorSpacingUnity = 2f;

        /// <summary>The Unity length of the pair template's two-cue "Long" segment (3 units plus 6 units).</summary>
        private const float LongSegmentLengthUnity = 9f;

        /// <summary>The Unity length of the pair template's single-cue "Short" segment (60 cm over 10).</summary>
        private const float ShortSegmentLengthUnity = 6f;

        /// <summary>The Unity length of the single template's only segment (60 cm over 10 cm per unit).</summary>
        private const float SingleSegmentLengthUnity = 6f;

        /// <summary>The byte code the staged templates assign to cue "A".</summary>
        private const byte CueCodeA = 1;

        /// <summary>The byte code the staged templates assign to cue "B".</summary>
        private const byte CueCodeB = 2;

        /// <summary>
        /// The seed the pair-template tests generate their maze with, except the generation-comparison tests that vary
        /// it deliberately.
        /// </summary>
        private const int PairSeed = 4242;

        /// <summary>The track length every pair-template traversal test generates its maze with.</summary>
        private const float PairTrackLength = 90f;

        /// <summary>The track length the generation-comparison tests use, long enough to make collisions absurd.
        /// </summary>
        private const float LongTrackLength = 300f;

        /// <summary>The seed every transition-sampler test draws its cumulative bucket with.</summary>
        private const int SamplerSeed = 11;

        /// <summary>The MQTT harness installed for every test, which captures every published payload.</summary>
        private MqttTestHarness _harness;

        /// <summary>The GameObjects the running test created, destroyed in reverse creation order.</summary>
        private List<GameObject> _createdObjects;

        /// <summary>The template names staged under the project's Configurations directory.</summary>
        private List<string> _stagedTemplateNames;

        /// <summary>Installs the MQTT singleton and resets the per-test bookkeeping lists.</summary>
        [SetUp]
        public void SetUp()
        {
            _harness = MqttTestHarness.Create();
            _createdObjects = new List<GameObject>();
            _stagedTemplateNames = new List<string>();
        }

        /// <summary>Destroys every created object and deletes every staged template file.</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = _createdObjects.Count - 1; index >= 0; index--)
            {
                if (_createdObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[index]);
                }
            }
            _createdObjects.Clear();

            foreach (string templateName in _stagedTemplateNames)
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
            _stagedTemplateNames.Clear();

            _harness.Dispose();
        }

        /// <summary>Verifies that the nondeterministic-seed sentinel is negative one.</summary>
        [Test]
        public void RandomSeedSentinel_Constant_EqualsNegativeOne()
        {
            Assert.AreEqual(-1, Task.RandomSeedSentinel);
        }

        /// <summary>Verifies that the default pre-generated track length is fifteen thousand Unity units.</summary>
        [Test]
        public void DefaultTrackLength_Constant_EqualsFifteenThousand()
        {
            Assert.AreEqual(15000f, Task.DefaultTrackLength);
        }

        /// <summary>Verifies that a freshly added component starts at the default track length.</summary>
        [Test]
        public void TrackLength_FreshComponent_DefaultsToTheDeclaredConstant()
        {
            GameObject host = new GameObject("Task");
            _createdObjects.Add(host);

            Task task = host.AddComponent<Task>();

            Assert.AreEqual(Task.DefaultTrackLength, task.trackLength);
        }

        /// <summary>Verifies that a freshly added component starts on the nondeterministic seed sentinel.</summary>
        [Test]
        public void TrackSeed_FreshComponent_DefaultsToTheSentinel()
        {
            GameObject host = new GameObject("Task");
            _createdObjects.Add(host);

            Task task = host.AddComponent<Task>();

            Assert.AreEqual(Task.RandomSeedSentinel, task.trackSeed);
        }

        /// <summary>Verifies that Start moves a displaced task back to the world origin.</summary>
        [Test]
        public void Start_NonZeroTransformPosition_ResetsTaskToTheOrigin()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task task = CreateTask(configPath, PairTrackLength, PairSeed, actor: null);
            task.transform.position = new Vector3(5f, 2f, 3f);

            PrivateAccess.Invoke(task, "Start");

            Assert.AreEqual(0f, task.transform.position.x);
            Assert.AreEqual(0f, task.transform.position.y);
            Assert.AreEqual(0f, task.transform.position.z);
        }

        /// <summary>Verifies that a null configuration path logs an error and disables the component.</summary>
        [Test]
        public void Start_NullConfigPath_LogsErrorAndDisablesTask()
        {
            Task task = CreateTask(configPath: null, trackLength: PairTrackLength, trackSeed: PairSeed, actor: null);
            LogAssert.Expect(LogType.Error, new Regex("configuration YAML not found"));

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
        }

        /// <summary>Verifies that an empty configuration path logs an error and disables the component.</summary>
        [Test]
        public void Start_EmptyConfigPath_LogsErrorAndDisablesTask()
        {
            Task task = CreateTask(string.Empty, PairTrackLength, PairSeed, actor: null);
            LogAssert.Expect(LogType.Error, new Regex("configuration YAML not found"));

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
        }

        /// <summary>Verifies that a configuration path naming no file logs an error and disables the component.
        /// </summary>
        [Test]
        public void Start_MissingConfigFile_LogsErrorAndDisablesTask()
        {
            Task task = CreateTask(
                $"{ConfigurationsDirectory}/ZZTest_Absent.yaml",
                PairTrackLength,
                PairSeed,
                actor: null
            );
            LogAssert.Expect(LogType.Error, new Regex("configuration YAML not found"));

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
        }

        /// <summary>Verifies that a leading forward slash is stripped instead of making the path absolute.</summary>
        [Test]
        public void Start_LeadingSlashConfigPath_ResolvesUnderTheAssetsFolder()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task task = CreateTask($"/{configPath}", PairTrackLength, PairSeed, actor: null);

            PrivateAccess.Invoke(task, "Start");

            Assert.IsTrue(task.enabled);
            Assert.AreEqual(PairTemplateName, LoadedTemplateName(task));
        }

        /// <summary>Verifies that a leading backslash is stripped instead of making the path absolute.</summary>
        [Test]
        public void Start_LeadingBackslashConfigPath_ResolvesUnderTheAssetsFolder()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task task = CreateTask($"\\{configPath}", PairTrackLength, PairSeed, actor: null);

            PrivateAccess.Invoke(task, "Start");

            Assert.IsTrue(task.enabled);
            Assert.AreEqual(PairTemplateName, LoadedTemplateName(task));
        }

        /// <summary>Verifies that a template failing validation logs an error and disables the component.</summary>
        [Test]
        public void Start_TemplateFailsValidation_LogsErrorAndDisablesTask()
        {
            string configPath = StageTemplate(InvalidTemplateName, BuildInvalidTemplate());
            Task task = CreateTask(configPath, PairTrackLength, PairSeed, actor: null);
            LogAssert.Expect(LogType.Error, new Regex("Failed to load task template from YAML file"));

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
        }

        /// <summary>Verifies that a combination count above the allocation limit logs an error and disables the task.
        /// </summary>
        /// <remarks>
        /// Two trials over a depth of 29 encode 536870912 corridors, which is twice the corridor map limit, so the
        /// guard rejects the template rather than requesting the four gigabyte array the count asks for.
        /// </remarks>
        [Test]
        public void Start_CombinationCountAboveTheAllocationLimit_LogsErrorAndDisablesTask()
        {
            string configPath = StageTemplate(DeepTemplateName, BuildDeepTemplate());
            Task task = CreateTask(configPath, PairTrackLength, PairSeed, actor: null);
            LogAssert.Expect(
                LogType.Error,
                new Regex($"declares {PairTrialCount} trials over a corridor depth of {ExcessiveCorridorDepth}")
            );

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
            Assert.IsNull(PrivateAccess.GetField<Array>(task, "_corridorMap"));
        }

        /// <summary>Verifies that a sequence shorter than the corridor depth logs an error and disables the task.
        /// </summary>
        [Test]
        public void Start_SequenceShorterThanCorridorDepth_LogsErrorAndDisablesTask()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task task = CreateTask(configPath, trackLength: 1f, trackSeed: PairSeed, actor: null);
            LogAssert.Expect(LogType.Error, new Regex("is too short for template"));

            PrivateAccess.Invoke(task, "Start");

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(1, SegmentSequence(task).Length);
        }

        /// <summary>Verifies that a sequence exactly as long as the corridor depth leaves the task enabled.</summary>
        [Test]
        public void Start_SequenceLengthEqualToCorridorDepth_LeavesTaskEnabled()
        {
            string configPath = StageTemplate(SingleTemplateName, BuildSingleTemplate());
            Task task = CreateTask(configPath, trackLength: 1f, trackSeed: 7, actor: null);

            PrivateAccess.Invoke(task, "Start");

            Assert.IsTrue(task.enabled);
            Assert.AreEqual(1, PrivateAccess.GetField<int>(task, "_depth"));
            Assert.AreEqual(1, SegmentSequence(task).Length);
        }

        /// <summary>Verifies that a well-formed template leaves the component enabled.</summary>
        [Test]
        public void Start_ValidTemplate_LeavesTaskEnabled()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            Assert.IsTrue(task.enabled);
        }

        /// <summary>Verifies that Start reads the trial names in the order the template declares them.</summary>
        [Test]
        public void Start_ValidTemplate_ReadsTrialNamesInTemplateOrder()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            string[] trialNames = PrivateAccess.GetField<string[]>(task, "_trialNames");
            Assert.AreEqual(PairTrialCount, trialNames.Length);
            Assert.AreEqual("Long", trialNames[0]);
            Assert.AreEqual("Short", trialNames[1]);
            Assert.AreEqual(PairTrialCount, PrivateAccess.GetField<int>(task, "_trialCount"));
        }

        /// <summary>Verifies that Start converts each trial's cue sequence into a Unity-unit segment length.</summary>
        [Test]
        public void Start_ValidTemplate_ConvertsSegmentLengthsToUnityUnits()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            float[] segmentLengths = SegmentLengths(task);
            Assert.AreEqual(PairTrialCount, segmentLengths.Length);
            Assert.AreEqual(LongSegmentLengthUnity, segmentLengths[TrialIndex(task, "Long")]);
            Assert.AreEqual(ShortSegmentLengthUnity, segmentLengths[TrialIndex(task, "Short")]);
        }

        /// <summary>Verifies that the corridor map holds one entry per trial-count-to-the-depth combination.</summary>
        [Test]
        public void Start_ValidTemplate_SizesCorridorMapAsTrialCountToTheDepth()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            Assert.AreEqual(PairDepth, PrivateAccess.GetField<int>(task, "_depth"));
            Assert.AreEqual(IntegerPower(PairTrialCount, PairDepth), CorridorMap(task).Length);
            Assert.AreEqual(PairCorridorCount, CorridorMap(task).Length);
        }

        /// <summary>Verifies that corridor map entry i sits at i times the corridor spacing along x.</summary>
        [Test]
        public void Start_ValidTemplate_SpacesCorridorMapEntriesByCorridorSpacing()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            (float xPosition, float firstSegmentLength)[] corridorMap = CorridorMap(task);
            for (int index = 0; index < corridorMap.Length; index++)
            {
                Assert.AreEqual(index * CorridorSpacingUnity, corridorMap[index].xPosition);
            }
        }

        /// <summary>Verifies that each corridor map entry caches the length of its leading segment.</summary>
        [Test]
        public void Start_ValidTemplate_StoresEachCorridorsFirstSegmentLength()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            float[] segmentLengths = SegmentLengths(task);
            (float xPosition, float firstSegmentLength)[] corridorMap = CorridorMap(task);
            for (int index = 0; index < corridorMap.Length; index++)
            {
                int leadingDigit = index / IntegerPower(PairTrialCount, PairDepth - 1) % PairTrialCount;
                Assert.AreEqual(segmentLengths[leadingDigit], corridorMap[index].firstSegmentLength);
            }
        }

        /// <summary>Verifies that Start seeds the corridor window with the first depth segments of the maze.</summary>
        [Test]
        public void Start_ValidTemplate_SeedsCorridorWindowWithTheFirstDepthSegments()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            int[] sequence = SegmentSequence(task);
            List<int> window = CurrentSegment(task);
            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(PairDepth, window.Count);
            for (int index = 0; index < PairDepth; index++)
            {
                Assert.AreEqual(sequence[index], window[index]);
            }
            Assert.AreEqual(EncodeKey(sequence, 0, PairDepth, PairTrialCount), CurrentKey(task));
        }

        /// <summary>Verifies that Start teleports the actor onto the x position of the first corridor.</summary>
        [Test]
        public void Start_ActorAssigned_TeleportsActorToTheFirstCorridorXPosition()
        {
            ActorObject actor = CreateActor(new Vector3(7f, 1.5f, 2.5f));
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);

            int[] sequence = SegmentSequence(task);
            int expectedKey = EncodeKey(sequence, 0, PairDepth, PairTrialCount);
            Assert.AreEqual(expectedKey * CorridorSpacingUnity, actor.transform.position.x);
            Assert.AreEqual(1.5f, actor.transform.position.y);
            Assert.AreEqual(2.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that Start leaves an unassigned actor's transform untouched.</summary>
        [Test]
        public void Start_NoActor_LeavesTheActorObjectPositionUnchanged()
        {
            ActorObject actor = CreateActor(new Vector3(7f, 1.5f, 2.5f));
            Task task = CreateTask(
                StageTemplate(PairTemplateName, BuildPairTemplate()),
                PairTrackLength,
                PairSeed,
                actor: null
            );

            PrivateAccess.Invoke(task, "Start");

            Assert.AreEqual(7f, actor.transform.position.x);
            Assert.AreEqual(1.5f, actor.transform.position.y);
            Assert.AreEqual(2.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that the corridor key reads the segment indices as base-trial-count digits.</summary>
        [Test]
        public void ComputeCorridorKey_BaseTwoDepthThree_ReadsSegmentsAsBaseTrialCountDigits()
        {
            Task task = CreateBareTask(trialCount: 2);

            int key = (int)PrivateAccess.Invoke(task, "ComputeCorridorKey", new List<int> { 1, 0, 1 });

            Assert.AreEqual(5, key);
        }

        /// <summary>Verifies that the corridor key of a base-three corridor weights each digit positionally.</summary>
        [Test]
        public void ComputeCorridorKey_BaseThreeDepthThree_WeightsDigitsPositionally()
        {
            Task task = CreateBareTask(trialCount: 3);

            int key = (int)PrivateAccess.Invoke(task, "ComputeCorridorKey", new List<int> { 1, 2, 0 });

            Assert.AreEqual(15, key);
        }

        /// <summary>Verifies that a depth-one corridor key equals the single segment index itself.</summary>
        [Test]
        public void ComputeCorridorKey_DepthOne_ReturnsTheSegmentIndex()
        {
            Task task = CreateBareTask(trialCount: 4);

            int key = (int)PrivateAccess.Invoke(task, "ComputeCorridorKey", new List<int> { 3 });

            Assert.AreEqual(3, key);
        }

        /// <summary>Verifies that an empty segment list encodes to the zeroth corridor.</summary>
        [Test]
        public void ComputeCorridorKey_EmptySegmentList_ReturnsZero()
        {
            Task task = CreateBareTask(trialCount: 5);

            int key = (int)PrivateAccess.Invoke(task, "ComputeCorridorKey", new List<int>());

            Assert.AreEqual(0, key);
        }

        /// <summary>Verifies that the encoding inverts the corridor-map build loop for every map index.</summary>
        [Test]
        public void ComputeCorridorKey_EveryCorridorMapIndex_RoundTripsAcrossBasesAndDepths()
        {
            Task task = CreateBareTask(trialCount: 2);

            AssertKeyRoundTrip(task, trialCount: 2, depth: 1);
            AssertKeyRoundTrip(task, trialCount: 2, depth: 3);
            AssertKeyRoundTrip(task, trialCount: 3, depth: 2);
            AssertKeyRoundTrip(task, trialCount: 4, depth: 1);
            AssertKeyRoundTrip(task, trialCount: 5, depth: 3);
        }

        /// <summary>Verifies that Update returns before touching corridor state when no actor is assigned.</summary>
        [Test]
        public void Update_ActorNullOnUnstartedTask_ReturnsWithoutThrowing()
        {
            Task task = CreateTask(configPath: null, trackLength: PairTrackLength, trackSeed: PairSeed, actor: null);

            Assert.DoesNotThrow(() => PrivateAccess.Invoke(task, "Update"));
        }

        /// <summary>Verifies that clearing the actor after Start stops the corridor advance entirely.</summary>
        [Test]
        public void Update_ActorClearedAfterStart_LeavesSegmentIndexUnchanged()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);
            task.actor = null;

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
        }

        /// <summary>Verifies that a negative corridor key logs an error and disables the task.</summary>
        [Test]
        public void Update_NegativeCorridorKey_LogsErrorAndDisablesTask()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);
            PrivateAccess.SetField(task, "_currentCorridorKey", -1);
            LogAssert.Expect(LogType.Error, new Regex("Task: Corridor key '-1' out of bounds"));

            PrivateAccess.Invoke(task, "Update");

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
        }

        /// <summary>Verifies that a corridor key equal to the map length logs an error and disables the task.</summary>
        [Test]
        public void Update_CorridorKeyAtMapLength_LogsErrorAndDisablesTask()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);
            PrivateAccess.SetField(task, "_currentCorridorKey", PairCorridorCount);
            LogAssert.Expect(LogType.Error, new Regex("Task: Corridor key '8' out of bounds"));

            PrivateAccess.Invoke(task, "Update");

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
        }

        /// <summary>Verifies that an out-of-bounds corridor key reports its error on one frame only.</summary>
        /// <remarks>
        /// The key stays out of range once it is corrupted, so a guard that only returned would log the same error on
        /// every later frame. The second frame is driven through the enabled check the player loop applies.
        /// </remarks>
        [Test]
        public void Update_CorridorKeyOutOfBoundsOnTwoFrames_ReportsTheErrorOnce()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);
            PrivateAccess.SetField(task, "_currentCorridorKey", PairCorridorCount);
            LogAssert.Expect(LogType.Error, new Regex("Task: Corridor key '8' out of bounds"));

            InvokeUpdateWhileEnabled(task);
            InvokeUpdateWhileEnabled(task);

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
        }

        /// <summary>Verifies that the last valid corridor key is accepted rather than rejected as out of range.
        /// </summary>
        [Test]
        public void Update_CorridorKeyAtLastMapIndex_AdvancesWithoutLoggingAnError()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            PrivateAccess.SetField(task, "_currentCorridorKey", PairCorridorCount - 1);
            (float xPosition, float firstSegmentLength)[] corridorMap = CorridorMap(task);
            SetActorZ(actor, corridorMap[PairCorridorCount - 1].firstSegmentLength + 0.5f);

            PrivateAccess.Invoke(task, "Update");

            // An unexpected Debug.LogError already fails the test on its own, so the advance itself is what this
            // pins. The wound-back z proves the corridor body ran instead of the bounds guard returning early.
            Assert.AreEqual(1, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(0.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that an actor short of the first segment's end does not advance the corridor.</summary>
        [Test]
        public void Update_ActorShortOfFirstSegmentLength_DoesNotAdvance()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            float firstSegmentLength = FirstSegmentLength(task);
            SetActorZ(actor, firstSegmentLength - 0.5f);

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(firstSegmentLength - 0.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that an actor exactly on the first segment's end does not advance the corridor.</summary>
        [Test]
        public void Update_ActorExactlyAtFirstSegmentLength_DoesNotAdvance()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            float firstSegmentLength = FirstSegmentLength(task);
            SetActorZ(actor, firstSegmentLength);

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(0, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(firstSegmentLength, actor.transform.position.z);
        }

        /// <summary>Verifies that the advance subtracts exactly the first segment's length from the actor's z.
        /// </summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_SubtractsFirstSegmentLengthFromZ()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(0.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that the advance moves the sequence index forward by exactly one segment.</summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_AdvancesSegmentIndexByOne()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(1, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
        }

        /// <summary>Verifies that the advance slides the corridor window forward by one segment.</summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_SlidesCorridorWindowByOneSegment()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            int[] sequence = SegmentSequence(task);
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            List<int> window = CurrentSegment(task);
            Assert.AreEqual(PairDepth, window.Count);
            for (int index = 0; index < PairDepth; index++)
            {
                Assert.AreEqual(sequence[index + 1], window[index]);
            }
        }

        /// <summary>Verifies that the advance recomputes the key and teleports the actor to the new corridor.</summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_MovesActorToTheNewCorridorXPosition()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            int[] sequence = SegmentSequence(task);
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            int expectedKey = EncodeKey(sequence, 1, PairDepth, PairTrialCount);
            Assert.AreEqual(expectedKey, CurrentKey(task));
            Assert.AreEqual(expectedKey * CorridorSpacingUnity, actor.transform.position.x);
        }

        /// <summary>Verifies that the advance resets every zone the task collected at startup.</summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_ResetsEveryResettableZone()
        {
            StimulusTriggerZone stimulusZone = CreateComponent<StimulusTriggerZone>("StimulusTriggerZone");
            OccupancyZone occupancyZone = CreateComponent<OccupancyZone>("OccupancyRegion");
            OccupancyGuidanceZone guidanceZone = CreateComponent<OccupancyGuidanceZone>("OccupancyGuidanceRegion");
            PrivateAccess.Invoke(occupancyZone, "Start");
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            stimulusZone.isActive = false;
            occupancyZone.isActive = false;
            occupancyZone.occupancyMet = true;
            occupancyZone.inZone = true;
            PrivateAccess.SetField(guidanceZone, "_hasTriggered", true);
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            Assert.IsTrue(stimulusZone.isActive);
            Assert.IsTrue(occupancyZone.isActive);
            Assert.IsFalse(occupancyZone.occupancyMet);
            Assert.IsFalse(occupancyZone.inZone);
            Assert.IsFalse(guidanceZone.BrakeTriggered);
        }

        /// <summary>Verifies that the advance collects a standalone GuidanceZone and clears its in-zone flag.
        /// </summary>
        [Test]
        public void Update_ActorPastFirstSegmentLength_ResetsAStandaloneGuidanceZone()
        {
            GuidanceZone guidanceZone = CreateComponent<GuidanceZone>("GuidanceRegion");
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            guidanceZone.inZone = true;
            MoveActorPastFirstSegment(task, actor);

            PrivateAccess.Invoke(task, "Update");

            CollectionAssert.Contains(PrivateAccess.GetField<IResettable[]>(task, "_resettables"), guidanceZone);
            Assert.IsFalse(guidanceZone.inZone);
        }

        /// <summary>Verifies that the last sequence index still leaving a full corridor window advances.</summary>
        [Test]
        public void Update_LastReachableSegmentIndex_StillAdvances()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            int[] sequence = SegmentSequence(task);
            PrivateAccess.SetField(task, "_currentSegmentIndex", sequence.Length - PairDepth - 1);
            SetActorZ(actor, FirstSegmentLength(task) + 0.5f);

            PrivateAccess.Invoke(task, "Update");

            List<int> window = CurrentSegment(task);
            Assert.AreEqual(sequence.Length - PairDepth, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(sequence[sequence.Length - 1], window[PairDepth - 1]);
        }

        /// <summary>Verifies that running past the generated sequence logs an error and disables the task.</summary>
        [Test]
        public void Update_SequenceExhausted_LogsErrorAndDisablesTask()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            int[] sequence = SegmentSequence(task);
            PrivateAccess.SetField(task, "_currentSegmentIndex", sequence.Length - PairDepth);
            SetActorZ(actor, 100f);
            Vector3 positionBefore = actor.transform.position;
            LogAssert.Expect(LogType.Error, new Regex("Animal ran through all generated segments"));

            PrivateAccess.Invoke(task, "Update");

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(sequence.Length - PairDepth + 1, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(positionBefore.x, actor.transform.position.x);
            Assert.AreEqual(100f, actor.transform.position.z);
        }

        /// <summary>Verifies that an exhausted sequence reports its error on one frame only.</summary>
        /// <remarks>
        /// The actor is left past the segment boundary, so a path that only returned would re-enter the advance on
        /// every later frame, logging again and raising the segment index without bound.
        /// </remarks>
        [Test]
        public void Update_SequenceExhaustedOnTwoFrames_ReportsTheErrorOnce()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            int[] sequence = SegmentSequence(task);
            PrivateAccess.SetField(task, "_currentSegmentIndex", sequence.Length - PairDepth);
            SetActorZ(actor, 100f);
            LogAssert.Expect(LogType.Error, new Regex("Animal ran through all generated segments"));

            InvokeUpdateWhileEnabled(task);
            InvokeUpdateWhileEnabled(task);

            Assert.IsFalse(task.enabled);
            Assert.AreEqual(sequence.Length - PairDepth + 1, PrivateAccess.GetField<int>(task, "_currentSegmentIndex"));
            Assert.AreEqual(100f, actor.transform.position.z);
        }

        /// <summary>Verifies that a recomputed key outside the corridor map logs an error and skips the teleport.
        /// </summary>
        [Test]
        public void Update_NewCorridorKeyOutOfBounds_LogsErrorAndSkipsTheTeleport()
        {
            ActorObject actor = CreateActor(Vector3.zero);
            Task task = StartPairTask(PairTrackLength, PairSeed, actor);
            PrivateAccess.SetField(task, "_corridorMap", new (float, float)[] { (0f, ShortSegmentLengthUnity) });
            PrivateAccess.SetField(task, "_currentCorridorKey", 0);
            PrivateAccess.SetField(task, "_currentSegment", new List<int> { 0, 0, 0 });
            PrivateAccess.SetField(task, "_segmentSequenceArray", new int[] { 0, 0, 0, 1, 1, 1 });
            PrivateAccess.SetField(task, "_currentSegmentIndex", 0);
            actor.transform.position = new Vector3(3f, 1f, ShortSegmentLengthUnity + 0.5f);
            LogAssert.Expect(LogType.Error, new Regex("Task: New corridor key '1' out of bounds"));

            PrivateAccess.Invoke(task, "Update");

            Assert.AreEqual(1, CurrentKey(task));
            Assert.AreEqual(3f, actor.transform.position.x);
            Assert.AreEqual(ShortSegmentLengthUnity + 0.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that the same track seed regenerates identical segment and cue arrays.</summary>
        [Test]
        public void Start_SameTrackSeed_ProducesIdenticalSegmentAndCueArrays()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task first = CreateTask(configPath, LongTrackLength, 991, actor: null);
            Task second = CreateTask(configPath, LongTrackLength, 991, actor: null);

            PrivateAccess.Invoke(first, "Start");
            PrivateAccess.Invoke(second, "Start");

            CollectionAssert.AreEqual(SegmentSequence(first), SegmentSequence(second));
            CollectionAssert.AreEqual(CueSequence(first), CueSequence(second));
        }

        /// <summary>Verifies that two different track seeds produce different segment sequences.</summary>
        [Test]
        public void Start_DifferentTrackSeeds_ProduceDifferentSegmentArrays()
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task first = CreateTask(configPath, LongTrackLength, 1, actor: null);
            Task second = CreateTask(configPath, LongTrackLength, 2, actor: null);

            PrivateAccess.Invoke(first, "Start");
            PrivateAccess.Invoke(second, "Start");

            CollectionAssert.AreNotEqual(SegmentSequence(first), SegmentSequence(second));
        }

        /// <summary>Verifies that a seeded run follows the seeded generator's draw sequence exactly.</summary>
        [Test]
        public void Start_SeededGeneration_FollowsTheSeededRandomDrawSequence()
        {
            Task task = StartPairTask(LongTrackLength, 12345, actor: null);

            int[] sequence = SegmentSequence(task);
            CollectionAssert.AreEqual(ReferenceDraws(12345, sequence.Length, PairTrialCount), sequence);
        }

        /// <summary>Verifies that the sentinel selects the nondeterministic path instead of seeding with it.</summary>
        [Test]
        public void Start_RandomSeedSentinel_DoesNotSeedTheGeneratorWithTheSentinel()
        {
            Task task = StartPairTask(LongTrackLength, Task.RandomSeedSentinel, actor: null);

            int[] sequence = SegmentSequence(task);
            int[] sentinelSeeded = ReferenceDraws(Task.RandomSeedSentinel, sequence.Length, PairTrialCount);
            CollectionAssert.AreNotEqual(sentinelSeeded, sequence);
        }

        /// <summary>Verifies that generation stops on the first segment that reaches the requested track length.
        /// </summary>
        [Test]
        public void Start_GeneratedSequence_ReachesAtLeastTrackLength()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            int[] sequence = SegmentSequence(task);
            float[] segmentLengths = SegmentLengths(task);
            float total = 0f;
            for (int index = 0; index < sequence.Length; index++)
            {
                total += segmentLengths[sequence[index]];
            }
            Assert.GreaterOrEqual(total, PairTrackLength);
            Assert.Less(total - segmentLengths[sequence[sequence.Length - 1]], PairTrackLength);
        }

        /// <summary>Verifies that a track length landing exactly on a segment boundary adds no extra segment.</summary>
        [Test]
        public void Start_TrackLengthAMultipleOfSegmentLength_StopsAtExactlyTrackLength()
        {
            string configPath = StageTemplate(SingleTemplateName, BuildSingleTemplate());
            Task task = CreateTask(configPath, trackLength: 12f, trackSeed: 3, actor: null);

            PrivateAccess.Invoke(task, "Start");

            Assert.AreEqual(2, SegmentSequence(task).Length);
            Assert.AreEqual(SingleSegmentLengthUnity, SegmentLengths(task)[0]);
        }

        /// <summary>Verifies that the flattened cue array concatenates each chosen trial's cue codes in order.
        /// </summary>
        [Test]
        public void Start_FlattenedCueArray_ConcatenatesEachTrialsCueCodes()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            int longIndex = TrialIndex(task, "Long");
            List<byte> expected = new List<byte>();
            foreach (int segment in SegmentSequence(task))
            {
                if (segment == longIndex)
                {
                    expected.Add(CueCodeA);
                    expected.Add(CueCodeB);
                }
                else
                {
                    expected.Add(CueCodeB);
                }
            }
            CollectionAssert.AreEqual(expected, CueSequence(task));
        }

        /// <summary>Verifies that transitions with a single certain target produce that deterministic walk.</summary>
        [Test]
        public void Start_TransitionsOnEveryTrial_FollowsTheDeterministicWalk()
        {
            string configPath = StageTemplate(AbsorbingTemplateName, BuildAbsorbingTemplate());
            Task task = CreateTask(configPath, trackLength: 100f, trackSeed: 77, actor: null);

            PrivateAccess.Invoke(task, "Start");

            int[] sequence = SegmentSequence(task);
            int shortIndex = TrialIndex(task, "Short");
            Assert.Greater(sequence.Length, 1);
            for (int index = 1; index < sequence.Length; index++)
            {
                Assert.AreEqual(shortIndex, sequence[index]);
            }
        }

        /// <summary>Verifies that a template without transitions samples across every declared trial.</summary>
        [Test]
        public void Start_NoTransitions_SamplesEveryTrialIndex()
        {
            Task task = StartPairTask(LongTrackLength, PairSeed, actor: null);

            HashSet<int> observed = new HashSet<int>(SegmentSequence(task));
            Assert.AreEqual(PairTrialCount, observed.Count);
            Assert.IsTrue(observed.Contains(0));
            Assert.IsTrue(observed.Contains(1));
        }

        /// <summary>Verifies that a zero-length request generates no segments and no cue codes.</summary>
        [Test]
        public void GenerateRandomMaze_ZeroLength_ReturnsEmptyArrays()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);

            (int[] segments, byte[] cueCodes) result = ((int[], byte[]))
                PrivateAccess.Invoke(task, "GenerateRandomMaze", 0f, 7);

            Assert.AreEqual(0, result.segments.Length);
            Assert.AreEqual(0, result.cueCodes.Length);
        }

        /// <summary>Verifies that a zero-length shortest segment still returns empty segment and cue arrays.</summary>
        [Test]
        public void GenerateRandomMaze_NonPositiveShortestSegment_StillReturnsEmptyArrays()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            PrivateAccess.SetField(task, "_segmentLengths", new float[] { 0f, 0f });

            (int[] segments, byte[] cueCodes) result = ((int[], byte[]))
                PrivateAccess.Invoke(task, "GenerateRandomMaze", 0f, 7);

            Assert.AreEqual(0, result.segments.Length);
            Assert.AreEqual(0, result.cueCodes.Length);
        }

        /// <summary>Verifies that a single certain target is always sampled from the distribution.</summary>
        [Test]
        public void TrySampleFromTransitions_SingleCertainTarget_ReturnsThatTarget()
        {
            object[] arguments = SamplerArguments(new Dictionary<string, float> { { "Short", 1f } });

            bool sampled = (bool)PrivateAccess.InvokeStatic(typeof(Task), "TrySampleFromTransitions", arguments);

            Assert.IsTrue(sampled);
            Assert.AreEqual("Short", arguments[2]);
        }

        /// <summary>Verifies that a zero-weight entry is skipped in favor of the next cumulative bucket.</summary>
        [Test]
        public void TrySampleFromTransitions_ZeroWeightFirstEntry_SkipsToTheNextTarget()
        {
            // The trailing zero-weight entry separates the cumulative-bucket return from the fall-through
            // return, because a sampler that never matched a bucket would answer with the last key instead.
            object[] arguments = SamplerArguments(
                new Dictionary<string, float>
                {
                    { "Long", 0f },
                    { "Short", 1f },
                    { "Extra", 0f },
                }
            );

            bool sampled = (bool)PrivateAccess.InvokeStatic(typeof(Task), "TrySampleFromTransitions", arguments);

            Assert.IsTrue(sampled);
            Assert.AreEqual("Short", arguments[2]);
        }

        /// <summary>Verifies that a distribution no bucket exceeds falls through onto the final key.</summary>
        [Test]
        public void TrySampleFromTransitions_NoEntryExceedsTheDraw_ReturnsTheLastKey()
        {
            object[] arguments = SamplerArguments(new Dictionary<string, float> { { "Long", 0f }, { "Short", 0f } });

            bool sampled = (bool)PrivateAccess.InvokeStatic(typeof(Task), "TrySampleFromTransitions", arguments);

            Assert.IsTrue(sampled);
            Assert.AreEqual("Short", arguments[2]);
        }

        /// <summary>Verifies that an empty distribution samples nothing and reports the failure.</summary>
        /// <remarks>
        /// The reported failure keeps the maze generator off the trial-name lookup, which raises a
        /// KeyNotFoundException for a name no trial carries.
        /// </remarks>
        [Test]
        public void TrySampleFromTransitions_EmptyDistribution_ReturnsFalseWithoutATrialName()
        {
            object[] arguments = SamplerArguments(new Dictionary<string, float>());

            bool sampled = (bool)PrivateAccess.InvokeStatic(typeof(Task), "TrySampleFromTransitions", arguments);

            Assert.IsFalse(sampled);
            Assert.IsNull(arguments[2]);
        }

        /// <summary>Verifies that a cue-sequence request is answered with the flattened cue array.</summary>
        [Test]
        public void OnCueSequenceTrigger_TriggerReceived_RepliesWithTheFlattenedCueSequence()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            byte[] expected = CueSequence(task);
            _harness.Clear();

            _harness.PublishTrigger(MQTTTopics.CueSequenceTrigger);

            Assert.AreEqual(1, _harness.CountOn(MQTTTopics.CueSequence));
            Task.SequenceMessage reply = _harness.LastMessageOn<Task.SequenceMessage>(MQTTTopics.CueSequence);
            CollectionAssert.AreEqual(expected, reply.cueSequence);
        }

        /// <summary>Verifies that a scene-name request is answered with the active scene's name.</summary>
        [Test]
        public void OnSceneNameTrigger_TriggerReceived_RepliesWithTheActiveSceneName()
        {
            StartPairTask(PairTrackLength, PairSeed, actor: null);
            string expectedName = SceneManager.GetActiveScene().name;
            _harness.Clear();

            _harness.PublishTrigger(MQTTTopics.SceneNameTrigger);

            Assert.AreEqual(1, _harness.CountOn(MQTTTopics.SceneName));
            Assert.AreEqual(expectedName, _harness.LastMessageOn<Task.SceneNameMessage>(MQTTTopics.SceneName).name);
        }

        /// <summary>Verifies that a true payload on RequireInteraction enables the interaction requirement.</summary>
        [Test]
        public void OnRequireInteraction_TruePayload_EnablesTheInteractionRequirement()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            task.requireInteraction = false;

            _harness.Publish(MQTTTopics.RequireInteraction, new Task.BoolMessage { value = true });

            Assert.IsTrue(task.requireInteraction);
        }

        /// <summary>Verifies that a false payload on RequireInteraction disables the interaction requirement.</summary>
        [Test]
        public void OnRequireInteraction_FalsePayload_DisablesTheInteractionRequirement()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            task.requireInteraction = true;

            _harness.Publish(MQTTTopics.RequireInteraction, new Task.BoolMessage { value = false });

            Assert.IsFalse(task.requireInteraction);
        }

        /// <summary>Verifies that a true payload on RequireWait enables the wait requirement.</summary>
        [Test]
        public void OnRequireWait_TruePayload_EnablesTheWaitRequirement()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            task.requireWait = false;

            _harness.Publish(MQTTTopics.RequireWait, new Task.BoolMessage { value = true });

            Assert.IsTrue(task.requireWait);
        }

        /// <summary>Verifies that a false payload on RequireWait disables the wait requirement.</summary>
        [Test]
        public void OnRequireWait_FalsePayload_DisablesTheWaitRequirement()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            task.requireWait = true;

            _harness.Publish(MQTTTopics.RequireWait, new Task.BoolMessage { value = false });

            Assert.IsFalse(task.requireWait);
        }

        /// <summary>Verifies that destruction stops the task from answering either request topic.</summary>
        [Test]
        public void OnDestroy_AfterStart_StopsRespondingToTheRequestTopics()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            PrivateAccess.Invoke(task, "OnDestroy");
            _harness.Clear();

            _harness.PublishTrigger(MQTTTopics.CueSequenceTrigger);
            _harness.PublishTrigger(MQTTTopics.SceneNameTrigger);

            Assert.AreEqual(0, _harness.CountOn(MQTTTopics.CueSequence));
            Assert.AreEqual(0, _harness.CountOn(MQTTTopics.SceneName));
        }

        /// <summary>Verifies that destruction stops the task from applying the requirement toggles.</summary>
        [Test]
        public void OnDestroy_AfterStart_StopsApplyingTheRequirementToggles()
        {
            Task task = StartPairTask(PairTrackLength, PairSeed, actor: null);
            task.requireInteraction = false;
            task.requireWait = false;
            PrivateAccess.Invoke(task, "OnDestroy");

            _harness.Publish(MQTTTopics.RequireInteraction, new Task.BoolMessage { value = true });
            _harness.Publish(MQTTTopics.RequireWait, new Task.BoolMessage { value = true });

            Assert.IsFalse(task.requireInteraction);
            Assert.IsFalse(task.requireWait);
        }

        /// <summary>Verifies that destroying a task whose Start never ran leaves no unset channel to unhook.</summary>
        [Test]
        public void OnDestroy_BeforeStart_DoesNotThrow()
        {
            Task task = CreateTask(configPath: null, trackLength: PairTrackLength, trackSeed: PairSeed, actor: null);

            Assert.DoesNotThrow(() => PrivateAccess.Invoke(task, "OnDestroy"));
        }

        /// <summary>Verifies that the zone scan returns every one of the four resettable implementers.</summary>
        [Test]
        public void FindResettableZones_SceneWithEachImplementer_ReturnsAllFour()
        {
            IResettable[] baseline = (IResettable[])PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");
            StimulusTriggerZone stimulusZone = CreateComponent<StimulusTriggerZone>("StimulusTriggerZone");
            GuidanceZone guidanceZone = CreateComponent<GuidanceZone>("GuidanceRegion");
            OccupancyZone occupancyZone = CreateComponent<OccupancyZone>("OccupancyRegion");
            OccupancyGuidanceZone occupancyGuidanceZone = CreateComponent<OccupancyGuidanceZone>(
                "OccupancyGuidanceRegion"
            );

            IResettable[] found = (IResettable[])PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");

            Assert.AreEqual(baseline.Length + 4, found.Length);
            CollectionAssert.Contains(found, stimulusZone);
            CollectionAssert.Contains(found, guidanceZone);
            CollectionAssert.Contains(found, occupancyZone);
            CollectionAssert.Contains(found, occupancyGuidanceZone);
        }

        /// <summary>Verifies that the zone scan adds nothing for a component that is not resettable.</summary>
        [Test]
        public void FindResettableZones_SceneWithANonResettableZone_LeavesTheCountUnchanged()
        {
            IResettable[] baseline = (IResettable[])PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");
            CreateComponent<BoxCollider>("NonResettableZone");

            IResettable[] found = (IResettable[])PrivateAccess.InvokeStatic(typeof(Task), "FindResettableZones");

            Assert.AreEqual(baseline.Length, found.Length);
        }

        /// <summary>Builds the two-trial corridor template with a short single-cue and a long two-cue trial.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildPairTemplate()
        {
            TemplateYaml template = new TemplateYaml();
            CueYaml shortCue = CueYaml.Named("A", CueCodeA);
            shortCue.lengthCm = 30f;
            shortCue.texture = StagedTextureName;
            template.cues.Add(shortCue);
            CueYaml longCue = CueYaml.Named("B", CueCodeB);
            longCue.lengthCm = 60f;
            longCue.texture = StagedTextureName;
            template.cues.Add(longCue);
            template.trials.Add(TrialYaml.Named("Long", "A", "B"));
            template.trials.Add(TrialYaml.Named("Short", "B"));
            template.vrEnvironment.corridorSpacingCm = 20f;
            template.vrEnvironment.segmentsPerCorridor = PairDepth;
            template.vrEnvironment.cmPerUnityUnit = 10f;
            return template;
        }

        /// <summary>Builds the single-trial template whose one segment measures six Unity units.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildSingleTemplate()
        {
            TemplateYaml template = new TemplateYaml();
            CueYaml cue = CueYaml.Named("A", CueCodeA);
            cue.lengthCm = 60f;
            cue.texture = StagedTextureName;
            template.cues.Add(cue);
            template.trials.Add(TrialYaml.Named("S", "A"));
            template.vrEnvironment.corridorSpacingCm = 20f;
            template.vrEnvironment.segmentsPerCorridor = 1;
            template.vrEnvironment.cmPerUnityUnit = 10f;
            return template;
        }

        /// <summary>Builds the template whose every trial transitions into "Short" with certainty.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildAbsorbingTemplate()
        {
            TemplateYaml template = BuildPairTemplate();
            template.vrEnvironment.segmentsPerCorridor = 1;
            template.Trial("Long").WithTransitions(new Dictionary<string, float> { { "Short", 1f } });
            template.Trial("Short").WithTransitions(new Dictionary<string, float> { { "Short", 1f } });
            return template;
        }

        /// <summary>Builds the two-trial template whose corridor depth overruns the corridor map limit.</summary>
        /// <returns>The template builder.</returns>
        private static TemplateYaml BuildDeepTemplate()
        {
            TemplateYaml template = BuildPairTemplate();
            template.vrEnvironment.segmentsPerCorridor = ExcessiveCorridorDepth;
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
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <returns>The absolute path of the staged template file.</returns>
        private static string AbsoluteTemplatePath(string templateName)
        {
            return Path.Combine(Application.dataPath, "InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");
        }

        /// <summary>Writes a template into the project's Configurations directory for the running test.</summary>
        /// <param name="templateName">The template name, which becomes the file name without its extension.</param>
        /// <param name="template">The builder whose rendered document is written.</param>
        /// <returns>The task-relative configuration path pointing at the staged file.</returns>
        private string StageTemplate(string templateName, TemplateYaml template)
        {
            File.WriteAllText(AbsoluteTemplatePath(templateName), template.Build());
            _stagedTemplateNames.Add(templateName);
            return $"{ConfigurationsDirectory}/{templateName}.yaml";
        }

        /// <summary>Creates a Task component configured with the supplied fields.</summary>
        /// <param name="configPath">The dataPath-relative path of the staged template YAML.</param>
        /// <param name="trackLength">The Unity-unit track length the pre-generated segment sequence covers.</param>
        /// <param name="trackSeed">The maze seed, or Task.RandomSeedSentinel for a nondeterministic run.</param>
        /// <param name="actor">The actor the task teleports between corridors, or null to leave it unassigned.</param>
        /// <returns>The created task.</returns>
        private Task CreateTask(string configPath, float trackLength, int trackSeed, ActorObject actor)
        {
            GameObject host = new GameObject("Task");
            _createdObjects.Add(host);
            Task task = host.AddComponent<Task>();
            task.configPath = configPath;
            task.trackLength = trackLength;
            task.trackSeed = trackSeed;
            task.actor = actor;
            return task;
        }

        /// <summary>Stages the pair template, creates a task against it, and runs the task's Start.</summary>
        /// <param name="trackLength">The Unity-unit track length the pre-generated segment sequence covers.</param>
        /// <param name="trackSeed">The maze seed, or Task.RandomSeedSentinel for a nondeterministic run.</param>
        /// <param name="actor">The actor the task teleports between corridors, or null to leave it unassigned.</param>
        /// <returns>The started task.</returns>
        private Task StartPairTask(float trackLength, int trackSeed, ActorObject actor)
        {
            string configPath = StageTemplate(PairTemplateName, BuildPairTemplate());
            Task task = CreateTask(configPath, trackLength, trackSeed, actor);
            PrivateAccess.Invoke(task, "Start");
            return task;
        }

        /// <summary>Creates a task that never ran Start, carrying only the trial count the encoder reads.</summary>
        /// <param name="trialCount">The value assigned to the task's private trial count field.</param>
        /// <returns>The created task.</returns>
        private Task CreateBareTask(int trialCount)
        {
            Task task = CreateTask(configPath: null, trackLength: Task.DefaultTrackLength, trackSeed: 0, actor: null);
            PrivateAccess.SetField(task, "_trialCount", trialCount);
            return task;
        }

        /// <summary>Creates an ActorObject positioned at the supplied world position.</summary>
        /// <param name="position">The world position assigned to the actor's transform.</param>
        /// <returns>The created actor.</returns>
        private ActorObject CreateActor(Vector3 position)
        {
            GameObject host = new GameObject("Actor");
            _createdObjects.Add(host);
            host.transform.position = position;
            return host.AddComponent<ActorObject>();
        }

        /// <summary>Creates a component of the requested type on its own GameObject.</summary>
        /// <typeparam name="TComponent">The component type to attach.</typeparam>
        /// <param name="objectName">The name of the hosting GameObject.</param>
        /// <returns>The created component.</returns>
        private TComponent CreateComponent<TComponent>(string objectName)
            where TComponent : Component
        {
            GameObject host = new GameObject(objectName);
            _createdObjects.Add(host);
            return host.AddComponent<TComponent>();
        }

        /// <summary>Returns the maze's segment index sequence.</summary>
        /// <param name="task">The task whose generated sequence to read.</param>
        /// <returns>The segment index sequence.</returns>
        private static int[] SegmentSequence(Task task)
        {
            return PrivateAccess.GetField<int[]>(task, "_segmentSequenceArray");
        }

        /// <summary>Returns the maze's flattened cue code array.</summary>
        /// <param name="task">The task whose generated cue codes to read.</param>
        /// <returns>The flattened cue code array.</returns>
        private static byte[] CueSequence(Task task)
        {
            return PrivateAccess.GetField<byte[]>(task, "_cueSequenceArray");
        }

        /// <summary>Returns the per-trial segment lengths in Unity units.</summary>
        /// <param name="task">The task whose segment lengths to read.</param>
        /// <returns>The segment lengths, indexed positionally by trial.</returns>
        private static float[] SegmentLengths(Task task)
        {
            return PrivateAccess.GetField<float[]>(task, "_segmentLengths");
        }

        /// <summary>Returns the corridor map built at startup.</summary>
        /// <param name="task">The task whose corridor map to read.</param>
        /// <returns>The corridor map entries.</returns>
        private static (float xPosition, float firstSegmentLength)[] CorridorMap(Task task)
        {
            return PrivateAccess.GetField<(float xPosition, float firstSegmentLength)[]>(task, "_corridorMap");
        }

        /// <summary>Returns the corridor window the task currently occupies.</summary>
        /// <param name="task">The task whose corridor window to read.</param>
        /// <returns>The ordered segment indices of the current corridor.</returns>
        private static List<int> CurrentSegment(Task task)
        {
            return PrivateAccess.GetField<List<int>>(task, "_currentSegment");
        }

        /// <summary>Returns the cached corridor key of the current corridor window.</summary>
        /// <param name="task">The task whose corridor key to read.</param>
        /// <returns>The cached corridor key.</returns>
        private static int CurrentKey(Task task)
        {
            return PrivateAccess.GetField<int>(task, "_currentCorridorKey");
        }

        /// <summary>Returns the template name the task loaded at startup.</summary>
        /// <param name="task">The task whose loaded template to read.</param>
        /// <returns>The loaded template's derived name.</returns>
        private static string LoadedTemplateName(Task task)
        {
            return PrivateAccess.GetField<TaskTemplate>(task, "_template").templateName;
        }

        /// <summary>Returns the positional index of a trial name in the task's trial-name array.</summary>
        /// <param name="task">The task whose trial names to search.</param>
        /// <param name="trialName">The trial name to locate.</param>
        /// <returns>The positional index of the trial.</returns>
        private static int TrialIndex(Task task, string trialName)
        {
            string[] trialNames = PrivateAccess.GetField<string[]>(task, "_trialNames");
            int index = Array.IndexOf(trialNames, trialName);
            Assert.GreaterOrEqual(index, 0);
            return index;
        }

        /// <summary>Returns the first segment length of the corridor the task currently occupies.</summary>
        /// <param name="task">The task whose current corridor to read.</param>
        /// <returns>The leading segment's length in Unity units.</returns>
        private static float FirstSegmentLength(Task task)
        {
            return CorridorMap(task)[CurrentKey(task)].firstSegmentLength;
        }

        /// <summary>Writes a new z coordinate onto the actor while preserving its x and y.</summary>
        /// <param name="actor">The actor whose transform to move.</param>
        /// <param name="zPosition">The new z coordinate.</param>
        private static void SetActorZ(ActorObject actor, float zPosition)
        {
            Vector3 position = actor.transform.position;
            position.z = zPosition;
            actor.transform.position = position;
        }

        /// <summary>Moves the actor half a Unity unit past the end of the current corridor's first segment.</summary>
        /// <param name="task">The task whose current corridor supplies the segment length.</param>
        /// <param name="actor">The actor whose transform to move.</param>
        private static void MoveActorPastFirstSegment(Task task, ActorObject actor)
        {
            SetActorZ(actor, FirstSegmentLength(task) + 0.5f);
        }

        /// <summary>Drives one frame of Update the way the player loop does, skipping a disabled component.</summary>
        /// <remarks>
        /// Reflection reaches Update regardless of the enabled flag, so a test that pins a terminal failure path
        /// consults the flag itself to reproduce the frame the engine would skip.
        /// </remarks>
        /// <param name="task">The task whose Update callback to drive.</param>
        private static void InvokeUpdateWhileEnabled(Task task)
        {
            if (task.enabled)
            {
                PrivateAccess.Invoke(task, "Update");
            }
        }

        /// <summary>Builds the reflection argument array the transition sampler writes its trial name into.</summary>
        /// <remarks>
        /// Reflection assigns an out parameter back into the very array it was handed, so the sampled name is read
        /// from the array's last slot once the call returns.
        /// </remarks>
        /// <param name="transitions">The distribution handed to the sampler as its first argument.</param>
        /// <returns>The argument array carrying the distribution, the generator, and the trial name slot.</returns>
        private static object[] SamplerArguments(Dictionary<string, float> transitions)
        {
            return new object[] { transitions, new System.Random(SamplerSeed), null };
        }

        /// <summary>Encodes a slice of a segment sequence as a base-trial-count corridor key.</summary>
        /// <param name="sequence">The generated segment index sequence.</param>
        /// <param name="start">The index of the corridor's leading segment.</param>
        /// <param name="depth">The number of segments comprising the corridor.</param>
        /// <param name="trialCount">The numeral base, which equals the trial count.</param>
        /// <returns>The corridor key.</returns>
        private static int EncodeKey(int[] sequence, int start, int depth, int trialCount)
        {
            int key = 0;
            for (int offset = 0; offset < depth; offset++)
            {
                key = key * trialCount + sequence[start + offset];
            }
            return key;
        }

        /// <summary>Raises an integer to an integer power without leaving integer arithmetic.</summary>
        /// <param name="baseValue">The base to raise.</param>
        /// <param name="exponent">The non-negative exponent.</param>
        /// <returns>The computed power.</returns>
        private static int IntegerPower(int baseValue, int exponent)
        {
            int result = 1;
            for (int step = 0; step < exponent; step++)
            {
                result *= baseValue;
            }
            return result;
        }

        /// <summary>Returns the trial draws a seeded generator produces for a uniform, transition-free template.
        /// </summary>
        /// <param name="seed">The seed handed to the reference generator.</param>
        /// <param name="drawCount">The number of draws to produce.</param>
        /// <param name="trialCount">The exclusive upper bound of each draw.</param>
        /// <returns>The reference draw sequence.</returns>
        private static int[] ReferenceDraws(int seed, int drawCount, int trialCount)
        {
            System.Random reference = new System.Random(seed);
            int[] draws = new int[drawCount];
            for (int index = 0; index < drawCount; index++)
            {
                draws[index] = reference.Next(trialCount);
            }
            return draws;
        }

        /// <summary>Asserts that every corridor map index round-trips through the corridor key encoding.</summary>
        /// <param name="task">The task whose encoder is exercised.</param>
        /// <param name="trialCount">The numeral base assigned to the task before encoding.</param>
        /// <param name="depth">The corridor depth, which is the digit count.</param>
        private static void AssertKeyRoundTrip(Task task, int trialCount, int depth)
        {
            PrivateAccess.SetField(task, "_trialCount", trialCount);
            int corridorCount = IntegerPower(trialCount, depth);
            for (int key = 0; key < corridorCount; key++)
            {
                List<int> digits = new List<int>(depth);
                for (int position = 0; position < depth; position++)
                {
                    digits.Add(key / IntegerPower(trialCount, depth - position - 1) % trialCount);
                }
                Assert.AreEqual(key, (int)PrivateAccess.Invoke(task, "ComputeCorridorKey", digits));
            }
        }
    }
}
