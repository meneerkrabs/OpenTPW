## Coaster upgrades (October 29, 2026)

- **Coasters can be upgraded again (issue #903).** Every shipped coaster has two
  researchable levels (Temple Of Gloom: `Upgrades[1]` and `[2]` at research 750
  and 500), but the catalogue-tier binding excluded coasters, so a coaster's
  upgrade list never listed its researched level and showed "no upgrade has been
  researched". tp.exe lists the level for every ride (`0x004AE140`) and its
  installation (`0x4df8f0`) has no track-type branch, so coasters now bind their
  tier like any ride. This also lets the coaster panel's loop button work, which
  needs level 1 (`0x4980D6`). A with-data test checks that every coaster binds its
  tier and seeds its level's capacity into its own `Coaster.RSE` variables, and a
  co-op join carries an upgraded coaster's tier.
