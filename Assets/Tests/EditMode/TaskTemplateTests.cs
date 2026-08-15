/// <summary>
/// Verifies the behavior of the TaskTemplate class.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SL.Config;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the TaskTemplate class.</summary>
    [TestFixture]
    public class TaskTemplateTests
    {
        /// <summary>The template under test, rebuilt before each test so no cache survives a test boundary.</summary>
        private TaskTemplate _template;

        /// <summary>Builds a fresh reference template before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _template = BuildTemplate();
        }

        /// <summary>Releases the template under test after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            _template = null;
        }

        /// <summary>Verifies that GetCueNameToCode maps every cue name to its declared byte code.</summary>
        [Test]
        public void GetCueNameToCode_MultipleCues_MapsEachNameToItsCode()
        {
            Dictionary<string, byte> codes = _template.GetCueNameToCode();

            Assert.AreEqual(3, codes.Count);
            Assert.AreEqual((byte)1, codes["A"]);
            Assert.AreEqual((byte)2, codes["B"]);
            Assert.AreEqual((byte)3, codes["C"]);
        }

        /// <summary>Verifies that GetCueNameToCode returns an empty map when the template declares no cues.</summary>
        [Test]
        public void GetCueNameToCode_NoCues_ReturnsEmptyMap()
        {
            _template.cues = new List<Cue>();

            Dictionary<string, byte> codes = _template.GetCueNameToCode();

            Assert.AreEqual(0, codes.Count);
        }

        /// <summary>Verifies that the inclusive byte-range boundary codes survive the cast unchanged.</summary>
        [Test]
        public void GetCueNameToCode_CodesAtByteBoundaries_PreservesBothValues()
        {
            _template.cues[0].code = 0;
            _template.cues[2].code = 255;

            Dictionary<string, byte> codes = _template.GetCueNameToCode();

            Assert.AreEqual((byte)0, codes["A"]);
            Assert.AreEqual((byte)255, codes["C"]);
        }

        /// <summary>Verifies that a code one past the byte maximum throws instead of resolving to zero.</summary>
        [Test]
        public void GetCueNameToCode_CodeAboveByteMaximum_ThrowsInvalidData()
        {
            _template.cues[0].code = 256;

            Assert.Throws<InvalidDataException>(() => _template.GetCueNameToCode());
        }

        /// <summary>
        /// Verifies that a code far above the byte maximum throws instead of resolving to its low-order byte.
        /// </summary>
        [Test]
        public void GetCueNameToCode_CodeFarAboveByteMaximum_ThrowsInvalidData()
        {
            _template.cues[0].code = 513;

            Assert.Throws<InvalidDataException>(() => _template.GetCueNameToCode());
        }

        /// <summary>
        /// Verifies that a code one below the byte minimum throws instead of resolving to the byte maximum.
        /// </summary>
        [Test]
        public void GetCueNameToCode_CodeBelowByteMinimum_ThrowsInvalidData()
        {
            _template.cues[0].code = -1;

            Assert.Throws<InvalidDataException>(() => _template.GetCueNameToCode());
        }

        /// <summary>Verifies that the out-of-range code report names the offending cue and its declared code.</summary>
        [Test]
        public void GetCueNameToCode_CodeAboveByteMaximum_ReportsTheCueNameAndCode()
        {
            _template.cues[1].code = 300;

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => _template.GetCueNameToCode());

            StringAssert.Contains("'B'", exception.Message);
            StringAssert.Contains("300", exception.Message);
        }

        /// <summary>
        /// Verifies that an out-of-range code leaves the code cache unset, so a corrected code maps every cue.
        /// </summary>
        [Test]
        public void GetCueNameToCode_CodeCorrectedAfterAThrow_MapsEveryCueName()
        {
            _template.cues[0].code = 256;
            Assert.Throws<InvalidDataException>(() => _template.GetCueNameToCode());
            Assert.IsNull(PrivateAccess.GetField<Dictionary<string, byte>>(_template, "_cueNameToCodeCache"));

            _template.cues[0].code = 7;
            Dictionary<string, byte> codes = _template.GetCueNameToCode();

            Assert.AreEqual(3, codes.Count);
            Assert.AreEqual((byte)7, codes["A"]);
        }

        /// <summary>
        /// Verifies that GetCueNameToCode returns the identical cached map instance on a second call.
        /// </summary>
        [Test]
        public void GetCueNameToCode_CalledTwice_ReturnsTheSameCachedInstance()
        {
            Dictionary<string, byte> first = _template.GetCueNameToCode();
            Dictionary<string, byte> second = _template.GetCueNameToCode();

            Assert.AreSame(first, second);
        }

        /// <summary>Verifies that adding a cue after the first call leaves the cached code map stale.</summary>
        [Test]
        public void GetCueNameToCode_CueAppendedAfterFirstCall_ReturnsStaleMap()
        {
            Dictionary<string, byte> first = _template.GetCueNameToCode();
            Assert.AreEqual(3, first.Count);

            _template.cues.Add(NewCue("D", 4, 10.0f));
            Dictionary<string, byte> second = _template.GetCueNameToCode();

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Count);
            Assert.IsFalse(second.ContainsKey("D"));
        }

        /// <summary>
        /// Verifies that replacing the cues list after the first call leaves the cached code map stale.
        /// </summary>
        [Test]
        public void GetCueNameToCode_CuesListReplacedAfterFirstCall_ReturnsStaleMap()
        {
            _template.GetCueNameToCode();

            _template.cues = new List<Cue> { NewCue("Z", 9, 10.0f) };
            Dictionary<string, byte> second = _template.GetCueNameToCode();

            Assert.AreEqual(3, second.Count);
            Assert.IsFalse(second.ContainsKey("Z"));
        }

        /// <summary>Verifies that two cues sharing a name make the code map construction throw.</summary>
        [Test]
        public void GetCueNameToCode_DuplicateCueNames_ThrowsArgumentException()
        {
            _template.cues[2].name = "A";

            Assert.Throws<ArgumentException>(() => _template.GetCueNameToCode());
        }

        /// <summary>Verifies that GetCueByName maps every cue name to the cue instance the list holds.</summary>
        [Test]
        public void GetCueByName_MultipleCues_MapsEachNameToItsCueInstance()
        {
            Dictionary<string, Cue> cueMap = _template.GetCueByName();

            Assert.AreEqual(3, cueMap.Count);
            Assert.AreSame(_template.cues[0], cueMap["A"]);
            Assert.AreSame(_template.cues[1], cueMap["B"]);
            Assert.AreSame(_template.cues[2], cueMap["C"]);
            Assert.AreEqual(45.0f, cueMap["B"].lengthCm);
        }

        /// <summary>Verifies that GetCueByName returns an empty map when the template declares no cues.</summary>
        [Test]
        public void GetCueByName_NoCues_ReturnsEmptyMap()
        {
            _template.cues = new List<Cue>();

            Dictionary<string, Cue> cueMap = _template.GetCueByName();

            Assert.AreEqual(0, cueMap.Count);
        }

        /// <summary>Verifies that GetCueByName returns the identical cached map instance on a second call.</summary>
        [Test]
        public void GetCueByName_CalledTwice_ReturnsTheSameCachedInstance()
        {
            Dictionary<string, Cue> first = _template.GetCueByName();
            Dictionary<string, Cue> second = _template.GetCueByName();

            Assert.AreSame(first, second);
        }

        /// <summary>Verifies that adding a cue after the first call leaves the cached cue map stale.</summary>
        [Test]
        public void GetCueByName_CueAppendedAfterFirstCall_ReturnsStaleMap()
        {
            Dictionary<string, Cue> first = _template.GetCueByName();

            _template.cues.Add(NewCue("D", 4, 10.0f));
            Dictionary<string, Cue> second = _template.GetCueByName();

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Count);
            Assert.IsFalse(second.ContainsKey("D"));
        }

        /// <summary>Verifies that two cues sharing a name make the cue map construction throw.</summary>
        [Test]
        public void GetCueByName_DuplicateCueNames_ThrowsArgumentException()
        {
            _template.cues[1].name = "A";

            Assert.Throws<ArgumentException>(() => _template.GetCueByName());
        }

        /// <summary>Verifies that GetCueLengthsUnity converts every cue in the cues list order.</summary>
        [Test]
        public void GetCueLengthsUnity_DefaultScaleFactor_ConvertsEachCueInCuesListOrder()
        {
            float[] lengths = _template.GetCueLengthsUnity();

            Assert.AreEqual(3, lengths.Length);
            Assert.AreEqual(3.0f, lengths[0]);
            Assert.AreEqual(4.5f, lengths[1]);
            Assert.AreEqual(2.5f, lengths[2]);
        }

        /// <summary>Verifies that GetCueLengthsUnity divides by a non-integral centimeters-per-unit factor.</summary>
        [Test]
        public void GetCueLengthsUnity_NonIntegralScaleFactor_ConvertsWithThatFactor()
        {
            _template.vrEnvironment.cmPerUnityUnit = 2.5f;

            float[] lengths = _template.GetCueLengthsUnity();

            Assert.AreEqual(12.0f, lengths[0]);
            Assert.AreEqual(18.0f, lengths[1]);
            Assert.AreEqual(10.0f, lengths[2]);
        }

        /// <summary>
        /// Verifies that GetCueLengthsUnity returns an empty array when the template declares no cues.
        /// </summary>
        [Test]
        public void GetCueLengthsUnity_NoCues_ReturnsEmptyArray()
        {
            _template.cues = new List<Cue>();

            float[] lengths = _template.GetCueLengthsUnity();

            Assert.AreEqual(0, lengths.Length);
        }

        /// <summary>
        /// Verifies that GetCueLengthsUnity returns the identical cached array instance on a second call.
        /// </summary>
        [Test]
        public void GetCueLengthsUnity_CalledTwice_ReturnsTheSameCachedInstance()
        {
            float[] first = _template.GetCueLengthsUnity();
            float[] second = _template.GetCueLengthsUnity();

            Assert.AreSame(first, second);
        }

        /// <summary>
        /// Verifies that changing the scale factor after the first call leaves the cached lengths stale.
        /// </summary>
        [Test]
        public void GetCueLengthsUnity_ScaleFactorChangedAfterFirstCall_ReturnsStaleLengths()
        {
            float[] first = _template.GetCueLengthsUnity();
            Assert.AreEqual(3.0f, first[0]);

            _template.vrEnvironment.cmPerUnityUnit = 1.0f;
            float[] second = _template.GetCueLengthsUnity();

            Assert.AreSame(first, second);
            Assert.AreEqual(3.0f, second[0]);
            Assert.AreEqual(4.5f, second[1]);
        }

        /// <summary>Verifies that adding a cue after the first call leaves the cached length array stale.</summary>
        [Test]
        public void GetCueLengthsUnity_CueAppendedAfterFirstCall_ReturnsStaleLengths()
        {
            float[] first = _template.GetCueLengthsUnity();

            _template.cues.Add(NewCue("D", 4, 90.0f));
            float[] second = _template.GetCueLengthsUnity();

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Length);
        }

        /// <summary>
        /// Verifies that GetSegmentLengthsUnity sums each trial's cue sequence into a segment length.
        /// </summary>
        [Test]
        public void GetSegmentLengthsUnity_MultipleTrials_SumsEachTrialCueSequence()
        {
            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(3, lengths.Length);
            Assert.AreEqual(7.5f, lengths[0]);
            Assert.AreEqual(7.0f, lengths[1]);
            Assert.AreEqual(5.5f, lengths[2]);
        }

        /// <summary>Verifies that the segment length array indexes positionally match the trial name array.</summary>
        [Test]
        public void GetSegmentLengthsUnity_ReturnedArray_MatchesGetTrialNamesPositionally()
        {
            Dictionary<string, float> expectedLengthByName = new Dictionary<string, float>
            {
                { "AB", 7.5f },
                { "BC", 7.0f },
                { "CA", 5.5f },
            };

            string[] names = _template.GetTrialNames();
            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(3, names.Length);
            Assert.AreEqual(3, lengths.Length);
            for (int index = 0; index < names.Length; index++)
            {
                Assert.AreEqual(expectedLengthByName[names[index]], lengths[index]);
            }
        }

        /// <summary>Verifies that a trial repeating a cue counts that cue once per occurrence.</summary>
        [Test]
        public void GetSegmentLengthsUnity_RepeatedCueInSequence_CountsEveryOccurrence()
        {
            _template.trialStructures["AB"] = NewTrial("A", "A", "A");

            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(9.0f, lengths[0]);
        }

        /// <summary>Verifies that a trial declaring an empty cue sequence contributes a zero-length segment.</summary>
        [Test]
        public void GetSegmentLengthsUnity_EmptyCueSequence_YieldsZeroLength()
        {
            _template.trialStructures["BC"] = NewTrial();

            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(3, lengths.Length);
            Assert.AreEqual(0.0f, lengths[1]);
        }

        /// <summary>Verifies that GetSegmentLengthsUnity returns an empty array when no trials are declared.</summary>
        [Test]
        public void GetSegmentLengthsUnity_NoTrials_ReturnsEmptyArray()
        {
            _template.trialStructures = new Dictionary<string, TrialStructure>();

            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(0, lengths.Length);
        }

        /// <summary>Verifies that a cue name absent from the cues list makes the segment sum throw.</summary>
        [Test]
        public void GetSegmentLengthsUnity_UnknownCueName_ThrowsKeyNotFound()
        {
            _template.trialStructures["CA"] = NewTrial("C", "Missing");

            Assert.Throws<KeyNotFoundException>(() => _template.GetSegmentLengthsUnity());
        }

        /// <summary>Verifies that GetSegmentLengthsUnity returns the identical cached array on a second call.</summary>
        [Test]
        public void GetSegmentLengthsUnity_CalledTwice_ReturnsTheSameCachedInstance()
        {
            float[] first = _template.GetSegmentLengthsUnity();
            float[] second = _template.GetSegmentLengthsUnity();

            Assert.AreSame(first, second);
        }

        /// <summary>Verifies that adding a trial after the first call leaves the cached segment array stale.</summary>
        [Test]
        public void GetSegmentLengthsUnity_TrialAddedAfterFirstCall_ReturnsStaleLengths()
        {
            float[] first = _template.GetSegmentLengthsUnity();

            _template.trialStructures["AC"] = NewTrial("A", "C");
            float[] second = _template.GetSegmentLengthsUnity();

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Length);
        }

        /// <summary>
        /// Verifies that a cue instance swapped after the cue map cached leaves the segment sums stale.
        /// </summary>
        [Test]
        public void GetSegmentLengthsUnity_CueInstanceReplacedAfterCueMapCached_UsesStaleCueMap()
        {
            _template.GetCueByName();

            _template.cues[1] = NewCue("B", 2, 400.0f);
            float[] lengths = _template.GetSegmentLengthsUnity();

            Assert.AreEqual(7.5f, lengths[0]);
            Assert.AreEqual(7.0f, lengths[1]);
        }

        /// <summary>Verifies that GetTrialNames returns the trial names in trial_structures insertion order.</summary>
        [Test]
        public void GetTrialNames_MultipleTrials_ReturnsInsertionOrder()
        {
            string[] names = _template.GetTrialNames();

            Assert.AreEqual(3, names.Length);
            Assert.AreEqual("AB", names[0]);
            Assert.AreEqual("BC", names[1]);
            Assert.AreEqual("CA", names[2]);
        }

        /// <summary>Verifies that GetTrialNames returns an empty array when the template declares no trials.</summary>
        [Test]
        public void GetTrialNames_NoTrials_ReturnsEmptyArray()
        {
            _template.trialStructures = new Dictionary<string, TrialStructure>();

            string[] names = _template.GetTrialNames();

            Assert.AreEqual(0, names.Length);
        }

        /// <summary>Verifies that GetTrialNames returns the identical cached array instance on a second call.</summary>
        [Test]
        public void GetTrialNames_CalledTwice_ReturnsTheSameCachedInstance()
        {
            string[] first = _template.GetTrialNames();
            string[] second = _template.GetTrialNames();

            Assert.AreSame(first, second);
        }

        /// <summary>Verifies that adding a trial after the first call leaves the cached trial names stale.</summary>
        [Test]
        public void GetTrialNames_TrialAddedAfterFirstCall_ReturnsStaleNames()
        {
            string[] first = _template.GetTrialNames();

            _template.trialStructures["AC"] = NewTrial("A", "C");
            string[] second = _template.GetTrialNames();

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Length);
            Assert.AreEqual("CA", second[2]);
        }

        /// <summary>
        /// Verifies that GetTrialLengthUnity returns the summed segment length for each declared trial.
        /// </summary>
        [Test]
        public void GetTrialLengthUnity_EachDeclaredTrial_ReturnsSummedSegmentLength()
        {
            Assert.AreEqual(7.5f, _template.GetTrialLengthUnity("AB"));
            Assert.AreEqual(7.0f, _template.GetTrialLengthUnity("BC"));
            Assert.AreEqual(5.5f, _template.GetTrialLengthUnity("CA"));
        }

        /// <summary>Verifies that GetTrialLengthUnity applies a non-integral centimeters-per-unit factor.</summary>
        [Test]
        public void GetTrialLengthUnity_NonIntegralScaleFactor_ConvertsWithThatFactor()
        {
            _template.vrEnvironment.cmPerUnityUnit = 2.5f;

            Assert.AreEqual(30.0f, _template.GetTrialLengthUnity("AB"));
            Assert.AreEqual(28.0f, _template.GetTrialLengthUnity("BC"));
        }

        /// <summary>Verifies that GetTrialLengthUnity returns zero for a trial with an empty cue sequence.</summary>
        [Test]
        public void GetTrialLengthUnity_EmptyCueSequence_ReturnsZero()
        {
            _template.trialStructures["AB"] = NewTrial();

            Assert.AreEqual(0.0f, _template.GetTrialLengthUnity("AB"));
        }

        /// <summary>Verifies that GetTrialLengthUnity throws when the requested trial name is not declared.</summary>
        [Test]
        public void GetTrialLengthUnity_UnknownTrialName_ThrowsKeyNotFound()
        {
            Assert.Throws<KeyNotFoundException>(() => _template.GetTrialLengthUnity("ZZ"));
        }

        /// <summary>
        /// Verifies that the unknown-trial report names the requested trial, the template, and the declared trials.
        /// </summary>
        [Test]
        public void GetTrialLengthUnity_UnknownTrialName_ReportsTheTrialAndTemplateNames()
        {
            KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() =>
                _template.GetTrialLengthUnity("ZZ")
            );

            StringAssert.Contains("'ZZ'", exception.Message);
            StringAssert.Contains("ReferenceTemplate", exception.Message);
            StringAssert.Contains("AB, BC, CA", exception.Message);
        }

        /// <summary>
        /// Verifies that GetTrialLengthUnity rejects a null trial name rather than returning a length.
        /// </summary>
        [Test]
        public void GetTrialLengthUnity_NullTrialName_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => _template.GetTrialLengthUnity(null));
        }

        /// <summary>Verifies that a cue name absent from the cues list makes the trial length lookup throw.</summary>
        [Test]
        public void GetTrialLengthUnity_UnknownCueName_ThrowsKeyNotFound()
        {
            _template.trialStructures["AB"] = NewTrial("A", "Missing");

            Assert.Throws<KeyNotFoundException>(() => _template.GetTrialLengthUnity("AB"));
        }

        /// <summary>Verifies that GetTrialLengthUnity reuses the identical cached lookup on a second call.</summary>
        [Test]
        public void GetTrialLengthUnity_CalledTwice_ReusesTheSameCachedLookup()
        {
            _template.GetTrialLengthUnity("AB");
            Dictionary<string, float> first = PrivateAccess.GetField<Dictionary<string, float>>(
                _template,
                "_trialLengthsUnityCache"
            );

            _template.GetTrialLengthUnity("BC");
            Dictionary<string, float> second = PrivateAccess.GetField<Dictionary<string, float>>(
                _template,
                "_trialLengthsUnityCache"
            );

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.Count);
        }

        /// <summary>Verifies that a cue length changed after the first call leaves the trial length stale.</summary>
        [Test]
        public void GetTrialLengthUnity_CueLengthChangedAfterFirstCall_ReturnsStaleLength()
        {
            Assert.AreEqual(7.5f, _template.GetTrialLengthUnity("AB"));

            _template.cues[0].lengthCm = 300.0f;

            Assert.AreEqual(7.5f, _template.GetTrialLengthUnity("AB"));
        }

        /// <summary>Verifies that a trial added after the first call is absent from the cached lookup.</summary>
        [Test]
        public void GetTrialLengthUnity_TrialAddedAfterFirstCall_ThrowsForTheNewTrial()
        {
            Assert.AreEqual(7.5f, _template.GetTrialLengthUnity("AB"));

            _template.trialStructures["AC"] = NewTrial("A", "C");

            Assert.Throws<KeyNotFoundException>(() => _template.GetTrialLengthUnity("AC"));
        }

        /// <summary>
        /// Verifies that a first GetTrialNames call populates only its own cache, leaving the other five unset.
        /// </summary>
        [Test]
        public void Getters_FirstCallOnOneGetter_LeavesTheUnrelatedCachesUnpopulated()
        {
            string[] names = _template.GetTrialNames();

            Assert.IsNull(PrivateAccess.GetField<Dictionary<string, byte>>(_template, "_cueNameToCodeCache"));
            Assert.IsNull(PrivateAccess.GetField<Dictionary<string, Cue>>(_template, "_cueByNameCache"));
            Assert.IsNull(PrivateAccess.GetField<float[]>(_template, "_cueLengthsUnityCache"));
            Assert.IsNull(PrivateAccess.GetField<float[]>(_template, "_segmentLengthsUnityCache"));
            Assert.IsNull(PrivateAccess.GetField<Dictionary<string, float>>(_template, "_trialLengthsUnityCache"));
            Assert.AreSame(names, PrivateAccess.GetField<string[]>(_template, "_trialNamesCache"));
        }

        /// <summary>Verifies that GetSegmentLengthsUnity populates the shared cue map cache as a side effect.</summary>
        [Test]
        public void GetSegmentLengthsUnity_FirstCall_PopulatesTheSharedCueMapCache()
        {
            Assert.IsNull(PrivateAccess.GetField<Dictionary<string, Cue>>(_template, "_cueByNameCache"));

            _template.GetSegmentLengthsUnity();

            Dictionary<string, Cue> cueMap = PrivateAccess.GetField<Dictionary<string, Cue>>(
                _template,
                "_cueByNameCache"
            );
            Assert.AreEqual(3, cueMap.Count);
            Assert.AreSame(_template.cues[1], cueMap["B"]);
            Assert.AreSame(cueMap, _template.GetCueByName());
        }

        /// <summary>Verifies that two template instances hold independent caches.</summary>
        [Test]
        public void Getters_TwoTemplateInstances_HoldIndependentCaches()
        {
            TaskTemplate other = BuildTemplate();

            string[] first = _template.GetTrialNames();
            string[] second = other.GetTrialNames();

            Assert.AreNotSame(first, second);
            Assert.AreEqual(3, first.Length);
            Assert.AreEqual(3, second.Length);
            Assert.AreEqual("AB", second[0]);
            Assert.AreEqual("CA", second[2]);
        }

        /// <summary>
        /// Builds the reference template carrying three cues and three trials with exact conversions.
        /// </summary>
        /// <returns>A fully populated template instance whose caches are untouched.</returns>
        private static TaskTemplate BuildTemplate()
        {
            TaskTemplate template = new TaskTemplate
            {
                templateName = "ReferenceTemplate",
                vrEnvironment = new VREnvironment(),
                cues = new List<Cue> { NewCue("A", 1, 30.0f), NewCue("B", 2, 45.0f), NewCue("C", 3, 25.0f) },
                trialStructures = new Dictionary<string, TrialStructure>(),
            };
            template.trialStructures["AB"] = NewTrial("A", "B");
            template.trialStructures["BC"] = NewTrial("B", "C");
            template.trialStructures["CA"] = NewTrial("C", "A");
            return template;
        }

        /// <summary>Builds one cue definition.</summary>
        /// <param name="cueName">The cue name used as the lookup key.</param>
        /// <param name="cueCode">The cue code the code map getter range-checks and converts to a byte.</param>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <returns>The constructed cue.</returns>
        private static Cue NewCue(string cueName, int cueCode, float lengthCm)
        {
            return new Cue
            {
                name = cueName,
                code = cueCode,
                lengthCm = lengthCm,
                texture = $"{cueName}.png",
            };
        }

        /// <summary>Builds one trial structure carrying the supplied cue sequence.</summary>
        /// <param name="cueNames">The ordered cue names comprising the trial's segment.</param>
        /// <returns>The constructed trial structure.</returns>
        private static TrialStructure NewTrial(params string[] cueNames)
        {
            return new TrialStructure
            {
                cueSequence = new List<string>(cueNames),
                triggerType = "collision",
                stimulusTriggerZoneStartCm = 0.0f,
                stimulusTriggerZoneEndCm = 10.0f,
                stimulusLocationCm = 5.0f,
            };
        }
    }
}
