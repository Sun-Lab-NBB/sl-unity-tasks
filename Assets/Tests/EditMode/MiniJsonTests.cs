/// <summary>
/// Verifies the behavior of the MiniJson class.
/// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using SL.Tasks;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the MiniJson serializer and recursive-descent parser.</summary>
    [TestFixture]
    public class MiniJsonTests
    {
        /// <summary>The nesting depth used by the deeply nested object and array tests.</summary>
        private const int NestingDepth = 40;

        /// <summary>The number of control characters JSON forbids raw inside a quoted string.</summary>
        private const int ControlCharacterCount = 0x20;

        /// <summary>The ambient culture captured before each test, restored once the test completes.</summary>
        private CultureInfo _originalCulture;

        /// <summary>Captures the ambient culture before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _originalCulture = CultureInfo.CurrentCulture;
        }

        /// <summary>Restores the ambient culture after each test, including the culture swapping tests.</summary>
        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }

        /// <summary>Verifies that Serialize renders a null reference as the bare null literal.</summary>
        [Test]
        public void Serialize_NullValue_ReturnsNullLiteral()
        {
            Assert.AreEqual("null", MiniJson.Serialize(null));
        }

        /// <summary>Verifies that Serialize renders a true boolean as the bare true literal.</summary>
        [Test]
        public void Serialize_TrueValue_ReturnsTrueLiteral()
        {
            Assert.AreEqual("true", MiniJson.Serialize(true));
        }

        /// <summary>Verifies that Serialize renders a false boolean as the bare false literal.</summary>
        [Test]
        public void Serialize_FalseValue_ReturnsFalseLiteral()
        {
            Assert.AreEqual("false", MiniJson.Serialize(false));
        }

        /// <summary>Verifies that Serialize wraps a plain string in quotes without altering its characters.</summary>
        [Test]
        public void Serialize_PlainString_ReturnsQuotedText()
        {
            Assert.AreEqual("\"corridor\"", MiniJson.Serialize("corridor"));
        }

        /// <summary>Verifies that Serialize renders an empty string as an empty pair of quotes.</summary>
        [Test]
        public void Serialize_EmptyString_ReturnsEmptyQuotedText()
        {
            Assert.AreEqual("\"\"", MiniJson.Serialize(string.Empty));
        }

        /// <summary>Verifies that Serialize escapes the backslash before it escapes the quote.</summary>
        [Test]
        public void Serialize_StringWithBackslashAndQuote_EscapesBackslashFirst()
        {
            Assert.AreEqual("\"a\\\\b\\\"c\"", MiniJson.Serialize("a\\b\"c"));
        }

        /// <summary>Verifies that Serialize escapes the newline, carriage return, and tab characters.</summary>
        [Test]
        public void Serialize_StringWithLineBreaksAndTab_EscapesWhitespaceCharacters()
        {
            Assert.AreEqual("\"a\\nb\\rc\\td\"", MiniJson.Serialize("a\nb\rc\td"));
        }

        /// <summary>Verifies that Serialize escapes the backspace and form feed characters.</summary>
        [Test]
        public void Serialize_StringWithBackspaceAndFormFeed_EscapesBothCharacters()
        {
            Assert.AreEqual("\"a\\bb\\fc\"", MiniJson.Serialize("a\bb\fc"));
        }

        /// <summary>Verifies that Serialize escapes a control character carrying no shorter escape.</summary>
        [Test]
        public void Serialize_StringWithControlCharacter_EmitsUnicodeEscape()
        {
            Assert.AreEqual("\"a\\u0001b\"", MiniJson.Serialize("a\u0001b"));
        }

        /// <summary>Verifies that Serialize writes the hexadecimal digits of a unicode escape in lower case.</summary>
        [Test]
        public void Serialize_StringWithHighControlCharacter_WritesLowercaseHexadecimalDigits()
        {
            Assert.AreEqual("\"\\u001f\"", MiniJson.Serialize("\u001F"));
        }

        /// <summary>Verifies that Serialize escapes an embedded null character rather than emitting it raw.</summary>
        [Test]
        public void Serialize_StringWithNullCharacter_EmitsUnicodeEscape()
        {
            Assert.AreEqual("\"x\\u0000y\"", MiniJson.Serialize("x\0y"));
        }

        /// <summary>Verifies that Serialize leaves no raw control character anywhere in the emitted text.</summary>
        [Test]
        public void Serialize_EveryControlCharacter_EmitsNoRawControlCharacter()
        {
            string serialized = MiniJson.Serialize(BuildControlCharacterText());

            // The code point is compared as an integer, because a char comparison resolves through NUnit's
            // general-purpose comparer and does not reliably order two characters by their numeric value.
            for (int index = 0; index < serialized.Length; index++)
            {
                int codePoint = serialized[index];
                Assert.GreaterOrEqual(
                    codePoint,
                    ControlCharacterCount,
                    $"A raw control character U+{codePoint:X4} reached the output at index {index}."
                );
            }
        }

        /// <summary>Verifies that Serialize preserves the delete character, which JSON allows raw.</summary>
        [Test]
        public void Serialize_StringWithDeleteCharacter_LeavesCharacterUnescaped()
        {
            Assert.AreEqual("\"a\u007Fb\"", MiniJson.Serialize("a\u007Fb"));
        }

        /// <summary>Verifies that Serialize emits a forward slash unescaped.</summary>
        [Test]
        public void Serialize_StringWithForwardSlash_LeavesSlashUnescaped()
        {
            Assert.AreEqual("\"Assets/Tests\"", MiniJson.Serialize("Assets/Tests"));
        }

        /// <summary>Verifies that Serialize preserves non-ASCII characters verbatim.</summary>
        [Test]
        public void Serialize_NonAsciiString_PreservesCharactersVerbatim()
        {
            Assert.AreEqual("\"caf\u00E9 \u65E5\u672C\"", MiniJson.Serialize("caf\u00E9 \u65E5\u672C"));
        }

        /// <summary>Verifies that Serialize renders a 32-bit integer as a bare number.</summary>
        [Test]
        public void Serialize_IntegerValue_ReturnsBareNumber()
        {
            Assert.AreEqual("42", MiniJson.Serialize(42));
        }

        /// <summary>Verifies that Serialize renders a negative 64-bit integer as a bare number.</summary>
        [Test]
        public void Serialize_LongMinValue_ReturnsBareNumber()
        {
            Assert.AreEqual("-9223372036854775808", MiniJson.Serialize(long.MinValue));
        }

        /// <summary>Verifies that Serialize renders a float as a bare number using the invariant separator.</summary>
        [Test]
        public void Serialize_FloatValue_ReturnsBareNumber()
        {
            Assert.AreEqual("2.5", MiniJson.Serialize(2.5f));
        }

        /// <summary>Verifies that Serialize renders a negative double as a bare number.</summary>
        [Test]
        public void Serialize_NegativeDoubleValue_ReturnsBareNumber()
        {
            Assert.AreEqual("-3.75", MiniJson.Serialize(-3.75d));
        }

        /// <summary>Verifies that Serialize drops the fractional part of a whole-valued double.</summary>
        [Test]
        public void Serialize_WholeValuedDouble_ReturnsIntegerText()
        {
            Assert.AreEqual("2", MiniJson.Serialize(2.0d));
        }

        /// <summary>Verifies that Serialize emits the invariant not-a-number text for a NaN double.</summary>
        [Test]
        public void Serialize_NotANumberDouble_ReturnsBareNotANumberText()
        {
            Assert.AreEqual("NaN", MiniJson.Serialize(double.NaN));
        }

        /// <summary>Verifies that Serialize quotes a numeric type outside the recognized four.</summary>
        [Test]
        public void Serialize_ShortValue_FallsBackToQuotedText()
        {
            Assert.AreEqual("\"7\"", MiniJson.Serialize((short)7));
        }

        /// <summary>Verifies that Serialize quotes a decimal, which is not one of the four number types.</summary>
        [Test]
        public void Serialize_DecimalValue_FallsBackToQuotedText()
        {
            Assert.AreEqual("\"7\"", MiniJson.Serialize(7m));
        }

        /// <summary>Verifies that Serialize quotes and escapes the text form of an unrecognized value.</summary>
        [Test]
        public void Serialize_UnrecognizedValue_ReturnsQuotedEscapedText()
        {
            Assert.AreEqual("\"raw\\\"text\\\\here\"", MiniJson.Serialize(new UnrecognizedValue()));
        }

        /// <summary>Verifies that Serialize renders an empty object dictionary as an empty pair of braces.</summary>
        [Test]
        public void Serialize_EmptyObjectDictionary_ReturnsEmptyBraces()
        {
            Assert.AreEqual("{}", MiniJson.Serialize(new Dictionary<string, object>()));
        }

        /// <summary>Verifies that Serialize writes no separator for a single-entry object dictionary.</summary>
        [Test]
        public void Serialize_SingleEntryObjectDictionary_OmitsSeparator()
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object> { { "success", true } };

            Assert.AreEqual("{\"success\":true}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize separates two object dictionary entries with a single comma.</summary>
        [Test]
        public void Serialize_TwoEntryObjectDictionary_SeparatesEntriesWithComma()
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object> { { "a", 1 }, { "b", 2 } };

            Assert.AreEqual("{\"a\":1,\"b\":2}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize escapes an object dictionary key.</summary>
        [Test]
        public void Serialize_ObjectDictionaryKeyWithQuote_EscapesKey()
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object> { { "a\"b", 1 } };

            Assert.AreEqual("{\"a\\\"b\":1}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize writes a null dictionary value as the bare null literal.</summary>
        [Test]
        public void Serialize_ObjectDictionaryWithNullValue_WritesNullLiteral()
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object> { { "error", null } };

            Assert.AreEqual("{\"error\":null}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize recurses into a nested object dictionary.</summary>
        [Test]
        public void Serialize_NestedObjectDictionary_RecursesIntoChild()
        {
            Dictionary<string, object> child = new Dictionary<string, object> { { "depth", 2 } };
            Dictionary<string, object> parent = new Dictionary<string, object> { { "child", child } };

            Assert.AreEqual("{\"child\":{\"depth\":2}}", MiniJson.Serialize(parent));
        }

        /// <summary>Verifies that Serialize renders an empty float dictionary as an empty pair of braces.</summary>
        [Test]
        public void Serialize_EmptyFloatDictionary_ReturnsEmptyBraces()
        {
            Assert.AreEqual("{}", MiniJson.Serialize(new Dictionary<string, float>()));
        }

        /// <summary>Verifies that Serialize writes no separator for a single-entry float dictionary.</summary>
        [Test]
        public void Serialize_SingleEntryFloatDictionary_OmitsSeparator()
        {
            Dictionary<string, float> dictionary = new Dictionary<string, float> { { "a", 0.5f } };

            Assert.AreEqual("{\"a\":0.5}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize separates two float dictionary entries with a single comma.</summary>
        [Test]
        public void Serialize_TwoEntryFloatDictionary_SeparatesEntriesWithComma()
        {
            Dictionary<string, float> dictionary = new Dictionary<string, float> { { "a", 0.5f }, { "b", -1.25f } };

            Assert.AreEqual("{\"a\":0.5,\"b\":-1.25}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize escapes a float dictionary key.</summary>
        [Test]
        public void Serialize_FloatDictionaryKeyWithBackslash_EscapesKey()
        {
            Dictionary<string, float> dictionary = new Dictionary<string, float> { { "a\\b", 2.5f } };

            Assert.AreEqual("{\"a\\\\b\":2.5}", MiniJson.Serialize(dictionary));
        }

        /// <summary>Verifies that Serialize renders an empty object list as an empty pair of brackets.</summary>
        [Test]
        public void Serialize_EmptyObjectList_ReturnsEmptyBrackets()
        {
            Assert.AreEqual("[]", MiniJson.Serialize(new List<object>()));
        }

        /// <summary>Verifies that Serialize writes no separator for a single-element object list.</summary>
        [Test]
        public void Serialize_SingleElementObjectList_OmitsSeparator()
        {
            Assert.AreEqual("[1]", MiniJson.Serialize(new List<object> { 1 }));
        }

        /// <summary>Verifies that Serialize separates list elements with a comma and keeps their kinds.</summary>
        [Test]
        public void Serialize_MixedObjectList_SeparatesElementsWithComma()
        {
            List<object> list = new List<object> { 1, "two", 3.5d, true, null };

            Assert.AreEqual("[1,\"two\",3.5,true,null]", MiniJson.Serialize(list));
        }

        /// <summary>Verifies that Serialize renders a string list as an array of quoted elements.</summary>
        [Test]
        public void Serialize_StringList_ReturnsArrayOfQuotedElements()
        {
            Assert.AreEqual("[\"a\",\"b\"]", MiniJson.Serialize(new List<string> { "a", "b" }));
        }

        /// <summary>Verifies that Serialize renders a list of dictionaries as an array of objects.</summary>
        [Test]
        public void Serialize_DictionaryList_ReturnsArrayOfObjects()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "a", 1 } },
                new Dictionary<string, object>(),
            };

            Assert.AreEqual("[{\"a\":1},{}]", MiniJson.Serialize(list));
        }

        /// <summary>Verifies that Serialize renders an object array as a JSON array.</summary>
        [Test]
        public void Serialize_ObjectArray_ReturnsJsonArray()
        {
            Assert.AreEqual("[true,false]", MiniJson.Serialize(new object[] { true, false }));
        }

        /// <summary>Verifies that Serialize renders a list of a value type as a JSON array.</summary>
        [Test]
        public void Serialize_IntegerList_ReturnsJsonArray()
        {
            Assert.AreEqual("[1,2]", MiniJson.Serialize(new List<int> { 1, 2 }));
        }

        /// <summary>Verifies that Serialize renders an array of a value type as a JSON array.</summary>
        [Test]
        public void Serialize_FloatArray_ReturnsJsonArray()
        {
            Assert.AreEqual("[1.5,-2]", MiniJson.Serialize(new float[] { 1.5f, -2f }));
        }

        /// <summary>Verifies that Serialize renders a queue of a value type as a JSON array in queue order.</summary>
        [Test]
        public void Serialize_QueueOfLongs_ReturnsJsonArray()
        {
            Queue<long> queue = new Queue<long>();
            queue.Enqueue(7L);
            queue.Enqueue(-8L);

            Assert.AreEqual("[7,-8]", MiniJson.Serialize(queue));
        }

        /// <summary>Verifies that Serialize composes dictionaries and lists into one nested document.</summary>
        [Test]
        public void Serialize_NestedDocument_ComposesObjectsAndArrays()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "success", true },
                {
                    "items",
                    new List<object>
                    {
                        new Dictionary<string, object> { { "name", "A" } },
                        2,
                    }
                },
            };

            Assert.AreEqual("{\"success\":true,\"items\":[{\"name\":\"A\"},2]}", MiniJson.Serialize(payload));
        }

        /// <summary>Verifies that Serialize writes the invariant separator under a comma decimal culture.</summary>
        [Test]
        public void Serialize_UnderCommaDecimalCulture_WritesInvariantSeparator()
        {
            InstallCommaDecimalCulture();

            Assert.AreEqual("1.5", MiniJson.Serialize(1.5d));
            Assert.AreEqual("2.5", MiniJson.Serialize(2.5f));
            Assert.AreEqual("{\"a\":0.5}", MiniJson.Serialize(new Dictionary<string, float> { { "a", 0.5f } }));
        }

        /// <summary>Verifies that Deserialize rejects a null input and names the offending argument.</summary>
        [Test]
        public void Deserialize_NullInput_ThrowsArgumentNull()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => MiniJson.Deserialize(null));

            Assert.AreEqual("json", exception.ParamName);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for an empty input string.</summary>
        [Test]
        public void Deserialize_EmptyInput_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize(string.Empty).Count);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for whitespace-only input.</summary>
        [Test]
        public void Deserialize_WhitespaceOnlyInput_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize(" \t\r\n ").Count);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for a bare token.</summary>
        [Test]
        public void Deserialize_BareToken_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize("hello").Count);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for a top-level array.</summary>
        [Test]
        public void Deserialize_TopLevelArray_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize("[1,2,3]").Count);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for an empty JSON object.</summary>
        [Test]
        public void Deserialize_EmptyObject_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize("{}").Count);
        }

        /// <summary>Verifies that Deserialize tolerates whitespace inside an otherwise empty object.</summary>
        [Test]
        public void Deserialize_EmptyObjectWithInteriorWhitespace_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize("{ \t\r\n }").Count);
        }

        /// <summary>Verifies that Deserialize tolerates whitespace surrounding every token of an object.</summary>
        [Test]
        public void Deserialize_WhitespaceAroundEveryToken_ParsesEveryPair()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("\n\t { \"a\" : 1 , \"b\" : 2 } \r\n");

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(1L, parsed["a"]);
            Assert.AreEqual(2L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize ignores any text following the closing brace.</summary>
        [Test]
        public void Deserialize_TrailingGarbage_IgnoresTextAfterClosingBrace()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":1} then nonsense");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(1L, parsed["a"]);
        }

        /// <summary>Verifies that Deserialize returns an empty dictionary for a lone opening brace.</summary>
        [Test]
        public void Deserialize_OpeningBraceOnly_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, MiniJson.Deserialize("{").Count);
        }

        /// <summary>Verifies that Deserialize returns the parsed pairs of an unterminated object.</summary>
        [Test]
        public void Deserialize_UnterminatedObject_ReturnsParsedPairs()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":1,\"b\":2");

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(1L, parsed["a"]);
            Assert.AreEqual(2L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize returns the parsed pairs of an object ending on a comma.</summary>
        [Test]
        public void Deserialize_ObjectEndingOnComma_ReturnsParsedPairs()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":1,");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(1L, parsed["a"]);
        }

        /// <summary>Verifies that Deserialize rejects a trailing comma placed before the closing brace.</summary>
        [Test]
        public void Deserialize_TrailingCommaBeforeClosingBrace_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":1,}"));

            StringAssert.Contains("'' is not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize rejects a pair whose value is missing before the closing brace.</summary>
        [Test]
        public void Deserialize_MissingValueBeforeClosingBrace_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":}"));

            StringAssert.Contains("not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize yields a null value when the input ends right after the colon.</summary>
        [Test]
        public void Deserialize_InputEndingAfterColon_YieldsNullValue()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":");

            Assert.AreEqual(1, parsed.Count);
            Assert.IsTrue(parsed.ContainsKey("a"));
            Assert.IsNull(parsed["a"]);
        }

        /// <summary>Verifies that Deserialize parses the value of a pair whose colon is missing.</summary>
        [Test]
        public void Deserialize_MissingColon_StillParsesValue()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\" 1}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(1L, parsed["a"]);
        }

        /// <summary>Verifies that Deserialize rejects an unquoted key because the value starts on a letter.</summary>
        [Test]
        public void Deserialize_UnquotedKey_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{a:1}"));

            StringAssert.Contains("not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize keeps the last value written for a repeated key.</summary>
        [Test]
        public void Deserialize_RepeatedKey_KeepsLastValue()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":1,\"a\":2}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(2L, parsed["a"]);
        }

        /// <summary>Verifies that Deserialize yields an empty string for an empty JSON string value.</summary>
        [Test]
        public void Deserialize_EmptyStringValue_YieldsEmptyString()
        {
            Assert.AreEqual(string.Empty, MiniJson.Deserialize("{\"a\":\"\"}")["a"]);
        }

        /// <summary>Verifies that Deserialize yields the accumulated text of an unterminated string.</summary>
        [Test]
        public void Deserialize_UnterminatedString_YieldsAccumulatedText()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":\"unterminated");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("unterminated", parsed["a"]);
        }

        /// <summary>Verifies that Deserialize decodes every single-character escape sequence.</summary>
        [Test]
        public void Deserialize_ShortEscapeSequences_DecodesEveryForm()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\"}");

            Assert.AreEqual("\"\\/\b\f\n\r\t", parsed["a"]);
        }

        /// <summary>Verifies that Deserialize keeps the escaped character itself for an unknown escape.</summary>
        [Test]
        public void Deserialize_UnknownEscape_KeepsEscapedCharacter()
        {
            Assert.AreEqual("q", MiniJson.Deserialize("{\"a\":\"\\q\"}")["a"]);
        }

        /// <summary>Verifies that Deserialize decodes an uppercase-hexadecimal unicode escape.</summary>
        [Test]
        public void Deserialize_UppercaseHexUnicodeEscape_DecodesCodePoint()
        {
            Assert.AreEqual("A", MiniJson.Deserialize("{\"a\":\"\\u0041\"}")["a"]);
        }

        /// <summary>Verifies that Deserialize decodes a lowercase-hexadecimal unicode escape.</summary>
        [Test]
        public void Deserialize_LowercaseHexUnicodeEscape_DecodesCodePoint()
        {
            Assert.AreEqual("\u00E9", MiniJson.Deserialize("{\"a\":\"\\u00e9\"}")["a"]);
        }

        /// <summary>Verifies that Deserialize decodes a unicode escape surrounded by literal characters.</summary>
        [Test]
        public void Deserialize_UnicodeEscapeBetweenLiterals_ResumesAfterTheEscape()
        {
            Assert.AreEqual("xAy", MiniJson.Deserialize("{\"a\":\"x\\u0041y\"}")["a"]);
        }

        /// <summary>Verifies that Deserialize decodes a null character escape without terminating the string.</summary>
        [Test]
        public void Deserialize_NullCharacterUnicodeEscape_DecodesEmbeddedNull()
        {
            string text = (string)MiniJson.Deserialize("{\"a\":\"x\\u0000y\"}")["a"];

            Assert.AreEqual(3, text.Length);
            Assert.AreEqual('\0', text[1]);
        }

        /// <summary>Verifies that Deserialize decodes an escaped surrogate pair into an astral character.</summary>
        [Test]
        public void Deserialize_EscapedSurrogatePair_DecodesAstralCharacter()
        {
            string text = (string)MiniJson.Deserialize("{\"a\":\"\\uD83D\\uDE00\"}")["a"];

            Assert.AreEqual(2, text.Length);
            Assert.AreEqual(char.ConvertFromUtf32(0x1F600), text);
        }

        /// <summary>Verifies that Deserialize keeps the escape letter when fewer than four characters remain.</summary>
        [Test]
        public void Deserialize_TruncatedUnicodeEscape_KeepsEscapeLetterLiterally()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":\"\\u1\"}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("u1", parsed["a"]);
        }

        /// <summary>Verifies that Deserialize reads four characters past a unicode escape marker.</summary>
        [Test]
        public void Deserialize_UnicodeEscapeShorterThanFourDigits_ThrowsFormat()
        {
            Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":\"\\u12\"}"));
        }

        /// <summary>Verifies that Deserialize rejects a unicode escape whose digits are not hexadecimal.</summary>
        [Test]
        public void Deserialize_NonHexadecimalUnicodeEscape_ThrowsFormat()
        {
            Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":\"\\uZZZZ\"}"));
        }

        /// <summary>Verifies that Deserialize keeps a trailing backslash that has no character to escape.</summary>
        [Test]
        public void Deserialize_TrailingBackslashAtEndOfInput_KeepsBackslash()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":\"x\\");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("x\\", parsed["a"]);
        }

        /// <summary>Verifies that Deserialize decodes an escape sequence appearing inside a key.</summary>
        [Test]
        public void Deserialize_EscapeSequenceInsideKey_DecodesKey()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\\nb\":1}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(1L, parsed["a\nb"]);
        }

        /// <summary>Verifies that Deserialize preserves raw non-ASCII characters in keys and values.</summary>
        [Test]
        public void Deserialize_NonAsciiText_PreservesCharactersVerbatim()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"caf\u00E9\":\"\u65E5\u672C\u8A9E\"}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("\u65E5\u672C\u8A9E", parsed["caf\u00E9"]);
        }

        /// <summary>Verifies that Deserialize yields a long for an integer literal.</summary>
        [Test]
        public void Deserialize_IntegerLiteral_YieldsLong()
        {
            object value = MiniJson.Deserialize("{\"a\":42}")["a"];

            Assert.IsInstanceOf<long>(value);
            Assert.AreEqual(42L, value);
        }

        /// <summary>Verifies that Deserialize yields a negative long for a signed integer literal.</summary>
        [Test]
        public void Deserialize_NegativeIntegerLiteral_YieldsNegativeLong()
        {
            object value = MiniJson.Deserialize("{\"a\":-5}")["a"];

            Assert.IsInstanceOf<long>(value);
            Assert.AreEqual(-5L, value);
        }

        /// <summary>Verifies that Deserialize yields zero for a zero literal.</summary>
        [Test]
        public void Deserialize_ZeroLiteral_YieldsZeroLong()
        {
            Assert.AreEqual(0L, MiniJson.Deserialize("{\"a\":0}")["a"]);
        }

        /// <summary>Verifies that Deserialize parses the largest integer the long path accepts.</summary>
        [Test]
        public void Deserialize_LongMaxValueLiteral_YieldsLongMaxValue()
        {
            object value = MiniJson.Deserialize("{\"a\":9223372036854775807}")["a"];

            Assert.IsInstanceOf<long>(value);
            Assert.AreEqual(long.MaxValue, value);
        }

        /// <summary>Verifies that Deserialize rejects an integer literal one past the long range.</summary>
        [Test]
        public void Deserialize_IntegerLiteralAboveLongRange_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() =>
                MiniJson.Deserialize("{\"a\":9223372036854775808}")
            );

            StringAssert.Contains("9223372036854775808", exception.Message);
            StringAssert.Contains("not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize rejects a lone minus sign as an integer literal.</summary>
        [Test]
        public void Deserialize_LoneMinusSign_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":-}"));

            StringAssert.Contains("'-' is not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize rejects an integer literal carrying an interior sign.</summary>
        [Test]
        public void Deserialize_IntegerLiteralWithInteriorSign_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":5-5}"));

            StringAssert.Contains("'5-5' is not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize yields a double for a literal carrying a decimal point.</summary>
        [Test]
        public void Deserialize_DecimalPointLiteral_YieldsDouble()
        {
            object value = MiniJson.Deserialize("{\"a\":1.5}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(1.5d, value);
        }

        /// <summary>Verifies that Deserialize yields a negative double for a signed fractional literal.</summary>
        [Test]
        public void Deserialize_NegativeDecimalPointLiteral_YieldsNegativeDouble()
        {
            object value = MiniJson.Deserialize("{\"a\":-0.25}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(-0.25d, value);
        }

        /// <summary>Verifies that Deserialize yields a double for a lowercase exponent literal.</summary>
        [Test]
        public void Deserialize_LowercaseExponentLiteral_YieldsDouble()
        {
            object value = MiniJson.Deserialize("{\"a\":1e3}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(1000.0d, value);
        }

        /// <summary>Verifies that Deserialize yields a double for an uppercase exponent literal with a sign.</summary>
        [Test]
        public void Deserialize_UppercaseExponentLiteralWithPlusSign_YieldsDouble()
        {
            object value = MiniJson.Deserialize("{\"a\":2E+2}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(200.0d, value);
        }

        /// <summary>Verifies that Deserialize yields a double for a negative exponent literal.</summary>
        [Test]
        public void Deserialize_NegativeExponentLiteral_YieldsDouble()
        {
            object value = MiniJson.Deserialize("{\"a\":1.5e-2}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(1.5e-2d, value);
        }

        /// <summary>Verifies that Deserialize rejects a literal carrying two decimal points.</summary>
        [Test]
        public void Deserialize_LiteralWithTwoDecimalPoints_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":1.2.3}"));

            StringAssert.Contains("'1.2.3' is not a valid floating-point literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize rejects a bare exponent marker as a floating-point literal.</summary>
        [Test]
        public void Deserialize_BareExponentMarker_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":e}"));

            StringAssert.Contains("'e' is not a valid floating-point literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize stops a number at the first character outside the literal set.</summary>
        [Test]
        public void Deserialize_NumberFollowedByComma_StopsAtTheSeparator()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":12,\"b\":34}");

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(12L, parsed["a"]);
            Assert.AreEqual(34L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize parses an invariant decimal under a comma decimal culture.</summary>
        [Test]
        public void Deserialize_UnderCommaDecimalCulture_ParsesInvariantSeparator()
        {
            InstallCommaDecimalCulture();

            object value = MiniJson.Deserialize("{\"a\":1.5}")["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(1.5d, value);
        }

        /// <summary>Verifies that Deserialize yields a boolean and resumes parsing after a true literal.</summary>
        [Test]
        public void Deserialize_TrueLiteral_YieldsBooleanAndResumesAfterFourCharacters()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":true,\"b\":1}");

            Assert.AreEqual(2, parsed.Count);
            Assert.IsInstanceOf<bool>(parsed["a"]);
            Assert.AreEqual(true, parsed["a"]);
            Assert.AreEqual(1L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize yields a boolean and resumes parsing after a false literal.</summary>
        [Test]
        public void Deserialize_FalseLiteral_YieldsBooleanAndResumesAfterFiveCharacters()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":false,\"b\":1}");

            Assert.AreEqual(2, parsed.Count);
            Assert.IsInstanceOf<bool>(parsed["a"]);
            Assert.AreEqual(false, parsed["a"]);
            Assert.AreEqual(1L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize rejects a truncated boolean literal and reports its index.</summary>
        [Test]
        public void Deserialize_TruncatedBooleanLiteral_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":truX}"));

            StringAssert.Contains("expected 'true' or 'false' at index 5", exception.Message);
        }

        /// <summary>Verifies that Deserialize rejects a literal that only begins like false.</summary>
        [Test]
        public void Deserialize_TruncatedFalseLiteral_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"ab\":fals}"));

            StringAssert.Contains("expected 'true' or 'false' at index 6", exception.Message);
        }

        /// <summary>Verifies that Deserialize yields a null value and resumes parsing after a null literal.</summary>
        [Test]
        public void Deserialize_NullLiteral_YieldsNullValueAndResumesAfterFourCharacters()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":null,\"b\":1}");

            Assert.AreEqual(2, parsed.Count);
            Assert.IsTrue(parsed.ContainsKey("a"));
            Assert.IsNull(parsed["a"]);
            Assert.AreEqual(1L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize rejects a truncated null literal and reports its index.</summary>
        [Test]
        public void Deserialize_TruncatedNullLiteral_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":nul}"));

            StringAssert.Contains("expected 'null' at index 5", exception.Message);
        }

        /// <summary>Verifies that Deserialize parses a nested object into a nested dictionary.</summary>
        [Test]
        public void Deserialize_NestedObject_YieldsNestedDictionary()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"outer\":{\"inner\":\"value\"},\"tail\":1}");

            Assert.AreEqual(2, parsed.Count);
            Dictionary<string, object> outer = (Dictionary<string, object>)parsed["outer"];
            Assert.AreEqual(1, outer.Count);
            Assert.AreEqual("value", outer["inner"]);
            Assert.AreEqual(1L, parsed["tail"]);
        }

        /// <summary>Verifies that Deserialize parses an empty nested object into an empty dictionary.</summary>
        [Test]
        public void Deserialize_EmptyNestedObject_YieldsEmptyDictionary()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"outer\":{}}");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(0, ((Dictionary<string, object>)parsed["outer"]).Count);
        }

        /// <summary>Verifies that Deserialize parses an empty array into an empty list.</summary>
        [Test]
        public void Deserialize_EmptyArray_YieldsEmptyList()
        {
            Assert.AreEqual(0, ((List<object>)MiniJson.Deserialize("{\"a\":[]}")["a"]).Count);
        }

        /// <summary>Verifies that Deserialize tolerates whitespace inside an otherwise empty array.</summary>
        [Test]
        public void Deserialize_EmptyArrayWithInteriorWhitespace_YieldsEmptyList()
        {
            Assert.AreEqual(0, ((List<object>)MiniJson.Deserialize("{\"a\":[ \n ]}")["a"]).Count);
        }

        /// <summary>Verifies that Deserialize parses every element of a numeric array.</summary>
        [Test]
        public void Deserialize_NumericArray_YieldsLongsInOrder()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[1,2,3]}")["a"];

            Assert.AreEqual(3, values.Count);
            Assert.AreEqual(1L, values[0]);
            Assert.AreEqual(2L, values[1]);
            Assert.AreEqual(3L, values[2]);
        }

        /// <summary>Verifies that Deserialize tolerates whitespace around array elements and separators.</summary>
        [Test]
        public void Deserialize_ArrayWithWhitespace_YieldsElementsInOrder()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[ 1 , 2 ]}")["a"];

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(1L, values[0]);
            Assert.AreEqual(2L, values[1]);
        }

        /// <summary>Verifies that Deserialize preserves the runtime type of every element of a mixed array.</summary>
        [Test]
        public void Deserialize_MixedArray_PreservesElementTypes()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[1,1.5,\"x\",true,false,null]}")["a"];

            Assert.AreEqual(6, values.Count);
            Assert.IsInstanceOf<long>(values[0]);
            Assert.IsInstanceOf<double>(values[1]);
            Assert.IsInstanceOf<string>(values[2]);
            Assert.AreEqual(true, values[3]);
            Assert.AreEqual(false, values[4]);
            Assert.IsNull(values[5]);
        }

        /// <summary>Verifies that Deserialize parses an array of objects into a list of dictionaries.</summary>
        [Test]
        public void Deserialize_ArrayOfObjects_YieldsListOfDictionaries()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[{\"n\":1},{\"n\":2}]}")["a"];

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(1L, ((Dictionary<string, object>)values[0])["n"]);
            Assert.AreEqual(2L, ((Dictionary<string, object>)values[1])["n"]);
        }

        /// <summary>Verifies that Deserialize parses an array nested inside another array.</summary>
        [Test]
        public void Deserialize_NestedArray_YieldsNestedList()
        {
            List<object> outer = (List<object>)MiniJson.Deserialize("{\"a\":[[1,2],[]]}")["a"];

            Assert.AreEqual(2, outer.Count);
            Assert.AreEqual(2, ((List<object>)outer[0]).Count);
            Assert.AreEqual(2L, ((List<object>)outer[0])[1]);
            Assert.AreEqual(0, ((List<object>)outer[1]).Count);
        }

        /// <summary>Verifies that Deserialize returns an empty list for a lone opening bracket.</summary>
        [Test]
        public void Deserialize_OpeningBracketOnly_YieldsEmptyList()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":[");

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(0, ((List<object>)parsed["a"]).Count);
        }

        /// <summary>Verifies that Deserialize returns the parsed elements of an unterminated array.</summary>
        [Test]
        public void Deserialize_UnterminatedArray_YieldsParsedElements()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[1,2")["a"];

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(1L, values[0]);
            Assert.AreEqual(2L, values[1]);
        }

        /// <summary>Verifies that Deserialize returns the parsed elements of an array ending on a comma.</summary>
        [Test]
        public void Deserialize_ArrayEndingOnComma_YieldsParsedElements()
        {
            List<object> values = (List<object>)MiniJson.Deserialize("{\"a\":[1,")["a"];

            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(1L, values[0]);
        }

        /// <summary>Verifies that Deserialize rejects a trailing comma placed before the closing bracket.</summary>
        [Test]
        public void Deserialize_TrailingCommaBeforeClosingBracket_ThrowsFormat()
        {
            FormatException exception = Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\":[1,]}"));

            StringAssert.Contains("'' is not a valid integer literal", exception.Message);
        }

        /// <summary>Verifies that Deserialize resumes the enclosing object after the array closes.</summary>
        [Test]
        public void Deserialize_ArrayFollowedByAnotherPair_ParsesBothPairs()
        {
            Dictionary<string, object> parsed = MiniJson.Deserialize("{\"a\":[1],\"b\":2}");

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(1, ((List<object>)parsed["a"]).Count);
            Assert.AreEqual(2L, parsed["b"]);
        }

        /// <summary>Verifies that Deserialize descends through deeply nested objects.</summary>
        [Test]
        public void Deserialize_DeeplyNestedObjects_ParsesEveryLevel()
        {
            StringBuilder builder = new StringBuilder();
            for (int level = 0; level < NestingDepth; level++)
            {
                builder.Append("{\"a\":");
            }
            builder.Append("1");
            for (int level = 0; level < NestingDepth; level++)
            {
                builder.Append("}");
            }

            object current = MiniJson.Deserialize(builder.ToString());
            for (int level = 0; level < NestingDepth; level++)
            {
                current = ((Dictionary<string, object>)current)["a"];
            }

            Assert.AreEqual(1L, current);
        }

        /// <summary>Verifies that Deserialize descends through deeply nested arrays.</summary>
        [Test]
        public void Deserialize_DeeplyNestedArrays_ParsesEveryLevel()
        {
            StringBuilder builder = new StringBuilder("{\"a\":");
            for (int level = 0; level < NestingDepth; level++)
            {
                builder.Append("[");
            }
            builder.Append("1");
            for (int level = 0; level < NestingDepth; level++)
            {
                builder.Append("]");
            }
            builder.Append("}");

            object current = MiniJson.Deserialize(builder.ToString())["a"];
            for (int level = 0; level < NestingDepth; level++)
            {
                current = ((List<object>)current)[0];
            }

            Assert.AreEqual(1L, current);
        }

        /// <summary>Verifies that a serialized nested document parses back into equivalent values.</summary>
        [Test]
        public void RoundTrip_NestedDocument_PreservesEveryValue()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "success", true },
                { "name", "corridor" },
                { "count", 3L },
                { "ratio", 0.5d },
                { "missing", null },
                {
                    "items",
                    new List<object> { "a", 2L }
                },
                {
                    "nested",
                    new Dictionary<string, object> { { "flag", false } }
                },
            };

            Dictionary<string, object> parsed = MiniJson.Deserialize(MiniJson.Serialize(payload));

            Assert.AreEqual(7, parsed.Count);
            Assert.AreEqual(true, parsed["success"]);
            Assert.AreEqual("corridor", parsed["name"]);
            Assert.AreEqual(3L, parsed["count"]);
            Assert.AreEqual(0.5d, parsed["ratio"]);
            Assert.IsNull(parsed["missing"]);
            Assert.AreEqual("a", ((List<object>)parsed["items"])[0]);
            Assert.AreEqual(2L, ((List<object>)parsed["items"])[1]);
            Assert.AreEqual(false, ((Dictionary<string, object>)parsed["nested"])["flag"]);
        }

        /// <summary>Verifies that a serialized string carrying escapes parses back into the original text.</summary>
        [Test]
        public void RoundTrip_StringWithEscapes_PreservesText()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "a\"b", "line\none\\two\ttab\rreturn/slash" },
            };

            Dictionary<string, object> parsed = MiniJson.Deserialize(MiniJson.Serialize(payload));

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("line\none\\two\ttab\rreturn/slash", parsed["a\"b"]);
        }

        /// <summary>Verifies that a serialized control character string parses back into the original text.</summary>
        [Test]
        public void RoundTrip_EveryControlCharacter_PreservesText()
        {
            string text = BuildControlCharacterText();
            Dictionary<string, object> payload = new Dictionary<string, object> { { "a", text } };

            Dictionary<string, object> parsed = MiniJson.Deserialize(MiniJson.Serialize(payload));

            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual(text, parsed["a"]);
        }

        /// <summary>Verifies that a serialized value-type sequence parses back into a list of equal values.</summary>
        [Test]
        public void RoundTrip_IntegerList_PreservesEveryElement()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                {
                    "a",
                    new List<int> { 1, 2 }
                },
            };

            List<object> values = (List<object>)MiniJson.Deserialize(MiniJson.Serialize(payload))["a"];

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(1L, values[0]);
            Assert.AreEqual(2L, values[1]);
        }

        /// <summary>Verifies that a serialized 32-bit integer parses back as a 64-bit integer.</summary>
        [Test]
        public void RoundTrip_IntegerValue_WidensToLong()
        {
            Dictionary<string, object> payload = new Dictionary<string, object> { { "a", 42 } };

            object value = MiniJson.Deserialize(MiniJson.Serialize(payload))["a"];

            Assert.IsInstanceOf<long>(value);
            Assert.AreEqual(42L, value);
        }

        /// <summary>Verifies that a serialized float parses back as a double.</summary>
        [Test]
        public void RoundTrip_FloatValue_WidensToDouble()
        {
            Dictionary<string, object> payload = new Dictionary<string, object> { { "a", 2.5f } };

            object value = MiniJson.Deserialize(MiniJson.Serialize(payload))["a"];

            Assert.IsInstanceOf<double>(value);
            Assert.AreEqual(2.5d, value);
        }

        /// <summary>Verifies that a serialized whole-valued double parses back as a long.</summary>
        [Test]
        public void RoundTrip_WholeValuedDouble_NarrowsToLong()
        {
            Dictionary<string, object> payload = new Dictionary<string, object> { { "a", 2.0d } };

            object value = MiniJson.Deserialize(MiniJson.Serialize(payload))["a"];

            Assert.IsInstanceOf<long>(value);
            Assert.AreEqual(2L, value);
        }

        /// <summary>Verifies that a serialized empty object and empty array parse back into empty containers.</summary>
        [Test]
        public void RoundTrip_EmptyContainers_PreservesEmptiness()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "object", new Dictionary<string, object>() },
                { "array", new List<object>() },
            };

            Dictionary<string, object> parsed = MiniJson.Deserialize(MiniJson.Serialize(payload));

            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual(0, ((Dictionary<string, object>)parsed["object"]).Count);
            Assert.AreEqual(0, ((List<object>)parsed["array"]).Count);
        }

        /// <summary>Builds a string holding every control character JSON forbids raw, in code point order.</summary>
        /// <returns>The text the control character tests serialize.</returns>
        private static string BuildControlCharacterText()
        {
            StringBuilder builder = new StringBuilder(ControlCharacterCount);
            for (int codePoint = 0; codePoint < ControlCharacterCount; codePoint++)
            {
                builder.Append((char)codePoint);
            }

            return builder.ToString();
        }

        /// <summary>Installs a culture whose decimal separator is a comma rather than a full stop.</summary>
        private static void InstallCommaDecimalCulture()
        {
            CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            culture.NumberFormat.NumberGroupSeparator = ".";
            CultureInfo.CurrentCulture = culture;
        }

        /// <summary>A value of a type MiniJson does not recognize, whose text form needs escaping.</summary>
        private sealed class UnrecognizedValue
        {
            /// <summary>Returns a text form carrying a quote and a backslash.</summary>
            /// <returns>The text form the serializer falls back to.</returns>
            public override string ToString()
            {
                return "raw\"text\\here";
            }
        }
    }
}
