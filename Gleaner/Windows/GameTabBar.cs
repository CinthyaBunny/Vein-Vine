using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Gleaner.Windows;

/// <summary>
/// A tab strip shaped like the game's.
///
/// FFXIV tabs are elongated hexagons: flat top and bottom, and both ends drawn
/// to a point at mid-height. ImGui's own tab bar offers a corner radius and
/// nothing else, which is why this is drawn by hand.
///
/// Shape and spacing are the three constants below - point sharpness, the gap
/// between tabs, and the padding around a label - so the strip can be retuned
/// without touching the drawing.
///
/// Doing so buys more than the outline. ImGui has a single Text colour for the
/// whole window, so its tabs could never carry the pale label on a dark tab
/// that the game uses even on its light themes; here each tab picks its own.
/// See <see cref="Services.UiStyle.Tabs"/>.
///
/// Deliberately not immediate-mode in ImGui's begin/end style. The whole strip
/// is drawn in one call and returns the index that is now selected, so the
/// caller draws only the content it needs - no per-tab Begin, no hidden state,
/// and a set of tabs that can change from frame to frame (the Nodes tab
/// disappears while the list is docked) costs nothing to express.
///
/// The shape is also available on its own through <see cref="DrawButton"/>, for
/// a control that belongs to this furniture without being a tab.
/// </summary>
public static class GameTabBar
{
    /// <summary>
    /// How far each pointed end is drawn in, as a fraction of the tab height.
    /// Larger is a sharper point; 0 would square the ends off.
    /// </summary>
    private const float SlantRatio = 0.42f;

    /// <summary>
    /// Space between neighbouring tabs, in unscaled pixels - multiplied by the
    /// UI scale, so it holds its proportions on a scaled display.
    ///
    /// A negative value tucks each tab's point into the notch of the next, the
    /// way the game packs its own strip; positive separates them.
    /// </summary>
    private const float GapPixels = 10f;

    /// <summary>Space either side of a tab's label, in unscaled pixels.</summary>
    private const float LabelPadding = 12f;

    /// <summary>
    /// Draws the strip and returns the selected index, which is
    /// <paramref name="selected"/> unless a tab was clicked this frame.
    ///
    /// <paramref name="tooltips"/> is positional against
    /// <paramref name="labels"/>; a null list, a short one, or an empty entry
    /// all mean that tab has nothing to say.
    /// </summary>
    public static int Draw(
        Plugin plugin,
        string id,
        IReadOnlyList<string> labels,
        int selected,
        IReadOnlyList<string>? tooltips = null)
    {
        if (labels.Count == 0)
            return selected;

        var scale = ImGuiHelpers.GlobalScale;
        var colours = plugin.UiStyle.Tabs;

        var height = ImGui.GetFrameHeight();
        var slant = height * SlantRatio;
        var padding = LabelPadding * scale;
        var gap = GapPixels * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var widths = new float[labels.Count];
        for (var i = 0; i < labels.Count; i++)
            widths[i] = ImGui.CalcTextSize(labels[i]).X + (padding * 2) + (slant * 2);

        var advance = new float[labels.Count];
        for (var i = 0; i < labels.Count; i++)
            advance[i] = widths[i] + gap;

        // Every tab but the last contributes its advance; the last contributes
        // its whole width, since nothing tucks into it.
        var stripWidth = widths[^1];
        for (var i = 0; i < labels.Count - 1; i++)
            stripWidth += advance[i];

        var clicked = -1;
        var hovered = -1;
        var x = origin.X;

        ImGui.PushID(id);

        for (var i = 0; i < labels.Count; i++)
        {
            // The whole tab is clickable, points included. That is only safe
            // while the gap keeps neighbours apart - close it up and the boxes
            // would start overlapping, so narrow them by the slant if GapPixels
            // ever goes negative.
            var hitInset = gap < 0 ? slant * 0.5f : 0f;

            ImGui.SetCursorScreenPos(new Vector2(x + hitInset, origin.Y));
            ImGui.InvisibleButton($"##tab{i}", new Vector2(widths[i] - (hitInset * 2), height));

            if (ImGui.IsItemHovered())
            {
                hovered = i;

                if (tooltips is not null && i < tooltips.Count && tooltips[i].Length > 0)
                    ImGui.SetTooltip(tooltips[i]);
            }

            if (ImGui.IsItemClicked())
                clicked = i;

            x += advance[i];
        }

        // Unselected first, then the selected one, so the current tab's outline
        // is never cut into by the tab that follows it - which matters if the
        // gap is ever taken negative and the shapes start to overlap.
        x = origin.X;
        for (var i = 0; i < labels.Count; i++)
        {
            if (i != selected)
                DrawTab(drawList, labels[i], new Vector2(x, origin.Y), widths[i], height, slant,
                    i == hovered ? colours.Hovered : colours.Idle, colours.IdleLabel, colours.Edge, false);

            x += advance[i];
        }

        x = origin.X;
        for (var i = 0; i < labels.Count; i++)
        {
            if (i == selected)
            {
                DrawTab(drawList, labels[i], new Vector2(x, origin.Y), widths[i], height, slant,
                    colours.Active, colours.ActiveLabel, colours.Edge, true);
                break;
            }

            x += advance[i];
        }

        ImGui.PopID();

        // Claim the strip so whatever follows starts below it. The running x
        // cannot be used here: the selected-tab pass stops as soon as it finds
        // its tab, so it is left somewhere in the middle of the row.
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(stripWidth, height));

        return clicked >= 0 ? clicked : selected;
    }

    /// <summary>
    /// Width the strip's shape needs to hold <paramref name="label"/>, for
    /// callers that must place a button before drawing it.
    /// </summary>
    public static float MeasureButton(string label)
    {
        var height = ImGui.GetFrameHeight();
        var slant = height * SlantRatio;
        return ImGui.CalcTextSize(label).X + (LabelPadding * ImGuiHelpers.GlobalScale * 2) + (slant * 2);
    }

    /// <summary>
    /// One hexagon on its own, for a control that should read as part of the
    /// same furniture as the strip without being a tab. Returns true on click.
    ///
    /// <paramref name="width"/> of zero sizes to the label; pass a width to pin
    /// it, which is what a control whose text changes needs so its neighbours
    /// do not shuffle.
    ///
    /// The label is only ever drawn, never used as an identifier - the hit box
    /// carries <paramref name="id"/> instead - so a caption that changes every
    /// frame costs nothing here, where an ImGui button would need a "###" to
    /// keep its identity.
    /// </summary>
    public static bool DrawButton(Plugin plugin, string id, string label, float width = 0f, bool active = false)
    {
        var colours = plugin.UiStyle.Tabs;
        var height = ImGui.GetFrameHeight();
        var slant = height * SlantRatio;

        if (width <= 0f)
            width = MeasureButton(label);

        // Captured before the hit box, which advances the cursor past it.
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton(id, new Vector2(width, height));

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        DrawTab(drawList, label, origin, width, height, slant,
            active ? colours.Active : hovered ? colours.Hovered : colours.Idle,
            active ? colours.ActiveLabel : colours.IdleLabel,
            colours.Edge,
            active);

        return clicked;
    }

    private static void DrawTab(
        ImDrawListPtr drawList,
        string label,
        Vector2 position,
        float width,
        float height,
        float slant,
        Vector4 fill,
        Vector4 labelColour,
        Vector4 edge,
        bool selected)
    {
        var x0 = position.X;
        var x1 = position.X + width;
        var y0 = position.Y;
        var y1 = position.Y + height;
        var ym = (y0 + y1) * 0.5f;

        // Flat top and bottom, a point at each end.
        Span<Vector2> hexagon =
        [
            new(x0 + slant, y0),
            new(x1 - slant, y0),
            new(x1, ym),
            new(x1 - slant, y1),
            new(x0 + slant, y1),
            new(x0, ym),
        ];

        // PathFillConvex consumes the path, so it is walked twice - once to
        // fill, once to outline.
        foreach (var point in hexagon)
            ImGui.PathLineTo(drawList, point);

        ImGui.PathFillConvex(drawList, ImGui.GetColorU32(fill));

        foreach (var point in hexagon)
            ImGui.PathLineTo(drawList, point);

        ImGui.PathStroke(
            drawList,
            ImGui.GetColorU32(new Vector4(edge.X, edge.Y, edge.Z, selected ? 0.85f : 0.35f)),
            ImDrawFlags.Closed,
            selected ? 1.5f : 1f);

        var textSize = ImGui.CalcTextSize(label);
        ImGui.AddText(
            drawList,
            new Vector2(x0 + ((width - textSize.X) * 0.5f), y0 + ((height - textSize.Y) * 0.5f)),
            ImGui.GetColorU32(labelColour),
            label);
    }
}
