using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic CPU resource and strict-output checks. No original data, graphics device or renderer clock.</summary>
[TestClass]
public class StaffSpriteDrawRecordTests
{
	internal static GuestSpriteAtlas Atlas( string name = "SPR_TEST", int directions = 5, int pixelX = 128, int pixelY = 128 )
	{
		var data = Files( name, directions );
		var atlas = new GuestSpriteAtlas( name, new SpriteBankFile( new MemoryStream( data[".TPC"] ) ),
			new SpriteAnimationFile( new MemoryStream( data[".ESP"] ) ) );
		for ( var index = 0; index < atlas.Frames.Length; index++ )
			atlas.Frames[index] = atlas.Frames[index] with { PixelsPerUnitX = (ushort)pixelX, PixelsPerUnitY = (ushort)pixelY };
		return atlas;
	}

	internal static IReadOnlyDictionary<string, byte[]> Files( string name = "SPR_TEST", int directions = 5 )
	{
		var builder = new SpriteSetBuilder( name ) { FullDetail = false };
		foreach ( var (slot, count) in new[] { (0, 1), (1, 8), (2, 8), (3, 1), (4, 8), (5, 4), (6, 4), (7, 8), (8, 8) } )
			builder.AddAnimation( slot, Enumerable.Range( 0, directions ).Select( direction => (IReadOnlyList<SpriteRgbaFrame>)
				Enumerable.Range( 0, count ).Select( frame => new SpriteRgbaFrame( 20, 40,
					Enumerable.Range( 0, 800 ).SelectMany( _ => new byte[] { (byte)(20 + slot), (byte)(30 + direction), (byte)(40 + frame), 255 } ).ToArray(), -8, -38 ) ).ToArray() ).ToArray() );
		builder.SetPerformance( 0, 0, 4, 2, 3 );
		return builder.Build().Files;
	}

	internal static StaffSpriteMetadataCatalog Catalog( GuestSpriteAtlas atlas ) => new( new[] {
		StaffSpriteBankMetadata.FromAtlas( 5, 0, 0, atlas.Name, atlas ) } );

	internal static StaffSpriteVmState State( bool clone = true )
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		state[StaffSpriteRegister.Kind] = 5; state[StaffSpriteRegister.Slot] = 1;
		state[StaffSpriteRegister.Frame] = 3; state[StaffSpriteRegister.FrameCount] = 8;
		state[StaffSpriteRegister.Heading] = 6; state[StaffSpriteRegister.Flags] = clone ? 0x50000u : 0x1200u;
		state[StaffSpriteRegister.PrimaryScale] = BitConverter.SingleToUInt32Bits( clone ? 8f : 1f );
		state.OutputValid = 1;
		return state;
	}
	internal static StaffSpriteDrawRecord Record( StaffSpriteVmState state, bool clone = true ) => new( state, 17, 2, 3, clone );

	[TestMethod]
	public void ScreenDrawIsCameraIndependentAndCopiesEveryRawRegister()
	{
		var atlas = Atlas(); var catalog = Catalog( atlas ); var state = State(); var before = JsonSerializer.Serialize( state );
		var record = Record( state ); var expected = catalog.ResolveFrame( record );
		Assert.IsTrue( expected.CanDraw ); Assert.AreEqual( 5 + 2 * 8 + 3, expected.FrameIndex ); Assert.IsTrue( expected.Mirror );
		foreach ( var yaw in new[] { 0f, MathF.PI / 2, MathF.PI, -MathF.PI / 2 } )
			for ( var index = 0; index < 20; index++ ) Assert.AreEqual( expected, catalog.ResolveFrame( record, yaw ) );
		Assert.AreEqual( before, JsonSerializer.Serialize( state ) );
		state.Registers.AsSpan().Fill( uint.MaxValue ); state.OutputValid = 0;
		Assert.AreEqual( expected, catalog.ResolveFrame( record ) ); Assert.AreEqual( 1u, record.OutputValid );
		Assert.AreEqual( 17L, record.Session ); Assert.AreEqual( 2, record.RegistrySlot ); Assert.AreEqual( 3ul, record.Generation );
	}

	[DataTestMethod]
	[DataRow( StaffSpriteRegister.Slot, 16L, StaffSpriteDiagnostic.InvalidSlot )]
	[DataRow( StaffSpriteRegister.Slot, 9L, StaffSpriteDiagnostic.MissingSlot )]
	[DataRow( StaffSpriteRegister.Frame, 8L, StaffSpriteDiagnostic.InvalidFrame )]
	[DataRow( StaffSpriteRegister.Frame, 4294967295L, StaffSpriteDiagnostic.FrameSuppressed )]
	[DataRow( StaffSpriteRegister.Heading, 8L, StaffSpriteDiagnostic.InvalidDirection )]
	[DataRow( StaffSpriteRegister.Kind, 6L, StaffSpriteDiagnostic.MetadataUnavailable )]
	[DataRow( StaffSpriteRegister.Variant, 1L, StaffSpriteDiagnostic.MetadataUnavailable )]
	public void InvalidOutputNeverFallsBackClampsOrWraps( StaffSpriteRegister register, long value, StaffSpriteDiagnostic expected )
	{
		var state = State(); state[register] = (uint)value; var before = JsonSerializer.Serialize( state );
		Assert.AreEqual( expected, Catalog( Atlas() ).ResolveFrame( Record( state ) ).Diagnostic );
		Assert.AreEqual( before, JsonSerializer.Serialize( state ) );
	}

	[TestMethod]
	public void MirroredDirectionOutsideStoredRangeIsRejected()
	{
		var state = State(); state[StaffSpriteRegister.Heading] = 4;
		Assert.AreEqual( StaffSpriteDiagnostic.InvalidDirection, Catalog( Atlas( directions: 3 ) ).ResolveFrame( Record( state ) ).Diagnostic );
	}

	[DataTestMethod]
	[DataRow( StaffSpriteRegister.Alpha, 254L )]
	[DataRow( StaffSpriteRegister.SecondaryScale, 1073741824L )]
	[DataRow( StaffSpriteRegister.PrimaryScale, 1065353216L )]
	[DataRow( StaffSpriteRegister.RotationX, 1L )]
	[DataRow( StaffSpriteRegister.RotationY, 1L )]
	[DataRow( StaffSpriteRegister.RotationZ, 1L )]
	public void NondefaultAppearanceIsExplicitlyUnsupportedWithoutChangingWords( StaffSpriteRegister register, long value )
	{
		var state = State(); state[register] = (uint)value; var record = Record( state );
		Assert.AreEqual( StaffSpriteDiagnostic.UnsupportedTransform, Catalog( Atlas() ).ResolveFrame( record ).Diagnostic );
		Assert.AreEqual( (uint)value, state[register] ); Assert.AreEqual( (uint)value, record[register] );
	}

	[TestMethod]
	public void UnsupportedMultipartAndNonfinitePoseRemainExplicit()
	{
		var atlas = Atlas(); var bank = new StaffSpriteBankMetadata( 5, 0, 0, atlas.Name, atlas.Width, atlas.Height,
			atlas.Animations.Animations, atlas.Frames, multipart: true );
		Assert.AreEqual( StaffSpriteDiagnostic.UnsupportedResource, new StaffSpriteMetadataCatalog( new[] { bank } ).ResolveFrame( Record( State() ) ).Diagnostic );
		var state = State(); state[StaffSpriteRegister.X] = BitConverter.SingleToUInt32Bits( float.NaN );
		Assert.AreEqual( StaffSpriteDiagnostic.InvalidPose, Catalog( atlas ).ResolveFrame( Record( state ) ).Diagnostic );
	}

	[TestMethod]
	public void WorldRouteKeepsAuthoredClonePoseAndReturnsToScreenOnlyThroughPartialCopy()
	{
		var catalog = Catalog( Atlas() ); var source = State( false );
		var clone = StaffSpriteCloneCopy.Create( source, 2, BitConverter.SingleToUInt32Bits( -0.31510416f ),
			BitConverter.SingleToUInt32Bits( 0.38020834f ), BitConverter.SingleToUInt32Bits( 1f ), BitConverter.SingleToUInt32Bits( 8f ) );
		var screen = Record( clone ); Assert.IsTrue( catalog.ResolveFrame( screen ).CanDraw );
		StaffSpriteVm.Tick( clone, 63 ); // Crosses the program's real SET flags.
		var world = Record( clone );
		Assert.AreEqual( StaffSpriteRoute.World, world.Route );
		Assert.AreEqual( StaffSpriteDiagnostic.WorldRouteNotImplemented, catalog.ResolveFrame( world ).Diagnostic );
		Assert.AreEqual( screen.X, world.X ); Assert.AreEqual( screen.Y, world.Y ); Assert.AreEqual( screen.Z, world.Z );
		Assert.AreEqual( screen.PrimaryScale, world.PrimaryScale );
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.IsTrue( catalog.ResolveFrame( Record( clone ) ).CanDraw );
		Assert.AreEqual( world.X, clone[StaffSpriteRegister.X] ); Assert.AreEqual( world.Y, clone[StaffSpriteRegister.Y] );
	}

	[TestMethod]
	public void PerformanceLoopDoesNotForceScreenCloneToWorldWhenSourceIsAbsent()
	{
		var state = State(); StaffSpriteVm.ApplyPerformance( state, 0, 4, 8 );
		state.CurrentPc = new StaffSpritePc( StaffSpriteProgram.Performance0Loop, 0 );
		state[StaffSpriteRegister.Scratch] = 0;
		StaffSpriteVm.Tick( state, 63 );
		Assert.AreEqual( StaffSpriteRoute.Screen, Record( state ).Route );
		Assert.IsTrue( Catalog( Atlas() ).ResolveFrame( Record( state ) ).CanDraw );
	}

	[TestMethod]
	public void PerformanceOneActuallyYieldsFrameCountAndStrictResolverRejectsIt()
	{
		var state = State( false ); StaffSpriteVm.ApplyPerformance( state, 1, 4, 8 );
		for ( var step = 1; step <= 9; step++ ) StaffSpriteVm.Tick( state, step * 63 );
		Assert.AreEqual( 8u, state[StaffSpriteRegister.Frame] ); Assert.AreEqual( 1u, state.OutputValid );
		Assert.AreEqual( StaffSpriteDiagnostic.InvalidFrame, Catalog( Atlas() ).ResolveFrame( Record( state, false ) ).Diagnostic );
	}

	[TestMethod]
	public void ZeroPixelFactorsDoNotUseThePermissiveGuestScaleFallback()
	{
		Assert.AreEqual( StaffSpriteDiagnostic.UnsupportedResource,
			Catalog( Atlas( pixelX: 0 ) ).ResolveFrame( Record( State() ) ).Diagnostic );
	}

	[TestMethod]
	public void FrozenMetadataCannotBeMutatedThroughInputCollectionsOrReplacedByNeighbour()
	{
		var atlas = Atlas(); var slots = atlas.Animations.Animations.ToArray(); var frames = atlas.Frames.ToArray();
		var performances = atlas.Animations.Performances.ToArray();
		var valid = new StaffSpriteBankMetadata( 5, 1, 0, atlas.Name, atlas.Width, atlas.Height, slots, frames, performances, 1 );
		var missing = StaffSpriteBankMetadata.Unavailable( 5, 0, "SPR_MISSING" );
		var catalog = new StaffSpriteMetadataCatalog( new[] { missing, valid } ); var layout = valid.Layout;
		slots[0] = default; frames[0] = default; performances[0] = default;
		Assert.IsTrue( valid.Available ); Assert.AreEqual( 1, valid.Slot0FrameCount ); Assert.AreEqual( layout, valid.Layout );
		Assert.IsTrue( valid.TryPerformance( 0, out var performance ) ); Assert.AreEqual( 8, performance.FrameCount );
		Assert.AreSame( missing, catalog.Select( StaffType.Handyman, 0 ) ); Assert.AreSame( valid, catalog.Select( StaffType.Handyman, 1 ) );
		Assert.IsFalse( catalog.Resolve( 5, 2 ).Available ); Assert.IsFalse( catalog.Resolve( 6, 1 ).Available );
		Assert.IsFalse( missing.Matches( atlas ) ); Assert.IsTrue( valid.Matches( atlas ) );
		atlas.Frames[0] = atlas.Frames[0] with { OriginX = 123 };
		Assert.IsFalse( valid.Matches( atlas ) ); Assert.AreEqual( -8, valid.Frames[0].OriginX );
	}

	[TestMethod]
	public void InvalidRangesAreUnavailableAndPerformanceHolesAreNotReplaced()
	{
		var atlas = Atlas(); var slots = atlas.Animations.Animations.ToArray();
		slots[4] = slots[4] with { FirstFrame = int.MaxValue };
		var bad = new StaffSpriteBankMetadata( 5, 0, 0, atlas.Name, atlas.Width, atlas.Height, slots, atlas.Frames );
		Assert.IsFalse( bad.Available ); Assert.AreEqual( 0, bad.Slot0FrameCount );
		var holes = new[] { new SpritePerformance( -1, -1, 0, 0 ), new SpritePerformance( 0, 4, 2, 3 ),
			new SpritePerformance( 5, 4, 0, 0 ), new SpritePerformance( 0, 9, 0, 0 ) };
		var bank = new StaffSpriteBankMetadata( 5, 0, 0, atlas.Name, atlas.Width, atlas.Height, atlas.Animations.Animations, atlas.Frames, holes, 3 );
		Assert.IsTrue( bank.Available ); Assert.IsFalse( bank.TryPerformance( 0, out _ ) );
		Assert.IsTrue( bank.TryPerformance( 1, out var entry ) ); Assert.AreEqual( new StaffSpritePerformanceMetadata( 0, 4, 8, 2, 3 ), entry );
		Assert.IsFalse( bank.TryPerformance( 2, out _ ) ); Assert.IsFalse( bank.TryPerformance( 3, out _ ) ); Assert.IsFalse( bank.TryPerformance( 4, out _ ) );
	}
}
