namespace InviteCraft.Domain.Enums;

/// <summary>Drives "Browse by occasion" vs "Browse by religion/tradition" vs festival sections.</summary>
public enum CategoryKind
{
    Religion,
    Occasion,
    Festival,
    Other
}

public enum TemplateTier
{
    Free,
    Premium
}

public enum TemplateOrientation
{
    Portrait,
    Landscape,
    Square
}

public enum TemplateStatus
{
    Draft,
    Published,
    Archived
}

public enum TemplateAssetType
{
    Thumbnail,
    Preview,
    Background,
    Decoration,
    Sample
}

/// <summary>Originals stay private; Web/Thumb variants are what browsers receive.</summary>
public enum AssetVariant
{
    Original,
    Web,
    Thumb
}

public enum EditableFieldType
{
    Text,
    MultilineText,
    Date,
    Time,
    Phone,
    Email,
    Number,
    Select,
    Image
}

public enum FontRole
{
    Heading,
    Body,
    Accent,
    Script
}

public enum BannerPlacement
{
    HomeHero,
    HomeStrip,
    CategoryTop
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}
