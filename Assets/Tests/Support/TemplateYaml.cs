/// <summary>
/// Provides the TemplateYaml builder that renders a task template YAML document for a test.
///
/// Minimal returns a document that passes every ConfigLoader check, so a test reaches one validation branch by
/// mutating exactly one field of that baseline. The three top-level sections are individually suppressible and each
/// carry raw override hooks, so a test also reaches the branches a well-typed document cannot express.
/// </summary>
using System.Collections.Generic;
using System.Text;

namespace SL.Tests
{
    /// <summary>
    /// Builds a complete task template YAML document from mutable cue, environment, and trial blocks.
    /// </summary>
    public sealed class TemplateYaml
    {
        /// <summary>The cue definitions rendered under the cues key.</summary>
        public readonly List<CueYaml> cues = new List<CueYaml>();

        /// <summary>The corridor geometry rendered under the vr_environment key.</summary>
        public readonly VrEnvironmentYaml vrEnvironment = new VrEnvironmentYaml();

        /// <summary>The trial definitions rendered under the trial_structures key.</summary>
        public readonly List<TrialYaml> trials = new List<TrialYaml>();

        /// <summary>Determines whether the cues key is written at all.</summary>
        public bool includeCuesSection = true;

        /// <summary>Determines whether the vr_environment key is written at all.</summary>
        public bool includeVrEnvironmentSection = true;

        /// <summary>Determines whether the trial_structures key is written at all.</summary>
        public bool includeTrialStructuresSection = true;

        /// <summary>The text appended verbatim after every rendered section.</summary>
        public string trailingRawText = null;

        /// <summary>
        /// Creates the baseline document: two 30 cm cues sharing one texture, default corridor geometry, and two
        /// collision trials whose cue sequences differ.
        /// </summary>
        /// <returns>A builder whose rendered document passes ConfigLoader validation unchanged.</returns>
        public static TemplateYaml Minimal()
        {
            TemplateYaml template = new TemplateYaml();
            template.cues.Add(CueYaml.Named("A", 1));
            template.cues.Add(CueYaml.Named("B", 2));
            template.trials.Add(TrialYaml.Named("AB", "A", "B"));
            template.trials.Add(TrialYaml.Named("BA", "B", "A"));
            return template;
        }

        /// <summary>Returns the cue block carrying the given name.</summary>
        /// <param name="cueName">The cue name to locate.</param>
        /// <returns>The matching cue block, or null when no cue carries the name.</returns>
        public CueYaml Cue(string cueName)
        {
            return cues.Find(cue => cue.name == cueName);
        }

        /// <summary>Returns the trial block carrying the given name.</summary>
        /// <param name="trialName">The trial name to locate.</param>
        /// <returns>The matching trial block, or null when no trial carries the name.</returns>
        public TrialYaml Trial(string trialName)
        {
            return trials.Find(trial => trial.name == trialName);
        }

        /// <summary>Returns every distinct texture file name the cue blocks reference.</summary>
        /// <returns>The referenced texture file names, with duplicates and omitted names removed.</returns>
        public IEnumerable<string> ReferencedTextureNames()
        {
            HashSet<string> names = new HashSet<string>();
            foreach (CueYaml cue in cues)
            {
                if (!string.IsNullOrEmpty(cue.texture))
                {
                    names.Add(cue.texture);
                }
            }
            return names;
        }

        /// <summary>Renders the complete YAML document.</summary>
        /// <returns>The rendered document text.</returns>
        public string Build()
        {
            StringBuilder builder = new StringBuilder();

            if (includeCuesSection)
            {
                builder.AppendLine("cues:");
                foreach (CueYaml cue in cues)
                {
                    cue.AppendTo(builder);
                }
                builder.AppendLine();
            }

            if (includeVrEnvironmentSection)
            {
                builder.AppendLine("vr_environment:");
                vrEnvironment.AppendTo(builder);
                builder.AppendLine();
            }

            if (includeTrialStructuresSection)
            {
                builder.AppendLine("trial_structures:");
                foreach (TrialYaml trial in trials)
                {
                    trial.AppendTo(builder);
                    builder.AppendLine();
                }
            }

            if (trailingRawText != null)
            {
                builder.AppendLine(trailingRawText);
            }

            return builder.ToString();
        }
    }
}
