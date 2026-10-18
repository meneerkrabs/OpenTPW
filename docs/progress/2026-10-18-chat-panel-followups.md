## Chat overlay follow-ups from the review of #607 (October 18, 2026)

Four small fixes to the in-park chat overlay from the review of PR #607:

- The entry takes at most 255 characters, as the original's (tp.exe `0x4BF460` builds it with 255); a chat line on the protocol still carries up to 256
  (`[EXT:online-chat-line-limit]`, formerly the stray label `EXT:ONLINE-030`), so older clients' lines still pass.
- The message list no longer cuts a row between the two halves of a surrogate pair, so an emoji at a row's end moves to the next row whole.
- The overlay asks for the buddy list at most once in five seconds, counting a typed `/buddy` (`[EXT:online-chat-person-flags]`). Switching to the buddy
  tab again and again no longer spends the connection's request allowance and fills the chat with "rate-limited" lines, and a typed `/buddy` followed by
  the buddy tab keeps its answer in the chat lines. An ask without an answer in that time counts as dropped, and a new connection asks at once.
- The server's room change lost a condition that always held.

Tests: `ChatPanelTests` (the entry's limit, rows with surrogate pairs, `TheOverlayAsksForTheBuddyListAtMostOnceInItsInterval`) and
`OnlineIntegrationTests` (a typed `/buddy` right before the buddy tab keeps its lines; the dropped-ask test lets its ask out at once).
