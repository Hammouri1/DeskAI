namespace DeskAI.Core.Appearance;

/// <summary>Whether DeskAI's window follows Windows, or is always light or always dark.</summary>
public enum ThemeMode
{
    FollowWindows,
    Light,
    Dark,
}

/// <summary>
/// The neutral colours a look may change, as "#RRGGBB" text.
/// </summary>
/// <remarks>
/// Deliberately only the ground, the surfaces, and the lines. A look never carries an accent,
/// caution, or danger colour: "green means safe or confirmed" is the one visual rule that has
/// to hold on every screen in every look, so those colours are not a look's to change. A test
/// asserts this record has no such property.
/// </remarks>
public sealed record LookPalette(string Ground, string Surface, string SurfaceRaised, string Line, string LineStrong)
{
    public IEnumerable<string> All => [Ground, Surface, SurfaceRaised, Line, LineStrong];
}

/// <summary>One of DeskAI's looks: a name, a line about it, and its dark and light colours.</summary>
public sealed record DeskLook(string Id, string Name, string Summary, LookPalette Dark, LookPalette Light);

/// <summary>
/// The looks DeskAI offers, fixed in code.
/// </summary>
/// <remarks>
/// "Slate" is the look DeskAI has had since its visual system was drawn, so it is the default
/// and the one high contrast falls back from. Each look is a tint of the neutral surfaces; the
/// text colours are shared, and a test checks each look keeps them readable.
/// </remarks>
public static class DeskLookCatalog
{
    public const string DefaultId = "slate";

    public static IReadOnlyList<DeskLook> All { get; } =
    [
        new("slate", "Slate", "Cool blue-grey. DeskAI's original look.",
            new LookPalette("#0F1216", "#161B21", "#1D242C", "#272E38", "#38414D"),
            new LookPalette("#F6F7F9", "#FFFFFF", "#FFFFFF", "#E1E5EA", "#C7CED6")),
        new("graphite", "Graphite", "Plain grey, nothing tinted.",
            new LookPalette("#111111", "#191919", "#212121", "#2C2C2C", "#3A3A3A"),
            new LookPalette("#F4F4F4", "#FFFFFF", "#FFFFFF", "#E0E0E0", "#C8C8C8")),
        new("sand", "Sand", "Warm and soft, like paper.",
            new LookPalette("#151210", "#1E1A16", "#26211C", "#332C25", "#463D34"),
            new LookPalette("#F8F4EC", "#FFFDF8", "#FFFDF8", "#E8E0D2", "#D2C6B2")),
        new("ocean", "Ocean", "Deep blue, calm and dark.",
            new LookPalette("#0B1620", "#10202D", "#16293A", "#1F3648", "#2C4A61"),
            new LookPalette("#EEF5F9", "#FFFFFF", "#FFFFFF", "#D6E4EE", "#B8CEDD")),
        new("lavender", "Lavender", "A little purple. Soft and relaxed.",
            new LookPalette("#14111C", "#1C1826", "#262031", "#362D45", "#514261"),
            new LookPalette("#F5F0FA", "#FFFCFF", "#FFFCFF", "#E4DAEF", "#CCBDDC")),
        new("rose", "Rose", "A warm blush, without the brightness.",
            new LookPalette("#191215", "#221A1E", "#2D2227", "#403039", "#59444E"),
            new LookPalette("#FCF1F3", "#FFFCFC", "#FFFCFC", "#EDDBE0", "#D8BDC5")),
    ];

    public static DeskLook Default => All[0];

    public static DeskLook? Find(string id) =>
        All.FirstOrDefault(look => string.Equals(look.Id, id, StringComparison.Ordinal));
}

/// <summary>How DeskAI's own window looks. Nothing here touches Windows or any file.</summary>
public sealed record AppearanceSettings(ThemeMode Mode, string LookId)
{
    public static AppearanceSettings Default { get; } = new(ThemeMode.FollowWindows, DeskLookCatalog.DefaultId);

    /// <summary>The chosen look, or the default when the stored ID is unknown.</summary>
    public DeskLook Look => DeskLookCatalog.Find(LookId) ?? DeskLookCatalog.Default;
}
