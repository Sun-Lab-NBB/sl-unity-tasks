/// <summary>
/// Provides utilities for programmatically managing Unity tags and layers.
///
/// Adapted from https://answers.unity.com/questions/33597/is-it-possible-to-create-a-tag-programmatically.html
/// </summary>
#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Gimbl
{
    /// <summary>Manages Unity tags and layers through the TagManager asset.</summary>
    public static class TagsAndLayers
    {
        /// <summary>The maximum number of tags allowed.</summary>
        private const int MaxTags = 10000;

        /// <summary>The exclusive upper bound on layer slot indices examined (slots 0..31).</summary>
        private const int MaxLayers = 32;

        /// <summary>Adds a new tag to the project if it does not already exist.</summary>
        /// <param name="tagName">The name of the tag to add.</param>
        /// <returns>True if the tag was added, false if it already exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The project already holds the maximum number of tags.
        /// </exception>
        public static bool AddTag(string tagName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
            );
            SerializedProperty tagsProperty = tagManager.FindProperty("tags");
            if (tagsProperty.arraySize >= MaxTags)
            {
                throw new InvalidOperationException(
                    $"No more tags can be added to the Tags property. You have {tagsProperty.arraySize} tags."
                );
            }
            if (!PropertyExists(tagsProperty, start: 0, end: tagsProperty.arraySize, value: tagName))
            {
                int index = tagsProperty.arraySize;
                tagsProperty.InsertArrayElementAtIndex(index);
                SerializedProperty newTag = tagsProperty.GetArrayElementAtIndex(index);
                newTag.stringValue = tagName;
                tagManager.ApplyModifiedProperties();
                return true;
            }
            return false;
        }

        /// <summary>Adds a new layer to the project if it does not already exist.</summary>
        /// <param name="layerName">The name of the layer to add.</param>
        /// <returns>True if the layer was added, false if it already exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// All allowed layer slots are already filled.
        /// </exception>
        public static bool AddLayer(string layerName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
            );
            SerializedProperty layersProperty = tagManager.FindProperty("layers");
            int layerSlotBound = Math.Min(MaxLayers, layersProperty.arraySize);
            if (!PropertyExists(layersProperty, start: 0, end: layerSlotBound, value: layerName))
            {
                SerializedProperty layerSlot;
                for (int layerIndex = 8; layerIndex < layerSlotBound; layerIndex++)
                {
                    layerSlot = layersProperty.GetArrayElementAtIndex(layerIndex);
                    if (string.IsNullOrEmpty(layerSlot.stringValue))
                    {
                        layerSlot.stringValue = layerName;
                        tagManager.ApplyModifiedProperties();
                        return true;
                    }
                }
                throw new InvalidOperationException("All allowed layers have been filled.");
            }
            return false;
        }

        /// <summary>Checks if a value exists in a serialized array property.</summary>
        /// <param name="property">The serialized array property to search.</param>
        /// <param name="start">The starting index for the search.</param>
        /// <param name="end">
        /// The exclusive upper bound (one past the last index) for the search, clamped to the number of elements the
        /// property stores.
        /// </param>
        /// <param name="value">The value to search for.</param>
        /// <returns>True if the value exists in the property range.</returns>
        private static bool PropertyExists(SerializedProperty property, int start, int end, string value)
        {
            int searchBound = Math.Min(end, property.arraySize);
            for (int elementIndex = start; elementIndex < searchBound; elementIndex++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(elementIndex);
                if (element.stringValue.Equals(value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
