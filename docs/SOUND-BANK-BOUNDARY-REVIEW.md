# Sound bank boundary review

Independent review of `27102d8f92fa88d6062ab4c37341c8a662d4b150`
against its parent `192c90ec`, using synthetic inputs and real `SoundBank`
readers. No production correction was needed.

The four regression cases failed independently with the parent reader:
an offset inside the table became an entry name, payload and name positions
above `int.MaxValue` wrapped backwards, and a reopened bank of another length
allocated 67,109,832 managed bytes before refusal. The reviewed reader rejects
the table offset, adds positions as `long`, and checks reopened length before
allocating the payload. The allocation number is a managed .NET 10 synthetic
measurement with tiered compilation disabled, not a browser measurement or a
format size limit.

The guards retain the existing count clamp and stored slot numbering. Names
still read their fixed 16-byte field without interpreting payload header
validity; MPEG lookup checks the header and payload range independently.
Empty reads retain their existing behavior. Every nonempty streamed read owns
and disposes a fresh stream, including exceptions from length checks, seek,
and exact reads. No decoder or mixer behavior changes.

`SoundBankIndependentReviewTests` additionally exercises two slots that alias
one valid entry with an extended 56-byte header, independent returned buffers,
and a successful subsequent lookup after a reopened payload stream reports a
changed length or throws during seek. These controls complement the author's
fragmentation, truncation, sparse offsets, and disposal tests.

The Release sound-bank/on-demand/SDT/audio/MP2 metadata/decode-scheduling
selection passed 50 tests, with 11 original-data tests skipped. The Release
solution build, affected-source style checks, fidelity register check, and
11 fidelity checker tests passed. Production source was restored byte for byte
after the failing parent-reader run.

The review uses synthetic host evidence. Fresh original-bank qualification,
playback, and GPU checks remain deferred while the asset mount is unavailable.
The reader trusts reported stream length, cannot detect same-length replacement,
and still allocates each complete valid requested sample. The full CPU suite
is reserved for the integrated branch's verification gate.
