# AUDIO-039: Bound device reverb fidelity

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
SETREVERB's preset is selected as the original selects it (the ridden ride's root script level, else 0; tp.exe 0x55A470 -> 0x51C8D0, sent only on a change) and its SndReverb.map record (0x6C5D20, 0x6C6000) is mixed by ReverbUnit, an OpenTPW Freeverb-style network on the positioned effects with the record's decay time, high-frequency ratio, diffusion, size, levels and delays; the original has no software reverb to port: it hands the record (13 dwords, EAX 2.0 listener order, 0x6C46B0) to the audio device or to the backend's vtable slot 0x50, and QMixer.dll's QSWaveMixSetReverb* forward to a COM object and hold no DSP, so the network, its constants, the size mapping and which voices it covers are OpenTPW's

## Source inspection
- `source/OpenTPW/Audio/AudioMixer.cs:215`: 217: public ReverbUnit Reverb { get; } = new( OutputRate );; 220: public long PlayedFrames => output?.PlayedFrames ?? (long)(wallClock.Elapsed.TotalSeconds * OutputRate);; 222: public void SetChannelGain( AudioChannel channel, float gain ) => channelGains[(int)channel] = Math.Clamp( gain, 0, 1 );; 224: public float GetChannelGain( AudioChannel channel ) => channelGains[(int)channel];
- `source/OpenTPW/Audio/GameAudio.cs:327`: 328: public static void SelectReverb( int preset ); 329: {; 330: if ( preset == ReverbPreset ); 331: return;
- `source/OpenTPW/Audio/GameAudio.cs:340`: 341: private static void ApplyReverb() => AudioMixer.Current?.Reverb.Set( ReverbPreset > 0 ? ReverbSettings.FromRecord( ReverbRecord ) : null );; 347: public static void LoadReverbMap( string gameRoot ); 348: {; 349: try
- `source/OpenTPW/Audio/ReverbUnit.cs:24`: 27: public sealed class ReverbUnit; 28: {; 30: private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };; 31: private static readonly int[] AllpassLengths = { 556, 441, 341, 225 };
- `source/OpenTPW/Audio/ReverbUnit.cs:199`: 200: var scale = Math.Clamp( MathF.Sqrt( Math.Clamp( value.Size, 1.5f, 100f ) / 7.5f ), 0.5f, 1.5f );; 201: if ( left.Combs.Length == 0 || scale != sizeScale ); 202: {; 203: sizeScale = scale;
- `source/OpenTPW.Tests/ReverbUnitTests.cs:8`: 11: [TestClass]; 12: public class ReverbUnitTests; 13: {; 14: private static uint Bits( float value ) => BitConverter.SingleToUInt32Bits( value );

## Bounded work
Document and evaluate substitute DSP against device/backend behavior; do not label Freeverb constants original.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Obtain EAX 2.0 or COM-backend response evidence, preset-zero behavior and voice coverage.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Record impulse responses for presets 0, 1 and 14; compare decay, delays and high-frequency damping.
2. Switch ridden ride presets and exit ride; verify only real changes apply and dry speech/music policy is identified as substitute if unproven.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
