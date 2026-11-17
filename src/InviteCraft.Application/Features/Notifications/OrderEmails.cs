using System.Net;
using System.Text;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Identity;

namespace InviteCraft.Application.Features.Notifications;

/// <summary>Branded order emails (inline styles for email-client compatibility; the layout matches the auth emails).</summary>
internal static class OrderEmails
{
    public static EmailMessage Receipt(Order order, User user, string origin)
    {
        var intro = $"Thank you, {FirstName(user)}! Your payment of ₹{order.TotalAmount:0.00} is confirmed and your invitation is unlocked. "
                  + "You can download it in every format your package includes — as often as your package allows.";
        return Build(order, user, origin,
            subject: $"Your receipt · {order.OrderNumber} · InviteCraft",
            heading: "Payment received",
            intro: intro,
            footnote: "Prices include GST. This email is your receipt — a printable invoice is available on the order page.");
    }

    public static EmailMessage Refunded(Order order, User user, string origin)
    {
        var intro = $"Hi {FirstName(user)}, we have refunded ₹{order.TotalAmount:0.00} for this order to your original payment method. "
                  + "Depending on your bank it can take a few working days to appear. The invitation is locked again.";
        return Build(order, user, origin,
            subject: $"Your refund · {order.OrderNumber} · InviteCraft",
            heading: "Order refunded",
            intro: intro,
            footnote: "If you did not expect this refund, just reply to this email and we will sort it out.");
    }

    private static string FirstName(User user) =>
        user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? user.FullName;

    private static EmailMessage Build(Order order, User user, string origin, string subject, string heading, string intro, string footnote)
    {
        static string e(string value) => WebUtility.HtmlEncode(value);

        var rows = new StringBuilder();
        var text = new StringBuilder($"{heading}\n\n{intro}\n\nOrder {order.OrderNumber}\n");
        foreach (var item in order.Items)
        {
            rows.Append($"""
                <tr>
                  <td style="padding:8px 0;font-size:14px">{e(item.Invitation?.Title ?? item.TemplateName)}<br/>
                    <span style="font-size:12px;color:#6b6560">{e(item.TemplateName)} · {e(item.PackageName)}</span></td>
                  <td style="padding:8px 0;font-size:14px;text-align:right;white-space:nowrap">₹{item.LineTotal:0.00}</td>
                </tr>
                """);
            text.AppendLine($"- {item.Invitation?.Title ?? item.TemplateName} ({item.PackageName}): ₹{item.LineTotal:0.00}");
        }

        var discount = order.DiscountAmount > 0
            ? $"""<tr><td style="padding:2px 0;font-size:13px;color:#2f6b47">Coupon {e(order.Coupon?.Code ?? "")}</td><td style="padding:2px 0;font-size:13px;text-align:right;color:#2f6b47">− ₹{order.DiscountAmount:0.00}</td></tr>"""
            : string.Empty;

        var link = string.IsNullOrWhiteSpace(origin) ? null : $"{origin.TrimEnd('/')}/account/orders/{order.Id}";
        var button = link is null
            ? string.Empty
            : $"""<p style="margin:24px 0 0"><a href="{e(link)}" style="display:inline-block;background:#b8925a;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:999px;font-weight:600">View order &amp; invoice</a></p>""";

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
                      <p style="margin:0 0 4px;font-size:13px;color:#6b6560">Order</p>
                      <p style="margin:0 0 16px;font-size:16px;font-weight:600;color:#14213d">{e(order.OrderNumber)}</p>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid #e7ddcc;border-bottom:1px solid #e7ddcc">
                        {rows}
                        {discount}
                        <tr><td style="padding:10px 0 2px;font-size:15px;font-weight:700">Total</td><td style="padding:10px 0 2px;font-size:15px;font-weight:700;text-align:right">₹{order.TotalAmount:0.00}</td></tr>
                        <tr><td style="padding:0 0 10px;font-size:12px;color:#6b6560">Includes GST</td><td style="padding:0 0 10px;font-size:12px;color:#6b6560;text-align:right">₹{order.TaxAmount:0.00}</td></tr>
                      </table>
                      {button}
                      <p style="margin:24px 0 0;font-size:13px;color:#6b6560">{e(footnote)}</p>
                    </td></tr>
                  </table>
                  <p style="font-size:12px;color:#6b6560;margin-top:16px">InviteCraft · Invitations crafted with love</p>
                </td></tr>
              </table>
            </body></html>
            """;

        text.AppendLine($"\nTotal: ₹{order.TotalAmount:0.00} (includes ₹{order.TaxAmount:0.00} GST)");
        if (link is not null)
            text.AppendLine($"\nOrder & invoice: {link}");
        text.AppendLine($"\n{footnote}\n\n— InviteCraft");

        return new EmailMessage(user.Email, user.FullName, subject, html, text.ToString());
    }
}
