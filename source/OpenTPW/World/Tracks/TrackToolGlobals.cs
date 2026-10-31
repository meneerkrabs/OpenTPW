namespace OpenTPW;

/// <summary>
/// The track tool's globals of tp.exe: the exit tile <c>0x7639E0/E4</c> and the entry tile <c>0x7639E8/EC</c>. The ride select (<c>0x5276D0</c>, the park view's click on an object, the sale and the Move
/// pick-up of a ride, the placement), the track tool's start and the edit tool's hover write them and nothing resets them, so the tiles the clear op spares at a demolition are the last
/// writer's, whichever ride that was (ORACLE.md, X37).
/// </summary>
// [BIN:TPW-EXE:0x007639E0 exit and entry globals] writers 0x52773B..0x527776 (the fence tool's post A, modes 0x19 and 0x1A), 0x52788A..0x5278B4 (the select of a ride: 0x52A3A0 and 0x52A420 map its anchor), 0x527A17..0x527A2A (the edit tool's hover), the click handler's station blocks (0x524CB4, 0x524FC9, 0x5251F5, 0x5255F0, 0x525838); readers 0x53B2F0..0x53B340 (the clear op), 0x53BC60 and 0x53BD8E (the end re-link), 0x52FC80 (the repair), 0x530B70 (the backward collector); the bulldozer's click (0x525E7F, 0x52C0AF) selects nothing first
public sealed class TrackToolGlobals
{
	/// <summary>The exit tile (<c>0x7639E0/E4</c>).</summary>
	public (int X, int Y) Exit { get; set; }

	/// <summary>The entry tile (<c>0x7639E8/EC</c>).</summary>
	public (int X, int Y) Entry { get; set; }

	/// <summary>The globals a game starts with: cell (0, 0) for both (the executable's zeroed data).</summary>
	public TrackToolGlobals() : this( (0, 0), (0, 0) )
	{
	}

	/// <summary>Globals that start at <paramref name="exit"/> and <paramref name="entry"/>.</summary>
	public TrackToolGlobals( (int X, int Y) exit, (int X, int Y) entry )
	{
		Exit = exit;
		Entry = entry;
	}
}
