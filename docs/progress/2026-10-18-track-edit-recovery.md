## Existing-track Backspace and valid-circuit reopening (October 18, 2026)

- The live kart/water line tool now seeds its undo stack from the open track's existing corners. Old and newly drawn segments share directional clearing, per-tile scrap refunds and add-on removal hooks; re-entry and saved-cell reconstruction rebuild the remaining history.
- Explicit valid line/edit walks use the attraction opening guard and synchronize the model and economy door. Invalid edited circuits close; restore, placement and add-on walks do not reopen deliberately closed rides.
- Added asset-free replay and real-level cut/mend/door tests, plus optional local-data family-cost and scrap-row coverage. C# execution remains unverified locally because `dotnet` is unavailable; no test pass or full-original-parity claim is made.
- [Evidence and boundaries](../reverse/TRACK-EDIT-RECOVERY.md): only complete mapped instruction spans from a partial local reference image were statically inspected. TRACK-018 retains stack-capacity and full replay/restore qualifications; TRACK-004/007 remain open and the BLUEPRINT-003 marker work is unchanged.
