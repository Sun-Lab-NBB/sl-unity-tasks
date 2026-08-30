/// <summary>
/// Provides the ControllerObject base class for input handling.
///
/// Concrete subclasses drive the linked ActorObject's position, drawing movement either from a physical treadmill over
/// MQTT or from the keyboard.
/// </summary>
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>Represents the abstract base class for all input controllers.</summary>
    public abstract class ControllerObject : MonoBehaviour
    {
        /// <summary>The actor receiving input from this controller.</summary>
        public ActorObject actor;

        /// <summary>The buffer for accumulating movement input between frames.</summary>
        internal readonly ValueBuffer movement = new ValueBuffer();

#if UNITY_EDITOR
        /// <summary>Parents this controller under the scene's Controllers root and registers it for undo.</summary>
        public void InitiateController()
        {
            gameObject.transform.SetParent(GameObject.Find("Controllers").transform);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Controller");
        }
#endif

        /// <summary>Accumulates input values into a running total that callers drain once per frame.</summary>
        internal class ValueBuffer
        {
            /// <summary>The running total of the values added since the last clear.</summary>
            private float _accumulator;

            /// <summary>Adds a value to the running total.</summary>
            /// <param name="value">The input increment to fold into the running total.</param>
            public void Add(float value)
            {
                _accumulator += value;
            }

            /// <summary>Returns the total of the values added since the last clear.</summary>
            public float Sum()
            {
                return _accumulator;
            }

            /// <summary>Resets the running total to zero.</summary>
            public void Clear()
            {
                _accumulator = 0f;
            }
        }
    }
}
