using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The byte-exact round trip of the original-format writer: every original save of the corpus (<c>OPENTPW_TPWS_SAVES</c>: the named parks, the autosaves and the four
/// <c>restart.INTS</c>) and the shipped Jungle <c>Easymode.TPWI</c> is read into block models, written again and compared with the file's bytes, payload and container.
/// Needs the private saves; never commits their bytes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalSaveWriterCorpusTests
{
	internal static IReadOnlyList<(string Name, string Path)> CorpusFiles()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" )
			.Select( file => (Name: $"{Path.GetFileName( Path.GetDirectoryName( file ) )}/{Path.GetFileName( file )}", Path: file) )
			.OrderBy( file => file.Path, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files." );
		return files;
	}

	[TestMethod]
	public void EveryCorpusSaveWritesBackToItsBytes()
	{
		foreach ( var (name, path) in CorpusFiles() )
		{
			var original = File.ReadAllBytes( path );
			using var reader = new SaveReader( new MemoryStream( original ) );
			var payload = reader.ReadFile();
			var model = OriginalSavePayload.Read( payload );
			var written = model.Write();
			Assert.AreEqual( payload.Length, written.Length, $"{name} payload length" );
			var first = Enumerable.Range( 0, payload.Length ).FirstOrDefault( index => payload[index] != written[index], -1 );
			Assert.AreEqual( -1, first, $"{name} payload differs at {first}" );
			var container = SaveContainerWriter.Write( SaveContainerFields.Of( reader ), written );
			Assert.AreEqual( original.Length, container.Length, $"{name} container length" );
			var firstContainer = Enumerable.Range( 0, original.Length ).FirstOrDefault( index => original[index] != container[index], -1 );
			Assert.AreEqual( -1, firstContainer, $"{name} container differs at {firstContainer}" );
		}
	}

	[TestMethod]
	public void ShippedJungleEasyModeWritesBackItsPayload()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Easymode.TPWI." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The shipped Jungle Easymode.TPWI is missing." );
		using var reader = new SaveReader( path );
		var payload = reader.ReadFile();
		var written = OriginalSavePayload.Read( payload ).Write();
		var first = Enumerable.Range( 0, Math.Min( payload.Length, written.Length ) ).FirstOrDefault( index => payload[index] != written[index], -1 );
		Assert.AreEqual( (payload.Length, -1), (written.Length, first), $"Easymode payload differs at {first}" );
		var container = SaveContainerWriter.Write( SaveContainerFields.Of( reader ), written );
		CollectionAssert.AreEqual( reader.buffer, container, "The shipped container is reproduced byte for byte." );
	}
}
