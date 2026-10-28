using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The cells a kart or water ride's placement leaves (docs/reverse/ORACLE.md, X38; ECON-026): <see cref="StationFootprintGolden"/> holds, from tp.exe's own placement routines run in the x86 oracle (the commit
/// 0x528A70, the station line, the exit and entry tiles), every cell near a placed station and the net worth 0x4C7DB0 of the map. The level's placement (the track set-up's station handler first, then the
/// footprint and the access cells, as <c>Level</c> wires them) must leave the same cells.
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>The level as <see cref="World"/> wires it, over a 128 × 128 cell map, with a ride of the golden case's shape placed at (16, 16).</summary>
	private (Level Level, ParkCellMap Cells, OriginalObject Ride, TrackCellLayer Tracks) StationWorld( StationFootprintGolden.Case item )
	{
		var ride = Entry( ObjectCategory.Ride, $"Info.Id 1150\nInfo.Name SyntheticStation\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType {item.TrackType}\nBumper.BumperType {(item.TrackType == 1 ? -4 : -5)}\n"
			+ "Bumper.NorthXAdjust -2\nBumper.NorthYAdjust 1\nBumper.EastXAdjust 1\nBumper.EastYAdjust 4\nBumper.SouthXAdjust 4\nBumper.SouthYAdjust 1\nBumper.WestXAdjust 1\nBumper.WestYAdjust -2\n", item.Shape );
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), Private, null, new object[] { "jungle", new[] { ride } }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( catalog, new SandboxParkGrid() );
		var guests = new GuestSimulation( new GuestPathGrid( 128, 128 ), GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "trackStations", new Dictionary<OriginalObject, TrackStation>() );
		Set( level, "trackRenderers", new Dictionary<TrackFamily, TrackRenderer>() );
		Set( level, "trackPieceAnimations", new TrackPieceAnimations() );
		Set( level, "waterPieceHandlers", new Dictionary<OriginalObject, Action<bool>>() );
		Set( level, "addOnSurfaces", new Dictionary<ObjectCatalogEntry, TrackSurface?>() );
		typeof( Level ).GetMethod( "SetupTracks", Private )!.Invoke( level, null );
		// the footprint and access-cell handlers the level wires after its track set-up (Level.ConnectObjectsToGuests), in that order
		var cells = guests.Grid.Cells;
		objects.ObjectPlaced += placed => cells.WriteFootprint( placed.Cells, Level.OwnerWord( placed ), placed.Placement.Rotation );
		objects.ObjectPlaced += placed => ObjectAccessCells.Lay( cells, placed.AccessPoints, Level.OwnerWord( placed ), true, new LinkEffects( null ) );
		var placedRide = objects.TryPlace( ride, 16, 16, item.Rotation, out var refusal );
		Assert.IsNotNull( placedRide, refusal.ToString() );
		return (level, cells, placedRide!, item.TrackType == 1 ? level.KartTracks!.Tracks : level.WaterTracks!.Tracks);
	}

	[TestMethod]
	public void APlacedStationLeavesTheCellsOfTheOriginalsPlacement()
	{
		foreach ( var item in StationFootprintGolden.Cases )
			CheckStation( $"{item.Name} {item.Rotation}", item );
	}

	private void CheckStation( string name, StationFootprintGolden.Case item )
	{
		var (_, cells, _, tracks) = StationWorld( item );
		if ( item.AddOnAt is { } at )
		{
			// the add-on's commit over the exit tile, as ObjectPlaced writes it (the tool's validation is not part of the oracle's run)
			var shape = new ObjectShape( new[] { "++", "++" } );
			cells.WriteFootprint( shape.Cells.Select( cell => (16 + at.Dx + cell.U, 16 + at.Dy + cell.V, cell) ), 0, 0 );
		}
		var expected = item.Cells.ToDictionary( cell => (16 + cell.Dx, 16 + cell.Dy) );
		var differences = new List<string>();
		for ( var y = 16 - 8; y <= 16 + 8; y++ )
		{
			for ( var x = 16 - 8; x <= 16 + 8; x++ )
			{
				var track = tracks.Get( x, y );
				var (type, _) = ParkEconomy.CellForValue( cells, x, y );
				var parent = track.IsExtension && track.Parent >= 0 ? (tracks.CellOf( track.Parent ).X - x, tracks.CellOf( track.Parent ).Y - y) : (0, 0);
				var actual = new StationFootprintGolden.Cell( x - 16, y - 16, type, (ushort)cells.FlagsAt( x, y ), cells.LinksAt( x, y ), cells.QueueLinkAt( x, y ), track.Type, track.Mask, track.Link, parent.Item1, parent.Item2 );
				if ( !expected.TryGetValue( (x, y), out var golden ) )
				{
					if ( actual.Type != 0 || actual.Record != 0 || actual.Links != 0 || actual.Queue != 0 )
						differences.Add( $"unexpected {actual}" );
				}
				else if ( actual != golden )
					differences.Add( $"({x - 16}, {y - 16}): oracle {golden}, level {actual}" );
			}
		}
		Assert.AreEqual( 0, differences.Count, $"{name}: {string.Join( "; ", differences.Take( 6 ) )}" );
		var park = EconomyTestData.Park();
		park.Litter.Cells = cells;
		Assert.AreEqual( item.NetWorth, (int)park.ParkValue, $"{name}: the net worth 0x4C7DB0" );
	}

	/// <summary>A park saved before the footprint's type went under the station tiles holds base type 0 there; the restore gives the oracle's cells (the net worth too).</summary>
	[TestMethod]
	public void ARestoredStationSavedWithoutTheBaseTypeUnderItsTilesGetsTheOriginalsCells()
	{
		var item = StationFootprintGolden.Cases.First( entry => entry.Name == "kart" && entry.Rotation == 90 );
		var (level, cells, _, tracks) = StationWorld( item );
		var saved = level.CaptureTracksSnapshot()!;
		var legacy = new TrackWorldSaveData
		{
			Cells = saved.Cells.Select( cell => cell with { BaseType = 0 } ).ToList(), Controllers = saved.Controllers, CosmeticState = saved.CosmeticState, FrameCount = saved.FrameCount, TickRemainder = saved.TickRemainder
		};
		// the cells of the save's own map: the tiles with base type 0
		TrackSave.RestoreCells( cells, legacy );
		var park = EconomyTestData.Park();
		park.Litter.Cells = cells;
		Assert.AreEqual( item.NetWorth + 16, (int)park.ParkValue, "the eight cells under the station tiles count 2 each before the restore" );
		level.RestoreTracksSnapshot( legacy );
		park.Litter.Cells = cells;
		Assert.AreEqual( item.NetWorth, (int)park.ParkValue, "the footprint's type goes under the tiles again" );
	}
}
