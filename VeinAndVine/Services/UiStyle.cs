using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace VeinAndVine.Services;

/// <summary>Which typeface the plugin's windows draw with.</summary>
public enum UiFontChoice
{
    /// <summary>Whatever the user configured Dalamud to use.</summary>
    Dalamud,

    /// <summary>Axis, the game's own UI typeface.</summary>
    GameAxis,
}

/// <summary>Which colour and metric set the plugin's windows use.</summary>
public enum UiThemeChoice
{
    /// <summary>Whatever the user configured Dalamud to use.</summary>
    Dalamud,

    /// <summary>Hand-matched to the game's dark blue panels and warm off-white text.</summary>
    GameDark,
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
        if (configuration.Theme != UiThemeChoice.GameDark)
            return;

        // Sampled from the game's own panels: near-black blue, warm off-white
        // text, and the muted gold the UI uses for borders and highlights.
        Color(ImGuiCol.WindowBg, new Vector4(0.043f, 0.055f, 0.078f, 0.94f));
        Color(ImGuiCol.ChildBg, new Vector4(0.000f, 0.000f, 0.000f, 0.00f));
        Color(ImGuiCol.PopupBg, new Vector4(0.055f, 0.067f, 0.094f, 0.98f));
        Color(ImGuiCol.Border, new Vector4(0.435f, 0.392f, 0.290f, 0.65f));
        Color(ImGuiCol.BorderShadow, new Vector4(0.000f, 0.000f, 0.000f, 0.40f));

        Color(ImGuiCol.Text, new Vector4(0.937f, 0.925f, 0.878f, 1.00f));
        Color(ImGuiCol.TextDisabled, new Vector4(0.596f, 0.580f, 0.522f, 1.00f));

        Color(ImGuiCol.FrameBg, new Vector4(0.102f, 0.114f, 0.145f, 0.90f));
        Color(ImGuiCol.FrameBgHovered, new Vector4(0.169f, 0.184f, 0.220f, 0.95f));
        Color(ImGuiCol.FrameBgActive, new Vector4(0.216f, 0.231f, 0.271f, 1.00f));

        Color(ImGuiCol.TitleBg, new Vector4(0.055f, 0.067f, 0.094f, 1.00f));
        Color(ImGuiCol.TitleBgActive, new Vector4(0.086f, 0.102f, 0.145f, 1.00f));
        Color(ImGuiCol.TitleBgCollapsed, new Vector4(0.043f, 0.055f, 0.078f, 0.80f));

        Color(ImGuiCol.Header, new Vector4(0.196f, 0.231f, 0.310f, 0.75f));
        Color(ImGuiCol.HeaderHovered, new Vector4(0.259f, 0.310f, 0.416f, 0.85f));
        Color(ImGuiCol.HeaderActive, new Vector4(0.310f, 0.373f, 0.494f, 0.95f));

        Color(ImGuiCol.Button, new Vector4(0.137f, 0.157f, 0.204f, 0.95f));
        Color(ImGuiCol.ButtonHovered, new Vector4(0.220f, 0.251f, 0.318f, 1.00f));
        Color(ImGuiCol.ButtonActive, new Vector4(0.290f, 0.325f, 0.404f, 1.00f));

        Color(ImGuiCol.Tab, new Vector4(0.086f, 0.098f, 0.129f, 1.00f));
        Color(ImGuiCol.TabHovered, new Vector4(0.259f, 0.310f, 0.416f, 1.00f));
        Color(ImGuiCol.TabActive, new Vector4(0.165f, 0.192f, 0.251f, 1.00f));
        Color(ImGuiCol.TabUnfocused, new Vector4(0.071f, 0.082f, 0.110f, 1.00f));
        Color(ImGuiCol.TabUnfocusedActive, new Vector4(0.126f, 0.145f, 0.188f, 1.00f));

        Color(ImGuiCol.TableHeaderBg, new Vector4(0.114f, 0.129f, 0.169f, 1.00f));
        Color(ImGuiCol.TableBorderStrong, new Vector4(0.353f, 0.322f, 0.243f, 0.70f));
        Color(ImGuiCol.TableBorderLight, new Vector4(0.235f, 0.220f, 0.176f, 0.50f));
        Color(ImGuiCol.TableRowBg, new Vector4(0.000f, 0.000f, 0.000f, 0.00f));
        Color(ImGuiCol.TableRowBgAlt, new Vector4(1.000f, 1.000f, 1.000f, 0.03f));

        Color(ImGuiCol.ScrollbarBg, new Vector4(0.031f, 0.039f, 0.055f, 0.60f));
        Color(ImGuiCol.ScrollbarGrab, new Vector4(0.243f, 0.259f, 0.310f, 1.00f));
        Color(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.318f, 0.337f, 0.396f, 1.00f));
        Color(ImGuiCol.ScrollbarGrabActive, new Vector4(0.388f, 0.408f, 0.475f, 1.00f));

        Color(ImGuiCol.CheckMark, new Vector4(0.847f, 0.741f, 0.463f, 1.00f));
        Color(ImGuiCol.Separator, new Vector4(0.353f, 0.322f, 0.243f, 0.55f));
        Color(ImGuiCol.ResizeGrip, new Vector4(0.435f, 0.392f, 0.290f, 0.35f));
        Color(ImGuiCol.ResizeGripHovered, new Vector4(0.545f, 0.490f, 0.361f, 0.65f));
        Color(ImGuiCol.ResizeGripActive, new Vector4(0.647f, 0.584f, 0.435f, 0.85f));

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
