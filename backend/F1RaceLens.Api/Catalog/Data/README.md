# Public career snapshot

`catalog.json` is a derived selection of public [F1DB v2026.17.0](https://github.com/f1db/f1db/releases/tag/v2026.17.0) data, licensed under [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/). Attribution and the release cutoff are also included in the API response and UI.

The selection contains 860 drivers with race entries and 186 constructors, career totals, participation seasons, championship years and constructor chronology. Source field names are mapped to a smaller UI schema; practice-only drivers without race entries are excluded. No private user data or ML artifacts are included. F1DB IDs must not be treated as Jolpica IDs. Constructor totals are not merged across names or successor entities.

Regenerate from the repository root with `./scripts/catalog/Update-Catalog.ps1 -Version v2026.17.0`. The archive stays in ignored `.local/catalog-import`; only this small attributed snapshot is versioned. Validate and review a new release, then restart the API; it keeps an immutable snapshot in memory. This does not refresh the model or current Jolpica standings. The source's race entries, actual starts, total points and championship points have distinct meanings; this UI exposes `totalPoints` as reported by F1DB.
