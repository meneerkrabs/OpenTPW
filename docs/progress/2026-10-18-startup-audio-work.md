## Avoid repeated cold sound decoding and whole-bank advisor reads (October 18, 2026)

The pending audio path now shares one decode task per sound category, bank and sample. Before,
several placed sounds or sentences asking for the same uncached sample each read and decoded it;
the completed PCM cache only prevented later calls. Playback ownership, sample selection, random
draws, volume, pitch, delay and voice creation stay with each caller. Stopping one voice does not
cancel another's shared PCM. A failed or cancelled task is removed so a later request can retry,
and a category forgotten during a decode still cannot repopulate the cache. The cooperative browser
path yields before its bank reads and first MPEG batch as well as between subsequent batches;
already-cached PCM still returns immediately. This schedules CPU/audio work only, not game or
renderer objects on a worker.

The automatic advisor's `LoadResponse` now uses the existing file-backed `SoundBank` reader for
the selected speech entry instead of allocating and reading the whole bank. This includes both
installation and language-overlay banks, for level-local and global responses. It preserves the
entry's PCM playback length, lip marks, response selection and synchronous playback/animation
start. There is no speculative welcome prewarm or change to the controller's clock or response span.

Synthetic CPU regressions cover shared native and cooperative tasks, the initial scheduler gate,
PCM equivalence, cancellation and I/O retry, cache retirement, independently stopped owners, and
small speech entry reads from a 32 MiB bank with a sub-1-MiB allocation ceiling. These fixtures need
no original game media. The historical 25–54 ms construction steps, roughly 100 ms first park frame
and 45 ms advisor start in `BROWSER-PERFORMANCE.md` are context, not measurements of this change.
No new browser/native first-frame or original-data timing result is claimed. Park construction
steps and their ordering are unchanged; their renderer-dependent work needs a measured follow-up.
