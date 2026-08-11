# Vein & Vine

A Dalamud plugin for FFXIV: a priority-sorted gathering node tracker. Read-only
by design — it never moves the player and never gathers. The one game-state
change it makes is placing a map flag.

This file is rules and invariants only. Descriptions live elsewhere, and are
linked rather than repeated, because a fact copied into two files rots in one of
them:

| Looking for | Read |
|---|---|
| Build, run, release, commands | [`README.md`](README.md) |
| Why it is built this way | [`docs/design.md`](docs/design.md) |
| Dataset format | [`VeinAndVine/Data/nodes.schema.md`](VeinAndVine/Data/nodes.schema.md) |

Before changing theming or the picker's counts, read the matching section of
`docs/design.md` first — both encode measurements and rejected alternatives that
are not recoverable from the code.

## Hard constraints

- **x64 only.** Never add `Any CPU` or `x86` to the solution. Both projects
  build to `bin\x64\`; an AnyCPU mapping builds the plugin a second time into
  `bin\`, including a second `latest.zip`, which is an easy way to ship the
  wrong artifact.
- **`Private=false` on every Dalamud reference.** Dalamud already has those
  assemblies loaded, and shipping a second copy breaks type identity.
- **`<Version>` in `VeinAndVine.csproj` and `AssemblyVersion` in `repo.json`
  must match.** A Release build refuses to produce a zip when they disagree.
- **Never let an exception escape an ImGui draw.** Unwinding out of a table or
  a tab bar leaves ImGui's begin/end stack unbalanced, which takes the game
  client down rather than logging.
- The plugin stays **read-only**: no movement, no automation, no gathering.

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

The newest entry sits outside a `<details>` wrapper so GitHub renders it open;
everything older is collapsed. The build finds an entry by its `<strong>`
version marker, so keep that line's shape.

This does not compete with the comments rule above; the two divide the work.
The reasoning behind a change belongs in a code comment or a commit message,
where the next developer will look. The changelog gets the outcome.

Accuracy still binds. Plain language is not licence to describe something that
did not happen — if an item cannot be said simply and truthfully, it is usually
because the change needs naming from the player's point of view rather than the
code's.

## Documentation

Keep one fact in one file. This repo has repeatedly shipped documentation that
contradicted itself — a file table crediting `UiStyle` with window textures on
the same page as a section explaining why the window art was removed, and the
same false claim in a third place in the UI copy. When editing, prefer a link
over a paraphrase.
