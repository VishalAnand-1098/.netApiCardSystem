using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Rendering;

public static class DownloadErrors
{
    /// <summary>Also returned for someone else's download, so ids cannot be probed.</summary>
    public static readonly Error NotFound = Error.NotFound("Download.NotFound", "Download not found.");

    public static readonly Error NotReady = Error.Validation("Download.InvitationIncomplete",
        "Fill in all required details before downloading.");

    public static readonly Error PaymentRequired = Error.Forbidden("Download.PaymentRequired",
        "Pay for this invitation before downloading. No file is available until the payment is confirmed.");

    public static readonly Error LinkInvalid = Error.NotFound("Download.LinkInvalid",
        "This download link is invalid or has expired. Open your downloads to get a new one.");

    public static readonly Error Expired = Error.NotFound("Download.Expired",
        "This file has expired. Create it again from your invitation.");

    public static readonly Error FileMissing = Error.NotFound("Download.FileMissing",
        "The file is no longer available. Create it again from your invitation.");

    public static Error FormatNotAvailable(DownloadFormat format, string reason) =>
        Error.Forbidden("Download.FormatNotAvailable", reason);

    public static Error QualityNotAvailable(int dpi, int maxDpi) =>
        Error.Validation("Download.QualityNotAvailable",
            maxDpi > 0 ? $"{dpi} dpi is not available for this invitation (up to {maxDpi} dpi)." : $"{dpi} dpi is not available.",
            nameof(CreateDownloadRequest.Dpi));

    public static Error AllowanceUsed(int max) => Error.Forbidden("Download.AllowanceUsed",
        $"You have used all {max} downloads included in your package.");

    public static readonly Error TooManyInProgress = Error.Conflict("Download.TooManyInProgress",
        "You already have downloads being prepared. Please wait for them to finish.");

    public static readonly Error RenderUnavailable = Error.Failure("Download.RenderUnavailable",
        "Preparing files is temporarily unavailable. Please try again in a few minutes.");
}
