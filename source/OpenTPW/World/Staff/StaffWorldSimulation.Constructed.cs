namespace OpenTPW;

public sealed partial class StaffWorldSimulation
{
	/// <summary>
	/// A new employee's role constructor has run (the placing click, <see cref="PlaceStaffCommand"/>): the member stands on its cell and takes the role's first search at once, in the
	/// constructor's own order, instead of on its first update of the thing sweep. False for a member this world does not drive or cannot place.
	/// </summary>
	// [BIN:TPW-EXE:0x004D4340 entertainer ctor] the base constructor (0x504B10: the base-speed draw), the row 0 on the first cell (0x4D8440), then the search 0x4D46D0 with no draw between them
	// [BIN:TPW-EXE:0x004D5E68 guard ctor] the base constructor, the guard's own variant draw (0x4D5E02), the gate 0x506A40, then the wander 0x4F9490 on a turn whose low two bits are not both 0 (state 1) else state 0: the search 0x4D63D0
	// [BIN:TPW-EXE:0x004D6C73 handyman ctor] the base constructor, then the job search 0x4D7100
	// [BIN:TPW-EXE:0x004D9FC4 mechanic ctor] the base constructor, the state 0 setter 0x4DA370 (0x5054D0 takes no draw), then the search 0x4DA5B0
	// [BIN:TPW-EXE:0x005026BA researcher ctor] the base constructor, the gate 0x506A40, a draw 0x5026CB whose low two bits decide the wander 0x4F9490 (state 1, 0x5054D0), else the tiredness test 0x506680 or the research (state 15), and last the research flag 0x5041F0 on the staff object (a flag byte and the message 5 or 6 to the advisor's subscription 0x599EDB; no draw)
	public bool Constructed( ParkEconomy economy, StaffMember member )
	{
		if ( !economy.DrivenByWorld( member ) || !members().Any( current => ReferenceEquals( current, member ) ) || !Place( member ) )
			return false;
		var turn = economy.Turn;
		// the entertainer's and the guard's row goes on the cell the thing stands on (0x4D8440) before the search
		economy.MoveStaffEffect( member, Cell( member ) );
		SearchByType( economy, member, turn );
		member.CellX = member.Navigation!.X / (float)Fixed16.One;
		member.CellY = member.Navigation.Y / (float)Fixed16.One;
		return true;
	}
}
