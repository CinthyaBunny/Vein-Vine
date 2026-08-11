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

## Building

```
dotnet build VeinAndVine.sln -c Debug     # or Release
```

Needs Dalamud's dev assemblies, found via `$DALAMUD_HOME` or the default
XIVLauncher path. See `Directory.Build.props`.

A Release build refuses to produce a zip unless `<Version>` in
`VeinAndVine.csproj` and `AssemblyVersion` in `repo.json` agree, and warns when
`CHANGELOG.md` has no section for the version being built.
