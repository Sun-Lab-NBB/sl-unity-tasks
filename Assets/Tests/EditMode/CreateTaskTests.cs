/// <summary>
/// Verifies the behavior of the CreateTask editor generation pipeline.
///
/// Every test template is named with the ZZTest_ prefix and every test cue is named with the ZZ prefix.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SL.Config;
using SL.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the CreateTask class.</summary>
    [TestFixture]
    public class CreateTaskTests
    {
        /// <summary>The project-relative root folder for every InfiniteCorridorTask-owned asset.</summary>
        private const string BaseFolder = "Assets/InfiniteCorridorTask";

        /// <summary>The project-relative folder holding the task template YAML documents.</summary>
        private const string ConfigurationsFolder = BaseFolder + "/Configurations";

        /// <summary>The project-relative folder holding the shared cue prefabs.</summary>
        private const string CuesFolder = BaseFolder + "/Cues";

        /// <summary>The project-relative folder holding the shared cue materials.</summary>
        private const string MaterialsFolder = BaseFolder + "/Materials";

        /// <summary>The project-relative folder holding the zone prefabs and the generated segment prefabs.</summary>
        private const string PrefabsFolder = BaseFolder + "/Prefabs";

        /// <summary>The project-relative folder holding the generated task prefabs.</summary>
        private const string TasksFolder = BaseFolder + "/Tasks";

        /// <summary>The project-relative folder holding the cue textures.</summary>
        private const string TexturesFolder = BaseFolder + "/Textures";

        /// <summary>The project-relative folder holding the scene assets.</summary>
        private const string ScenesFolder = "Assets/Scenes";

        /// <summary>The project-relative path of the hand-authored scene that scene generation copies.</summary>
        private const string TemplateScenePath = ScenesFolder + "/ExperimentTemplate.unity";

        /// <summary>
        /// The filename prefix every template, segment, task, scene, and texture asset a test creates carries.
        /// </summary>
        private const string TestAssetPrefix = "ZZTest_";

        /// <summary>The filename prefix every cue prefab and cue material a test creates carries.</summary>
        private const string TestCueAssetPrefix = "Cue_ZZ";

        /// <summary>The name of the first test cue, chosen so no shipped template declares the same identity.</summary>
        private const string FirstCueName = "ZZA";

        /// <summary>The name of the second test cue, chosen so no shipped template declares the same identity.
        /// </summary>
        private const string SecondCueName = "ZZB";

        /// <summary>The byte code of the first test cue.</summary>
        private const int FirstCueCode = 200;

        /// <summary>The byte code of the second test cue.</summary>
        private const int SecondCueCode = 201;

        /// <summary>The length of the first test cue in centimeters.</summary>
        private const float FirstCueLengthCm = 33f;

        /// <summary>The length of the second test cue in centimeters.</summary>
        private const float SecondCueLengthCm = 44f;

        /// <summary>The length of the first test cue in Unity units at the shared conversion factor.</summary>
        private const float FirstCueLengthUnity = 3.3f;

        /// <summary>The length of the second test cue in Unity units at the shared conversion factor.</summary>
        private const float SecondCueLengthUnity = 4.4f;

        /// <summary>The asset stem the first test cue resolves to.</summary>
        private const string FirstCueAssetStem = "Cue_ZZA_33cm";

        /// <summary>The name of the first real texture the test cues point at.</summary>
        private const string FirstTextureName = "Cue 001 - 2x1 repeat.png";

        /// <summary>The name of the second real texture the test cues point at.</summary>
        private const string SecondTextureName = "Cue 002 - 2x1 repeat.png";

        /// <summary>The name of the trial every single-trial test template declares.</summary>
        private const string FirstTrialName = "T1";

        /// <summary>The name of the second trial the multi-trial test templates declare.</summary>
        private const string SecondTrialName = "T2";

        /// <summary>The centimeters represented by one Unity unit in the shared test template baseline.</summary>
        private const float CmPerUnityUnit = 10f;

        /// <summary>The horizontal corridor spacing in centimeters in every generated test template.</summary>
        private const float CorridorSpacingCm = 20f;

        /// <summary>The corridor spacing in Unity units at the shared conversion factor.</summary>
        private const float CorridorSpacingUnity = 2f;

        /// <summary>The trigger zone starting boundary in centimeters in every generated test template.</summary>
        private const float ZoneStartCm = 5f;

        /// <summary>The trigger zone ending boundary in centimeters in every generated test template.</summary>
        private const float ZoneEndCm = 15f;

        /// <summary>The stimulus boundary position in centimeters in the shared test trial baseline.</summary>
        private const float StimulusLocationCm = 15f;

        /// <summary>The trigger zone center in Unity units derived from the shared zone boundaries.</summary>
        private const float ZoneCenterUnity = 1f;

        /// <summary>The trigger zone length in Unity units derived from the shared zone boundaries.</summary>
        private const float ZoneSizeUnity = 1f;

        /// <summary>The stimulus boundary position in Unity units derived from the shared stimulus location.</summary>
        private const float StimulusLocationUnity = 1.5f;

        /// <summary>The vertical offset CreateTask applies to every generated trigger zone.</summary>
        private const float ZoneVerticalOffset = 0.505f;

        /// <summary>The Z-axis depth CreateTask applies to every guidance and collision-wall collider.</summary>
        private const float GuidanceColliderDepth = 0.4f;

        /// <summary>The occupancy duration in milliseconds the occupancy test templates declare.</summary>
        private const float OccupancyDurationMs = 750f;

        /// <summary>
        /// The absolute tolerance for generated vector component comparisons, with AssertRotation carrying its own
        /// rotation tolerance.
        /// </summary>
        private const float GeometryTolerance = 0.0001f;

        /// <summary>Removes any leftover test assets so a previously aborted run cannot bias this test.</summary>
        [SetUp]
        public void SetUp()
        {
            RemoveTestAssets();
        }

        /// <summary>Removes every asset this test created, leaving hand-authored and shipped assets alone.</summary>
        [TearDown]
        public void TearDown()
        {
            RemoveTestAssets();
        }

        /// <summary>Verifies that FormatCueLengthLabel drops the decimals of an integral cue length.</summary>
        [Test]
        public void FormatCueLengthLabel_IntegralLength_OmitsTheDecimalSeparator()
        {
            Assert.AreEqual("30", FormatCueLengthLabel(30f));
        }

        /// <summary>Verifies that FormatCueLengthLabel keeps a single significant decimal.</summary>
        [Test]
        public void FormatCueLengthLabel_OneDecimalLength_KeepsTheSingleDecimal()
        {
            Assert.AreEqual("37.5", FormatCueLengthLabel(37.5f));
        }

        /// <summary>Verifies that FormatCueLengthLabel rounds a length carrying more than two decimals.</summary>
        [Test]
        public void FormatCueLengthLabel_ThreeDecimalLength_RoundsToTwoDecimals()
        {
            Assert.AreEqual("30.46", FormatCueLengthLabel(30.456f));
        }

        /// <summary>Verifies that FormatCueLengthLabel keeps a two decimal length intact.</summary>
        [Test]
        public void FormatCueLengthLabel_TwoDecimalLength_KeepsBothDecimals()
        {
            Assert.AreEqual("12.25", FormatCueLengthLabel(12.25f));
        }

        /// <summary>Verifies that CanonicalSegmentName joins the template and trial with exactly one hyphen.</summary>
        [Test]
        public void CanonicalSegmentName_TemplateAndTrial_JoinsWithASingleHyphen()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Alpha", CmPerUnityUnit, 1, "Padding", 30f);

            string segmentName = CanonicalSegmentName(template, FirstTrialName);

            Assert.AreEqual("ZZTest_Alpha-T1", segmentName);
            string[] halves = segmentName.Split('-');
            Assert.AreEqual(2, halves.Length);
            Assert.AreEqual("ZZTest_Alpha", halves[0]);
            Assert.AreEqual(FirstTrialName, halves[1]);
        }

        /// <summary>Verifies that a nested template basename still splits back to exactly one owning template.
        /// </summary>
        [Test]
        public void CanonicalSegmentName_NestedTemplateBasenames_SplitBackToTheirOwnTemplate()
        {
            TaskTemplate outerTemplate = BuildInMemoryTemplate("ZZTest_Base", CmPerUnityUnit, 1, "Padding", 30f);
            TaskTemplate nestedTemplate = BuildInMemoryTemplate("ZZTest_Base_Extra", CmPerUnityUnit, 1, "Padding", 30f);

            string outerName = CanonicalSegmentName(outerTemplate, FirstTrialName);
            string nestedName = CanonicalSegmentName(nestedTemplate, FirstTrialName);

            Assert.AreEqual("ZZTest_Base-T1", outerName);
            Assert.AreEqual("ZZTest_Base_Extra-T1", nestedName);
            Assert.AreEqual("ZZTest_Base", outerName.Split('-')[0]);
            Assert.AreEqual("ZZTest_Base_Extra", nestedName.Split('-')[0]);
            Assert.AreEqual(2, outerName.Split('-').Length);
            Assert.AreEqual(2, nestedName.Split('-').Length);
        }

        /// <summary>Verifies that the arm literal resolves to the arm trigger mode.</summary>
        [Test]
        public void ResolveOccupancyTriggerMode_ArmLiteral_ReturnsOccupancyArm()
        {
            TriggerMode mode = ResolveOccupancyTriggerMode("occupancy_arm");

            Assert.AreEqual(TriggerMode.OccupancyArm, mode);
        }

        /// <summary>Verifies that the trigger literal resolves to the trigger trigger mode.</summary>
        [Test]
        public void ResolveOccupancyTriggerMode_TriggerLiteral_ReturnsOccupancyTrigger()
        {
            TriggerMode mode = ResolveOccupancyTriggerMode("occupancy_trigger");

            Assert.AreEqual(TriggerMode.OccupancyTrigger, mode);
        }

        /// <summary>Verifies that the disarm literal resolves to the disarm trigger mode.</summary>
        [Test]
        public void ResolveOccupancyTriggerMode_DisarmLiteral_ReturnsOccupancyDisarm()
        {
            TriggerMode mode = ResolveOccupancyTriggerMode("occupancy_disarm");

            Assert.AreEqual(TriggerMode.OccupancyDisarm, mode);
        }

        /// <summary>Verifies that an unrecognized literal falls back to the disarm trigger mode.</summary>
        [Test]
        public void ResolveOccupancyTriggerMode_UnknownLiteral_FallsBackToOccupancyDisarm()
        {
            TriggerMode mode = ResolveOccupancyTriggerMode("collision");

            Assert.AreEqual(TriggerMode.OccupancyDisarm, mode);
        }

        /// <summary>Verifies that the track length check passes when the segment count equals the corridor depth.
        /// </summary>
        [Test]
        public void ValidateTrackLengthCoversCorridor_SegmentCountEqualsDepth_ReturnsNull()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Boundary", 1f, 3, "Padding", 5000f);

            string error = ValidateTrackLengthCoversCorridor(template);

            Assert.IsNull(error);
        }

        /// <summary>Verifies that the track length check fails one segment below the corridor depth.</summary>
        [Test]
        public void ValidateTrackLengthCoversCorridor_SegmentCountOneBelowDepth_ReturnsError()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Short", 1f, 3, "Padding", 5000.5f);

            string error = ValidateTrackLengthCoversCorridor(template);

            Assert.IsNotNull(error);
            StringAssert.Contains("Unable to generate from template 'ZZTest_Short'.", error);
            StringAssert.Contains("must cover the segments_per_corridor value of 3", error);
        }

        /// <summary>Verifies that the track length check passes when the segment count exceeds the corridor depth.
        /// </summary>
        [Test]
        public void ValidateTrackLengthCoversCorridor_SegmentCountAboveDepth_ReturnsNull()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Roomy", 1f, 3, "Padding", 1000f);

            string error = ValidateTrackLengthCoversCorridor(template);

            Assert.IsNull(error);
        }

        /// <summary>Verifies that the track length check is driven by the template's longest segment.</summary>
        [Test]
        public void ValidateTrackLengthCoversCorridor_LongestSegmentTooLong_ReturnsErrorNamingThatSegment()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Mixed", 1f, 3, "Padding", 1000f, 6000f);

            string error = ValidateTrackLengthCoversCorridor(template);

            Assert.IsNotNull(error);
            StringAssert.Contains("longest segment of 6000 Unity units yields at most 2", error);
            StringAssert.Contains("default track length 15000", error);
            StringAssert.Contains("must cover the segments_per_corridor value of 3", error);
        }

        /// <summary>Verifies that a template whose segments carry no length reports a positive length error.</summary>
        [Test]
        public void ValidateTrackLengthCoversCorridor_ZeroLengthSegments_ReturnsPositiveLengthError()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_ZeroLength", CmPerUnityUnit, 1, "Padding", 0f);

            string error = ValidateTrackLengthCoversCorridor(template);

            Assert.IsNotNull(error);
            StringAssert.Contains("longest segment measures 0 Unity units", error);
            StringAssert.Contains("Every segment length must be positive", error);
        }

        /// <summary>Verifies that the hand-authored asset check passes when every required asset is present.</summary>
        [Test]
        public void ValidateHandAuthoredAssets_EveryRequiredAssetPresent_ReturnsNull()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Assets", CmPerUnityUnit, 1, "Padding", 30f);

            string error = ValidateHandAuthoredAssets(template);

            Assert.IsNull(error);
        }

        /// <summary>Verifies that the hand-authored asset check names the padding prefab it cannot resolve.</summary>
        [Test]
        public void ValidateHandAuthoredAssets_MissingPaddingPrefab_ReturnsErrorNamingThePath()
        {
            TaskTemplate template = BuildInMemoryTemplate(
                "ZZTest_NoPadding",
                CmPerUnityUnit,
                1,
                "ZZTest_AbsentPadding",
                30f
            );

            string error = ValidateHandAuthoredAssets(template);

            Assert.IsNotNull(error);
            StringAssert.Contains("Every hand-authored asset the pipeline references must exist", error);
            StringAssert.Contains("ZZTest_AbsentPadding.prefab", error);
            StringAssert.Contains("Restore them from version control", error);
        }

        /// <summary>Verifies that the required hand-authored set carries the cue shader reference.</summary>
        [Test]
        public void BuildRequiredHandAuthoredPaths_AnyTemplate_CarriesTheCueShaderReference()
        {
            TaskTemplate template = BuildInMemoryTemplate("ZZTest_Shader", CmPerUnityUnit, 1, "Padding", 30f);

            string[] requiredPaths = BuildRequiredHandAuthoredPaths(template);

            CollectionAssert.Contains(requiredPaths, MaterialsFolder + "/_CueShaderReference.mat");
        }

        /// <summary>Verifies that two templates declaring one cue identity with two textures block generation.
        /// </summary>
        [Test]
        public void CreateFromTemplate_CueIdentityDeclaredWithTwoTextures_ReturnsPreflightConflictError()
        {
            WriteSingleTrialTemplate("ZZTest_ConflictOne", FirstTextureName);
            WriteSingleTrialTemplate("ZZTest_ConflictTwo", SecondTextureName);

            string result = Generate("ZZTest_ConflictOne");

            StringAssert.StartsWith("error: Unable to generate. Each cue identity must declare one texture", result);
            StringAssert.Contains("Cue 'ZZA at 33cm'", result);
            StringAssert.Contains($"ZZTest_ConflictOne -> '{FirstTextureName}'", result);
            StringAssert.Contains($"ZZTest_ConflictTwo -> '{SecondTextureName}'", result);
        }

        /// <summary>Verifies that the cue-texture conflict aborts before any cue or task asset is written.</summary>
        [Test]
        public void CreateFromTemplate_CueIdentityDeclaredWithTwoTextures_WritesNoAssets()
        {
            WriteSingleTrialTemplate("ZZTest_ConflictOne", FirstTextureName);
            WriteSingleTrialTemplate("ZZTest_ConflictTwo", SecondTextureName);

            Generate("ZZTest_ConflictOne");

            Assert.IsNull(LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab"));
            Assert.IsNull(LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat"));
            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_ConflictOne-T1.prefab"));
            Assert.IsNull(LoadAsset<GameObject>($"{TasksFolder}/ZZTest_ConflictOne.prefab"));
        }

        /// <summary>Verifies that one cue name at two lengths resolves to two separate cue identities.</summary>
        [Test]
        public void CreateFromTemplate_CueNameSharedAtTwoLengths_GeneratesOneCueAssetPerLength()
        {
            TemplateYaml shortTemplate = SingleTrialTemplate(
                "collision",
                showBoundary: false,
                occupancyDurationMs: null
            );
            WriteTemplate("ZZTest_LengthOne", shortTemplate);
            TemplateYaml longTemplate = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            longTemplate.cues.Add(TestCue(FirstCueName, FirstCueCode, SecondCueLengthCm, SecondTextureName));
            longTemplate.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            WriteTemplate("ZZTest_LengthTwo", longTemplate);

            Assert.AreEqual(SuccessMessage("ZZTest_LengthOne"), Generate("ZZTest_LengthOne"));
            Assert.AreEqual(SuccessMessage("ZZTest_LengthTwo"), Generate("ZZTest_LengthTwo"));

            GameObject shortCuePrefab = LoadAsset<GameObject>($"{CuesFolder}/Cue_ZZA_33cm.prefab");
            GameObject longCuePrefab = LoadAsset<GameObject>($"{CuesFolder}/Cue_ZZA_44cm.prefab");
            Assert.IsNotNull(shortCuePrefab, "Cue_ZZA_33cm.prefab was not generated.");
            Assert.IsNotNull(longCuePrefab, "Cue_ZZA_44cm.prefab was not generated.");
            Assert.AreEqual("Cue_ZZA_33cm", shortCuePrefab.name);
            Assert.AreEqual("Cue_ZZA_44cm", longCuePrefab.name);
            AssertVector3(
                new Vector3(-FirstCueLengthUnity, 1f, 1f),
                shortCuePrefab.transform.Find("Right").localScale,
                "short cue right wall scale"
            );
            AssertVector3(
                new Vector3(-SecondCueLengthUnity, 1f, 1f),
                longCuePrefab.transform.Find("Right").localScale,
                "long cue right wall scale"
            );
        }

        /// <summary>Verifies that a sibling template the preflight cannot load aborts the whole request.</summary>
        [Test]
        public void CreateFromTemplate_SiblingTemplateFailsToLoad_ReturnsPreflightAbortError()
        {
            WriteSingleTrialTemplate("ZZTest_Good", FirstTextureName);
            TemplateYaml brokenTemplate = SingleTrialTemplate(
                "collision",
                showBoundary: false,
                occupancyDurationMs: null
            );
            brokenTemplate.includeCuesSection = false;
            WriteTemplate("ZZTest_Broken", brokenTemplate);

            string result = Generate("ZZTest_Good");

            StringAssert.StartsWith("error: Unable to run the cross-template cue-texture preflight.", result);
            StringAssert.Contains("ZZTest_Broken", result);
            StringAssert.Contains("No cues defined in template.", result);
        }

        /// <summary>Verifies that two templates agreeing on a cue identity share one cue material.</summary>
        [Test]
        public void CreateFromTemplate_TwoTemplatesAgreeOnACueIdentity_ShareOneCueMaterial()
        {
            WriteSingleTrialTemplate("ZZTest_ShareOne", FirstTextureName);
            WriteSingleTrialTemplate("ZZTest_ShareTwo", FirstTextureName);
            Assert.AreEqual(SuccessMessage("ZZTest_ShareOne"), Generate("ZZTest_ShareOne"));
            Assert.AreEqual(SuccessMessage("ZZTest_ShareTwo"), Generate("ZZTest_ShareTwo"));

            Material firstMaterial = FirstCueMaterial("ZZTest_ShareOne", FirstTrialName);
            Material secondMaterial = FirstCueMaterial("ZZTest_ShareTwo", FirstTrialName);

            Assert.AreEqual($"{MaterialsFolder}/{FirstCueAssetStem}.mat", AssetDatabase.GetAssetPath(firstMaterial));
            Assert.AreEqual($"{MaterialsFolder}/{FirstCueAssetStem}.mat", AssetDatabase.GetAssetPath(secondMaterial));
            Assert.AreEqual(firstMaterial.GetInstanceID(), secondMaterial.GetInstanceID());
        }

        /// <summary>Verifies that generation writes the cue prefab and material under the length-suffixed name.
        /// </summary>
        [Test]
        public void CreateFromTemplate_NewCue_WritesTheLengthSuffixedPrefabAndMaterial()
        {
            WriteSingleTrialTemplate("ZZTest_Cue", FirstTextureName);

            Assert.AreEqual(SuccessMessage("ZZTest_Cue"), Generate("ZZTest_Cue"));

            GameObject cuePrefab = LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab");
            Material cueMaterial = LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat");
            Assert.IsNotNull(cuePrefab);
            Assert.AreEqual(FirstCueAssetStem, cuePrefab.name);
            Assert.IsNotNull(cueMaterial);
            Texture2D declaredTexture = LoadAsset<Texture2D>($"{TexturesFolder}/{FirstTextureName}");
            Assert.AreEqual(declaredTexture.GetInstanceID(), cueMaterial.GetTexture("_MainTex").GetInstanceID());
        }

        /// <summary>Verifies that a generated cue prefab mirrors its right wall against its left wall.</summary>
        [Test]
        public void CreateFromTemplate_NewCue_BuildsMirroredLeftAndRightWalls()
        {
            WriteSingleTrialTemplate("ZZTest_Walls", FirstTextureName);
            Generate("ZZTest_Walls");

            GameObject cuePrefab = LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab");
            Transform right = cuePrefab.transform.Find("Right");
            Transform left = cuePrefab.transform.Find("Left");

            AssertVector3(new Vector3(0.49f, 0.5f, FirstCueLengthUnity / 2f), right.localPosition, "right position");
            AssertVector3(new Vector3(-FirstCueLengthUnity, 1f, 1f), right.localScale, "right scale");
            AssertRotation(Quaternion.Euler(0f, 90f, 0f), right.localRotation, "right");
            AssertVector3(new Vector3(-0.49f, 0.5f, FirstCueLengthUnity / 2f), left.localPosition, "left position");
            AssertVector3(new Vector3(FirstCueLengthUnity, 1f, 1f), left.localScale, "left scale");
            AssertRotation(Quaternion.Euler(0f, -90f, 0f), left.localRotation, "left");
        }

        /// <summary>Verifies that a cached cue material built from another texture aborts the cue build.</summary>
        [Test]
        public void CreateFromTemplate_CachedMaterialBuiltFromAnotherTexture_ReturnsCueBuildError()
        {
            WriteSingleTrialTemplate("ZZTest_Swap", FirstTextureName);
            Assert.AreEqual(SuccessMessage("ZZTest_Swap"), Generate("ZZTest_Swap"));
            WriteSingleTrialTemplate("ZZTest_Swap", SecondTextureName);
            LogAssert.Expect(LogType.Error, new Regex("was built from a different texture"));

            string result = Generate("ZZTest_Swap");

            Assert.AreEqual(
                "error: Unable to generate the task. Every cue prefab the template declares must build, but "
                    + "at least one failed. The preceding error names the cue.",
                result
            );
        }

        /// <summary>Verifies that the cue build abort leaves the previous generation's assets on disk.</summary>
        [Test]
        public void CreateFromTemplate_CachedMaterialConflictAborts_LeavesThePreviousGenerationIntact()
        {
            WriteSingleTrialTemplate("ZZTest_Swap", FirstTextureName);
            Generate("ZZTest_Swap");
            WriteSingleTrialTemplate("ZZTest_Swap", SecondTextureName);
            LogAssert.Expect(LogType.Error, new Regex("was built from a different texture"));

            Generate("ZZTest_Swap");

            Assert.IsNotNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Swap-T1.prefab"));
            Assert.IsNotNull(LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Swap.prefab"));
            Assert.IsNotNull(LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab"));
            Material cachedMaterial = LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat");
            Texture2D originalTexture = LoadAsset<Texture2D>($"{TexturesFolder}/{FirstTextureName}");
            Assert.AreEqual(originalTexture.GetInstanceID(), cachedMaterial.GetTexture("_MainTex").GetInstanceID());
        }

        /// <summary>Verifies that a cue texture that does not import as a Texture2D aborts the cue build.</summary>
        [Test]
        public void CreateFromTemplate_CueTextureDoesNotImportAsTexture_ReturnsCueBuildError()
        {
            string decoyTextureName = "ZZTest_NotATexture.txt";
            WriteTextFile($"{TexturesFolder}/{decoyTextureName}", "This file is not an image.");
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, decoyTextureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            WriteTemplate("ZZTest_BadTexture", template);
            LogAssert.Expect(LogType.Error, new Regex("Unable to build the cue prefab for"));

            string result = Generate("ZZTest_BadTexture");

            Assert.AreEqual(
                "error: Unable to generate the task. Every cue prefab the template declares must build, but "
                    + "at least one failed. The preceding error names the cue.",
                result
            );
            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_BadTexture-T1.prefab"));
        }

        /// <summary>Verifies that a cue prefab surviving without its material rebuilds and rewrites both assets.
        /// </summary>
        [Test]
        public void CreateFromTemplate_CuePrefabSurvivesWithoutItsMaterial_RebuildsBothAssets()
        {
            WriteSingleTrialTemplate("ZZTest_Rebuild", FirstTextureName);
            Generate("ZZTest_Rebuild");
            Assert.IsTrue(AssetDatabase.DeleteAsset($"{MaterialsFolder}/{FirstCueAssetStem}.mat"));
            Assert.IsNull(LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat"));

            Assert.AreEqual(SuccessMessage("ZZTest_Rebuild"), Regenerate("ZZTest_Rebuild"));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Material rebuiltMaterial = LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat");
            Assert.IsNotNull(rebuiltMaterial, "The regeneration did not rebuild the deleted cue material.");
            Assert.AreEqual(FirstCueAssetStem, rebuiltMaterial.name);
            Assert.IsNotNull(rebuiltMaterial.GetTexture("_MainTex"), "The rebuilt material carries no texture.");

            GameObject cuePrefab = LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab");
            Assert.IsNotNull(cuePrefab, "The regeneration did not rewrite the cue prefab.");
            Assert.AreEqual(2, cuePrefab.GetComponentsInChildren<MeshRenderer>(includeInactive: true).Length);
        }

        /// <summary>Verifies that the rebuilt cue prefab points its wall renderers at the rebuilt material.</summary>
        /// <remarks>
        /// The regeneration repairing that state has to land the new material on both wall renderers.
        /// </remarks>
        [Test]
        public void CreateFromTemplate_CuePrefabSurvivesWithoutItsMaterial_RelinksTheRebuiltMaterial()
        {
            WriteSingleTrialTemplate("ZZTest_Relink", FirstTextureName);
            Generate("ZZTest_Relink");
            Assert.IsTrue(AssetDatabase.DeleteAsset($"{MaterialsFolder}/{FirstCueAssetStem}.mat"));

            Assert.AreEqual(SuccessMessage("ZZTest_Relink"), Regenerate("ZZTest_Relink"));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Material rebuiltMaterial = LoadAsset<Material>($"{MaterialsFolder}/{FirstCueAssetStem}.mat");
            GameObject cuePrefab = LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab");
            foreach (
                MeshRenderer wallRenderer in cuePrefab.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
            )
            {
                Assert.IsNotNull(
                    wallRenderer.sharedMaterial,
                    $"The rebuilt cue prefab's '{wallRenderer.gameObject.name}' renderer lost its material."
                );
                Assert.AreEqual(rebuiltMaterial.GetInstanceID(), wallRenderer.sharedMaterial.GetInstanceID());
            }

            Material segmentMaterial = FirstCueMaterial("ZZTest_Relink", FirstTrialName);
            Assert.IsNotNull(segmentMaterial, "The regenerated segment's first cue instance lost its material.");
            Assert.AreEqual(rebuiltMaterial.GetInstanceID(), segmentMaterial.GetInstanceID());
        }

        /// <summary>Verifies that generation names each segment prefab TemplateName-TrialName.</summary>
        [Test]
        public void CreateFromTemplate_Template_NamesEachSegmentPrefabTemplateNameHyphenTrialName()
        {
            WriteSingleTrialTemplate("ZZTest_Segment", FirstTextureName);

            Generate("ZZTest_Segment");

            GameObject segment = LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Segment-T1.prefab");
            Assert.IsNotNull(segment);
            Assert.AreEqual("ZZTest_Segment-T1", segment.name);
        }

        /// <summary>Verifies that a template declaring no cue offset leaves the segment root on the origin.</summary>
        [Test]
        public void CreateFromTemplate_ZeroCueOffset_LeavesTheSegmentRootOnTheOrigin()
        {
            WriteSingleTrialTemplate("ZZTest_NoOffset", FirstTextureName);

            Generate("ZZTest_NoOffset");

            GameObject segment = LoadSegment("ZZTest_NoOffset", FirstTrialName);
            AssertVector3(Vector3.zero, segment.transform.localPosition, "segment root position");
        }

        /// <summary>Verifies that a non-zero cue offset shifts the segment root upstream along Z.</summary>
        [Test]
        public void CreateFromTemplate_NonZeroCueOffset_ShiftsTheSegmentRootUpstream()
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 15f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            WriteTemplate("ZZTest_Offset", template);

            Generate("ZZTest_Offset");

            GameObject segment = LoadSegment("ZZTest_Offset", FirstTrialName);
            AssertVector3(new Vector3(0f, 0f, -1.5f), segment.transform.localPosition, "segment root position");
        }

        /// <summary>Verifies that a multi-cue trial concatenates its cue instances along the Z axis.</summary>
        [Test]
        public void CreateFromTemplate_MultiCueTrial_ConcatenatesTheCueInstancesAlongZ()
        {
            WriteTemplate("ZZTest_Concat", MultiCueTemplate());

            Generate("ZZTest_Concat");

            GameObject segment = LoadSegment("ZZTest_Concat", FirstTrialName);
            Transform firstCue = segment.transform.GetChild(0);
            Transform secondCue = segment.transform.GetChild(1);
            Transform thirdCue = segment.transform.GetChild(2);
            Assert.AreEqual($"Cue{FirstCueName}", firstCue.name);
            Assert.AreEqual($"Cue{SecondCueName}", secondCue.name);
            Assert.AreEqual($"Cue{FirstCueName}", thirdCue.name);
            AssertVector3(Vector3.zero, firstCue.localPosition, "first cue position");
            AssertVector3(new Vector3(0f, 0f, FirstCueLengthUnity), secondCue.localPosition, "second cue position");
            AssertVector3(
                new Vector3(0f, 0f, FirstCueLengthUnity + SecondCueLengthUnity),
                thirdCue.localPosition,
                "third cue position"
            );
        }

        /// <summary>Verifies that a multi-cue trial sizes its floor and walls to the whole segment length.</summary>
        [Test]
        public void CreateFromTemplate_MultiCueTrial_SizesTheFloorAndWallsToTheSegmentLength()
        {
            WriteTemplate("ZZTest_Shell", MultiCueTemplate());
            float totalLengthUnity = FirstCueLengthUnity + SecondCueLengthUnity + FirstCueLengthUnity;

            Generate("ZZTest_Shell");

            GameObject segment = LoadSegment("ZZTest_Shell", FirstTrialName);
            Transform floor = segment.transform.Find("Floor");
            AssertVector3(new Vector3(0f, 0f, totalLengthUnity / 2f), floor.localPosition, "floor position");
            AssertVector3(new Vector3(0.1f, 1f, totalLengthUnity / 10f), floor.localScale, "floor scale");
            Transform leftWall = segment.transform.Find("Walls/LeftWall");
            AssertVector3(new Vector3(-0.5f, 0.5f, totalLengthUnity / 2f), leftWall.localPosition, "left wall");
            AssertVector3(new Vector3(totalLengthUnity, 1f, 1f), leftWall.localScale, "left wall scale");
            AssertRotation(Quaternion.Euler(0f, -90f, 0f), leftWall.localRotation, "left wall");
            Transform rightWall = segment.transform.Find("Walls/RightWall");
            AssertVector3(new Vector3(0.5f, 0.5f, totalLengthUnity / 2f), rightWall.localPosition, "right wall");
            AssertVector3(new Vector3(totalLengthUnity, 1f, 1f), rightWall.localScale, "right wall scale");
            AssertRotation(Quaternion.Euler(0f, 90f, 0f), rightWall.localRotation, "right wall");
        }

        /// <summary>Verifies that CleanGeneratedSegments removes every segment the template declares.</summary>
        [Test]
        public void CleanGeneratedSegments_TemplateWithTwoTrials_RemovesBothOwnedSegments()
        {
            WriteTemplate("ZZTest_Multi", TwoTrialTemplate());
            Generate("ZZTest_Multi");
            Assert.IsNotNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Multi-T1.prefab"));
            Assert.IsNotNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Multi-T2.prefab"));
            TaskTemplate loadedTemplate = ConfigLoader.LoadTemplate(AbsoluteTemplatePath("ZZTest_Multi"));

            CleanGeneratedSegments(loadedTemplate);

            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Multi-T1.prefab"));
            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Multi-T2.prefab"));
        }

        /// <summary>Verifies that CleanGeneratedSegments spares a segment owned by a nesting template basename.
        /// </summary>
        [Test]
        public void CleanGeneratedSegments_NestingTemplateBasename_RemovesOnlyTheOwnedSegment()
        {
            WriteSingleTrialTemplate("ZZTest_Base", FirstTextureName);
            WriteSingleTrialTemplate("ZZTest_Base_Extra", FirstTextureName);
            Generate("ZZTest_Base");
            Generate("ZZTest_Base_Extra");
            TaskTemplate loadedTemplate = ConfigLoader.LoadTemplate(AbsoluteTemplatePath("ZZTest_Base"));

            CleanGeneratedSegments(loadedTemplate);

            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Base-T1.prefab"));
            Assert.IsNotNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_Base_Extra-T1.prefab"));
        }

        /// <summary>Verifies that an interaction trial places its zone across the declared trigger zone span.</summary>
        [Test]
        public void CreateFromTemplate_InteractionTrial_SpansTheZoneWithTheRootCollider()
        {
            WriteTemplate(
                "ZZTest_Interaction",
                SingleTrialTemplate("interaction", showBoundary: false, occupancyDurationMs: null)
            );

            Generate("ZZTest_Interaction");

            GameObject segment = LoadSegment("ZZTest_Interaction", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            AssertVector3(
                new Vector3(0f, ZoneVerticalOffset, ZoneCenterUnity),
                zone.transform.localPosition,
                "zone position"
            );
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            AssertVector3(new Vector3(1f, 1f, ZoneSizeUnity), rootCollider.size, "root collider size");
            AssertVector3(Vector3.zero, rootCollider.center, "root collider center");
        }

        /// <summary>Verifies that an interaction trial anchors its guidance region on the stimulus location.</summary>
        [Test]
        public void CreateFromTemplate_InteractionTrial_AnchorsTheGuidanceRegionOnTheStimulusLocation()
        {
            WriteTemplate(
                "ZZTest_Guidance",
                SingleTrialTemplate("interaction", showBoundary: false, occupancyDurationMs: null)
            );

            Generate("ZZTest_Guidance");

            GameObject segment = LoadSegment("ZZTest_Guidance", FirstTrialName);
            GuidanceZone guidanceZone = segment.GetComponentInChildren<GuidanceZone>(includeInactive: true);
            Assert.IsNotNull(guidanceZone);
            Assert.AreEqual("GuidanceRegion", guidanceZone.gameObject.name);
            BoxCollider guidanceCollider = guidanceZone.GetComponent<BoxCollider>();
            AssertVector3(new Vector3(1f, 1f, GuidanceColliderDepth), guidanceCollider.size, "guidance size");
            float expectedCenterZ = StimulusLocationUnity - ZoneCenterUnity + GuidanceColliderDepth / 2f;
            AssertVector3(new Vector3(0f, 0f, expectedCenterZ), guidanceCollider.center, "guidance center");
        }

        /// <summary>Verifies that an interaction trial configures its zone from the stimulus trigger zone prefab.
        /// </summary>
        [Test]
        public void CreateFromTemplate_InteractionTrial_ConfiguresTheStimulusZonePrefab()
        {
            WriteTemplate(
                "ZZTest_InteractionSetup",
                SingleTrialTemplate("interaction", showBoundary: false, occupancyDurationMs: null)
            );

            Generate("ZZTest_InteractionSetup");

            GameObject segment = LoadSegment("ZZTest_InteractionSetup", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.AreEqual("StimulusTriggerZone", zone.gameObject.name);
            Assert.AreEqual(TriggerMode.Interaction, zone.triggerMode);
            Assert.AreEqual(FirstTrialName, zone.trialName);
            Assert.IsFalse(zone.showBoundary);
            Assert.IsNull(segment.GetComponentInChildren<OccupancyZone>(includeInactive: true));
        }

        /// <summary>Verifies that a collision trial places its wall just past the stimulus location.</summary>
        [Test]
        public void CreateFromTemplate_CollisionTrial_PlacesAThinWallOnTheStimulusLocation()
        {
            WriteTemplate(
                "ZZTest_Collision",
                SingleTrialTemplate("collision", showBoundary: true, occupancyDurationMs: null)
            );

            Generate("ZZTest_Collision");

            GameObject segment = LoadSegment("ZZTest_Collision", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            float expectedWallCenter = StimulusLocationUnity + GuidanceColliderDepth / 2f;
            AssertVector3(
                new Vector3(0f, ZoneVerticalOffset, expectedWallCenter),
                zone.transform.localPosition,
                "zone position"
            );
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            AssertVector3(new Vector3(1f, 1f, GuidanceColliderDepth), rootCollider.size, "root collider size");
            AssertVector3(Vector3.zero, rootCollider.center, "root collider center");
        }

        /// <summary>Verifies that a collision trial strips the guidance region off the stimulus zone prefab.</summary>
        [Test]
        public void CreateFromTemplate_CollisionTrial_StripsTheGuidanceRegionChild()
        {
            WriteTemplate(
                "ZZTest_CollisionSetup",
                SingleTrialTemplate("collision", showBoundary: true, occupancyDurationMs: null)
            );

            Generate("ZZTest_CollisionSetup");

            GameObject segment = LoadSegment("ZZTest_CollisionSetup", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.AreEqual("StimulusTriggerZone", zone.gameObject.name);
            Assert.AreEqual(TriggerMode.Collision, zone.triggerMode);
            Assert.AreEqual(FirstTrialName, zone.trialName);
            Assert.IsTrue(zone.showBoundary);
            Assert.IsNull(segment.GetComponentInChildren<GuidanceZone>(includeInactive: true));
            Assert.IsNull(zone.transform.Find("GuidanceRegion"));
        }

        /// <summary>Verifies that an occupancy trial places its root past the occupancy span.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyTrial_PlacesTheRootPastTheOccupancySpan()
        {
            WriteTemplate(
                "ZZTest_Occupancy",
                SingleTrialTemplate("occupancy_disarm", showBoundary: true, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_Occupancy");

            GameObject segment = LoadSegment("ZZTest_Occupancy", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            float expectedRootZ = StimulusLocationUnity + ZoneSizeUnity / 2f;
            AssertVector3(
                new Vector3(0f, ZoneVerticalOffset, expectedRootZ),
                zone.transform.localPosition,
                "zone position"
            );
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            AssertVector3(new Vector3(1f, 1f, ZoneSizeUnity), rootCollider.size, "root collider size");
            AssertVector3(Vector3.zero, rootCollider.center, "root collider center");
        }

        /// <summary>Verifies that an occupancy trial sizes its occupancy region to the declared zone span.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyTrial_SizesTheOccupancyRegionToTheZoneSpan()
        {
            WriteTemplate(
                "ZZTest_OccupancyRegion",
                SingleTrialTemplate("occupancy_disarm", showBoundary: true, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_OccupancyRegion");

            GameObject segment = LoadSegment("ZZTest_OccupancyRegion", FirstTrialName);
            OccupancyZone occupancyZone = segment.GetComponentInChildren<OccupancyZone>(includeInactive: true);
            Assert.IsNotNull(occupancyZone);
            Assert.AreEqual("OccupancyRegion", occupancyZone.gameObject.name);
            BoxCollider occupancyCollider = occupancyZone.GetComponent<BoxCollider>();
            float expectedCenterZ = ZoneCenterUnity - (StimulusLocationUnity + ZoneSizeUnity / 2f);
            AssertVector3(new Vector3(1f, 1f, ZoneSizeUnity), occupancyCollider.size, "occupancy size");
            AssertVector3(new Vector3(0f, 0f, expectedCenterZ), occupancyCollider.center, "occupancy center");
        }

        /// <summary>Verifies that an occupancy trial anchors its guidance brake on the occupancy region's end.
        /// </summary>
        [Test]
        public void CreateFromTemplate_OccupancyTrial_AnchorsTheGuidanceBrakeOnTheOccupancyRegionEnd()
        {
            WriteTemplate(
                "ZZTest_OccupancyBrake",
                SingleTrialTemplate("occupancy_disarm", showBoundary: true, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_OccupancyBrake");

            GameObject segment = LoadSegment("ZZTest_OccupancyBrake", FirstTrialName);
            OccupancyGuidanceZone guidanceZone = segment.GetComponentInChildren<OccupancyGuidanceZone>(
                includeInactive: true
            );
            Assert.IsNotNull(guidanceZone);
            Assert.AreEqual("OccupancyGuidanceRegion", guidanceZone.gameObject.name);
            BoxCollider guidanceCollider = guidanceZone.GetComponent<BoxCollider>();
            float occupancyCenterOffset = ZoneCenterUnity - (StimulusLocationUnity + ZoneSizeUnity / 2f);
            float expectedCenterZ = occupancyCenterOffset + ZoneSizeUnity / 2f - GuidanceColliderDepth / 2f;
            AssertVector3(new Vector3(1f, 1f, GuidanceColliderDepth), guidanceCollider.size, "brake size");
            AssertVector3(new Vector3(0f, 0f, expectedCenterZ), guidanceCollider.center, "brake center");
        }

        /// <summary>Verifies that an occupancy trial writes its declared duration onto the occupancy zone.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyTrial_WritesTheDeclaredOccupancyDuration()
        {
            WriteTemplate(
                "ZZTest_OccupancyDuration",
                SingleTrialTemplate("occupancy_arm", showBoundary: false, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_OccupancyDuration");

            GameObject segment = LoadSegment("ZZTest_OccupancyDuration", FirstTrialName);
            OccupancyZone occupancyZone = segment.GetComponentInChildren<OccupancyZone>(includeInactive: true);
            Assert.AreEqual(OccupancyDurationMs, occupancyZone.occupancyDurationMs, GeometryTolerance);
        }

        /// <summary>Verifies that the disarm literal configures the occupancy prefab in disarm mode.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyDisarmTrial_ConfiguresTheOccupancyPrefabInDisarmMode()
        {
            WriteTemplate(
                "ZZTest_Disarm",
                SingleTrialTemplate("occupancy_disarm", showBoundary: true, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_Disarm");

            GameObject segment = LoadSegment("ZZTest_Disarm", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.AreEqual("OccupancyTriggerZone", zone.gameObject.name);
            Assert.AreEqual(TriggerMode.OccupancyDisarm, zone.triggerMode);
            Assert.AreEqual(FirstTrialName, zone.trialName);
            Assert.IsTrue(zone.showBoundary);
        }

        /// <summary>Verifies that the arm literal configures the occupancy prefab in arm mode.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyArmTrial_ConfiguresTheOccupancyPrefabInArmMode()
        {
            WriteTemplate(
                "ZZTest_Arm",
                SingleTrialTemplate("occupancy_arm", showBoundary: false, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_Arm");

            GameObject segment = LoadSegment("ZZTest_Arm", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.AreEqual("OccupancyTriggerZone", zone.gameObject.name);
            Assert.AreEqual(TriggerMode.OccupancyArm, zone.triggerMode);
            Assert.AreEqual(FirstTrialName, zone.trialName);
            Assert.IsFalse(zone.showBoundary);
        }

        /// <summary>Verifies that the trigger literal configures the occupancy prefab in trigger mode.</summary>
        [Test]
        public void CreateFromTemplate_OccupancyTriggerTrial_ConfiguresTheOccupancyPrefabInTriggerMode()
        {
            WriteTemplate(
                "ZZTest_Trigger",
                SingleTrialTemplate("occupancy_trigger", showBoundary: false, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_Trigger");

            GameObject segment = LoadSegment("ZZTest_Trigger", FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.AreEqual("OccupancyTriggerZone", zone.gameObject.name);
            Assert.AreEqual(TriggerMode.OccupancyTrigger, zone.triggerMode);
            Assert.AreEqual(FirstTrialName, zone.trialName);
        }

        /// <summary>Verifies that an interaction trial hiding its boundary disables the zone's boundary renderer.
        /// </summary>
        [Test]
        public void CreateFromTemplate_InteractionTrialHidesTheBoundary_DisablesTheBoundaryRenderer()
        {
            WriteTemplate(
                "ZZTest_InteractionHidden",
                SingleTrialTemplate("interaction", showBoundary: false, occupancyDurationMs: null)
            );

            Generate("ZZTest_InteractionHidden");

            Assert.IsFalse(BoundaryRenderer("ZZTest_InteractionHidden").enabled);
        }

        /// <summary>Verifies that a collision trial hiding its boundary disables the zone's boundary renderer.
        /// </summary>
        [Test]
        public void CreateFromTemplate_CollisionTrialHidesTheBoundary_DisablesTheBoundaryRenderer()
        {
            WriteTemplate(
                "ZZTest_CollisionHidden",
                SingleTrialTemplate("collision", showBoundary: false, occupancyDurationMs: null)
            );

            Generate("ZZTest_CollisionHidden");

            Assert.IsFalse(BoundaryRenderer("ZZTest_CollisionHidden").enabled);
        }

        /// <summary>Verifies that an occupancy trial hiding its boundary disables the zone's boundary renderer.
        /// </summary>
        [Test]
        public void CreateFromTemplate_OccupancyTrialHidesTheBoundary_DisablesTheBoundaryRenderer()
        {
            WriteTemplate(
                "ZZTest_OccupancyHidden",
                SingleTrialTemplate("occupancy_disarm", showBoundary: false, occupancyDurationMs: OccupancyDurationMs)
            );

            Generate("ZZTest_OccupancyHidden");

            Assert.IsFalse(BoundaryRenderer("ZZTest_OccupancyHidden").enabled);
        }

        /// <summary>Verifies that a trial showing its boundary leaves the zone's boundary renderer enabled.</summary>
        [Test]
        public void CreateFromTemplate_TrialShowsTheBoundary_EnablesTheBoundaryRenderer()
        {
            WriteTemplate(
                "ZZTest_BoundaryShown",
                SingleTrialTemplate("collision", showBoundary: true, occupancyDurationMs: null)
            );

            Generate("ZZTest_BoundaryShown");

            Assert.IsTrue(BoundaryRenderer("ZZTest_BoundaryShown").enabled);
        }

        /// <summary>Verifies that a trigger type no placement branch handles fails without writing a segment.</summary>
        [Test]
        public void BuildSegmentPrefabs_UnrecognizedTriggerType_FailsWithoutWritingTheSegment()
        {
            WriteSingleTrialTemplate("ZZTest_KnownMode", FirstTextureName);
            Assert.AreEqual(SuccessMessage("ZZTest_KnownMode"), Generate("ZZTest_KnownMode"));
            TaskTemplate template = InMemoryTriggerTypeTemplate("ZZTest_UnknownMode", "teleport");
            LogAssert.Expect(LogType.Error, new Regex("The trigger_type must be one of"));

            bool built = BuildSegmentPrefabs(template);

            Assert.IsFalse(built);
            Assert.IsNull(LoadAsset<GameObject>($"{PrefabsFolder}/ZZTest_UnknownMode-T1.prefab"));
        }

        /// <summary>Verifies that generation reports the path the task prefab was written to.</summary>
        [Test]
        public void CreateFromTemplate_ValidTemplate_ReturnsTheSuccessMessageNamingTheSavePath()
        {
            WriteSingleTrialTemplate("ZZTest_Result", FirstTextureName);

            string result = Generate("ZZTest_Result");

            Assert.AreEqual($"success: Task prefab saved to {TasksFolder}/ZZTest_Result.prefab", result);
            Assert.IsNotNull(LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Result.prefab"));
        }

        /// <summary>Verifies that generation builds one corridor per segment combination.</summary>
        [Test]
        public void CreateFromTemplate_TwoTrialsAtDepthTwo_BuildsOneCorridorPerCombination()
        {
            WriteTemplate("ZZTest_Corridors", TwoTrialTemplate());

            Generate("ZZTest_Corridors");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Corridors.prefab");
            Assert.AreEqual(4, task.transform.childCount);
            Assert.AreEqual("Corridor00", task.transform.GetChild(0).name);
            Assert.AreEqual("Corridor01", task.transform.GetChild(1).name);
            Assert.AreEqual("Corridor10", task.transform.GetChild(2).name);
            Assert.AreEqual("Corridor11", task.transform.GetChild(3).name);
        }

        /// <summary>Verifies that generation spaces successive corridors along the X axis.</summary>
        [Test]
        public void CreateFromTemplate_TwoTrialsAtDepthTwo_SpacesCorridorsAlongX()
        {
            WriteTemplate("ZZTest_Spacing", TwoTrialTemplate());

            Generate("ZZTest_Spacing");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Spacing.prefab");
            for (int index = 0; index < task.transform.childCount; index++)
            {
                Vector3 expected = new Vector3(index * CorridorSpacingUnity, 0f, 0f);
                AssertVector3(expected, task.transform.GetChild(index).localPosition, $"corridor {index} position");
            }
        }

        /// <summary>Verifies that a corridor concatenates its segments along Z from their own lengths.</summary>
        [Test]
        public void CreateFromTemplate_MixedLengthCorridor_ConcatenatesSegmentsAlongZ()
        {
            WriteTemplate("ZZTest_Concatenation", TwoTrialTemplate());
            float cueOffsetUnity = 1f;

            Generate("ZZTest_Concatenation");

            Transform corridor = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Concatenation.prefab")
                .transform.Find("Corridor01");
            Assert.AreEqual("ZZTest_Concatenation-T1", corridor.GetChild(0).name);
            Assert.AreEqual("ZZTest_Concatenation-T2", corridor.GetChild(1).name);
            AssertVector3(new Vector3(0f, 0f, -cueOffsetUnity), corridor.GetChild(0).localPosition, "first segment");
            AssertVector3(
                new Vector3(0f, 0f, FirstCueLengthUnity - cueOffsetUnity),
                corridor.GetChild(1).localPosition,
                "second segment"
            );
        }

        /// <summary>Verifies that each corridor anchors its padding on its own accumulated length.</summary>
        [Test]
        public void CreateFromTemplate_MixedLengthCorridors_AnchorPaddingOnTheirOwnLength()
        {
            WriteTemplate("ZZTest_Padding", TwoTrialTemplate());
            float cueOffsetUnity = 1f;
            float shortTrialLength = FirstCueLengthUnity;
            float longTrialLength = FirstCueLengthUnity + SecondCueLengthUnity;

            Generate("ZZTest_Padding");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Padding.prefab");
            Transform shortCorridorPadding = task.transform.Find("Corridor00").GetChild(2);
            Assert.AreEqual("Padding", shortCorridorPadding.name);
            AssertVector3(
                new Vector3(0f, 0f, shortTrialLength + shortTrialLength - cueOffsetUnity),
                shortCorridorPadding.localPosition,
                "short corridor padding"
            );
            Transform mixedCorridorPadding = task.transform.Find("Corridor01").GetChild(2);
            AssertVector3(
                new Vector3(0f, 0f, shortTrialLength + longTrialLength - cueOffsetUnity),
                mixedCorridorPadding.localPosition,
                "mixed corridor padding"
            );
        }

        /// <summary>Verifies that only the first segment of each corridor keeps its stimulus trigger zone.</summary>
        [Test]
        public void CreateFromTemplate_Corridor_KeepsTheStimulusZoneOnTheFirstSegmentOnly()
        {
            WriteTemplate("ZZTest_ZoneStrip", TwoTrialTemplate());

            Generate("ZZTest_ZoneStrip");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_ZoneStrip.prefab");
            foreach (Transform corridor in task.transform)
            {
                StimulusTriggerZone firstZone = corridor
                    .GetChild(0)
                    .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
                StimulusTriggerZone secondZone = corridor
                    .GetChild(1)
                    .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
                Assert.IsNotNull(firstZone, $"{corridor.name} first segment zone");
                Assert.IsNull(secondZone, $"{corridor.name} second segment zone");
            }
        }

        /// <summary>Verifies that the first segment's boundary visibility comes from its own trial.</summary>
        [Test]
        public void CreateFromTemplate_Corridor_AppliesTheFirstSegmentTrialBoundaryVisibility()
        {
            WriteTemplate("ZZTest_Visibility", TwoTrialTemplate());

            Generate("ZZTest_Visibility");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_Visibility.prefab");
            Transform firstTrialCorridor = task.transform.Find("Corridor01");
            Transform secondTrialCorridor = task.transform.Find("Corridor10");
            Assert.AreEqual("ZZTest_Visibility-T1", firstTrialCorridor.GetChild(0).name);
            Assert.AreEqual("ZZTest_Visibility-T2", secondTrialCorridor.GetChild(0).name);
            Assert.IsFalse(
                firstTrialCorridor
                    .GetChild(0)
                    .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true)
                    .showBoundary
            );
            Assert.IsTrue(
                secondTrialCorridor
                    .GetChild(0)
                    .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true)
                    .showBoundary
            );
        }

        /// <summary>Verifies that the first segment's boundary renderer follows its own trial's visibility.</summary>
        [Test]
        public void CreateFromTemplate_Corridor_SyncsTheFirstSegmentBoundaryRendererWithItsTrial()
        {
            WriteTemplate("ZZTest_RendererVisibility", TwoTrialTemplate());

            Generate("ZZTest_RendererVisibility");

            GameObject task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_RendererVisibility.prefab");
            StimulusTriggerZone hiddenZone = task
                .transform.Find("Corridor01")
                .GetChild(0)
                .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            StimulusTriggerZone shownZone = task
                .transform.Find("Corridor10")
                .GetChild(0)
                .GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.IsFalse(hiddenZone.GetComponent<MeshRenderer>().enabled, "hidden trial boundary renderer");
            Assert.IsTrue(shownZone.GetComponent<MeshRenderer>().enabled, "shown trial boundary renderer");
        }

        /// <summary>Verifies that the generated task component stores its configuration path and requirement.</summary>
        [Test]
        public void CreateFromTemplate_ValidTemplate_StoresTheConfigPathAndRequiresInteraction()
        {
            WriteSingleTrialTemplate("ZZTest_TaskFields", FirstTextureName);

            Generate("ZZTest_TaskFields");

            Task task = LoadAsset<GameObject>($"{TasksFolder}/ZZTest_TaskFields.prefab").GetComponent<Task>();
            Assert.AreEqual("InfiniteCorridorTask/Configurations/ZZTest_TaskFields.yaml", task.configPath);
            Assert.IsTrue(task.requireInteraction);
        }

        /// <summary>Verifies that a zone reaching past the cue sequence warns about the measured length.</summary>
        [Test]
        public void CreateFromTemplate_ZoneReachesPastTheCueSequence_WarnsAboutTheLengthMismatch()
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            TrialYaml trial = TestTrial(FirstTrialName, "collision", FirstCueName);
            trial.stimulusLocationCm = 100f;
            template.trials.Add(trial);
            WriteTemplate("ZZTest_Mismatch", template);
            LogAssert.Expect(LogType.Warning, new Regex("Unable to reconcile the measured length of trial T1"));

            string result = Generate("ZZTest_Mismatch");

            Assert.AreEqual(SuccessMessage("ZZTest_Mismatch"), result);
        }

        /// <summary>Verifies that segments outrunning the default track length block generation.</summary>
        [Test]
        public void CreateFromTemplate_SegmentsOutrunTheDefaultTrackLength_ReturnsTrackLengthError()
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.vrEnvironment.cmPerUnityUnit = 0.001f;
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            WriteTemplate("ZZTest_TooLong", template);

            string result = Generate("ZZTest_TooLong");

            StringAssert.StartsWith("error: Unable to generate from template 'ZZTest_TooLong'.", result);
            StringAssert.Contains("must cover the segments_per_corridor value of 1", result);
            Assert.IsNull(LoadAsset<GameObject>($"{CuesFolder}/{FirstCueAssetStem}.prefab"));
        }

        /// <summary>Verifies that scene creation rejects an empty save path.</summary>
        [Test]
        public void CreateSceneFromTemplate_EmptySavePath_ReportsFailure()
        {
            CreateTask.SceneCreationResult result = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: string.Empty,
                taskPrefabPath: string.Empty,
                overwriteExisting: false
            );

            Assert.IsFalse(result.Success);
            Assert.AreEqual(
                "Unable to create the scene. The scene save path must name a project-relative scene file, but "
                    + "it is null or empty.",
                result.Message
            );
        }

        /// <summary>Verifies that scene creation rejects a null save path.</summary>
        [Test]
        public void CreateSceneFromTemplate_NullSavePath_ReportsFailure()
        {
            CreateTask.SceneCreationResult result = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: null,
                taskPrefabPath: string.Empty,
                overwriteExisting: false
            );

            Assert.IsFalse(result.Success);
            Assert.AreEqual(
                "Unable to create the scene. The scene save path must name a project-relative scene file, but "
                    + "it is null or empty.",
                result.Message
            );
        }

        /// <summary>Verifies that scene creation strips the template's default Main Camera.</summary>
        /// <remarks>
        /// The generated scene becomes the active scene, so the assertion reads the live hierarchy and the finally
        /// block restores whichever scene the run had open before teardown deletes the generated one.
        /// </remarks>
        [Test]
        public void CreateSceneFromTemplate_TemplateScene_RemovesTheDefaultMainCamera()
        {
            string initialScenePath = SceneManager.GetActiveScene().path;
            string sceneSavePath = $"{ScenesFolder}/ZZTest_CameraStripped.unity";

            try
            {
                CreateTask.SceneCreationResult result = CreateTask.CreateSceneFromTemplate(
                    sceneSavePath: sceneSavePath,
                    taskPrefabPath: string.Empty,
                    overwriteExisting: false
                );

                Assert.IsTrue(result.Success, result.Message);
                List<string> defaultCameras = UnityEngine
                    .Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(camera =>
                        camera.gameObject.CompareTag("MainCamera")
                        || string.Equals(camera.gameObject.name, "Main Camera", StringComparison.Ordinal)
                    )
                    .Select(camera => camera.gameObject.name)
                    .ToList();
                CollectionAssert.IsEmpty(defaultCameras);
            }
            finally
            {
                if (string.IsNullOrEmpty(initialScenePath))
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.OpenScene(initialScenePath);
                }
            }
        }

        /// <summary>Verifies that scene creation refuses to clobber an existing scene without permission.</summary>
        [Test]
        public void CreateSceneFromTemplate_ExistingSceneWithoutOverwrite_ReportsFailure()
        {
            string sceneSavePath = $"{ScenesFolder}/ZZTest_Existing.unity";
            Assert.IsTrue(AssetDatabase.CopyAsset(TemplateScenePath, sceneSavePath));

            CreateTask.SceneCreationResult result = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: sceneSavePath,
                taskPrefabPath: string.Empty,
                overwriteExisting: false
            );

            Assert.IsFalse(result.Success);
            Assert.AreEqual(
                "Unable to create the scene. The save path must be free, but a scene already exists at "
                    + $"{sceneSavePath}.",
                result.Message
            );
        }

        /// <summary>Invokes the private cue length label formatter for the supplied length.</summary>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <returns>The length label used inside cue asset filenames.</returns>
        private static string FormatCueLengthLabel(float lengthCm)
        {
            return (string)PrivateAccess.InvokeStatic(typeof(CreateTask), "FormatCueLengthLabel", lengthCm);
        }

        /// <summary>Invokes the private canonical segment name builder for the supplied template and trial.</summary>
        /// <param name="template">The task template owning the trial, which supplies the template name.</param>
        /// <param name="trialName">The trial key under trial_structures.</param>
        /// <returns>The canonical segment prefab name, without the .prefab extension.</returns>
        private static string CanonicalSegmentName(TaskTemplate template, string trialName)
        {
            return (string)PrivateAccess.InvokeStatic(typeof(CreateTask), "CanonicalSegmentName", template, trialName);
        }

        /// <summary>Invokes the private cross-template segment cleanup for the supplied template.</summary>
        /// <param name="template">The template whose owned segment prefabs are removed.</param>
        private static void CleanGeneratedSegments(TaskTemplate template)
        {
            // The sweep removes segment prefabs that the previous generation's task prefab still nests, so the
            // importer re-reads that task prefab and logs a missing-nested-prefab error. The production pipeline
            // regenerates both in the same pass, so the log is transient state rather than the behavior under test.
            bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                PrivateAccess.InvokeStatic(typeof(CreateTask), "CleanGeneratedSegments", template);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreSetting;
            }
        }

        /// <summary>Invokes the private track length guard for the supplied template.</summary>
        /// <param name="template">The template whose longest segment is checked.</param>
        /// <returns>The guard's error message, or null when the template passes.</returns>
        private static string ValidateTrackLengthCoversCorridor(TaskTemplate template)
        {
            return (string)
                PrivateAccess.InvokeStatic(typeof(CreateTask), "ValidateTrackLengthCoversCorridor", template);
        }

        /// <summary>Invokes the private hand-authored asset guard for the supplied template.</summary>
        /// <param name="template">The template naming the padding prefab the guard resolves.</param>
        /// <returns>The guard's error message, or null when every required asset is present.</returns>
        private static string ValidateHandAuthoredAssets(TaskTemplate template)
        {
            return (string)PrivateAccess.InvokeStatic(typeof(CreateTask), "ValidateHandAuthoredAssets", template);
        }

        /// <summary>Invokes the private hand-authored path builder for the supplied template.</summary>
        /// <param name="template">The template whose padding prefab the builder resolves.</param>
        /// <returns>The required hand-authored asset paths.</returns>
        private static string[] BuildRequiredHandAuthoredPaths(TaskTemplate template)
        {
            return (string[])PrivateAccess.InvokeStatic(typeof(CreateTask), "BuildRequiredHandAuthoredPaths", template);
        }

        /// <summary>Invokes the private occupancy trigger mode resolver for the supplied literal.</summary>
        /// <param name="triggerType">The trigger type literal to resolve.</param>
        private static TriggerMode ResolveOccupancyTriggerMode(string triggerType)
        {
            return (TriggerMode)
                PrivateAccess.InvokeStatic(typeof(CreateTask), "ResolveOccupancyTriggerMode", triggerType);
        }

        /// <summary>Invokes the private segment build for the supplied template.</summary>
        /// <param name="template">The template whose trials are built into segment prefabs.</param>
        /// <returns>True when every segment prefab was written, false otherwise.</returns>
        private static bool BuildSegmentPrefabs(TaskTemplate template)
        {
            return (bool)PrivateAccess.InvokeStatic(typeof(CreateTask), "BuildSegmentPrefabs", template);
        }

        /// <summary>Builds an in-memory template carrying one single-cue trial per supplied cue length.</summary>
        /// <param name="templateName">The template name the generated segment names are derived from.</param>
        /// <param name="cmPerUnityUnit">The centimeters-per-Unity-unit conversion factor.</param>
        /// <param name="segmentsPerCorridor">The corridor depth in segments.</param>
        /// <param name="paddingPrefabName">The padding prefab name the hand-authored asset guard resolves.</param>
        /// <param name="trialCueLengthsCm">One cue length in centimeters per generated trial.</param>
        /// <returns>The assembled template, which never touches the filesystem.</returns>
        private static TaskTemplate BuildInMemoryTemplate(
            string templateName,
            float cmPerUnityUnit,
            int segmentsPerCorridor,
            string paddingPrefabName,
            params float[] trialCueLengthsCm
        )
        {
            TaskTemplate template = new TaskTemplate
            {
                templateName = templateName,
                cues = new List<Cue>(),
                vrEnvironment = new VREnvironment
                {
                    corridorSpacingCm = CorridorSpacingCm,
                    segmentsPerCorridor = segmentsPerCorridor,
                    paddingPrefabName = paddingPrefabName,
                    cmPerUnityUnit = cmPerUnityUnit,
                    cueOffsetCm = 0f,
                },
                trialStructures = new Dictionary<string, TrialStructure>(),
            };

            for (int index = 0; index < trialCueLengthsCm.Length; index++)
            {
                string cueName = $"ZZ{index}";
                template.cues.Add(
                    new Cue
                    {
                        name = cueName,
                        code = index,
                        lengthCm = trialCueLengthsCm[index],
                        texture = FirstTextureName,
                    }
                );
                template.trialStructures[$"T{index + 1}"] = new TrialStructure
                {
                    cueSequence = new List<string> { cueName },
                    stimulusTriggerZoneStartCm = ZoneStartCm,
                    stimulusTriggerZoneEndCm = ZoneEndCm,
                    stimulusLocationCm = StimulusLocationCm,
                    triggerType = "collision",
                };
            }

            return template;
        }

        /// <summary>
        /// Builds an in-memory template whose single trial declares the supplied trigger type over the first test
        /// cue, so a literal that ConfigLoader rejects still reaches the segment build.
        /// </summary>
        /// <param name="templateName">The template name the generated segment name is derived from.</param>
        /// <param name="triggerType">The trigger type literal the trial declares.</param>
        /// <returns>The assembled template, which never touches the filesystem.</returns>
        private static TaskTemplate InMemoryTriggerTypeTemplate(string templateName, string triggerType)
        {
            return new TaskTemplate
            {
                templateName = templateName,
                cues = new List<Cue>
                {
                    new Cue
                    {
                        name = FirstCueName,
                        code = FirstCueCode,
                        lengthCm = FirstCueLengthCm,
                        texture = FirstTextureName,
                    },
                },
                vrEnvironment = new VREnvironment
                {
                    corridorSpacingCm = CorridorSpacingCm,
                    segmentsPerCorridor = 1,
                    paddingPrefabName = "Padding",
                    cmPerUnityUnit = CmPerUnityUnit,
                    cueOffsetCm = 0f,
                },
                trialStructures = new Dictionary<string, TrialStructure>
                {
                    [FirstTrialName] = new TrialStructure
                    {
                        cueSequence = new List<string> { FirstCueName },
                        stimulusTriggerZoneStartCm = ZoneStartCm,
                        stimulusTriggerZoneEndCm = ZoneEndCm,
                        stimulusLocationCm = StimulusLocationCm,
                        triggerType = triggerType,
                    },
                },
            };
        }

        /// <summary>Creates a template document carrying the shared corridor geometry and no cues or trials.</summary>
        /// <param name="segmentsPerCorridor">The corridor depth in segments.</param>
        /// <param name="cueOffsetCm">The animal start offset in centimeters.</param>
        /// <returns>The template document builder.</returns>
        private static TemplateYaml NewTemplate(int segmentsPerCorridor, float cueOffsetCm)
        {
            TemplateYaml template = new TemplateYaml();
            template.vrEnvironment.corridorSpacingCm = CorridorSpacingCm;
            template.vrEnvironment.segmentsPerCorridor = segmentsPerCorridor;
            template.vrEnvironment.paddingPrefabName = "Padding";
            template.vrEnvironment.cmPerUnityUnit = CmPerUnityUnit;
            template.vrEnvironment.cueOffsetCm = cueOffsetCm;
            return template;
        }

        /// <summary>Creates a cue document block carrying the supplied identity and texture.</summary>
        /// <param name="cueName">The cue name, which together with the length keys the cue prefab and material.</param>
        /// <param name="cueCode">The cue byte code.</param>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <param name="textureName">The cue texture filename.</param>
        /// <returns>The cue document block builder.</returns>
        private static CueYaml TestCue(string cueName, int cueCode, float lengthCm, string textureName)
        {
            CueYaml cue = CueYaml.Named(cueName, cueCode);
            cue.lengthCm = lengthCm;
            cue.texture = textureName;
            return cue;
        }

        /// <summary>Creates a trial document block carrying the shared trigger zone geometry.</summary>
        /// <param name="trialName">The name the trial block declares, which suffixes the segment prefab.</param>
        /// <param name="triggerType">The trigger type literal.</param>
        /// <param name="cueNames">The ordered cue names comprising the trial's segment.</param>
        /// <returns>The trial document block builder.</returns>
        private static TrialYaml TestTrial(string trialName, string triggerType, params string[] cueNames)
        {
            TrialYaml trial = TrialYaml.Named(trialName, cueNames);
            trial.stimulusTriggerZoneStartCm = ZoneStartCm;
            trial.stimulusTriggerZoneEndCm = ZoneEndCm;
            trial.stimulusLocationCm = StimulusLocationCm;
            trial.showStimulusCollisionBoundary = false;
            trial.triggerType = triggerType;
            return trial;
        }

        /// <summary>Creates a one cue, one trial template document at corridor depth one.</summary>
        /// <param name="triggerType">The trigger type literal the trial declares.</param>
        /// <param name="showBoundary">Determines whether the trial shows its stimulus boundary.</param>
        /// <param name="occupancyDurationMs">The occupancy duration in milliseconds, or null to omit it.</param>
        /// <returns>The template document builder.</returns>
        private static TemplateYaml SingleTrialTemplate(
            string triggerType,
            bool showBoundary,
            float? occupancyDurationMs
        )
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            TrialYaml trial = TestTrial(FirstTrialName, triggerType, FirstCueName);
            trial.showStimulusCollisionBoundary = showBoundary;
            trial.occupancyDurationMs = occupancyDurationMs;
            template.trials.Add(trial);
            return template;
        }

        /// <summary>Creates a one trial template whose first cue repeats on both sides of a longer middle cue.
        /// </summary>
        /// <returns>The template document builder.</returns>
        private static TemplateYaml MultiCueTemplate()
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            template.cues.Add(TestCue(SecondCueName, SecondCueCode, SecondCueLengthCm, SecondTextureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName, SecondCueName, FirstCueName));
            return template;
        }

        /// <summary>Creates a two trial template at corridor depth two whose trials differ in length.</summary>
        /// <returns>The template document builder.</returns>
        private static TemplateYaml TwoTrialTemplate()
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 2, cueOffsetCm: 10f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, FirstTextureName));
            template.cues.Add(TestCue(SecondCueName, SecondCueCode, SecondCueLengthCm, SecondTextureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            TrialYaml secondTrial = TestTrial(SecondTrialName, "collision", FirstCueName, SecondCueName);
            secondTrial.showStimulusCollisionBoundary = true;
            template.trials.Add(secondTrial);
            return template;
        }

        /// <summary>Writes a one cue, one collision trial template pointing at the supplied texture.</summary>
        /// <param name="templateName">The template name, which becomes the YAML filename.</param>
        /// <param name="textureName">The cue texture filename.</param>
        private static void WriteSingleTrialTemplate(string templateName, string textureName)
        {
            TemplateYaml template = NewTemplate(segmentsPerCorridor: 1, cueOffsetCm: 0f);
            template.cues.Add(TestCue(FirstCueName, FirstCueCode, FirstCueLengthCm, textureName));
            template.trials.Add(TestTrial(FirstTrialName, "collision", FirstCueName));
            WriteTemplate(templateName, template);
        }

        /// <summary>Writes a template document into the project's Configurations folder and imports it.</summary>
        /// <param name="templateName">The template name, which becomes the YAML filename.</param>
        /// <param name="template">The template document builder whose rendered body is written.</param>
        private static void WriteTemplate(string templateName, TemplateYaml template)
        {
            WriteTextFile($"{ConfigurationsFolder}/{templateName}.yaml", template.Build());
        }

        /// <summary>Writes a text file at a project-relative path and imports it into the asset database.</summary>
        /// <param name="projectRelativePath">The project-relative path the file is written to.</param>
        /// <param name="contents">The file body.</param>
        private static void WriteTextFile(string projectRelativePath, string contents)
        {
            File.WriteAllText(AbsolutePath(projectRelativePath), contents);
            AssetDatabase.ImportAsset(projectRelativePath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Runs the generation pipeline for a template already written into Configurations.</summary>
        /// <param name="templateName">The template name, which drives every auto-resolved output path.</param>
        /// <returns>The pipeline's status message.</returns>
        private static string Generate(string templateName)
        {
            string relativeConfigPath = $"InfiniteCorridorTask/Configurations/{templateName}.yaml";
            return CreateTask.CreateFromTemplate(
                AbsoluteTemplatePath(templateName),
                relativeConfigPath,
                $"{TasksFolder}/{templateName}.prefab"
            );
        }

        /// <summary>Runs the generation pipeline over a template that a previous pass already generated.</summary>
        /// <remarks>
        /// The pipeline's segment sweep removes the prefabs that the previous pass's task prefab still nests, so the
        /// importer re-reads that task prefab and logs a missing-nested-prefab error before the pass rewrites it. The
        /// log is transient state of a regeneration rather than the behavior under test, so it is suppressed for the
        /// duration of the call alone and the caller's assertions still fail on an unexpected error of their own.
        /// </remarks>
        /// <param name="templateName">The template name, which drives every auto-resolved output path.</param>
        /// <returns>The pipeline's status message.</returns>
        private static string Regenerate(string templateName)
        {
            bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                return Generate(templateName);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreSetting;
            }
        }

        /// <summary>Returns the success message the pipeline reports for the supplied template.</summary>
        /// <param name="templateName">The template name driving the task prefab path.</param>
        private static string SuccessMessage(string templateName)
        {
            return $"success: Task prefab saved to {TasksFolder}/{templateName}.prefab";
        }

        /// <summary>Returns the absolute path of a template written into the project's Configurations folder.</summary>
        /// <param name="templateName">The template name, which becomes the YAML filename.</param>
        private static string AbsoluteTemplatePath(string templateName)
        {
            return AbsolutePath($"{ConfigurationsFolder}/{templateName}.yaml");
        }

        /// <summary>Converts a project-relative asset path into an absolute filesystem path.</summary>
        /// <param name="projectRelativePath">The project-relative path, which starts with the Assets folder.</param>
        private static string AbsolutePath(string projectRelativePath)
        {
            return Path.Combine(Application.dataPath, projectRelativePath.Substring("Assets/".Length));
        }

        /// <summary>Loads an asset of the requested type from a project-relative path.</summary>
        /// <typeparam name="TAsset">The asset type to load.</typeparam>
        /// <param name="projectRelativePath">The path the asset database resolves the asset under.</param>
        /// <returns>The loaded asset, or null when no such asset exists.</returns>
        private static TAsset LoadAsset<TAsset>(string projectRelativePath)
            where TAsset : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<TAsset>(projectRelativePath);
        }

        /// <summary>Loads the segment prefab a template generated for one of its trials.</summary>
        /// <param name="trialName">The trial whose segment prefab the template generated.</param>
        private static GameObject LoadSegment(string templateName, string trialName)
        {
            GameObject segment = LoadAsset<GameObject>($"{PrefabsFolder}/{templateName}-{trialName}.prefab");
            Assert.IsNotNull(segment, $"Segment prefab for {templateName}-{trialName} was not generated.");
            return segment;
        }

        /// <summary>Returns the material the first cue instance of a generated segment renders with.</summary>
        /// <param name="trialName">The trial whose segment prefab the template generated.</param>
        /// <returns>The shared material of the first cue instance's wall renderer.</returns>
        private static Material FirstCueMaterial(string templateName, string trialName)
        {
            GameObject segment = LoadSegment(templateName, trialName);
            MeshRenderer renderer = segment
                .transform.GetChild(0)
                .GetComponentInChildren<MeshRenderer>(includeInactive: true);
            Assert.IsNotNull(renderer, "The first cue instance carries no wall renderer.");
            return renderer.sharedMaterial;
        }

        /// <summary>Returns the boundary renderer of the trigger zone a generated single-trial segment carries.
        /// </summary>
        /// <param name="templateName">The owning template name.</param>
        private static MeshRenderer BoundaryRenderer(string templateName)
        {
            GameObject segment = LoadSegment(templateName, FirstTrialName);
            StimulusTriggerZone zone = segment.GetComponentInChildren<StimulusTriggerZone>(includeInactive: true);
            Assert.IsNotNull(zone, "The generated segment carries no stimulus trigger zone.");
            MeshRenderer boundaryRenderer = zone.GetComponent<MeshRenderer>();
            Assert.IsNotNull(boundaryRenderer, "The generated trigger zone carries no boundary renderer.");
            return boundaryRenderer;
        }

        /// <summary>Asserts that every component of a generated vector matches the expected value.</summary>
        /// <param name="expected">The geometry the template's declared centimeters convert to.</param>
        /// <param name="actual">The geometry read off the generated object.</param>
        /// <param name="label">The label quoted in each component's failure message.</param>
        private static void AssertVector3(Vector3 expected, Vector3 actual, string label)
        {
            Assert.AreEqual(expected.x, actual.x, GeometryTolerance, $"{label} x component");
            Assert.AreEqual(expected.y, actual.y, GeometryTolerance, $"{label} y component");
            Assert.AreEqual(expected.z, actual.z, GeometryTolerance, $"{label} z component");
        }

        /// <summary>Asserts that a generated rotation matches the expected orientation.</summary>
        /// <param name="expected">The orientation the generation pipeline is required to apply.</param>
        /// <param name="actual">The rotation read off the generated object.</param>
        /// <param name="label">The label quoted in the failure message.</param>
        private static void AssertRotation(Quaternion expected, Quaternion actual, string label)
        {
            Assert.AreEqual(0f, Quaternion.Angle(expected, actual), 0.01f, $"{label} rotation");
        }

        /// <summary>
        /// Deletes every asset carrying a test filename prefix from the fixture and generation output folders.
        /// </summary>
        /// <remarks>
        /// Sweeping by filename prefix rather than by a recorded list keeps a test that fails midway from stranding
        /// generated assets, and neither prefix can match a hand-authored asset or an asset a shipped template owns.
        /// The folders are swept dependents first, because deleting a cue prefab that a segment prefab still nests
        /// makes the importer re-read the dependent and log a missing-nested-prefab error. The log suppression covers
        /// the residue that ordering alone cannot remove, since a sweep that starts from a half-generated tree reaches
        /// assets whose dependents were never written. It is restored before the caller resumes, so a test body still
        /// fails on an unexpected error of its own.
        /// </remarks>
        private static void RemoveTestAssets()
        {
            string[] folders =
            {
                ScenesFolder,
                TasksFolder,
                PrefabsFolder,
                CuesFolder,
                MaterialsFolder,
                TexturesFolder,
                ConfigurationsFolder,
            };

            bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                bool removedAnything = false;
                foreach (string folder in folders)
                {
                    string absoluteFolder = AbsolutePath(folder);
                    if (!Directory.Exists(absoluteFolder))
                    {
                        continue;
                    }

                    foreach (string absoluteFile in Directory.GetFiles(absoluteFolder))
                    {
                        string fileName = Path.GetFileName(absoluteFile);
                        if (fileName.EndsWith(".meta", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (
                            !fileName.StartsWith(TestAssetPrefix, StringComparison.Ordinal)
                            && !fileName.StartsWith(TestCueAssetPrefix, StringComparison.Ordinal)
                        )
                        {
                            continue;
                        }

                        if (!AssetDatabase.DeleteAsset($"{folder}/{fileName}"))
                        {
                            DeleteFileWithMeta(absoluteFile);
                        }
                        removedAnything = true;
                    }
                }

                if (removedAnything)
                {
                    AssetDatabase.Refresh();
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreSetting;
            }
        }

        /// <summary>Deletes a file the asset database does not track, along with its meta companion.</summary>
        /// <param name="absoluteFilePath">The absolute path of the file to delete.</param>
        private static void DeleteFileWithMeta(string absoluteFilePath)
        {
            if (File.Exists(absoluteFilePath))
            {
                File.Delete(absoluteFilePath);
            }
            string metaPath = $"{absoluteFilePath}.meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }
}
