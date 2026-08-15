/// <summary>
/// Verifies the behavior of the FullScreenViewsSaved class.
/// </summary>
using System;
using System.Collections.Generic;
using Gimbl;
using NUnit.Framework;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the FullScreenViewsSaved class.</summary>
    [TestFixture]
    public class FullScreenViewsSavedTests
    {
        /// <summary>The saved-views instance backing each test.</summary>
        private FullScreenViewsSaved _savedViews;

        /// <summary>Creates a fresh saved-views instance before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedViews = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
        }

        /// <summary>Destroys the saved-views instance after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_savedViews != null)
            {
                UnityEngine.Object.DestroyImmediate(_savedViews);
            }
            _savedViews = null;
        }

        /// <summary>Verifies that a newly created asset exposes an empty camera name list.</summary>
        [Test]
        public void OnEnable_NewInstance_AllocatesAnEmptyCameraNameList()
        {
            Assert.IsNotNull(_savedViews.cameraNames);
            Assert.AreEqual(0, _savedViews.cameraNames.Count);
        }

        /// <summary>Verifies that a second enable pass keeps the camera names the asset already carries.</summary>
        [Test]
        public void OnEnable_PopulatedCameraNames_KeepsTheExistingList()
        {
            List<string> existing = new List<string> { "Display/Left View", string.Empty };
            _savedViews.cameraNames = existing;

            PrivateAccess.Invoke(_savedViews, "OnEnable");

            Assert.AreSame(existing, _savedViews.cameraNames);
            Assert.AreEqual(2, _savedViews.cameraNames.Count);
            Assert.AreEqual("Display/Left View", _savedViews.cameraNames[0]);
            Assert.AreEqual(string.Empty, _savedViews.cameraNames[1]);
        }

        /// <summary>Verifies that an enable pass over a cleared field allocates a replacement list.</summary>
        [Test]
        public void OnEnable_NullCameraNames_AllocatesAReplacementList()
        {
            _savedViews.cameraNames = null;

            PrivateAccess.Invoke(_savedViews, "OnEnable");

            Assert.IsNotNull(_savedViews.cameraNames);
            Assert.AreEqual(0, _savedViews.cameraNames.Count);
        }

        /// <summary>Verifies that two saved-views instances hold independent camera name lists.</summary>
        [Test]
        public void OnEnable_TwoInstances_HoldIndependentCameraNameLists()
        {
            FullScreenViewsSaved other = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
            try
            {
                _savedViews.cameraNames.Add("Display/Left View");

                Assert.AreEqual(1, _savedViews.cameraNames.Count);
                Assert.AreEqual(0, other.cameraNames.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        /// <summary>Verifies that the saved-views type derives from ScriptableObject.</summary>
        [Test]
        public void FullScreenViewsSavedType_Declaration_DerivesFromScriptableObject()
        {
            Assert.IsTrue(typeof(FullScreenViewsSaved).IsSubclassOf(typeof(ScriptableObject)));
        }

        /// <summary>Verifies that the saved-views type is serializable, as the companion asset requires.</summary>
        [Test]
        public void FullScreenViewsSavedType_Declaration_IsSerializable()
        {
            Assert.IsTrue(typeof(FullScreenViewsSaved).IsDefined(typeof(SerializableAttribute), inherit: false));
        }
    }
}
