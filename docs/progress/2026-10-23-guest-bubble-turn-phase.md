## A guest's bubble from the turn's first tick no longer stops a save from loading (October 23, 2026)

Since the guests cut their turns where the economy does (October 17), a guest's park turn is one past the turn of the guests' own tick count in some ticks of a park whose economy started ahead of them, such as Instant Action's prebuilt park. The snapshot does not hold that phase, and a restored park takes it again only on its next running tick.

- A thought shown on such a tick stamps its bubble with that turn, and the guest snapshot's check refused it as a bubble from the future. A save, a co-op join's capture or a resync's taken then did not load: "Guest 52 bubble 17 at turn 856 is not a thought bubble".
- The smarter guests extension's ride throughputs, meal times and complaints are stamped the same way, and their restore refused such turns too.
- Both checks now allow the latest turn any phase can give: the tick count's turn, or one more unless a turn starts exactly at that tick. A later turn is still refused, and a park restored with such a bubble runs on as the original once its turns are aligned again.
