namespace DAGoldManager.Core;

// The gateway uses this only for an explicit server refusal, never for a timeout.
public sealed class CraftRegistrationRequiredException(string message) : InvalidOperationException(message);
