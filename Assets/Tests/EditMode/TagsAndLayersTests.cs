/// <summary>Verifies the behavior of the TagsAndLayers class.</summary>
using System;
using Gimbl;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the TagsAndLayers class.</summary>
    /// <remarks>
    /// Tests that add a tag or layer write into the project's TagManager asset, so each name a test introduces carries
    /// the ZZTest prefix and teardown removes every prefixed tag and layer. The assertions read the asset back through
    /// a separate SerializedObject rather than through the class under test, so a broken PropertyExists cannot mask a
    /// failure.
    /// </remarks>
    [TestFixture]
    public class TagsAndLayersTests
    {
        /// <summary>The prefix every tag and layer name a test introduces carries.</summary>
        private const string TestPrefix = "ZZTest";

        /// <summary>The lowest layer slot the layer writer is allowed to fill.</summary>
        private const int FirstWritableLayerSlot = 8;

        /// <summary>The exclusive upper bound on the layer slots the layer writer examines.</summary>
        private const int LayerSlotBound = 32;

        /// <summary>The project path of the TagManager asset both the class under test and the tests open.</summary>
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";

        /// <summary>Removes every tag and layer the finished test introduced.</summary>
        [TearDown]
        public void TearDown()
        {
            SerializedObject tagManager = OpenTagManager();
            SerializedProperty tags = tagManager.FindProperty("tags");
            for (int index = tags.arraySize - 1; index >= 0; index--)
            {
                if (tags.GetArrayElementAtIndex(index).stringValue.StartsWith(TestPrefix, StringComparison.Ordinal))
                {
                    tags.DeleteArrayElementAtIndex(index);
                }
            }

            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int index = FirstWritableLayerSlot; index < LayerSlotBound; index++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(index);
                if (slot.stringValue.StartsWith(TestPrefix, StringComparison.Ordinal))
                {
                    slot.stringValue = string.Empty;
                }
            }

            tagManager.ApplyModifiedProperties();
        }

        /// <summary>Verifies that adding an unknown tag reports success and registers exactly one entry.</summary>
        [Test]
        public void AddTag_UnknownTag_ReturnsTrueAndRegistersOneEntry()
        {
            string tagName = $"{TestPrefix}TagAlpha";

            bool added = TagsAndLayers.AddTag(tagName);

            Assert.IsTrue(added);
            Assert.AreEqual(1, CountTags(tagName));
        }

        /// <summary>Verifies that adding the same tag twice reports failure and leaves one entry.</summary>
        [Test]
        public void AddTag_SameTagAddedTwice_ReturnsFalseAndLeavesOneEntry()
        {
            string tagName = $"{TestPrefix}TagBeta";
            TagsAndLayers.AddTag(tagName);

            bool addedAgain = TagsAndLayers.AddTag(tagName);

            Assert.IsFalse(addedAgain);
            Assert.AreEqual(1, CountTags(tagName));
        }

        /// <summary>Verifies that adding a tag the project already defines reports failure.</summary>
        [Test]
        public void AddTag_ProjectDefinedTag_ReturnsFalseAndAddsNoEntry()
        {
            int countBefore = TagArraySize();

            bool added = TagsAndLayers.AddTag("VRDisplay");

            Assert.IsFalse(added);
            Assert.AreEqual(1, CountTags("VRDisplay"));
            Assert.AreEqual(countBefore, TagArraySize());
        }

        /// <summary>Verifies that a new tag is appended after the last existing tag entry.</summary>
        [Test]
        public void AddTag_UnknownTag_AppendsTheEntryAtTheEndOfTheList()
        {
            string tagName = $"{TestPrefix}TagGamma";
            int countBefore = TagArraySize();

            TagsAndLayers.AddTag(tagName);

            Assert.AreEqual(countBefore + 1, TagArraySize());
            Assert.AreEqual(tagName, TagAt(countBefore));
        }

        /// <summary>Verifies that adding an unknown layer fills the lowest empty writable slot.</summary>
        [Test]
        public void AddLayer_UnknownLayer_ReturnsTrueAndFillsTheLowestEmptyWritableSlot()
        {
            string layerName = $"{TestPrefix}LayerAlpha";
            int expectedSlot = FirstEmptyLayerSlot();
            Assert.GreaterOrEqual(expectedSlot, FirstWritableLayerSlot);

            bool added = TagsAndLayers.AddLayer(layerName);

            Assert.IsTrue(added);
            Assert.AreEqual(layerName, LayerAt(expectedSlot));
            Assert.AreEqual(1, CountLayers(layerName));
        }

        /// <summary>Verifies that a layer added through the class under test resolves by name afterwards.</summary>
        [Test]
        public void AddLayer_UnknownLayer_BecomesResolvableByName()
        {
            string layerName = $"{TestPrefix}LayerDelta";
            int expectedSlot = FirstEmptyLayerSlot();

            TagsAndLayers.AddLayer(layerName);

            Assert.AreEqual(expectedSlot, LayerMask.NameToLayer(layerName));
        }

        /// <summary>Verifies that adding the same layer twice reports failure and leaves one slot.</summary>
        [Test]
        public void AddLayer_SameLayerAddedTwice_ReturnsFalseAndLeavesOneSlot()
        {
            string layerName = $"{TestPrefix}LayerBeta";
            TagsAndLayers.AddLayer(layerName);

            bool addedAgain = TagsAndLayers.AddLayer(layerName);

            Assert.IsFalse(addedAgain);
            Assert.AreEqual(1, CountLayers(layerName));
        }

        /// <summary>Verifies that a built-in layer below the writable range is recognized as existing.</summary>
        [Test]
        public void AddLayer_BuiltInLayerBelowTheWritableRange_ReturnsFalse()
        {
            int emptySlotBefore = FirstEmptyLayerSlot();

            bool added = TagsAndLayers.AddLayer("Default");

            Assert.IsFalse(added);
            Assert.AreEqual("Default", LayerAt(0));
            Assert.AreEqual(emptySlotBefore, FirstEmptyLayerSlot());
        }

        /// <summary>Verifies that the project layer occupying the first writable slot is left in place.</summary>
        [Test]
        public void AddLayer_ProjectDefinedLayerAtTheFirstWritableSlot_ReturnsFalse()
        {
            bool added = TagsAndLayers.AddLayer("Actor");

            Assert.IsFalse(added);
            Assert.AreEqual("Actor", LayerAt(FirstWritableLayerSlot));
        }

        /// <summary>Verifies that adding a layer with every writable slot filled reports the exhaustion.</summary>
        [Test]
        public void AddLayer_EveryWritableSlotFilled_ThrowsInvalidOperation()
        {
            FillEveryWritableLayerSlot();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                TagsAndLayers.AddLayer($"{TestPrefix}LayerOverflow")
            );

            StringAssert.Contains("All allowed layers have been filled.", exception.Message);
        }

        /// <summary>Verifies that the highest layer slot is filled once every writable slot below it is taken.
        /// </summary>
        [Test]
        public void AddLayer_OnlyTheHighestSlotEmpty_FillsTheHighestSlot()
        {
            string layerName = $"{TestPrefix}LayerHighest";
            int highestSlot = LayerSlotBound - 1;
            FillWritableLayerSlotsBelow(highestSlot);
            Assert.AreEqual(highestSlot, FirstEmptyLayerSlot());

            bool added = TagsAndLayers.AddLayer(layerName);

            Assert.IsTrue(added);
            Assert.AreEqual(layerName, LayerAt(highestSlot));
            Assert.AreEqual(1, CountLayers(layerName));
        }

        /// <summary>Verifies that a search bound past the stored entries stops at the last one it holds.</summary>
        [Test]
        public void PropertyExists_EndBeyondTheStoredEntries_StopsAtTheLastStoredEntry()
        {
            SerializedProperty tags = OpenTagManager().FindProperty("tags");

            bool exists = (bool)
                PrivateAccess.InvokeStatic(
                    typeof(TagsAndLayers),
                    "PropertyExists",
                    tags,
                    0,
                    tags.arraySize + 8,
                    $"{TestPrefix}AbsentTag"
                );

            Assert.IsFalse(exists);
        }

        /// <summary>Opens the project's TagManager asset as a serialized object.</summary>
        /// <returns>The serialized TagManager.</returns>
        private static SerializedObject OpenTagManager()
        {
            return new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(TagManagerPath)[0]);
        }

        /// <summary>Counts the tag entries carrying the specified name.</summary>
        /// <param name="tagName">The tag name to count.</param>
        /// <returns>The number of matching entries.</returns>
        private static int CountTags(string tagName)
        {
            SerializedProperty tags = OpenTagManager().FindProperty("tags");
            int matches = 0;
            for (int index = 0; index < tags.arraySize; index++)
            {
                if (string.Equals(tags.GetArrayElementAtIndex(index).stringValue, tagName, StringComparison.Ordinal))
                {
                    matches++;
                }
            }
            return matches;
        }

        /// <summary>Returns the number of entries in the project's tag list.</summary>
        /// <returns>The tag entry count.</returns>
        private static int TagArraySize()
        {
            return OpenTagManager().FindProperty("tags").arraySize;
        }

        /// <summary>Returns the tag name stored at the specified list index.</summary>
        /// <param name="index">The tag list index to read.</param>
        /// <returns>The stored tag name.</returns>
        private static string TagAt(int index)
        {
            return OpenTagManager().FindProperty("tags").GetArrayElementAtIndex(index).stringValue;
        }

        /// <summary>Counts the layer slots below the examined bound carrying the specified name.</summary>
        /// <param name="layerName">The layer name to count.</param>
        /// <returns>The number of matching slots.</returns>
        private static int CountLayers(string layerName)
        {
            SerializedProperty layers = OpenTagManager().FindProperty("layers");
            int matches = 0;
            for (int index = 0; index < LayerSlotBound; index++)
            {
                string slotValue = layers.GetArrayElementAtIndex(index).stringValue;
                if (string.Equals(slotValue, layerName, StringComparison.Ordinal))
                {
                    matches++;
                }
            }
            return matches;
        }

        /// <summary>Returns the layer name stored in the specified slot.</summary>
        /// <param name="index">The layer slot to read.</param>
        /// <returns>The stored layer name.</returns>
        private static string LayerAt(int index)
        {
            return OpenTagManager().FindProperty("layers").GetArrayElementAtIndex(index).stringValue;
        }

        /// <summary>Returns the lowest empty writable layer slot, or the examined bound when none is empty.</summary>
        /// <returns>The lowest empty writable slot index.</returns>
        private static int FirstEmptyLayerSlot()
        {
            SerializedProperty layers = OpenTagManager().FindProperty("layers");
            for (int index = FirstWritableLayerSlot; index < LayerSlotBound; index++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue))
                {
                    return index;
                }
            }
            return LayerSlotBound;
        }

        /// <summary>Fills every empty writable layer slot with a removable prefixed placeholder name.</summary>
        private static void FillEveryWritableLayerSlot()
        {
            FillWritableLayerSlotsBelow(LayerSlotBound);
        }

        /// <summary>Fills every empty writable layer slot below the specified bound with a placeholder name.</summary>
        /// <param name="exclusiveBound">The exclusive upper bound on the slots that receive a placeholder.</param>
        private static void FillWritableLayerSlotsBelow(int exclusiveBound)
        {
            SerializedObject tagManager = OpenTagManager();
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int index = FirstWritableLayerSlot; index < exclusiveBound; index++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(index);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = $"{TestPrefix}Fill{index}";
                }
            }
            tagManager.ApplyModifiedProperties();
        }
    }
}
