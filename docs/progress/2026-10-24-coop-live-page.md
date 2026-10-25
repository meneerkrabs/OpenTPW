## The browser shares a park live too, as host and as guest (October 24, 2026)

The shared park's live acceptance now runs the browser's page as host and as guest ([COOP.md](../COOP.md), "The live acceptance"; `[EXT:online-coop-session]`). This is C2d-5b: native/page, page/native and page/page, through the real server with the game's data.

- Each page runs in a headless, muted Chrome of its own, through a local server of its own in front of the real one. The driver checks that it is the web build under test, starts the run from the front end, and relays the page's status to the other game.
- In the page, the chat's keys, F4 and the park's clicks go through the browser's own key and mouse events on the canvas, as a player's do. The driver takes each picture as a screenshot of the canvas.
- The page runs found a bug: after a server restart, the browser's chat did not reconnect by itself, because the browser reports a dropped connection with its own close status. It now reconnects as the desktop's does, and says so in the chat.
- The endurance run now samples the park every five seconds, and the driver checks the interval on its own clock too.
