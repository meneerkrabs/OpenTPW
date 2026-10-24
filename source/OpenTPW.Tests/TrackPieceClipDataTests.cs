using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The shipped piece clips on the original PC data (inconclusive without OPENTPW_GAME_PATH): the renderer draws texture-coordinate tracks only (TRACK-018), because the one
/// texture-frame clip family of the kart and water pieces, the Crypt Karts' bridges, has nothing to show.
/// </summary>
[TestClass]
public class TrackPieceClipDataTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	[TestMethod]
	public void EveryTextureFrameTrackOfAPieceClipKeysFrameZeroOfAOneFrameSlot()
	{
		var withFrames = 0;
		foreach ( var entry in ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.Where( item => !item.IsBonus && item.Category == ObjectCategory.Ride && item.Settings.GetInt( "Bumper.WhichTrackType" ) is 1 or 2 ) )
		{
			var family = entry.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 ? TrackFamily.Kart : TrackFamily.Water;
			var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, family );
			foreach ( var piece in kit.Entries.Where( item => item.Index != 0 ) )
			{
				if ( kit.GetAnimation( piece.Index, TrackPieceKit.WaterAnimation ) is not { } water || kit.GetModel( piece.Index ) is not { } model )
					continue;
				foreach ( var track in water.Clip.TextureFrameTracks )
				{
					withFrames++;
					Assert.IsTrue( water.Clip.Tracks.Count == 0, $"{entry.DisplayName} {water.Name}: the clip moves no node" );
					Assert.AreEqual( 1, model.Textures[track.Slot].FrameNames.Count, $"{entry.DisplayName} {water.Name}: the slot has one frame" );
					Assert.IsTrue( track.Keys.All( key => key.Frame == 0 ), $"{entry.DisplayName} {water.Name}: every key names frame 0" );
				}
			}
		}
		Assert.IsTrue( withFrames >= 10, $"the Crypt Karts' four bridge families (3 + 3 + 3 + 1 pieces) carry the tracks, found {withFrames}" );
	}

	[TestMethod]
	public void NoMemberOfTheCryptKartsArchiveAnimatesTheFlameSlotOfTheBridge()
	{
		var crypt = ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.First( item => !item.IsBonus && item.Category == ObjectCategory.Ride && item.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 && item.DisplayName.StartsWith( "Crypt", StringComparison.Ordinal ) );
		var bridge = ObjectAssets.LoadModel( crypt.FileSystem, crypt.ArchivePath + "/gk_trckH.MD2" );
		var flame = Enumerable.Range( 0, bridge.Textures.Count ).Single( slot => bridge.Textures[slot].FrameNames.Count > 1 );
		Assert.AreEqual( 7, flame, "the flame frames fire0.tga..flame2.tga are slot 7 of the bridge model" );
		foreach ( var file in crypt.FileSystem.GetFiles( crypt.ArchivePath ).Where( path => path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var model = ObjectAssets.LoadModel( crypt.FileSystem, file );
			if ( model.Kind != ModelFileKind.Animation || Path.GetFileName( file ).StartsWith( "Pgokarts", StringComparison.OrdinalIgnoreCase ) )
				continue;
			Assert.IsFalse( model.Clip!.TextureFrameTracks.Any( track => track.Slot == flame ), $"{Path.GetFileName( file )} animates the flame slot" );
		}
	}
}
