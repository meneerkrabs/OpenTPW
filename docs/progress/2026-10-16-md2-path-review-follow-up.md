## MD2 path review follow-up (October 16, 2026)

- The review of the MD2 path round (#208) re-read the sampler in tp.exe and found it matching; its nits are settled: three source files lose
  the byte-order marks the round added, `docs/MD2-MODELS.md` says the path sampler is the one part read from the PC executable, and
  `ModelPath.Facing` names the one known difference (before a record's first rotation key OpenTPW passes the rest rotation's up row where
  the original reads the node's current row).
