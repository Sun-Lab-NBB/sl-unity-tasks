/// <summary>
/// Provides the IResettable interface implemented by zones that Task notifies at each corridor advance.
/// </summary>
namespace SL.Tasks
{
    /// <summary>
    /// Marks a zone component as resettable by <see cref="Task"/> at each lap boundary.
    /// </summary>
    /// <remarks>
    /// The interface lets <see cref="Task"/> drive every per-lap reset through a single typed loop instead of one
    /// loop per concrete zone class. Implementers are expected to be MonoBehaviours so scene-wide discovery via
    /// Unity's typed find helpers continues to work, and a new implementer joins the enumeration in
    /// <see cref="Task"/> so the corridor advance reaches it.
    /// </remarks>
    public interface IResettable
    {
        /// <summary>Resets the zone's per-lap state.</summary>
        void ResetState();
    }
}
