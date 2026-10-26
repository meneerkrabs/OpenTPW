using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The terrain's texture-coordinate clips (<c>basem.MD2</c> of the Jungle terrain archive, <c>Basem1.MD2</c> of the lobby): the data they hold, the
/// way tp.exe plays them (a render callback runs the terrain instance every rendered frame in the park and in the lobby, 0x54e2b0 → 0x44e510), and the park river, waterfall
/// and lobby sea that scroll with them, on the scene clock that stands still while the park is paused.
/// </summary>
[TestClass]
public class TerrainUvAnimationTests
{
	private static readonly string[] Themes = { "fantasy", "hallow", "jungle", "space" };

	private static string Data() => Md2CorpusTests.OriginalDataPath();

	private static (ModelFile Base, ModelFile Clip) Load( string archive, string baseName, string clipName )
	{
		using var wad = new WadArchive( Path.Combine( Data(), archive ) );
		var members = Md2CorpusTests.Md2Members( wad.Root, "" ).ToDictionary( entry => entry.Name.ToLowerInvariant(), entry => entry.File );
		var geometry = new ModelFile( new MemoryStream( members.Single( entry => entry.Key.EndsWith( baseName, StringComparison.Ordinal ) ).Value.GetData() ) );
		var clip = new ModelFile( new MemoryStream( members.Single( entry => entry.Key.EndsWith( clipName, StringComparison.Ordinal ) ).Value.GetData() ) );
		return (geometry, clip);
	}

	/// <summary>
	/// The terrain instance's channel on a synthetic clip of the basem shape (100 ticks, one unit of scroll per cycle): the first frame past the total restarts it at once
	/// (the idle restart at the head of 0x473c70), the last frame before it holds the last key, and nothing ever ends.
	/// </summary>
	[TestMethod]
	public void ASyntheticHundredTickClipLoopsWithoutEnding()
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		var clip = new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateUvAnimation( duration: 100 ) ) ).Clip!;
		var animator = new ObjectAnimator( model );
		animator.Trigger( 0, clip, "basem", animationId: 5, variant: 0, loop: true, speed: 1f );
		// Corner 1 runs from (1, 1) at tick 0 to (2, 0) at tick 100.
		Assert.AreEqual( (1f, 1f), (animator.GetTexCoords( 0 )![1].X, animator.GetTexCoords( 0 )![1].Y) );
		animator.Advance( 3.3 );
		Assert.AreEqual( 99f, animator.GetTick( 0 ), 1e-3f );
		Assert.AreEqual( 1.99f, animator.GetTexCoords( 0 )![1].X, 1e-4f );
		Assert.AreEqual( 0.01f, animator.GetTexCoords( 0 )![1].Y, 1e-4f );
		animator.Advance( 0.034 );
		Assert.IsTrue( animator.GetTick( 0 ) < 4f, $"restarted with the overshoot carried: {animator.GetTick( 0 )}" );
		Assert.IsTrue( animator.GetTexCoords( 0 )![1].X < 1.1f );
		for ( var step = 0; step < 50; step++ )
		{
			animator.Advance( 0.5 );
			Assert.IsTrue( animator.IsChannelPlaying( 0 ) );
			Assert.IsTrue( animator.GetTick( 0 ) <= 100f );
		}
	}

	/// <summary>Of the four theme terrain archives only the Jungle one has an animation member of <c>base</c>; the lobby has the numbered <c>Basem1</c>.</summary>
	[TestMethod]
	public void OnlyTheJungleTerrainAndTheLobbyHaveABaseClip()
	{
		var found = new List<string>();
		foreach ( var theme in Themes.Select( theme => $"levels/{theme}/terrain.wad" ).Append( "lobby.wad" ) )
		{
			using var wad = new WadArchive( Path.Combine( Data(), theme ) );
			var names = Md2CorpusTests.Md2Members( wad.Root, "" ).Select( entry => Path.GetFileName( entry.Name ) ).ToArray();
			var clips = ObjectAnimations.Find( "base", names );
			found.AddRange( clips.Select( clip => $"{theme}:{clip.Name.ToLowerInvariant()}:{clip.AnimationIndex}:{clip.Numbered}" ) );
		}
		CollectionAssert.AreEqual( new[] { "levels/jungle/terrain.wad:basem:5:False", "lobby.wad:basem1:5:True" }, found );
	}

	private static void Mount()
	{
		FileSystem = new BaseFileSystem( Data() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static BaseFileSystem? original;

	[TestInitialize]
	public void Setup() => original = FileSystem;

	[TestCleanup]
	public void Cleanup() => FileSystem = original!;

	/// <summary>The jungle terrain animator with a vertex array registered for each scrolled mesh; the uploads are counted.</summary>
	private static (TerrainUvAnimator Animator, OriginalPark Park, Dictionary<int, Vertex[]> Vertices, int[] Uploads) JungleAnimator()
	{
		Mount();
		var park = OriginalPark.Load( "jungle" );
		var animator = TerrainUvAnimator.Create( park.TerrainModel, FileSystem.GetFiles( park.TerrainDirectory ) )!;
		Assert.IsNotNull( animator );
		var vertices = new Dictionary<int, Vertex[]>();
		var uploads = new int[1];
		foreach ( var mesh in animator.AnimatedMeshes )
		{
			var array = park.TerrainModel.Meshes[mesh].TexCoords.Select( uv => new Vertex { TexCoords = uv } ).ToArray();
			vertices[mesh] = array;
			animator.Register( mesh, array, _ => uploads[0]++ );
		}
		return (animator, park, vertices, uploads);
	}

	/// <summary>The park terrain plays basem.MD2 on its eleven water meshes: tick 0 is the stored coordinates, 100 ticks a cycle, 30 ticks a second of scene time, one unit of U per cycle.</summary>
	[TestMethod]
	public void TheParkTerrainScrollsItsElevenWaterMeshesOnTheSceneClock()
	{
		var (animator, park, vertices, uploads) = JungleAnimator();
		Assert.AreEqual( 11, animator.AnimatedMeshes.Count );
		Assert.AreEqual( 11, animator.RegisteredCount );
		CollectionAssert.AreEqual( new[] { 27, 28, 29, 30, 35, 38, 39, 42, 171, 173, 267 }, animator.AnimatedMeshes.Select( mesh => park.TerrainModel.Meshes[mesh].NodeIndex ).OrderBy( node => node ).ToArray() );
		var first = vertices[animator.AnimatedMeshes.First()];
		var start = first.Select( vertex => (vertex.TexCoords.X, vertex.TexCoords.Y) ).ToArray();
		animator.Update( 5000, 0 );
		Assert.AreEqual( 11, uploads[0], "the first frame uploads every mesh at tick 0 and sets the clock's origin" );
		animator.Update( 6000, 0 );
		Assert.AreEqual( 22, uploads[0], "every mesh uploaded again" );
		for ( var corner = 0; corner < first.Length; corner++ )
		{
			Assert.AreEqual( start[corner].X + 0.3f, first[corner].TexCoords.X, 1e-3f, "30 of 100 ticks is 0.3 of a unit of U" );
			Assert.AreEqual( start[corner].Y, first[corner].TexCoords.Y, 1e-4f );
		}
		// 3.34 s of scene time is 100.2 ticks: past the total, the clip restarts, so the coordinates are back near the stored ones.
		animator.Update( 8340, 0 );
		for ( var corner = 0; corner < first.Length; corner++ )
			Assert.AreEqual( start[corner].X, first[corner].TexCoords.X, 0.01f );
	}

	/// <summary>The clock is the scaled one: unchanged while the park is paused, twice as fast at the double game speed.</summary>
	[TestMethod]
	public void TheParkWaterStandsStillWhilePausedAndFollowsTheGameSpeed()
	{
		var (animator, _, vertices, uploads) = JungleAnimator();
		var first = vertices[animator.AnimatedMeshes.First()];
		var clock = new SceneClock();
		animator.Update( clock.Milliseconds, 0 );
		clock.Advance( 0.5 );
		animator.Update( clock.Milliseconds, 0 );
		var halfSecond = first[0].TexCoords.X;
		var uploadsAfterHalf = uploads[0];
		// the game pause freezes the clock (scale 0): the frames come, the coordinates and the uploads do not change
		for ( var frame = 0; frame < 120; frame++ )
		{
			clock.Advance( 1.0 / 60 * 0 );
			animator.Update( clock.Milliseconds, 1.0 / 60 );
		}
		Assert.AreEqual( halfSecond, first[0].TexCoords.X );
		Assert.AreEqual( uploadsAfterHalf, uploads[0] );
		// at the double speed a frame advances the scene clock twice as far: 0.5 s of real time moves 30 ticks, like 1 s at the normal speed
		clock.Advance( 0.5 * 2 );
		animator.Update( clock.Milliseconds, 0 );
		Assert.AreEqual( halfSecond + 0.3f, first[0].TexCoords.X, 2e-3f );
	}

	/// <summary>Only the Jungle terrain has a clip; another theme's terrain gets no animator.</summary>
	[TestMethod]
	public void ANonJungleTerrainHasNoAnimator()
	{
		Mount();
		var park = OriginalPark.Load( "fantasy" );
		Assert.IsNull( TerrainUvAnimator.Create( park.TerrainModel, FileSystem.GetFiles( park.TerrainDirectory ) ) );
	}

	/// <summary>basem.MD2: 100 ticks, trailer flag 0x10, eleven UV records on eleven water meshes, every corner two keys (tick 0 = the stored coordinate, tick 100 = one unit further in U).</summary>
	[TestMethod]
	public void JungleBasemScrollsElevenWaterMeshesOneUnitPerHundredTicks()
	{
		var (geometry, file) = Load( "levels/jungle/terrain.wad", "base.md2", "basem.md2" );
		Assert.AreEqual( ModelFileKind.Animation, file.Kind );
		Assert.AreEqual( 0x22u, file.HeaderFlags );
		var clip = file.Clip!;
		Assert.AreEqual( 100, clip.Duration );
		Assert.AreEqual( 0, clip.FirstTick );
		Assert.AreEqual( 0x10u, clip.TrailerFlags );
		Assert.AreEqual( 0, clip.TextureFrameTracks.Count );
		Assert.AreEqual( 0, clip.NodeList.Count );
		CollectionAssert.AreEqual( new[] { 27, 28, 29, 30, 35, 38, 39, 42, 171, 173, 267 }, clip.Tracks.Select( track => track.NodeIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { "surface16", "surface15", "surface14", "surface13", "surface22", "surface23", "surface25", "surface27", "falls02", "surface17", "Object12" },
			clip.Tracks.Select( track => geometry.Nodes[track.NodeIndex].Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { 37, 36, 32, 32, 40, 16, 32, 40, 25, 37, 16 }, clip.Tracks.Select( track => track.UvAnimation!.CornerCount ).ToArray() );
		foreach ( var track in clip.Tracks )
		{
			Assert.AreEqual( ModelAnimationTrack.UvAnimationFlag, track.Flags );
			var uv = track.UvAnimation!;
			var mesh = geometry.Meshes.Single( candidate => candidate.NodeIndex == track.NodeIndex );
			Assert.AreEqual( mesh.TexCoords.Length, uv.CornerCount, "the block animates every corner of the node's mesh" );
			Assert.IsTrue( mesh.Materials.All( material => material.Name.EndsWith( "water3", StringComparison.OrdinalIgnoreCase ) ), "only the water textures scroll" );
			for ( var corner = 0; corner < uv.CornerCount; corner++ )
			{
				var (first, count) = uv.GetTrack( corner );
				Assert.AreEqual( 2, count );
				Assert.AreEqual( 0, uv.Ticks[first] );
				Assert.AreEqual( 100, uv.Ticks[first + 1] );
				Assert.AreEqual( (mesh.TexCoords[corner].X, mesh.TexCoords[corner].Y), (uv.Keys[first].X, uv.Keys[first].Y), "tick 0 is the stored coordinate" );
				Assert.AreEqual( 1f, uv.Keys[first + 1].X - uv.Keys[first].X, 1e-5f );
				Assert.AreEqual( 0f, uv.Keys[first + 1].Y - uv.Keys[first].Y, 1e-5f );
			}
		}
		// Every slot a scrolled mesh uses already wraps (flag 0x10/0x20): the loader's 0x8030 write into the slot record (0x461f10) changes nothing for it.
		foreach ( var name in new[] { "dk_water3", "wr_water3" } )
			Assert.IsTrue( TextureSlotAddressing.Wraps( geometry.Textures.Single( texture => texture.FrameNames[0].StartsWith( name, StringComparison.OrdinalIgnoreCase ) ).Flags ), name );
	}

	/// <summary>Basem1.MD2 of the lobby: 200 ticks, the two sea meshes, one unit towards -U and -V per cycle.</summary>
	[TestMethod]
	public void LobbyBasem1ScrollsTheSeaDiagonallyOverTwoHundredTicks()
	{
		var (geometry, file) = Load( "lobby.wad", "base.md2", "basem1.md2" );
		var clip = file.Clip!;
		Assert.AreEqual( 200, clip.Duration );
		Assert.AreEqual( 0x10u, clip.TrailerFlags );
		CollectionAssert.AreEqual( new[] { 1, 2 }, clip.Tracks.Select( track => track.NodeIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Sea_Under", "Sea" }, clip.Tracks.Select( track => geometry.Nodes[track.NodeIndex].Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { 66, 61 }, clip.Tracks.Select( track => track.UvAnimation!.CornerCount ).ToArray() );
		foreach ( var track in clip.Tracks )
		{
			var uv = track.UvAnimation!;
			var mesh = geometry.Meshes.Single( candidate => candidate.NodeIndex == track.NodeIndex );
			for ( var corner = 0; corner < uv.CornerCount; corner++ )
			{
				var (first, count) = uv.GetTrack( corner );
				Assert.AreEqual( 2, count );
				Assert.AreEqual( 200, uv.Ticks[first + 1] );
				Assert.AreEqual( (mesh.TexCoords[corner].X, mesh.TexCoords[corner].Y), (uv.Keys[first].X, uv.Keys[first].Y) );
				Assert.AreEqual( -1f, uv.Keys[first + 1].X - uv.Keys[first].X, 1e-5f );
				Assert.AreEqual( -1f, uv.Keys[first + 1].Y - uv.Keys[first].Y, 1e-5f );
			}
		}
	}

	/// <summary>The terrain instance's channel: frame = (u32)(ms) · 0.03, the sampler's last key holds at the total, and the first frame past it restarts the clip with the overshoot carried.</summary>
	[TestMethod]
	public void TheSeaClipLoopsAtTwoHundredTicksOnTheMillisecondClock()
	{
		var (geometry, file) = Load( "lobby.wad", "base.md2", "basem1.md2" );
		var animator = new ObjectAnimator( geometry );
		animator.Trigger( 0, file.Clip, "Basem1", animationId: 5, variant: 0, loop: true, speed: 1f );
		var sea = geometry.Meshes.Single( mesh => mesh.Name.Equals( "Sea", StringComparison.OrdinalIgnoreCase ) );
		var index = geometry.Meshes.IndexOf( sea );
		Assert.AreEqual( 0f, animator.GetTick( 0 ) );
		CollectionAssert.AreEqual( sea.TexCoords.Select( uv => (uv.X, uv.Y) ).ToArray(), animator.GetTexCoords( index )!.Select( uv => (uv.X, uv.Y) ).ToArray(), "tick 0 is the stored coordinates" );

		animator.Advance( 3.333 );
		Assert.AreEqual( 99.99f, animator.GetTick( 0 ), 1e-3f );
		var half = animator.GetTexCoords( index )![0];
		Assert.AreEqual( sea.TexCoords[0].X - 99.99f / 200, half.X, 1e-4f );
		Assert.AreEqual( sea.TexCoords[0].Y - 99.99f / 200, half.Y, 1e-4f );

		// 6.666 s is frame 199.98: still on the last segment. 6.667 s is 200.01, past the total: the clip restarts with the 0.01 carried (whole milliseconds: none).
		animator.Advance( 6.666 - 3.333 );
		Assert.AreEqual( 199.98f, animator.GetTick( 0 ), 1e-3f );
		Assert.AreEqual( sea.TexCoords[0].X - 199.98f / 200, animator.GetTexCoords( index )![0].X, 1e-4f );
		animator.Advance( 0.001 );
		Assert.IsTrue( animator.GetTick( 0 ) < 1f, $"the loop restarted: {animator.GetTick( 0 )}" );
		Assert.AreEqual( sea.TexCoords[0].X, animator.GetTexCoords( index )![0].X, 0.01f );
		// The scrolled coordinates repeat: one unit further in U and V is the same texel of the wrapping texture.
		Assert.IsTrue( TextureSlotAddressing.Wraps( geometry.Textures.Single( texture => texture.FrameNames[0].StartsWith( "jri_lak3", StringComparison.OrdinalIgnoreCase ) ).Flags ) );
		Assert.AreEqual( 1, animator.ActiveChannels );
		Assert.IsTrue( animator.IsChannelPlaying( 0 ), "the clip never ends" );
	}

	/// <summary>
	/// The way the lobby draws the clip (<c>AnimatedModel.UpdateParts</c>): the converted parts of the sea meshes get the animator's coordinates written
	/// (<see cref="ObjectRenderParts.WriteTexCoords"/>) and nothing else moves.
	/// </summary>
	[TestMethod]
	public void TheConvertedSeaPartsScrollWithTheClip()
	{
		var (geometry, file) = Load( "lobby.wad", "base.md2", "basem1.md2" );
		var animator = new ObjectAnimator( geometry );
		animator.Trigger( 0, file.Clip, "Basem1", animationId: 5, variant: 0, loop: true, speed: 1f );
		var moved = 0;
		var seaMeshes = 0;
		for ( var mesh = 0; mesh < geometry.Meshes.Count; mesh++ )
		{
			if ( ObjectAssets.GetUnrenderableReason( geometry.Meshes[mesh] ) is "no geometry" or "no materials" )
				continue;
			foreach ( var (vertices, _, _, _) in ObjectAssets.ConvertMeshParts( geometry.Meshes[mesh] ) )
			{
				var start = vertices.Select( vertex => (vertex.Position, X: vertex.TexCoords.X, Y: vertex.TexCoords.Y) ).ToArray();
				ObjectRenderParts.WriteTexCoords( geometry.Meshes[mesh], animator.GetTexCoords( mesh ), vertices );
				CollectionAssert.AreEqual( start.Select( vertex => vertex.X ).ToArray(), vertices.Select( vertex => vertex.TexCoords.X ).ToArray(), "tick 0 writes the stored coordinates" );
			}
		}
		animator.Advance( 1.0 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
		for ( var mesh = 0; mesh < geometry.Meshes.Count; mesh++ )
		{
			if ( animator.GetTexCoords( mesh ) == null )
				continue;
			seaMeshes++;
			foreach ( var (vertices, _, _, _) in ObjectAssets.ConvertMeshParts( geometry.Meshes[mesh] ) )
			{
				var start = vertices.Select( vertex => (vertex.Position, vertex.TexCoords.X, vertex.TexCoords.Y) ).ToArray();
				ObjectRenderParts.WriteTexCoords( geometry.Meshes[mesh], animator.GetTexCoords( mesh ), vertices );
				for ( var vertex = 0; vertex < vertices.Length; vertex++ )
				{
					Assert.AreEqual( start[vertex].Position, vertices[vertex].Position, "only the coordinates scroll" );
					Assert.AreEqual( -0.15f, vertices[vertex].TexCoords.X - start[vertex].X, 1e-3f, "30 of 200 ticks: 0.15 towards -U" );
					Assert.AreEqual( -0.15f, vertices[vertex].TexCoords.Y - start[vertex].Y, 1e-3f );
					moved++;
				}
			}
		}
		Assert.AreEqual( 2, seaMeshes );
		Assert.AreEqual( 66 + 61, moved, "every corner of both sea meshes moved" );
	}
}
