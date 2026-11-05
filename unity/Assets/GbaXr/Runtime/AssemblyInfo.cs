using System.Runtime.CompilerServices;

// Keep the native ABI surface internal to the renderer while allowing its
// contract to be verified from the dedicated Editor-only test assembly.
[assembly: InternalsVisibleTo("GbaXr.EditModeTests")]
