## Doppler gate and the upgrade restore (October 30, 2026)

- **Upgrade request over a breakdown (follow-up of #913).** A save written before
  #913 can hold a ride that is both broken and upgrading. The bridge's state-word
  setter and a bridge snapshot restore copied the breakdown back under the upgrade
  request; both now keep the upgrade (`0x004E0E60`, `0x004E0086`: state 2
  overwrites a breakdown). Old saves still load; no pin moves.
- **Doppler (AUDIO-018) stays narrowed and off.** Another read of `tp.exe` and
  `QMixer.dll` found no gate that makes the original skip the Doppler factor, and
  ruled out several candidates (default channel flags, the start path's flag
  mapping, the listener velocity bookkeeping, the voice-to-channel update chain).
  The request word's `0xc0` (`0x6bc2af`) with the reverb test (`0x6b9acb`) feeds a
  second EnableChannel (`0x6c5690`), but it runs before the start's own
  (`0x6d2380`) and is replaced by it. The capture-based evidence and the
  off-by-default switch are unchanged; the register text lists what is still
  open.
