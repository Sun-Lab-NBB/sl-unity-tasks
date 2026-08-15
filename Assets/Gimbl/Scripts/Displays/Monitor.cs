/// <summary>
/// Provides the Monitor class for detecting and storing system monitor information.
///
/// Enumerates physical monitors across Windows, Linux, and macOS platforms and stores
/// their position, dimensions, and camera assignment for multi-monitor VR displays.
/// </summary>
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Gimbl
{
    /// <summary>
    /// Stores monitor display information and camera assignment.
    /// </summary>
    [Serializable]
    public class Monitor
    {
        /// <summary>
        /// The budget in milliseconds allowed separately for the enumeration subprocess to exit and for its output
        /// to be read, bounding each wait on the editor thread.
        /// </summary>
        private const int SubprocessTimeoutMilliseconds = 5000;

        /// <summary>The displayplacer install path used by the Apple Silicon Homebrew prefix.</summary>
        private const string AppleSiliconDisplayPlacerPath = "/opt/homebrew/bin/displayplacer";

        /// <summary>The displayplacer install path used by the Intel Homebrew prefix.</summary>
        private const string IntelDisplayPlacerPath = "/usr/local/bin/displayplacer";

        /// <summary>The bare displayplacer executable name, resolved through PATH.</summary>
        private const string DisplayPlacerExecutableName = "displayplacer";

        /// <summary>The pre-compiled regex matching xrandr `WxH+L+T` connected-monitor lines.</summary>
        private static readonly Regex LinuxMonitorRegex = new Regex(
            @"(\d+)x(\d+)\+(\d+)\+(\d+)",
            RegexOptions.Compiled
        );

        /// <summary>
        /// The pre-compiled regex pairing each displayplacer Resolution line with the Origin line of the same display
        /// block. The gap between the two fields is consumed one line at a time and stops at the next Resolution line,
        /// so a block whose Origin fails to match cannot borrow the origin of the block that follows it.
        /// </summary>
        private static readonly Regex MacOsMonitorRegex = new Regex(
            @"Resolution: (\d+)x(\d+)(?:\r?\n(?!Resolution:)[^\r\n]*)*?\r?\nOrigin: [(](-?\d+),(-?\d+)[)]",
            RegexOptions.Compiled
        );

        /// <summary>The left position of the monitor in pixels.</summary>
        public int left;

        /// <summary>The top position of the monitor in pixels.</summary>
        public int top;

        /// <summary>The width of the monitor in pixels.</summary>
        public int width;

        /// <summary>The height of the monitor in pixels.</summary>
        public int height;

        /// <summary>The pixels per point scaling factor for this monitor.</summary>
        public float pixelsPerPoint;

        /// <summary>The entity ID of the camera assigned to this monitor.</summary>
        public EntityId cameraEntityId;

        /// <summary>Creates a new monitor with the specified position and dimensions.</summary>
        /// <param name="leftPosition">The left position in pixels.</param>
        /// <param name="topPosition">The top position in pixels.</param>
        /// <param name="widthPixels">The width in pixels.</param>
        /// <param name="heightPixels">The height in pixels.</param>
        private Monitor(int leftPosition, int topPosition, int widthPixels, int heightPixels)
        {
            left = leftPosition;
            top = topPosition;
            width = widthPixels;
            height = heightPixels;
            pixelsPerPoint = 1.0f;
            cameraEntityId = EntityId.None;
        }

        /// <summary>Detects and returns a list of all system monitors.</summary>
        /// <remarks>
        /// On each detected monitor, briefly opens a 20×20 popup <see cref="MonitorTester"/> window to
        /// measure <c>EditorGUIUtility.pixelsPerPoint</c>, then closes it immediately. This per-monitor
        /// probe is the only way to read the DPI scale that varies between displays on the same host.
        /// </remarks>
        /// <returns>The list of detected monitors with their positions and dimensions.</returns>
        public static List<Monitor> EnumerateMonitors()
        {
            List<Monitor> result = new List<Monitor>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnumDisplayMonitors(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    delegate(IntPtr hMonitor, IntPtr hdc, ref RectApi monitorRect, IntPtr dwData)
                    {
                        result.Add(
                            new Monitor(monitorRect.left, monitorRect.top, monitorRect.Width, monitorRect.Height)
                        );
                        return true;
                    },
                    0
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                EnumerateViaSubprocess("xrandr", string.Empty, LinuxMonitorRegex, result);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                EnumerateViaSubprocess(ResolveDisplayPlacerPath(), "list", MacOsMonitorRegex, result);
            }

            foreach (Monitor monitor in result)
            {
                MonitorTester tester = EditorWindow.CreateInstance<MonitorTester>();
                tester.position = new Rect(monitor.left, monitor.top, 20, 20);
                tester.monitor = monitor;
                tester.ShowPopup();
            }

            return result;
        }

        /// <summary>Resolves the displayplacer executable path across the supported Homebrew prefixes.</summary>
        /// <returns>The first Homebrew install path that exists, or the bare executable name.</returns>
        private static string ResolveDisplayPlacerPath()
        {
            if (File.Exists(AppleSiliconDisplayPlacerPath))
            {
                return AppleSiliconDisplayPlacerPath;
            }
            if (File.Exists(IntelDisplayPlacerPath))
            {
                return IntelDisplayPlacerPath;
            }
            return DisplayPlacerExecutableName;
        }

        /// <summary>
        /// Spawns a subprocess, reads its stdout to completion, and appends every parsed monitor match to
        /// <paramref name="result"/>. Used by the Linux and macOS branches of <see cref="EnumerateMonitors"/>.
        /// </summary>
        /// <param name="command">The executable to invoke (full path or PATH-resolved name).</param>
        /// <param name="arguments">The command-line arguments passed to <paramref name="command"/>.</param>
        /// <param name="pattern">The regex producing groups (width, height, left, top) at indices 1-4.</param>
        /// <param name="result">The collection that receives every successfully parsed monitor.</param>
        private static void EnumerateViaSubprocess(
            string command,
            string arguments,
            Regex pattern,
            List<Monitor> result
        )
        {
            using Process process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.FileName = command;
            process.StartInfo.Arguments = arguments;

            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                string startMessage = $"Monitor enumeration: failed to start '{command}': {exception.Message}";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    startMessage += " Install it with 'brew install displayplacer'.";
                }
                Debug.LogWarning(startMessage);
                return;
            }

            // The read starts before the wait so a child filling the pipe buffer cannot deadlock against a drain
            // that has not begun, and each of the two waits is bounded so neither can stall the editor thread.
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(SubprocessTimeoutMilliseconds))
            {
                process.Kill();
                Debug.LogWarning(
                    $"Monitor enumeration: '{command}' timed out after {SubprocessTimeoutMilliseconds}ms; "
                        + "monitor list may be incomplete."
                );
            }

            string output;
            try
            {
                if (!outputTask.Wait(SubprocessTimeoutMilliseconds))
                {
                    Debug.LogWarning(
                        $"Monitor enumeration: reading the output of '{command}' timed out after "
                            + $"{SubprocessTimeoutMilliseconds}ms."
                    );
                    return;
                }
                output = outputTask.Result;
            }
            catch (AggregateException exception)
            {
                Debug.LogWarning($"Monitor enumeration: failed to read the output of '{command}': {exception.Message}");
                return;
            }

            foreach (Match match in pattern.Matches(output))
            {
                if (
                    match.Groups.Count >= 5
                    && int.TryParse(match.Groups[1].Value, out int matchWidth)
                    && int.TryParse(match.Groups[2].Value, out int matchHeight)
                    && int.TryParse(match.Groups[3].Value, out int matchLeft)
                    && int.TryParse(match.Groups[4].Value, out int matchTop)
                )
                {
                    result.Add(new Monitor(matchLeft, matchTop, matchWidth, matchHeight));
                }
            }
        }

        /// <summary>The delegate for Windows monitor enumeration callback.</summary>
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RectApi pRect, IntPtr dwData);

        /// <summary>Windows API function to enumerate display monitors.</summary>
        [DllImport("user32")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lpRect, MonitorEnumProc callback, int dwData);

        /// <summary>
        /// Windows API rectangle structure for monitor bounds.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RectApi
        {
            /// <summary>The left edge coordinate in pixels.</summary>
            public int left;

            /// <summary>The top edge coordinate in pixels.</summary>
            public int top;

            /// <summary>The right edge coordinate in pixels.</summary>
            public int right;

            /// <summary>The bottom edge coordinate in pixels.</summary>
            public int bottom;

            /// <summary>The width of the rectangle in pixels.</summary>
            public int Width => right - left;

            /// <summary>The height of the rectangle in pixels.</summary>
            public int Height => bottom - top;
        }

        /// <summary>
        /// Temporary editor window for detecting pixels per point on each monitor.
        /// </summary>
        private class MonitorTester : EditorWindow
        {
            /// <summary>The monitor to test.</summary>
            internal Monitor monitor;

            /// <summary>Records pixels per point and closes immediately.</summary>
            private void OnGUI()
            {
                monitor.pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Close();
            }
        }
    }
}
#endif
