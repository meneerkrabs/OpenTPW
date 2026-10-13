using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Texture-frame tracks against the original catalogue (RIDES-035): every clip whose trailer enables them
/// applies its in-range tracks while it plays, every keyed frame's art resolves in the object's texture
/// directories, and every animated slot that a material references is bound in a render part, so the frame
/// the animator selects reaches a rendered texture. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectTextureFrameOriginalDataTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out originalFileSystem ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the texture-frame corpus." );
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>
	/// Across the whole catalogue: 37 entries carry clips with enabled texture-frame tracks, 203 clips apply
	/// in-range tracks, 155 of those change the frame while playing (the rest hold key 0, e.g. sideshow
	/// sleep screens), the animator's frame always equals the winning track's direct <c>FrameAt</c> sample,
	/// and every keyed frame name resolves to a texture file of the object.
	/// </summary>
	[TestMethod]
	public void EveryCatalogTextureFrameClipPlaysAndItsKeyedArtResolves()
	{
		int entries = 0, applied = 0, changing = 0, samples = 0, resolved = 0;
		var missing = new List<string>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				var entryClips = 0;
				foreach ( var animation in entry.Animations )
				{
					ModelAnimation? clip;
					try
					{
						clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip;
					}
					catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
					{
						continue;
					}
					if ( clip?.TextureFramesEnabled != true || clip.TextureFrameTracks.Count == 0 )
						continue;
					entryClips++;
					var tracks = clip.TextureFrameTracks.Where( track => track.Slot < model.Textures.Count ).ToArray();
					if ( tracks.Length == 0 )
						continue;
					applied++;
					var animator = new ObjectAnimator( model );
					animator.Play( 0, clip, animation.Name, loop: false );
					var seen = new HashSet<int>();
					for ( var step = 0; step <= 8; step++ )
					{
						if ( step > 0 )
							animator.Advance( clip.Duration / 8.0 / animator.TicksPerSecond );
						foreach ( var track in tracks )
						{
							var frame = animator.GetTextureFrame( track.Slot );
							seen.Add( frame );
							// The winner is the last track of the clip that samples the slot non-null (hyenasm1
							// holds two tracks on slot 15: a constant 0 and the face cycle, which wins).
							var value = -1;
							foreach ( var candidate in tracks.Where( candidate => candidate.Slot == track.Slot ) )
								if ( candidate.FrameAt( animator.GetTick( 0 ) ) is { } sampled )
									value = sampled;
							if ( value < 0 || value >= model.Textures[track.Slot].FrameNames.Count )
								continue;
							Assert.AreEqual( value, frame, $"{animation.Path} slot {track.Slot} at tick {animator.GetTick( 0 )}" );
							samples++;
							var name = model.Textures[track.Slot].FrameNames[frame];
							if ( ObjectAssets.ResolveTexture( entry, name ) != null )
								resolved++;
							else
								missing.Add( $"{entry.ArchiveName}:{animation.Name} slot {track.Slot} frame {frame} {name}" );
						}
					}
					changing += seen.Count > 1 ? 1 : 0;
				}
				entries += entryClips > 0 ? 1 : 0;
			}
		}
		Assert.AreEqual( 0, missing.Count, string.Join( "; ", missing ) );
		Assert.AreEqual( (37, 203, 155), (entries, applied, changing), "pinned against the local corpus (docs/MD2-MODELS.md)" );
		Assert.IsTrue( samples > 3000 && resolved == samples, $"{resolved} of {samples} keyed frame samples resolved" );
	}

	/// <summary>
	/// Every animated slot that a mesh material references is carried by a render part (unbaked parts name
	/// their mesh's material slots; static batches retain separate model slots even for identical art), so the frame the
	/// animator picks has a bound texture to change — including the fixed gate's worm face (slot 2) and the
	/// baked static part of the lights.
	/// </summary>
	[TestMethod]
	public void EveryAnimatedSlotAMaterialUsesIsBoundInARenderPart()
	{
		int boundSlots = 0, entriesWithBoundSlots = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				var animated = new HashSet<int>();
				foreach ( var animation in entry.Animations )
				{
					ModelAnimation? clip;
					try
					{
						clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip;
					}
					catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
					{
						continue;
					}
					if ( clip?.TextureFramesEnabled == true )
						foreach ( var slot in clip.TextureFrameTracks.Where( track => track.Slot < model.Textures.Count
								&& model.Textures[track.Slot].FrameNames.Count > 1 ).Select( track => (int)track.Slot ) )
							animated.Add( slot );
				}
				if ( animated.Count == 0 )
					continue;
				var parts = ObjectRenderParts.Build( entry, model );
				var bound = false;
				foreach ( var slot in animated )
				{
					if ( !model.Meshes.Any( mesh => mesh.Materials.Any( material => material.TextureIndex == slot ) ) )
						continue;
					var part = parts.FirstOrDefault( candidate => candidate.TextureSlots != null && candidate.TextureSlots.Contains( slot ) );
					Assert.IsNotNull( part, $"{entry.ArchiveName} slot {slot} is used by a mesh but no render part carries it" );
					Assert.AreEqual( Path.GetFileNameWithoutExtension( model.Textures[slot].FrameNames[0] ),
						part.Textures[Array.IndexOf( part.TextureSlots!, slot )], $"{entry.ArchiveName} slot {slot}" );
					boundSlots++;
					bound = true;
				}
				entriesWithBoundSlots += bound ? 1 : 0;
			}
		}
		Assert.IsTrue( boundSlots > 20 && entriesWithBoundSlots > 10, $"{boundSlots} slots over {entriesWithBoundSlots} entries" );
	}

	/// <summary>
	/// The supported real assets change their bound texture while the clip plays: the Belly Bounce's alien
	/// eyes (sp_eye01..06 on slot 19 of the unbaked Alien part), Bug TV's screen (tv01..10 on slot 19), the
	/// arcade's attract screens (sf_01..10 on slot 13) and the fixed gate's worm face (fgt_fce1/2 on slot 2).
	/// </summary>
	[TestMethod]
	public void SupportedAssetsSelectFramesForBoundTextureSlots()
	{
		Assert.IsTrue( ChangesFrames( "space", "bouncy", 19, out var bouncyFrames ) );
		Assert.IsTrue( bouncyFrames.Count >= 4, "the alien's eyes cycle most of the six frames" );
		Assert.IsTrue( ChangesFrames( "fantasy", "bugstv", 19, out var tvFrames ) );
		Assert.AreEqual( 10, tvFrames.Count, "the Bug TV screen reaches all ten frames" );
		Assert.IsTrue( ChangesFrames( "space", "arcade", 13, out _ ) );
		Assert.IsTrue( ChangesFrames( "fantasy", "gates", 2, out _ ) );
	}

	[TestMethod]
	public void ThemeParkIncKeyedFramesUseItsEditionSpecificTextureDecoders()
	{
		var root = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( root ) || !Directory.Exists( root ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to Theme Park Inc Data." );
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( root! );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			GameEditions.Select( GameEdition.ThemeParkInc );
			var decoded = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var applied = 0;
			var missing = new SortedSet<string>( StringComparer.Ordinal );
			foreach ( var world in new[] { "science", "arabian" } )
				foreach ( var entry in ObjectCatalog.Load( world ).Entries )
				{
					var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
					foreach ( var animation in entry.Animations )
					{
						var clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip;
						if ( clip?.TextureFramesEnabled != true )
							continue;
						foreach ( var track in clip.TextureFrameTracks.Where( track => track.Slot < model.Textures.Count ) )
						{
							applied++;
							foreach ( var frame in track.Keys.Select( key => (int)key.Frame ).Distinct().Where( frame => frame < model.Textures[track.Slot].FrameNames.Count ) )
							{
								var resolved = ObjectAssets.ResolveTexture( entry, model.Textures[track.Slot].FrameNames[frame] );
								if ( resolved == null )
								{
									missing.Add( $"{world}/{entry.ArchiveName}:{track.Slot}:{frame}:{model.Textures[track.Slot].FrameNames[frame]}" );
									continue;
								}
								var path = resolved.Value.Path;
								Assert.IsFalse( path.HasExtension( ".wct" ), path );
								if ( decoded.Add( path ) )
								{
									var (data, width, height) = Texture.DecodeImage( path );
									Assert.IsTrue( width > 0 && height > 0 && data.Length == width * height * 4, path );
								}
							}
						}
					}
				}
			Assert.AreEqual( 230, applied );
			Assert.AreEqual( 101, decoded.Count );
			CollectionAssert.AreEqual( new[] { "science/tvsim:39:3:lt1.tga" }, missing.ToArray(), "pin the retail missing art so loader regressions fail" );
			Console.WriteLine( $"TPI texture frames: {applied} in-range tracks; {decoded.Count} unique keyed textures decoded. Missing: {string.Join( "; ", missing )}" );
		}
		finally
		{
			FileSystem = previous;
			GameEditions.Select( GameEdition.ThemeParkWorld );
		}
	}

	/// <summary>Plays every clip of the entry and collects the frames its animated slot shows; false without one.</summary>
	private static bool ChangesFrames( string theme, string archive, int slot, out HashSet<int> frames )
	{
		var entry = ObjectCatalog.Load( theme ).Entries.Single( candidate => candidate.ArchiveName == archive );
		var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		Assert.IsTrue( model.Textures[slot].FrameNames.Count > 1, $"{archive} slot {slot} is multi-frame" );
		frames = new HashSet<int>();
		foreach ( var animation in entry.Animations )
		{
			var clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip;
			if ( clip?.TextureFramesEnabled != true || !clip.TextureFrameTracks.Any( track => track.Slot == slot ) )
				continue;
			var animator = new ObjectAnimator( model );
			animator.Play( 0, clip, animation.Name, loop: true );
			for ( var step = 0; step <= 8; step++ )
			{
				if ( step > 0 )
					animator.Advance( clip.Duration / 8.0 / animator.TicksPerSecond );
				frames.Add( animator.GetTextureFrame( slot ) );
			}
		}
		return frames.Count > 0;
	}
}
