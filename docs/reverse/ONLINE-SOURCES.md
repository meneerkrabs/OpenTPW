# The original online service: outside sources

October 11, 2026. Facts about the original Theme Park World / Sim Theme Park online service that
come from outside the game files: an archived copy of ThemeParkWorld.com, the FreeSO forum threads
on the service, and the roadmap of TPW-SE, an independent server restoration. None of their code
or data is used; they are evidence for what the original did. Where a fact can also be traced in
the binaries, the trace wins and should be cited next to it ([ONLINE-FEATURES.md](ONLINE-FEATURES.md)).

## Sources

| Id | Source | What it is |
| --- | --- | --- |
| S1 | Wayback Machine, `themeparkworld.com/gameregister/...` (captures 2000–2003) | The official site's registration, "My Theme Park", top-ten and player-search pages |
| S2 | Wayback Machine, `themeparkworld.com/tpw/eu/germany/generic/online_faqs/faqs.htm` (2001-05-09) | The official online FAQ (German edition) |
| S3 | forum.freeso.org thread 597, "Sim Theme Park / Theme Park World online functions" (2015) | First community notes on the service |
| S4 | forum.freeso.org thread 6610, "Sim Theme Park Online 1.0 restoration" (2021–2022) | Progress posts of the TPW-SE restoration (xXJDrocks450Xx, Blayer98) |
| S5 | "Theme Park World Online Roadmap", Jeremy Glazebrook, TPW-SE (2022, PDF supplied by the maintainer) | Feature list of the restoration, from reverse engineering the client and its packets |

## Service structure

- The client connects to `daphne.eagames.co.uk`, named in `online.sam`; an `onlineoverride.sam`
  also exists (S3). Online files are kept in `save\online` of the installation (S3).
- TPW-SE splits the service into a login server (password check, error codes), a news server
  (Game News and System News), a city server and a chat server (S5).
- City-server packets named by TPW-SE: ThemeInfo, LogicalServerInfo, RideInfo, ParkInfo, CityInfo,
  SearchResult and Top10 (S5).
- The "Bullfrog" world/pin holds the developers' parks (S4).

## Parks, votes and Platinum Tickets

- A player may have at most **ten** parks published at once; publishing another reports that the
  park limit is reached and the player must delete an old park first (S2).
- Published parks can be viewed in the game with Find Parks or on the website's "My Theme Park"
  page (S2). Parks can be searched by user name (S5).
- The top-parks list is ordered by **votes**. Every **visit** earns the park's owner **Platinum
  Tickets**, whether or not the visitor votes (S2).
- Visitors could walk round other players' parks, ride their rides and vote (S3).
- Swear words in park names were shown as asterisks (`****`) (S2).
- A publish removes nothing; removing a park from the online world is a separate action (S5).

## Website (ThemeParkWorld.com, S1)

- **My Theme Park** (per player, shareable URL `MyThemePark.asp?PlayerID=n`): every published park
  of the player with park name, number of visitors, number of votes, publish date and time, current
  rank and highest rank. Example: one player had twelve parks listed (2000), so the ten-park limit
  of S2 applied to parks on show at one time.
- **Top 10 parks**: rank, park name, player.
- **Top 10 players**: rank, player name.
- **Richest players**: rank, player, Platinum Tickets (for example 3,798).
- **Player search**: by user name, first name or last name, or any combination; more than 50 results
  asks for more detail.
- Registration asked for a security question and its answer, used to verify the player's identity
  with the privacy coordinator (S2). The e-mail address shows with a player's published parks only
  if the player allowed it when creating the account, changeable under "change my details" (S2).

## Chat parks (S5)

The globe's satellites are chat parks that can be joined from any menu. In a chat park, players are
present together and can:

- join a room, ask for a room's information, create a room, and be added to an existing room;
- see players join and the list of players in the session;
- **move around the park** (the server relays player movement; TPW-SE also lists teleport);
- **Say** (to the park), **Tell** (private message), **Shout**, and **Emote** (shown as
  "Dan &lt;emote text&gt;", per the manual);
- put on **Earmuffs** to stop hearing shouts;
- receive messages and private messages from "Server";
- add other players as **buddies** and **ignore** players;
- be told when a buddy enters another park or the park you are in;
- **Locate** a buddy ("Dan is in Halloween World in San Francisco") and **Go to** that park;
- **Page** a buddy who is in another park ("Dan pages you: Hello").

The command words are the game's `CHAT_COMMANDS.str`; TPW-SE keeps its list in `TPWConstants.cs`
(not seen).
