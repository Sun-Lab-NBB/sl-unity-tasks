/// <summary>
/// Verifies the behavior of the DisplaySettings class.
/// </summary>
using System;
using Gimbl;
using NUnit.Framework;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the DisplaySettings class.</summary>
    [TestFixture]
    public class DisplaySettingsTests
    {
        /// <summary>The settings instance backing each test.</summary>
        private DisplaySettings _settings;

        /// <summary>Creates a fresh settings instance before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<DisplaySettings>();
        }

        /// <summary>Destroys the settings instance after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
            }
            _settings = null;
        }

        /// <summary>Verifies that a new settings asset starts at half brightness.</summary>
        [Test]
        public void CreateInstance_NewSettings_DefaultsBrightnessToFifty()
        {
            Assert.AreEqual(50f, _settings.brightness, 1e-6f);
        }

        /// <summary>Verifies that a new settings asset starts at the default VR eye height.</summary>
        [Test]
        public void CreateInstance_NewSettings_DefaultsHeightInVRToTwoTenths()
        {
            Assert.AreEqual(0.2f, _settings.heightInVR, 1e-6f);
        }

        /// <summary>Verifies that the brightness field stores the value assigned to it.</summary>
        [Test]
        public void Brightness_AssignedValue_RoundTrips()
        {
            _settings.brightness = 12.5f;

            Assert.AreEqual(12.5f, _settings.brightness, 1e-6f);
        }

        /// <summary>Verifies that the VR eye height field stores the value assigned to it.</summary>
        [Test]
        public void HeightInVR_AssignedValue_RoundTrips()
        {
            _settings.heightInVR = -0.75f;

            Assert.AreEqual(-0.75f, _settings.heightInVR, 1e-6f);
        }

        /// <summary>Verifies that two settings instances hold independent field values.</summary>
        [Test]
        public void CreateInstance_TwoSettings_HoldIndependentValues()
        {
            DisplaySettings other = ScriptableObject.CreateInstance<DisplaySettings>();
            try
            {
                _settings.brightness = 0f;

                Assert.AreEqual(0f, _settings.brightness, 1e-6f);
                Assert.AreEqual(50f, other.brightness, 1e-6f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        /// <summary>Verifies that the settings type derives from ScriptableObject.</summary>
        [Test]
        public void DisplaySettingsType_Declaration_DerivesFromScriptableObject()
        {
            Assert.IsTrue(typeof(DisplaySettings).IsSubclassOf(typeof(ScriptableObject)));
        }

        /// <summary>Verifies that the settings type is serializable, as the display settings asset requires.</summary>
        [Test]
        public void DisplaySettingsType_Declaration_IsSerializable()
        {
            Assert.IsTrue(typeof(DisplaySettings).IsDefined(typeof(SerializableAttribute), inherit: false));
        }
    }
}
