/// <summary>
/// Verifies the Play Mode branches of the McpBridge play-state tools.
/// </summary>
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.PlayMode
{
    /// <summary>Verifies the Play Mode branches of the McpBridge play-state tools.</summary>
    /// <remarks>
    /// The Edit Mode bridge fixture leaves enter_play_mode undispatched, because dispatching it there would strand the
    /// Editor in Play Mode for the remainder of the run. Running inside Play Mode reaches the already-playing branch
    /// instead, which answers without calling EditorApplication.EnterPlaymode, so the tool name and that branch are
    /// both covered without a transition to undo.
    /// </remarks>
    [TestFixture]
    public class McpBridgePlayModeTests
    {
        /// <summary>The assembly-qualified name of the bridge type the editor assembly declares.</summary>
        /// <remarks>
        /// The bridge lives in an editor assembly outside this assembly's references, so the fixture resolves the type
        /// by name and reaches its dispatcher through reflection.
        /// </remarks>
        private const string BridgeTypeName = "SL.Tasks.McpBridge, Sollertia.InfiniteCorridorTask.Editor";

        /// <summary>The resolved bridge type, looked up once for the fixture.</summary>
        private Type _bridgeType;

        /// <summary>Resolves the bridge type from the editor assembly.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _bridgeType = Type.GetType(BridgeTypeName);
            Assert.IsNotNull(_bridgeType, $"Unable to resolve the bridge type '{BridgeTypeName}'.");
        }

        /// <summary>
        /// Verifies that dispatching enter_play_mode while playing reports the already-playing state.
        /// </summary>
        [UnityTest]
        public IEnumerator Dispatch_EnterPlayModeWhileAlreadyPlaying_ReportsPlayingAndStaysInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "The fixture must run under the player loop.");
            yield return null;

            string json = Dispatch("enter_play_mode");

            StringAssert.Contains("\"success\":true", json);
            StringAssert.Contains("Already in Play Mode.", json);
            StringAssert.Contains("\"state\":\"playing\"", json);

            yield return null;
            Assert.IsTrue(Application.isPlaying, "The dispatch must leave the player loop running.");
        }

        /// <summary>Verifies that the play-state tool reports the playing state while under the player loop.</summary>
        [UnityTest]
        public IEnumerator Dispatch_GetPlayStateWhilePlaying_ReportsThePlayingState()
        {
            yield return null;

            string json = Dispatch("get_play_state");

            StringAssert.Contains("\"success\":true", json);
            StringAssert.Contains("\"state\":\"playing\"", json);
        }

        /// <summary>Verifies that an unknown tool name resolves to the dispatch fallback while playing.</summary>
        [UnityTest]
        public IEnumerator Dispatch_UnknownToolWhilePlaying_ReportsTheUnknownToolError()
        {
            yield return null;

            string json = Dispatch("not_a_tool");

            StringAssert.Contains("\"success\":false", json);
            StringAssert.Contains("Unknown tool: not_a_tool", json);
        }

        /// <summary>Dispatches a tool that carries no arguments through the bridge's private entry point.</summary>
        /// <param name="tool">The tool name handed to the dispatcher.</param>
        /// <returns>The raw JSON response envelope.</returns>
        private string Dispatch(string tool)
        {
            return (string)PrivateAccess.InvokeStatic(_bridgeType, "Dispatch", tool, new Dictionary<string, object>());
        }
    }
}
