# Vein & Vine — design notes

Why the plugin is built the way it is. For how to build, run and ship it, see
the [README](../README.md).

## Architecture

The deliberate split is **game-coupled** vs **pure**. Everything in the pure
column is a function of its inputs and can be unit tested with no game running.

| File | Coupling | Purpose |
|---|---|---|
| `Plugin.cs` | Dalamud | Wiring: services, config, commands, window lifecycle |
| `Service.cs` | Dalamud | `[PluginService]` container |
| `Services/WeatherService.cs` | Lumina sheets | Territory → weather via the rate table |
| `Services/NodeDatabase.cs` | filesystem | Loads `Data/nodes.json` |
| `Services/MapUtil.cs` | Lumina sheets | World-space → map-coordinate conversion |
| `Services/EorzeaTime.cs` | **pure** | Eorzea clock + the weather seed roll |
| `Services/PriorityEngine.cs` | **pure** | Availability + sorting, gated behind `IWeatherProvider` |
| `Services/NodeQuery.cs` | **pure** | Filters, the per-item index, picker sorting |
| `Services/ItemInfo.cs` | Lumina, textures | Item icons and descriptions, from the game client |
| `Services/UiStyle.cs` | Lumina, fonts | Optional game font and palette, from the `UIColor` sheet |
| `Models/` | **pure** | `GatherNode`, `WishlistEntry` |
| `Windows/MainWindow.cs` | ImGui | The one window: tab bar, Display and Appearance |
| `Windows/NodeListTab.cs` | ImGui | The node list, and the map flag |
| `Windows/NodeListWindow.cs` | ImGui | Optional docked panel hosting the same node list |
| `Windows/GameTabBar.cs` | ImGui | The hand-drawn hexagonal tab strip |
| `Windows/UiShared.cs` | ImGui | Job and duration formatting, icons, sort-spec bridge |
| `tools/NodeGen/` | Lumina, build-time | Regenerates `Data/nodes.json` from game sheets |

`PriorityEngine` depends on `IWeatherProvider`, not `WeatherService`, specifically
so it can be tested against a fake clock.

The filtering and sorting are all in the pure column — `NodeFilter.Matches`,
`NodeQuery.SortItems`, `PriorityEngine.Sort` — so the parts that decide what you
see and in what order can be exercised against the shipped dataset with no game
running. The windows only draw.

[`tools/NodeGen`](../tools/NodeGen) is in the solution so it keeps compiling
against the model, but it is a standalone exe and is never shipped. It touches
only the Dalamud-free parts of the plugin (`Models`, the pure
`MapUtil.WorldToMap` overload, `NodeDatabase.Parse`), so it runs with no game or
Dalamud host attached.

## One window, four tabs

**Nodes** leads because that is the question you keep the window open to answer;
**Wishlist**, **Display** and **Appearance** configure it and sit behind it in
the same frame. They used to be two separate windows, which meant hunting for
the second one every time you wanted to track something.

### The node list

One row per node, coloured green (up, and going to expire), amber (up within
five minutes), grey (waiting), or ordinary text for a node that is simply always
there. That fourth state matters: three quarters of the dataset is always up,
and painting it the same green as a node with four minutes left would drown out
the only rows worth hurrying for. For the same reason the summary counts only
*timed* nodes as "up now", and the default sort puts what expires above what
doesn't.

Sort state deliberately lives in ImGui's own `.ini`, not in `Configuration`.
ImGui already persists table column sorting, and a second copy in our config
would only get a chance to disagree with the arrows in the header.

### The item picker, and why the tabs overlap

One row per *item*, not per node. The dataset is node-shaped, so the same item
appears in several zones with several windows; `NodeQuery` collapses that into
one row per item, with the zone count and the union of its windows.

Items are split across **All / Miner / Botanist** sub-tabs — 517 mining and 631
botany out of 1,050, because **98 items have both mining and botany nodes** and
so appear on both tabs. That overlap is why `GatherItem` carries a `Jobs` flag
set rather than one `NodeType`: collapsing it to a single job hides each of
those items from one of the two tabs.

Each single-job tab carries a **second strip** for the finer split the game
makes within it — `All / Mining / Quarrying` under Miner, `All / Logging /
Harvesting` under Botanist, named as the `GatheringType` sheet names them. The
dataset used to throw this away, folding gathering types 0 and 1 into "Mining"
and 2 and 3 into "Botany", so nodes now carry a `method` alongside their `type`.
The same overlap applies one level down: 16 items per job come off both kinds of
node and appear under both sub-tabs.

`All` has no sub-strip. It already spans both jobs, so a five-way method strip
under it would be a second job filter in a different hat.

Narrowing to a method rebuilds the index rather than filtering it. A row is a
*summary* of the nodes behind it — its zones, its level, whether it is timed at
all — so summarising every node and then dropping rows describes an item by
nodes the sub-tab has excluded: quarrying rows listing zones you can only mine
in, and levels no quarrying node actually reaches. `BuildItemIndex` takes the
`MethodFilter` and applies it before the grouping, and the window keeps one
index per scope, built on first use and thrown away on reload. There are seven
scopes and a build is a single pass over 1,587 nodes, so this costs nothing that
a frame would notice.

**Every tab is labelled with what it would show** — `All (1,050)`, `Miner (517)`,
`Botanist (631)` — and the number on a tab is the number of rows you get for
clicking it, narrowing included: a Miner tab pinned to Quarrying reads `Miner
(202)`. Those counts come from the filtered list, not from the population
behind it. Search "ore" and the strip reads `All (143)`, `Miner (118)`,
`Botanist (27)`. There is one definition of "in this tab", `Matching`, and the
rows, every label and the footer all run through it, so a filter added there
moves all of them together instead of leaving the labels describing the list as
it was two filters ago.

The counts reconcile at every level: a parent equals its two children less the
items in both. `1,050 = 517 + 631 − 98` across the jobs, `517 = 331 + 202 − 16`
across the miner's methods. The overlap is not a constant — under a search for
"ore" the job overlap falls to 2 and the miner overlap to 1 — which is exactly
why it has to be counted rather than remembered.

Three labels that add up to more than their parent read as a miscount unless the
difference is stated, so **hovering any tab spells the subtraction out**, and the
footer offers the same reconciliation. Both run through one `Reconciliation`
helper rather than two descriptions of the same numbers, which could otherwise
drift apart.

A narrowed job is the one case where a label and the sum disagree — the sum uses
the jobs in full, because that is what `All` is actually made of — so the
tooltip says which job is narrowed and what its label is counting instead.

Recounting is seven predicated passes over at most a thousand rows, on the
frames where a filter actually moved. The footer keeps one figure that isn't on
a label — the tab's population with the filters cleared — and only prints it
once a filter has removed something, since "517 of 517" is noise.

Each tab owns its sort order, its own method narrowing and its own filtered
list, so sorting Miner by level or pinning it to Quarrying doesn't disturb how
Botanist was left, and switching between them is instant. The `Job` column hides
itself on the single-job tabs, where it has nothing to say. `All` is kept
because searching for an item whose job you don't know is a real thing you do.

The filter row sits *above* the tabs and applies to all of them — a search you
had to retype on every tab switch would be worse than no tabs. It holds text,
zone, a level range you type into, timed-only, and tracked-only. `Track all
shown` applies to exactly the rows the filters left on the current tab, which
is what makes "every level 80 botany item" a two-click operation.

At 1,050 rows the picker is clipped with `ImGuiListClipper` and its filter/sort
result is cached behind a key of its inputs, so scrolling submits only the
visible slice and a still frame does no work at all.

### The level boxes

`Lv [1] to [100]` rejects at the keystroke rather than clamping at the commit.
`NodeFilter.AcceptsLevelKeystroke` is handed the box's text, the selection and
the character, and answers whether *the text the box would then hold* is still a
gathering level — which is the only way 1–100 can hold, since every digit is
legal on its own and still turns 10 into 105. ImGui runs pasted text through the
same filter one character at a time, so pasting `abc` or `9999` is refused the
way typing it is.

The point is that there is then no invalid state: nothing to clamp, no error to
show, and nothing rewritten under you when you click away — which is what a
clamp does, and is hard to tell apart from the box eating your input. The rule
is pure and lives next to the range it enforces, so the harness walks every
state the box can reach through every printable key at every cursor and
selection position: 54,910 keystrokes reaching **exactly 101 states — the empty
box and the hundred levels**, with all hundred typeable.

Empty is the one state the filter allows that isn't a level. It reads as "no
bound from this end" while you retype, and fills back in when you leave the box.
The boxes select-all on focus, without which a box reading `100` would refuse
every digit you typed — each one making a number over the limit — and read as
simply broken. Filtering reads an ordered view of the two boxes, so a min above
the max reads as a range typed backwards rather than as nothing.

### Icons and descriptions

Both lists show the item's game icon, and hovering a row gives the item's own
in-game description plus its gathering requirements — perception needed for the
full yield, and the node's star rating.

**None of that is fetched over the network.** The client already has the icons
and the localised text on disk, so `ItemInfo` reads them through Dalamud's
`ITextureProvider` and the `Item` sheet. XIVAPI would trade that for latency, a
cache, a rate limit, and an offline failure mode in exchange for the same
bytes. The only thing baked into `nodes.json` is the two-byte `iconId`, because
every visible row needs it; descriptions are read live for the single row under
the cursor.

Icons load asynchronously, so a row reserves the space whether or not the
texture has arrived — otherwise the whole list twitches on first paint.

## Wearing the game's clothes

The **Appearance** tab draws everything from the client rather than from bundled
assets:

| Setting | What it uses |
|---|---|
| Font | **Axis**, via `FontAtlas.NewGameFontHandle` — the typeface the game's own UI draws with, and the single biggest contributor to looking native |
| Theme | All eight of the game's UI themes, colouring the panel, its border, and every control inside it |

**Theme is one setting, not two.** Picking one of the game's themes means
wanting the whole look, and the border is drawn from that theme's own colours
anyway, so separating the palette from the frame only allowed combinations
nobody wanted.

Both are **on by default** — a plugin window sitting next to the game's own
windows may as well look like one — and each drops back to Dalamud's default
with a click. An explicit choice outranks the default: only a config that
predates these settings picks the game look up automatically.

All 55 `ImGuiCol` slots are set, not just the obvious ones. The leftovers are
what give a half-themed window away: a stock blue text selection or nav outline
sitting in the middle of an otherwise game-coloured panel. Every border width is
pinned too, rather than inherited from whatever the user's Dalamud style says.

That includes the slots for a tab ImGui draws itself. The plugin's own strip is
hand-drawn and never uses them, but Dalamud ships ImGui's docking branch, so a
user who docks the window into another gets a dock-node tab — one that would
otherwise be stock blue in an otherwise game-coloured setup.

### Why the window art isn't used

An earlier version drew the game's own `WindowA_BgNormal_*` tiles as a
nine-slice. It was dropped, for two reasons that only turned up by reading the
pixels:

- **The tiles are near-black neutral grey** (R≈G≈B≈48), and ImGui tints images
  by *multiplying*, which can only darken. No tint could ever produce a light
  panel for Light, Clear White or Clear Pink.
- **The set isn't a nine-slice.** `Corner` is a 32×96 strip holding the panel's
  entire vertical profile — transparent padding, a rounded cap, a long gradient
  body, another cap — and it's horizontally symmetric. Treating it as one
  quadrant of a 2×2 atlas made every "corner" 16×48, which is where the heavy
  dark band across the top of each window came from.

A themed `WindowBg` plus a one-pixel `Border` gets closer to how a game panel
actually reads at its edges, works identically across all eight themes, and
removed the texture loading, the readiness gating, and a failure mode where the
window could come out transparent.

### The palettes

The colour themes are **Dark, Light, Classic FF, Clear Blue, Clear White, Clear
Green, Clear Grey and Clear Pink** — all eight, named as the game names them in
System Configuration (it really is "Classic FF"), in the game's own dropdown
order.

`UIColor` has a column for every one, but Lumina has only named the first six.
Clear Grey and Clear Pink come from its two unnamed columns. Two independent
things say which is which: the sheet's named columns run in the same order as
the game's dropdown, so the leftover pair is Grey then Pink; and the data
agrees, since one of them is dark purple text on a white ground, which can only
be Clear Pink. If a future Lumina names those columns properly, the plugin
stops compiling — which is the right way for that to break.

Each palette is derived from four anchors rather than hand-written slot by
slot, so there is one code path instead of eight tables to keep in step:

| Anchor | Source |
|---|---|
| Text | `UIColor` row 1 — inverts correctly, white on Dark, brown on Light |
| Dimmed text | row 3 |
| Accent | row 22 |
| Ground | matched by eye, per theme |

Row 22 rather than the paler row 8 for the accent: row 8 is pure white under
Classic FF, which would sink every border and checkmark into the text. Row 22
stays distinct in all eight and gives Classic FF its pale blue.

**The ground is the one anchor the sheet cannot supply.** Row 7 looks like the
window colour and isn't — it's each theme's *darkest or lightest* tone. It is
pure black for Dark, Clear Blue, Clear Green and Clear Grey alike, and pure
white for both Clear White and Clear Pink, so using it collapsed four themes to
identical black and two to identical white.

The game keeps the real panel colour as a tint on its window textures rather
than as a sheet entry, so the grounds are **sampled from the game's own theme
previews** in System Configuration — the most frequent pixel in the middle of
each preview panel. Reference shots are in
[`Alpha 0.0.1.x Photos/Theme Examples`](../VeinAndVine/Alpha%200.0.1.x%20Photos/Theme%20Examples).

Measuring them mattered, because the themes are far more saturated than they
look in memory:

| Theme | Actual | Guessed first |
|---|---|---|
| Classic FF | `#190090` vivid blue-violet | `#12142A` dark navy |
| Clear White | `#B3B7B9` mid grey | `#ECECEC` near-white |
| Clear Pink | `#E7A7D6` strong pink | `#F0E3E8` blush |
| Light | `#F5D4A9` warm peach | `#DEDACC` grey parchment |

### Everything is fitted to its theme, not just the panel

A palette that only sets the background leaves the interesting colours wrong.
Three guards handle the rest, all of them measuring rather than assuming:

| Guard | What it protects |
|---|---|
| `Readable` | Dimmed text against the panel, floor 3:1 |
| `Legible` | The node list's green and amber against the panel, floor 4.5:1 |
| `Control` | Every control, on **two** fronts at once — visible against the panel (1.25:1) *and* readable underneath its label (4.5:1) |
| `AccentControl` | The same, for the "this one" states — selected rows, the active tab |

All of them ask one question first — is this a light panel or a dark one — and
they ask it through a single `IsLight` helper. Two different brightness
calculations once answered it two different ways; they agreed on all eight
current themes, but a ninth could have had a control lifted one way and its
label chosen for the other.

**`Legible` keeps the hue and moves only the lightness**, because green and
amber *mean* something here — up now, up soon. On the dark themes they stay
light; on Light, Clear White and Clear Pink they invert to a deep green and a
dark olive. This one mattered: the amber was `#F2C759` on Light's `#F5D4A9`
peach, which is the same colour twice.

**`Control` is a two-sided constraint, and that is the whole point.** Sampling
the game's previews shows its controls always stand off the panel — lighter on
the dark themes, darker on the light ones — and that it flips the *label* to
suit when it sinks a control hard. ImGui has a single `Text` colour, so that
escape isn't available: the lift is kept modest, and if the game's direction
would cost the label its contrast, the opposite direction is taken and pushed
until the control is properly distinct.

An earlier version solved only half of this — it mixed toward the text and
pulled back whenever contrast suffered, which protected the label and quietly
dissolved the thing the label sits on. Both constraints are now checked
together, which caught Clear Pink (whose dark text leaves so little headroom
that a darker button is unreadable and a lighter one is invisible) and Classic
FF (whose idle tab barely registered against a very dark, saturated panel).

Every control also gets a **1px border**, pinned by the theme rather than
inherited from the user's Dalamud style, so a control is an object with an edge
rather than just a slightly different shade.

Measured across all eight themes and all seven control states: body text
6.4–15.7:1, status colours 4.5–9.8:1, control fills 1.21–2.80:1 against their
panel, and every label 4.5:1 or better on the control beneath it.

### Shapes

`FrameRounding` is set to half the frame height. ImGui clamps rounding to half
the shorter side, so one number turns a button into a full pill while leaving a
wide search field as a rounded bar with the same end caps — which is the game's
pair of shapes exactly.

**Tabs are drawn by hand**, in [`GameTabBar.cs`](../VeinAndVine/Windows/GameTabBar.cs).
The game's are elongated hexagons — flat top and bottom, both ends drawn to a
point at mid-height. ImGui's tab bar offers a corner radius and nothing else,
so this builds the polygon with `PathLineTo` / `PathFillConvex` / `PathStroke`.

Three constants at the top of the file carry the whole look — `SlantRatio` for
how sharp the points are, `GapPixels` for the spacing between tabs, and
`LabelPadding`. Gap is in unscaled pixels and multiplied by the UI scale, so it
holds up on a scaled display; taking it negative tucks each tab's point into
the notch of the next, the way the game packs its own strip.

Doing so buys more than the outline. **The game uses a dark tab with a pale
label on every theme**, including the light ones — Light's tabs are near-black
lettered in cream, on a peach panel. ImGui could never express that, because a
single `Text` colour has to serve the panel and the tabs alike. Drawing the
strip means each tab picks its own label colour, so this follows the game
rather than compromising with it. The selected tab is also the *darker* one,
which is the opposite of most interfaces and is what the game does: the current
tab is recessed and the rest stand proud of it.

It isn't immediate-mode in ImGui's begin/end style. The whole strip is one call
returning the index now selected, so the caller draws only the content it
needs. That also makes a changing set of tabs free — the Nodes tab vanishes
while the list is docked — where `BeginTabItem` would need the set fixed. Tabs
take an optional parallel list of tooltips for the same reason: a null or short
list simply means a strip has nothing to explain.

The whole tab is clickable, points included, which is only safe while the gap
keeps neighbours apart — the hit boxes narrow by half a slant automatically if
`GapPixels` is taken negative. The selected tab is painted last either way, so
the tab after it can't cut into its edge.

## Docking the node list (experimental)

`Node list` on the Appearance tab moves the list out of its tab and into a
panel welded to the main window's left edge. The Nodes tab disappears while
docked — two copies of the same table, one of them stale-looking, is worse than
one — and both share a single `NodeListTab` instance, so sort state can't drift
between them.

The geometry is deliberately lopsided:

| Dimension | Owner |
|---|---|
| Position | The host — recomputed every frame, so the panel follows it around |
| Height | The host — taken from the host's own size each frame |
| Width | You — the grip in the bottom-left corner; the right edge stays welded |

**The resize grip is hand-drawn on the bottom-left**, and ImGui's own resizing
is switched off (`NoResize`) rather than steered. Its grip is fixed to the
bottom-right, which is the corner that *cannot* move here — the right edge is
welded to the host — and the second grip it offers on the bottom-left only
exists when the user has enabled resize-from-edges globally, which is not
something a plugin should depend on or change.

So the panel draws its own: an `InvisibleButton` in the corner, the mouse delta
subtracted from the width (dragging left grows it, because width is the
distance the left edge has travelled from a fixed right edge), and the same
triangle ImGui uses for its grips, mirrored. With nothing else writing the
size, both axes are then just set outright each frame.

The panel hangs off the host's `IsDrawing`, not its `IsOpen` — a window that is
open but collapsed has no rect to pin to, and the panel would otherwise strand
itself wherever it last saw one. Width is written to the config when the drag
ends, not during it, since each save serialises the whole file.

Font and palette are pushed from `PreDraw` and popped in `PostDraw`, not inside
`Draw` — the window background, border and title bar are drawn by `ImGui.Begin`
itself and would otherwise ignore them. `PushWindowStyle` pops any leftover
state before pushing, so a skipped `PostDraw` can't leak a few style entries per
frame until ImGui asserts.

## Why the dataset is generated

The file is **generated, not hand-written** — see the README for how to run the
generator.

Hand-maintaining it isn't viable. `territoryTypeId` matters more than it looks —
it drives both the weather lookup and the map flag, and a node with a wrong id
silently never goes active — and the spawn windows live in
`GatheringRarePopTimeTable` in a packed-decimal encoding (`160` means one hour
and sixty minutes, i.e. two hours). Reading them out of sqpack makes the dataset
reproducible and re-runnable after a patch.

The generator references the plugin project, so it emits the real `GatherNode`
type and reuses `MapUtil`'s coordinate formula. After writing, it re-reads the
file through the plugin's own `NodeDatabase.Parse` and fails if any node has a
null territory, a zero map id, out-of-range coordinates, no time window, or a
duplicate key.

Always-up nodes earn their place even though they never need watching: without
them the wishlist can only track the 333 timed items, so "where do I get iron
ore" has no answer. They are reported as available with no countdown and shown
as `Always`, and the `Timed only` toggle in both windows hides them when the
question is "what's up right now" instead.

**Spearfishing is excluded.** It is a gathering node in the sheets, but nothing
here models bait or the tug, and neither window offers a Fisher filter — 168
nodes that no UI could reach. One `if` in the generator, marked for when fishing
is handled.

## API notes

- `Service.ObjectTable.LocalPlayer` — moved off `IClientState` in API 15.
- `Service.ObjectTable.EventObjects` — where gathering nodes appear in the live
  object table, if you later want proximity detection.
- ImGui is `Dalamud.Bindings.ImGui`, not `ImGuiNET`.
- `TerritoryType.WeatherRate` is a `RowRef<WeatherRate>`, so it navigates
  directly; `WeatherRate` holds parallel `Weather` / `Rate` collections forming a
  cumulative distribution.
