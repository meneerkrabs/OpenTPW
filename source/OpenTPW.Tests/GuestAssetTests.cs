using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Guest data from the original game (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class GuestAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original guest sprites, balance files and Jungle save." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) || !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad or levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestMethod]
	public void KidSpriteSetsDecodeWithTheirAnimationTables()
	{
		var kids = GuestSpriteAtlas.LoadKids();
		CollectionAssert.AreEqual( new[] { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" }, kids.Select( kid => kid.Name ).ToArray() );
		foreach ( var kid in kids )
		{
			Assert.AreEqual( 175, kid.Frames.Length, kid.Name );
			Assert.AreEqual( $"{kid.Name}.TPS", kid.Animations.BankName );
			var walk = kid.Animations.Animations[GuestSpriteAtlas.WalkSlot];
			Assert.AreEqual( (135, 8, 5), (walk.FirstFrame, walk.FramesPerDirection, walk.Directions), kid.Name );
			var used = kid.Animations.Animations.Where( slot => !slot.IsEmpty ).SelectMany( slot => Enumerable.Range( slot.FirstFrame, slot.FrameCount ) ).OrderBy( x => x ).ToArray();
			CollectionAssert.AreEqual( Enumerable.Range( 0, 175 ).ToArray(), used, $"{kid.Name}: the slots cover every frame exactly once" );
			var walkFrames = Enumerable.Range( walk.FirstFrame, walk.FrameCount ).Select( index => kid.Frames[index] );
			Assert.IsTrue( walkFrames.All( frame => frame.OriginY < 0 && -frame.OriginY <= frame.Height && -frame.OriginY >= frame.Height - 6 ), "walk-frame hotspot at the feet" );
			Assert.IsTrue( kid.Pixels.Where( ( value, index ) => index % 4 == 3 ).Any( value => value == 255 ) );
		}
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 0, 1, 1, 1, 0 }, kids.Select( kid => (int)kid.Animations.FlagB ).ToArray() );
	}

	[TestMethod]
	public void ThemeBalanceFileGivesGuestSettings()
	{
		var settings = GuestSettings.Load( "jungle" );
		Assert.AreEqual( 8, settings.Types.Count );
		Assert.IsTrue( settings.Types.All( type => type.PreferredExcitement is > 0 and <= 100 && type.StartingCash > 0 && type.BoredomThreshold > 0 ) );
		Assert.IsTrue( settings.ExitLevelSeconds > settings.ExitLevelVarSeconds && settings.ExitLevelVarSeconds > 0 );
		Assert.IsTrue( settings.PerfectRide > settings.GoodRide && settings.GoodRide > settings.OkRide );
		Assert.IsNotNull( settings.ArrivalLaneA );
		Assert.IsNotNull( settings.ArrivalLaneB );
		Assert.AreEqual( 5, settings.ArrivalLaneA!.Length );
	}

	private static (GuestPathGrid Grid, GuestSettings Settings) LoadJungle()
	{
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		return (GuestPathGrid.FromOriginal( park.Map, park.Save ), GuestSettings.Load( "jungle" ));
	}

	[TestMethod]
	public void JunglePathsFormOneNetworkFromTheEntrances()
	{
		var (grid, settings) = LoadJungle();
		Assert.AreEqual( 78, grid.WalkableCount );
		var entrance = settings.ArrivalLaneA![^1];
		Assert.IsTrue( grid.IsWalkable( entrance.X, entrance.Y ), "Standard.sam EntranceA is an imported path cell" );
		var field = grid.GetFlowField( entrance.X, entrance.Y );
		var reachable = field.Count( distance => distance >= 0 );
		Assert.AreEqual( 78, reachable, "every Easymode path cell is reachable from the entrance" );
	}

	/// <summary>Real guests ride the original Totem.RSE headlessly (no models): it fills before its 10 s time-out and releases its riders.</summary>
	[TestMethod]
	public void GuestsRideTheOriginalTotemScript()
	{
		var (grid, settings) = LoadJungle();
		var simulation = new GuestSimulation( grid, settings, 99 ) { ArrivalsEnabled = false };
		var bridge = new RideVisitorBridge( 1, "Inca Totem", RideVisitorKind.Ride, 6, 70, 25 ) { EntranceCell = (39, 21), ExitCell = (39, 21) };
		RideVM vm;
		using ( var stream = FileSystem.OpenRead( "/levels/jungle/rides/totem/Totem.RSE" ) )
			vm = new RideVM( stream, new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 4 } );
		vm[RideVariables.VAR_CAPACITY] = 6;
		vm[RideVariables.VAR_RIDECLOSED] = 0;
		bridge.Attach( vm, () => vm[RideVariables.VAR_RIDECLOSED] == 0 );
		simulation.Register( bridge );
		foreach ( var cell in new[] { (39, 22), (39, 23), (40, 21), (41, 21), (39, 24), (42, 21), (39, 25), (43, 21) } )
			simulation.SpawnInPark( cell.Item1, cell.Item2 );
		double? runningAt = null;
		var maximumRiders = 0;
		for ( var tick = 0; tick < 60 * 60; tick++ )
		{
			simulation.Tick( Tick );
			bridge.HostStep();
			vm.Advance( Tick );
			maximumRiders = Math.Max( maximumRiders, vm[RideVariables.VAR_ONRIDE] );
			if ( runningAt == null && vm[RideVariables.VAR_RUNNING] == 1 )
				runningAt = vm.TimeMilliseconds;
		}
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
		Assert.AreEqual( 6, maximumRiders, "Totem.RSE boarded a full load through HUSH/WALKON" );
		Assert.IsNotNull( runningAt );
		Assert.IsTrue( runningAt < 10_000, $"started at {runningAt} ms, before the 10 s passenger time-out" );
		Assert.IsTrue( bridge.ReleasedTotal >= 6, "HOP/WALKOFF/WALKGET → VAR_LETMEOFF released the riders" );
		Assert.IsTrue( simulation.Guests.Count( guest => guest.RidesTaken > 0 ) >= 6 );
		Assert.IsTrue( simulation.Guests.All( guest => guest.State != GuestState.Using || bridge.Riders.Contains( guest.Id ) ) );
	}

	[TestMethod]
	public void JungleParkRunIsDeterministic()
	{
		var (grid, settings) = LoadJungle();
		ulong Run( ulong seed )
		{
			var simulation = new GuestSimulation( grid, settings, seed );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( Tick );
			Assert.IsTrue( simulation.Admissions > 0 && simulation.Guests.Count > 20 );
			return simulation.ComputeStateHash();
		}
		Assert.AreEqual( Run( 1 ), Run( 1 ) );
		Assert.AreNotEqual( Run( 1 ), Run( 2 ) );
	}
}
