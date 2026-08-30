/// <summary>
/// Provides utility functions for measuring prefab dimensions.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>Provides static utility functions for measuring prefab dimensions.</summary>
    public static class Utility
    {
        /// <summary>
        /// Calculates the z-axis length of a prefab by combining the bounds of every Renderer on the prefab and its
        /// active children. A renderer under a deactivated object stays outside the combined bounds.
        /// </summary>
        /// <param name="prefab">The root object whose own and child renderers bound the measurement.</param>
        /// <returns>The z-axis size of the combined bounds, or 0 when the prefab carries no renderers.</returns>
        public static float GetPrefabLength(GameObject prefab)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
            {
                string message =
                    $"Unable to measure the z-axis length of prefab '{prefab.name}'. The prefab must carry at "
                    + "least one renderer on itself or an active child, but it carries none, so the measured "
                    + "length is 0.";
                Debug.LogWarning(message);
                return 0f;
            }

            Bounds combinedBounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }

            return combinedBounds.size.z;
        }
    }
}
