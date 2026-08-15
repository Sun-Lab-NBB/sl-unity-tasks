/// <summary>
/// Verifies the behavior of the ActorObject class.
///
/// Every test drives the editor-time entry points directly, because Edit Mode runs no player loop. The tests that
/// touch the tracking camera reason about the displays already claimed by the open scene rather than assuming an
/// empty scene, so the assertions hold whether the runner opens ExperimentTemplate.unity or an untitled scene.
/// </summary>
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gimbl;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the ActorObject class.</summary>
    [TestFixture]
    public class ActorObjectTests
    {
        /// <summary>The layer slot the project reserves for the "Actor" layer name.</summary>
        private const int ActorLayerIndex = 8;

        /// <summary>The highest display index the tracking camera assignment considers.</summary>
        private const int HighestDisplayIndex = 7;

        /// <summary>The GameObjects the running test created, destroyed during teardown.</summary>
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /// <summary>Clears the undo stack and destroys every object the finished test created.</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            foreach (GameObject created in _createdObjects)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }
            _createdObjects.Clear();
        }

        /// <summary>Verifies that assigning a display parents it under the actor at the local origin.</summary>
        [Test]
        public void Display_AssignedFromNull_ParentsTheDisplayUnderTheActor()
        {
            ActorObject actor = NewActor("Actor");
            DisplayObject display = NewDisplay("Display");

            actor.Display = display;

            Assert.AreEqual(display, actor.Display);
            Assert.AreEqual(actor.transform, display.transform.parent);
            Assert.AreEqual(Vector3.zero, display.transform.localPosition);
        }

        /// <summary>Verifies that re-assigning the same display instance skips the re-parenting work.</summary>
        [Test]
        public void Display_AssignedSameInstanceTwice_SkipsTheReparenting()
        {
            ActorObject actor = NewActor("Actor");
            DisplayObject display = NewDisplay("Display");
            actor.Display = display;
            display.transform.SetParent(null);

            actor.Display = display;

            Assert.IsNull(display.transform.parent);
            Assert.AreEqual(display, actor.Display);
        }

        /// <summary>Verifies that swapping displays unparents the outgoing one and parents the incoming one.</summary>
        [Test]
        public void Display_ReplacedWithAnotherDisplay_UnparentsThePreviousDisplay()
        {
            ActorObject actor = NewActor("Actor");
            DisplayObject first = NewDisplay("FirstDisplay");
            DisplayObject second = NewDisplay("SecondDisplay");
            actor.Display = first;

            actor.Display = second;

            Assert.IsNull(first.transform.parent);
            Assert.AreEqual(actor.transform, second.transform.parent);
            Assert.AreEqual(second, actor.Display);
        }

        /// <summary>Verifies that clearing the display unparents it and empties the serialized field.</summary>
        [Test]
        public void Display_ClearedToNull_UnparentsThePreviousDisplay()
        {
            ActorObject actor = NewActor("Actor");
            DisplayObject display = NewDisplay("Display");
            actor.Display = display;

            actor.Display = null;

            Assert.IsNull(display.transform.parent);
            Assert.IsNull(actor.Display);
        }

        /// <summary>Verifies that clearing an already empty display slot leaves the field empty.</summary>
        [Test]
        public void Display_AssignedNullWhileAlreadyNull_LeavesTheFieldEmpty()
        {
            ActorObject actor = NewActor("Actor");

            actor.Display = null;

            Assert.IsNull(actor.Display);
        }

        /// <summary>Verifies that assigning a controller wires the actor onto the controller's master.</summary>
        [Test]
        public void Controller_AssignedFromNull_WiresTheActorOntoTheMaster()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput output = NewControllerOutput("Linear", withMaster: true);

            actor.Controller = output;

            Assert.AreEqual(output, actor.Controller);
            Assert.AreEqual(actor, output.master.actor);
        }

        /// <summary>Verifies that assigning a masterless controller output stores it without throwing.</summary>
        [Test]
        public void Controller_AssignedOutputWithoutMaster_StoresTheOutput()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput output = NewControllerOutput("Masterless", withMaster: false);

            actor.Controller = output;

            Assert.AreEqual(output, actor.Controller);
            Assert.IsNull(output.master);
        }

        /// <summary>Verifies that swapping controllers clears the outgoing master's actor reference.</summary>
        [Test]
        public void Controller_ReplacedWithAnotherOutput_ClearsThePreviousMasterReference()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput first = NewControllerOutput("FirstLinear", withMaster: true);
            ControllerOutput second = NewControllerOutput("SecondLinear", withMaster: true);
            actor.Controller = first;

            actor.Controller = second;

            Assert.IsNull(first.master.actor);
            Assert.AreEqual(actor, second.master.actor);
            Assert.AreEqual(second, actor.Controller);
        }

        /// <summary>Verifies that swapping away from a masterless output wires only the incoming master.</summary>
        [Test]
        public void Controller_ReplacedWhilePreviousOutputHasNoMaster_WiresOnlyTheIncomingMaster()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput first = NewControllerOutput("Masterless", withMaster: false);
            ControllerOutput second = NewControllerOutput("Linear", withMaster: true);
            actor.Controller = first;

            actor.Controller = second;

            Assert.AreEqual(actor, second.master.actor);
            Assert.AreEqual(second, actor.Controller);
        }

        /// <summary>Verifies that clearing the controller clears the previous master's actor reference.</summary>
        [Test]
        public void Controller_ClearedToNull_ClearsThePreviousMasterReference()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput output = NewControllerOutput("Linear", withMaster: true);
            actor.Controller = output;

            actor.Controller = null;

            Assert.IsNull(output.master.actor);
            Assert.IsNull(actor.Controller);
        }

        /// <summary>Verifies that re-assigning the same controller output skips the re-wiring work.</summary>
        [Test]
        public void Controller_AssignedSameInstanceTwice_SkipsTheRewiring()
        {
            ActorObject actor = NewActor("Actor");
            ControllerOutput output = NewControllerOutput("Linear", withMaster: true);
            actor.Controller = output;
            output.master.actor = null;

            actor.Controller = output;

            Assert.IsNull(output.master.actor);
        }

        /// <summary>Verifies that clearing an already empty controller slot leaves the field empty.</summary>
        [Test]
        public void Controller_AssignedNullWhileAlreadyNull_LeavesTheFieldEmpty()
        {
            ActorObject actor = NewActor("Actor");

            actor.Controller = null;

            Assert.IsNull(actor.Controller);
        }

        /// <summary>Verifies that SetModel instantiates one model child named after the prefab.</summary>
        [Test]
        public void SetModel_KnownModel_InstantiatesOneNamedModelChild()
        {
            ActorObject actor = NewActor("Actor");

            actor.SetModel("Rodent");

            Assert.AreEqual(1, actor.transform.childCount);
            Assert.AreEqual("Model Rodent", actor.transform.GetChild(0).name);
            Assert.AreEqual(actor.transform, actor.transform.GetChild(0).parent);
        }

        /// <summary>Verifies that SetModel moves the model onto the layer named after the actor.</summary>
        [Test]
        public void SetModel_ActorNameMatchesALayer_MovesTheModelOntoThatLayer()
        {
            ActorObject actor = NewActor("Actor");
            Assert.AreEqual(ActorLayerIndex, LayerMask.NameToLayer("Actor"));

            actor.SetModel("Rodent");

            Assert.AreEqual(ActorLayerIndex, actor.transform.GetChild(0).gameObject.layer);
        }

        /// <summary>Verifies that SetModel keeps the prefab layer when no layer matches the actor name.</summary>
        [Test]
        public void SetModel_ActorNameMatchesNoLayer_KeepsThePrefabLayer()
        {
            ActorObject actor = NewActor("ZZTestUnlayeredActor");
            Assert.AreEqual(-1, LayerMask.NameToLayer("ZZTestUnlayeredActor"));

            actor.SetModel("Rodent");

            Assert.AreEqual(0, actor.transform.GetChild(0).gameObject.layer);
        }

        /// <summary>Verifies that a second SetModel call replaces the model child instead of adding one.</summary>
        [Test]
        public void SetModel_CalledTwice_ReplacesThePreviousModelChild()
        {
            ActorObject actor = NewActor("Actor");
            actor.SetModel("Rodent");

            actor.SetModel("Rodent");

            Assert.AreEqual(1, actor.transform.childCount);
            Assert.AreEqual("Model Rodent", actor.transform.GetChild(0).name);
        }

        /// <summary>Verifies that the "None" model name removes every model child and adds nothing.</summary>
        [Test]
        public void SetModel_None_RemovesEveryModelChild()
        {
            ActorObject actor = NewActor("Actor");
            NewChild(actor, "Model Rodent");
            NewChild(actor, "Model Other");

            actor.SetModel("None");

            Assert.AreEqual(0, actor.transform.childCount);
        }

        /// <summary>Verifies that the model sweep matches the "Model " prefix case-sensitively.</summary>
        [Test]
        public void SetModel_None_PreservesChildrenWithoutTheExactModelPrefix()
        {
            ActorObject actor = NewActor("Actor");
            NewChild(actor, "Keep");
            NewChild(actor, "Model");
            NewChild(actor, "model Rodent");

            actor.SetModel("None");

            Assert.AreEqual(3, actor.transform.childCount);
        }

        /// <summary>Verifies that an unresolvable model name logs an error and adds no child.</summary>
        [Test]
        public void SetModel_UnknownModel_LogsAnErrorAndAddsNoChild()
        {
            ActorObject actor = NewActor("Actor");
            LogAssert.Expect(
                LogType.Error,
                new Regex("ActorObject\\.SetModel: model 'ZZMissingModel' not found under Resources/Actors/Prefabs\\.")
            );

            actor.SetModel("ZZMissingModel");

            Assert.AreEqual(0, actor.transform.childCount);
        }

        /// <summary>Verifies that an unresolvable model name still removes the previous model child.</summary>
        [Test]
        public void SetModel_UnknownModel_StillRemovesThePreviousModelChild()
        {
            ActorObject actor = NewActor("Actor");
            NewChild(actor, "Model Rodent");
            LogAssert.Expect(LogType.Error, new Regex("ActorObject\\.SetModel: model 'ZZMissingModel' not found"));

            actor.SetModel("ZZMissingModel");

            Assert.AreEqual(0, actor.transform.childCount);
        }

        /// <summary>Verifies that InitiateActor configures the character controller capsule exactly.</summary>
        [Test]
        public void InitiateActor_AnyActor_ConfiguresTheCharacterControllerCapsule()
        {
            EnsureActorsRoot();
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: false);

            CharacterController characterController = actor.GetComponent<CharacterController>();
            Assert.AreEqual(45f, characterController.slopeLimit);
            Assert.AreEqual(0.000001f, characterController.stepOffset);
            Assert.AreEqual(0.05f, characterController.skinWidth);
            Assert.AreEqual(0.001f, characterController.minMoveDistance);
            Assert.AreEqual(new Vector3(0f, 0.55f, 0f), characterController.center);
            Assert.AreEqual(0.5f, characterController.radius);
            Assert.AreEqual(0.1f, characterController.height);
        }

        /// <summary>Verifies that InitiateActor parents the actor under the scene's Actors root.</summary>
        [Test]
        public void InitiateActor_AnyActor_ParentsTheActorUnderTheActorsRoot()
        {
            EnsureActorsRoot();
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: false);

            Assert.IsNotNull(actor.transform.parent);
            Assert.AreEqual("Actors", actor.transform.parent.name);
        }

        /// <summary>Verifies that InitiateActor forwards its model name to the model swap.</summary>
        [Test]
        public void InitiateActor_NamedModel_AddsTheMatchingModelChild()
        {
            EnsureActorsRoot();
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("Rodent", trackCamera: false);

            Assert.AreEqual(1, actor.transform.childCount);
            Assert.AreEqual("Model Rodent", actor.transform.GetChild(0).name);
        }

        /// <summary>Verifies that InitiateActor creates no tracking camera when tracking is disabled.</summary>
        [Test]
        public void InitiateActor_TrackCameraDisabled_CreatesNoTrackingCamera()
        {
            EnsureActorsRoot();
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: false);

            Assert.IsNull(actor.transform.Find("Actor View"));
            Assert.AreEqual(0, actor.transform.childCount);
        }

        /// <summary>Verifies that InitiateActor builds the tracking camera with its fixed pose and flags.</summary>
        [Test]
        public void InitiateActor_TrackCameraEnabled_BuildsTheTrackingCameraChild()
        {
            EnsureActorsRoot();
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: true);

            Transform cameraTransform = actor.transform.Find("Actor View");
            Assert.IsNotNull(cameraTransform);
            Assert.AreEqual("TrackCam", cameraTransform.gameObject.tag);
            Assert.AreEqual(new Vector3(0f, 1f, -1.3f), cameraTransform.localPosition);
            Assert.AreEqual(20f, cameraTransform.eulerAngles.x, 0.001f);
            Assert.AreEqual(0f, cameraTransform.eulerAngles.y, 0.001f);
            Assert.AreEqual(0f, cameraTransform.eulerAngles.z, 0.001f);
            Camera trackingCamera = cameraTransform.GetComponent<Camera>();
            Assert.AreEqual(CameraClearFlags.Skybox, trackingCamera.clearFlags);
            Assert.AreEqual(Color.black, trackingCamera.backgroundColor);
        }

        /// <summary>Verifies that the tracking camera claims the lowest display no other camera uses.</summary>
        [Test]
        public void InitiateActor_TrackCameraEnabled_ClaimsTheLowestUnusedDisplay()
        {
            EnsureActorsRoot();
            OccupyTrackCameraDisplay(0);
            int expectedDisplay = LowestUnusedDisplay(UsedTrackCameraDisplays());
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: true);

            Camera trackingCamera = actor.transform.Find("Actor View").GetComponent<Camera>();
            Assert.AreEqual(expectedDisplay, trackingCamera.targetDisplay);
            Assert.AreNotEqual(0, trackingCamera.targetDisplay);
        }

        /// <summary>Verifies that the tracking camera falls back to display seven when all eight are used.</summary>
        [Test]
        public void InitiateActor_TrackCameraEnabledAndEveryDisplayUsed_FallsBackToDisplaySeven()
        {
            EnsureActorsRoot();
            for (int displayIndex = 0; displayIndex <= HighestDisplayIndex; displayIndex++)
            {
                OccupyTrackCameraDisplay(displayIndex);
            }
            ActorObject actor = NewActor("Actor");

            actor.InitiateActor("None", trackCamera: true);

            Camera trackingCamera = actor.transform.Find("Actor View").GetComponent<Camera>();
            Assert.AreEqual(7, trackingCamera.targetDisplay);
        }

        /// <summary>Creates a tracked GameObject that teardown destroys.</summary>
        /// <param name="objectName">The name assigned to the created object.</param>
        /// <returns>The created object.</returns>
        private GameObject NewObject(string objectName)
        {
            GameObject created = new GameObject(objectName);
            _createdObjects.Add(created);
            return created;
        }

        /// <summary>Creates a tracked actor GameObject carrying an ActorObject component.</summary>
        /// <param name="actorName">The name assigned to the actor object, which selects its render layer.</param>
        /// <returns>The created actor component.</returns>
        private ActorObject NewActor(string actorName)
        {
            return NewObject(actorName).AddComponent<ActorObject>();
        }

        /// <summary>Creates a tracked display GameObject carrying a DisplayObject component.</summary>
        /// <param name="displayName">The name assigned to the display object.</param>
        /// <returns>The created display component.</returns>
        private DisplayObject NewDisplay(string displayName)
        {
            return NewObject(displayName).AddComponent<DisplayObject>();
        }

        /// <summary>Creates a tracked controller output, optionally backed by a treadmill master.</summary>
        /// <param name="outputName">The name assigned to the controller object.</param>
        /// <param name="withMaster">Determines whether a LinearTreadmill master is attached and linked.</param>
        /// <returns>The created controller output.</returns>
        private ControllerOutput NewControllerOutput(string outputName, bool withMaster)
        {
            GameObject host = NewObject(outputName);
            ControllerOutput output = host.AddComponent<ControllerOutput>();
            if (withMaster)
            {
                output.master = host.AddComponent<LinearTreadmill>();
            }
            return output;
        }

        /// <summary>Creates a child object under an actor, tracked so teardown reaches it either way.</summary>
        /// <param name="actor">The actor receiving the child.</param>
        /// <param name="childName">The name assigned to the child object.</param>
        private void NewChild(ActorObject actor, string childName)
        {
            NewObject(childName).transform.SetParent(actor.transform);
        }

        /// <summary>Returns the scene's Actors root, creating a tracked one when the open scene lacks it.</summary>
        /// <returns>The Actors root object.</returns>
        private GameObject EnsureActorsRoot()
        {
            GameObject existing = GameObject.Find("Actors");
            return existing != null ? existing : NewObject("Actors");
        }

        /// <summary>Creates a tracked TrackCam-tagged camera claiming the specified display index.</summary>
        /// <param name="displayIndex">The display index the created camera renders to.</param>
        private void OccupyTrackCameraDisplay(int displayIndex)
        {
            GameObject host = NewObject($"ZZTestTrackCam{displayIndex}");
            host.tag = "TrackCam";
            host.AddComponent<Camera>().targetDisplay = displayIndex;
        }

        /// <summary>Returns the display indices every active TrackCam camera currently claims.</summary>
        /// <returns>The claimed display indices, which may repeat.</returns>
        private static List<int> UsedTrackCameraDisplays()
        {
            List<int> usedDisplays = new List<int>();
            foreach (GameObject tagged in GameObject.FindGameObjectsWithTag("TrackCam"))
            {
                if (tagged.TryGetComponent<Camera>(out Camera taggedCamera))
                {
                    usedDisplays.Add(taggedCamera.targetDisplay);
                }
            }
            return usedDisplays;
        }

        /// <summary>Returns the lowest display index not present in the claimed set, or seven when none is.</summary>
        /// <param name="usedDisplays">The claimed display indices.</param>
        /// <returns>The expected display index for the next tracking camera.</returns>
        private static int LowestUnusedDisplay(List<int> usedDisplays)
        {
            for (int displayIndex = 0; displayIndex <= HighestDisplayIndex; displayIndex++)
            {
                if (!usedDisplays.Contains(displayIndex))
                {
                    return displayIndex;
                }
            }
            return HighestDisplayIndex;
        }
    }
}
