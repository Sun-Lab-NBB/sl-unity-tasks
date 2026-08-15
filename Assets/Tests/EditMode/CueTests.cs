/// <summary>
/// Verifies the behavior of the Cue class.
/// </summary>
using NUnit.Framework;
using SL.Config;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the Cue class.</summary>
    [TestFixture]
    public class CueTests
    {
        /// <summary>
        /// The tolerance applied to a conversion whose exact quotient is not representable as a float.
        /// </summary>
        private const float Tolerance = 1e-6f;

        /// <summary>Verifies that a freshly constructed cue leaves every field at its CLR default.</summary>
        [Test]
        public void Constructor_Default_LeavesEveryFieldAtItsClrDefault()
        {
            Cue cue = new Cue();

            Assert.IsNull(cue.name);
            Assert.AreEqual(0, cue.code);
            Assert.AreEqual(0.0f, cue.lengthCm);
            Assert.IsNull(cue.texture);
        }

        /// <summary>
        /// Verifies that LengthUnity divides the centimeter length by the default ten-centimeter factor.
        /// </summary>
        [Test]
        public void LengthUnity_DefaultScaleFactor_DividesLengthByFactor()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float result = cue.LengthUnity(10.0f);

            Assert.AreEqual(3.0f, result);
        }

        /// <summary>Verifies that LengthUnity divides by a non-integral centimeters-per-unit factor.</summary>
        [Test]
        public void LengthUnity_NonIntegralScaleFactor_DividesLengthByFactor()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 12.5f,
            };

            float result = cue.LengthUnity(2.5f);

            Assert.AreEqual(5.0f, result);
        }

        /// <summary>
        /// Verifies that LengthUnity returns the float quotient when the division does not terminate.
        /// </summary>
        [Test]
        public void LengthUnity_RepeatingQuotient_ReturnsFloatQuotient()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 10.0f,
            };

            float result = cue.LengthUnity(3.0f);

            Assert.AreEqual(3.3333333f, result, Tolerance);
        }

        /// <summary>Verifies that a factor of exactly one leaves the centimeter length numerically unchanged.</summary>
        [Test]
        public void LengthUnity_UnitScaleFactor_ReturnsLengthUnchanged()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float result = cue.LengthUnity(1.0f);

            Assert.AreEqual(30.0f, result);
        }

        /// <summary>Verifies that a factor below one scales the length up.</summary>
        [Test]
        public void LengthUnity_ScaleFactorBelowOne_ReturnsLargerValue()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float result = cue.LengthUnity(0.5f);

            Assert.AreEqual(60.0f, result);
        }

        /// <summary>Verifies that a zero-length cue converts to zero Unity units for a finite factor.</summary>
        [Test]
        public void LengthUnity_ZeroLength_ReturnsZero()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 0.0f,
            };

            float result = cue.LengthUnity(10.0f);

            Assert.AreEqual(0.0f, result);
        }

        /// <summary>Verifies that a zero factor divides a positive length into positive infinity.</summary>
        [Test]
        public void LengthUnity_ZeroScaleFactor_ReturnsPositiveInfinity()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float result = cue.LengthUnity(0.0f);

            Assert.IsTrue(float.IsPositiveInfinity(result));
        }

        /// <summary>Verifies that a zero length divided by a zero factor produces a NaN.</summary>
        [Test]
        public void LengthUnity_ZeroLengthAndZeroScaleFactor_ReturnsNaN()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 0.0f,
            };

            float result = cue.LengthUnity(0.0f);

            Assert.IsTrue(float.IsNaN(result));
        }

        /// <summary>Verifies that a negative factor flips the sign of the converted length.</summary>
        [Test]
        public void LengthUnity_NegativeScaleFactor_ReturnsNegativeLength()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float result = cue.LengthUnity(-10.0f);

            Assert.AreEqual(-3.0f, result);
        }

        /// <summary>Verifies that a negative centimeter length converts to a negative Unity length.</summary>
        [Test]
        public void LengthUnity_NegativeLength_ReturnsNegativeLength()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = -45.0f,
            };

            float result = cue.LengthUnity(10.0f);

            Assert.AreEqual(-4.5f, result);
        }

        /// <summary>Verifies that LengthUnity is a pure read that leaves the stored centimeter length intact.</summary>
        [Test]
        public void LengthUnity_CalledTwice_LeavesLengthCmUnchanged()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float first = cue.LengthUnity(10.0f);
            float second = cue.LengthUnity(10.0f);

            Assert.AreEqual(3.0f, first);
            Assert.AreEqual(3.0f, second);
            Assert.AreEqual(30.0f, cue.lengthCm);
        }

        /// <summary>Verifies that LengthUnity honors the factor passed on each call.</summary>
        [Test]
        public void LengthUnity_DifferentFactorPerCall_UsesTheFactorSuppliedToThatCall()
        {
            Cue cue = new Cue
            {
                name = "A",
                code = 1,
                lengthCm = 30.0f,
            };

            float first = cue.LengthUnity(10.0f);
            float second = cue.LengthUnity(5.0f);

            Assert.AreEqual(3.0f, first);
            Assert.AreEqual(6.0f, second);
        }

        /// <summary>Verifies that the cue code field stores values outside the byte range without clamping.</summary>
        [Test]
        public void Code_ValuesOutsideByteRange_AreStoredWithoutClamping()
        {
            Cue belowRange = new Cue { name = "A", code = -1 };
            Cue atMaximum = new Cue { name = "B", code = 255 };
            Cue aboveRange = new Cue { name = "C", code = 256 };

            Assert.AreEqual(-1, belowRange.code);
            Assert.AreEqual(255, atMaximum.code);
            Assert.AreEqual(256, aboveRange.code);
        }
    }
}
