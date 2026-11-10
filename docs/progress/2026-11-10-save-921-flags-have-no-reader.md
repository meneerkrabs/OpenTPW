## SAVE-921: the track controller flags have no reader (November 10, 2026)

The entry said the track flags (wire `0x4..0x100`, controller `+0` bits `0x20..0x800`) are not modelled by the coaster runtime beyond the trains-set-up bit `0x800`. Re-running the repository's own scan — `tools/ppc-analysis/controller_flag_readers.py` against the identified `tp.exe` — reports `PROBLEMS: none`, and every candidate it lists is dismissed with the reason it gives, that the base is not a controller pointer in the flow.

So nothing outside the saver and the loader reads them, and modelling them would add state no one consumes. What the entry actually costs is a round trip: the original's loader restores the bits and its saver writes them again, while OpenTPW drops them on import and would not write them back. The import already reports them, so the loss is visible rather than silent.

That reframes the residual for whoever picks it up: the work is to carry the raw word through import and re-save, not to give the bits behaviour. The declaration now says so.
