# BlueCafe standalone app specification

Status: runtime spine implemented; geometry proof, Linux alpha and beta remain open
Date: 2026-10-10
Canonical decision map: [Standalone app specification](map.md)

This handoff assembles the resolved route to a desktop cafe designer. Decision
detail lives in the linked tickets; implementation must satisfy the complete
standalone scope in [docs/PLAN.md](../../STANDALONE.md), not
only the first runtime slice. Read [VERIFICATION.md](../../VERIFICATION.md)
before implementation/completion and [CONTRACTS.md](../../CONTRACTS.md) before
changing an integration boundary. Those files distinguish implemented Editor/runtime contracts from the remaining
standalone scope. Do not present the complete application as implemented.

## Scope and design sources

Target a standalone Unity desktop app: Linux alpha, Windows/macOS beta. Build,
Rooms, Presets and Assets share one open cafe, with Settings for the user-provided
asset path. Preserve original assets/provenance and unavailable states. No asset
downloader, raw-game-file import/conversion, multi-document UI or required cloud
service. Drawing room areas is separate from physical walls and furniture.

Rooms includes Areas/Walls modes for the already-agreed separate geometry tools.
Preset Open and Open as new cafe have distinct saved-cafe identities, as specified
in the document lifecycle decision. These flows supplement the original mockups.

Use the four-screen design references (retained locally)
and [glossary](../../../GLOSSARY.md). Keep the original generated reference images;
the Assets Downloads tab is superseded. Real runtime thumbnails and examples
come from supplied source assets. Native-game behavior and BlueCafe polygon/group
extensions must remain distinguished.

## Behavioral and data contracts

| Area | Authoritative decision |
| --- | --- |
| Supported asset input and Assets-page explanation | [Local asset input contract](issues/01-local-asset-contract.md) |
| Pack-root paths, catalog compatibility, verification, caches and relinking | [Local pack validation and relocation](issues/07-pack-validation.md) |
| Polygon rooms, flooring/wallpaper coverage and protected attachments | [Room areas and physical wall relationships](issues/02-room-wall-model.md) |
| My cafes, portable files, replacement, Save/Discard/Cancel and recovery | [Single cafe replacement and recovery](issues/03-document-lifecycle.md) |
| Rigid group transforms/copies, atomic failures and split/merge styles | [Atomic group editing and attachment behavior](issues/04-authoring-operations.md) |
| Runtime modules, v2 model responsibilities, command revisions, agent bridge and migration | [Runtime architecture and shared agent boundary](issues/05-runtime-boundaries.md) |
| Evidence, failure exercises, performance and timed acceptance | [Standalone delivery sequence and acceptance](issues/06-delivery-gates.md) |

## Implementation route

1. **Runtime spine.** Extract Core and keep the Editor adapter usable. Create a
   Linux Player loading the actual prepared pack through runtime mesh/PNG loading,
   preserve audited material rules, and prove a shared UI/agent placement,
   undo/persistence and deterministic capture. No second worker-owned Editor.
2. **Geometry/document proof.** Add versioned rooms/walls/attachments and legacy
   migration, then prove L-shaped and diagonal polygons, shared styled boundaries,
   original surface fitting, wall decorations and exact save/reopen in the Player.
3. **Linux alpha.** Complete all workspaces/input/document/asset/recovery features
   and the revised visual design. Exercise native dialogs and actual physical
   input on a clean machine without Unity or Python.
4. **Workflow acceptance.** Verify original inventory and appearance with Momo,
   Abydos, library and ship; obtain visual approval and record human/agent timing.
5. **Cross-platform beta.** Repeat target-machine acceptance on Windows/Linux/macOS
   and verify installation/upgrade preserve cafes, recovery data and source packs.

Delegated implementation boundaries should follow the architecture modules and
explicit interfaces; the orchestrator integrates and owns the live Editor,
packages and Unity CLI targeting this project. Heavy 3D work uses gpt-6-astra/high
with bounded context as required by AGENTS.md. Workers request live commands;
they must not launch another Editor or edit scene/asset YAML.

## Starting implementation

The first deliverable is the runtime spine, not a full UI rewrite. Preserve the
current Editor baseline and user work. Update actual shared contracts when the
new boundary is integrated. Use meaningful core placement/persistence tests and
independent Player input/render evidence; record the exact tree tested.

Startup/cache budgets and calendar estimates come from the runtime/geometry
proofs. They are measured engineering outputs rather than unresolved product
scope. Acceptance targets remain unverified until exercised. All seven decision
tickets are resolved; no planning blocker remains before starting this route.


## Implementation evidence, 2026-10-10

Delivery step 1 now has a runnable Linux development Player with read-only prepared
pack loading, original rendering, shared Core UI/agent authoring, atomic group
commands, v1 persistence, My cafes, recovery and captures. See
the dated report (evidence retained locally) and
[runtime usage](../../STANDALONE.md). The first shell is not complete
four-screen design fidelity. Steps 2–5 remain open and authoritative.
