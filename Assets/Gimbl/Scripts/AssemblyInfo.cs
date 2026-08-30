/// <summary>
/// Grants the test assemblies access to the internal members of the Sollertia.Gimbl assembly.
/// </summary>
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sollertia.Tests.EditMode")]
[assembly: InternalsVisibleTo("Sollertia.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Sollertia.Tests.Support")]
