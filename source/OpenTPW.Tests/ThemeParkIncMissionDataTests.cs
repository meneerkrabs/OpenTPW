using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's mission controller over the shipped data (docs/reverse/TPI-MISSIONS.md): mission 0 of a new career from the Science economy to mission 1. Needs OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncMissionDataTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	private static void UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
	}

	private sealed class Guests( int census ) : IParkGuestStatistics
	{
		public int Census { get; set; } = census;
		public int PeopleInPark => Census;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	/// <summary>The prebuilt Science park's attraction things and land as the controller's world; it records the obstacles it opens and the cameras it is asked for.</summary>
	private sealed class PrebuiltWorld( ThemeParkIncSaveWorld save, ParkCellMap cells ) : IThemeParkIncMissionWorld
	{
		public ushort CurrentObstacle { get; private set; } = save.Variables.CurrentObstacle;
		public List<ushort> Opened { get; } = new();
		public List<ThemeParkIncCameraRequest> Cameras { get; } = new();

		public void FindCurrentObstacle( int type )
		{
			if ( type == 0 )
				CurrentObstacle = 0;
			foreach ( var attraction in save.Attractions.Where( attraction => attraction.InfoId == type ) )
				CurrentObstacle = (ushort)attraction.Id;
		}

		public ushort FindAttraction( int type ) => save.Attractions.FirstOrDefault( attraction => attraction.InfoId == type ) is { } found ? (ushort)found.Id : (ushort)0;
		public bool OpensAutomatically( int type ) => false;
		public void OpenObstacle( ushort thing ) => Opened.Add( thing );
		public void DestroyThing( ushort thing ) => throw new InvalidOperationException( "No shipped mission destroys a thing." );
		public void LoadExtent( int index ) => Assert.IsTrue( ThemeParkIncLand.ApplyExtent( cells, index ) );
		public void PlayCamera( ThemeParkIncCameraRequest request ) => Cameras.Add( request );
	}

	[TestMethod]
	public void MissionZeroCompletesFromFortyGuestsToMissionOne()
	{
		UseTpiData();
		var park = OriginalPark.Load( "science" );
		var save = park.Save!.ThemeParkIncWorld!;
		var gate = save.Attractions.Single( attraction => attraction.InfoId == 1700 );
		var lake = save.Attractions.Single( attraction => attraction.InfoId == 1703 );
		Assert.AreEqual( (ushort)gate.Id, save.Variables.CurrentObstacle, "the prebuilt's current obstacle is the 1700 gate" );
		var cells = Level.OriginalCellMap( park, GuestSettings.Load( "science" ), null );
		Assert.AreEqual( 1259, ThemeParkIncLand.OwnedCount( cells ) );

		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( park ).Economy;
		var career = economy.TpiCareer!;
		var missions = economy.TpiMissions!;
		Assert.AreEqual( (0, 0, "science"), (career.MissionCounter, career.Mission.Number, career.CurrentZone!.Name) );
		Assert.AreEqual( (1, 8, 40), (career.StatusWord( 0, 0 ), career.StatusWord( 0, 1 ), career.StatusWord( 0, 2 )), "miss00's statistic started: 40 guests in the park" );
		var world = new PrebuiltWorld( save, cells );
		economy.TpiMissionWorld = world;
		var guests = new Guests( 39 );
		economy.GuestStatistics = guests;
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 1, 0, economy.TpiCareer!.Mission.Number, false ), "mission 0 lists no thinking space" );

		void AdvanceTo( long turn ) => economy.Advance( ParkCalendar.TickOfTurn( turn ) - economy.Tick );
		AdvanceTo( 28 );
		Assert.AreEqual( (1, 39), (career.StatusWord( 0, 0 ), career.StatusWord( 0, 6 )), "day 1: 39 guests" );
		Assert.IsFalse( economy.ThemeParkIncMissionMet );
		guests.Census = 40;
		AdvanceTo( 52 );
		Assert.AreEqual( (2, 40), (career.StatusWord( 0, 0 ), career.StatusWord( 0, 6 )), "day 2: 40 guests reach the target" );
		Assert.IsTrue( economy.ThemeParkIncMissionMet, "the mission window's button would be enabled" );

		Assert.IsTrue( missions.OnMissionComplete( economy.Turn ) );
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleOpened, (ushort)gate.Id), (missions.State, missions.CutsceneObject) );
		Assert.AreEqual( new ThemeParkIncCameraRequest( (ushort)gate.Id, 2, 0 ), world.Cameras.Single(), "the camera flies to the 1700 gate (anim 2, mode 0)" );
		Assert.AreEqual( 0, career.MissionCounter, "nothing completes before the camera's end" );

		missions.CameraFinished( economy.Turn );
		CollectionAssert.AreEqual( new[] { (ushort)gate.Id }, world.Opened, "the gate opens" );
		Assert.AreEqual( 3030, ThemeParkIncLand.OwnedCount( cells ), "extent.000 grows the land" );
		Assert.AreEqual( (1, 1, ThemeParkIncMissionState.Idle), (career.MissionCounter, career.Mission.Number, missions.State) );
		Assert.AreEqual( (1703, (ushort)lake.Id), (career.Mission.ObstacleType, world.CurrentObstacle), "miss01's obstacle is the lake" );
		Assert.AreEqual( 1, career.Regions[13], "mission 1 opens its overview region" );
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 1, 0, career.Mission.Number, false ), "mission 1 lists the thinking spaces" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 1, career.Mission.Number, missions.ObstacleState() == 2 ), "the training room waits for the lake's research" );
		Assert.AreEqual( 1, missions.ObstacleState(), "the lake is not researched" );
		Assert.IsFalse( economy.ThemeParkIncMissionMet, "an obstacle mission waits for the research and the staff slots" );
		Assert.IsFalse( missions.RegisterObstacleResearch, "the advance does not set +0x1A again: only the constructor and a load do" );
		AdvanceTo( economy.Turn + 1 );
		Assert.IsTrue( career.ObjectiveLetterPosted );
		CollectionAssert.AreEqual( new[] { 0 }, economy.TpiObjectiveLetters, "the objective letter of mission 0" );
	}
}
