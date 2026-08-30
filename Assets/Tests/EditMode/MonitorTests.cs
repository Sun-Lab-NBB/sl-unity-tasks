/// <summary>Verifies the behavior of the Monitor class.</summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the Monitor class.</summary>
    /// <remarks>
    /// EnumerateMonitors itself opens a popup EditorWindow per detected monitor, P/Invokes user32 on Windows, and
    /// shells out to xrandr or displayplacer on Linux and macOS. The fixture therefore drives the parsing helpers, the
    /// subprocess helper, and the record constructor directly rather than the public entry point.
    /// </remarks>
    [TestFixture]
    public class MonitorTests
    {
        /// <summary>The executable the subprocess tests use to emit a canned enumeration output.</summary>
        private const string EchoExecutablePath = "/bin/echo";

        /// <summary>The command path the failure-to-start test hands to the subprocess helper.</summary>
        private const string MissingExecutablePath = "/nonexistent/sollertia-monitor-enumeration-probe";

        /// <summary>Verifies that the Linux pattern extracts every connected monitor from xrandr output.</summary>
        [Test]
        public void LinuxMonitorRegex_XrandrOutput_ExtractsEveryConnectedMonitor()
        {
            string output = JoinLines(
                "Screen 0: minimum 320 x 200, current 3840 x 1080, maximum 16384 x 16384",
                "DP-1 connected primary 1920x1080+0+0 (normal left inverted right) 527mm x 296mm",
                "HDMI-1 connected 1280x720+1920+360 (normal left inverted right) 340mm x 190mm",
                "DP-2 disconnected (normal left inverted right)"
            );

            MatchCollection matches = LinuxPattern().Matches(output);

            Assert.AreEqual(2, matches.Count);
            AssertGeometry(matches[0], width: 1920, height: 1080, left: 0, top: 0);
            AssertGeometry(matches[1], width: 1280, height: 720, left: 1920, top: 360);
        }

        /// <summary>Verifies that the Linux pattern ignores the mode list rows xrandr prints per output.</summary>
        [Test]
        public void LinuxMonitorRegex_ModeListRows_MatchNothing()
        {
            string output = JoinLines("   1920x1080     60.00*+  59.94    50.00", "   1280x720      60.00    59.94");

            MatchCollection matches = LinuxPattern().Matches(output);

            Assert.AreEqual(0, matches.Count);
        }

        /// <summary>Verifies that the Linux pattern finds nothing in empty command output.</summary>
        [Test]
        public void LinuxMonitorRegex_EmptyOutput_MatchesNothing()
        {
            MatchCollection matches = LinuxPattern().Matches(string.Empty);

            Assert.AreEqual(0, matches.Count);
        }

        /// <summary>Verifies that the Linux pattern keeps a monitor whose offsets carry a minus sign.</summary>
        [Test]
        public void LinuxMonitorRegex_NegativeOffsets_ExtractsTheSignedOrigin()
        {
            string output = JoinLines(
                "DP-1 connected 1920x1080+-1920+0 (normal left inverted right) 527mm x 296mm",
                "HDMI-1 connected primary 2560x1440+0+-360 (normal left inverted right) 597mm x 336mm"
            );

            MatchCollection matches = LinuxPattern().Matches(output);

            Assert.AreEqual(2, matches.Count);
            AssertGeometry(matches[0], width: 1920, height: 1080, left: -1920, top: 0);
            AssertGeometry(matches[1], width: 2560, height: 1440, left: 0, top: -360);
        }

        /// <summary>Verifies that the macOS pattern pairs each resolution with the origin of its own block.</summary>
        [Test]
        public void MacOsMonitorRegex_DisplayPlacerOutput_PairsResolutionWithBlockOrigin()
        {
            string output = JoinLines(
                "Persistent screen id: s4239",
                "Type: 27 inch external screen",
                "Resolution: 2560x1440",
                "Hertz: 60",
                "Color Depth: 8",
                "Scaling: off",
                "Origin: (0,0) - main display",
                "Rotation: 0",
                string.Empty,
                "Persistent screen id: s7712",
                "Type: MacBook built in screen",
                "Resolution: 1512x982",
                "Hertz: 120",
                "Origin: (-1512,-360)",
                "Rotation: 0"
            );

            MatchCollection matches = MacOsPattern().Matches(output);

            Assert.AreEqual(2, matches.Count);
            AssertGeometry(matches[0], width: 2560, height: 1440, left: 0, top: 0);
            AssertGeometry(matches[1], width: 1512, height: 982, left: -1512, top: -360);
        }

        /// <summary>Verifies that a block missing its origin cannot borrow the origin of the next block.</summary>
        [Test]
        public void MacOsMonitorRegex_BlockWithoutOrigin_DoesNotBorrowTheNextBlockOrigin()
        {
            string output = JoinLines(
                "Resolution: 800x600",
                "Hertz: 60",
                string.Empty,
                "Resolution: 1920x1080",
                "Origin: (10,20)",
                "Rotation: 0"
            );

            MatchCollection matches = MacOsPattern().Matches(output);

            Assert.AreEqual(1, matches.Count);
            AssertGeometry(matches[0], width: 1920, height: 1080, left: 10, top: 20);
        }

        /// <summary>Verifies that the macOS pattern tolerates carriage return line endings.</summary>
        [Test]
        public void MacOsMonitorRegex_CarriageReturnLineEndings_StillMatches()
        {
            string output = string.Join(
                "\r\n",
                new string[] { "Resolution: 3840x2160", "Hertz: 60", "Origin: (0,0) - main display" }
            );

            MatchCollection matches = MacOsPattern().Matches(output);

            Assert.AreEqual(1, matches.Count);
            AssertGeometry(matches[0], width: 3840, height: 2160, left: 0, top: 0);
        }

        /// <summary>Verifies that the macOS pattern rejects an origin whose coordinates are not numeric.</summary>
        [Test]
        public void MacOsMonitorRegex_MalformedOrigin_MatchesNothing()
        {
            string output = JoinLines("Resolution: 1920x1080", "Hertz: 60", "Origin: (left,top)");

            MatchCollection matches = MacOsPattern().Matches(output);

            Assert.AreEqual(0, matches.Count);
        }

        /// <summary>Verifies that the macOS pattern finds nothing in empty command output.</summary>
        [Test]
        public void MacOsMonitorRegex_EmptyOutput_MatchesNothing()
        {
            MatchCollection matches = MacOsPattern().Matches(string.Empty);

            Assert.AreEqual(0, matches.Count);
        }

        /// <summary>Verifies that a new monitor record stores its geometry and the unassigned defaults.</summary>
        [Test]
        public void Constructor_NewMonitor_StoresGeometryAndUnassignedDefaults()
        {
            Monitor monitor = CreateMonitor(left: -1920, top: -360, width: 1280, height: 720);

            Assert.AreEqual(-1920, monitor.left);
            Assert.AreEqual(-360, monitor.top);
            Assert.AreEqual(1280, monitor.width);
            Assert.AreEqual(720, monitor.height);
            Assert.AreEqual(1.0f, monitor.pixelsPerPoint, 1e-6f);
            Assert.AreEqual(EntityId.None, monitor.cameraEntityId);
        }

        /// <summary>
        /// Verifies that the displayplacer constants match the two supported Homebrew prefixes and the bare
        /// PATH-resolved executable name.
        /// </summary>
        [Test]
        public void DisplayPlacerConstants_InstallPaths_MatchTheHomebrewPrefixes()
        {
            Assert.AreEqual(
                "/opt/homebrew/bin/displayplacer",
                PrivateAccess.GetStaticField<string>(typeof(Monitor), "AppleSiliconDisplayPlacerPath")
            );
            Assert.AreEqual(
                "/usr/local/bin/displayplacer",
                PrivateAccess.GetStaticField<string>(typeof(Monitor), "IntelDisplayPlacerPath")
            );
            Assert.AreEqual(
                "displayplacer",
                PrivateAccess.GetStaticField<string>(typeof(Monitor), "DisplayPlacerExecutableName")
            );
        }

        /// <summary>Verifies that the subprocess budget stays at five seconds per wait.</summary>
        [Test]
        public void SubprocessTimeoutMilliseconds_Constant_IsFiveSeconds()
        {
            Assert.AreEqual(5000, PrivateAccess.GetStaticField<int>(typeof(Monitor), "SubprocessTimeoutMilliseconds"));
        }

        /// <summary>
        /// Verifies that the displayplacer path resolves to the first Homebrew prefix that exists, falling back to
        /// the bare executable name.
        /// </summary>
        [Test]
        public void ResolveDisplayPlacerPath_HomebrewPrefixes_ReturnsTheFirstExistingPath()
        {
            string resolved = (string)PrivateAccess.InvokeStatic(typeof(Monitor), "ResolveDisplayPlacerPath");

            if (File.Exists("/opt/homebrew/bin/displayplacer"))
            {
                Assert.AreEqual("/opt/homebrew/bin/displayplacer", resolved);
            }
            else if (File.Exists("/usr/local/bin/displayplacer"))
            {
                Assert.AreEqual("/usr/local/bin/displayplacer", resolved);
            }
            else
            {
                Assert.AreEqual("displayplacer", resolved);
            }
        }

        /// <summary>Verifies that an unstartable command warns and leaves the monitor list untouched.</summary>
        [Test]
        public void EnumerateViaSubprocess_UnstartableCommand_WarnsAndAddsNoMonitors()
        {
            List<Monitor> result = new List<Monitor>();
            LogAssert.Expect(LogType.Warning, new Regex(".*Monitor enumeration: failed to start.*"));

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                MissingExecutablePath,
                string.Empty,
                LinuxPattern(),
                result
            );

            Assert.AreEqual(0, result.Count);
        }

        /// <summary>Verifies that every parsed match becomes a monitor record carrying the parsed geometry.</summary>
        [Test]
        public void EnumerateViaSubprocess_MatchingOutput_AddsOneMonitorPerMatch()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                "1920x1080+0+0 1280x720+1920+360",
                LinuxPattern(),
                result
            );

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1920, result[0].width);
            Assert.AreEqual(1080, result[0].height);
            Assert.AreEqual(0, result[0].left);
            Assert.AreEqual(0, result[0].top);
            Assert.AreEqual(1280, result[1].width);
            Assert.AreEqual(720, result[1].height);
            Assert.AreEqual(1920, result[1].left);
            Assert.AreEqual(360, result[1].top);
        }

        /// <summary>Verifies that each parsed monitor starts unscaled and unassigned.</summary>
        [Test]
        public void EnumerateViaSubprocess_MatchingOutput_LeavesEachMonitorUnassigned()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                "1920x1080+0+0",
                LinuxPattern(),
                result
            );

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1.0f, result[0].pixelsPerPoint, 1e-6f);
            Assert.AreEqual(EntityId.None, result[0].cameraEntityId);
        }

        /// <summary>Verifies that a monitor placed left of and above the origin keeps its signed offsets.</summary>
        [Test]
        public void EnumerateViaSubprocess_NegativeOffsets_RecordsTheSignedOrigin()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                "1920x1080+-1920+-360",
                LinuxPattern(),
                result
            );

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1920, result[0].width);
            Assert.AreEqual(1080, result[0].height);
            Assert.AreEqual(-1920, result[0].left);
            Assert.AreEqual(-360, result[0].top);
        }

        /// <summary>Verifies that a dimension too large for a 32-bit integer drops only that match.</summary>
        [Test]
        public void EnumerateViaSubprocess_DimensionOutsideIntegerRange_DropsOnlyThatMatch()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                "99999999999x1080+0+0 1280x720+1920+360",
                LinuxPattern(),
                result
            );

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1280, result[0].width);
            Assert.AreEqual(720, result[0].height);
            Assert.AreEqual(1920, result[0].left);
            Assert.AreEqual(360, result[0].top);
        }

        /// <summary>Verifies that a pattern exposing fewer than four groups contributes no monitors.</summary>
        [Test]
        public void EnumerateViaSubprocess_PatternWithoutFourGroups_AddsNoMonitors()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                "1920x1080+0+0",
                new Regex(@"(\d+)x(\d+)"),
                result
            );

            Assert.AreEqual(0, result.Count);
        }

        /// <summary>Verifies that empty command output contributes no monitors.</summary>
        [Test]
        public void EnumerateViaSubprocess_EmptyOutput_AddsNoMonitors()
        {
            RequireEchoExecutable();
            List<Monitor> result = new List<Monitor>();

            PrivateAccess.InvokeStatic(
                typeof(Monitor),
                "EnumerateViaSubprocess",
                EchoExecutablePath,
                string.Empty,
                LinuxPattern(),
                result
            );

            Assert.AreEqual(0, result.Count);
        }

        /// <summary>Returns the pre-compiled xrandr pattern that the Linux enumeration path uses.</summary>
        /// <returns>The Linux monitor pattern.</returns>
        private static Regex LinuxPattern()
        {
            return PrivateAccess.GetStaticField<Regex>(typeof(Monitor), "LinuxMonitorRegex");
        }

        /// <summary>Returns the pre-compiled displayplacer pattern that the macOS enumeration path uses.</summary>
        /// <returns>The macOS monitor pattern.</returns>
        private static Regex MacOsPattern()
        {
            return PrivateAccess.GetStaticField<Regex>(typeof(Monitor), "MacOsMonitorRegex");
        }

        /// <summary>Builds a monitor record through the private constructor the enumeration path uses.</summary>
        /// <param name="left">The left position in pixels.</param>
        /// <param name="top">The top position in pixels.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <returns>The constructed monitor record.</returns>
        private static Monitor CreateMonitor(int left, int top, int width, int height)
        {
            return (Monitor)
                Activator.CreateInstance(
                    typeof(Monitor),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { left, top, width, height },
                    culture: null
                );
        }

        /// <summary>Asserts that a match exposes the four geometry groups the enumeration path reads.</summary>
        /// <param name="match">The match produced by one of the platform patterns.</param>
        /// <param name="width">The expected width in pixels.</param>
        /// <param name="height">The expected height in pixels.</param>
        /// <param name="left">The expected left position in pixels.</param>
        /// <param name="top">The expected top position in pixels.</param>
        private static void AssertGeometry(Match match, int width, int height, int left, int top)
        {
            Assert.AreEqual(5, match.Groups.Count);
            Assert.AreEqual(width.ToString(CultureInfo.InvariantCulture), match.Groups[1].Value);
            Assert.AreEqual(height.ToString(CultureInfo.InvariantCulture), match.Groups[2].Value);
            Assert.AreEqual(left.ToString(CultureInfo.InvariantCulture), match.Groups[3].Value);
            Assert.AreEqual(top.ToString(CultureInfo.InvariantCulture), match.Groups[4].Value);
        }

        /// <summary>Joins the supplied lines with the Unix line separator.</summary>
        /// <param name="lines">The output lines to join.</param>
        /// <returns>The joined command output.</returns>
        private static string JoinLines(params string[] lines)
        {
            return string.Join("\n", lines);
        }

        /// <summary>Skips the calling test when the host provides no echo executable to spawn.</summary>
        private static void RequireEchoExecutable()
        {
            if (!File.Exists(EchoExecutablePath))
            {
                string message =
                    $"The subprocess enumeration path needs '{EchoExecutablePath}' to emit canned output, but this "
                    + "host does not provide it.";
                Assert.Ignore(message);
            }
        }
    }
}
