/// <summary>
/// Provides the Cue class that defines a single visual cue used in the VR environment.
/// </summary>
using System;

namespace SL.Config
{
    /// <summary>Defines a single visual cue used in the VR environment.</summary>
    [Serializable]
    public class Cue
    {
        /// <summary>The visual identifier for the cue (e.g., 'A', 'B', 'Gray').</summary>
        public string name;

        /// <summary>The unique uint8 code (0-255) used for MQTT communication and data analysis.</summary>
        public int code;

        /// <summary>The length of the cue in centimeters.</summary>
        public float lengthCm;

        /// <summary>
        /// The texture filename (e.g., "Cue 001 - 2x1 repeat.png") located in Assets/InfiniteCorridorTask/Textures/.
        /// </summary>
        public string texture;

        /// <summary>Returns the length in Unity units given a cm-per-unit conversion factor.</summary>
        /// <param name="cmPerUnit">
        /// The scene's centimeter-to-Unity-unit scale, taken from the VR environment block.
        /// </param>
        /// <returns>The cue length in Unity units.</returns>
        public float LengthUnity(float cmPerUnit) => lengthCm / cmPerUnit;
    }
}
