# MPEG-1 Layer II joint review

October 14, 2026. Review of the bounded MPEG-1 joint-stereo decoder change.
This is generic format support, not original game decoder qualification.

`Mpeg1JointIndependentReviewTests` adds literal CRC words and allocation spans
for all four mode extensions in profiles A–D. The values were calculated from
sixteen protected header bits and the specified zero allocation fields using
seed FFFF and polynomial 8005, independently of the fixture's CRC calculation.
Nonzero C/D controls compare extensions whose boundary reaches the profile
limit: their distinct channel allocations and factors must produce identical
PCM. A shared-band factor62 control compares against independently encoded
nonjoint channels and proves that the small right signal cannot replace the
left channel's factor. Replacing the shared right factor with the left factor
made that control fail; the exact production source was restored afterward.

The decoder review found no production correction needed. Boundary selection,
allocation traversal, both channels' SCFSI/factors and shared triplet ordering
retain the selected frame profile. CRC protects transmitted allocation once in
shared bands and SCFSI for both allocated channels. Layer I, MPEG-2 LSF joint
refusal, allocation tables, synthesis, rounding and complete-frame policies
remain unchanged. This review does not include the separate runtime sound-bank
size-preflight proposal or change source-error propagation.

A separate reference lane measured 172 generated joint/duplicated-nonjoint
pairs (1,585,152 complete samples): maximum managed-versus-FFmpeg difference
1 LSB, and exact joint/nonjoint companion equality in each decoder. Its archived
source/input/PCM/tool hashes and permanent pins are separate evidence; this
review did not rerun that external tool. These measurements qualify generated
signals on the measured platform only, not every bitstream or platform.

No original assets, native game executable, audio device, GPU or full MPEG
conformance suite was used. MPEG-2 Layer II joint remains unsupported.
