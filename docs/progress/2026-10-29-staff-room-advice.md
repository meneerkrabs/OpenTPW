## Staff room advice (October 29, 2026)

- **The plain advisor no longer buzzes over every staff room (issue #829).** The
  advisor's "staff rooms closed" message 263 counted each standing staff room as
  closed: the closed test took any state but Idle as closed, and a staff room's
  record holds state 3 because nobody may choose it (`0x4DB4FB`). tp.exe's count
  (`0x4CA0C0` → `0x4E0440`) reads only the open word, which state 3 leaves alone.
  The message's only response (513) plays the `z_error` placeholder without a
  costume, the 0.4 s noise players heard. Now a staff room counts as closed only
  when it is closed, broken, upgrading or condemned.
