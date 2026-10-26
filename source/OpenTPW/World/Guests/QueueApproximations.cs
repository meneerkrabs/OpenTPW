namespace OpenTPW;

/// <summary>
/// The queue slice's approximation register (docs/GUESTS.md, "Queues"; docs/reverse/QUEUE-plan.md §10). Each
/// entry is tagged <c>[APPROX:QUEUE-NNN]</c> at its code site. Everything else in the queue model is traced
/// from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class QueueApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("QUEUE-003", "back-cell admission and fourth-slot path geometry are implemented; supported live typed HasQueue entries derive their front from the actual entry link byte (+0x0C, tp.exe 0x4DE040, bits 1, 16, 64, 4), and a native zero front (no cardinal bit) leaves the ride unconnected (0x4DE4A0) and its demolition without a count to undo (0x527FB4); the guests' joining and scoring of a zero-front ride, linkless or unsupported entries and untyped worlds retain the host positive one-cell fallback; divergent imported mEntryPos and the native cache invalidation timing (0x4DE130 keeps +0x3A until the next 0x4DE1F0, where OpenTPW recomputes on every grid change) remain unsupported"),
		("QUEUE-010", "a guest leaving a queue (0x5004AA, the queue edit's dismissal 0x501390, a removed ride's message 0x4FB360) stays where it stands in state 6 on every placed ride, whatever its cell is now (a path, a queue cell, the owned stand cell, or a cell the queue's undo or a demolition emptied), and decides from there by the ported wander (0x4F9490); a bridge without placed cells (no owner word or no exit cell: the gates, the test rigs, the Theme Park Inc slice) and the exits of a guest whose attraction is gone retain the front/nearest-path relocation, whose cells carry no links to walk off by"),
		("QUEUE-012", "queue cells need allowed terrain, owned land and no object of the queue tool's overwrite priority 3 or more (a lower one is demolished, as tp.exe's validator 0x535670 compares 0x818c10 = 3 with the owner's Info.OverwritePriority); the validator refuses a cell whose flag byte +0x0E has bit 0x40 (the park's land, modelled), allows empty cells and ordinary path cells, and reads no MAP terrain bit (map setup makes inside water, blocked and walkway cells type 0), where OpenTPW's terrain rule also holds the Easymode save's occupied cells"),
		("QUEUE-015", "the queue tool's undo (Back 0x52FE50) takes cells and refunds as 0x5367A0 does and writes no guest: the re-evaluation that follows (0x4DE1F0 at 0x52FFEC, 0x501390 per guest) dismisses by place alone; the corner walk's counters (+0x20: 1 on a corner, 0 elsewhere, 0x5302C2) are written by the queue edit click, whose tool takes the segments back through the native undo (the cells go from the segment's end, so a corner keeps its counter 1 - 1 and stays); a ride whose front holds no queue cell is demolished with one cell's debit (0x527FB4). The tool entries that run no walk here (a ride's placement, the ride window's button, a track's or coaster's completion) take the chain from the segment's first cell on without counters, and the removal from a clicked cell on stays only as the labelled extensions EXT:dev-one-click-remove and EXT:queue-backtrack, whose queued guests are set down on the nearest path (GuestSimulation.SetDownOffQueueCells) where the original has no such removal"),
	};
}
