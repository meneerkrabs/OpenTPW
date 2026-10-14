# MPEG-1 nonjoint Layer II independent review

Reviewed `870f93f6a81810cb2a7bf0370d2a87a7523bac57` against its accepted CRC
setup parent `7729cf30af3e9f1465dfb4ef2aeb1ca4b87a88e9`. The prerequisite review
copy represents original `650c1a6d`; it is not another MPEG-1 feature commit.
No production correction was required.

The private bitrate/mode validation, per-channel A/B/C/D selection and numeric
maps agree with the approved [ISO/IEC 11172-3](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf)
contract and read-only [official FFmpeg numeric tables](https://www.ffmpeg.org/doxygen/9.0/mpegaudiodata_8c_source.html).
Limits are 27/30/8/12; low-table code 15 resolves to 32,767 levels and 15 bits,
not the high-table 65,535-level class. No vendor expressive code was reused.
Structured primary text and official tables were available; a PDF table screenshot
was not available through the retrieval tool, so no new visual-table claim is made.

Fresh expanded checks passed 287 tests with ten original-data skips and no
failures. Eight independent cases distinguish interior high-first versus
high-middle code 2 mappings and low code 3 through explicitly encoded LSF
companions at the same subband. Whole waveforms match the unchanged quantizer
and synthesis path despite different allocation codes. This is a reuse
regression, not an external PCM golden. A reverse B/C/A/B stream validates
per-frame profile and CRC selection with cooperative equality. All 15 permanent
reference packets produce identical PCM under all four nonjoint mode-extension
values, not only under a silent or single DC packet.

The reviewer also independently decoded all 231 complete generated vectors from
the reference actor's existing manifest archive, verified every source/PCM hash,
rate, channel count and four-frame length, and compared all 1,774,080 samples.
**Measured maximum error: 1 LSB.** The scratch checker only loaded the current
managed decoder and pre-existing reference PCM; it did not execute FFmpeg or
another external decoder. The permanent 15 pinned cases also passed normally.
These are generic synthetic references on this platform, not original game
assets or complete ISO compliance. Generator/tool/hash provenance remains in
[MPEG1-LAYER2-VERIFICATION.md](MPEG1-LAYER2-VERIFICATION.md).

Byte comparisons against the accepted CRC parent confirm unchanged Layer I,
LSF table literals, synthesis, scale-factor/PCM append, StepCrc and the complete
DecodeState/cooperative/mismatch policy. Header changes remain limited to private
version/bitrate retention and the legal MPEG-1 Layer II matrix; allocation limits
parameterize the existing Layer II loops and CRC prepass. Existing LSF and Layer I
controls, 149 author MPEG-1 controls and valid/bad-CRC WAV controls remain green.
Joint Layer II, free format, MPEG-2.5 and Layer III stay unsupported. Layer I
joint/native synthesis/de-emphasis/mismatch behavior retains prior qualifications.
No original assets, original executable oracle, GPU/device/browser runtime or
full CPU suite was used for this bounded review.
