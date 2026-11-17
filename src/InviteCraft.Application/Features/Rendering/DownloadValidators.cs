using FluentValidation;
using InviteCraft.Application.Common.Validation;

namespace InviteCraft.Application.Features.Rendering;

/// <summary>Shape-level checks. Whether the format/resolution is allowed is decided by <see cref="DownloadPolicy"/>.</summary>
internal sealed class CreateDownloadRequestValidator : AppValidator<CreateDownloadRequest>
{
    public CreateDownloadRequestValidator()
    {
        RuleFor(x => x.Format).IsInEnum().WithMessage("Choose JPG, PNG, PDF or PrintPdf.");
        RuleFor(x => x.Dpi).InclusiveBetween(72, 1200).When(x => x.Dpi.HasValue).WithMessage("Resolution must be between 72 and 1200 dpi.");
    }
}
