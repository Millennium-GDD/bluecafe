# Local pack validation and relocation

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: 01

## Question

Given prepared BlueCafe packs as the only supported input, define the precise
folder-root/version/provenance contract and compatibility with the existing
project-relative catalog paths. Decide identity, contained path resolution,
verification, partial packs/missing files, derived cache policy, changing/moving
the configured folder, and which source paths or identifiers enter portable
layouts. Source pack bytes must remain intact. No raw-game-file conversion.

## Comments

Graduated from [Local asset input contract](01-local-asset-contract.md) on
2026-10-10. Input support is settled; these remaining packaging and lifecycle
questions are not inferred from the user's format-description request.

## Answer

Accepted by the user on 2026-10-10 after review of the proposed pack rules.

Choose the folder containing catalog.json. Load the prepared mesh JSON and PNG
files referenced by that catalog; never use Editor-only prefab import or raw-game
conversion as a fallback. Support current catalog version 1 explicitly; reject
unknown versions with an actionable error rather than guess their schema.

Canonical new pack paths are relative to the selected root. For existing v1 packs,
normalize only the exact Assets/BlueCafe/Content/ prefix in memory, preserving
source catalog bytes. Resolve both forms within the selected root; reject absolute
paths and paths/symlinks escaping it. Folder names are not an identity mechanism.
Existing catalog region/gameVersion/patchVersion/sourceCatalog/metadataSource and
supplements provide declared source metadata; content fingerprints distinguish
revisions. Report metadata absent/unverified rather than invent provenance.

Read source packs without rewriting them. Derived caches live in app data and
are keyed by content/format fingerprints; portable cafe layouts contain stable
furniture IDs and source identity hints, never machine-local asset paths.

Validate a candidate path before adopting it. Invalid catalog structure/version,
duplicate IDs or unsafe paths reject adoption and preserve the prior source and
open cafe. Missing/corrupt per-item geometry or required textures makes that item
unavailable, visible and unplaceable, with a specific reason. A missing thumbnail
shows an image-unavailable state without falsely marking otherwise valid geometry
missing. Partial packs can load known unavailable records; do not silently omit
broken parts or substitute furniture.

Change/rescan/relink refreshes resources without editing cafe instance transforms,
IDs, room geometry or undo history. If the path disappears, retain the open cafe
and expose unavailable assets with a relink action. Cache invalidation must not
conceal missing source files. Verify files reports declared counts, unavailable
items and structural/hash checks separately from rendered fidelity.

Assets uses the agreed brief format description and expandable requirements;
Settings selects the folder. This is an accepted specification, not implemented or verified standalone support.
