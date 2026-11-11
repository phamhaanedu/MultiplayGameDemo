using System.Runtime.CompilerServices;

// Ensures Game.dll always compiles cleanly and grants white-box visibility
// to EditMode and PlayMode NUnit test assemblies without breaking public encapsulation.
[assembly: InternalsVisibleTo("EditModeTests")]
[assembly: InternalsVisibleTo("PlayModeTests")]
