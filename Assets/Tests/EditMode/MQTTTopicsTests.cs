/// <summary>
/// Verifies the behavior of the MQTTTopics class.
/// </summary>
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the MQTTTopics class.</summary>
    /// <remarks>
    /// The literals form the wire contract shared with sollertia-experiment, so each expected value is written
    /// out explicitly here instead of being read back from the constant it pins. Reading the constant would make
    /// the assertion tautological and would let a rename travel silently into the other repository. Reflection
    /// is used only for the structural checks, which must observe the declared set as a whole.
    /// </remarks>
    [TestFixture]
    public class MQTTTopicsTests
    {
        /// <summary>The number of topic constants the experiment-side contract expects the catalog to declare.
        /// </summary>
        private const int ExpectedTopicCount = 12;

        /// <summary>The characters MQTT reserves for topic hierarchy and wildcard matching.</summary>
        private static readonly char[] ForbiddenSeparators = { '/', '#', '+' };

        /// <summary>The complete set of topic literals the experiment-side contract expects.</summary>
        private static readonly string[] ExpectedTopics =
        {
            "SessionStart",
            "SessionStop",
            "Motion",
            "Interaction",
            "Stimulus",
            "Delay",
            "CueSequenceTrigger",
            "CueSequence",
            "SceneNameTrigger",
            "SceneName",
            "RequireInteraction",
            "RequireWait",
        };

        /// <summary>Verifies that the session-start topic matches the experiment-side contract literal.</summary>
        [Test]
        public void SessionStart_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("SessionStart", MQTTTopics.SessionStart);
        }

        /// <summary>Verifies that the session-stop topic matches the experiment-side contract literal.</summary>
        [Test]
        public void SessionStop_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("SessionStop", MQTTTopics.SessionStop);
        }

        /// <summary>Verifies that the treadmill motion topic matches the experiment-side contract literal.</summary>
        [Test]
        public void Motion_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("Motion", MQTTTopics.Motion);
        }

        /// <summary>Verifies that the sensor interaction topic matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void Interaction_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("Interaction", MQTTTopics.Interaction);
        }

        /// <summary>Verifies that the trial outcome topic matches the experiment-side contract literal.</summary>
        [Test]
        public void Stimulus_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("Stimulus", MQTTTopics.Stimulus);
        }

        /// <summary>Verifies that the brake activation topic matches the experiment-side contract literal.</summary>
        [Test]
        public void Delay_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("Delay", MQTTTopics.Delay);
        }

        /// <summary>Verifies that the cue sequence request topic matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void CueSequenceTrigger_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("CueSequenceTrigger", MQTTTopics.CueSequenceTrigger);
        }

        /// <summary>Verifies that the cue sequence reply topic matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void CueSequence_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("CueSequence", MQTTTopics.CueSequence);
        }

        /// <summary>Verifies that the scene name request topic matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void SceneNameTrigger_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("SceneNameTrigger", MQTTTopics.SceneNameTrigger);
        }

        /// <summary>Verifies that the scene name reply topic matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void SceneName_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("SceneName", MQTTTopics.SceneName);
        }

        /// <summary>Verifies that the interaction requirement toggle matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void RequireInteraction_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("RequireInteraction", MQTTTopics.RequireInteraction);
        }

        /// <summary>Verifies that the wait requirement toggle matches the experiment-side contract literal.
        /// </summary>
        [Test]
        public void RequireWait_Constant_EqualsTheContractLiteral()
        {
            Assert.AreEqual("RequireWait", MQTTTopics.RequireWait);
        }

        /// <summary>Verifies that the catalog declares exactly the number of topics the contract expects.</summary>
        /// <remarks>
        /// The reflection call is written out here rather than routed through <see cref="DeclaredTopicFields"/> so this
        /// test owns the count contract outright and reports it under its own name. That leaves the helper's guard to
        /// serve only the structural tests that would otherwise loop over nothing.
        /// </remarks>
        [Test]
        public void DeclaredTopics_FieldCount_MatchesTheContractTopicCount()
        {
            FieldInfo[] fields = typeof(MQTTTopics).GetFields(BindingFlags.Public | BindingFlags.Static);

            Assert.AreEqual(ExpectedTopicCount, fields.Length);
        }

        /// <summary>Verifies that the declared literals are exactly the contract topic set.</summary>
        [Test]
        public void DeclaredTopics_LiteralSet_MatchesTheContractTopicSet()
        {
            CollectionAssert.AreEquivalent(ExpectedTopics, DeclaredTopicValues());
        }

        /// <summary>Verifies that no declared topic carries an MQTT hierarchy or wildcard character.</summary>
        [Test]
        public void DeclaredTopics_EveryLiteral_CarriesNoHierarchicalSeparator()
        {
            foreach (string topic in DeclaredTopicValues())
            {
                Assert.AreEqual(
                    -1,
                    topic.IndexOfAny(ForbiddenSeparators),
                    $"Topic '{topic}' must be flat, but it carries an MQTT hierarchy or wildcard character."
                );
            }
        }

        /// <summary>Verifies that no declared topic carries a whitespace character.</summary>
        [Test]
        public void DeclaredTopics_EveryLiteral_CarriesNoWhitespace()
        {
            foreach (string topic in DeclaredTopicValues())
            {
                foreach (char character in topic)
                {
                    Assert.IsFalse(
                        char.IsWhiteSpace(character),
                        $"Topic '{topic}' must carry no whitespace, but it carries at least one whitespace character."
                    );
                }
            }
        }

        /// <summary>Verifies that no declared topic is null or empty.</summary>
        [Test]
        public void DeclaredTopics_EveryLiteral_IsNonEmpty()
        {
            foreach (FieldInfo field in DeclaredTopicFields())
            {
                string topic = (string)field.GetValue(null);
                Assert.IsFalse(
                    string.IsNullOrEmpty(topic),
                    $"Topic constant '{field.Name}' must carry a non-empty literal, but it carries none."
                );
            }
        }

        /// <summary>Verifies that every declared topic literal is unique within the catalog.</summary>
        [Test]
        public void DeclaredTopics_EveryLiteral_IsDistinct()
        {
            List<string> topics = DeclaredTopicValues();
            HashSet<string> distinctTopics = new HashSet<string>(topics, StringComparer.Ordinal);

            Assert.AreEqual(topics.Count, distinctTopics.Count);
        }

        /// <summary>Verifies that every declared topic is a compile-time string constant.</summary>
        [Test]
        public void DeclaredTopics_EveryField_IsACompileTimeStringConstant()
        {
            foreach (FieldInfo field in DeclaredTopicFields())
            {
                Assert.AreEqual(
                    typeof(string),
                    field.FieldType,
                    $"Topic constant '{field.Name}' must be declared as a string."
                );
                Assert.IsTrue(field.IsLiteral, $"Topic constant '{field.Name}' must be declared const.");
                Assert.IsFalse(field.IsInitOnly, $"Topic constant '{field.Name}' must not be static readonly.");
            }
        }

        /// <summary>Verifies that every topic constant's name equals the literal it declares.</summary>
        [Test]
        public void DeclaredTopics_EveryFieldName_EqualsItsLiteralValue()
        {
            foreach (FieldInfo field in DeclaredTopicFields())
            {
                Assert.AreEqual(field.Name, (string)field.GetValue(null));
            }
        }

        /// <summary>Verifies that every declared topic literal is a single PascalCase identifier.</summary>
        [Test]
        public void DeclaredTopics_EveryLiteral_IsASinglePascalCaseIdentifier()
        {
            Regex identifierPattern = new Regex("^[A-Z][A-Za-z0-9]*$");
            foreach (string topic in DeclaredTopicValues())
            {
                Assert.IsTrue(
                    identifierPattern.IsMatch(topic),
                    $"Topic '{topic}' must be a single PascalCase identifier, but it is not."
                );
            }
        }

        /// <summary>Verifies that each request topic is its reply topic extended with the Trigger suffix.</summary>
        [Test]
        public void DeclaredTopics_RequestReplyPairs_ExtendTheReplyLiteralWithTheTriggerSuffix()
        {
            Assert.AreEqual($"{MQTTTopics.CueSequence}Trigger", MQTTTopics.CueSequenceTrigger);
            Assert.AreEqual($"{MQTTTopics.SceneName}Trigger", MQTTTopics.SceneNameTrigger);
        }

        /// <summary>Verifies that the catalog is a public static class rather than an instantiable type.</summary>
        [Test]
        public void MQTTTopics_Type_IsAPublicStaticCatalog()
        {
            Type catalogType = typeof(MQTTTopics);

            Assert.IsTrue(catalogType.IsPublic, "The topic catalog must be public.");
            Assert.IsTrue(catalogType.IsAbstract, "The topic catalog must be a static class.");
            Assert.IsTrue(catalogType.IsSealed, "The topic catalog must be a static class.");
        }

        /// <summary>Verifies that the catalog declares no instance state alongside its topic constants.</summary>
        [Test]
        public void MQTTTopics_Type_DeclaresNoInstanceFields()
        {
            FieldInfo[] instanceFields = typeof(MQTTTopics).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            Assert.AreEqual(0, instanceFields.Length);
        }

        /// <summary>Returns every public static field declared by the topic catalog.</summary>
        /// <remarks>
        /// The reflected set is size-checked here rather than in the callers because every structural test below walks
        /// it with a foreach. An empty or truncated set would make each of those loops pass vacuously. The guard
        /// therefore turns a reflection lookup that stopped seeing the catalog into a failure in every test that relies
        /// on it.
        /// </remarks>
        /// <returns>The declared topic fields.</returns>
        private static FieldInfo[] DeclaredTopicFields()
        {
            FieldInfo[] fields = typeof(MQTTTopics).GetFields(BindingFlags.Public | BindingFlags.Static);
            Assert.AreEqual(
                ExpectedTopicCount,
                fields.Length,
                "The reflected field set must cover the whole catalog, otherwise every per-topic loop passes "
                    + "without inspecting anything."
            );
            return fields;
        }

        /// <summary>Returns the value of every topic field declared by the catalog.</summary>
        /// <remarks>
        /// The value is read with <c>GetValue</c> rather than <c>GetRawConstantValue</c> so a field that stopped
        /// being a compile-time constant still yields a value here and fails the dedicated constness test with a
        /// readable message instead of throwing out of this helper.
        /// </remarks>
        /// <returns>The declared topic values.</returns>
        private static List<string> DeclaredTopicValues()
        {
            List<string> values = new List<string>();
            foreach (FieldInfo field in DeclaredTopicFields())
            {
                values.Add((string)field.GetValue(null));
            }
            return values;
        }
    }
}
