# Vein & Vine

A Dalamud plugin for FFXIV: a priority-sorted gathering node tracker. Read-only
by design — it never moves the player and never gathers. The one game-state
change it makes is placing a map flag.

## Layout

- `VeinAndVine/` — the plugin. Models → Services → Windows, one direction only.
- `tools/NodeGen/` — regenerates `VeinAndVine/Data/nodes.json` from the game's
  Excel sheets. References the plugin so the dataset cannot drift from the type
  that parses it.
- `Helpful Data/_original_scaffold/` — superseded reference only. Not in the
  solution, does not build.

x64 only. Do not add `Any CPU` or `x86` configurations to the solution: the
plugin and NodeGen both build to `bin\x64\`, and an AnyCPU mapping makes the
plugin build a second time into `bin\` — including a second `latest.zip`, which
is an easy way to ship the wrong artifact.

## Comments

Comment only where the code cannot explain itself:

- **Why** a decision was made, not **what** the code does.
- Non-obvious constraints, edge cases, and gotchas.
- Reasons for unusual or non-idiomatic code.
- Assumptions the code relies on but does not check.

Do not write a comment that restates the code in words. If a section is
self-explanatory, leave it uncommented. Prefer clear naming over commentary —
comment only what naming cannot convey.

Before finalizing any comment, apply the test: **would someone lose real
information if this were deleted?** If not, delete it.

This applies to XML doc comments too. `/// <summary>Real seconds per Eorzea
hour.</summary>` on `RealSecondsPerEorzeaHour` earns nothing. A doc comment
survives on the strength of what it adds — a unit, a constraint, a failure
mode — not because a member is public.

Much of this codebase's existing commentary records genuinely hard-won
knowledge: sheet columns that are mislabelled, formulas that break when
rounded, ImGui ordering requirements that crash the game client if violated.
That is exactly what to keep. Prune restatement, never the reasoning.

## Changelog entries

`CHANGELOG.md` is not a developer document. A release build lifts its text
verbatim into both manifests, so what is written there is what a player reads
in the in-game installer, next to the Update button.

**Write every item as plain language describing what happened to the plugin
from the outside.** "Fixed an issue where the node list showed the wrong
countdown", not "corrected the `TimeRemaining` guard in `PriorityEngine`".

Keep out of it:

- File, type, method, and field names.
- Regexes, metrics, contrast ratios, row counts, and sheet column numbers.
- Internal architecture, and the reasoning behind a fix.

If a change has no effect a player can see — build plumbing, dead code, a
refactor — say that plainly in a line or two rather than explaining the
engineering. A player who cannot see a difference needs to know only that
there isn't one.

This does not compete with the comments rule above; the two divide the work.
The reasoning behind a change belongs in a code comment or a commit message,
where the next developer will look. The changelog gets the outcome.

Accuracy still binds. Plain language is not licence to describe something that
did not happen — if an item cannot be said simply and truthfully, it is usually
because the change needs naming from the player's point of view rather than the
code's.

## Building

```
dotnet build VeinAndVine.sln -c Debug     # or Release
```

Needs Dalamud's dev assemblies, found via `$DALAMUD_HOME` or the default
XIVLauncher path. See `Directory.Build.props`.

A Release build refuses to produce a zip unless `<Version>` in
`VeinAndVine.csproj` and `AssemblyVersion` in `repo.json` agree, and warns when
`CHANGELOG.md` has no section for the version being built.
