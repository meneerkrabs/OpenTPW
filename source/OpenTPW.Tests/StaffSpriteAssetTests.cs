using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The staff sprite banks of the original <c>esprites.wad</c> (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class StaffSpriteAssetTests
{
	private BaseFileSystem previousFileSystem = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original staff sprites." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	// Other tests read the global FileSystem, so the original game data must not outlive the test.
	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem;
	}

	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "fantasy" )]
	[DataRow( "hallow" )]
	[DataRow( "space" )]
	public void EveryRolesBanksLoadFromTheWadWithTheStaffSlots( string level )
	{
		var set = StaffSpriteSet.Load( level );
		foreach ( StaffType type in Enum.GetValues<StaffType>() )
		{
			var expected = StaffSpriteRules.Banks( type, level )!;
			var first = set.AtlasFor( type, 0 );
			Assert.IsTrue( first >= 0, $"{type} in {level} has banks" );
			var loaded = Enumerable.Range( 0, expected.Banks.Count ).Select( variant => set.Atlases[set.AtlasFor( type, variant )] ).ToArray();
			CollectionAssert.AreEqual( expected.Banks.ToArray(), loaded.Select( atlas => atlas.Name ).ToArray(), $"{type} in {level}: the variant order is the file order" );
			foreach ( var atlas in loaded )
			{
				var animations = atlas.Animations.Animations;
				Assert.IsFalse( animations[StaffSpriteRules.Dangle.Slot].IsEmpty, $"{atlas.Name}: slot 6 (dangling)" );
				Assert.IsTrue( animations[StaffSpriteRules.Dangle.Slot].FramesPerDirection >= 4 && animations[6].Directions == 5, $"{atlas.Name}: 4 frames x 5 directions" );
				// the hire and carry tools draw script 0x15 on this slot in direction 4 (0x46C730, 0x46C480); the FPC banks hold it as 2 x 2 placeholders, the TPC (what tp.exe draws) has the art
				for ( var step = 0; step < 4; step++ )
				{
					var frame = atlas.Frames[atlas.GetFrame( StaffSpriteRules.Dangle.Slot, StaffRenderer.DangleDirection, step )];
					Assert.IsTrue( frame.Width > 16 && frame.Height > 16, $"{atlas.Name}: dangling frame {step} has art ({frame.Width} x {frame.Height})" );
				}
				Assert.IsFalse( animations[StaffSpriteRules.Stand.Slot].IsEmpty || animations[StaffSpriteRules.Walk.Slot].IsEmpty, $"{atlas.Name}: stand and walk" );
				Assert.AreEqual( 8, animations[StaffSpriteRules.Walk.Slot].FramesPerDirection, $"{atlas.Name}: the 8-frame walk" );
				Assert.IsTrue( atlas.Pixels.Where( ( value, index ) => index % 4 == 3 ).Any( value => value == 255 ), $"{atlas.Name} decodes to pixels" );
				// the Jungle SPR_EX walk slot lists 8 frames x 5 directions from frame 179 of a 210-frame bank: the atlas clamps the last ones
				// Only the first 16 entries are animations (the last four hold bytes such as 01 05 02 28, SpriteAnimationFile.ExtraSlotBytes).
				Assert.AreEqual( SpriteAnimationFile.SlotCount, animations.Count );
				var used = Enumerable.Range( 0, animations.Count ).Where( slot => !animations[slot].IsEmpty )
					.All( slot => animations[slot].FirstFrame < atlas.Frames.Length );
				Assert.IsTrue( used, $"{atlas.Name}: every slot starts inside the bank" );
			}
		}
	}

	[TestMethod]
	public void TheHandymansBankHasTheScriptSlots()
	{
		var handyman = StaffSpriteSet.Load( "jungle" );
		var animations = handyman.Atlases[handyman.AtlasFor( StaffType.Handyman, 0 )].Animations.Animations;
		foreach ( var slot in new[] { 0, 1, 4, 5, 6 } )
			Assert.IsFalse( animations[slot].IsEmpty, $"handyman slot {slot}" );
		Assert.AreEqual( 4, animations[4].FramesPerDirection, "sweeping: 4 frames" );
		Assert.AreEqual( 4, animations[5].FramesPerDirection, "striking: 4 frames" );
		Assert.AreEqual( 1, animations[0].FramesPerDirection, "standing: 1 frame" );
		var guard = StaffSpriteSet.Load( "jungle" );
		var guardAnimations = guard.Atlases[guard.AtlasFor( StaffType.Guard, 0 )].Animations.Animations;
		Assert.IsFalse( guardAnimations[7].IsEmpty || guardAnimations[8].IsEmpty, "the guard's catch and carry slots (7, 8)" );
	}
}
