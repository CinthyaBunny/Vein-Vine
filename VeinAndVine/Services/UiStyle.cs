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
        // theme but Dark is held to APCA - see BodyFloor.
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
        Color(ImGuiCol.FrameBg, Alpha(Fill(0.10f), 0.95f));
        Color(ImGuiCol.FrameBgHovered, Fill(0.16f));
        Color(ImGuiCol.FrameBgActive, Fill(0.22f));

        Color(ImGuiCol.TitleBg, Fill(0.08f));
        Color(ImGuiCol.TitleBgActive, AccentFill(0.14f));
        Color(ImGuiCol.TitleBgCollapsed, Alpha(ground, 0.80f));

        Color(ImGuiCol.Header, AccentFill(0.16f));
        Color(ImGuiCol.HeaderHovered, AccentFill(HoveredLift));
        // Make active headers (row highlights) darker on light themes so they read as a distinct, recessed object.
        Color(ImGuiCol.HeaderActive, AccentFill(SelectedLift(onLight)));

        Color(ImGuiCol.Button, Fill(0.17f));
        Color(ImGuiCol.ButtonHovered, Fill(0.25f));
        Color(ImGuiCol.ButtonActive, Fill(0.33f));

        // Not the plugin's own tab strip - that is hand-drawn by GameTabBar
        // from Tabs below, and no BeginTabBar exists anywhere. These style the
        // tab ImGui itself draws once the user docks this window into another,
        // which is why grepping for a tab bar finds nothing.
        Color(ImGuiCol.Tab, Fill(0.09f));
        Color(ImGuiCol.TabHovered, AccentFill(0.26f));
        Color(ImGuiCol.TabActive, AccentFill(0.20f));
        Color(ImGuiCol.TabUnfocused, Fill(0.06f));
        Color(ImGuiCol.TabUnfocusedActive, Fill(0.14f));

        Color(ImGuiCol.TableHeaderBg, Fill(0.12f));

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

        // The lift is the only thing that varies between these - the panel, the
        // text and the measure are fixed for the whole window - so it is the
        // only thing worth reading at each call site.
        Vector4 Fill(float lift) => Control(ground, text, lift, apca);

        Vector4 AccentFill(float lift) => AccentControl(ground, accent, text, lift, apca);

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
    /// The two lifts the row highlight is drawn with. Named rather than written
    /// at the point of use, because <see cref="BuildPalette"/> has to fit the
    /// status colours against these exact fills: a number changed in one place
    /// and not the other would fit them to a highlight that is not the one the
    /// list draws, which is a failure with no symptom until someone hovers a
    /// row on the wrong theme.
    /// </summary>
    private const float HoveredLift = 0.24f;

    private static float SelectedLift(bool onLight) => onLight ? 0.48f : 0.32f;

    /// <summary>
    /// Every surface a row's text is drawn on: the panel, and the two fills the
    /// row highlight paints over it.
    ///
    /// A colour fitted against the panel alone is fitted against the one moment
    /// it is least likely to be read closely. The row under the pointer is the
    /// row being read, and it is not the panel colour - it is the highlight.
    /// </summary>
    private readonly record struct Surfaces(Vector4 Panel, Vector4 Hovered, Vector4 Selected);

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
        TextScore(White, fill, apca) >= TextScore(Black, fill, apca) ? White : Black;

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
            var text = Legible(Rgba(Column(textRow, theme)), ground, apca);
            var accent = Rgba(Column(accentRow, theme));

            // The body text is fitted against the panel alone, and needs no more
            // than that: every control fill is built to carry it, so a fill that
            // would cost it its floor is never chosen in the first place. The
            // dimmed and status colours get no such guarantee - nothing consults
            // them when a fill is picked - so they are fitted against the
            // highlight as well as the panel, in the order the window builds
            // them: the fills depend on the text, and these depend on the fills.
            var surfaces = new Surfaces(
                ground,
                AccentControl(ground, accent, text, HoveredLift, apca),
                AccentControl(ground, accent, text, SelectedLift(IsLight(ground)), apca));

            return new Palette(
                ground,
                text,
                // After the text, and against it: this walks the dimmed colour
                // towards whatever the text ended up being, so it has to be the
                // fitted one or it would be aiming at a colour nothing uses.
                Readable(Rgba(Column(dimRow, theme)), text, surfaces, apca),
                accent,
                Legible(DefaultSuccess, surfaces, apca),
                Legible(DefaultWarning, surfaces, apca));
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
    /// <see cref="BodyFloor"/> for why Dark is the exception.
    /// </summary>
    private static bool UsesApca(UiThemeChoice theme) => theme != UiThemeChoice.Dark;

    /// <summary>
    /// How far past the body floor a panel is fitted, in Lc. Covers the gap
    /// between what pure black or white can reach against it and what a colour
    /// that must keep its hue can.
    /// </summary>
    private const float PanelHeadroom = 6f;

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
    ///
    /// Held to the secondary-text floor rather than the body-text one. This
    /// colour carries whole columns, so it has to be comfortably readable - but
    /// it is also the plugin's way of saying "this is the lesser information",
    /// and fitting it to the same floor as the text beside it would make the two
    /// indistinguishable and lose that.
    ///
    /// Against the highlight as well as the panel, which costs it some of that
    /// distinctness on the dark themes: their row highlight is a mid-tone slab
    /// two to three times the panel's luminance, and a dimmed grey vanishes into
    /// it - Clear Blue's fell to Lc 35 under the pointer, on the row the reader
    /// is looking at. Clearing that pushes the colour pale enough to sit close
    /// to the body text it is meant to defer to. The trade is deliberate: text
    /// that cannot be read has lost more than text that reads as slightly less
    /// quiet than intended.
    /// </summary>
    private static Vector4 Readable(Vector4 dim, Vector4 text, Surfaces on, bool apca)
    {
        // The panel first, and unconditionally. This floor is not negotiable
        // against the one below it: a dimmed colour that cannot be read on the
        // panel fails on every row, where one that cannot be read on the
        // highlight fails only on the row under the pointer.
        for (var step = 0; step < 12 && !SecondaryReads(dim, on.Panel, apca); step++)
            dim = Mix(dim, text, 0.25f);

        // Then the highlight, as far as the colour can go and still be dim.
        var best = TextScore(dim, on.Panel, apca);

        for (var step = 0; step < 12 && !SecondaryOnHighlight(dim, on, apca); step++)
        {
            var moved = Mix(dim, text, 0.25f);

            // The walk is towards the body text, so there is a point past which
            // it has not made the dimmed colour readable, it has made it the
            // body text. That is not a fitted colour, it is a lost distinction:
            // the zone and level columns stop being visibly quieter than the
            // name beside them. Where the highlight cannot be cleared without
            // going that far, the colour stops here and stays dim.
            if (TextScore(moved, text, apca) < DimGap(apca))
                break;

            // Progress on the panel, for the reason Legible gives: it is the one
            // surface the walk moves monotonically away from.
            var score = TextScore(moved, on.Panel, apca);

            if (score <= best)
                break;

            dim = moved;
            best = score;
        }

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
    /// <remarks>
    /// The panel-only overload is for the body text, which needs nothing more:
    /// every control fill is chosen to carry it, so no surface it lands on can
    /// cost it its floor. Anything the fills do not consult - the status
    /// colours - takes the <see cref="Surfaces"/> overload instead.
    /// </remarks>
    private static Vector4 Legible(Vector4 colour, Vector4 ground, bool apca) =>
        Legible(colour, new Surfaces(ground, ground, ground), apca);

    private static Vector4 Legible(Vector4 colour, Surfaces on, bool apca)
    {
        var ground = on.Panel;

        // The two directions are not mirror images, and treating them as one
        // was costing the status colours their identity. Blending towards black
        // scales every channel down, which leaves hue and saturation untouched;
        // blending towards white washes saturation out, and on Clear Green -
        // whose panel is itself green, so the green has furthest to travel -
        // "up now" arrived as a near-white mint with two thirds of its
        // saturation gone. Legible, and no longer recognisably green.
        //
        // So lightening raises the colour's value and leaves its saturation
        // alone, and only surrenders saturation once value has nowhere left to
        // go. See Brighten.
        var lighten = !IsLight(ground);

        // Generous step count, because a colour starting close to its panel has
        // a long way to walk.
        //
        // The loop stops the moment a step stops helping, which is not
        // belt-and-braces: on a mid-tone panel the floor can be unreachable in
        // both directions, and walking on regardless would trade a colour that
        // was as good as that panel allows for a worse one. Clear White is the
        // case - its panel is a mid grey, and even pure black only reaches
        // Lc 64 against it - so its text stops where the sheet left it rather
        // than being marched to black for nothing.
        //
        // Progress is measured against the panel even though the colour has to
        // satisfy all three surfaces, because the panel is the only one it moves
        // monotonically away from. Scoring the step on the worst surface instead
        // stopped the walk dead: a colour travelling from light to dark passes
        // straight through the highlight's own luminance on the way, APCA scores
        // that crossing as zero - its way of saying "too close to call" - and
        // the guard read the crossing as a step that had stopped paying. Every
        // status colour on the three light themes was left exactly where it
        // started.
        var best = TextScore(colour, ground, apca);

        for (var step = 0; step < 28 && !StatusReads(colour, on, apca); step++)
        {
            var moved = lighten
                ? Brighten(colour, 0.10f)
                : Mix(colour, new Vector4(0f, 0f, 0f, colour.W), 0.10f);
            var score = TextScore(moved, ground, apca);

            if (score <= best)
                break;

            colour = moved;
            best = score;
        }

        return colour;
    }

    /// <summary>
    /// A status colour has arrived when it clears the body floor on the panel
    /// and at least the secondary floor on the highlight fills.
    ///
    /// Not the body floor on all three: the highlight is a transient state, and
    /// holding a colour to the full floor on a mid-tone fill costs it either its
    /// saturation or its hue, which for green and amber is the whole message.
    /// </summary>
    private static bool StatusReads(Vector4 colour, Surfaces on, bool apca) =>
        BodyReads(colour, on.Panel, apca) && SecondaryOnHighlight(colour, on, apca);

    private static bool SecondaryOnHighlight(Vector4 colour, Surfaces on, bool apca) =>
        SecondaryReads(colour, on.Hovered, apca) && SecondaryReads(colour, on.Selected, apca);

    /// <summary>
    /// How far dimmed text has to stay from the body text to still read as
    /// dimmed. A distinctness floor rather than a legibility one - the other
    /// floors ask whether a colour can be read, this one asks whether it is
    /// still saying "lesser".
    /// </summary>
    private static float DimGap(bool apca) => apca ? 12f : 1.25f;

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// Makes a colour lighter without washing it out: raises its value and
    /// leaves hue and saturation where they are.
    ///
    /// The counterpart to blending towards black, which gets this for free -
    /// scaling every channel down keeps the ratios between them, so the hue and
    /// the saturation both survive. Blending towards white does not: it pulls
    /// every channel towards the same number, which is exactly what desaturates
    /// a colour, and a status colour that has lost its saturation has lost the
    /// thing it was there to say.
    ///
    /// Saturation is only spent once value is exhausted. A colour already at
    /// full brightness has nowhere else to go, and a pale version of the right
    /// hue is still better than the wrong one.
    /// </summary>
    private static Vector4 Brighten(Vector4 colour, float amount)
    {
        var (hue, saturation, value) = ToHsv(colour);

        // Value climbs by a flat step rather than by a fraction of what is left.
        // A proportional step never actually arrives - from 0.85 it needs about
        // fifty passes to come within a thousandth of the ceiling, and the loop
        // calling this gives up long before that - so saturation would never
        // get its turn and the colour would sit just short of full brightness
        // for ever. Clear Green is where that showed: its panel is green, so its
        // green status colour has the furthest to travel and was the one left
        // below the floor.
        if (value < 1f)
            value = MathF.Min(1f, value + amount);
        else
            saturation = MathF.Max(0f, saturation - (saturation * amount));

        return FromHsv(hue, saturation, value, colour.W);
    }

    private static (float Hue, float Saturation, float Value) ToHsv(Vector4 colour)
    {
        var max = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
        var min = MathF.Min(colour.X, MathF.Min(colour.Y, colour.Z));
        var chroma = max - min;

        var hue = 0f;

        if (chroma > 0.000001f)
        {
            if (max == colour.X)
                hue = 60f * (((colour.Y - colour.Z) / chroma) % 6f);
            else if (max == colour.Y)
                hue = 60f * (((colour.Z - colour.X) / chroma) + 2f);
            else
                hue = 60f * (((colour.X - colour.Y) / chroma) + 4f);
        }

        if (hue < 0f)
            hue += 360f;

        return (hue, max <= 0.000001f ? 0f : chroma / max, max);
    }

    private static Vector4 FromHsv(float hue, float saturation, float value, float alpha)
    {
        var chroma = value * saturation;
        var sector = hue / 60f;
        var second = chroma * (1f - MathF.Abs((sector % 2f) - 1f));
        var offset = value - chroma;

        var (r, g, b) = ((int)MathF.Floor(sector) % 6) switch
        {
            0 => (chroma, second, 0f),
            1 => (second, chroma, 0f),
            2 => (0f, chroma, second),
            3 => (0f, second, chroma),
            4 => (second, 0f, chroma),
            _ => (chroma, 0f, second),
        };

        return new Vector4(r + offset, g + offset, b + offset, alpha);
    }

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

        if (BodyReads(text, preferred, apca))
        {
            // A small lift off a very dark, saturated panel barely registers -
            // Classic FF's idle tab landed at 1.18:1. Keep going until the
            // control is a distinct object, stopping the moment the label
            // would suffer for it.
            for (var step = 0; step < 8 && !ShapeReads(preferred, ground); step++)
            {
                var further = Mix(preferred, toward, 0.10f);
                if (!BodyReads(text, further, apca))
                    break;

                preferred = further;
            }

            return preferred;
        }

        // The requested lift costs the label too much - but the answer is a
        // smaller lift, not the opposite direction.
        //
        // This used to turn round here, and that was wrong in a way only the
        // light themes showed: a selected row came out *lighter* than the panel
        // on Light, Clear White and Clear Pink. The game sinks its controls into
        // a light panel rather than raising them out of it, and a highlight that
        // goes the other way reads as the row being lifted off the list instead
        // of pressed into it - the opposite of "this one".
        //
        // So the direction is fixed and the depth negotiates. Ease back until
        // the label reads, and take the deepest that does - but stop before the
        // fill stops being a fill.
        //
        // That floor is the whole fix for a highlight nobody could see. Light
        // and Clear Pink carry dark text on a panel fitted to barely clear the
        // body floor, so sinking a fill by even a few percent costs the label
        // its last few Lc, and this walk used to run to the end of its rope and
        // hand back a row highlight 1.02:1 against the panel. A control eased
        // until it is invisible has not been saved, it has been deleted.
        //
        // The step is small because on those panels the depths that satisfy
        // both tests are a narrow band, and the coarse ladder this used to walk
        // stepped straight over it - landing either just too pale to read or
        // just too faint to see, on either side of the answer.
        var eased = preferred;

        // The deepest fill that at least clears the secondary floor. Kept as
        // the walk passes it, because it is only wanted if the body floor turns
        // out to be unreachable at any visible depth, and by then the walk has
        // gone too shallow to find it again.
        Vector4? lesser = SecondaryReads(text, preferred, apca) ? preferred : null;

        for (var step = 0; step < 24; step++)
        {
            var shallower = Mix(ground, toward, lift * 0.90f);

            if (!ShapeReads(shallower, ground))
                break;

            lift *= 0.90f;
            eased = shallower;

            if (BodyReads(text, eased, apca))
                return eased;

            lesser ??= SecondaryReads(text, eased, apca) ? eased : null;
        }

        // Nothing at a visible depth satisfies the body floor, which happens on
        // a light panel because sinking the fill moves it towards the dark text
        // sitting on it. Held to the secondary floor instead of abandoned: a
        // control fill is a transient state rather than a column of prose, and
        // the game's own escape - flipping the label pale - is not available
        // when one Text colour serves the whole window. The hand-drawn tab strip
        // does have that escape, and uses it; see Tabs.
        if (lesser is { } readable)
            return readable;

        // Not even the secondary floor, anywhere a fill would still be visible.
        // Only here is visibility finally traded away, because a label nobody
        // can read is the one failure worse than furniture nobody can see.
        var settled = eased;

        for (var step = 0; step < 7 && !SecondaryReads(text, settled, apca); step++)
        {
            lift *= 0.65f;
            settled = Mix(ground, toward, lift);
        }

        return settled;
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
        //
        // Against what the plain fill actually achieved, not against the body
        // floor flat. On a light panel the plain fill is itself held to the
        // secondary floor - it cannot reach the body one at any visible depth -
        // so demanding the body floor of the tinted version asked it to beat a
        // mark the colour it is walking back towards already fails, and the
        // accent could never survive on the three themes at all.
        var floor = MathF.Min(BodyFloor(apca), TextScore(text, plain, apca));

        for (var step = 0; step < 12 && TextScore(text, tinted, apca) < floor; step++)
            tinted = Mix(tinted, plain, 0.25f);

        // The tint has to leave the control visible as well as legible. Tinting
        // moves the fill on both axes, and on a light panel a dark accent can
        // carry it back across the panel's own luminance - a row highlight that
        // reads perfectly and cannot be seen. Walking back towards the plain
        // fill only ever helps both, so anything that still fails either test
        // gives up its tint rather than its visibility.
        return TextScore(text, tinted, apca) >= floor && ShapeReads(tinted, ground)
            ? tinted
            : plain;
    }

    /// <summary>WCAG relative-luminance contrast ratio, 1:1 to 21:1.</summary>
    private static float ContrastRatio(Vector4 a, Vector4 b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }

    /// <summary>
    /// The floor body text has to clear, on whichever measure the theme is
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
    /// The two numbers are not convertible into one another. They are different
    /// scales, so each floor names both of its values rather than deriving one
    /// from the other - and they are paired here, once, so that no caller can
    /// hold a colour to one tier on one scale and a different tier on the other.
    /// </summary>
    private static float BodyFloor(bool apca) => apca ? Apca.BodyText : 4.5f;

    /// <summary>
    /// The floor for text that is deliberately lesser - dimmed columns, and the
    /// control fills that have nowhere left to go. See <see cref="BodyFloor"/>
    /// for why both scales appear.
    /// </summary>
    private static float SecondaryFloor(bool apca) => apca ? Apca.SecondaryText : 3f;

    private static bool BodyReads(Vector4 text, Vector4 background, bool apca) =>
        TextScore(text, background, apca) >= BodyFloor(apca);

    private static bool SecondaryReads(Vector4 text, Vector4 background, bool apca) =>
        TextScore(text, background, apca) >= SecondaryFloor(apca);

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
