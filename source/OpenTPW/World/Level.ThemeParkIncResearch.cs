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
		if ( world.ResearchLab is { } lab )
			economy.SeedThemeParkIncResearch( lab, world.ObjectControls );
	}

	/// <summary>
	/// Gives the park's research lab (also one a load put in place) this level's object types' research words and the obstacle's research group; called every tick, as the economy can be replaced.
	/// </summary>
	private void AttachThemeParkIncResearchInfos()
	{
		if ( !IsThemeParkIncSlice || Park?.Economy is not { TpiResearch: { } research } economy || research.Infos.Count > 0 )
			return;
		research.Infos = ThemeParkIncResearchInfos();
		economy.TpiResearchGroupOf = GroupOf;
	}

	private Dictionary<int, byte>? researchGroups;

	private byte GroupOf( int type ) => researchGroups != null && researchGroups.TryGetValue( type, out var group ) ? group : (byte)0;

	/// <summary>
	/// The research words of every object type of the zone (the type list <c>0xA09AC8</c>): the catalogue's objects and the obstacles (<c>levels/&lt;zone&gt;/obstacles</c>), a missing key read as 0 (the SAM
	/// reset's value).
	/// </summary>
	// [DATA:<object>.sam:Research.Category, Research.LinkedWith, Research.Group, Upgrades[n].CostOfResearch, Upgrades[n].CashPerBrainPoint] info +0x254, +0x24C, +0x250, +0x270 + 0x48 n, +0x274 + 0x48 n
	private IReadOnlyList<ThemeParkIncResearchInfo> ThemeParkIncResearchInfos()
	{
		var infos = new List<ThemeParkIncResearchInfo>();
		researchGroups = new Dictionary<int, byte>();
		ThemeParkIncResearchInfo Info( int id, Func<string, int> value )
		{
			researchGroups[id] = (byte)value( "Research.Group" );
			return new ThemeParkIncResearchInfo( id, value( "Research.Category" ), value( "Research.LinkedWith" ),
				Enumerable.Range( 0, 3 ).Select( level => value( $"Upgrades[{level}].CostOfResearch" ) ).ToArray(),
				Enumerable.Range( 0, 3 ).Select( level => value( $"Upgrades[{level}].CashPerBrainPoint" ) ).ToArray() );
		}
		var seen = new HashSet<int>();
		foreach ( var entry in Objects.Catalog.Entries )
			if ( seen.Add( entry.Settings.GetInt( "Info.Id" ) ) )
				infos.Add( Info( entry.Settings.GetInt( "Info.Id" ), key => entry.Settings.GetInt( key ) ) );
		var folder = $"/levels/{LevelName}/obstacles";
		if ( FileSystem.DirectoryExists( folder ) )
		{
			foreach ( var archive in FileSystem.GetDirectories( folder ).Select( path => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( path ) ) ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				foreach ( var file in FileSystem.GetFiles( archive ).Where( path => path.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
				{
					var settings = ObjectSettingsFile.Load( FileSystem, FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( file ) ) );
					int Value( string key ) => settings.Values.TryGetValue( key, out var text ) && int.TryParse( text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number ) ? number : 0;
					if ( settings.Values.ContainsKey( "Info.Id" ) && seen.Add( Value( "Info.Id" ) ) )
						infos.Add( Info( Value( "Info.Id" ), Value ) );
				}
			}
		}
		return infos;
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
			if ( TpiStaffWorld is not { } staffWorld || Park?.Economy is not { } economy )
				return;
			if ( item.Entry.Settings.GetInt( "UsageInfo.TrainingRoom" ) != 0 )
				staffWorld.TrainingRoomRemoved( economy, economy.Turn );
			// [BIN:TPI-EXE:0x61DC20 lab message 0x21] a destroyed kind-3 thing with +0x43 & 2 (a research room) clears both teams (0x61DB90); the researchers whose room it is hear message 10
			if ( item.Entry.Settings.GetInt( "UsageInfo.ThinkingSpace" ) != 0 && item.ThingId is > 0 and <= ushort.MaxValue )
			{
				staffWorld.ResearchRoomRemoved( economy, (ushort)item.ThingId, economy.Turn );
				economy.TpiResearch?.ResearchRoomRemoved();
			}
		};
	}

	/// <summary>Whether the object type is researched (the object control's +0x10); every type in a park without a seeded lab.</summary>
	internal bool ThemeParkIncResearched( int type ) => Park?.Economy.TpiResearch?.IsResearched( type ) ?? true;

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
