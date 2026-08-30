/// <summary>
/// Provides the IResettable interface implemented by zones that Task notifies at each corridor advance.
/// </summary>
namespace SL.Tasks
{
    /// <summary>Marks a zone component as resettable by <see cref="Task"/> at each lap boundary.</summary>
    /// <remarks>
    /// Implementers are expected to be MonoBehaviours, so Unity's typed find helpers discover them scene-wide.
    /// </remarks>
    public interface IResettable
    {
        /// <summary>Resets the zone's per-lap state.</summary>
        void ResetState();
    }
}
