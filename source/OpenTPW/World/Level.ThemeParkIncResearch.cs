namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// A park built from a Theme Park Inc save (the prebuilt Science park) takes its staff HQ's obstacle slots and trained-staff counts; a continuation restores its own later, and a new park's HQ built
	/// its table from the mission (ParkEconomy).
	/// </summary>
	// [BIN:TPI-EXE:0x64CD3E staff HQ load] a park file's HQ thing is loaded, not constructed: its slots are the saved ones (the prebuilt's twenty {7, 0, 0}), not the current mission's table
	private void SeedThemeParkIncHqAndLab()
	{
		if ( !IsThemeParkIncSlice || resumeState != null || OriginalPark?.Save?.ThemeParkIncWorld is not { } world || Park?.Economy is not { } economy )
			return;
		if ( world.StaffHq is { } hq && economy.TpiStaff is { } staff )
			staff.Hq.Import( hq.StaffSlots, hq.TrainedStaff );
	}

	/// <summary>The Theme Park Inc staff rooms of this level: the newest built object of a kind, its entrance cell, and its script's variable 0.</summary>
	private sealed class ThemeParkIncRooms( Level level ) : IThemeParkIncRooms
	{
		// [BIN:TPI-EXE:0x5DB786 room flags] the attraction builder sets the thing's +0x42 |= 0x200 when the type record's +0x1B4 (UsageInfo.ThinkingSpace) is nonzero and |= 0x400 when +0x1B8
		// (UsageInfo.TrainingRoom) is; [BIN:TPI-EXE:0x64F3E0 object list] the builder pushes the thing onto the front of the object list park+0x1EAB88 (the World variable FirstObject), as 0x637100 pushes
		// it onto the thing list: an object built in the park is first on both, so the thing list's order (Level.ThemeParkIncAttractionsOf) is the object list's for every room a player builds; no
		// prebuilt Theme Park Inc park holds a research or training room (ThemeParkIncTrainingTests)
		public ThemeParkIncRoom? First( ThemeParkIncRoomKind kind )
		{
			var key = kind == ThemeParkIncRoomKind.Research ? "UsageInfo.ThinkingSpace" : "UsageInfo.TrainingRoom";
			var order = level.Things.Capture().Select( ( slot, index ) => (slot.Id, index) ).ToDictionary( pair => pair.Id, pair => pair.index );
			var room = level.Objects.Objects.Where( item => !item.IsDeleted && item.Entry.Settings.GetInt( key ) != 0 && item.ThingId is > 0 and <= ushort.MaxValue )
				.OrderBy( item => order.TryGetValue( item.ThingId, out var index ) ? index : int.MaxValue ).ThenByDescending( item => item.ThingId ).FirstOrDefault();
			if ( room == null )
				return null;
			var settings = room.Entry.Settings;
			var thing = new ThemeParkIncStaffWorld.DropObject( settings.GetInt( "Info.WhichUIType" ), room.ThingCell, room.Placement.Rotation, room.Entry.Shape,
				settings.GetInt( "Info.MapOffsetX" ), settings.GetInt( "Info.MapOffsetY" ) );
			return new ThemeParkIncRoom( (ushort)room.ThingId, ThemeParkIncStaffWorld.CellOfWord( ThemeParkIncStaffWorld.EntranceWord( thing ) ) );
		}

		// [BIN:TPI-EXE:0x6B0740 script instance] the room thing's +0x2C keys its script instance in the list 0xB8C5C8; 0x6B0C90 / 0x6B0780 read and write its int variable [i] (+0x1C, bounded by +0x8C)
		public void AddOccupancy( ushort thing, int delta )
		{
			if ( level.ThemeParkIncThing( thing )?.Script is { Variables.Length: > 0 } script )
				script.Variables[0] = unchecked( script.Variables[0] + delta );
		}
	}

	/// <summary>Gives the Theme Park Inc staff world this level's rooms and tells it when a room goes.</summary>
	private void AttachThemeParkIncRooms()
	{
		if ( TpiStaffWorld is not { } world )
			return;
		world.Rooms = new ThemeParkIncRooms( this );
		// [BIN:TPI-EXE:0x637C40 thing destroy] the room thing's destroy broadcasts the staff message 0xA while the thing still stands; OpenTPW's object removal (ParkObjects.ObjectRemoving, the room still
		// listed) is the seam, at the removal rather than at the turn's deferred destroy
		Objects.ObjectRemoving += item =>
		{
			if ( TpiStaffWorld is { } staffWorld && Park?.Economy is { } economy && item.Entry.Settings.GetInt( "UsageInfo.TrainingRoom" ) != 0 )
				staffWorld.TrainingRoomRemoved( economy, economy.Turn );
		};
	}

	/// <summary>Whether the park has a training room (the text HUD offers training only then: the original's walk to the room would read thing 0's entrance).</summary>
	internal bool ThemeParkIncHasTrainingRoom => TpiStaffWorld?.Rooms?.First( ThemeParkIncRoomKind.Training ) != null;

	/// <summary>The "Select Ability To Learn" choice for a placed member (ThemeParkIncStaffWorld.ChooseAbility).</summary>
	internal ThemeParkIncTrainingChoice? ChooseThemeParkIncAbility( ThemeParkIncStaffMember member, int ability, bool confirmed )
	{
		if ( TpiStaffWorld is not { } world || Park?.Economy is not { } economy || !member.Placed || !ThemeParkIncHasTrainingRoom )
			return null;
		return world.ChooseAbility( economy, member, ability, confirmed, economy.Turn );
	}
}
