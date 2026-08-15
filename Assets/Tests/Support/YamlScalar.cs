/// <summary>
/// Provides the YamlScalar helper that renders C# values as the YAML scalars a task template spells.
/// </summary>
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
        /// ConfigLoader rejects a non-finite geometry scalar, so a test covering that branch needs the loader to
        /// actually parse a NaN or an infinity rather than a token YamlDotNet would reject outright.
        /// </remarks>
        /// <param name="value">The number to render.</param>
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
        /// <param name="value">The integer to render.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Renders a boolean using YAML's lowercase spelling.</summary>
        /// <param name="value">The boolean to render.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Boolean(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>Renders a string as a double-quoted scalar with backslashes and quotes escaped.</summary>
        /// <param name="value">The string to render.</param>
        /// <returns>The YAML scalar text.</returns>
        internal static string Text(string value)
        {
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}
