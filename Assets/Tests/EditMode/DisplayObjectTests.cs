/// <summary>
/// Verifies the behavior of the DisplayObject class.
/// </summary>
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the DisplayObject class.</summary>
    /// <remarks>
    /// Everything reachable without touching project assets is covered: the actor attachment, the eye height offset,
    /// the actor layer culling, and the detach path.
    /// </remarks>
    [TestFixture]
    public class DisplayObjectTests
    {
        /// <summary>The name of a built-in layer every Unity project defines, used as the actor layer.</summary>
        private const string ExistingLayerName = "TransparentFX";

        /// <summary>The culling mask expected once the TransparentFX layer at index one is excluded.</summary>
        private const int CullingMaskWithoutLayerOne = -3;

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

        /// <summary>Verifies that a new display component starts at full brightness.</summary>
        [Test]
        public void CurrentBrightness_NewComponent_DefaultsToOneHundred()
        {
            DisplayObject display = CreateDisplay(cameraCount: 0);

            Assert.AreEqual(100f, display.currentBrightness, 1e-6f);
        }

        /// <summary>Verifies that attaching a display parents it under the actor transform.</summary>
        [Test]
        public void ParentToActor_AnyActor_ParentsTheDisplayUnderTheActor()
        {
            DisplayObject display = CreateDisplay(cameraCount: 0);
            ActorObject actor = CreateActor(ExistingLayerName);

            display.ParentToActor(actor);

            Assert.AreSame(actor.transform, display.transform.parent);
        }

        /// <summary>Verifies that a display without settings sits exactly on the actor origin.</summary>
        [Test]
        public void ParentToActor_WithoutSettings_PlacesTheDisplayOnTheActorOrigin()
        {
            DisplayObject display = CreateDisplay(cameraCount: 0);
            ActorObject actor = CreateActor(ExistingLayerName);
            actor.transform.position = new Vector3(3f, 4f, 5f);

            display.ParentToActor(actor);

            Assert.AreEqual(0f, display.transform.localPosition.x, 1e-6f);
            Assert.AreEqual(0f, display.transform.localPosition.y, 1e-6f);
            Assert.AreEqual(0f, display.transform.localPosition.z, 1e-6f);
        }

        /// <summary>Verifies that a display with settings sits at the configured VR eye height.</summary>
        [Test]
        public void ParentToActor_WithSettings_PlacesTheDisplayAtTheConfiguredEyeHeight()
        {
            DisplayObject display = CreateDisplay(cameraCount: 0);
            DisplaySettings settings = ScriptableObject.CreateInstance<DisplaySettings>();
            _createdObjects.Add(settings);
            settings.heightInVR = 0.35f;
            display.settings = settings;
            ActorObject actor = CreateActor(ExistingLayerName);
            actor.transform.position = new Vector3(3f, 4f, 5f);

            display.ParentToActor(actor);

            Assert.AreEqual(0f, display.transform.localPosition.x, 1e-6f);
            Assert.AreEqual(0.35f, display.transform.localPosition.y, 1e-6f);
            Assert.AreEqual(0f, display.transform.localPosition.z, 1e-6f);
        }

        /// <summary>Verifies that an actor whose name matches a layer is culled from every display camera.</summary>
        [Test]
        public void ParentToActor_ActorNameMatchingALayer_CullsThatLayerFromEveryDisplayCamera()
        {
            DisplayObject display = CreateDisplay(cameraCount: 2);
            ActorObject actor = CreateActor(ExistingLayerName);

            display.ParentToActor(actor);

            Camera[] displayCameras = display.GetComponentsInChildren<Camera>();
            Assert.AreEqual(2, displayCameras.Length);
            Assert.AreEqual(CullingMaskWithoutLayerOne, displayCameras[0].cullingMask);
            Assert.AreEqual(CullingMaskWithoutLayerOne, displayCameras[1].cullingMask);
        }

        /// <summary>Verifies that an actor without a matching layer warns and leaves the culling mask open.</summary>
        [Test]
        public void ParentToActor_ActorNameWithoutALayer_WarnsAndLeavesTheCullingMaskOpen()
        {
            DisplayObject display = CreateDisplay(cameraCount: 1);
            ActorObject actor = CreateActor("SollertiaLayerThatDoesNotExist");
            LogAssert.Expect(LogType.Warning, new Regex(".*unable to cull the actor model from display.*"));

            display.ParentToActor(actor);

            Assert.AreEqual(-1, display.GetComponentInChildren<Camera>().cullingMask);
        }

        /// <summary>Verifies that a display carrying no cameras attaches without throwing.</summary>
        [Test]
        public void ParentToActor_DisplayWithoutCameras_AttachesWithoutThrowing()
        {
            DisplayObject display = CreateDisplay(cameraCount: 0);
            ActorObject actor = CreateActor(ExistingLayerName);

            Assert.DoesNotThrow(() => display.ParentToActor(actor));
            Assert.AreSame(actor.transform, display.transform.parent);
        }

        /// <summary>Verifies that detaching a display clears its parent.</summary>
        [Test]
        public void Unparent_AttachedDisplay_ClearsTheParentTransform()
        {
            DisplayObject display = CreateDisplay(cameraCount: 1);
            ActorObject actor = CreateActor(ExistingLayerName);
            display.ParentToActor(actor);

            display.Unparent();

            Assert.IsNull(display.transform.parent);
        }

        /// <summary>Verifies that detaching a display restores the full culling mask on every display camera.</summary>
        [Test]
        public void Unparent_AttachedDisplay_RestoresTheFullCullingMask()
        {
            DisplayObject display = CreateDisplay(cameraCount: 2);
            ActorObject actor = CreateActor(ExistingLayerName);
            display.ParentToActor(actor);

            display.Unparent();

            Camera[] displayCameras = display.GetComponentsInChildren<Camera>();
            Assert.AreEqual(2, displayCameras.Length);
            Assert.AreEqual(-1, displayCameras[0].cullingMask);
            Assert.AreEqual(-1, displayCameras[1].cullingMask);
        }

        /// <summary>Verifies that creating a display from an absent model prefab reports the failure.</summary>
        [Test]
        public void Create_AbsentModelPrefab_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(
                LogType.Error,
                new Regex(".*model 'SollertiaModelThatDoesNotExist' not found under Resources/Displays.*")
            );

            DisplayObject created = DisplayObject.Create("TestDisplay", "SollertiaModelThatDoesNotExist");

            Assert.IsNull(created);
        }

        /// <summary>Builds a display GameObject carrying the requested number of child cameras.</summary>
        /// <param name="cameraCount">The number of child cameras parented under the display.</param>
        /// <returns>The display component under test.</returns>
        private DisplayObject CreateDisplay(int cameraCount)
        {
            GameObject displayGameObject = new GameObject("Display");
            _createdObjects.Add(displayGameObject);
            DisplayObject display = displayGameObject.AddComponent<DisplayObject>();
            for (int cameraIndex = 0; cameraIndex < cameraCount; cameraIndex++)
            {
                GameObject cameraObject = new GameObject($"View {cameraIndex}");
                cameraObject.transform.SetParent(displayGameObject.transform);
                cameraObject.AddComponent<Camera>();
            }
            return display;
        }

        /// <summary>Builds an actor GameObject carrying the supplied name.</summary>
        /// <param name="actorName">The actor name the culling path resolves into a layer index.</param>
        /// <returns>The actor component the display attaches to.</returns>
        private ActorObject CreateActor(string actorName)
        {
            GameObject actorGameObject = new GameObject(actorName);
            _createdObjects.Add(actorGameObject);
            return actorGameObject.AddComponent<ActorObject>();
        }
    }
}
