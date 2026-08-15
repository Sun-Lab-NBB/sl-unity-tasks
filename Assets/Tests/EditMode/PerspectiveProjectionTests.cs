/// <summary>
/// Verifies the behavior of the PerspectiveProjection class.
/// </summary>
using System;
using System.Collections.Generic;
using Gimbl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the PerspectiveProjection class.</summary>
    [TestFixture]
    public class PerspectiveProjectionTests
    {
        /// <summary>The absolute tolerance every matrix entry comparison allows.</summary>
        private const float MatrixTolerance = 1e-4f;

        /// <summary>The far clip distance the fixture assigns to the projection camera.</summary>
        private const float FarClipDistance = 100f;

        /// <summary>The near clip distance the fixture assigns to the projection camera.</summary>
        private const float NearClipDistance = 1f;

        /// <summary>The shader name Awake resolves when it builds the brightness material.</summary>
        private const string BrightnessShaderName = "Hidden/BrightnessShader";

        /// <summary>Every object a test created, destroyed once the test completes.</summary>
        private List<UnityEngine.Object> _createdObjects;

        /// <summary>The deactivated GameObject hosting the camera and the projection under test.</summary>
        private GameObject _projectionObject;

        /// <summary>The camera the projection writes its matrices to.</summary>
        private Camera _camera;

        /// <summary>The projection under test.</summary>
        private PerspectiveProjection _projection;

        /// <summary>
        /// Builds a deactivated camera GameObject carrying the projection under test.
        /// </summary>
        /// <remarks>
        /// The GameObject stays deactivated so Unity never invokes Awake on the ExecuteInEditMode component, which
        /// keeps the material, the resolved display object, and the cached camera out of the matrix tests. UpdateView
        /// resolves the camera itself, so the deactivated rig still exercises the full projection path.
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<UnityEngine.Object>();

            _projectionObject = new GameObject("ProjectionCamera");
            _projectionObject.SetActive(false);
            _createdObjects.Add(_projectionObject);

            _camera = _projectionObject.AddComponent<Camera>();
            _camera.farClipPlane = FarClipDistance;
            _camera.nearClipPlane = NearClipDistance;

            _projection = _projectionObject.AddComponent<PerspectiveProjection>();
            _projection.estimateViewFrustum = false;
            _projection.setNearClipPlane = false;
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

        /// <summary>Verifies that UpdateView leaves the camera alone without a projection screen.</summary>
        [Test]
        public void UpdateView_NullProjectionScreen_LeavesProjectionMatrixUnchanged()
        {
            _camera.projectionMatrix = Matrix4x4.identity;

            _projection.UpdateView();

            AssertMatrixApproximatelyEquals(Matrix4x4.identity, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that UpdateView throws when the projection screen carries no MeshFilter.</summary>
        [Test]
        public void UpdateView_ProjectionScreenWithoutMeshFilter_ThrowsMissingComponent()
        {
            GameObject screen = new GameObject("ProjectionScreen");
            screen.SetActive(false);
            _createdObjects.Add(screen);
            _projection.projectionScreen = screen;

            // The editor reports an absent component through MissingComponentException, which derives from
            // Exception rather than from NullReferenceException, so the concrete type is what a test can pin.
            Assert.Throws<MissingComponentException>(() => _projection.UpdateView());
        }

        /// <summary>Verifies that UpdateView throws when the projection screen's MeshFilter holds no mesh.</summary>
        [Test]
        public void UpdateView_MeshFilterWithoutSharedMesh_ThrowsNullReference()
        {
            GameObject screen = new GameObject("ProjectionScreen");
            screen.SetActive(false);
            _createdObjects.Add(screen);
            screen.AddComponent<MeshFilter>().sharedMesh = null;
            _projection.projectionScreen = screen;

            Assert.Throws<NullReferenceException>(() => _projection.UpdateView());
        }

        /// <summary>Verifies that UpdateView resolves the mesh type and returns when no camera is attached.</summary>
        [Test]
        public void UpdateView_MissingCameraComponent_ResolvesMeshTypeAndReturns()
        {
            GameObject cameralessObject = new GameObject("CameralessProjection");
            cameralessObject.SetActive(false);
            _createdObjects.Add(cameralessObject);
            PerspectiveProjection cameralessProjection = cameralessObject.AddComponent<PerspectiveProjection>();
            cameralessProjection.projectionScreen = CreateQuadScreen();

            cameralessProjection.UpdateView();

            Assert.AreEqual("Quad", PrivateAccess.GetField<string>(cameralessProjection, "_meshType"));
        }

        /// <summary>Verifies that a quad screen centered on the eye produces a symmetric projection matrix.</summary>
        [Test]
        public void UpdateView_QuadScreenCenteredOnEye_ProducesSymmetricProjectionMatrix()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that a quad screen centered on the eye produces a screen-aligned view matrix.</summary>
        [Test]
        public void UpdateView_QuadScreenCenteredOnEye_ProducesScreenAlignedViewMatrix()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = new Matrix4x4();
            expected[0, 0] = 1f;
            expected[1, 1] = 1f;
            expected[2, 2] = -1f;
            expected[3, 3] = 1f;
            AssertMatrixApproximatelyEquals(expected, _camera.worldToCameraMatrix, "worldToCameraMatrix");
        }

        /// <summary>Verifies that an eye offset along the screen right axis skews the frustum horizontally.</summary>
        [Test]
        public void UpdateView_EyeOffsetAlongScreenRightAxis_SkewsFrustumHorizontally()
        {
            _projectionObject.transform.position = new Vector3(0.5f, 0f, 0f);
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = BuildExpectedProjection(1f, -0.5f, 1f, 0f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that an eye offset along the screen right axis shifts the view translation.</summary>
        [Test]
        public void UpdateView_EyeOffsetAlongScreenRightAxis_TranslatesViewMatrixByNegatedEye()
        {
            _projectionObject.transform.position = new Vector3(0.5f, 0f, 0f);
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = new Matrix4x4();
            expected[0, 0] = 1f;
            expected[0, 3] = -0.5f;
            expected[1, 1] = 1f;
            expected[2, 2] = -1f;
            expected[3, 3] = 1f;
            AssertMatrixApproximatelyEquals(expected, _camera.worldToCameraMatrix, "worldToCameraMatrix");
        }

        /// <summary>Verifies that an eye offset along the screen up axis skews the frustum vertically.</summary>
        [Test]
        public void UpdateView_EyeOffsetAlongScreenUpAxis_SkewsFrustumVertically()
        {
            _projectionObject.transform.position = new Vector3(0f, 0.5f, 0f);
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, -0.5f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that an eye behind the screen still yields a symmetric projection matrix.</summary>
        [Test]
        public void UpdateView_EyeBehindScreen_ProducesSymmetricProjectionMatrix()
        {
            _projectionObject.transform.position = new Vector3(0f, 0f, 2f);
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that an eye behind the screen flips the screen up axis in the view matrix.</summary>
        [Test]
        public void UpdateView_EyeBehindScreen_FlipsScreenUpAxisInViewMatrix()
        {
            _projectionObject.transform.position = new Vector3(0f, 0f, 2f);
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            Matrix4x4 expected = new Matrix4x4();
            expected[0, 0] = 1f;
            expected[1, 1] = -1f;
            expected[2, 2] = 1f;
            expected[2, 3] = -2f;
            expected[3, 3] = 1f;
            AssertMatrixApproximatelyEquals(expected, _camera.worldToCameraMatrix, "worldToCameraMatrix");
        }

        /// <summary>Verifies that a plane screen reads its corners from the plane's ten-unit local extents.</summary>
        [Test]
        public void UpdateView_PlaneScreen_ReadsCornersFromPlaneLocalExtents()
        {
            _projectionObject.transform.position = new Vector3(0f, 1f, 0f);
            _projection.projectionScreen = CreateScreen(
                "Plane",
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.2f, 1f, 0.2f)
            );

            _projection.UpdateView();

            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that a plane screen orients the view matrix along the plane's horizontal axes.</summary>
        [Test]
        public void UpdateView_PlaneScreen_OrientsViewMatrixAlongPlaneAxes()
        {
            _projectionObject.transform.position = new Vector3(0f, 1f, 0f);
            _projection.projectionScreen = CreateScreen(
                "Plane",
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.2f, 1f, 0.2f)
            );

            _projection.UpdateView();

            Matrix4x4 expected = new Matrix4x4();
            expected[0, 0] = 1f;
            expected[1, 2] = 1f;
            expected[2, 1] = 1f;
            expected[2, 3] = -1f;
            expected[3, 3] = 1f;
            AssertMatrixApproximatelyEquals(expected, _camera.worldToCameraMatrix, "worldToCameraMatrix");
        }

        /// <summary>Verifies that a mesh named neither Plane nor Quad degenerates the projection matrix.</summary>
        /// <remarks>
        /// The mesh type switch has no default arm, so the three screen corners stay at the origin and the
        /// eye-to-screen distance collapses to zero, which makes every frustum edge distance non-finite. Unity may
        /// report the degenerate matrices it is handed, so log failures are ignored for the duration of the act.
        /// </remarks>
        [Test]
        public void UpdateView_UnrecognizedMeshName_ProducesNonFiniteProjectionMatrix()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateScreen(
                "Cube",
                new Vector3(0f, 0f, 1f),
                Quaternion.identity,
                Vector3.one
            );

            bool previousIgnoreSetting = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                _projection.UpdateView();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreSetting;
            }

            Assert.AreEqual("Cube", PrivateAccess.GetField<string>(_projection, "_meshType"));
            Assert.IsTrue(float.IsNaN(_camera.projectionMatrix[0, 0]), "projectionMatrix[0,0] must be NaN.");
            Assert.IsTrue(float.IsNaN(_camera.projectionMatrix[1, 1]), "projectionMatrix[1,1] must be NaN.");
        }

        /// <summary>Verifies that the resolved mesh type is cached across UpdateView calls.</summary>
        [Test]
        public void UpdateView_CalledTwiceWithSwappedMesh_KeepsTheFirstResolvedMeshType()
        {
            _projectionObject.transform.position = Vector3.zero;
            GameObject screen = CreateQuadScreen();
            _projection.projectionScreen = screen;
            _projection.UpdateView();

            screen.GetComponent<MeshFilter>().sharedMesh = CreateNamedMesh("Plane");
            _projection.UpdateView();

            Assert.AreEqual("Quad", PrivateAccess.GetField<string>(_projection, "_meshType"));
            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0202020f, -2.0202020f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that the automatic near clip plane trails the eye-to-screen distance.</summary>
        [Test]
        public void UpdateView_SetNearClipPlaneEnabled_WritesEyeToScreenDistancePlusDefaultOffset()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _projection.setNearClipPlane = true;

            _projection.UpdateView();

            Assert.AreEqual(0.99f, _camera.nearClipPlane, 1e-5f);
            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0199980f, -1.9997980f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that a positive near clip offset pushes the near plane past the screen.</summary>
        [Test]
        public void UpdateView_PositiveNearClipDistanceOffset_PushesNearPlaneBeyondScreen()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _projection.setNearClipPlane = true;
            _projection.nearClipDistanceOffset = 0.5f;

            _projection.UpdateView();

            Assert.AreEqual(1.5f, _camera.nearClipPlane, 1e-5f);
            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0304569f, -3.0456853f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that the disabled near clip mode reuses the camera's own near clip plane.</summary>
        [Test]
        public void UpdateView_SetNearClipPlaneDisabled_ReusesCameraNearClipPlane()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _camera.nearClipPlane = 2f;

            _projection.UpdateView();

            Assert.AreEqual(2f, _camera.nearClipPlane, 1e-5f);
            Matrix4x4 expected = BuildExpectedProjection(1f, 0f, 1f, 0f, -1.0408163f, -4.0816327f);
            AssertMatrixApproximatelyEquals(expected, _camera.projectionMatrix, "projectionMatrix");
        }

        /// <summary>Verifies that the frustum estimate aims the camera at the screen center.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumEnabled_AimsCameraAtScreenCenter()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projectionObject.transform.rotation = Quaternion.Euler(37f, 11f, 5f);
            _projection.projectionScreen = CreateQuadScreen();
            _projection.estimateViewFrustum = true;
            _camera.aspect = 2f;

            _projection.UpdateView();

            float angleToIdentity = Quaternion.Angle(Quaternion.identity, _projectionObject.transform.rotation);
            Assert.AreEqual(0f, angleToIdentity, 1e-2f);
        }

        /// <summary>Verifies that a wide aspect leaves the estimated field of view undivided.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumWithWideAspect_LeavesFieldOfViewUndivided()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _projection.estimateViewFrustum = true;
            _camera.aspect = 2f;

            _projection.UpdateView();

            Assert.AreEqual(66.5868f, _camera.fieldOfView, 1e-2f);
        }

        /// <summary>Verifies that the square aspect boundary leaves the estimated field of view undivided.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumWithSquareAspect_LeavesFieldOfViewUndivided()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _projection.estimateViewFrustum = true;
            _camera.aspect = 1f;

            _projection.UpdateView();

            Assert.AreEqual(66.5868f, _camera.fieldOfView, 1e-2f);
        }

        /// <summary>Verifies that a narrow aspect divides the estimated field of view by the aspect.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumWithNarrowAspect_DividesFieldOfViewByAspect()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _projection.estimateViewFrustum = true;
            _camera.aspect = 0.5f;

            _projection.UpdateView();

            Assert.AreEqual(133.1736f, _camera.fieldOfView, 2e-2f);
        }

        /// <summary>Verifies that the disabled frustum estimate leaves the camera field of view alone.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumDisabled_LeavesFieldOfViewUnchanged()
        {
            _projectionObject.transform.position = Vector3.zero;
            _projection.projectionScreen = CreateQuadScreen();
            _camera.aspect = 2f;
            _camera.fieldOfView = 30f;

            _projection.UpdateView();

            Assert.AreEqual(30f, _camera.fieldOfView, 1e-3f);
        }

        /// <summary>Verifies that the disabled frustum estimate leaves the camera rotation alone.</summary>
        [Test]
        public void UpdateView_EstimateViewFrustumDisabled_LeavesCameraRotationUnchanged()
        {
            _projectionObject.transform.position = Vector3.zero;
            Quaternion originalRotation = Quaternion.Euler(37f, 11f, 5f);
            _projectionObject.transform.rotation = originalRotation;
            _projection.projectionScreen = CreateQuadScreen();

            _projection.UpdateView();

            float rotationDrift = Quaternion.Angle(originalRotation, _projectionObject.transform.rotation);
            Assert.AreEqual(0f, rotationDrift, 1e-3f);
        }

        /// <summary>Verifies that Awake builds the brightness material when none is assigned.</summary>
        [Test]
        public void Awake_WithoutMaterial_BuildsBrightnessShaderMaterial()
        {
            Assert.IsNull(_projection.material);

            PrivateAccess.Invoke(_projection, "Awake");

            Assert.IsNotNull(_projection.material);
            Assert.AreEqual(BrightnessShaderName, _projection.material.shader.name);
            _createdObjects.Add(_projection.material);
        }

        /// <summary>Verifies that Awake keeps a material the Inspector already assigned.</summary>
        [Test]
        public void Awake_WithAssignedMaterial_KeepsTheAssignedMaterial()
        {
            Material assignedMaterial = new Material(Shader.Find(BrightnessShaderName));
            _createdObjects.Add(assignedMaterial);
            _projection.material = assignedMaterial;

            PrivateAccess.Invoke(_projection, "Awake");

            Assert.AreSame(assignedMaterial, _projection.material);
        }

        /// <summary>Verifies that Awake resolves the camera and the display object from the hierarchy.</summary>
        [Test]
        public void Awake_UnderDisplayObjectParent_ResolvesCameraAndDisplayObject()
        {
            GameObject displayRoot = new GameObject("Display");
            _createdObjects.Add(displayRoot);
            DisplayObject displayObject = displayRoot.AddComponent<DisplayObject>();
            GameObject cameraObject = new GameObject("Left View");
            cameraObject.transform.SetParent(displayRoot.transform);
            Camera camera = cameraObject.AddComponent<Camera>();

            // The camera stays disabled so the active hierarchy Awake needs cannot also drive the render callback.
            camera.enabled = false;
            PerspectiveProjection projection = cameraObject.AddComponent<PerspectiveProjection>();

            Assert.AreSame(displayObject, projection.displayObject);
            Assert.AreSame(camera, PrivateAccess.GetField<Camera>(projection, "_cameraComponent"));
            _createdObjects.Add(projection.material);
        }

        /// <summary>Verifies that Awake leaves the display object null outside a display hierarchy.</summary>
        [Test]
        public void Awake_WithoutDisplayObjectParent_LeavesDisplayObjectNull()
        {
            GameObject cameraObject = new GameObject("Left View");
            _createdObjects.Add(cameraObject);

            // The camera stays disabled so the active hierarchy Awake needs cannot also drive the render callback.
            cameraObject.AddComponent<Camera>().enabled = false;
            PerspectiveProjection projection = cameraObject.AddComponent<PerspectiveProjection>();

            Assert.IsNull(projection.displayObject);
            _createdObjects.Add(projection.material);
        }

        /// <summary>Builds the two-by-two quad screen that sits one unit in front of the world origin.</summary>
        /// <returns>The projection screen GameObject.</returns>
        private GameObject CreateQuadScreen()
        {
            return CreateScreen("Quad", new Vector3(0f, 0f, 1f), Quaternion.identity, new Vector3(2f, 2f, 1f));
        }

        /// <summary>Builds a deactivated projection screen carrying a mesh with the supplied name.</summary>
        /// <param name="meshName">The mesh name the projection reads to pick its corner layout.</param>
        /// <param name="position">The world position assigned to the screen transform.</param>
        /// <param name="rotation">The world rotation assigned to the screen transform.</param>
        /// <param name="scale">The local scale assigned to the screen transform.</param>
        /// <returns>The projection screen GameObject.</returns>
        private GameObject CreateScreen(string meshName, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject screen = new GameObject("ProjectionScreen");
            screen.SetActive(false);
            _createdObjects.Add(screen);
            screen.transform.position = position;
            screen.transform.rotation = rotation;
            screen.transform.localScale = scale;
            screen.AddComponent<MeshFilter>().sharedMesh = CreateNamedMesh(meshName);
            return screen;
        }

        /// <summary>Builds an empty mesh carrying the supplied name.</summary>
        /// <param name="meshName">The name assigned to the mesh.</param>
        /// <returns>The named mesh.</returns>
        private Mesh CreateNamedMesh(string meshName)
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;
            _createdObjects.Add(mesh);
            return mesh;
        }

        /// <summary>Builds the expected off-axis projection matrix from its six non-trivial entries.</summary>
        /// <param name="horizontalScale">The expected entry at row zero, column zero.</param>
        /// <param name="horizontalSkew">The expected entry at row zero, column two.</param>
        /// <param name="verticalScale">The expected entry at row one, column one.</param>
        /// <param name="verticalSkew">The expected entry at row one, column two.</param>
        /// <param name="depthScale">The expected entry at row two, column two.</param>
        /// <param name="depthOffset">The expected entry at row two, column three.</param>
        /// <returns>The fully populated expected matrix.</returns>
        private static Matrix4x4 BuildExpectedProjection(
            float horizontalScale,
            float horizontalSkew,
            float verticalScale,
            float verticalSkew,
            float depthScale,
            float depthOffset
        )
        {
            Matrix4x4 expected = new Matrix4x4();
            expected[0, 0] = horizontalScale;
            expected[0, 2] = horizontalSkew;
            expected[1, 1] = verticalScale;
            expected[1, 2] = verticalSkew;
            expected[2, 2] = depthScale;
            expected[2, 3] = depthOffset;
            expected[3, 2] = -1f;
            return expected;
        }

        /// <summary>Asserts that every one of the sixteen matrix entries matches within the shared tolerance.</summary>
        /// <param name="expected">The matrix the production code is expected to produce.</param>
        /// <param name="actual">The matrix the production code produced.</param>
        /// <param name="label">The matrix name quoted in each failure message.</param>
        private static void AssertMatrixApproximatelyEquals(Matrix4x4 expected, Matrix4x4 actual, string label)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    Assert.AreEqual(
                        expected[row, column],
                        actual[row, column],
                        MatrixTolerance,
                        $"{label}[{row},{column}]"
                    );
                }
            }
        }
    }
}
