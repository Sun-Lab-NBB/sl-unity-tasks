/// <summary>Provides the CueYaml builder describing one entry of a task template's cues list.</summary>
using System.Collections.Generic;
using System.Text;

namespace SL.Tests
{
    /// <summary>Builds the YAML block for a single cue definition.</summary>
    /// <remarks>
    /// A typed field set to null omits its YAML key entirely, which is how a test reaches ConfigLoader's missing-field
    /// branches.
    /// </remarks>
    public sealed class CueYaml
    {
        /// <summary>The cue name, or null to omit the name key.</summary>
        public string name = "A";

        /// <summary>The cue byte code, or null to omit the code key.</summary>
        public int? code = 1;

        /// <summary>The cue length in centimeters, or null to omit the length_cm key.</summary>
        public float? lengthCm = 30f;

        /// <summary>The cue texture file name, or null to omit the texture key.</summary>
        public string texture = "Gray Cue 2x1.png";

        /// <summary>The literal YAML text emitted for a key, overriding whatever the typed field holds.</summary>
        /// <remarks>
        /// An entry reaches the ConfigLoader branches a well-typed value cannot express, such as a wrong-typed or
        /// malformed scalar.
        /// </remarks>
        public readonly Dictionary<string, string> rawOverrides = new Dictionary<string, string>();

        /// <summary>Creates a cue block with the supplied identity and the default length and texture.</summary>
        /// <param name="cueName">The cue name.</param>
        /// <param name="cueCode">The cue byte code.</param>
        /// <returns>The cue block builder.</returns>
        public static CueYaml Named(string cueName, int cueCode)
        {
            return new CueYaml { name = cueName, code = cueCode };
        }

        /// <summary>Appends this cue as a YAML sequence item under the cues key.</summary>
        /// <param name="builder">The document builder the cue block is appended to.</param>
        internal void AppendTo(StringBuilder builder)
        {
            List<string> lines = new List<string>();
            AppendEntry(lines, "name", name == null ? null : YamlScalar.Text(name));
            AppendEntry(lines, "code", code.HasValue ? YamlScalar.Integer(code.Value) : null);
            AppendEntry(lines, "length_cm", lengthCm.HasValue ? YamlScalar.Number(lengthCm.Value) : null);
            AppendEntry(lines, "texture", texture == null ? null : YamlScalar.Text(texture));

            foreach (KeyValuePair<string, string> entry in rawOverrides)
            {
                if (!lines.Exists(line => line.StartsWith($"{entry.Key}:", System.StringComparison.Ordinal)))
                {
                    lines.Add($"{entry.Key}: {entry.Value}");
                }
            }

            if (lines.Count == 0)
            {
                builder.AppendLine("  - {}");
                return;
            }

            builder.AppendLine($"  - {lines[0]}");
            for (int index = 1; index < lines.Count; index++)
            {
                builder.AppendLine($"    {lines[index]}");
            }
        }

        /// <summary>Adds one key line, preferring the raw override when the key carries one.</summary>
        /// <param name="lines">The accumulated key lines for this cue block.</param>
        /// <param name="key">The underscored YAML key.</param>
        /// <param name="renderedValue">The rendered typed value, or null when the key is omitted.</param>
        private void AppendEntry(List<string> lines, string key, string renderedValue)
        {
            if (rawOverrides.TryGetValue(key, out string rawValue))
            {
                lines.Add($"{key}: {rawValue}");
                return;
            }
            if (renderedValue == null)
            {
                return;
            }
            lines.Add($"{key}: {renderedValue}");
        }
    }
}
