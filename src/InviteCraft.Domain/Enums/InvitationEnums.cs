namespace InviteCraft.Domain.Enums;

public enum InvitationStatus
{
    Draft,
    Ready,
    Archived
}

/// <summary>
/// Private: owner only. LinkOnly: anyone with the unguessable share link (not indexed).
/// Public: anyone with the link, indexable, eligible for link previews.
/// </summary>
public enum InvitationVisibility
{
    Private,
    LinkOnly,
    Public
}

public enum DownloadFormat
{
    Jpg,
    Png,
    Pdf,
    PrintPdf
}

public enum DownloadStatus
{
    Queued,
    Rendering,
    Ready,
    Failed,
    Expired
}
