/// <summary>
/// Verifies the behavior of the FullScreenViewManager class.
/// </summary>
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Gimbl;
using NUnit.Framework;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the FullScreenViewManager class.</summary>
    /// <remarks>
    /// The IMGUI entry points and the borderless window creation path need a real editor layout and a graphics device,
    /// so they stay out of scope.
    /// </remarks>
    [TestFixture]
    public class FullScreenViewManagerTests
    {
        /// <summary>The tooltip every camera dropdown entry is expected to carry.</summary>
        private const string ExpectedCameraOptionTooltip =
            "Scene Camera that renders to this monitor when full-screen views are launched. "
            + "None leaves the monitor unused.";

        /// <summary>Every object a test created, destroyed once the test completes.</summary>
        private List<UnityEngine.Object> _createdObjects;

        /// <summary>Prepares the per-test cleanup list.</summary>
        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<UnityEngine.Object>();
        }

        /// <summary>Destroys every object the test created.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object created in _createdObjects)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }
            _createdObjects.Clear();
        }

        /// <summary>Verifies that a monitor left of the origin scales its own x position by its own DPI.</summary>
        [Test]
        public void ComputeWindowRect_MonitorLeftOfOrigin_ScalesXByItsOwnPixelsPerPoint()
        {
            FullScreenViewManager manager = CreateManager(
                CreateMonitor(0, 0, 1920, 1080, 2.0f),
                CreateMonitor(-1600, 0, 1600, 900, 1.0f)
            );

            Rect rect = (Rect)PrivateAccess.Invoke(manager, "ComputeWindowRect", manager.monitors[1]);

            Assert.AreEqual(-1600f, rect.x, 1e-4f);
            Assert.AreEqual(0f, rect.y, 1e-4f);
            Assert.AreEqual(1600f, rect.width, 1e-4f);
            Assert.AreEqual(900f, rect.height, 1e-4f);
        }

        /// <summary>Verifies that a monitor above the origin scales its own y position by its own DPI.</summary>
        [Test]
        public void ComputeWindowRect_MonitorAboveOrigin_ScalesYByItsOwnPixelsPerPoint()
        {
            FullScreenViewManager manager = CreateManager(
                CreateMonitor(0, 0, 1920, 1080, 2.0f),
                CreateMonitor(1920, -100, 1920, 1080, 1.5f)
            );

            Rect rect = (Rect)PrivateAccess.Invoke(manager, "ComputeWindowRect", manager.monitors[1]);

            Assert.AreEqual(960f, rect.x, 1e-4f);
            Assert.AreEqual(-66f, rect.y, 1e-4f);
            Assert.AreEqual(1280f, rect.width, 1e-4f);
            Assert.AreEqual(720f, rect.height, 1e-4f);
        }

        /// <summary>Verifies that a monitor right of the origin scales by the primary monitor DPI.</summary>
        [Test]
        public void ComputeWindowRect_MonitorRightOfOrigin_ScalesPositionByPrimaryPixelsPerPoint()
        {
            FullScreenViewManager manager = CreateManager(
                CreateMonitor(0, 0, 1920, 1080, 2.0f),
                CreateMonitor(1, 1, 4, 6, 1.0f)
            );

            Rect rect = (Rect)PrivateAccess.Invoke(manager, "ComputeWindowRect", manager.monitors[1]);

            Assert.AreEqual(0f, rect.x, 1e-4f);
            Assert.AreEqual(0f, rect.y, 1e-4f);
            Assert.AreEqual(4f, rect.width, 1e-4f);
            Assert.AreEqual(6f, rect.height, 1e-4f);
        }

        /// <summary>Verifies that the primary monitor scales its own rect when it sits left of the origin.</summary>
        [Test]
        public void ComputeWindowRect_PrimaryMonitorLeftAndAboveOrigin_ScalesEveryComponentByItsOwnDpi()
        {
            FullScreenViewManager manager = CreateManager(CreateMonitor(-10, -20, 800, 600, 2.0f));

            Rect rect = (Rect)PrivateAccess.Invoke(manager, "ComputeWindowRect", manager.monitors[0]);

            Assert.AreEqual(-5f, rect.x, 1e-4f);
            Assert.AreEqual(-10f, rect.y, 1e-4f);
            Assert.AreEqual(400f, rect.width, 1e-4f);
            Assert.AreEqual(300f, rect.height, 1e-4f);
        }

        /// <summary>Verifies that a root GameObject resolves to its own name.</summary>
        [Test]
        public void PathName_RootGameObject_ReturnsTheObjectName()
        {
            GameObject root = new GameObject("Solo View");
            _createdObjects.Add(root);

            string path = (string)PrivateAccess.InvokeStatic(typeof(FullScreenViewManager), "PathName", root);

            Assert.AreEqual("Solo View", path);
        }

        /// <summary>Verifies that a nested GameObject resolves to its full root-to-leaf hierarchy path.</summary>
        [Test]
        public void PathName_NestedGameObject_ReturnsTheRootToLeafPath()
        {
            GameObject root = new GameObject("Display");
            _createdObjects.Add(root);
            GameObject middle = new GameObject("Screens");
            middle.transform.SetParent(root.transform);
            GameObject leaf = new GameObject("Left View");
            leaf.transform.SetParent(middle.transform);

            string path = (string)PrivateAccess.InvokeStatic(typeof(FullScreenViewManager), "PathName", leaf);

            Assert.AreEqual("Display/Screens/Left View", path);
        }

        /// <summary>Verifies that an unassigned monitor resolves to no camera.</summary>
        [Test]
        public void GetCameraFor_UnassignedMonitor_ResolvesToNull()
        {
            Monitor monitor = CreateMonitor(0, 0, 1920, 1080, 1f);

            Camera camera = (Camera)PrivateAccess.InvokeStatic(typeof(FullScreenViewManager), "GetCameraFor", monitor);

            Assert.IsNull(camera);
        }

        /// <summary>Verifies that an assigned monitor resolves back to the camera behind its entity id.</summary>
        [Test]
        public void GetCameraFor_AssignedMonitor_ResolvesToTheAssignedCamera()
        {
            GameObject cameraObject = new GameObject("Left View");
            _createdObjects.Add(cameraObject);
            Camera assignedCamera = cameraObject.AddComponent<Camera>();
            Monitor monitor = CreateMonitor(0, 0, 1920, 1080, 1f);
            monitor.cameraEntityId = assignedCamera.GetEntityId();

            Camera camera = (Camera)PrivateAccess.InvokeStatic(typeof(FullScreenViewManager), "GetCameraFor", monitor);

            Assert.AreSame(assignedCamera, camera);
        }

        /// <summary>Verifies that an empty camera set still offers the leading None entry.</summary>
        [Test]
        public void BuildCameraOptions_NoCameras_ReturnsTheNoneEntryAlone()
        {
            GUIContent[] options = (GUIContent[])
                PrivateAccess.InvokeStatic(
                    typeof(FullScreenViewManager),
                    "BuildCameraOptions",
                    new object[] { new Camera[0] }
                );

            Assert.AreEqual(1, options.Length);
            Assert.AreEqual("None", options[0].text);
            Assert.AreEqual(ExpectedCameraOptionTooltip, options[0].tooltip);
        }

        /// <summary>Verifies that every supplied camera follows the None entry in declaration order.</summary>
        [Test]
        public void BuildCameraOptions_TwoCameras_AppendsEachCameraNameAfterNone()
        {
            GameObject firstObject = new GameObject("Left View");
            _createdObjects.Add(firstObject);
            GameObject secondObject = new GameObject("Right View");
            _createdObjects.Add(secondObject);
            Camera[] cameras = new Camera[] { firstObject.AddComponent<Camera>(), secondObject.AddComponent<Camera>() };

            GUIContent[] options = (GUIContent[])
                PrivateAccess.InvokeStatic(
                    typeof(FullScreenViewManager),
                    "BuildCameraOptions",
                    new object[] { cameras }
                );

            Assert.AreEqual(3, options.Length);
            Assert.AreEqual("None", options[0].text);
            Assert.AreEqual("Left View", options[1].text);
            Assert.AreEqual("Right View", options[2].text);
            Assert.AreEqual(ExpectedCameraOptionTooltip, options[2].tooltip);
        }

        /// <summary>Verifies that the dropdown drops the default Main Camera by tag and by name.</summary>
        [Test]
        public void EnumerateAssignableCameras_DefaultMainCameras_AreExcludedFromTheResult()
        {
            GameObject assignableObject = new GameObject("Left View");
            _createdObjects.Add(assignableObject);
            Camera assignableCamera = assignableObject.AddComponent<Camera>();

            GameObject namedObject = new GameObject("Main Camera");
            _createdObjects.Add(namedObject);
            Camera namedCamera = namedObject.AddComponent<Camera>();

            GameObject taggedObject = new GameObject("Renamed Default Camera");
            _createdObjects.Add(taggedObject);
            taggedObject.tag = "MainCamera";
            Camera taggedCamera = taggedObject.AddComponent<Camera>();

            Camera[] assignable = (Camera[])
                PrivateAccess.InvokeStatic(typeof(FullScreenViewManager), "EnumerateAssignableCameras");

            CollectionAssert.Contains(assignable, assignableCamera);
            CollectionAssert.DoesNotContain(assignable, namedCamera);
            CollectionAssert.DoesNotContain(assignable, taggedCamera);
        }

        /// <summary>Verifies that saving without a companion asset leaves the manager untouched.</summary>
        [Test]
        public void SaveCameras_WithoutCompanionAsset_LeavesTheCompanionFieldNull()
        {
            FullScreenViewManager manager = CreateManager(CreateMonitor(0, 0, 1920, 1080, 1f));
            PrivateAccess.SetField(manager, "_savedFullScreenViews", null);

            manager.SaveCameras();

            Assert.IsNull(PrivateAccess.GetField<FullScreenViewsSaved>(manager, "_savedFullScreenViews"));
        }

        /// <summary>Verifies that saving with no detected monitors preserves the persisted camera names.</summary>
        [Test]
        public void SaveCameras_NoDetectedMonitors_PreservesThePersistedCameraNames()
        {
            FullScreenViewsSaved saved = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
            _createdObjects.Add(saved);
            saved.cameraNames.Add("Display/Left View");
            FullScreenViewManager manager = CreateManager();
            PrivateAccess.SetField(manager, "_savedFullScreenViews", saved);

            manager.SaveCameras();

            Assert.AreEqual(1, saved.cameraNames.Count);
            Assert.AreEqual("Display/Left View", saved.cameraNames[0]);
        }

        /// <summary>Verifies that saving rebuilds one camera path entry per detected monitor.</summary>
        [Test]
        public void SaveCameras_MixedAssignments_WritesOnePathEntryPerMonitor()
        {
            GameObject root = new GameObject("Display");
            _createdObjects.Add(root);
            GameObject cameraObject = new GameObject("Left View");
            cameraObject.transform.SetParent(root.transform);
            Camera assignedCamera = cameraObject.AddComponent<Camera>();

            FullScreenViewsSaved saved = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
            _createdObjects.Add(saved);
            saved.cameraNames.Add("Stale/Entry");

            FullScreenViewManager manager = CreateManager(
                CreateMonitor(0, 0, 1920, 1080, 1f),
                CreateMonitor(1920, 0, 1920, 1080, 1f)
            );
            manager.monitors[0].cameraEntityId = assignedCamera.GetEntityId();
            PrivateAccess.SetField(manager, "_savedFullScreenViews", saved);

            manager.SaveCameras();

            Assert.AreEqual(2, saved.cameraNames.Count);
            Assert.AreEqual("Display/Left View", saved.cameraNames[0]);
            Assert.AreEqual(string.Empty, saved.cameraNames[1]);
        }

        /// <summary>Verifies that unassigned monitors open no borderless windows.</summary>
        [Test]
        public void ShowFullScreenViews_UnassignedMonitors_OpensNoViews()
        {
            FullScreenViewManager manager = CreateManager(
                CreateMonitor(0, 0, 1920, 1080, 1f),
                CreateMonitor(1920, 0, 1920, 1080, 1f)
            );
            int viewCountBefore = FullScreenView.Views.Count;

            manager.ShowFullScreenViews(closeOldViews: false);

            Assert.AreEqual(viewCountBefore, FullScreenView.Views.Count);
        }

        /// <summary>Builds a manager without running the monitor-enumerating constructor.</summary>
        /// <remarks>
        /// The public constructor enumerates the host's monitors, which opens a popup EditorWindow per monitor, so no
        /// test runs it.
        /// </remarks>
        /// <param name="monitors">The monitors assigned to the manager.</param>
        /// <returns>The manager carrying the supplied monitor list.</returns>
        private static FullScreenViewManager CreateManager(params Monitor[] monitors)
        {
            FullScreenViewManager manager = (FullScreenViewManager)
                FormatterServices.GetUninitializedObject(typeof(FullScreenViewManager));
            manager.monitors = new List<Monitor>(monitors);
            return manager;
        }

        /// <summary>Builds a monitor record through the private constructor the enumeration path uses.</summary>
        /// <param name="left">The left position in pixels.</param>
        /// <param name="top">The top position in pixels.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <param name="pixelsPerPoint">The DPI scale assigned to the record after construction.</param>
        /// <returns>The constructed monitor record.</returns>
        private static Monitor CreateMonitor(int left, int top, int width, int height, float pixelsPerPoint)
        {
            Monitor monitor = (Monitor)
                Activator.CreateInstance(
                    typeof(Monitor),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { left, top, width, height },
                    null
                );
            monitor.pixelsPerPoint = pixelsPerPoint;
            return monitor;
        }
    }
}
