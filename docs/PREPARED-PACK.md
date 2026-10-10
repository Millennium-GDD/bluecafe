# Prepared pack contract

BlueCafe accepts a user-supplied prepared directory containing `catalog.json`, mesh JSON and PNG resources. It does not accept raw bundles or download/convert content. Keep the pack outside Git; `Assets/BlueCafe/Content` is ignored for local Editor use. The standalone app accepts any compatible selected folder.

The canonical catalog DTOs are in `Assets/BlueCafe/Core/Model.cs`; mesh DTOs and validation are in `Assets/BlueCafe/Infrastructure/Pack.cs`. Catalog schema version 1 includes a positive `gridUnit` and `items` with stable IDs, names, category/theme, placement kind, cell dimensions, availability, original parts and resource paths. The exact supported fields and checks are defined by these types and the Infrastructure tests.

Paths are relative to the selected pack root. The exact legacy prefix `Assets/BlueCafe/Content/` is normalized in memory for existing packs. Absolute paths, traversal and symbolic links are rejected. Source meshes, textures and catalog bytes are read-only. Missing or corrupt files leave items visibly unavailable rather than substituting furniture. Declared source fields are metadata, not proof of authenticity.

Each part retains its original position, rotation, scale and material data. Mesh JSON contains flat vertex/normal/UV arrays and submesh triangle indices. Images use PNG. Pack validation checks finite geometry/transforms, dimensions, indices and resource integrity, and records a pack fingerprint.

Use Settings to select or relink the pack. Catalog refresh validates references in the current layout and history before adoption. Known unavailable furniture may survive a saved layout; new placement requires available content. See `docs/CONTRACTS.md` for placement/document boundaries.

The repository includes no downloaded catalog, game images, mesh data or acquisition scripts. Obtain and use content only under the permissions applicable to it.
