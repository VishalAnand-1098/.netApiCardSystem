using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Invitations;

public static class InvitationErrors
{
    /// <summary>Also returned for someone else's invitation, so ids cannot be probed.</summary>
    public static readonly Error NotFound = Error.NotFound("Invitation.NotFound", "Invitation not found.");

    public static readonly Error VersionConflict = Error.Conflict("Invitation.VersionConflict",
        "This invitation was changed in another tab or device. Reload it to continue.");

    public static readonly Error EditingClosed = Error.Forbidden("Invitation.EditingClosed",
        "The editing period for this invitation has ended. You can still download and share it.");

    public static Error LimitReached(int max) => Error.Conflict("Invitation.LimitReached",
        $"You have reached the limit of {max} invitations. Delete some drafts to create more.");

    public static readonly Error Unlocked = Error.Conflict("Invitation.Unlocked",
        "Purchased invitations cannot be deleted.");

    public static Error InvalidLanguage(string language) => Error.Validation("Invitation.InvalidLanguage",
        $"This design does not support the language \"{language}\".", "language");

    public static Error UnknownField(string key) => Error.Validation("Invitation.UnknownField",
        $"This design has no field called \"{key}\".", key);

    public static Error DuplicateField(string key) => Error.Validation("Invitation.DuplicateField",
        $"The field \"{key}\" was sent more than once.", key);

    public static Error FieldInvalid(string key, string message) => Error.Validation("Invitation.FieldInvalid", message, key);

    public static Error StyleNotAllowed(string key, string message) => Error.Validation("Invitation.StyleNotAllowed", message, key);

    public static Error MoveNotAllowed(string key, string message) => Error.Validation("Invitation.MoveNotAllowed", message, key);

    public static Error ImageNotFound(string key) => Error.Validation("Invitation.ImageNotFound",
        "That image was not found. Please upload it again.", key);

    public static readonly Error NotImageField = Error.Validation("Invitation.NotImageField",
        "This field does not accept images.", "fieldKey");
}
