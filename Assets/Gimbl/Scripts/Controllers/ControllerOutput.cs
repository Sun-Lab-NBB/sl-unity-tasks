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
    /// Unity treats a serialized slot typed as a concrete subclass as incompatible once the underlying type changes.
    /// Funneling every controller through this single component type keeps a scene reference valid across a
    /// controller-type swap.
    /// </remarks>
    public class ControllerOutput : MonoBehaviour
    {
        /// <summary>The <see cref="ControllerObject"/> subclass driving this output.</summary>
        public ControllerObject master;
    }
}
