# Standalone status

Personal Linux alpha 0.1.0-alpha.1 supports Build, Presets, Assets and Settings. Rooms is accessible but experimental and excluded from personal readiness. Polygon areas, physical wall geometry, full standalone-spec acceptance and Windows/macOS beta remain open.

The release is a non-development Linux x86_64 Player built with Unity 6000.2.6f2. It has no development watermark or UI test probes. The authenticated local agent bridge remains available. Run `python3 bin/cafe-agent.py --help` for the client; never commit its token-bearing `agent.json` discovery file.

The release build completed with zero errors and one optional Unity Pipeline configuration warning. Extracted-package checks passed catalog search, placement/rotation/half-cell move/undo, exact save/reopen, release probe exclusion and normal shutdown. The preceding placement/grid implementation passed 154 EditMode tests. These checks were performed on the development workstation, not a clean machine. Evidence captures and raw logs are retained locally and excluded from Git.

Floor moves snap to half the room grid. Preview and selection share the same rotated catalog footprint. The grid appears automatically during placement and movement. Furniture artwork is loaded from the external pack at its original scale.

Imported portable layouts are unsaved copies and trigger Save/Discard/Cancel on close. Saved cafe identity is separate from its display name. Settings and saved cafes use the existing BlueCafe application-data identity. For launch/setup information see the repository README and prepared-pack contract.
