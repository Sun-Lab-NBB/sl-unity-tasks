/// <summary>
/// Verifies the behavior of the controller classes: ControllerObject and its ValueBuffer, ControllerOutput,
/// ControllerTypes, LinearTreadmill, and SimulatedLinearTreadmill.
///
/// Every lifecycle callback is invoked directly, because Edit Mode runs no player loop. The simulated treadmill's
/// keyboard action map is detached from its component before teardown, so the Input System asset is never disposed
/// through UnityEngine.Object.Destroy, which is illegal outside Play Mode.
/// </summary>
using System;
using System.Collections.Generic;
using Gimbl;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the controller classes.</summary>
    [TestFixture]
    public class ControllerTests
    {
        /// <summary>The MQTT harness hosting the client singleton and capturing every published payload.</summary>
        private MqttTestHarness _mqtt;

        /// <summary>The GameObjects the running test created, destroyed during teardown.</summary>
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        /// <summary>Installs a fresh MQTT client singleton before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _mqtt = MqttTestHarness.Create();
        }

        /// <summary>Detaches every simulated input map, then destroys the test objects and the singleton.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject created in _createdObjects)
            {
                if (created == null)
                {
                    continue;
                }
                foreach (SimulatedLinearTreadmill treadmill in created.GetComponents<SimulatedLinearTreadmill>())
                {
                    DetachSimulatedInput(treadmill);
                }
            }

            Undo.ClearAll();
            foreach (GameObject created in _createdObjects)
            {
                if (created != null)
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }
            _createdObjects.Clear();
            _mqtt.Dispose();
        }

        /// <summary>Verifies that a fresh value buffer reports a zero running total.</summary>
        [Test]
        public void Sum_FreshValueBuffer_ReturnsZero()
        {
            ControllerObject.ValueBuffer buffer = new ControllerObject.ValueBuffer();

            Assert.AreEqual(0f, buffer.Sum());
        }

        /// <summary>Verifies that the value buffer accumulates positive and negative values into one total.</summary>
        [Test]
        public void Add_SeveralValues_AccumulatesTheRunningTotal()
        {
            ControllerObject.ValueBuffer buffer = new ControllerObject.ValueBuffer();

            buffer.Add(1.5f);
            buffer.Add(-0.5f);
            buffer.Add(2f);

            Assert.AreEqual(3f, buffer.Sum());
        }

        /// <summary>Verifies that clearing the value buffer resets the running total to zero.</summary>
        [Test]
        public void Clear_BufferHoldingValues_ResetsTheRunningTotal()
        {
            ControllerObject.ValueBuffer buffer = new ControllerObject.ValueBuffer();
            buffer.Add(4.25f);

            buffer.Clear();

            Assert.AreEqual(0f, buffer.Sum());
        }

        /// <summary>Verifies that values added after a clear accumulate from zero rather than the old total.</summary>
        [Test]
        public void Add_AfterClear_AccumulatesFromZero()
        {
            ControllerObject.ValueBuffer buffer = new ControllerObject.ValueBuffer();
            buffer.Add(4.25f);
            buffer.Clear();

            buffer.Add(0.75f);

            Assert.AreEqual(0.75f, buffer.Sum());
        }

        /// <summary>Verifies that a newly created controller carries no actor reference.</summary>
        [Test]
        public void Actor_FreshController_DefaultsToNull()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            Assert.IsNull(treadmill.actor);
        }

        /// <summary>Verifies that a newly created controller exposes an empty movement buffer.</summary>
        [Test]
        public void Movement_FreshController_StartsAtZero()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            Assert.IsNotNull(treadmill.movement);
            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that InitiateController parents the controller under the Controllers root.</summary>
        [Test]
        public void InitiateController_ControllersRootPresent_ParentsTheControllerUnderTheRoot()
        {
            EnsureControllersRoot();
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            treadmill.InitiateController();

            Assert.IsNotNull(treadmill.transform.parent);
            Assert.AreEqual("Controllers", treadmill.transform.parent.name);
        }

        /// <summary>Verifies that a newly created controller output carries no master reference.</summary>
        [Test]
        public void Master_FreshControllerOutput_DefaultsToNull()
        {
            ControllerOutput output = NewObject("Linear").AddComponent<ControllerOutput>();

            Assert.IsNull(output.master);
        }

        /// <summary>Verifies that a controller output exposes the controller assigned to its master slot.</summary>
        [Test]
        public void Master_AssignedController_ExposesTheAssignedController()
        {
            GameObject host = NewObject("Linear");
            ControllerOutput output = host.AddComponent<ControllerOutput>();
            LinearTreadmill treadmill = host.AddComponent<LinearTreadmill>();

            output.master = treadmill;

            Assert.AreEqual(treadmill, output.master);
        }

        /// <summary>Verifies that the controller type ordinals match the order the enum declares them in.</summary>
        [Test]
        public void ControllerTypes_MemberOrdinals_MatchTheDeclaredOrder()
        {
            Assert.AreEqual(0, (int)ControllerTypes.LinearTreadmill);
            Assert.AreEqual(1, (int)ControllerTypes.SimulatedLinearTreadmill);
        }

        /// <summary>Verifies that the controller type enumeration holds exactly the two declared members.</summary>
        [Test]
        public void ControllerTypes_MemberSet_HoldsExactlyTwoNamesInDeclarationOrder()
        {
            string[] memberNames = Enum.GetNames(typeof(ControllerTypes));

            Assert.AreEqual(2, memberNames.Length);
            Assert.AreEqual("LinearTreadmill", memberNames[0]);
            Assert.AreEqual("SimulatedLinearTreadmill", memberNames[1]);
        }

        /// <summary>Verifies that every controller type name resolves to a ControllerObject subclass.</summary>
        [Test]
        public void ControllerTypes_EveryMemberName_ResolvesToAControllerObjectSubclass()
        {
            foreach (string memberName in Enum.GetNames(typeof(ControllerTypes)))
            {
                Type resolvedType = typeof(ControllerObject).Assembly.GetType($"Gimbl.{memberName}");

                Assert.IsNotNull(resolvedType, $"Gimbl.{memberName} must exist for the controller spec table.");
                Assert.IsTrue(resolvedType.IsSubclassOf(typeof(ControllerObject)));
            }
        }

        /// <summary>Verifies that the hardware treadmill subscribes to the Motion topic on start.</summary>
        [Test]
        public void Start_HardwareTreadmill_SubscribesToTheMotionTopic()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            PrivateAccess.Invoke(treadmill, "Start");

            MQTTChannel dataChannel = PrivateAccess.GetField<MQTTChannel>(treadmill, "_dataChannel");
            Assert.IsNotNull(dataChannel);
            Assert.AreEqual("Motion", dataChannel.topic);
            Assert.AreEqual(MQTTTopics.Motion, dataChannel.topic);
        }

        /// <summary>Verifies that a Motion message received after start accumulates its movement value.</summary>
        [Test]
        public void OnMessage_MotionMessageReceived_AccumulatesTheMovementValue()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            PublishMotion(2.5f);

            Assert.AreEqual(2.5f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that consecutive Motion messages accumulate into a single running total.</summary>
        [Test]
        public void OnMessage_ConsecutiveMotionMessages_AccumulateIntoOneTotal()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            PublishMotion(1.5f);
            PublishMotion(2.25f);

            Assert.AreEqual(3.75f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that a directly delivered message adds its movement value to the buffer.</summary>
        [Test]
        public void OnMessage_NegativeMovement_AddsTheNegativeValue()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            treadmill.OnMessage(new LinearTreadmill.TreadmillMessage { movement = -1.25f });

            Assert.AreEqual(-1.25f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that processing movement advances the actor along Z and leaves X and Y alone.</summary>
        [Test]
        public void ProcessMovement_ActorAssigned_AdvancesTheActorAlongZOnly()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            ActorObject actor = NewActor("Actor", new Vector3(1f, 2f, 3f));
            treadmill.actor = actor;
            treadmill.movement.Add(2.5f);

            treadmill.ProcessMovement();

            Assert.AreEqual(new Vector3(1f, 2f, 5.5f), actor.transform.position);
        }

        /// <summary>Verifies that a negative movement total moves the actor backward along Z.</summary>
        [Test]
        public void ProcessMovement_NegativeMovementTotal_MovesTheActorBackwardAlongZ()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            ActorObject actor = NewActor("Actor", new Vector3(0f, 0f, 3f));
            treadmill.actor = actor;
            treadmill.movement.Add(-1.25f);

            treadmill.ProcessMovement();

            Assert.AreEqual(1.75f, actor.transform.position.z);
        }

        /// <summary>Verifies that processing movement drains the buffer once it has been applied.</summary>
        [Test]
        public void ProcessMovement_ActorAssigned_DrainsTheMovementBuffer()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            treadmill.actor = NewActor("Actor", Vector3.zero);
            treadmill.movement.Add(2.5f);

            treadmill.ProcessMovement();

            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that a second processing pass does not re-apply an already consumed total.</summary>
        [Test]
        public void ProcessMovement_CalledTwiceForOneMessage_MovesTheActorOnce()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            ActorObject actor = NewActor("Actor", Vector3.zero);
            treadmill.actor = actor;
            treadmill.movement.Add(2.5f);

            treadmill.ProcessMovement();
            treadmill.ProcessMovement();

            Assert.AreEqual(2.5f, actor.transform.position.z);
        }

        /// <summary>Verifies that processing movement without an actor still drains the buffer.</summary>
        [Test]
        public void ProcessMovement_NoActorAssigned_DrainsTheBufferWithoutThrowing()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            treadmill.movement.Add(5f);

            Assert.DoesNotThrow(() => treadmill.ProcessMovement());

            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that the hardware treadmill's frame update applies the accumulated movement.</summary>
        [Test]
        public void Update_HardwareTreadmill_AppliesTheAccumulatedMovement()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            ActorObject actor = NewActor("Actor", new Vector3(0f, 0f, 1f));
            treadmill.actor = actor;
            treadmill.movement.Add(3f);

            treadmill.Update();

            Assert.AreEqual(4f, actor.transform.position.z);
        }

        /// <summary>Verifies that destroying a started treadmill stops it from accumulating Motion messages.</summary>
        [Test]
        public void OnDestroy_StartedTreadmill_StopsAccumulatingMotionMessages()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            PrivateAccess.Invoke(treadmill, "OnDestroy");
            PublishMotion(4f);

            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that destroying a treadmill that never started leaves the channel untouched.</summary>
        [Test]
        public void OnDestroy_TreadmillThatNeverStarted_DoesNotThrow()
        {
            LinearTreadmill treadmill = NewController<LinearTreadmill>("Linear");

            Assert.DoesNotThrow(() => PrivateAccess.Invoke(treadmill, "OnDestroy"));
        }

        /// <summary>Verifies that the simulated treadmill opens the Interaction channel on start.</summary>
        [Test]
        public void Start_SimulatedTreadmill_OpensTheInteractionChannel()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");

            PrivateAccess.Invoke(treadmill, "Start");

            MQTTChannel interactionChannel = PrivateAccess.GetField<MQTTChannel>(treadmill, "_interactionTrigger");
            Assert.IsNotNull(interactionChannel);
            Assert.AreEqual("Interaction", interactionChannel.topic);
            Assert.AreEqual(MQTTTopics.Interaction, interactionChannel.topic);
        }

        /// <summary>Verifies that the simulated treadmill leaves the hardware Motion subscription unopened.</summary>
        [Test]
        public void Start_SimulatedTreadmill_LeavesTheMotionSubscriptionUnopened()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");

            PrivateAccess.Invoke(treadmill, "Start");

            Assert.IsNull(PrivateAccess.GetField<MQTTChannel>(treadmill, "_dataChannel"));
        }

        /// <summary>Verifies that the simulated treadmill ignores movement published on the Motion topic.</summary>
        [Test]
        public void Start_SimulatedTreadmill_IgnoresMotionMessages()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            PublishMotion(3f);

            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that reading an unactuated keyboard adds no movement to the buffer.</summary>
        [Test]
        public void GetSimulatedInput_UnactuatedKeyboard_AddsNoMovement()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            treadmill.GetSimulatedInput();

            Assert.AreEqual(0f, treadmill.movement.Sum());
        }

        /// <summary>Verifies that the simulated frame update publishes no interaction without an actor.</summary>
        [Test]
        public void Update_SimulatedTreadmillWithoutActor_PublishesNoInteraction()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");
            PrivateAccess.Invoke(treadmill, "Start");

            treadmill.Update();

            Assert.AreEqual(0, _mqtt.CountOn(MQTTTopics.Interaction));
        }

        /// <summary>Verifies that the simulated frame update applies the buffered movement to the actor.</summary>
        [Test]
        public void Update_SimulatedTreadmillWithActor_AppliesTheBufferedMovement()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");
            PrivateAccess.Invoke(treadmill, "Start");
            ActorObject actor = NewActor("Actor", new Vector3(0f, 0f, 1f));
            treadmill.actor = actor;
            treadmill.movement.Add(4f);

            treadmill.Update();

            Assert.AreEqual(5f, actor.transform.position.z);
        }

        /// <summary>Verifies that the simulated interaction trigger publishes an empty Interaction payload.</summary>
        [Test]
        public void InteractionTrigger_Sent_PublishesOneEmptyPayloadOnTheInteractionTopic()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");
            PrivateAccess.Invoke(treadmill, "Start");
            MQTTChannel interactionChannel = PrivateAccess.GetField<MQTTChannel>(treadmill, "_interactionTrigger");

            interactionChannel.Send();

            Assert.AreEqual(1, _mqtt.CountOn(MQTTTopics.Interaction));
            Assert.AreEqual(string.Empty, _mqtt.LastPayloadOn(MQTTTopics.Interaction));
        }

        /// <summary>Verifies that destroying a simulated treadmill that never started leaves the input alone.
        /// </summary>
        [Test]
        public void OnDestroy_SimulatedTreadmillThatNeverStarted_DoesNotThrow()
        {
            SimulatedLinearTreadmill treadmill = NewController<SimulatedLinearTreadmill>("Simulated Linear");

            Assert.DoesNotThrow(() => PrivateAccess.Invoke(treadmill, "OnDestroy"));
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

        /// <summary>Creates a tracked GameObject carrying the requested controller component.</summary>
        /// <typeparam name="TController">The controller component type to attach.</typeparam>
        /// <param name="controllerName">The name assigned to the controller object.</param>
        /// <returns>The attached controller component.</returns>
        private TController NewController<TController>(string controllerName)
            where TController : ControllerObject
        {
            return NewObject(controllerName).AddComponent<TController>();
        }

        /// <summary>Creates a tracked actor GameObject positioned at the requested world position.</summary>
        /// <param name="actorName">The name assigned to the actor object.</param>
        /// <param name="position">The world position the actor starts at.</param>
        /// <returns>The created actor component.</returns>
        private ActorObject NewActor(string actorName, Vector3 position)
        {
            GameObject host = NewObject(actorName);
            host.transform.position = position;
            return host.AddComponent<ActorObject>();
        }

        /// <summary>Returns the scene's Controllers root, creating a tracked one when the scene lacks it.</summary>
        /// <returns>The Controllers root object.</returns>
        private GameObject EnsureControllersRoot()
        {
            GameObject existing = GameObject.Find("Controllers");
            return existing != null ? existing : NewObject("Controllers");
        }

        /// <summary>Publishes a treadmill movement value on the Motion topic through the production path.</summary>
        /// <param name="movement">The movement value carried by the published message.</param>
        private void PublishMotion(float movement)
        {
            _mqtt.Publish(MQTTTopics.Motion, new LinearTreadmill.TreadmillMessage { movement = movement });
        }

        /// <summary>Disables and detaches the simulated action map so teardown never disposes the asset.</summary>
        /// <remarks>
        /// SimulatedLinearTreadmill.OnDestroy disposes the generated action map, which destroys the backing
        /// InputActionAsset through UnityEngine.Object.Destroy. That call is rejected outside Play Mode, so the
        /// field is emptied first and Unity's teardown callback takes the null branch instead.
        /// </remarks>
        /// <param name="treadmill">The simulated treadmill whose action map is released.</param>
        private static void DetachSimulatedInput(SimulatedLinearTreadmill treadmill)
        {
            object simulatedInput = PrivateAccess.GetField<object>(treadmill, "_input");
            if (simulatedInput != null)
            {
                PrivateAccess.Invoke(simulatedInput, "Disable");
            }
            PrivateAccess.SetField(treadmill, "_input", null);
        }
    }
}
