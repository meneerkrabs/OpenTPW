using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

/// <summary>
/// The shipped models whose materials use the runtime sign textures <c>sign1</c>/<c>sign2</c>, from the original data (OPENTPW_GAME_PATH): the lobby's four sign models
/// compose their theme's gate lines on their own <c>.sgn</c>, and no track piece, vehicle, coaster car, pylon or tour car carries a sign slot.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LobbySignCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign corpus." );
		originalFileSystem = previous;
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	private static IEnumerable<(string Name, WadArchiveFile File)> Members( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
				foreach ( var entry in Members( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			else if ( child is WadArchiveFile file )
				yield return ($"{prefix}{file.Name}", file);
		}
	}

	/// <summary>The stems of an archive's MD2 members whose materials use sign1/sign2.</summary>
	private static List<string> SignModels( WadArchive archive )
	{
		var models = new List<string>();
		foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
		{
			ModelFile model;
			try
			{
				model = new ModelFile( new MemoryStream( file.GetData() ) );
			}
			catch ( Exception exception ) when ( exception is NotSupportedException or InvalidDataException )
			{
				continue;
			}
			finally
			{
				file.Free();
			}
			if ( model.Meshes.SelectMany( mesh => mesh.Materials ).Any( material => ObjectSigns.IsSignSlot( material.Name ) ) )
				models.Add( Path.GetFileNameWithoutExtension( name ) );
		}
		return models;
	}

	[TestMethod]
	public void EveryLobbySignModelComposesItsThemesGateLinesOnItsOwnSign()
	{
		var data = Md2CorpusTests.OriginalDataPath();
		using var archive = new WadArchive( Path.Combine( data, "lobby.wad" ) );
		var models = SignModels( archive ).Order( StringComparer.OrdinalIgnoreCase ).ToList();
		CollectionAssert.AreEqual( new[] { "Fan_gate", "Hal_isle", "Jun_isle", "Spa_gate" }, models, StringComparer.OrdinalIgnoreCase, string.Join( ", ", models ) );
		var objectNames = LanguageTests.OriginalLanguagePublic( "English" ).LoadStrings( ObjectNames.FileName ).Entries;
		var terrain = FileSystem.GetFiles( "/lobby/terrain" ).ToList();
		var islands = LobbyDefinition.Load().Islands;
		Assert.AreEqual( 4, islands.Count );
		foreach ( var island in islands )
		{
			var model = new[] { island.IslandModel, island.GateModel }.Single( candidate => models.Contains( candidate, StringComparer.OrdinalIgnoreCase ) );
			var sign = terrain.Single( file => string.Equals( Path.GetFileName( file ), model + ".sgn", StringComparison.OrdinalIgnoreCase ) );
			var lines = LobbySigns.Lines( island, objectNames );
			Assert.AreEqual( ParkNameLines.Default( LobbySigns.GateInfoId( island.Level )!.Value, objectNames ), lines, $"{island.Level}: the gate's own row, as a new park's gate starts" );
			var images = LobbySigns.Compose( $"/lobby/terrain/{Path.GetFileName( sign )}", lines );
			Assert.IsNotNull( images, model );
			Assert.AreEqual( lines.Line1, images.Line1 );
			Assert.AreEqual( lines.Line2, images.Line2, "the lines are drawn as they are, not split again" );
			Assert.IsTrue( images.TextPixelCount > 0, $"{model}: \"{lines.DisplayName}\" draws text" );
			Assert.IsTrue( LobbySigns.TryGetCached( $"/lobby/terrain/{Path.GetFileName( sign )}", lines, out var cached ) && ReferenceEquals( cached, images ), "composed once per session" );
		}
	}

	[TestMethod]
	public void TheLobbyGatesAreTheLevelsGates()
	{
		foreach ( var level in OriginalGateSign.ThemeLevels )
		{
			var global = new SettingsFile( $"/levels/{level}/global.sam" );
			Assert.AreEqual( int.Parse( global["ParkName.GateObjectId"] ), LobbySigns.GateInfoId( level ), level );
		}
	}

	[TestMethod]
	public void NoTrackPieceVehicleCoasterCarPylonOrTourCarHasSignSlots()
	{
		var data = Md2CorpusTests.OriginalDataPath();
		var meshReference = new Regex( @"(?:SupplementalMeshes\[\d+\]\.FileName|pcMeshFilename)\s+""?([^""\s]+)", RegexOptions.IgnoreCase );
		var references = 0;
		var signModels = 0;
		foreach ( var wad in Directory.EnumerateFiles( data, "*.wad", SearchOption.AllDirectories ).Where( path => path.Contains( $"{Path.DirectorySeparatorChar}rides{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			var signs = SignModels( archive );
			signModels += signs.Count;
			// the meshes the track, coaster and tour renderers load: supplemental meshes (vehicles, tour cars), coaster car types and pylons, and the track pieces and boats
			var meshes = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var (name, file) in Members( archive.Root, "" ).Where( member => member.Name.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ).ToList() )
			{
				try
				{
					foreach ( Match match in meshReference.Matches( Encoding.Latin1.GetString( file.GetData() ) ) )
						meshes.Add( Path.GetFileNameWithoutExtension( match.Groups[1].Value ) );
				}
				finally
				{
					file.Free();
				}
			}
			references += meshes.Count;
			foreach ( var sign in signs )
			{
				Assert.IsFalse( meshes.Contains( sign ), $"{Path.GetFileName( wad )}: {sign} is drawn by a track, coaster or tour renderer" );
				Assert.IsFalse( sign.StartsWith( TrackPieceKit.WaterPrefix, StringComparison.OrdinalIgnoreCase ) || sign.StartsWith( TrackPieceKit.KartPrefix, StringComparison.OrdinalIgnoreCase )
					|| string.Equals( sign, "wr_ring", StringComparison.OrdinalIgnoreCase ), $"{Path.GetFileName( wad )}: {sign} is a track piece or boat" );
			}
		}
		Assert.IsTrue( references > 50 && signModels > 50, $"the scan found the rides' meshes ({references}) and sign models ({signModels})" );
	}
}
