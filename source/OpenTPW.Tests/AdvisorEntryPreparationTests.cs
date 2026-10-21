using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// What a park build reads ahead for the advisor its entering frame creates and lets speak (<see cref="AdvisorEntryPreparation"/>, docs/BROWSER-PERFORMANCE.md "Event hitches"):
/// a prepared result is the one its reader would read, it is taken once and only from the file system it was read from, a failed read is not kept, and
/// what a read logged is written when its result is taken and dropped with it otherwise.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorEntryPreparationTests
{
	private BaseFileSystem previous = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		previous = FileSystem;
	}

	[TestCleanup]
	public void Cleanup()
	{
		AdvisorEntryPreparation.Drop();
		FileSystem = previous;
	}

	private static BaseFileSystem EmptyFileSystem() => new( Directory.CreateTempSubdirectory( "opentpw-advisor-" ).FullName );

	private sealed class Disposable : IDisposable
	{
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}

	[TestMethod]
	public void APreparedResultIsTakenOnceFromItsOwnFileSystem()
	{
		var files = EmptyFileSystem();
		var reads = 0;
		AdvisorEntryPreparation.Prepare( "key", files, () => ++reads );
		AdvisorEntryPreparation.Prepare( "key", files, () => ++reads );
		Assert.AreEqual( 1, reads, "a result is read once" );
		Assert.IsFalse( AdvisorEntryPreparation.TryTake<int>( "key", EmptyFileSystem(), out _ ), "not from another file system" );
		Assert.IsFalse( AdvisorEntryPreparation.TryTake<string>( "key", files, out _ ), "not as another type" );
		Assert.IsTrue( AdvisorEntryPreparation.TryTake<int>( "key", files, out var value ) );
		Assert.AreEqual( 1, value );
		Assert.IsFalse( AdvisorEntryPreparation.TryTake<int>( "key", files, out _ ), "taken once" );
		// a read that found nothing is kept as its answer
		AdvisorEntryPreparation.Prepare( "nothing", files, () => null );
		Assert.IsTrue( AdvisorEntryPreparation.TryTake<string>( "nothing", files, out var nothing ) );
		Assert.IsNull( nothing );
	}

	[TestMethod]
	public void AFailedReadIsNotKeptAndItsMessagesAreNotWritten()
	{
		var files = EmptyFileSystem();
		var written = new List<string>();
		Logger.LogDelegate record = ( _, text ) => written.Add( text );
		Logger.OnLog += record;
		try
		{
			AdvisorEntryPreparation.Prepare( "key", files, () =>
			{
				Log.Warning( "read half" );
				throw new IOException( "simulated failure" );
			} );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count, "the reader meets the failure itself" );
			Assert.IsFalse( written.Contains( "read half" ) );
			Log.Warning( "after the preparation" );
			Assert.IsTrue( written.Contains( "after the preparation" ), "the capture ended with the read" );
		}
		finally
		{
			Logger.OnLog -= record;
		}
	}

	[TestMethod]
	public void MessagesAreWrittenWhenTheResultIsTakenAndDroppedWithIt()
	{
		var files = EmptyFileSystem();
		var written = new List<string>();
		Logger.LogDelegate record = ( _, text ) => written.Add( text );
		Logger.OnLog += record;
		try
		{
			var dropped = new Disposable();
			AdvisorEntryPreparation.Prepare( "taken", files, () => { Log.Warning( "taken read" ); return "value"; } );
			AdvisorEntryPreparation.Prepare( "dropped", files, () => { Log.Warning( "dropped read" ); return dropped; } );
			Assert.AreEqual( 0, written.Count, "nothing is written while preparing" );
			Assert.IsTrue( AdvisorEntryPreparation.TryTake<string>( "taken", files, out _ ) );
			CollectionAssert.AreEqual( new[] { "taken read" }, written );
			AdvisorEntryPreparation.Drop();
			Assert.IsTrue( dropped.Disposed, "a dropped result is disposed" );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count );
			CollectionAssert.AreEqual( new[] { "taken read" }, written, "a dropped result's messages are not written" );
		}
		finally
		{
			Logger.OnLog -= record;
		}
	}

	[TestMethod]
	public void ALookupInvalidationOrAModChangeDropsAPreparedResult()
	{
		foreach ( var change in new Action<BaseFileSystem>[] { files => files.InvalidateLookups(), files => files.Overrides = null, files => files.ArchiveRedirect = null } )
		{
			var files = EmptyFileSystem();
			var stale = new Disposable();
			AdvisorEntryPreparation.Prepare( "key", files, () => stale );
			change( files );
			Assert.IsFalse( AdvisorEntryPreparation.TryTake<Disposable>( "key", files, out _ ), "read before the file system changed what it reads" );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count, "and dropped" );
			Assert.IsTrue( stale.Disposed );

			// a preparation for a changed file system reads again instead of keeping the old result
			var first = new Disposable();
			var reads = 0;
			AdvisorEntryPreparation.Prepare( "key", files, () => { reads++; return first; } );
			change( files );
			var second = new Disposable();
			AdvisorEntryPreparation.Prepare( "key", files, () => { reads++; return second; } );
			Assert.AreEqual( 2, reads );
			Assert.IsTrue( first.Disposed );
			Assert.IsTrue( AdvisorEntryPreparation.TryTake<Disposable>( "key", files, out var taken ) );
			Assert.AreSame( second, taken );
		}
	}

	private static BaseFileSystem GameFileSystem()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var files = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		files.RegisterArchiveHandler<WadArchive>( ".wad" );
		return files;
	}

	[TestMethod]
	public void APreparedBalanceMakesTheAdvisorTheFileMakes()
	{
		FileSystem = GameFileSystem();
		var fromFile = AutomaticAdvisor.TryCreate()!.Controller.Balance;
		AdvisorEntryPreparation.Prepare( AdvisorEntryPreparation.BalanceKey, FileSystem, AdvisorBalance.Load );
		var balance = AutomaticAdvisor.TryCreate()!.Controller.Balance;
		Assert.AreEqual( 0, AdvisorEntryPreparation.Count, "the advisor took the prepared settings" );
		Assert.AreEqual( (fromFile.MinimumScore, fromFile.MinimumTimeAnyMessage, fromFile.MinimumTimeSameMessage, fromFile.ContextHelpScore),
			(balance.MinimumScore, balance.MinimumTimeAnyMessage, balance.MinimumTimeSameMessage, balance.ContextHelpScore) );
		CollectionAssert.AreEquivalent( fromFile.Scores.ToList(), balance.Scores.ToList() );
	}

	[TestMethod]
	public void ThePresentationStepsReadWhatTheFirstLineReads()
	{
		FileSystem = GameFileSystem();
		foreach ( var _ in AdvisorEntryPreparation.PresentationSteps( FileSystem, "jungle", globalRig: false ) ) { }
		foreach ( var archive in new[] { AdvisorRig.LevelArchive( "jungle" ), Advisor.ArchivePath } )
		{
			var direct = AdvisorClipDurations.LoadArchive( FileSystem, archive );
			Assert.IsTrue( AdvisorEntryPreparation.TryTake<AdvisorClipDurations>( AdvisorEntryPreparation.DurationsKey( archive ), FileSystem, out var prepared ), archive );
			Assert.AreEqual( direct == null, prepared == null, archive );
			for ( var clip = 1; direct != null && clip <= AdvisorClipDurations.Count; clip++ )
				Assert.AreEqual( (direct.Of( clip ), direct.PlayOf( clip )), (prepared!.Of( clip ), prepared.PlayOf( clip )), $"{archive} clip {clip}" );
		}
		var theme = AdvisorThemeCostume.Load( FileSystem, "jungle" );
		Assert.IsTrue( AdvisorEntryPreparation.TryTake<AdvisorThemeCostume>( AdvisorEntryPreparation.ThemeKey( "jungle" ), FileSystem, out var preparedTheme ) );
		CollectionAssert.AreEqual( theme.AddOns.ToList(), preparedTheme!.AddOns.ToList() );
		CollectionAssert.AreEqual( theme.SubOffs.ToList(), preparedTheme.SubOffs.ToList() );
	}
}
