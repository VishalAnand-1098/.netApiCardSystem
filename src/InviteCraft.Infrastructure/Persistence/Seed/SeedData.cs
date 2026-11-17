using System.Security.Cryptography;
using System.Text;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Infrastructure.Persistence.Seed;

/// <summary>
/// Reference data inserted by migrations (EF Core HasData). Ids are deterministic so the
/// migration is stable and other seed rows can reference them.
/// Admin user (Phase 3, needs password hashing) and fonts (Phase 5, need font files) are seeded later.
/// Everything here is editable from the admin panel afterwards.
/// </summary>
internal static class SeedData
{
    /// <summary>Fixed timestamp so the model snapshot does not change on every build.</summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Name-based deterministic GUID (MD5, RFC 4122 version 3 layout).</summary>
    public static Guid Id(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("invitecraft:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }

    // ------------------------------------------------------------------ roles

    public static readonly Guid AdminRoleId = Id("role:admin");
    public static readonly Guid CustomerRoleId = Id("role:customer");

    public static object[] Roles =>
    [
        new { Id = AdminRoleId, Name = RoleNames.Admin, Description = "Full access to the admin panel." },
        new { Id = CustomerRoleId, Name = RoleNames.Customer, Description = "Creates, buys and shares invitations." },
    ];

    // ------------------------------------------------------------------ categories

    private sealed record CategorySeed(
        string Slug, string Name, CategoryKind Kind, string Icon, string Description, bool ShowOnHome, string[] SubCategories);

    private static readonly CategorySeed[] CategoryTree =
    [
        new("hindu-ceremonies", "Hindu Ceremonies", CategoryKind.Religion, "bi-flower1",
            "Traditional invitations for Hindu rituals and samskaras.", true,
            ["Wedding", "Engagement", "Haldi", "Mehndi", "Griha Pravesh", "Puja", "Annaprashan", "Naming Ceremony"]),
        new("muslim-ceremonies", "Muslim Ceremonies", CategoryKind.Religion, "bi-moon-stars",
            "Elegant invitations for Nikah, Walima and other Islamic occasions.", true,
            ["Nikah", "Walima", "Aqeeqah", "Eid"]),
        new("christian-ceremonies", "Christian Ceremonies", CategoryKind.Religion, "bi-brightness-high",
            "Graceful invitations for church weddings, sacraments and Christmas.", true,
            ["Wedding", "Baptism", "First Communion", "Christmas"]),
        new("wedding", "Wedding", CategoryKind.Occasion, "bi-heart",
            "Everything for the big day — from save the date to anniversary.", true,
            ["Save the Date", "Wedding Invitation", "Reception", "Anniversary"]),
        new("birthday", "Birthday", CategoryKind.Occasion, "bi-cake2",
            "Fun and stylish birthday invitations for every age.", true,
            ["Kids Birthday", "Adult Birthday", "Milestone Birthday"]),
        new("festivals", "Festivals", CategoryKind.Festival, "bi-stars",
            "Festive greetings and celebration invites.", true,
            ["Diwali", "Holi", "Eid", "Christmas", "Raksha Bandhan", "New Year"]),
        new("other", "Other Events", CategoryKind.Other, "bi-calendar-event",
            "Baby showers, graduations, housewarmings and corporate events.", false,
            ["Baby Shower", "Graduation", "Retirement", "Corporate Events", "Housewarming"]),
    ];

    public static Guid CategoryId(string slug) => Id("category:" + slug);
    public static Guid SubCategoryId(string categorySlug, string subSlug) => Id($"subcategory:{categorySlug}/{subSlug}");

    public static object[] Categories => CategoryTree
        .Select((c, index) => (object)new
        {
            Id = CategoryId(c.Slug),
            c.Name,
            c.Slug,
            c.Kind,
            c.Description,
            c.Icon,
            SortOrder = (index + 1) * 10,
            IsActive = true,
            c.ShowOnHome,
            SeoTitle = $"{c.Name} Invitation Cards | InviteCraft",
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
            IsDeleted = false,
        })
        .ToArray();

    public static object[] SubCategories => CategoryTree
        .SelectMany(c => c.SubCategories.Select((name, index) => (object)new
        {
            Id = SubCategoryId(c.Slug, Slugify(name)),
            CategoryId = CategoryId(c.Slug),
            Name = name,
            Slug = Slugify(name),
            SortOrder = (index + 1) * 10,
            IsActive = true,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
            IsDeleted = false,
        }))
        .ToArray();

    // ------------------------------------------------------------------ packages

    public static object[] Packages =>
    [
        Package(Domain.Entities.Templates.Package.FreeCode, "Free", 1, 0m,
            "For free templates — share digitally at no cost.",
            ["Jpg", "Png"], maxDpi: 150, maxDownloads: null, editWindowDays: null, customDesign: false, recommended: false,
            """["Free templates only","JPG & PNG downloads","Share via WhatsApp & link"]"""),
        Package(Domain.Entities.Templates.Package.BasicCode, "Basic", 2, 99m,
            "Clean, watermark-free digital invitation.",
            ["Jpg", "Png"], maxDpi: 150, maxDownloads: 5, editWindowDays: 7, customDesign: false, recommended: false,
            """["No watermark","HD JPG & PNG","5 downloads","Edit for 7 days"]"""),
        Package(Domain.Entities.Templates.Package.PremiumCode, "Premium", 3, 249m,
            "High-resolution files for sharing and home printing.",
            ["Jpg", "Png", "Pdf"], maxDpi: 300, maxDownloads: null, editWindowDays: 30, customDesign: false, recommended: true,
            """["300 DPI high resolution","JPG, PNG & PDF","Unlimited downloads","Edit for 30 days","Shareable invitation page"]"""),
        Package(Domain.Entities.Templates.Package.LuxuryCode, "Luxury", 4, 499m,
            "Print-shop ready with bleed and crop marks.",
            ["Jpg", "Png", "Pdf", "PrintPdf"], maxDpi: 300, maxDownloads: null, editWindowDays: 90, customDesign: false, recommended: false,
            """["Everything in Premium","Print-ready PDF with bleed & crop marks","Edit for 90 days","Priority support"]"""),
        Package(Domain.Entities.Templates.Package.CustomCode, "Custom Design", 5, 1999m,
            "A designer crafts a bespoke invitation for you.",
            ["Jpg", "Png", "Pdf", "PrintPdf"], maxDpi: 300, maxDownloads: null, editWindowDays: 180, customDesign: true, recommended: false,
            """["Personal designer","Bespoke layout & artwork","Two revision rounds","All formats incl. print-ready PDF"]"""),
    ];

    public static Guid PackageId(string code) => Id("package:" + code);

    private static object Package(
        string code, string name, int sortOrder, decimal price, string description, string[] formats,
        int maxDpi, int? maxDownloads, int? editWindowDays, bool customDesign, bool recommended, string features) => new
        {
            Id = PackageId(code),
            Code = code,
            Name = name,
            Description = description,
            Features = features,
            BasePrice = price,
            AllowedFormats = formats,
            MaxDpi = maxDpi,
            MaxDownloads = maxDownloads,
            RemovesWatermark = true,
            EditWindowDays = editWindowDays,
            IncludesCustomDesign = customDesign,
            IsRecommended = recommended,
            SortOrder = sortOrder * 10,
            IsActive = true,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
        };

    // ------------------------------------------------------------------ site settings

    public static object[] SiteSettings =>
    [
        Setting("tax.gst", """{"rate":18,"pricesIncludeTax":true}""", "GST applied to orders. Prices shown to customers include tax."),
        Setting("watermark", """{"text":"InviteCraft · Preview","opacity":0.18}""", "Watermark drawn on locked premium previews."),
        Setting("orders", """{"pendingExpiryMinutes":30}""", "Unpaid orders expire after this many minutes."),
        Setting("downloads", """{"signedUrlMinutes":15}""", "Lifetime of signed download links."),
        Setting("contact", """{"email":"support@invitecraft.example","phone":null,"whatsapp":null}""", "Public contact details shown in the footer and Contact page."),
    ];

    private static object Setting(string key, string json, string description) =>
        new { Key = key, Value = json, Description = description, UpdatedAt = SeededAt };

    // ------------------------------------------------------------------ helpers

    public static string Slugify(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
