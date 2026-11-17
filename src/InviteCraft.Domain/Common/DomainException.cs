namespace InviteCraft.Domain.Common;

/// <summary>
/// Thrown when a domain invariant would be violated (e.g. marking an already-paid order as paid).
/// Translated to HTTP 422 by the API's global exception handler.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
