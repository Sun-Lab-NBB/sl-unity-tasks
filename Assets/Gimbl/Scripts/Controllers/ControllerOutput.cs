/// <summary>
/// Provides the ControllerOutput class for linking controllers to actors in the scene.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Holds a typed reference to the active <see cref="ControllerObject"/> so controller-type swaps do not
    /// invalidate the serialized scene reference.
    /// </summary>
    /// <remarks>
    /// The <see cref="ActorObject.Controller"/> property (whose serialized backing field is the Inspector slot) is
    /// typed as <see cref="ControllerOutput"/> rather than the concrete <see cref="ControllerObject"/> subclass so that
    /// swapping controller types (LinearTreadmill ↔ SimulatedLinearTreadmill, or any future subclass) does not
    /// invalidate the scene's serialized reference. Unity treats subclass slots as incompatible when the underlying
    /// type changes. The <see cref="master"/> indirection erases that distinction by funneling every controller through
    /// a single stable component type.
    /// </remarks>
    public class ControllerOutput : MonoBehaviour
    {
        /// <summary>The <see cref="ControllerObject"/> subclass driving this output.</summary>
        public ControllerObject master;
    }
}
