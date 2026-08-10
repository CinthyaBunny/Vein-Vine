using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Lumina.Excel.Sheets;

namespace VeinAndVine.Services;

/// <summary>Which typeface the plugin's windows draw with.</summary>
public enum UiFontChoice
{
    /// <summary>Whatever the user configured Dalamud to use.</summary>
    Dalamud,

    /// <summary>Axis, the game's own UI typeface.</summary>
    GameAxis,
}

/// <summary>
/// Which colour set the plugin's windows use.
///
/// Everything below <see cref="Dalamud"/> is one of the game's own UI themes,
/// named as the game names it in System Configuration - including
/// <see cref="ClassicFF"/>, which really is "Classic FF" and not "Classic
/// FFXIV". The order matches the game's dropdown.
///
/// All eight of the game's themes. <c>UIColor</c> carries a column for every
/// one, though Lumina has only named the first six - see
/// <see cref="UiStyle.Column"/> for how the last two are reached.
/// </summary>
public enum UiThemeChoice
{
    /// <summary>Whatever the user configured Dalamud to use.</summary>
    Dalamud,

    Dark,
    Light,
    ClassicFF,
    ClearBlue,
    ClearWhite,
    ClearGreen,
    ClearGrey,
    ClearPink,
}

/// <summary>Where the node list lives.</summary>
public enum NodeListPlacement
{
    /// <summary>The first tab of the main window.</summary>
    Tabbed,

    /// <summary>
    /// Its own panel pinned to the main window's left edge, matching its
    /// height and following it around, but with its own width.
    /// </summary>
    DockedLeft,
}

/// <summary>
/// Applies the game's own typeface, palette and window art to the plugin's
/// windows, each independently switchable.
///
/// All three read from the client rather than from bundled assets: Axis is the
/// font the game's own UI uses, and the frame is the WindowA panel art that
/// every normal game window is built from. Nothing is downloaded.
///
/// All three are on by default - a plugin window sitting next to the game's
/// own windows may as well look like one - and each is a click from Dalamud's
/// default in the Appearance tab for anyone who themes Dalamud themselves.
/// </summary>
public sealed class UiStyle : IDisposable
{
    private readonly Configuration configuration;

    private IFontHandle? axisFont;
    private bool fontLoadFailed;

    // What was pushed for the current window, so the pop matches exactly even
    // if the setting changes mid-frame.
    private int pushedColors;
    private int pushedVars;
    private IDisposable? fontScope;

    public UiStyle(Configuration configuration) => this.configuration = configuration;

    public void Dispose()
    {
        PopWindowStyle();
        axisFont?.Dispose();
        axisFont = null;
    }

    /// <summary>
    /// Panel and border are ImGui's own, themed - see the note on PushTheme.
    /// Nothing extra is needed on the window itself.
    /// </summary>
    public ImGuiWindowFlags ExtraWindowFlags => ImGuiWindowFlags.None;

    /// <summary>
    /// Pushes the font and theme for one window. Call from PreDraw, before
    /// ImGui.Begin: the window background, border and title bar are drawn by
    /// Begin itself and would ignore anything pushed inside Draw.
    /// </summary>
    public void PushWindowStyle()
    {
        // Self-healing. If a PostDraw were ever skipped, resetting the counters
        // blind would orphan the previous frame's pushes and leak a few more
        // every frame until ImGui asserts.
        PopWindowStyle();

        fontScope = PushFont();
        PushTheme();
    }

    /// <summary>Undoes <see cref="PushWindowStyle"/>. Call from PostDraw.</summary>
    public void PopWindowStyle()
    {
        PopTheme();

        fontScope?.Dispose();
        fontScope = null;
    }

    private void PushTheme()
    {
        if (GetPalette(configuration.Theme) is not { } p)
            return;

        // Everything is derived from four anchors rather than written out per
        // theme. Six hand-tuned tables would be six things to keep in step, and
        // five of them would be guesses - the sheet only tells us what the text
        // and accent should be, so the rest is blended from those.
        var ground = p.Background;
        var text = p.Text;
        var accent = p.Accent;

        Color(ImGuiCol.WindowBg, Alpha(ground, 0.95f));
        Color(ImGuiCol.ChildBg, Alpha(ground, 0f));
        Color(ImGuiCol.PopupBg, Alpha(ground, 0.98f));

        // A single hairline. The game's panels are edged, not framed - the
        // heavy border this used to draw was the loudest thing on screen.
        Color(ImGuiCol.Border, Alpha(accent, 0.45f));
        Color(ImGuiCol.BorderShadow, Alpha(ground, 0f));

        Color(ImGuiCol.Text, text);
        Color(ImGuiCol.TextDisabled, p.TextDim);

        Color(ImGuiCol.FrameBg, Alpha(Mix(ground, text, 0.08f), 0.90f));
        Color(ImGuiCol.FrameBgHovered, Alpha(Mix(ground, text, 0.16f), 0.95f));
        Color(ImGuiCol.FrameBgActive, Mix(ground, text, 0.22f));

        Color(ImGuiCol.TitleBg, Mix(ground, text, 0.05f));
        Color(ImGuiCol.TitleBgActive, Mix(ground, accent, 0.18f));
        Color(ImGuiCol.TitleBgCollapsed, Alpha(ground, 0.80f));

        Color(ImGuiCol.Header, Alpha(Mix(ground, accent, 0.25f), 0.75f));
        Color(ImGuiCol.HeaderHovered, Alpha(Mix(ground, accent, 0.35f), 0.85f));
        Color(ImGuiCol.HeaderActive, Alpha(Mix(ground, accent, 0.45f), 0.95f));

        Color(ImGuiCol.Button, Alpha(Mix(ground, text, 0.12f), 0.95f));
        Color(ImGuiCol.ButtonHovered, Mix(ground, text, 0.20f));
        Color(ImGuiCol.ButtonActive, Mix(ground, text, 0.28f));

        Color(ImGuiCol.Tab, Mix(ground, text, 0.06f));
        Color(ImGuiCol.TabHovered, Mix(ground, accent, 0.35f));
        Color(ImGuiCol.TabActive, Mix(ground, accent, 0.22f));
        Color(ImGuiCol.TabUnfocused, Mix(ground, text, 0.03f));
        Color(ImGuiCol.TabUnfocusedActive, Mix(ground, accent, 0.12f));

        Color(ImGuiCol.TableHeaderBg, Mix(ground, text, 0.10f));

        // Faint and text-tinted, not accent-tinted. Accent-coloured borders
        // drew a full-height gold rule between every column, which no game list
        // has - its separators are barely-there hairlines.
        Color(ImGuiCol.TableBorderStrong, Alpha(text, 0.22f));
        Color(ImGuiCol.TableBorderLight, Alpha(text, 0.11f));
        Color(ImGuiCol.TableRowBg, Alpha(ground, 0f));
        Color(ImGuiCol.TableRowBgAlt, Alpha(text, 0.04f));

        Color(ImGuiCol.ScrollbarBg, Alpha(Mix(ground, text, 0.02f), 0.60f));
        Color(ImGuiCol.ScrollbarGrab, Mix(ground, text, 0.22f));
        Color(ImGuiCol.ScrollbarGrabHovered, Mix(ground, text, 0.30f));
        Color(ImGuiCol.ScrollbarGrabActive, Mix(ground, text, 0.38f));

        Color(ImGuiCol.CheckMark, accent);
        Color(ImGuiCol.SliderGrab, Alpha(accent, 0.80f));
        Color(ImGuiCol.SliderGrabActive, accent);

        Color(ImGuiCol.Separator, Alpha(accent, 0.55f));
        Color(ImGuiCol.SeparatorHovered, Alpha(accent, 0.75f));
        Color(ImGuiCol.SeparatorActive, accent);

        Color(ImGuiCol.ResizeGrip, Alpha(accent, 0.35f));
        Color(ImGuiCol.ResizeGripHovered, Alpha(accent, 0.65f));
        Color(ImGuiCol.ResizeGripActive, Alpha(accent, 0.85f));

        // The rest of the set. Left at Dalamud's defaults these are the bits
        // that give a half-themed window away - a stock blue text selection or
        // nav outline in the middle of an otherwise game-coloured panel.
        Color(ImGuiCol.MenuBarBg, Mix(ground, text, 0.05f));
        Color(ImGuiCol.TextSelectedBg, Alpha(accent, 0.35f));
        Color(ImGuiCol.DragDropTarget, accent);

        Color(ImGuiCol.NavHighlight, Alpha(accent, 0.80f));
        Color(ImGuiCol.NavWindowingHighlight, Alpha(text, 0.70f));
        Color(ImGuiCol.NavWindowingDimBg, Alpha(InvertTowards(ground), 0.20f));
        Color(ImGuiCol.ModalWindowDimBg, Alpha(InvertTowards(ground), 0.35f));

        Color(ImGuiCol.DockingPreview, Alpha(accent, 0.70f));
        Color(ImGuiCol.DockingEmptyBg, ground);

        Color(ImGuiCol.PlotLines, accent);
        Color(ImGuiCol.PlotLinesHovered, Mix(accent, text, 0.40f));
        Color(ImGuiCol.PlotHistogram, accent);
        Color(ImGuiCol.PlotHistogramHovered, Mix(accent, text, 0.40f));

        // Game panels are barely rounded and edged with a single hairline.
        // Every border size is pinned to 1 or 0 rather than left to the user's
        // Dalamud style, which is where the thick frames were coming from.
        Var(ImGuiStyleVar.WindowRounding, 3f);
        Var(ImGuiStyleVar.ChildRounding, 2f);
        Var(ImGuiStyleVar.FrameRounding, 2f);
        Var(ImGuiStyleVar.PopupRounding, 2f);
        Var(ImGuiStyleVar.ScrollbarRounding, 2f);
        Var(ImGuiStyleVar.TabRounding, 2f);
        Var(ImGuiStyleVar.GrabRounding, 2f);

        Var(ImGuiStyleVar.WindowBorderSize, 1f);
        Var(ImGuiStyleVar.ChildBorderSize, 1f);
        Var(ImGuiStyleVar.PopupBorderSize, 1f);
        Var(ImGuiStyleVar.FrameBorderSize, 0f);

        void Color(ImGuiCol target, Vector4 value)
        {
            ImGui.PushStyleColor(target, value);
            pushedColors++;
        }

        void Var(ImGuiStyleVar target, float value)
        {
            ImGui.PushStyleVar(target, value);
            pushedVars++;
        }
    }

    private void PopTheme()
    {
        if (pushedColors > 0)
            ImGui.PopStyleColor(pushedColors);

        if (pushedVars > 0)
            ImGui.PopStyleVar(pushedVars);

        pushedColors = 0;
        pushedVars = 0;
    }

    /// <summary>
    /// The four anchors a theme is built from. Three come straight out of the
    /// game's <c>UIColor</c> sheet; see <see cref="BuildPalette"/> for why the
    /// background does not.
    /// </summary>
    private sealed record Palette(Vector4 Background, Vector4 Text, Vector4 TextDim, Vector4 Accent);

    private readonly Dictionary<UiThemeChoice, Palette?> palettes = [];

    /// <summary>
    /// The palette for a theme, or null for <see cref="UiThemeChoice.Dalamud"/>
    /// and for anything that could not be read. Cached - the sheet does not
    /// change, and this is asked once per window per frame.
    /// </summary>
    private Palette? GetPalette(UiThemeChoice theme)
    {
        if (theme == UiThemeChoice.Dalamud)
            return null;

        if (palettes.TryGetValue(theme, out var cached))
            return cached;

        var built = BuildPalette(theme);
        palettes[theme] = built;
        return built;
    }

    /// <summary>
    /// Reads a theme's anchors out of the game's <c>UIColor</c> sheet, which
    /// carries one column per theme.
    ///
    /// Rows 1 and 3 are text and dimmed text - readable straight off the sheet,
    /// because they invert exactly as you would expect between Dark and Light
    /// (white text becomes brown, and so on).
    ///
    /// Row 22 is the accent, in preference to the paler row 8: row 8 is pure
    /// white under Classic FF, which would make every border and checkmark
    /// vanish into the text. Row 22 stays distinct from the text in all six,
    /// and gives Classic FF the pale blue it is known for.
    ///
    /// The ground is not from the sheet, and cannot be. Row 7 - the obvious
    /// candidate - turns out to be each theme's darkest or lightest tone rather
    /// than its window colour: it is pure black for Dark, Clear Blue, Clear
    /// Green and Clear Grey alike, and pure white for both Clear White and
    /// Clear Pink. Using it made four themes identically black and two
    /// identically white. The game keeps the real panel colour as a tint on its
    /// window textures, not as a sheet entry, so <see cref="Ground"/> carries
    /// values sampled from the game's own theme previews instead.
    /// </summary>
    private static Palette? BuildPalette(UiThemeChoice theme)
    {
        try
        {
            var sheet = Service.DataManager.GetExcelSheet<UIColor>();

            if (!sheet.TryGetRow(1, out var textRow) ||
                !sheet.TryGetRow(3, out var dimRow) ||
                !sheet.TryGetRow(22, out var accentRow))
                return null;

            var ground = Ground(theme);
            var text = Rgba(Column(textRow, theme));

            return new Palette(
                ground,
                text,
                Readable(Rgba(Column(dimRow, theme)), text, ground),
                Rgba(Column(accentRow, theme)));
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Could not read the {theme} theme from the UIColor sheet.");
            return null;
        }
    }

    /// <summary>
    /// Each theme's panel colour, sampled from the game's own theme previews in
    /// System Configuration - the most frequent pixel in the middle of each
    /// preview panel, which is the panel fill.
    ///
    /// Measured rather than judged, because judging them went badly: the
    /// themes are far more saturated than they look in memory. Classic FF is a
    /// vivid blue-violet, not a dark navy. Clear White is a mid grey, not
    /// white. Clear Pink is a strong pink, not a blush. Light is a warm peach,
    /// not a grey parchment.
    ///
    /// Reference shots live in Alpha 0.0.1.x Photos/Theme Examples.
    /// </summary>
    private static Vector4 Ground(UiThemeChoice theme) => theme switch
    {
        UiThemeChoice.Light => new Vector4(0.961f, 0.831f, 0.663f, 1f),      // #F5D4A9 warm peach
        UiThemeChoice.ClassicFF => new Vector4(0.098f, 0.000f, 0.565f, 1f),  // #190090 blue-violet
        UiThemeChoice.ClearBlue => new Vector4(0.090f, 0.188f, 0.400f, 1f),  // #173066
        UiThemeChoice.ClearWhite => new Vector4(0.702f, 0.718f, 0.725f, 1f), // #B3B7B9 mid grey
        UiThemeChoice.ClearGreen => new Vector4(0.165f, 0.392f, 0.090f, 1f), // #2A6417
        UiThemeChoice.ClearGrey => new Vector4(0.208f, 0.220f, 0.235f, 1f),  // #35383C
        UiThemeChoice.ClearPink => new Vector4(0.906f, 0.655f, 0.839f, 1f),  // #E7A7D6
        _ => new Vector4(0.137f, 0.137f, 0.137f, 1f),                        // #232323 Dark
    };

    /// <summary>
    /// Picks a theme's column out of a <c>UIColor</c> row.
    ///
    /// Clear Grey and Clear Pink come from Lumina's two unnamed columns. The
    /// sheet's named columns run in the same order as the game's own theme
    /// dropdown - Dark, Light, Classic FF, Clear Blue, Clear White, Clear
    /// Green - and Grey and Pink are the two that follow, so the unnamed pair
    /// is them.
    ///
    /// The data says the same thing independently, which is what makes it safe
    /// to rely on: Unknown3 is dark purple text on a white ground, which is
    /// Clear Pink and could not be anything else, while Unknown2 is neutral
    /// greys on black.
    ///
    /// If a future Lumina names these properly this stops compiling, which is
    /// the right way for it to break.
    /// </summary>
    private static uint Column(UIColor row, UiThemeChoice theme) => theme switch
    {
        UiThemeChoice.Light => row.Light,
        UiThemeChoice.ClassicFF => row.ClassicFF,
        UiThemeChoice.ClearBlue => row.ClearBlue,
        UiThemeChoice.ClearWhite => row.ClearWhite,
        UiThemeChoice.ClearGreen => row.ClearGreen,
        UiThemeChoice.ClearGrey => row.Unknown2,
        UiThemeChoice.ClearPink => row.Unknown3,
        _ => row.Dark,
    };

    /// <summary>UIColor packs its entries as RRGGBBAA, not the usual ARGB.</summary>
    private static Vector4 Rgba(uint packed) => new(
        ((packed >> 24) & 0xFF) / 255f,
        ((packed >> 16) & 0xFF) / 255f,
        ((packed >> 8) & 0xFF) / 255f,
        (packed & 0xFF) / 255f);

    /// <summary>
    /// Nudges dimmed text towards the full text colour until it is legible
    /// against the panel.
    ///
    /// The sheet's dimmed entry is a colour the game uses over its own
    /// backgrounds, which are not always the panel - under Clear Pink it is a
    /// purple-grey on pink at 2.7:1, short of the 3:1 usually taken as the
    /// floor for interface text. Dimmed text carries whole columns here (zone,
    /// level, windows), so it has to clear that.
    ///
    /// Written as a guard rather than a correction to one theme: it does
    /// nothing at all to the seven that already pass.
    /// </summary>
    private static Vector4 Readable(Vector4 dim, Vector4 text, Vector4 ground)
    {
        for (var step = 0; step < 8 && ContrastRatio(dim, ground) < 3f; step++)
            dim = Mix(dim, text, 0.25f);

        return dim;
    }

    /// <summary>WCAG relative-luminance contrast ratio, 1:1 to 21:1.</summary>
    private static float ContrastRatio(Vector4 a, Vector4 b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

    private static float Luminance(Vector4 c) =>
        (0.2126f * Channel(c.X)) + (0.7152f * Channel(c.Y)) + (0.0722f * Channel(c.Z));

    private static float Channel(float v) =>
        v <= 0.03928f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);

    /// <summary>Blends RGB towards <paramref name="towards"/>, keeping a's alpha.</summary>
    private static Vector4 Mix(Vector4 a, Vector4 towards, float amount) => new(
        a.X + ((towards.X - a.X) * amount),
        a.Y + ((towards.Y - a.Y) * amount),
        a.Z + ((towards.Z - a.Z) * amount),
        a.W);

    private static Vector4 Alpha(Vector4 colour, float alpha) =>
        new(colour.X, colour.Y, colour.Z, alpha);

    /// <summary>
    /// Black over a light ground, white over a dark one. The dimming layers
    /// behind modals need to darken a light theme and lighten a dark one, and
    /// a fixed black would be invisible against Dark's near-black panels.
    /// </summary>
    private static Vector4 InvertTowards(Vector4 ground) =>
        (0.2126f * ground.X) + (0.7152f * ground.Y) + (0.0722f * ground.Z) > 0.5f
            ? new Vector4(0f, 0f, 0f, 1f)
            : new Vector4(1f, 1f, 1f, 1f);

    /// <summary>
    /// Scopes the chosen font. Returns null when the default font is wanted, or
    /// when the game font is not built yet - the atlas builds asynchronously,
    /// so the first frames after a reload simply use the default.
    /// </summary>
    private IDisposable? PushFont()
    {
        if (configuration.Font != UiFontChoice.GameAxis)
            return null;

        var handle = GetAxisFont();
        return handle is { Available: true } ? handle.Push() : null;
    }

    private IFontHandle? GetAxisFont()
    {
        if (axisFont is not null || fontLoadFailed)
            return axisFont;

        try
        {
            axisFont = Service.PluginInterface.UiBuilder.FontAtlas
                .NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));
        }
        catch (Exception ex)
        {
            // Once, not every frame.
            fontLoadFailed = true;
            Service.Log.Error(ex, "Could not load the game's Axis font; using the default.");
        }

        return axisFont;
    }

}
