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
