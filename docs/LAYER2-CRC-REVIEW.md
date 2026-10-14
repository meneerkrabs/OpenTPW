# Supported Layer II CRC review

Reviewed `2ee0c9d19a98ee66e347629ce11350abb7d3746b` against
`b60abcedcbed9d941905eedc1cee7eff8c41b23b`. No production correction was needed.
This review covers existing MPEG-2 LSF non-joint Layer II only.

The [ISO/IEC 11172-3 primary text](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf),
section 2.4.1.6, transmits SCFSI only for nonzero allocations. The protected field
boundary follows allocation and those transmitted selectors, before scale
factors. [Official FFmpeg source](https://www.ffmpeg.org/doxygen/9.0/mpegaudiodec__template_8c_source.html)
independently corroborates that ordering: its Layer II CRC check occurs after
conditional scale-code reads and before scale factors. These were read-only
references; no vendor code or decoder execution was used. Structured PDF text was
available, but its table screenshot failed; no visual table verification is claimed.

The unchanged LSF descriptor supplies 75 allocation bits per channel. One
allocated mono band adds two selector bits, giving 77; zero allocations give 75,
all 30 give 135. Two channels give 150 allocation bits and add two per allocated
channel/subband; unequal one-band dual gives 152. The old 135/270 tests mirrored
maxima and were not independent evidence for sparse allocation coverage.

Fresh focused verification passed 132 tests with 10 original-data skips and no
failures. Eight independent cases use a separate packet encoder and polynomial
long division of the explicitly emitted protected message, without production
ProtectedBits/StepCrc/tables. Literal checkwords for one allocated last mono
subband and SCFSI 0..3 are 7f2f/ff2a/ff20/7f25. Equal 152-bit dual spans at left band 0
and right band 29 yield cd37 and 0aaa, with the other channel silent. The protected
last selector bit and the first excluded valid scale bit are distinguished:
changing the former reports CRC failure while still decoding; changing the
latter changes PCM without a CRC failure. A valid emphasis change also proves
header bit 31 remains covered. Protected/unprotected PCM equality is a regression
check against the existing decoder, not an external conformance golden.

The original red log was read: nine sparse/zero/excluded-field/WAV/cooperative
failures with 14 controls pass before the allocation-driven correction. Fresh
checks retain all existing valid WAV/bad-checkword refusal, zero/one/all allocation,
protected versus excluded field, truncation and cooperative controls.

Byte comparison confirms unchanged StepCrc, synchronous/cooperative decode and
mismatch continuation, the complete Header/bitreader/synthesis/PCM tail, and the
entire Layer I file. Layer I joint coverage is preserved but not adjudicated.
MPEG-1 Layer II, Layer II joint stereo, free format, MPEG-2.5 and Layer III remain
unsupported. No original assets, oracle/device/GPU/browser runtime, full CPU suite
or complete/native MPEG conformance is claimed; synthesis/de-emphasis and native
mismatch-policy qualifications remain.
