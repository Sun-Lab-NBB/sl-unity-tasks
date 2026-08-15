/// <summary>Provides the VrEnvironmentYaml builder describing a task template's vr_environment block.</summary>
using System.Collections.Generic;
using System.Text;

namespace SL.Tests
{
    /// <summary>Builds the YAML block for the VR corridor geometry configuration.</summary>
    /// <remarks>
    /// A typed field set to null omits its YAML key entirely, which is how a test reaches ConfigLoader's missing-field
    /// branches.
    /// </remarks>
    public sealed class VrEnvironmentYaml
    {
        /// <summary>The horizontal spacing between corridors in centimeters, or null to omit the key.</summary>
        public float? corridorSpacingCm = 20f;

        /// <summary>The corridor depth in segments, or null to omit the key.</summary>
        public int? segmentsPerCorridor = 3;

        /// <summary>The padding prefab name, or null to omit the key.</summary>
        public string paddingPrefabName = "Padding";

        /// <summary>The centimeters represented by one Unity unit, or null to omit the key.</summary>
        public float? cmPerUnityUnit = 10f;

        /// <summary>The animal start offset in centimeters, or null to omit the key.</summary>
        public float? cueOffsetCm = 0f;

        /// <summary>The literal YAML text emitted for a key, overriding whatever the typed field holds.</summary>
        public readonly Dictionary<string, string> rawOverrides = new Dictionary<string, string>();

        /// <summary>Appends the vr_environment mapping body.</summary>
        /// <param name="builder">The document builder the block is appended to.</param>
        internal void AppendTo(StringBuilder builder)
        {
            AppendEntry(
                builder,
                "corridor_spacing_cm",
                corridorSpacingCm.HasValue ? YamlScalar.Number(corridorSpacingCm.Value) : null
            );
            AppendEntry(
                builder,
                "segments_per_corridor",
                segmentsPerCorridor.HasValue ? YamlScalar.Integer(segmentsPerCorridor.Value) : null
            );
            AppendEntry(
                builder,
                "padding_prefab_name",
                paddingPrefabName == null ? null : YamlScalar.Text(paddingPrefabName)
            );
            AppendEntry(
                builder,
                "cm_per_unity_unit",
                cmPerUnityUnit.HasValue ? YamlScalar.Number(cmPerUnityUnit.Value) : null
            );
            AppendEntry(builder, "cue_offset_cm", cueOffsetCm.HasValue ? YamlScalar.Number(cueOffsetCm.Value) : null);

            foreach (KeyValuePair<string, string> entry in rawOverrides)
            {
                if (!IsTypedKey(entry.Key))
                {
                    builder.AppendLine($"  {entry.Key}: {entry.Value}");
                }
            }
        }

        /// <summary>Determines whether a key is one of the typed fields this builder already emits.</summary>
        /// <param name="key">The underscored YAML key.</param>
        /// <returns>True when the key names a typed field, false otherwise.</returns>
        private static bool IsTypedKey(string key)
        {
            return key switch
            {
                "corridor_spacing_cm" => true,
                "segments_per_corridor" => true,
                "padding_prefab_name" => true,
                "cm_per_unity_unit" => true,
                "cue_offset_cm" => true,
                _ => false,
            };
        }

        /// <summary>Appends one key line, preferring the raw override when the key carries one.</summary>
        /// <param name="builder">The document builder the line is appended to.</param>
        /// <param name="key">The underscored YAML key.</param>
        /// <param name="renderedValue">The rendered typed value, or null when the key is omitted.</param>
        private void AppendEntry(StringBuilder builder, string key, string renderedValue)
        {
            if (rawOverrides.TryGetValue(key, out string rawValue))
            {
                builder.AppendLine($"  {key}: {rawValue}");
                return;
            }
            if (renderedValue == null)
            {
                return;
            }
            builder.AppendLine($"  {key}: {renderedValue}");
        }
    }
}
