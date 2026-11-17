namespace InviteCraft.Domain.Enums;

public enum TokenPurpose
{
    EmailVerification,
    PasswordReset
}

public enum NotificationChannel
{
    InApp,
    Email,
    WhatsApp
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}
