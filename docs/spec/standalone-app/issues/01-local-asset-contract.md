# Local asset input contract

Parent: [Standalone app specification](../map.md)
Label: wayfinder:grilling
Type: grilling
Status: resolved
Assignee: krisspy (Codex session)
Blocked by: none

## Question

What asset input does the standalone app support and advertise, and how does the
Assets page explain it? Root/version validation and pack lifecycle details move
to [Local pack validation and relocation](07-pack-validation.md).

## Comments

Charted 2026-10-10. Resolved after the user rejected raw-file support and requested an expected-format description.

## Answer

Resolved 2026-10-10 with the user. Support user-provided prepared BlueCafe asset
packs only. The app neither imports/converts raw game files nor advertises such
support, and has no downloader or acquisition helper.

The Assets page describes expected contents: catalog.json and the referenced
mesh data, textures, thumbnail images and source metadata. Use brief copy and
expandable Folder requirements. Show the configured path and specific validation
results. Preserve unavailable entries explicitly rather than substitute assets.

Detailed input-format rules are resolved in [Local pack validation and relocation](07-pack-validation.md).
The current Editor pipeline is not evidence of standalone loading support.
