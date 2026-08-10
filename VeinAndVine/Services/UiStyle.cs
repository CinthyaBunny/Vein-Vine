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

/// <summary>Which window frame the plugin's windows wear.</summary>
public enum UiChromeChoice
{
    /// <summary>ImGui's own background and border.</summary>
    Dalamud,

    /// <summary>The game's WindowA panel art, drawn as a nine-slice.</summary>
    GameFrame,
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
    /// <summary>
    /// The game's WindowA panel, split across four dedicated tile textures.
    ///
    /// Deliberately loaded as whole textures with hand-computed UVs rather than
    /// through UldWrapper's part indices: the part tables are undocumented and
    /// move between patches, whereas these four files each hold exactly one
    /// kind of tile, which the names make unambiguous.
    /// </summary>
    private const string CornerTexture = "ui/uld/WindowA_BgNormal_Corner.tex";
    private const string HorizontalTexture = "ui/uld/WindowA_BgNormal_H.tex";
    private const string VerticalTexture = "ui/uld/WindowA_BgNormal_V.tex";
    private const string FillTexture = "ui/uld/WindowA_BgNormal_HV.tex";

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
    /// Extra window flags the current chrome needs. The game frame draws its
    /// own background, so ImGui's would sit on top of it.
    /// </summary>
    public ImGuiWindowFlags ExtraWindowFlags =>
        configuration.Chrome == UiChromeChoice.GameFrame && ChromeReady()
            ? ImGuiWindowFlags.NoBackground
            : ImGuiWindowFlags.None;

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

        Color(ImGuiCol.WindowBg, Alpha(ground, 0.94f));
        Color(ImGuiCol.ChildBg, Alpha(ground, 0f));
        Color(ImGuiCol.PopupBg, Alpha(ground, 0.98f));
        Color(ImGuiCol.Border, Alpha(accent, 0.65f));
        Color(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0.40f));

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
        Color(ImGuiCol.TableBorderStrong, Alpha(accent, 0.70f));
        Color(ImGuiCol.TableBorderLight, Alpha(accent, 0.40f));
        Color(ImGuiCol.TableRowBg, Alpha(ground, 0f));
        Color(ImGuiCol.TableRowBgAlt, Alpha(text, 0.04f));

        Color(ImGuiCol.ScrollbarBg, Alpha(Mix(ground, text, 0.02f), 0.60f));
        Color(ImGuiCol.ScrollbarGrab, Mix(ground, text, 0.22f));
        Color(ImGuiCol.ScrollbarGrabHovered, Mix(ground, text, 0.30f));
        Color(ImGuiCol.ScrollbarGrabActive, Mix(ground, text, 0.38f));

        Color(ImGuiCol.CheckMark, accent);
        Color(ImGuiCol.Separator, Alpha(accent, 0.55f));
        Color(ImGuiCol.ResizeGrip, Alpha(accent, 0.35f));
        Color(ImGuiCol.ResizeGripHovered, Alpha(accent, 0.65f));
        Color(ImGuiCol.ResizeGripActive, Alpha(accent, 0.85f));

        // Game panels are square-cornered with a thin single border.
        Var(ImGuiStyleVar.WindowRounding, 2f);
        Var(ImGuiStyleVar.ChildRounding, 2f);
        Var(ImGuiStyleVar.FrameRounding, 2f);
        Var(ImGuiStyleVar.PopupRounding, 2f);
        Var(ImGuiStyleVar.ScrollbarRounding, 2f);
        Var(ImGuiStyleVar.TabRounding, 2f);
        Var(ImGuiStyleVar.WindowBorderSize, 1f);
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
    /// The background does not come from the sheet. Clear Blue and Clear Green
    /// have no blue or green row anywhere in all 204 of them - the game tints
    /// its window textures for that, rather than storing a colour - so those
    /// two get a hand-picked ground and the rest use row 7, which is the tone
    /// the game itself grounds each theme on.
    /// </summary>
    private static Palette? BuildPalette(UiThemeChoice theme)
    {
        try
        {
            var sheet = Service.DataManager.GetExcelSheet<UIColor>();

            if (!sheet.TryGetRow(1, out var textRow) ||
                !sheet.TryGetRow(3, out var dimRow) ||
                !sheet.TryGetRow(7, out var groundRow) ||
                !sheet.TryGetRow(22, out var accentRow))
                return null;

            var ground = theme switch
            {
                UiThemeChoice.ClearBlue => new Vector4(0.055f, 0.090f, 0.165f, 1f),
                UiThemeChoice.ClearGreen => new Vector4(0.047f, 0.114f, 0.075f, 1f),
                _ => Rgba(Column(groundRow, theme)),
            };

            return new Palette(
                ground,
                Rgba(Column(textRow, theme)),
                Rgba(Column(dimRow, theme)),
                Rgba(Column(accentRow, theme)));
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Could not read the {theme} theme from the UIColor sheet.");
            return null;
        }
    }

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

    /// <summary>Blends RGB towards <paramref name="towards"/>, keeping a's alpha.</summary>
    private static Vector4 Mix(Vector4 a, Vector4 towards, float amount) => new(
        a.X + ((towards.X - a.X) * amount),
        a.Y + ((towards.Y - a.Y) * amount),
        a.Z + ((towards.Z - a.Z) * amount),
        a.W);

    private static Vector4 Alpha(Vector4 colour, float alpha) =>
        new(colour.X, colour.Y, colour.Z, alpha);

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

    /// <summary>
    /// Draws the game's window panel behind the current window's content. Call
    /// it as the first thing in Draw, so everything else lands on top.
    /// </summary>
    public void DrawChrome()
    {
        if (configuration.Chrome != UiChromeChoice.GameFrame || !ChromeReady())
            return;

        var corner = Texture(CornerTexture);
        var horizontal = Texture(HorizontalTexture);
        var vertical = Texture(VerticalTexture);
        var fill = Texture(FillTexture);

        if (corner is null || horizontal is null || vertical is null || fill is null)
            return;

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        // Each corner tile is one quadrant of the corner texture.
        var cw = corner.Width / 2f;
        var ch = corner.Height / 2f;

        // Nothing sensible to draw if the window is smaller than its own frame.
        if (max.X - min.X < cw * 2 || max.Y - min.Y < ch * 2)
            return;

        var drawList = ImGui.GetWindowDrawList();

        var innerMin = new Vector2(min.X + cw, min.Y + ch);
        var innerMax = new Vector2(max.X - cw, max.Y - ch);

        // Centre fill first, then edges, then corners on top.
        Image(fill, innerMin, innerMax, Vector2.Zero, Vector2.One);

        Image(horizontal, new Vector2(innerMin.X, min.Y), new Vector2(innerMax.X, innerMin.Y),
            new Vector2(0f, 0f), new Vector2(1f, 0.5f));
        Image(horizontal, new Vector2(innerMin.X, innerMax.Y), new Vector2(innerMax.X, max.Y),
            new Vector2(0f, 0.5f), new Vector2(1f, 1f));

        Image(vertical, new Vector2(min.X, innerMin.Y), new Vector2(innerMin.X, innerMax.Y),
            new Vector2(0f, 0f), new Vector2(0.5f, 1f));
        Image(vertical, new Vector2(innerMax.X, innerMin.Y), new Vector2(max.X, innerMax.Y),
            new Vector2(0.5f, 0f), new Vector2(1f, 1f));

        Image(corner, min, innerMin, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        Image(corner, new Vector2(innerMax.X, min.Y), new Vector2(max.X, innerMin.Y),
            new Vector2(0.5f, 0f), new Vector2(1f, 0.5f));
        Image(corner, new Vector2(min.X, innerMax.Y), new Vector2(innerMin.X, max.Y),
            new Vector2(0f, 0.5f), new Vector2(0.5f, 1f));
        Image(corner, innerMax, max, new Vector2(0.5f, 0.5f), new Vector2(1f, 1f));

        void Image(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap wrap,
                   Vector2 topLeft, Vector2 bottomRight, Vector2 uv0, Vector2 uv1) =>
            ImGui.AddImage(drawList, wrap.Handle, topLeft, bottomRight, uv0, uv1);
    }

    /// <summary>True once every piece of the frame has finished loading.</summary>
    private bool ChromeReady() =>
        Texture(CornerTexture) is not null &&
        Texture(HorizontalTexture) is not null &&
        Texture(VerticalTexture) is not null &&
        Texture(FillTexture) is not null;

    /// <summary>
    /// A game texture, or null while it loads. Dalamud owns and caches the
    /// wrap, so this must not be disposed and is cheap to call per frame.
    /// </summary>
    private static Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? Texture(string path)
    {
        try
        {
            return Service.TextureProvider.GetFromGame(path).GetWrapOrDefault();
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, $"Could not load the game texture {path}.");
            return null;
        }
    }
}
