using System.Text.RegularExpressions;
using InviteCraft.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace InviteCraft.Infrastructure.Email;

public sealed class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" sends real email (Mailpit in dev, a provider in production). "Pickup" writes .html files locally.</summary>
    public string Mode { get; init; } = "Smtp";
    public string FromAddress { get; init; } = "no-reply@invitecraft.example";
    public string FromName { get; init; } = "InviteCraft";

    /// <summary>Folder for Pickup mode, relative to the API content root.</summary>
    public string PickupDirectory { get; init; } = "App_Data/emails";

    public SmtpSettings Smtp { get; init; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    /// <summary>MailKit SecureSocketOptions: None, Auto, StartTls, SslOnConnect.</summary>
    public string Security { get; init; } = "None";
    public string? Username { get; init; }
    public string? Password { get; init; }
}

internal sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Smtp.Host, settings.Smtp.Port,
            Enum.Parse<SecureSocketOptions>(settings.Smtp.Security, ignoreCase: true), cancellationToken);
        if (!string.IsNullOrEmpty(settings.Smtp.Username))
            await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Email '{Subject}' sent to {Recipient}", message.Subject, message.ToAddress);
    }
}

/// <summary>
/// Development sender that needs no mail server: saves each email as an .html file and logs the
/// first link it contains, so verification/reset flows can be tested by clicking the logged URL.
/// </summary>
internal sealed partial class PickupDirectoryEmailSender(
    IOptions<EmailSettings> options,
    IHostEnvironment environment,
    ILogger<PickupDirectoryEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.PickupDirectory));
        Directory.CreateDirectory(directory);

        var safeRecipient = InvalidFileChars().Replace(message.ToAddress, "_");
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{safeRecipient}.html");
        await File.WriteAllTextAsync(path, message.HtmlBody, cancellationToken);

        var link = FirstLink().Match(message.TextBody);
        logger.LogInformation(
            "DEV EMAIL to {Recipient} — '{Subject}' saved to {Path}. Link: {Link}",
            message.ToAddress, message.Subject, path, link.Success ? link.Value : "(none)");
    }

    [GeneratedRegex(@"[^a-zA-Z0-9@._-]")]
    private static partial Regex InvalidFileChars();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex FirstLink();
}
