/// <summary>
/// Verifies the behavior of the Utility class.
/// </summary>
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SL.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the Utility class.</summary>
    [TestFixture]
    public class UtilityTests
    {
        /// <summary>The tolerance applied to every measured bounds comparison.</summary>
        private const float Tolerance = 1e-4f;

        /// <summary>The root objects a test created, destroyed once the test completes.</summary>
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /// <summary>Destroys every root object the test created.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject created in _createdObjects)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }
            _createdObjects.Clear();
        }

        /// <summary>Verifies that GetPrefabLength warns and returns zero when the object carries no renderer.
        /// </summary>
        [Test]
        public void GetPrefabLength_NoRenderers_LogsWarningAndReturnsZero()
        {
            GameObject prefab = CreateEmptyObject("EmptyPrefab", null, Vector3.zero);
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Utility\.GetPrefabLength: No renderers found on prefab 'EmptyPrefab'\.")
            );

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(0f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength returns the z size of a single unscaled renderer.</summary>
        [Test]
        public void GetPrefabLength_SingleUnitCube_ReturnsUnitLength()
        {
            GameObject prefab = CreateCube("UnitCube", null, Vector3.zero, Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(1f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength scales the measured length with the renderer's z scale.</summary>
        [Test]
        public void GetPrefabLength_SingleCubeScaledAlongZ_ReturnsScaledLength()
        {
            GameObject prefab = CreateCube("LongCube", null, Vector3.zero, new Vector3(1f, 1f, 3f), Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(3f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength combines the root renderer with a child renderer.</summary>
        [Test]
        public void GetPrefabLength_RootAndChildRenderers_ReturnsCombinedLength()
        {
            GameObject prefab = CreateCube("RootCube", null, Vector3.zero, Vector3.one, Vector3.zero);
            CreateCube("ChildCube", prefab.transform, new Vector3(0f, 0f, 10f), Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(11f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength measures children when the root carries no renderer.</summary>
        [Test]
        public void GetPrefabLength_ChildRenderersWithoutRootRenderer_ReturnsCombinedLength()
        {
            GameObject prefab = CreateEmptyObject("SegmentRoot", null, Vector3.zero);
            CreateCube("NearCube", prefab.transform, Vector3.zero, Vector3.one, Vector3.zero);
            CreateCube("FarCube", prefab.transform, new Vector3(0f, 0f, 10f), Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(11f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength spans children placed on both sides of the origin.</summary>
        [Test]
        public void GetPrefabLength_ChildrenSpanningNegativeAndPositiveZ_ReturnsFullSpan()
        {
            GameObject prefab = CreateEmptyObject("SpanRoot", null, Vector3.zero);
            CreateCube("BehindCube", prefab.transform, new Vector3(0f, 0f, -5f), Vector3.one, Vector3.zero);
            CreateCube("CenterCube", prefab.transform, Vector3.zero, Vector3.one, Vector3.zero);
            CreateCube("AheadCube", prefab.transform, new Vector3(0f, 0f, 5f), Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(11f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength is independent of the order the renderers are enumerated in.
        /// </summary>
        [Test]
        public void GetPrefabLength_FarChildEnumeratedFirst_ReturnsSameCombinedLength()
        {
            GameObject prefab = CreateEmptyObject("OrderRoot", null, Vector3.zero);
            CreateCube("FarCube", prefab.transform, new Vector3(0f, 0f, 10f), Vector3.one, Vector3.zero);
            CreateCube("NearCube", prefab.transform, Vector3.zero, Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(11f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength keeps the enclosing extent when a child sits inside another.
        /// </summary>
        [Test]
        public void GetPrefabLength_ChildContainedWithinAnother_ReturnsEnclosingLength()
        {
            GameObject prefab = CreateEmptyObject("ContainmentRoot", null, Vector3.zero);
            CreateCube("EnclosingCube", prefab.transform, Vector3.zero, new Vector3(1f, 1f, 10f), Vector3.zero);
            CreateCube("InnerCube", prefab.transform, new Vector3(0f, 0f, 2f), Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(10f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength measures the world extent of a rotated child renderer.</summary>
        [Test]
        public void GetPrefabLength_ChildRotatedFortyFiveDegrees_ReturnsWorldAlignedExtent()
        {
            GameObject prefab = CreateEmptyObject("RotationRoot", null, Vector3.zero);
            CreateCube("RotatedCube", prefab.transform, Vector3.zero, Vector3.one, new Vector3(0f, 45f, 0f));

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(1.4142136f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength combines a rotated child with a scaled and displaced child.
        /// </summary>
        [Test]
        public void GetPrefabLength_RotatedAndScaledChildren_ReturnsCombinedWorldExtent()
        {
            GameObject prefab = CreateEmptyObject("MixedRoot", null, Vector3.zero);
            CreateCube("RotatedCube", prefab.transform, Vector3.zero, Vector3.one, new Vector3(0f, 45f, 0f));
            CreateCube("ScaledCube", prefab.transform, new Vector3(0f, 0f, 5f), new Vector3(1f, 1f, 2f), Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(6.7071068f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength reaches a renderer nested below the immediate children.
        /// </summary>
        [Test]
        public void GetPrefabLength_NestedGrandchildRenderer_IsIncluded()
        {
            GameObject prefab = CreateCube("RootCube", null, Vector3.zero, Vector3.one, Vector3.zero);
            GameObject pivot = CreateEmptyObject("Pivot", prefab.transform, new Vector3(0f, 0f, 4f));
            CreateCube("GrandchildCube", pivot.transform, new Vector3(0f, 0f, 6f), Vector3.one, Vector3.zero);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(11f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength skips a renderer sitting on a deactivated child object.</summary>
        [Test]
        public void GetPrefabLength_InactiveChildRenderer_IsExcluded()
        {
            GameObject prefab = CreateCube("RootCube", null, Vector3.zero, Vector3.one, Vector3.zero);
            GameObject child = CreateCube(
                "HiddenCube",
                prefab.transform,
                new Vector3(0f, 0f, 10f),
                Vector3.one,
                Vector3.zero
            );
            child.SetActive(false);

            float length = Utility.GetPrefabLength(prefab);

            Assert.AreEqual(1f, length, Tolerance);
        }

        /// <summary>Verifies that GetPrefabLength does not guard against a null prefab argument.</summary>
        [Test]
        public void GetPrefabLength_NullPrefab_ThrowsNullReference()
        {
            Assert.Throws<NullReferenceException>(() => Utility.GetPrefabLength(null));
        }

        /// <summary>Creates an object carrying no renderer and places it under the supplied parent.</summary>
        /// <param name="name">The name assigned to the created object.</param>
        /// <param name="parent">The parent transform, or null to leave the object at the scene root.</param>
        /// <param name="localPosition">The local position assigned to the created object.</param>
        /// <returns>The created object.</returns>
        private GameObject CreateEmptyObject(string name, Transform parent, Vector3 localPosition)
        {
            GameObject created = new GameObject(name);
            Attach(created, parent);
            created.transform.localPosition = localPosition;
            return created;
        }

        /// <summary>Creates a unit cube primitive and places it under the supplied parent.</summary>
        /// <param name="name">The name assigned to the created cube.</param>
        /// <param name="parent">The parent transform, or null to leave the cube at the scene root.</param>
        /// <param name="localPosition">The local position assigned to the created cube.</param>
        /// <param name="localScale">The local scale assigned to the created cube.</param>
        /// <param name="localEulerAngles">The local Euler rotation assigned to the created cube.</param>
        /// <returns>The created cube.</returns>
        private GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEulerAngles
        )
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Attach(cube, parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.transform.localEulerAngles = localEulerAngles;
            return cube;
        }

        /// <summary>Parents a created object, tracking it for teardown when it stays at the scene root.</summary>
        /// <param name="created">The object to parent or track.</param>
        /// <param name="parent">The parent transform, or null to leave the object at the scene root.</param>
        private void Attach(GameObject created, Transform parent)
        {
            if (parent == null)
            {
                _createdObjects.Add(created);
                return;
            }
            created.transform.SetParent(parent, worldPositionStays: false);
        }
    }
}
