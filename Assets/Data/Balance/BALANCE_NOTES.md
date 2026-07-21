# Balance CSV Notes

- **Source of truth:** `balance_material_usage.csv` and `balance_weapon_stats.csv` override design markdown when they disagree.
- **Base = level 1** in both files.
- **Variant spawn (resolved in code):** orbital roulette adds `floor(runTime / 120s) * 3` weight to variant entries.
- **Rare drops on base enemies:** 5% rare / 95% common per enemy family.
- **`XX` material role:** second principal (`PrincipalExtra`), used by Blades A for Sheet metal.
- **Import:** Unity menu `ScrapWaves/Balance/Import All CSV` or auto-import on editor load when `MaterialUsageBalance.asset` is missing.
