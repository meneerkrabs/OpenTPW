using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>Park start selection and the Instant Action gates (docs/ECONOMY.md, "Game modes").</summary>
[TestClass]
[DoNotParallelize]
public class ParkStartTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestMethod]
	public void FrontEndChoiceMapsByNameNotByValue()
	{
		Assert.AreEqual( ParkStartKind.FullSimulation, ParkStart.FromFrontEnd( GameMode.FullSimulation ) );
		Assert.AreEqual( ParkStartKind.InstantAction, ParkStart.FromFrontEnd( GameMode.InstantAction ) );
		Assert.AreNotEqual( (int)GameMode.InstantAction, (int)ParkGameMode.InstantAction, "the enums differ in value order, so a cast would swap the modes" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ParkStart.FromFrontEnd( (GameMode)7 ) );
	}

	[TestMethod]
	public void StartsKeepModeBalanceLayerAndSeedSeparate()
	{
		foreach ( var ships in new[] { false, true } )
		{
			foreach ( var easy in new[] { false, true } )
			{
				var full = ParkStart.Resolve( ParkStartKind.FullSimulation, ships, easy );
				Assert.AreEqual( new ParkStart( ParkStartKind.FullSimulation, ParkGameMode.FullSimulation, false, false ), full );
				var instant = ParkStart.Resolve( ParkStartKind.InstantAction, ships, easy );
				Assert.AreEqual( new ParkStart( ParkStartKind.InstantAction, ParkGameMode.InstantAction, easy, ships ), instant );
				var reference = ParkStart.Resolve( ParkStartKind.OriginalSaveReference, ships, easy );
				Assert.AreEqual( new ParkStart( ParkStartKind.OriginalSaveReference, ParkGameMode.FullSimulation, ships && easy, ships ), reference );
			}
		}
		Assert.IsFalse( ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) );
		Assert.IsTrue( ParkStart.ReadsShippedSave( ParkStartKind.InstantAction ) );
		Assert.IsTrue( ParkStart.ReadsShippedSave( ParkStartKind.OriginalSaveReference ) );
	}

	[TestMethod]
	public void InstantActionGatesEveryModeFeature()
	{
		Assert.AreEqual( new ParkModeFeatures( true, true, true, true, true ), ParkModeFeatures.For( ParkGameMode.FullSimulation ) );
		Assert.AreEqual( new ParkModeFeatures( false, false, false, false, false ), ParkModeFeatures.For( ParkGameMode.InstantAction ) );

		var instant = EconomyTestData.Park( mode: ParkGameMode.InstantAction );
		Assert.AreEqual( 0, instant.AvailableLoans.Count() );
		Assert.ThrowsException<InvalidOperationException>( () => instant.TakeLoan( 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => instant.SetResearchEffort( ResearchCategory.Ride, 0 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotAvailableInInstantAction, instant.TryBuyUpgrade( instant.RegisterExisting( 1100 ).Id ) );

		var full = EconomyTestData.Park();
		Assert.IsTrue( full.AvailableLoans.Any() );
		full.SetResearchEffort( ResearchCategory.Ride, 0 );
		Assert.AreEqual( 0, full.Research.Effort[(int)ResearchCategory.Ride] );
	}

	[TestMethod]
	public void InstantActionRunsNoTicketChecksOrChallenges()
	{
		var events = new Dictionary<ParkGameMode, List<ParkEvent>>();
		foreach ( var mode in new[] { ParkGameMode.FullSimulation, ParkGameMode.InstantAction } )
		{
			var park = EconomyTestData.Park( mode: mode );
			events[mode] = new List<ParkEvent>();
			park.EventRaised += events[mode].Add;
			park.OpenPark();
			for ( var visitor = 0; visitor < 100; visitor++ )
				park.TryAdmitVisitor( 100, out _ );
			park.ReportRecord( ParkRecordKind.CoasterHeight, 0, 110 );
			park.AdvanceDays( 540 );
			if ( mode == ParkGameMode.InstantAction )
			{
				Assert.AreEqual( 0, park.Objectives.GoldenTickets.Count );
				Assert.IsNull( park.Objectives.Current );
				Assert.ThrowsException<InvalidOperationException>( park.AcceptChallenge );
			}
		}
		Assert.IsTrue( events[ParkGameMode.FullSimulation].Any( item => item.Kind == ParkEventKind.GoldenTicketWon ) );
		Assert.IsTrue( events[ParkGameMode.FullSimulation].Any( item => item.Kind == ParkEventKind.ChallengeOffered ) );
		Assert.IsFalse( events[ParkGameMode.InstantAction].Any( item => item.Kind is ParkEventKind.GoldenTicketWon or ParkEventKind.ChallengeOffered ) );
	}

	[TestMethod]
	public void JungleStartsDifferByMode()
	{
		UseOriginalData();
		var instant = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle", ParkStart.ReadsShippedSave( ParkStartKind.InstantAction ) ), ParkStartKind.InstantAction );
		Assert.AreEqual( ParkGameMode.InstantAction, instant.Economy.Mode );
		Assert.IsTrue( instant.Economy.Settings.IsEasy );
		Assert.AreEqual( 100000, instant.Economy.Balance, "Easy_Standard.sam InitialCash" );
		Assert.AreEqual( 11, instant.Import!.ImportedObjects );
		Assert.AreEqual( 0, instant.Economy.Staff.Members.Count, "the seed's staff are not decoded, so none are invented" );

		var fullPark = OriginalPark.Load( "jungle", ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) );
		Assert.IsNull( fullPark.Save );
		var full = ParkEconomyRuntime.ForOriginalLevel( fullPark, ParkStartKind.FullSimulation );
		Assert.AreEqual( ParkGameMode.FullSimulation, full.Economy.Mode );
		Assert.IsFalse( full.Economy.Settings.IsEasy );
		Assert.AreEqual( 50000, full.Economy.Balance, "Standard.sam InitialCash" );
		Assert.IsNull( full.Import );
		Assert.AreEqual( 0, full.Economy.Objects.Count );

		var reference = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle" ) );
		Assert.AreEqual( new ParkStart( ParkStartKind.OriginalSaveReference, ParkGameMode.FullSimulation, true, true ), reference.Start );
		Assert.AreEqual( 11, reference.Import!.ImportedObjects );
		Assert.ThrowsException<ArgumentException>( () => ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle" ), ParkStartKind.FullSimulation ), "a Full Simulation start never receives the shipped save" );

		var hallow = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "hallow" ), ParkStartKind.InstantAction );
		Assert.AreEqual( new ParkStart( ParkStartKind.InstantAction, ParkGameMode.InstantAction, false, false ), hallow.Start, "no Easy_ layer or seed outside jungle; the missing layer is not an error" );
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		Log ??= new();
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
