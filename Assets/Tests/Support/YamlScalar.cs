/// <summary>Provides the YamlScalar helper that renders C# values as the YAML scalars a task template spells.</summary>
using System.Globalization;

namespace SL.Tests
{
    /// <summary>
    /// Renders numbers, strings, and booleans as YAML scalars using invariant, round-trippable formatting.
    /// </summary>
    internal static class YamlScalar
    {
        /// <summary>Renders a single-precision number, mapping the non-finite values onto YAML's spellings.</summary>
        /// <remarks>
        /// ConfigLoader rejects a non-finite geometry scalar, so the non-finite spellings are the ones YamlDotNet
        /// parses back into a float.
        /// </remarks>
        /// <param name="value">The value whose YAML spelling is produced, including a non-finite one.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Number(float value)
        {
            if (float.IsNaN(value))
            {
                return ".nan";
            }
            if (float.IsPositiveInfinity(value))
            {
                return ".inf";
            }
            if (float.IsNegativeInfinity(value))
            {
                return "-.inf";
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Renders an integer.</summary>
        /// <param name="value">The value written as a plain YAML integer.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Renders a boolean using YAML's lowercase spelling.</summary>
        /// <param name="value">The value written as a YAML true or false token.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Boolean(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>Renders a string as a double-quoted scalar with backslashes and quotes escaped.</summary>
        /// <param name="value">The value quoted and escaped for the document.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Text(string value)
        {
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}
