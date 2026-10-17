## Buy window: a golden-ticket row previews the Mystery ride, not the ride it hides (October 17, 2026)

A golden-ticket row the player has not bought yet already read "Mystery Ride!" (UITEXT 137) in the buy list, but selecting it turned the real ride's model in the preview frame, with its name and footprint, which gave the surprise away. tp.exe's preview maker (`0x4ab1b0`) sends such a row (type `GoldenTicketCost` above 0, not in the bought-with-tickets set `0x4d4b70`) down the branch of the negative land-row ids: the preview object is the record of type 100, the Mystery ride tool (`rides/mystery`), the footprint control `0x1eb` is only cleared and the name control `0x1ec` gets UITEXT 137.

- `IBuildCatalog.MysteryPreview` offers the type-100 tool as a preview item (`OriginalBuildCatalog.MysteryRideInfoId`); `ParkHud.BuyPreviewModelItem` turns it for a mystery row, the title uses the list name, and the footprint stays empty.
- The stats frame (`0x4abb00`) has no such test in tp.exe, so it keeps showing the ride's own values, as the original does.
- Tests: `BuyListTicketRowTests.AnUnboughtTicketRowPreviewsTheMysteryRideAndNotTheRideItHides`, and with data `OriginalUiAssetTests.EveryThemeHasTheMysteryRidePreviewForTicketRows` (all four themes have the model, every texture resolves).
