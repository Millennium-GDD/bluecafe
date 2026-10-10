# BlueCafe

Independent Unity cafe design tool. BlueOffice is context only.

Read docs/VERIFICATION.md before implementation and completion; read docs/CONTRACTS.md before changing integration boundaries.
Use Unity CLI with an explicit project path. One orchestrator owns the live Editor, package installation and integration; delegate independent code work. Workers must not launch another Editor or manually mutate scene/asset YAML.
Preserve source asset provenance and bytes. Missing assets must remain visibly unavailable. Game assets, catalogs, downloads, user data and captures stay outside Git.
