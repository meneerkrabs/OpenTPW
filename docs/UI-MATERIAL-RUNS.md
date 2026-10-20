# UI material-state runs

This is a bounded host correction for the UI-034 proposal. The public MD2 reader represents texture names and raw flags independently for each slot. Separate slots may resolve to the same image filename. UI model flattening previously combined all such triangles into one part and enabled the host black-key texture variant for every triangle when any contributing slot had flag 0x2.

Flattening now retains each face's flag in contiguous runs. The existing case-insensitive texture groups, average-depth group ordering, stable per-group triangle ordering, vertices, bounds and frame identity are preserved. Splitting occurs after sorting; it does not regroup all keyed faces together. Homogeneous groups remain one part. An absent texture still resolves to solid white, irrespective of the stored flag.

The synthetic `UiMaterialRunTests` build ordinary geometry through ModelBuilder, set a flag in generated slot bytes, and parse it with ModelFile. They cover alternating keyed/opaque runs in both directions, equal-depth stability, texture-group averages, case-insensitive filenames, homogeneous groups, actual DrawModel resource keys and vertex order, image shape picking, image preparation, missing textures and untextured material behavior. No original media is needed.

These are renderer-independent host-contract tests. C# execution and rendered validation remain pending. The change establishes neither a shipped mixed-slot example nor original black-key thresholds, alpha conversion, sampler or effective device-state equivalence. UI-034 and the related material-consumer evidence gates remain open.
