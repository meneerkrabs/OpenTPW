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
		("QUEUE-003", "guests join the back of the queue from an adjacent walkable cell; the original takes the first connected path neighbour (1, 4, 16, 64), and OpenTPW keeps no connection bits for queue cells"),
		("QUEUE-004", "the join excitement of a ride whose thing behind ride +36 is none of the water and kart controllers (OpenTPW holds no other thing there) is not modelled; water and kart rides take the controller's base (60 % of the level plus up to 40 from the circuit's corners, longest straight run and crossings, docs/TRACKS.md); the gate, the clamped difference and the live excitement (sideshow, speed and duration ratios, coaster) follow the original (ORACLE.md F16)"),
		("QUEUE-005", "after a queue edit, a guest whose cell left the queue leaves (Confused) and the others re-walk, where the original only sends away guests beyond the room; a guest sent away thinks Angry by OpenTPW's guest id modulo 3 rather than its thing id, and its dismissal records no ride event and plays no sound"),
		("QUEUE-006", "queue places follow the original's axis per link; the back cell's fourth place takes its axis from OpenTPW's join cell"),
		("QUEUE-008", "toilets wear by use and handymen clean them, but the online branch of the wear step and the dirty-toilet message handlers are not modelled"),
		("QUEUE-009", "the queue facing change's sign comes from the 1-in-10 draw"),
		("QUEUE-010", "guests leaving a queue reappear on its join cell at once"),
		("QUEUE-011", "exact completed economy tiers drive ordinary TPW factory-owned catalogue defaults and fallback capacity; live VM CAP/DUR remain authored, and native reset/performance behavior is unverified"),
		("QUEUE-012", "queue cells need allowed terrain and no object footprint"),
		("QUEUE-013", "a queue has at most 25 cells"),
		("QUEUE-014", "queues are laid cell by cell from the entrance's outside cell, each touching the back cell"),
		("QUEUE-015", "removing a queue cell removes every cell behind it, where the original's cell clear removes only that cell"),
		("QUEUE-016", "rides evaluate admission and queued guests run state 11 once per park turn"),
		("QUEUE-017", "guests do not choose a broken attraction (VAR_BROKEN ≠ 0)"),
		("QUEUE-018", "a broken attraction refuses joins as if closed"),
		("QUEUE-019", "OpenTPW keeps no ride state word: a ride is in state 0 while it is open and in state 1 while it is closed and VAR_BROKEN ≠ 0; a queue that is still there when the script clears VAR_BROKEN is turned away at once, the original's ride stays in state 1 and keeps dismissing its head until a SetState(0) caller reopens it, and state 4 (life gauge 0) is not told apart"),
		("QUEUE-020", "BOUNCE stores one script-clock reading as both the deadline's base and the slot's start; the original reads its millisecond clock 0x10e844 twice, so its start can lie 1 ms after the deadline's base"),
		("QUEUE-021", "a left click on a queue cell starts the queue tool at that ride's back cell (help 444 says click to edit); the click handler is not traced"),
		("QUEUE-022", "a bridge without any operating values (speed, design speed, duration and design duration all 0: a synthetic object; every original object has InitSpeed and InitDuration) is read at its design speed and duration, where the original would divide 0 by 0 and score 0"),
	};
}
