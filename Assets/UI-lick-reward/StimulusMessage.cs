/// <summary>
/// Provides the StimulusMessage class that displays a temporary UI indicator for stimulus events.
/// </summary>
using UnityEngine;

namespace SL.UI
{
    /// <summary>Schedules the indicator's destruction after a configurable delay.</summary>
    public class StimulusMessage : MonoBehaviour
    {
        /// <summary>The time in seconds before this indicator is destroyed.</summary>
        public float destroyTime = 4.0f;

        /// <summary>Schedules the destruction of this game object after the specified delay.</summary>
        private void Start()
        {
            Destroy(gameObject, destroyTime);
        }
    }
}
