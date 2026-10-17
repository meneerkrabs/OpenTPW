using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The export of an OpenTPW park's economy into an original-format park (<see cref="OriginalParkExport"/>). The synthetic tests check each value lands in the original's
/// field; <see cref="ExportingAnImportedParkReproducesItsSaveExceptForTheStampsTheOriginalWouldWriteToo"/> reads every original save into the economy and writes it back.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalParkExportTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	private static (ParkEconomy Economy, BalanceSettings Settings) Economy()
	{
		var settings = OriginalSaveImportTests.Settings();
		return (new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 ), settings);
	}

	private static SaveBank BankOf( OriginalSavePayload payload ) =>
		SaveBank.Read( payload.World.Things.Single( thing => thing.Model == SaveThingModel.Bank ).Body, new SaveThing( 1, SaveThingModel.Bank, 0, 0, 0, SaveBank.Bytes ) );

	[TestMethod]
	public void TheEconomysTurnOpenFlagBankAndLoansLandInTheOriginalsFields()
	{
		var (economy, settings) = Economy();
		economy.StartAtOriginalTurn( 600 );
		economy.OpenPark();
		economy.SetEntranceFee( 35 );
		var loan = economy.TakeLoan( 2 );
		var template = OriginalSavePayloadFixtures.Blank( settings );
		var park = ParkSaveFile.Capture( economy );
		var exported = OriginalParkExport.Export( template, park, out var evidence, new OriginalParkExportOptions( VisitorsToDate: 321 ) );
		var variables = exported.World.Variables;
		Assert.AreEqual( (600u, 0u, 321u), (variables.GameTick, variables.ParkClosed, variables.NumberOfVisitorsToDate) );
		Assert.AreEqual( park.WorldGenerator, variables.RandomSeed );
		Assert.AreEqual( OriginalSavePayloadFixtures.BankId, variables.BankAccount, "the thing ids are the template's" );
		var bank = BankOf( exported );
		Assert.AreEqual( (35, (int)economy.Ledger.Balance, true), (bank.Account.AdmissionFee, bank.Account.Balance, bank.Account.WithdrawalsEnabled) );
		Assert.AreEqual( 8, bank.Loans.Count );
		Assert.IsTrue( bank.Loans[2].Bought );
		Assert.AreEqual( (int)loan.OriginalAmount, bank.Loans[2].Amount );
		Assert.AreEqual( (int)loan.MonthlyRepayment, bank.Loans[2].MonthlyRepayment );
		Assert.AreEqual( 0, bank.Loans[2].MonthsRepaid );
		Assert.AreEqual( 1, bank.Loans.Count( offer => offer.Bought ) );
		Assert.IsTrue( evidence.Any( line => line.StartsWith( "Bank:", StringComparison.Ordinal ) && line.Contains( "1 bought loans" ) ) );
		// the calendar of a played park: the epoch, the date caches and the rate the import checks
		var date = ParkCalendar.DateAtTurn( 600 );
		Assert.AreEqual( ((ParkCalendar.Epoch - new DateTime( 1601, 1, 1 )).Ticks, date.Month - 1, date.Day, 15000), (exported.World.Calendar.FunnyTimeStart, exported.World.Calendar.MonthAtLastUpdate, exported.World.Calendar.DayAtLastUpdate, exported.World.Calendar.FunnySecsPerRealSec) );
		Assert.AreEqual( 1234, exported.World.Calendar.SessionStart, "the session start is the template's wall-clock stamp" );
		// the export is a payload the loader's split accepts
		var layout = SavePayloadLayout.Parse( exported.Write() );
		Assert.AreEqual( 19, layout.Blocks.Count );
	}

	[TestMethod]
	public void AParkThatHasNotRunKeepsTheConstructorsCalendarAndTheTemplateIsNeverChanged()
	{
		var (economy, settings) = Economy();
		var template = OriginalSavePayloadFixtures.Blank( settings );
		var before = template.Write();
		var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out var evidence );
		Assert.IsTrue( exported.World.Calendar.IsInitial );
		Assert.AreEqual( 0u, exported.World.Variables.GameTick );
		Assert.AreEqual( 1u, exported.World.Variables.ParkClosed, "a new park is closed" );
		Assert.IsTrue( evidence.Any( line => line.StartsWith( "Calendar: the park has not run", StringComparison.Ordinal ) ) );
		CollectionAssert.AreEqual( before, template.Write(), "the template is not modified" );
		Assert.AreNotSame( template.World, exported.World );
	}

	[TestMethod]
	public void FirstPurchaseTurnsReplaceTheControlWordsOfTheTypesTheEconomyBought()
	{
		var (economy, settings) = Economy();
		economy.Counters.Max( ParkCounters.FirstPurchaseTurn( 1100 ), 777 );
		economy.Counters.Max( ParkCounters.FirstPurchaseTurn( 4242 ), 5 );
		var exported = OriginalParkExport.Export( OriginalSavePayloadFixtures.Blank( settings ), ParkSaveFile.Capture( economy ), out _ );
		uint Word( int index ) => BitConverter.ToUInt32( exported.World.Controls.Records, index * SaveObjectControl.Bytes + 0x1C );
		Assert.AreEqual( 777u, Word( 0 ) );
		Assert.AreEqual( 0u, Word( 1 ), "a type without a counter keeps its word" );
		// a type the control table does not hold is not added: the original's table grows only when an object of the type is built
		Assert.AreEqual( 2u, exported.World.Controls.Count );
	}

	[TestMethod]
	public void ValuesTheOriginalsWordsCannotHoldAreRefusedAndATemplateWithoutABankHasNowhereToGo()
	{
		var (economy, settings) = Economy();
		economy.Ledger.Restore( 3_000_000_000, 3_000_000_000, 0, new Dictionary<LedgerCategory, long>(), Array.Empty<LedgerMonth>() );
		Assert.ThrowsException<InvalidDataException>( () => OriginalParkExport.Export( OriginalSavePayloadFixtures.Blank( settings ), ParkSaveFile.Capture( economy ), out _ ) );
		var (fresh, _) = Economy();
		var noBank = OriginalSavePayloadFixtures.Blank( settings );
		noBank.World.Things[0].Model = SaveThingModel.UIMsgReceiver;
		noBank.World.Things[0].Body = new byte[8];
		Assert.ThrowsException<InvalidDataException>( () => OriginalParkExport.Export( noBank, ParkSaveFile.Capture( fresh ), out _ ) );
	}

	[TestMethod]
	public void TheExportedFileIsAnOriginalContainerThatReadsBack()
	{
		var (economy, settings) = Economy();
		economy.StartAtOriginalTurn( 40 );
		var template = OriginalSavePayloadFixtures.Blank( settings );
		var fields = new SaveContainerFields( SaveContainerFields.WriterMagic, 0, "OpenTPW test export", new byte[32] );
		var file = OriginalParkExport.ExportFile( fields, template, ParkSaveFile.Capture( economy ), out _ );
		using var reader = new SaveReader( new MemoryStream( file ) );
		var payload = reader.ReadFile();
		Assert.AreEqual( 40u, OriginalSavePayload.Read( payload ).World.Variables.GameTick );
		Assert.AreEqual( 19, SavePayloadLayout.Parse( payload ).Blocks.Count );
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	/// <summary>
	/// Every original save (the eleven private ones and the shipped Jungle <c>Easymode.TPWI</c>) is imported into an economy, captured and exported back onto its own bytes. What
	/// the economy models is written exactly as the original had it: the four <c>restart.INTS</c> and every other field of every save come back unchanged. The one thing that
	/// differs is a first-purchase turn the import stamps into a type whose control word was 0 (the original's own stamp when the first saved object of the type is built,
	/// <c>0x4DB690</c>, so the original writes it too on its next save): its value is the saved park turn. The re-read export goes through the loader's own split and the
	/// importers again and gives the same economy.
	/// </summary>
	[TestMethod]
	public void ExportingAnImportedParkReproducesItsSaveExceptForTheStampsTheOriginalWouldWriteToo()
	{
		var files = OriginalSaveWriterCorpusTests.CorpusFiles().ToList();
		UseOriginalData();
		var easymode = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" );
		if ( File.Exists( easymode ) )
			files.Add( ("jungle/Easymode.TPWI", easymode) );
		var failures = new List<string>();
		foreach ( var (name, path) in files )
		{
			var easy = name.EndsWith( "Easymode.TPWI", StringComparison.Ordinal );
			var theme = easy ? "jungle" : Path.GetFileName( Path.GetDirectoryName( path ) )!;
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( payload, map );
			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy ), ParkGameMode.FullSimulation, 1 );
			var import = OriginalEconomyImport.Apply( economy, park );
			if ( !import.ParkClosed )
				economy.OpenPark();
			var template = OriginalSavePayload.Read( payload );
			var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out _, new OriginalParkExportOptions( park.World.Variables.NumberOfVisitorsToDate ) );
			var written = exported.Write();
			Assert.AreEqual( payload.Length, written.Length, name );
			var differing = Enumerable.Range( 0, payload.Length ).Where( index => payload[index] != written[index] ).ToList();
			var layout = SavePayloadLayout.Parse( payload );
			var controls = layout[SaveBlock.World].Offset + SaveWorldVariables.Bytes;
			foreach ( var index in differing )
			{
				var relative = index - controls;
				// only the low bytes of a control record's first-purchase word (+0x1C); the table's records are 32 bytes
				if ( relative is < 0 or >= SaveObjectControls.Slots * SaveObjectControl.Bytes || relative % SaveObjectControl.Bytes < 0x1C )
					failures.Add( $"{name}: byte {index} differs outside a first-purchase word" );
			}
			foreach ( var record in differing.Where( index => index >= controls && index < controls + SaveObjectControls.Slots * SaveObjectControl.Bytes ).Select( index => (index - controls) / SaveObjectControl.Bytes ).Distinct() )
			{
				var word = BitConverter.ToUInt32( written, controls + record * SaveObjectControl.Bytes + 0x1C );
				if ( word != park.World.Variables.GameTick )
					failures.Add( $"{name}: control {record} first-purchase word {word} is not the saved turn {park.World.Variables.GameTick}" );
				if ( BitConverter.ToUInt32( payload, controls + record * SaveObjectControl.Bytes + 0x1C ) != 0 )
					failures.Add( $"{name}: control {record} had a first-purchase turn the export replaced" );
			}
			if ( name.EndsWith( ".INTS", StringComparison.Ordinal ) && differing.Count != 0 )
				failures.Add( $"{name}: a restart save, which holds no stamped type, differs at {differing.Count} bytes" );
			// the loader's rules (the split, each block's reader and tag) and the importers accept the export and see the same economy
			var again = OriginalParkImport.Import( written, map );
			var economyAgain = new ParkEconomy( BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy ), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( economyAgain, again );
			if ( !import.ParkClosed )
				economyAgain.OpenPark();
			var (first, second) = (ParkSaveFile.Capture( economy ), ParkSaveFile.Capture( economyAgain ));
			if ( (first.Tick, first.EntranceFee, first.Ledger.Balance, first.ParkOpen, first.WorldGenerator, first.Loans.Count) != (second.Tick, second.EntranceFee, second.Ledger.Balance, second.ParkOpen, second.WorldGenerator, second.Loans.Count) )
				failures.Add( $"{name}: the re-imported economy differs from the exported one" );
			var container = SaveContainerWriter.Write( SaveContainerFields.Of( reader ), written );
			using var back = new SaveReader( new MemoryStream( container ) );
			CollectionAssert.AreEqual( written, back.ReadFile(), name );
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures ) );
	}
}
