/// <summary>
/// Verifies the behavior of the VREnvironment class.
/// </summary>
using NUnit.Framework;
using SL.Config;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the VREnvironment class.</summary>
    [TestFixture]
    public class VREnvironmentTests
    {
        /// <summary>
        /// The tolerance applied to a conversion whose exact quotient is not representable as a float.
        /// </summary>
        private const float Tolerance = 1e-6f;

        /// <summary>Verifies that a freshly constructed environment carries the documented field defaults.</summary>
        [Test]
        public void Constructor_Default_MatchesDocumentedFieldDefaults()
        {
            VREnvironment environment = new VREnvironment();

            Assert.AreEqual(20.0f, environment.corridorSpacingCm);
            Assert.AreEqual(3, environment.segmentsPerCorridor);
            Assert.AreEqual("Padding", environment.paddingPrefabName);
            Assert.AreEqual(10.0f, environment.cmPerUnityUnit);
            Assert.AreEqual(0.0f, environment.cueOffsetCm);
        }

        /// <summary>Verifies that the default spacing and default scale factor convert to two Unity units.</summary>
        [Test]
        public void CorridorSpacingUnity_DefaultConfiguration_ReturnsTwoUnits()
        {
            VREnvironment environment = new VREnvironment();

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(2.0f, result);
        }

        /// <summary>Verifies that CorridorSpacingUnity divides by a non-integral centimeters-per-unit factor.</summary>
        [Test]
        public void CorridorSpacingUnity_NonIntegralScaleFactor_DividesSpacingByFactor()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 17.5f, cmPerUnityUnit = 2.5f };

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(7.0f, result);
        }

        /// <summary>Verifies that CorridorSpacingUnity returns the float quotient when the division repeats.</summary>
        [Test]
        public void CorridorSpacingUnity_RepeatingQuotient_ReturnsFloatQuotient()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 10.0f, cmPerUnityUnit = 3.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(3.3333333f, result, Tolerance);
        }

        /// <summary>Verifies that a zero spacing converts to zero Unity units for a finite factor.</summary>
        [Test]
        public void CorridorSpacingUnity_ZeroSpacing_ReturnsZero()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 0.0f, cmPerUnityUnit = 10.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(0.0f, result);
        }

        /// <summary>Verifies that a negative spacing converts to a negative Unity offset.</summary>
        [Test]
        public void CorridorSpacingUnity_NegativeSpacing_ReturnsNegativeUnits()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = -25.0f, cmPerUnityUnit = 10.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(-2.5f, result);
        }

        /// <summary>Verifies that a negative scale factor flips the sign of the converted spacing.</summary>
        [Test]
        public void CorridorSpacingUnity_NegativeScaleFactor_ReturnsNegativeUnits()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 20.0f, cmPerUnityUnit = -10.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.AreEqual(-2.0f, result);
        }

        /// <summary>Verifies that a zero scale factor divides a positive spacing into positive infinity.</summary>
        [Test]
        public void CorridorSpacingUnity_ZeroScaleFactor_ReturnsPositiveInfinity()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 20.0f, cmPerUnityUnit = 0.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.IsTrue(float.IsPositiveInfinity(result));
        }

        /// <summary>Verifies that a zero spacing over a zero scale factor produces a NaN rather than a throw.</summary>
        [Test]
        public void CorridorSpacingUnity_ZeroSpacingAndZeroScaleFactor_ReturnsNaN()
        {
            VREnvironment environment = new VREnvironment { corridorSpacingCm = 0.0f, cmPerUnityUnit = 0.0f };

            float result = environment.CorridorSpacingUnity;

            Assert.IsTrue(float.IsNaN(result));
        }

        /// <summary>Verifies that CorridorSpacingUnity recomputes from the current fields on every read.</summary>
        [Test]
        public void CorridorSpacingUnity_FieldsChangedAfterFirstRead_RecomputesFromCurrentFields()
        {
            VREnvironment environment = new VREnvironment();
            float first = environment.CorridorSpacingUnity;

            environment.corridorSpacingCm = 45.0f;
            environment.cmPerUnityUnit = 5.0f;
            float second = environment.CorridorSpacingUnity;

            Assert.AreEqual(2.0f, first);
            Assert.AreEqual(9.0f, second);
        }

        /// <summary>Verifies that the default cue offset converts to zero Unity units.</summary>
        [Test]
        public void CueOffsetUnity_DefaultConfiguration_ReturnsZero()
        {
            VREnvironment environment = new VREnvironment();

            float result = environment.CueOffsetUnity;

            Assert.AreEqual(0.0f, result);
        }

        /// <summary>Verifies that CueOffsetUnity divides by a non-integral centimeters-per-unit factor.</summary>
        [Test]
        public void CueOffsetUnity_NonIntegralScaleFactor_DividesOffsetByFactor()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = 7.5f, cmPerUnityUnit = 2.5f };

            float result = environment.CueOffsetUnity;

            Assert.AreEqual(3.0f, result);
        }

        /// <summary>Verifies that CueOffsetUnity returns the float quotient when the division repeats.</summary>
        [Test]
        public void CueOffsetUnity_RepeatingQuotient_ReturnsFloatQuotient()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = 10.0f, cmPerUnityUnit = 3.0f };

            float result = environment.CueOffsetUnity;

            Assert.AreEqual(3.3333333f, result, Tolerance);
        }

        /// <summary>Verifies that a negative cue offset converts to a negative upstream Unity shift.</summary>
        [Test]
        public void CueOffsetUnity_NegativeOffset_ReturnsNegativeUnits()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = -15.0f, cmPerUnityUnit = 10.0f };

            float result = environment.CueOffsetUnity;

            Assert.AreEqual(-1.5f, result);
        }

        /// <summary>Verifies that a zero scale factor divides a positive cue offset into positive infinity.</summary>
        [Test]
        public void CueOffsetUnity_ZeroScaleFactor_ReturnsPositiveInfinity()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = 5.0f, cmPerUnityUnit = 0.0f };

            float result = environment.CueOffsetUnity;

            Assert.IsTrue(float.IsPositiveInfinity(result));
        }

        /// <summary>
        /// Verifies that a zero cue offset over a zero scale factor produces a NaN rather than a throw.
        /// </summary>
        [Test]
        public void CueOffsetUnity_ZeroOffsetAndZeroScaleFactor_ReturnsNaN()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = 0.0f, cmPerUnityUnit = 0.0f };

            float result = environment.CueOffsetUnity;

            Assert.IsTrue(float.IsNaN(result));
        }

        /// <summary>Verifies that CueOffsetUnity recomputes from the current fields on every read.</summary>
        [Test]
        public void CueOffsetUnity_FieldsChangedAfterFirstRead_RecomputesFromCurrentFields()
        {
            VREnvironment environment = new VREnvironment { cueOffsetCm = 20.0f };
            float first = environment.CueOffsetUnity;

            environment.cueOffsetCm = 30.0f;
            environment.cmPerUnityUnit = 4.0f;
            float second = environment.CueOffsetUnity;

            Assert.AreEqual(2.0f, first);
            Assert.AreEqual(7.5f, second);
        }

        /// <summary>
        /// Verifies that the two converters read their own centimeter field rather than each other's.
        /// </summary>
        [Test]
        public void ConversionProperties_DistinctCentimeterFields_ConvertIndependently()
        {
            VREnvironment environment = new VREnvironment
            {
                corridorSpacingCm = 20.0f,
                cueOffsetCm = 50.0f,
                cmPerUnityUnit = 10.0f,
            };

            Assert.AreEqual(2.0f, environment.CorridorSpacingUnity);
            Assert.AreEqual(5.0f, environment.CueOffsetUnity);
        }
    }
}
