/// <summary>
/// Verifies the behavior of the ConfigLoader class.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SL.Config;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the ConfigLoader class.</summary>
    [TestFixture]
    public class ConfigLoaderTests
    {
        /// <summary>The staged Configurations and Textures directory pair backing each test.</summary>
        private TemplateWorkspace _workspace;

        /// <summary>Stages a fresh workspace before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _workspace = TemplateWorkspace.Create();
        }

        /// <summary>Deletes the staged workspace after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            _workspace.Dispose();
        }

        /// <summary>Verifies that LoadTemplate derives the template name from the file name.</summary>
        [Test]
        public void LoadTemplate_ValidDocument_DerivesTemplateNameFromFileName()
        {
            string path = _workspace.WriteTemplate("Valid_Template", TemplateYaml.Minimal());

            TaskTemplate template = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("Valid_Template", template.templateName);
            Assert.AreEqual(2, template.cues.Count);
            Assert.AreEqual(2, template.trialStructures.Count);
        }

        /// <summary>Verifies that LoadTemplate throws its own not-found message when the file is absent.</summary>
        [Test]
        public void LoadTemplate_MissingFile_ThrowsFileNotFound()
        {
            string path = _workspace.TemplatePath("Absent");

            FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
                ConfigLoader.LoadTemplate(path)
            );
            StringAssert.Contains("Template file not found", exception.Message);
            Assert.AreEqual(path, exception.FileName);
        }

        /// <summary>Verifies that LoadTemplate rejects a file name carrying a hyphen.</summary>
        [Test]
        public void LoadTemplate_HyphenatedFileName_ThrowsInvalidData()
        {
            string path = _workspace.WriteTemplate("Bad-Name", TemplateYaml.Minimal());

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Bad-Name", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a template declaring no cues.</summary>
        [Test]
        public void LoadTemplate_NoCuesSection_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeCuesSection = false;
            string path = _workspace.WriteTemplate("NoCues", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("No cues defined", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue code above the byte range.</summary>
        [Test]
        public void LoadTemplate_CueCodeAboveByteRange_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").code = 256;
            string path = _workspace.WriteTemplate("HighCode", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Must be 0-255", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts the inclusive cue code boundaries.</summary>
        [Test]
        public void LoadTemplate_CueCodesAtByteBoundaries_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").code = 0;
            template.Cue("B").code = 255;
            string path = _workspace.WriteTemplate("BoundaryCodes", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0, loaded.cues[0].code);
            Assert.AreEqual(255, loaded.cues[1].code);
        }

        /// <summary>Verifies that LoadTemplate rejects an occupancy trial that omits its occupancy duration.</summary>
        [Test]
        public void LoadTemplate_OccupancyTriggerWithoutDuration_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_arm");
            string path = _workspace.WriteTemplate("NoDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("occupancy_duration_ms is required", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects two trials whose cue sequences are identical.</summary>
        [Test]
        public void LoadTemplate_DuplicateCueSequences_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("BA").cueSequence = new List<string> { "A", "B" };
            string path = _workspace.WriteTemplate("DuplicateSequences", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("identical cue sequence", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a non-finite corridor geometry scalar.</summary>
        [Test]
        public void LoadTemplate_NonFiniteCmPerUnityUnit_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cmPerUnityUnit = float.NaN;
            string path = _workspace.WriteTemplate("NanScale", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("cm_per_unity_unit", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects transition probabilities that do not sum to one.</summary>
        [Test]
        public void LoadTemplate_TransitionsOutsideSumTolerance_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "AB", 0.5f } });
            string path = _workspace.WriteTemplate("BadSum", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("sum to", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts a file name mixing letters, digits, and underscores.</summary>
        [Test]
        public void LoadTemplate_AlphanumericFileName_Loads()
        {
            string path = _workspace.WriteTemplate("Task_01_Alpha", TemplateYaml.Minimal());

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("Task_01_Alpha", loaded.templateName);
        }

        /// <summary>Verifies that LoadTemplate rejects a file name carrying a space.</summary>
        [Test]
        public void LoadTemplate_FileNameWithSpace_ThrowsInvalidData()
        {
            string path = _workspace.WriteTemplate("Bad Name", TemplateYaml.Minimal());

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Template filename 'Bad Name' is invalid", exception.Message);
            StringAssert.Contains("letters, digits, and underscores", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a file name carrying an interior period.</summary>
        [Test]
        public void LoadTemplate_FileNameWithPeriod_ThrowsInvalidData()
        {
            string path = _workspace.WriteTemplate("Bad.Name", TemplateYaml.Minimal());

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Template filename 'Bad.Name' is invalid", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a file whose name is empty before the extension.</summary>
        [Test]
        public void LoadTemplate_EmptyFileName_ThrowsInvalidData()
        {
            string path = _workspace.WriteTemplate(string.Empty, TemplateYaml.Minimal());

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Template filename '' is invalid", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a document body that deserializes to null.</summary>
        [Test]
        public void LoadTemplate_NullDocumentBody_ThrowsFormatException()
        {
            string path = _workspace.WriteTemplate("NullBody", "null\n");

            FormatException exception = Assert.Throws<FormatException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Failed to parse template file", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate checks the file name before it validates the document body.</summary>
        [Test]
        public void LoadTemplate_InvalidFileNameAndInvalidBody_ReportsTheFileNameFailure()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeCuesSection = false;
            string path = _workspace.WriteTemplate("Bad Name", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Template filename 'Bad Name' is invalid", exception.Message);
            StringAssert.DoesNotContain("No cues defined", exception.Message);
        }

        /// <summary>Verifies that the file name overwrites a template_name declared inside the document.</summary>
        [Test]
        public void LoadTemplate_TemplateNameDeclaredInBody_OverwrittenByFileName()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.trailingRawText = "template_name: \"BodyDeclaredName\"";
            string path = _workspace.WriteTemplate("FileDeclaredName", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("FileDeclaredName", loaded.templateName);
        }

        /// <summary>Verifies that LoadTemplate rejects a template whose cues list is present but empty.</summary>
        [Test]
        public void LoadTemplate_EmptyCuesList_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeCuesSection = false;
            template.trailingRawText = "cues: []";
            string path = _workspace.WriteTemplate("EmptyCues", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("No cues defined", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a template that omits the vr_environment section.</summary>
        [Test]
        public void LoadTemplate_NoVrEnvironmentSection_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeVrEnvironmentSection = false;
            string path = _workspace.WriteTemplate("NoEnvironment", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("No VR environment configuration defined", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a template that omits the trial_structures section.</summary>
        [Test]
        public void LoadTemplate_NoTrialStructuresSection_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeTrialStructuresSection = false;
            string path = _workspace.WriteTemplate("NoTrials", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("No trial structures defined", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trial_structures mapping that is present but empty.</summary>
        [Test]
        public void LoadTemplate_EmptyTrialStructures_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.includeTrialStructuresSection = false;
            template.trailingRawText = "trial_structures: {}";
            string path = _workspace.WriteTemplate("EmptyTrials", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("No trial structures defined", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue entry that omits its name key.</summary>
        [Test]
        public void LoadTemplate_CueMissingName_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").name = null;
            string path = _workspace.WriteTemplate("NoCueName", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("A cue entry is missing the required 'name' field", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue entry whose name is the empty string.</summary>
        [Test]
        public void LoadTemplate_CueEmptyName_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").name = string.Empty;
            string path = _workspace.WriteTemplate("EmptyCueName", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("A cue entry is missing the required 'name' field", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue code below the byte range.</summary>
        [Test]
        public void LoadTemplate_CueCodeBelowByteRange_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").code = -1;
            string path = _workspace.WriteTemplate("LowCode", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' has invalid code -1. Must be 0-255.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects two cues sharing one byte code.</summary>
        [Test]
        public void LoadTemplate_DuplicateCueCode_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("B").code = 1;
            string path = _workspace.WriteTemplate("DuplicateCode", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Duplicate cue code 1 found.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects two cues sharing one name.</summary>
        [Test]
        public void LoadTemplate_DuplicateCueName_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.cues[1].name = "A";
            string path = _workspace.WriteTemplate("DuplicateName", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Duplicate cue name 'A' found.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue length of exactly zero.</summary>
        [Test]
        public void LoadTemplate_ZeroCueLength_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").lengthCm = 0f;
            string path = _workspace.WriteTemplate("ZeroLength", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' has invalid length 0", exception.Message);
            StringAssert.Contains("Must be positive.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a negative cue length.</summary>
        [Test]
        public void LoadTemplate_NegativeCueLength_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").lengthCm = -5f;
            string path = _workspace.WriteTemplate("NegativeLength", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' has invalid length -5", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts a fractional cue length just above zero.</summary>
        [Test]
        public void LoadTemplate_SmallPositiveCueLength_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").lengthCm = 0.25f;
            string path = _workspace.WriteTemplate("TinyLength", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0.25f, loaded.cues[0].lengthCm);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue entry that omits its texture key.</summary>
        [Test]
        public void LoadTemplate_CueMissingTexture_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").texture = null;
            string path = _workspace.WriteTemplate("NoTexture", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' is missing required 'texture' field.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue whose texture key is the empty string.</summary>
        [Test]
        public void LoadTemplate_CueEmptyTexture_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").texture = string.Empty;
            string path = _workspace.WriteTemplate("EmptyTexture", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' is missing required 'texture' field.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue whose texture file is absent from disk.</summary>
        [Test]
        public void LoadTemplate_CueTextureFileAbsent_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").texture = "Absent Cue.png";
            string path = _workspace.WriteTemplate("AbsentTexture", template.Build());

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Cue 'A' references texture 'Absent Cue.png'", exception.Message);
            StringAssert.Contains("no file found at", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trial name carrying a hyphen.</summary>
        [Test]
        public void LoadTemplate_HyphenatedTrialName_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").name = "A-B";
            string path = _workspace.WriteTemplate("HyphenTrial", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial name 'A-B' is invalid", exception.Message);
            StringAssert.Contains("letters, digits, and underscores", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trial name carrying a space.</summary>
        [Test]
        public void LoadTemplate_TrialNameWithSpace_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").name = "A B";
            string path = _workspace.WriteTemplate("SpacedTrial", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial name 'A B' is invalid", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts a trial name mixing letters, digits, and underscores.</summary>
        [Test]
        public void LoadTemplate_AlphanumericTrialName_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").name = "Trial_01";
            string path = _workspace.WriteTemplate("NamedTrial", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(2, loaded.trialStructures.Count);
            Assert.IsTrue(loaded.trialStructures.ContainsKey("Trial_01"));
        }

        /// <summary>Verifies that LoadTemplate rejects a trial that omits its cue_sequence key.</summary>
        [Test]
        public void LoadTemplate_TrialMissingCueSequence_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").cueSequence = null;
            string path = _workspace.WriteTemplate("NoSequence", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has no cue sequence.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trial whose cue sequence is present but empty.</summary>
        [Test]
        public void LoadTemplate_TrialEmptyCueSequence_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").cueSequence = new List<string>();
            string path = _workspace.WriteTemplate("EmptySequence", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has no cue sequence.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a cue sequence naming a cue the template omits.</summary>
        [Test]
        public void LoadTemplate_TrialReferencesUndefinedCue_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").cueSequence = new List<string> { "A", "Z" };
            string path = _workspace.WriteTemplate("UnknownCue", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' references unknown cue 'Z'.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trial that omits its trigger_type key.</summary>
        [Test]
        public void LoadTemplate_TrialMissingTriggerType_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").triggerType = null;
            string path = _workspace.WriteTemplate("NoTrigger", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' is missing required 'trigger_type' field.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a trigger_type that is the empty string.</summary>
        [Test]
        public void LoadTemplate_TrialEmptyTriggerType_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").triggerType = string.Empty;
            string path = _workspace.WriteTemplate("BlankTrigger", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' is missing required 'trigger_type' field.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts the interaction trigger type.</summary>
        [Test]
        public void LoadTemplate_InteractionTriggerType_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("interaction");
            string path = _workspace.WriteTemplate("InteractionTrigger", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("interaction", loaded.trialStructures["AB"].triggerType);
            Assert.IsFalse(loaded.trialStructures["AB"].occupancyDurationMs.HasValue);
        }

        /// <summary>Verifies that LoadTemplate accepts the collision trigger type.</summary>
        [Test]
        public void LoadTemplate_CollisionTriggerType_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("collision");
            string path = _workspace.WriteTemplate("CollisionTrigger", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("collision", loaded.trialStructures["AB"].triggerType);
        }

        /// <summary>Verifies that LoadTemplate accepts the occupancy_disarm trigger type.</summary>
        [Test]
        public void LoadTemplate_OccupancyDisarmTriggerType_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_disarm", 500f);
            string path = _workspace.WriteTemplate("DisarmTrigger", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("occupancy_disarm", loaded.trialStructures["AB"].triggerType);
            Assert.AreEqual(500f, loaded.trialStructures["AB"].occupancyDurationMs.Value);
        }

        /// <summary>Verifies that LoadTemplate accepts the occupancy_arm trigger type.</summary>
        [Test]
        public void LoadTemplate_OccupancyArmTriggerType_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_arm", 750f);
            string path = _workspace.WriteTemplate("ArmTrigger", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("occupancy_arm", loaded.trialStructures["AB"].triggerType);
            Assert.AreEqual(750f, loaded.trialStructures["AB"].occupancyDurationMs.Value);
        }

        /// <summary>Verifies that LoadTemplate accepts the occupancy_trigger trigger type.</summary>
        [Test]
        public void LoadTemplate_OccupancyTriggerTriggerType_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_trigger", 125f);
            string path = _workspace.WriteTemplate("TriggerTrigger", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("occupancy_trigger", loaded.trialStructures["AB"].triggerType);
            Assert.AreEqual(125f, loaded.trialStructures["AB"].occupancyDurationMs.Value);
        }

        /// <summary>Verifies that LoadTemplate rejects a trigger_type outside the accepted literal set.</summary>
        [Test]
        public void LoadTemplate_UnknownTriggerType_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("teleport");
            string path = _workspace.WriteTemplate("UnknownTrigger", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has invalid trigger_type 'teleport'.", exception.Message);
            StringAssert.Contains("'interaction', 'collision', 'occupancy_disarm'", exception.Message);
        }

        /// <summary>Verifies that the trigger_type comparison is ordinal, so casing alone rejects a literal.</summary>
        [Test]
        public void LoadTemplate_TriggerTypeDifferingOnlyInCase_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("Collision");
            string path = _workspace.WriteTemplate("CasedTrigger", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has invalid trigger_type 'Collision'.", exception.Message);
        }

        /// <summary>Verifies that the occupancy_disarm mode requires an occupancy duration.</summary>
        [Test]
        public void LoadTemplate_OccupancyDisarmWithoutDuration_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_disarm");
            string path = _workspace.WriteTemplate("DisarmNoDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("occupancy_duration_ms is required, but it is unset.", exception.Message);
            StringAssert.Contains("trigger_type 'occupancy_disarm'", exception.Message);
        }

        /// <summary>Verifies that the occupancy_trigger mode requires an occupancy duration.</summary>
        [Test]
        public void LoadTemplate_OccupancyTriggerModeWithoutDuration_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_trigger");
            string path = _workspace.WriteTemplate("TriggerNoDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("occupancy_duration_ms is required, but it is unset.", exception.Message);
            StringAssert.Contains("trigger_type 'occupancy_trigger'", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects an occupancy duration of exactly zero.</summary>
        [Test]
        public void LoadTemplate_ZeroOccupancyDuration_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_arm", 0f);
            string path = _workspace.WriteTemplate("ZeroDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has invalid occupancy_duration_ms 0", exception.Message);
            StringAssert.Contains("Must be positive.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a negative occupancy duration.</summary>
        [Test]
        public void LoadTemplate_NegativeOccupancyDuration_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_trigger", -50f);
            string path = _workspace.WriteTemplate("NegativeDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has invalid occupancy_duration_ms -50", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts a fractional occupancy duration just above zero.</summary>
        [Test]
        public void LoadTemplate_SmallPositiveOccupancyDuration_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("occupancy_arm", 0.5f);
            string path = _workspace.WriteTemplate("TinyDuration", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0.5f, loaded.trialStructures["AB"].occupancyDurationMs.Value);
        }

        /// <summary>Verifies that a non-occupancy trial may still carry a positive occupancy duration.</summary>
        [Test]
        public void LoadTemplate_OccupancyDurationOnCollisionTrial_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("collision", 250f);
            string path = _workspace.WriteTemplate("CollisionDuration", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("collision", loaded.trialStructures["AB"].triggerType);
            Assert.AreEqual(250f, loaded.trialStructures["AB"].occupancyDurationMs.Value);
        }

        /// <summary>Verifies that a non-occupancy trial still rejects a non-positive occupancy duration.</summary>
        [Test]
        public void LoadTemplate_ZeroOccupancyDurationOnCollisionTrial_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTrigger("collision", 0f);
            string path = _workspace.WriteTemplate("CollisionZeroDuration", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has invalid occupancy_duration_ms 0", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a transition naming a trial the template omits.</summary>
        [Test]
        public void LoadTemplate_TransitionToUndefinedTrial_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "ZZ", 1f } });
            string path = _workspace.WriteTemplate("UnknownTransition", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' has a transition to unknown trial 'ZZ'.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a negative transition probability.</summary>
        [Test]
        public void LoadTemplate_NegativeTransitionProbability_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "BA", -1f } });
            string path = _workspace.WriteTemplate("NegativeProbability", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("transition to 'BA' with invalid probability -1", exception.Message);
            StringAssert.Contains("Must be between 0.0 and 1.0.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a transition probability above one.</summary>
        [Test]
        public void LoadTemplate_TransitionProbabilityAboveOne_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "BA", 1.5f } });
            string path = _workspace.WriteTemplate("HighProbability", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("transition to 'BA' with invalid probability 1.5", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a NaN transition probability.</summary>
        [Test]
        public void LoadTemplate_NaNTransitionProbability_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "BA", float.NaN } });
            string path = _workspace.WriteTemplate("NanProbability", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("transition to 'BA' with invalid probability", exception.Message);
            StringAssert.Contains("Must be between 0.0 and 1.0.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects an infinite transition probability.</summary>
        [Test]
        public void LoadTemplate_InfiniteTransitionProbability_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            Dictionary<string, float> distribution = new Dictionary<string, float> { { "BA", float.PositiveInfinity } };
            template.Trial("AB").WithTransitions(distribution);
            string path = _workspace.WriteTemplate("InfiniteProbability", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("transition to 'BA' with invalid probability", exception.Message);
            StringAssert.Contains("Must be between 0.0 and 1.0.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts transition probabilities at both inclusive bounds.</summary>
        [Test]
        public void LoadTemplate_TransitionProbabilitiesAtInclusiveBounds_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            Dictionary<string, float> distribution = new Dictionary<string, float> { { "AB", 1f }, { "BA", 0f } };
            template.Trial("AB").WithTransitions(distribution);
            string path = _workspace.WriteTemplate("BoundProbabilities", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.IsTrue(loaded.trialStructures["AB"].HasTransitions);
            Assert.AreEqual(1f, loaded.trialStructures["AB"].transitions["AB"]);
            Assert.AreEqual(0f, loaded.trialStructures["AB"].transitions["BA"]);
        }

        /// <summary>Verifies that a transition sum of 0.9995 stays inside the 0.001 tolerance.</summary>
        [Test]
        public void LoadTemplate_TransitionSumJustBelowOne_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "BA", 0.9995f } });
            string path = _workspace.WriteTemplate("SumJustBelow", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0.9995f, loaded.trialStructures["AB"].transitions["BA"]);
        }

        /// <summary>Verifies that a transition sum of 1.0005 stays inside the 0.001 tolerance.</summary>
        [Test]
        public void LoadTemplate_TransitionSumJustAboveOne_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            Dictionary<string, float> distribution = new Dictionary<string, float>
            {
                { "AB", 0.5005f },
                { "BA", 0.5f },
            };
            template.Trial("AB").WithTransitions(distribution);
            string path = _workspace.WriteTemplate("SumJustAbove", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0.5005f, loaded.trialStructures["AB"].transitions["AB"]);
            Assert.AreEqual(0.5f, loaded.trialStructures["AB"].transitions["BA"]);
        }

        /// <summary>Verifies that a transition sum of 0.998 falls outside the 0.001 tolerance.</summary>
        [Test]
        public void LoadTemplate_TransitionSumBelowTolerance_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float> { { "BA", 0.998f } });
            string path = _workspace.WriteTemplate("SumBelow", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' transition probabilities sum to", exception.Message);
            StringAssert.Contains("must be 1.0.", exception.Message);
        }

        /// <summary>Verifies that a transition sum of 1.002 falls outside the 0.001 tolerance.</summary>
        [Test]
        public void LoadTemplate_TransitionSumAboveTolerance_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            Dictionary<string, float> distribution = new Dictionary<string, float> { { "AB", 0.502f }, { "BA", 0.5f } };
            template.Trial("AB").WithTransitions(distribution);
            string path = _workspace.WriteTemplate("SumAbove", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Trial 'AB' transition probabilities sum to", exception.Message);
            StringAssert.Contains("must be 1.0.", exception.Message);
        }

        /// <summary>Verifies that an empty transitions mapping skips the probability validation entirely.</summary>
        [Test]
        public void LoadTemplate_EmptyTransitionsMap_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").WithTransitions(new Dictionary<string, float>());
            string path = _workspace.WriteTemplate("EmptyTransitions", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0, loaded.trialStructures["AB"].transitions.Count);
            Assert.IsFalse(loaded.trialStructures["AB"].HasTransitions);
        }

        /// <summary>Verifies that an omitted transitions mapping skips the probability validation entirely.</summary>
        [Test]
        public void LoadTemplate_OmittedTransitionsMap_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            string path = _workspace.WriteTemplate("NoTransitions", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.IsNull(loaded.trialStructures["AB"].transitions);
            Assert.IsFalse(loaded.trialStructures["AB"].HasTransitions);
        }

        /// <summary>Verifies that LoadTemplate rejects a corridor depth of zero segments.</summary>
        [Test]
        public void LoadTemplate_ZeroSegmentsPerCorridor_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.segmentsPerCorridor = 0;
            string path = _workspace.WriteTemplate("ZeroSegments", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid segments_per_corridor 0. Must be at least 1.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts the inclusive corridor depth boundary of one segment.</summary>
        [Test]
        public void LoadTemplate_SingleSegmentPerCorridor_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.segmentsPerCorridor = 1;
            string path = _workspace.WriteTemplate("OneSegment", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(1, loaded.vrEnvironment.segmentsPerCorridor);
        }

        /// <summary>Verifies that LoadTemplate rejects a cm_per_unity_unit of exactly zero.</summary>
        [Test]
        public void LoadTemplate_ZeroCmPerUnityUnit_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cmPerUnityUnit = 0f;
            string path = _workspace.WriteTemplate("ZeroScale", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid cm_per_unity_unit 0", exception.Message);
            StringAssert.Contains("Must be positive and finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a negative cm_per_unity_unit.</summary>
        [Test]
        public void LoadTemplate_NegativeCmPerUnityUnit_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cmPerUnityUnit = -10f;
            string path = _workspace.WriteTemplate("NegativeScale", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid cm_per_unity_unit -10", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects an infinite cm_per_unity_unit.</summary>
        [Test]
        public void LoadTemplate_InfiniteCmPerUnityUnit_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cmPerUnityUnit = float.PositiveInfinity;
            string path = _workspace.WriteTemplate("InfiniteScale", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid cm_per_unity_unit", exception.Message);
            StringAssert.Contains("Must be positive and finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate accepts a fractional cm_per_unity_unit just above zero.</summary>
        [Test]
        public void LoadTemplate_SmallPositiveCmPerUnityUnit_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cmPerUnityUnit = 0.5f;
            string path = _workspace.WriteTemplate("TinyScale", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(0.5f, loaded.vrEnvironment.cmPerUnityUnit);
        }

        /// <summary>Verifies that LoadTemplate rejects a corridor_spacing_cm of exactly zero.</summary>
        [Test]
        public void LoadTemplate_ZeroCorridorSpacing_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.corridorSpacingCm = 0f;
            string path = _workspace.WriteTemplate("ZeroSpacing", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid corridor_spacing_cm 0", exception.Message);
            StringAssert.Contains("Must be positive and finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a negative corridor_spacing_cm.</summary>
        [Test]
        public void LoadTemplate_NegativeCorridorSpacing_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.corridorSpacingCm = -20f;
            string path = _workspace.WriteTemplate("NegativeSpacing", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid corridor_spacing_cm -20", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a NaN corridor_spacing_cm.</summary>
        [Test]
        public void LoadTemplate_NaNCorridorSpacing_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.corridorSpacingCm = float.NaN;
            string path = _workspace.WriteTemplate("NanSpacing", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid corridor_spacing_cm", exception.Message);
            StringAssert.Contains("Must be positive and finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects an infinite corridor_spacing_cm.</summary>
        [Test]
        public void LoadTemplate_InfiniteCorridorSpacing_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.corridorSpacingCm = float.PositiveInfinity;
            string path = _workspace.WriteTemplate("InfiniteSpacing", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid corridor_spacing_cm", exception.Message);
            StringAssert.Contains("Must be positive and finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects a NaN cue_offset_cm.</summary>
        [Test]
        public void LoadTemplate_NaNCueOffset_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cueOffsetCm = float.NaN;
            string path = _workspace.WriteTemplate("NanOffset", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid cue_offset_cm", exception.Message);
            StringAssert.Contains("Must be finite.", exception.Message);
        }

        /// <summary>Verifies that LoadTemplate rejects an infinite cue_offset_cm.</summary>
        [Test]
        public void LoadTemplate_InfiniteCueOffset_ThrowsInvalidData()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cueOffsetCm = float.NegativeInfinity;
            string path = _workspace.WriteTemplate("InfiniteOffset", template);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadTemplate(path));
            StringAssert.Contains("Invalid cue_offset_cm", exception.Message);
            StringAssert.Contains("Must be finite.", exception.Message);
        }

        /// <summary>Verifies that a negative cue_offset_cm is legal because only finiteness is required.</summary>
        [Test]
        public void LoadTemplate_NegativeCueOffset_Loads()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.cueOffsetCm = -15f;
            string path = _workspace.WriteTemplate("NegativeOffset", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(-15f, loaded.vrEnvironment.cueOffsetCm);
        }

        /// <summary>Verifies that the underscored cue keys map onto their camelCase Cue members.</summary>
        [Test]
        public void LoadTemplate_UnderscoredCueKeys_MapOntoCamelCaseMembers()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            CueYaml cue = template.Cue("A");
            cue.code = 7;
            cue.lengthCm = 45.5f;
            cue.texture = "Custom Cue.png";
            string path = _workspace.WriteTemplate("CueKeys", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("A", loaded.cues[0].name);
            Assert.AreEqual(7, loaded.cues[0].code);
            Assert.AreEqual(45.5f, loaded.cues[0].lengthCm);
            Assert.AreEqual("Custom Cue.png", loaded.cues[0].texture);
        }

        /// <summary>Verifies that the underscored vr_environment keys map onto their camelCase members.</summary>
        [Test]
        public void LoadTemplate_UnderscoredVrEnvironmentKeys_MapOntoCamelCaseMembers()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.vrEnvironment.corridorSpacingCm = 42.5f;
            template.vrEnvironment.segmentsPerCorridor = 7;
            template.vrEnvironment.paddingPrefabName = "CustomPadding";
            template.vrEnvironment.cmPerUnityUnit = 12.5f;
            template.vrEnvironment.cueOffsetCm = -3.5f;
            string path = _workspace.WriteTemplate("EnvironmentKeys", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual(42.5f, loaded.vrEnvironment.corridorSpacingCm);
            Assert.AreEqual(7, loaded.vrEnvironment.segmentsPerCorridor);
            Assert.AreEqual("CustomPadding", loaded.vrEnvironment.paddingPrefabName);
            Assert.AreEqual(12.5f, loaded.vrEnvironment.cmPerUnityUnit);
            Assert.AreEqual(-3.5f, loaded.vrEnvironment.cueOffsetCm);
        }

        /// <summary>Verifies that the underscored trial keys map onto their camelCase TrialStructure members.</summary>
        [Test]
        public void LoadTemplate_UnderscoredTrialKeys_MapOntoCamelCaseMembers()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            TrialYaml trial = template.Trial("AB");
            trial.stimulusTriggerZoneStartCm = 3.5f;
            trial.stimulusTriggerZoneEndCm = 27.25f;
            trial.stimulusLocationCm = 19.75f;
            trial.showStimulusCollisionBoundary = true;
            trial.WithTrigger("collision", 750f);
            string path = _workspace.WriteTemplate("TrialKeys", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            TrialStructure loadedTrial = loaded.trialStructures["AB"];
            CollectionAssert.AreEqual(new List<string> { "A", "B" }, loadedTrial.cueSequence);
            Assert.AreEqual(3.5f, loadedTrial.stimulusTriggerZoneStartCm);
            Assert.AreEqual(27.25f, loadedTrial.stimulusTriggerZoneEndCm);
            Assert.AreEqual(19.75f, loadedTrial.stimulusLocationCm);
            Assert.IsTrue(loadedTrial.showStimulusCollisionBoundary);
            Assert.AreEqual(750f, loadedTrial.occupancyDurationMs.Value);
        }

        /// <summary>Verifies that an omitted show_stimulus_collision_boundary key keeps the boundary hidden.</summary>
        [Test]
        public void LoadTemplate_OmittedStimulusBoundaryKey_DefaultsToFalse()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").showStimulusCollisionBoundary = null;
            string path = _workspace.WriteTemplate("OmittedBoundary", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.IsFalse(loaded.trialStructures["AB"].showStimulusCollisionBoundary);
        }

        /// <summary>Verifies that an unmatched top-level YAML key is ignored rather than fatal.</summary>
        [Test]
        public void LoadTemplate_UnmatchedTopLevelKey_IsIgnored()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.trailingRawText = "unexpected_top_level_key: 7";
            string path = _workspace.WriteTemplate("UnmatchedTopKey", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("UnmatchedTopKey", loaded.templateName);
            Assert.AreEqual(2, loaded.cues.Count);
        }

        /// <summary>Verifies that an unmatched key inside a cue entry is ignored rather than fatal.</summary>
        [Test]
        public void LoadTemplate_UnmatchedCueKey_IsIgnored()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Cue("A").rawOverrides["unexpected_cue_key"] = "\"ignored\"";
            string path = _workspace.WriteTemplate("UnmatchedCueKey", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("A", loaded.cues[0].name);
            Assert.AreEqual(1, loaded.cues[0].code);
        }

        /// <summary>Verifies that an unmatched key inside a trial entry is ignored rather than fatal.</summary>
        [Test]
        public void LoadTemplate_UnmatchedTrialKey_IsIgnored()
        {
            TemplateYaml template = TemplateYaml.Minimal();
            template.Trial("AB").rawOverrides["unexpected_trial_key"] = "13";
            string path = _workspace.WriteTemplate("UnmatchedTrialKey", template);

            TaskTemplate loaded = ConfigLoader.LoadTemplate(path);

            Assert.AreEqual("collision", loaded.trialStructures["AB"].triggerType);
            Assert.AreEqual(2, loaded.trialStructures.Count);
        }
    }
}
