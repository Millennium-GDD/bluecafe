# BlueCafe verification

Scope: the independent Unity cafe builder, its retained Editor tool and standalone
desktop delivery.
BlueOffice's npm/runtime checks do not establish Unity acceptance.

## Required evidence

- Unity imports and compiles the combined project with no C# errors.
- Asset acquisition records source version/URL, downloaded bytes/hashes, extracted
  model/texture counts, and unavailable entries. Original game furniture is visibly
  rendered in Unity; downloader documentation alone is insufficient.
- Catalog search and category/theme filters operate over the acquired catalog.
  Report catalog completeness against its region/version and database count.
- A human can drag furniture into the room, move, rotate, duplicate, delete,
  undo/redo, and save/reload a layout. Verify a concrete interaction sequence.
- Floor, wall, wallpaper and flooring placement rules are explicit. Items may be
  duplicated freely; there are no in-game ownership or comfort constraints.
- Layout import/export round-trips stable furniture ids and transforms. Reject
  invalid ids and malformed/nonfinite transforms without corrupting the open layout.
- An agent can inspect the catalog, place items, save, and capture a deterministic
  2.5D view through the Unity CLI. Time a representative scene from an empty layout.
- Render a simple preset and a denser reference layout. Show images to the user
  during development; visual approval must be stated separately from test success.
- Experienced-human completion in minutes needs a timed human exercise. Automation
  time is not evidence of human usability. Keep that criterion unverified until run.

## Evidence records

Save dated logs, screenshots, layout JSON, source hashes and reports under
`evidence/`. Record the exact project tree fingerprint or commit tested. Rerun
affected checks after integration. Preserve failed attempts and document fixes.

Use meaningful EditMode tests for placement constraints and layout persistence;
exercise the actual UI and renderer independently. A passing compile or geometry
test is not visual acceptance. Before calling the tool complete, account for each
user criterion as observed, tested, or explicitly unverified.

## Standalone delivery gates

The complete desktop specification lives in `docs/spec/standalone-app/spec.md`.
Keep Editor baseline evidence and Player acceptance separate.

- Runtime spine: Linux Player loads a prepared pack read-only without
  AssetDatabase; original floor/wall/furniture and audited special surfaces render.
  Exercise one actual UI event placement and one bridge placement through Core,
  stale revisions/atomic failures, undo/redo, strict import and exact JSON reopen.
  Capture both UI and original-asset views from the executable itself.
- Geometry/document proof: v2 migration retains v1 IDs/transforms; L-shaped and
  diagonal areas, non-overlap, styled shared boundaries, physical walls/openings,
  protected attachments and fitted original surfaces round-trip in the Player.
- Linux alpha: all four revised workspaces, native dialogs and physical input,
  complete filters/categories, group preview/copy, safe replacement and close,
  crash/recovery/Discard failures and unavailable/relinked sources on a clean
  machine without Unity or Python. A development Player on this workstation is
  not clean-machine acceptance.
- Performance: record hardware, pack fingerprint, fixture and resolution; target
  60 fps normal/30 fps dense at 1920×1080, preview p95 <=100 ms and warm filtering
  p95 <=200 ms. Measure startup separately. Synchronous first-resource creation
  in the spine does not establish these budgets.
- Workflow: Momo/Abydos inventory and persistence plus library/ship walls;
  experienced human Momo <=15 minutes and independent standalone agent <30
  minutes including corrections/save/reopen. Synthetic UI events do not
  establish physical mouse usability or human timing.
- Beta: repeat installation/path/dialog/input/edit/save/recovery/export/bridge
  acceptance on Windows, Linux and macOS and prove upgrades preserve user data.

Show executable screenshots during development. Mockups, compile/tests and
numerical camera reproduction remain separate from human visual approval.
