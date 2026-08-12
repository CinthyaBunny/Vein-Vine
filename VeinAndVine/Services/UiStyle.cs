using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Lumina.Excel.Sheets;

namespace VeinAndVine.Services;

public enum UiFontChoice
{
    /// <summary>Whatever the user configured Dalamud to use.</summary>
    Dalamud,

    /// <summary>Axis, the game's own UI typeface.</summary>
    GameAxis,
}

/// <summary>
/// Everything below <see cref="Dalamud"/> is one of the game's own UI themes,
/// named and ordered as the game does in System Configuration - including
/// <see cref="ClassicFF"/>
///
/// <c>UIColor</c> carries a column for all eight, though Lumina has only named
/// the first six - see <see cref="UiStyle.Column"/> for how the last two are
/// reached.
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
/// Applies the game's own typeface and palette to the plugin's windows, each
/// switchable independently of the other.
///
/// Both are read out of the client rather than bundled: Axis is the font the
/// game's own UI uses, and the colours come from the <c>UIColor</c> sheet.
/// Nothing is downloaded.
///
/// Both default to the game's look - a plugin window sitting next to the
/// game's own windows may as well look like one - and each is one click from
/// Dalamud's default for anyone who has themed Dalamud themselves.
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

        // Every colour below is blended from these three rather than written
        // out per theme. Eight hand-tuned tables would be eight things to keep
        // in step, and most of each would be guesswork - the sheet gives only a
        // text and an accent colour, so the rest has to be derived anyway.
        var ground = p.Background;
        var text = p.Text;
        var accent = p.Accent;
        var onLight = IsLight(ground);

        // Which measure this theme's control fills are fitted against. Every
        // theme but Dark is held to APCA - see TextReads.
        var apca = UsesApca(configuration.Theme);

        Color(ImGuiCol.WindowBg, Alpha(ground, 0.95f));
        Color(ImGuiCol.ChildBg, Alpha(ground, 0f));
        Color(ImGuiCol.PopupBg, Alpha(ground, 0.98f));

        // A single hairline. The game's panels are edged, not framed - the
        // heavy border this used to draw was the loudest thing on screen.
        Color(ImGuiCol.Border, Alpha(accent, 0.45f));
        Color(ImGuiCol.BorderShadow, Alpha(ground, 0f));

        Color(ImGuiCol.Text, text);
        Color(ImGuiCol.TextDisabled, p.TextDim);

        // Every control is a visible object standing off the panel, not a
        // tint of it. Inputs sit a little lower than buttons, which is the
        // usual reading: a field is a well, a button is a raised key.
        Color(ImGuiCol.FrameBg, Alpha(Control(ground, text, 0.10f, apca), 0.95f));
        Color(ImGuiCol.FrameBgHovered, Control(ground, text, 0.16f, apca));
        Color(ImGuiCol.FrameBgActive, Control(ground, text, 0.22f, apca));

        Color(ImGuiCol.TitleBg, Control(ground, text, 0.08f, apca));
        Color(ImGuiCol.TitleBgActive, AccentControl(ground, accent, text, 0.14f, apca));
        Color(ImGuiCol.TitleBgCollapsed, Alpha(ground, 0.80f));

        Color(ImGuiCol.Header, AccentControl(ground, accent, text, 0.16f, apca));
        Color(ImGuiCol.HeaderHovered, AccentControl(ground, accent, text, 0.24f, apca));
        // Make active headers (row highlights) darker on light themes so they read as a distinct, recessed object.
        Color(ImGuiCol.HeaderActive, AccentControl(ground, accent, text, onLight ? 0.48f : 0.32f, apca));

        Color(ImGuiCol.Button, Control(ground, text, 0.17f, apca));
        Color(ImGuiCol.ButtonHovered, Control(ground, text, 0.25f, apca));
        Color(ImGuiCol.ButtonActive, Control(ground, text, 0.33f, apca));

        // Not the plugin's own tab strip - that is hand-drawn by GameTabBar
        // from Tabs below, and no BeginTabBar exists anywhere. These style the
        // tab ImGui itself draws once the user docks this window into another,
        // which is why grepping for a tab bar finds nothing.
        Color(ImGuiCol.Tab, Control(ground, text, 0.09f, apca));
        Color(ImGuiCol.TabHovered, AccentControl(ground, accent, text, 0.26f, apca));
        Color(ImGuiCol.TabActive, AccentControl(ground, accent, text, 0.20f, apca));
        Color(ImGuiCol.TabUnfocused, Control(ground, text, 0.06f, apca));
        Color(ImGuiCol.TabUnfocusedActive, Control(ground, text, 0.14f, apca));

        Color(ImGuiCol.TableHeaderBg, Control(ground, text, 0.12f, apca));

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

        // Buttons and fields are pills, as they are in game. ImGui clamps
        // rounding to half the shorter side, so half the frame height rounds a
        // button into a full pill while leaving a wide search field as a
        // rounded bar with the same end caps - which is exactly the game's
        // pair of shapes from one number.
        var pill = ImGui.GetFrameHeight() * 0.5f;

        Var(ImGuiStyleVar.FrameRounding, pill);
        Var(ImGuiStyleVar.GrabRounding, pill);
        Var(ImGuiStyleVar.ScrollbarRounding, pill);

        Var(ImGuiStyleVar.TabRounding, 6f);

        Var(ImGuiStyleVar.WindowRounding, 3f);
        Var(ImGuiStyleVar.ChildRounding, 3f);
        Var(ImGuiStyleVar.PopupRounding, 3f);

        // A hairline on the panel, and one on every control - this is what
        // stops a button being merely a slightly different shade of the
        // background. Pinned rather than inherited from the user's Dalamud
        // style, which is free to set them to zero.
        Var(ImGuiStyleVar.WindowBorderSize, 1f);
        Var(ImGuiStyleVar.ChildBorderSize, 1f);
        Var(ImGuiStyleVar.PopupBorderSize, 1f);
        Var(ImGuiStyleVar.FrameBorderSize, 1f);

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
    /// The anchors a theme is built from. Three come straight out of the game's
    /// <c>UIColor</c> sheet; see <see cref="BuildPalette"/> for why the
    /// background does not, and <see cref="Legible"/> for how the two status
    /// colours are fitted to it.
    /// </summary>
    private sealed record Palette(
        Vector4 Background,
        Vector4 Text,
        Vector4 TextDim,
        Vector4 Accent,
        Vector4 Success,
        Vector4 Warning);

    // What the node list means by "up now" and "up soon" when no game theme is
    // selected. Tuned for Dalamud's own dark default.
    private static readonly Vector4 DefaultSuccess = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 DefaultWarning = new(0.95f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 DefaultMuted = new(0.62f, 0.62f, 0.62f, 1f);

    private Palette? Current => GetPalette(configuration.Theme);

    /// <summary>A node that is up now. Green, adjusted to read on this theme.</summary>
    public Vector4 StatusUp => Current?.Success ?? DefaultSuccess;

    /// <summary>A node about to come up. Amber, adjusted to read on this theme.</summary>
    public Vector4 StatusSoon => Current?.Warning ?? DefaultWarning;

    /// <summary>A node that is waiting, or simply always there.</summary>
    public Vector4 StatusIdle => Current?.TextDim ?? DefaultMuted;

    /// <summary>Fills and label colours for the hand-drawn tab strip.</summary>
    public readonly record struct TabColours(
        Vector4 Idle,
        Vector4 Hovered,
        Vector4 Active,
        Vector4 IdleLabel,
        Vector4 ActiveLabel,
        Vector4 Edge);

    /// <summary>
    /// The tab strip's own colours, which do not follow the rules the rest of
    /// the interface does.
    ///
    /// In game a tab is dark and its label pale, on the light themes as much as
    /// the dark ones - Light's tabs are near-black lettered in cream, sitting
    /// on a peach panel. ImGui's tab bar could never do that, because one Text
    /// colour has to serve the panel and the tabs alike. Drawing the strip by
    /// hand means the label can be chosen per tab, so this follows the game
    /// instead of compromising with it.
    ///
    /// The selected tab is the darker of the two, which is the opposite of most
    /// interfaces and is what the game does: the current tab is recessed, the
    /// others stand proud of it.
    /// </summary>
    public TabColours Tabs
    {
        get
        {
            var ground = Current?.Background ?? new Vector4(0.090f, 0.090f, 0.102f, 1f);
            var accent = Current?.Accent ?? new Vector4(0.847f, 0.741f, 0.463f, 1f);
            var onLight = IsLight(ground);

            // A light panel needs its tabs taken much further down to read as
            // dark furniture; a panel that is already near-black does not.
            var active = Mix(ground, Black, onLight ? 0.72f : 0.55f);
            var idle = Mix(ground, Black, onLight ? 0.52f : 0.28f);
            var hovered = Mix(ground, Black, onLight ? 0.62f : 0.40f);

            return new TabColours(
                idle,
                hovered,
                active,
                LabelOn(idle, UsesApca(configuration.Theme)),
                LabelOn(active, UsesApca(configuration.Theme)),
                accent);
        }
    }

    private static Vector4 LabelOn(Vector4 fill, bool apca) =>
        apca
            ? Apca.Contrast(White, fill) >= Apca.Contrast(Black, fill) ? White : Black
            : LabelOnByRatio(fill);

    private static Vector4 LabelOnByRatio(Vector4 fill) =>
        ContrastRatio(White, fill) >= ContrastRatio(Black, fill) ? White : Black;

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
    /// vanish into the text. Row 22 stays distinct from the text in all eight,
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

            var apca = UsesApca(theme);
            var ground = FitGroundForText(Ground(theme), apca);

            // The body text is fitted too, which it did not used to be - it went
            // into the palette exactly as the sheet gave it. That was survivable
            // only because the 2.x ratio flattered it: measured properly the
            // sheet's own text falls short of the body floor on Light (Lc 74),
            // Clear White (Lc 65) and Clear Pink (Lc 59), while the ratio called
            // all three comfortable at 8.1:1, 10.4:1 and 6.4:1.
            //
            // The sheet is not wrong; it is answering a different question. Its
            // text colour is meant for the backgrounds the game pairs it with,
            // and the panel colours here are sampled from previews rather than
            // taken from it, so the pairing is ours to make good.
            var text = Legible(Rgba(Column(textRow, theme)), ground, 4.5f, apca);

            return new Palette(
                ground,
                text,
                // After the text, and against it: this walks the dimmed colour
                // towards whatever the text ended up being, so it has to be the
                // fitted one or it would be aiming at a colour nothing uses.
                Readable(Rgba(Column(dimRow, theme)), text, ground, apca),
                Rgba(Column(accentRow, theme)),
                Legible(DefaultSuccess, ground, 4.5f, apca),
                Legible(DefaultWarning, ground, 4.5f, apca));
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Could not read the {theme} theme from the UIColor sheet.");
            return null;
        }
    }

    /// <summary>
    /// Which measure a theme's colours are fitted with. Everything but
    /// <see cref="UiThemeChoice.Dark"/> uses APCA - see
    /// <see cref="TextReads"/> for why Dark is the exception.
    /// </summary>
    private static bool UsesApca(UiThemeChoice theme) => theme != UiThemeChoice.Dark;

    /// <summary>
    /// Moves a panel away from mid-tone, keeping its hue, until body text can
    /// actually reach its floor against it.
    ///
    /// This is the one case the text guards cannot solve, because the problem
    /// is not the text. A mid-tone panel is too close to both ends at once:
    /// Clear White's grey gives pure black only Lc 64 and pure white only
    /// Lc 45, so no text colour exists that would pass, and every guard above
    /// can do is pick the least bad of them. The panel has to move.
    ///
    /// It costs something real, and the cost is the point of keeping it
    /// minimal. These panel colours are sampled from the game's own theme
    /// previews and are treated everywhere else as ground truth, so this walks
    /// in the smallest steps that reach the floor and stops - Clear White and
    /// Clear Pink are the only two themes it touches at all, and it leaves both
    /// recognisably themselves. The alternative was a plugin whose Clear White
    /// nobody with less than perfect eyesight could read.
    ///
    /// Away from mid-tone rather than toward a fixed colour: a light panel gets
    /// lighter so dark text works, a dark one darker so light text does. Moving
    /// either toward the middle is what caused this.
    /// </summary>
    /// <summary>
    /// How far past the body floor a panel is fitted, in Lc. Covers the gap
    /// between what pure black or white can reach against it and what a colour
    /// that must keep its hue can.
    /// </summary>
    private const float PanelHeadroom = 6f;

    private static Vector4 FitGroundForText(Vector4 ground, bool apca)
    {
        if (!apca)
            return ground;

        var lighten = IsLight(ground);
        var toward = lighten ? White : Black;

        // The best text this panel could ever carry. If that cannot reach the
        // floor, no choice of text colour will.
        var bestPossibleText = lighten ? Black : White;

        // Fitted past the floor rather than to it, because black and white are
        // not the only text here. The status green and amber have to keep their
        // hue to keep their meaning, so they can never be taken all the way to
        // the extreme - and a panel that only just permits pure black leaves
        // them a few Lc short. This is the room they need.
        var target = Apca.BodyText + PanelHeadroom;

        for (var step = 0;
             step < 12 && Apca.Contrast(bestPossibleText, ground) < target;
             step++)
        {
            ground = Mix(ground, toward, 0.08f);
        }

        return ground;
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
    private static Vector4 Readable(Vector4 dim, Vector4 text, Vector4 ground, bool apca)
    {
        // Held to the secondary-text floor rather than the body-text one. This
        // colour carries whole columns, so it has to be comfortably readable -
        // but it is also the plugin's way of saying "this is the lesser
        // information", and fitting it to the same floor as the text beside it
        // would make the two indistinguishable and lose that.
        for (var step = 0; step < 12 && !TextReads(dim, ground, apca, Apca.SecondaryText, 3f); step++)
            dim = Mix(dim, text, 0.25f);

        return dim;
    }

    /// <summary>
    /// Darkens or lightens a colour until it reads against the panel, keeping
    /// its hue.
    ///
    /// Used for every colour that has to carry meaning as well as be read: the
    /// node list's green and amber - up now, up soon - and the body text
    /// itself. The hue has to survive in all of them, so only the lightness
    /// moves, and it moves away from the panel: a light green becomes a deep
    /// green on Light's peach and stays light on Dark.
    ///
    /// Without this the status column was very nearly invisible on three
    /// themes: amber on Light's #F5D4A9 is the same colour twice.
    /// </summary>
    private static Vector4 Legible(Vector4 colour, Vector4 ground, float target, bool apca)
    {
        var away = IsLight(ground)
            ? new Vector4(0f, 0f, 0f, colour.W)
            : new Vector4(1f, 1f, 1f, colour.W);

        // Sixteen steps rather than twelve. The APCA floor is the stricter of
        // the two, and a colour starting close to its panel needs more room to
        // walk out to it.
        //
        // The loop also stops the moment a step stops helping, which is not
        // belt-and-braces: on a mid-tone panel the floor can be unreachable in
        // both directions, and walking on regardless would trade a colour that
        // was as good as that panel allows for a worse one. Clear White is the
        // case - its panel is a mid grey, and even pure black only reaches
        // Lc 64 against it - so its text stops where the sheet left it rather
        // than being marched to black for nothing.
        var best = TextScore(colour, ground, apca);

        for (var step = 0; step < 28 && best < (apca ? Apca.BodyText : target); step++)
        {
            var moved = Mix(colour, away, 0.10f);
            var score = TextScore(moved, ground, apca);

            if (score <= best)
                break;

            colour = moved;
            best = score;
        }

        return colour;
    }

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// The fill for a control - a button, a tab, an input, a selected row -
    /// which has to be visible as an object in its own right and still carry a
    /// readable label.
    ///
    /// Sampling the game's own theme previews shows its controls always stand
    /// off the panel, and which way depends on the panel: lighter on the dark
    /// themes, darker on the light ones. The game can afford to sink a control
    /// hard on a light panel - Light's buttons are near-black - because it
    /// flips the label to a pale colour to suit. ImGui has one Text colour for
    /// everything, so that is not available here: the lift is kept modest, and
    /// if the game's direction would cost the label its contrast, the other
    /// direction is taken instead.
    ///
    /// This replaced an earlier version that mixed towards the text and then
    /// pulled back towards the panel whenever contrast suffered. It protected
    /// the label and quietly destroyed the thing the label sits on, leaving
    /// controls that had all but dissolved into the background.
    /// </summary>
    private static Vector4 Control(Vector4 ground, Vector4 text, float lift, bool apca)
    {
        var onLightPanel = IsLight(ground);

        var toward = onLightPanel ? Black : White;
        var preferred = Mix(ground, toward, lift);

        if (TextReads(text, preferred, apca, Apca.BodyText, 4.5f))
        {
            // A small lift off a very dark, saturated panel barely registers -
            // Classic FF's idle tab landed at 1.18:1. Keep going until the
            // control is a distinct object, stopping the moment the label
            // would suffer for it.
            for (var step = 0; step < 8 && !ShapeReads(preferred, ground); step++)
            {
                var further = Mix(preferred, toward, 0.10f);
                if (!TextReads(text, further, apca, Apca.BodyText, 4.5f))
                    break;

                preferred = further;
            }

            return preferred;
        }

        // The game's direction would cost the label its contrast, so go the
        // other way - which is away from the text, and so can only help it.
        // That also means there is no reason to stop at the requested lift:
        // keep going until the control is genuinely distinct from the panel.
        // Clear Pink needs this. Its text is only 6.4:1 off the panel, so a
        // darker button is unreadable and a slightly lighter one is invisible.
        var away = onLightPanel ? White : Black;
        var result = Mix(ground, away, lift);

        for (var step = 0; step < 10 && !ShapeReads(result, ground); step++)
            result = Mix(result, away, 0.12f);

        return result;
    }

    /// <summary>
    /// A control fill carrying the theme's accent, for the states that mean
    /// "this one" - a selected row, the active tab. Tinted first, then given
    /// the same lift and the same guarantee as any other control.
    /// </summary>
    private static Vector4 AccentControl(Vector4 ground, Vector4 accent, Vector4 text, float lift, bool apca)
    {
        var plain = Control(ground, text, lift, apca);
        var tinted = Mix(plain, accent, 0.35f);

        // The tint can undo the contrast the lift just bought; walk it back
        // towards the plain control fill, which is already known to be both
        // visible and readable, until the label reads again.
        for (var step = 0; step < 12 && !TextReads(text, tinted, apca, Apca.BodyText, 4.5f); step++)
            tinted = Mix(tinted, plain, 0.25f);

        return TextReads(text, tinted, apca, Apca.BodyText, 4.5f) ? tinted : plain;
    }

    /// <summary>WCAG relative-luminance contrast ratio, 1:1 to 21:1.</summary>
    private static float ContrastRatio(Vector4 a, Vector4 b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

    /// <summary>
    /// Whether text reads on a background, by whichever measure the theme is
    /// fitted with.
    ///
    /// Every theme but <see cref="UiThemeChoice.Dark"/> is fitted with APCA,
    /// which unlike the 2.x ratio knows which of the two colours is the text
    /// and scores the two polarities differently. Seven of the eight themes are
    /// the ones that needed it: three are light panels, where the 2.x ratio is
    /// least reliable, and the saturated ones sit where its symmetry hurts most.
    ///
    /// Dark keeps the ratio it was tuned against. Its palette was measured by
    /// eye across every control state under that measure, and re-fitting it to
    /// a different one would move colours that are already right - a change
    /// with a cost and no benefit.
    ///
    /// The two thresholds are not convertible into one another. They are
    /// different scales, so each is passed explicitly rather than derived.
    /// </summary>
    private static bool TextReads(
        Vector4 text, Vector4 background, bool apca, float minimumLc, float minimumRatio) =>
        TextScore(text, background, apca) >= (apca ? minimumLc : minimumRatio);

    /// <summary>
    /// The same judgement as a number, for the fitting loops - they need to
    /// know not just whether a colour passes but whether a step improved it.
    /// The two scales are not comparable with each other; only with themselves.
    /// </summary>
    private static float TextScore(Vector4 text, Vector4 background, bool apca) =>
        apca ? Apca.Contrast(text, background) : ContrastRatio(text, background);

    /// <summary>
    /// Whether something non-textual is distinct from what is behind it - a
    /// control's fill against the panel.
    ///
    /// APCA is a text measure and its authors are explicit that non-text is a
    /// separate question, so this stays on the ratio for every theme. Using a
    /// text threshold here would be a misreading of the algorithm rather than a
    /// stricter application of it.
    /// </summary>
    private static bool ShapeReads(Vector4 shape, Vector4 background) =>
        ContrastRatio(shape, background) >= 1.25f;

    /// <summary>
    /// Above this a panel counts as light, and every rule that depends on which
    /// way to move a colour flips: controls sink instead of lifting, status
    /// colours darken instead of lightening, tab labels go black instead of
    /// white.
    ///
    /// One constant because those rules have to agree. Two of them disagreeing
    /// on a single theme is how you get a control lifted the wrong way with a
    /// label chosen for the other direction.
    /// </summary>
    private const float LightThreshold = 0.4f;

    private static bool IsLight(Vector4 colour) => Luminance(colour) > LightThreshold;

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
    private static Vector4 InvertTowards(Vector4 ground) => IsLight(ground) ? Black : White;

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
