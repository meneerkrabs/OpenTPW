using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Signs composed ahead on a worker (<see cref="ObjectSigns.ComposeAhead"/>, issue #754) are the signs <see cref="ObjectSigns.Get"/> composes
/// on the calling thread, byte for byte, with the same log; a composition made before the file system changed is not used. Over the game data's
/// own file system (<see cref="GameDataFileSystem.Create"/>, remembered lookups) and the debug sign export. Inconclusive without
/// <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectSignComposeAheadTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };

	private BaseFileSystem? previousFileSystem;
	private Logger? previousLog;
	private bool initialized;
	private string root = null!;

	[TestInitialize]
	public void Initialize()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the signs composed ahead." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !Directory.Exists( Path.Combine( dataPath, "levels", "jungle", "rides" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH has no level data." );
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new Logger();
		FileSystem = GameDataFileSystem.Create( dataPath );
		ObjectCatalog.BonusDataRoot = null;
		root = Directory.CreateTempSubdirectory( "opentpw-sign-ahead-" ).FullName;
		ObjectSigns.ForgetForTests();
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( !initialized )
			return;
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = !OperatingSystem.IsBrowser();
		FileSystem = previousFileSystem!;
		Log = previousLog!;
		ObjectCatalog.BonusDataRoot = null;
		Directory.Delete( root, true );
	}

	private static List<ObjectCatalogEntry> SignEntries() =>
		Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.Where( entry => entry.FileSystem.GetFiles( entry.ArchivePath ).Any( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) ) )
			.ToList();

	private sealed record Composed( string Line1, string Line2, byte[] Left, byte[] Right, int TextPixels, string[] Diagnostics, string[] Logged );

	/// <summary><see cref="ObjectSigns.Get"/> of every entry, with what each call logged.</summary>
	private static List<Composed?> GetAll( IEnumerable<ObjectCatalogEntry> entries )
	{
		var result = new List<Composed?>();
		foreach ( var entry in entries )
		{
			var logged = new List<string>();
			void Record( Logger.Level severity, string text ) => logged.Add( $"{severity}: {text}" );
			Logger.OnLog += Record;
			ObjectSignImages? images;
			try { images = ObjectSigns.Get( entry ); }
			finally { Logger.OnLog -= Record; }
			result.Add( images == null ? null : new Composed( images.Line1, images.Line2, images.Left, images.Right, images.TextPixelCount, images.Diagnostics.ToArray(), logged.ToArray() ) );
		}
		return result;
	}

	private static void AssertSame( IReadOnlyList<ObjectCatalogEntry> entries, IReadOnlyList<Composed?> expected, IReadOnlyList<Composed?> actual )
	{
		Assert.AreEqual( expected.Count, actual.Count );
		for ( var index = 0; index < expected.Count; index++ )
		{
			var name = entries[index].ArchivePath;
			Assert.AreEqual( expected[index] == null, actual[index] == null, name );
			if ( expected[index] is not { } want || actual[index] is not { } have )
				continue;
			Assert.AreEqual( (want.Line1, want.Line2, want.TextPixels), (have.Line1, have.Line2, have.TextPixels), name );
			CollectionAssert.AreEqual( want.Left, have.Left, $"{name} sign1 pixels" );
			CollectionAssert.AreEqual( want.Right, have.Right, $"{name} sign2 pixels" );
			CollectionAssert.AreEqual( want.Diagnostics, have.Diagnostics, $"{name} diagnostics" );
			CollectionAssert.AreEqual( want.Logged, have.Logged, $"{name} log" );
		}
	}

	[TestMethod]
	public void ASignComposedAheadIsTheSignComposedOnUseByteForByte()
	{
		var entries = SignEntries();
		Assert.IsTrue( entries.Count >= 75, $"the four themes have at least 75 signs ({entries.Count})" );

		var onUse = GetAll( entries );
		Assert.IsTrue( onUse.All( composed => composed != null ), "every sign composes" );

		ObjectSigns.ForgetForTests();
		var workerLog = new List<string>();
		void Record( Logger.Level severity, string text ) => workerLog.Add( text );
		Logger.OnLog += Record;
		try
		{
			foreach ( var entry in entries )
				ObjectSigns.ComposeAhead( entry );
			Assert.IsTrue( ObjectSigns.WaitForComposeAhead( TimeSpan.FromMinutes( 5 ) ), "the worker finishes" );
		}
		finally { Logger.OnLog -= Record; }
		CollectionAssert.AreEqual( Array.Empty<string>(), workerLog, "the worker logs nothing itself: Get writes what a composition logged when it uses it" );
		Assert.IsTrue( entries.All( ObjectSigns.ComposedAhead ), "every sign was composed on the worker" );

		AssertSame( entries, onUse, GetAll( entries ) );
	}

	[TestMethod]
	public void GetWaitsForTheWorkerOrComposesWhatItHasNotStarted()
	{
		var entries = SignEntries();
		var onUse = GetAll( entries );
		ObjectSigns.ForgetForTests();
		foreach ( var entry in entries )
			ObjectSigns.ComposeAhead( entry );
		// asked at once in reverse: the last ones are still queued (composed here), one is on the worker (waited for), the first ones are done
		var reversed = Enumerable.Range( 0, entries.Count ).Reverse().ToArray();
		var mixed = GetAll( reversed.Select( index => entries[index] ) );
		AssertSame( reversed.Select( index => entries[index] ).ToList(), reversed.Select( index => onUse[index] ).ToList(), mixed );
		Assert.IsTrue( ObjectSigns.WaitForComposeAhead( TimeSpan.FromMinutes( 5 ) ) );
	}

	[TestMethod]
	public void ACompositionMadeBeforeTheFileSystemChangedIsNotUsed()
	{
		var entry = SignEntries().First();
		var onUse = GetAll( new[] { entry } );
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAhead( entry );
		Assert.IsTrue( ObjectSigns.WaitForComposeAhead( TimeSpan.FromMinutes( 1 ) ) );
		Assert.IsTrue( ObjectSigns.ComposedAhead( entry ) );
		// what changes the lookups (a mod set, the interface language) leaves the composition unused: Get composes again
		FileSystem.InvalidateLookups();
		LoadProfile.Enable( true );
		try
		{
			AssertSame( new[] { entry }, onUse, GetAll( new[] { entry } ) );
			Assert.AreEqual( 1L, LoadProfile.Counters().GetValueOrDefault( "signs composed on use" ) );
			Assert.AreEqual( 0L, LoadProfile.Counters().GetValueOrDefault( "signs composed ahead" ) );
		}
		finally { LoadProfile.Enable( false ); }
	}

	/// <summary>Runs <paramref name="steps"/> to their end and returns how many there were; a log written by them reaches the log there and then (a capture left open would hold it).</summary>
	private static int Drain( IEnumerable<bool> steps, List<string> logged )
	{
		var count = 0;
		var before = logged.Count;
		foreach ( var _ in steps )
		{
			count++;
			// between the steps the live game logs as ever: no capture of the steps is open
			Log.Info( $"between {count}" );
		}
		Assert.AreEqual( count, logged.Skip( before ).Count( text => text.StartsWith( "between ", StringComparison.Ordinal ) ), "every line logged between the steps is written" );
		return count;
	}

	[TestMethod]
	public void ASignComposedAheadOverStepsIsTheSignComposedOnUseWithItsLogWrittenThen()
	{
		var entries = SignEntries();
		var onUse = GetAll( entries );
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = false;
		var logged = new List<string>();
		void Record( Logger.Level severity, string text ) => logged.Add( text );
		Logger.OnLog += Record;
		LoadProfile.Enable( true );
		try
		{
			foreach ( var entry in entries )
				Assert.IsTrue( Drain( ObjectSigns.ComposeAheadSteps( entry ), logged ) >= 3, $"{entry.ArchivePath}: the sign's read and its parts in steps" );
			CollectionAssert.AreEqual( Array.Empty<string>(), logged.Where( text => !text.StartsWith( "between ", StringComparison.Ordinal ) ).ToArray(),
				"the steps log nothing themselves: Get writes what the composition logged when it uses it" );
			Assert.IsTrue( entries.All( ObjectSigns.ComposedAhead ), "every sign is handed over composed" );
		}
		finally { Logger.OnLog -= Record; }
		try
		{
			AssertSame( entries, onUse, GetAll( entries ) );
			Assert.AreEqual( (long)entries.Count, LoadProfile.Counters().GetValueOrDefault( "signs composed ahead" ) );
			Assert.AreEqual( 0L, LoadProfile.Counters().GetValueOrDefault( "signs composed on use" ), "Get composed none itself" );
		}
		finally { LoadProfile.Enable( false ); }
	}

	[TestMethod]
	public void ASignTheLiveGameUsesWhileItIsComposedAheadOverStepsKeepsTheLiveOne()
	{
		var entry = SignEntries().First();
		var onUse = GetAll( new[] { entry } );
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = false;
		using var steps = ObjectSigns.ComposeAheadSteps( entry ).GetEnumerator();
		Assert.IsTrue( steps.MoveNext() );
		Assert.IsTrue( steps.MoveNext() );
		// the live game's frame between two steps asks for the sign: it composes it on use, with its log then
		var live = GetAll( new[] { entry } );
		AssertSame( new[] { entry }, onUse, live );
		var cached = ObjectSigns.Get( entry );
		while ( steps.MoveNext() )
		{
		}
		Assert.IsFalse( ObjectSigns.ComposedAhead( entry ), "the finished steps hand over nothing" );
		Assert.AreSame( cached, ObjectSigns.Get( entry ), "the live game's sign stands" );
	}

	[TestMethod]
	public void ASignComposedAheadOverStepsFromChangedDataOrGivenUpPartWayIsNotHandedOver()
	{
		var entry = SignEntries().First();
		var onUse = GetAll( new[] { entry } );
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = false;
		// what changes the lookups (a mod set, the interface language) between the steps leaves their sign unused
		using ( var steps = ObjectSigns.ComposeAheadSteps( entry ).GetEnumerator() )
		{
			Assert.IsTrue( steps.MoveNext() );
			FileSystem.InvalidateLookups();
			while ( steps.MoveNext() )
			{
			}
		}
		Assert.IsFalse( ObjectSigns.ComposedAhead( entry ), "a sign of changed data is not handed over" );
		// steps given up part way hold no log capture and hand over nothing
		var logged = new List<string>();
		void Record( Logger.Level severity, string text ) => logged.Add( text );
		Logger.OnLog += Record;
		try
		{
			using ( var steps = ObjectSigns.ComposeAheadSteps( entry ).GetEnumerator() )
			{
				Assert.IsTrue( steps.MoveNext() );
				Assert.IsTrue( steps.MoveNext() );
			}
			Log.Info( "after the steps" );
		}
		finally { Logger.OnLog -= Record; }
		CollectionAssert.AreEqual( new[] { "after the steps" }, logged, "nothing captured, nothing logged by the steps" );
		Assert.IsFalse( ObjectSigns.ComposedAhead( entry ) );
		LoadProfile.Enable( true );
		try
		{
			AssertSame( new[] { entry }, onUse, GetAll( new[] { entry } ) );
			Assert.AreEqual( 1L, LoadProfile.Counters().GetValueOrDefault( "signs composed on use" ), "the first use composes it" );
		}
		finally { LoadProfile.Enable( false ); }
	}

	[TestMethod]
	public void ASignThatCannotBeReadIsLeftToItsFirstUseWhichMeetsTheFailureAsBefore()
	{
		var archive = Path.Combine( root, "unreadable" );
		Directory.CreateDirectory( archive );
		File.WriteAllBytes( Path.Combine( archive, "bad.sgn" ), new byte[] { 1, 2, 3 } );
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.Name Wobbly Ride\n", "unreadable.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/unreadable", settings, new ObjectShape( new[] { "*" } ), "/model.MD2", null, null, Array.Empty<string>(),
			Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		ObjectSigns.ComposeAheadEnabled = false;
		var logged = new List<string>();
		void Record( Logger.Level severity, string text ) => logged.Add( text );
		Logger.OnLog += Record;
		try
		{
			foreach ( var _ in ObjectSigns.ComposeAheadSteps( entry ) )
			{
			}
			Assert.AreEqual( 0, logged.Count, "the steps log nothing" );
			Assert.IsFalse( ObjectSigns.ComposedAhead( entry ), "a failure is handed over to no one" );
			Assert.IsNull( ObjectSigns.Get( entry ) );
		}
		finally { Logger.OnLog -= Record; }
		Assert.AreEqual( 1, logged.Count( text => text.StartsWith( "Sign of unreadable unavailable", StringComparison.Ordinal ) ), $"the first use logs the failure once ({string.Join( " | ", logged )})" );
	}

	[TestMethod]
	public void TheSignExportIsTheSameWithSignsComposedAhead()
	{
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousEdition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var data = FileSystem.GetAbsolutePath( "/" );
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			var onUse = SignExport.Run( Path.Combine( root, "on-use" ), data );

			ObjectSigns.ForgetForTests();
			var entries = SignEntries();
			foreach ( var entry in entries )
				ObjectSigns.ComposeAhead( entry );
			Assert.IsTrue( ObjectSigns.WaitForComposeAhead( TimeSpan.FromMinutes( 5 ) ) );
			var ahead = SignExport.Run( Path.Combine( root, "ahead" ), data );

			Assert.IsTrue( onUse.Count > 150, $"the export writes the signs of the four themes ({onUse.Count} files)" );
			CollectionAssert.AreEqual( onUse.Select( file => (file.RelativePath, file.PngSha256) ).ToArray(), ahead.Select( file => (file.RelativePath, file.PngSha256) ).ToArray() );
			foreach ( var file in onUse )
				CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( root, "on-use", file.RelativePath ) ), File.ReadAllBytes( Path.Combine( root, "ahead", file.RelativePath ) ), file.RelativePath );
		}
		finally
		{
			if ( previousLanguage != null )
				GameLanguage.Current = previousLanguage;
			GameEditions.Select( previousEdition );
		}
	}
}
