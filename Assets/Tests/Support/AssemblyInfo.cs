/// <summary>
/// Grants the Edit Mode and Play Mode test assemblies access to the internal members of the support assembly.
/// </summary>
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sollertia.Tests.EditMode")]
[assembly: InternalsVisibleTo("Sollertia.Tests.PlayMode")]
