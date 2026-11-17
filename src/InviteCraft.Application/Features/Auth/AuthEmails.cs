using System.Net;
using InviteCraft.Application.Common.Interfaces;

namespace InviteCraft.Application.Features.Auth;

/// <summary>Branded transactional emails for the auth flows (inline styles for email-client compatibility).</summary>
internal static class AuthEmails
{
    public static EmailMessage Verification(string toAddress, string toName, string link, int validHours) => Build(
        toAddress, toName,
        subject: "Verify your email · InviteCraft",
        heading: "Confirm your email address",
        intro: $"Welcome to InviteCraft, {toName}! Please confirm your email address so we can send you order receipts and invitation updates.",
        buttonText: "Verify email",
        link: link,
        footnote: $"This link expires in {validHours} hours. If you did not create an account, you can ignore this email.");

    public static EmailMessage PasswordReset(string toAddress, string toName, string link, int validMinutes) => Build(
        toAddress, toName,
        subject: "Reset your password · InviteCraft",
        heading: "Reset your password",
        intro: $"Hi {toName}, we received a request to reset the password for your InviteCraft account.",
        buttonText: "Choose a new password",
        link: link,
        footnote: $"This link expires in {validMinutes} minutes and can be used once. If you did not request a reset, no action is needed — your password is unchanged.");

    private static EmailMessage Build(
        string toAddress, string toName, string subject, string heading, string intro, string buttonText, string link, string footnote)
    {
        static string e(string value) => WebUtility.HtmlEncode(value);
        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:0;background:#fbf7f0;font-family:Inter,Segoe UI,Arial,sans-serif;color:#1e1b18">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#fbf7f0;padding:32px 12px">
                <tr><td align="center">
                  <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid #e7ddcc;border-radius:16px;overflow:hidden">
                    <tr><td style="background:#14213d;padding:24px 32px;font-family:Georgia,'Times New Roman',serif;font-size:24px;color:#ffffff">
                      Invite<em style="color:#b8925a">Craft</em>
                    </td></tr>
                    <tr><td style="padding:32px">
                      <h1 style="margin:0 0 16px;font-family:Georgia,'Times New Roman',serif;font-weight:600;font-size:26px;color:#14213d">{e(heading)}</h1>
                      <p style="margin:0 0 24px;font-size:15px;line-height:1.6">{e(intro)}</p>
                      <p style="margin:0 0 24px"><a href="{e(link)}" style="display:inline-block;background:#b8925a;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:999px;font-weight:600">{e(buttonText)}</a></p>
                      <p style="margin:0 0 8px;font-size:13px;color:#6b6560">Or paste this link into your browser:</p>
                      <p style="margin:0 0 24px;font-size:13px;word-break:break-all"><a href="{e(link)}" style="color:#8c6a3a">{e(link)}</a></p>
                      <p style="margin:0;font-size:13px;color:#6b6560">{e(footnote)}</p>
                    </td></tr>
                  </table>
                  <p style="font-size:12px;color:#6b6560;margin-top:16px">InviteCraft · Invitations crafted with love</p>
                </td></tr>
              </table>
            </body></html>
            """;

        var text = $"{heading}\n\n{intro}\n\n{buttonText}: {link}\n\n{footnote}\n\n— InviteCraft";
        return new EmailMessage(toAddress, toName, subject, html, text);
    }
}
