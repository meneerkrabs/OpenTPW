using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SVector3 = System.Numerics.Vector3;
using SVector4 = System.Numerics.Vector4;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:point-lights] The renderer's point lights (docs/LIGHTS.md): the original engine's falloff, the units, which lights reach a draw, the uniform layout the shaders read and the
/// guarantee that a draw without lights is the draw it always was. CPU only; the shaders' arithmetic is the same expression (checked against their text below).
/// </summary>
[TestClass]
public class PointLightTests
{
	private sealed class FixedSource( params PointLight[] lights ) : IPointLightSource
	{
		public void AppendPointLights( List<PointLight> into ) => into.AddRange( lights );
	}

	private static PointLight Light( float x, float y, float z, float radius = 10f ) => new( new SVector3( x, y, z ), SVector3.One, radius );

	private static PointLightGpu[] Select( PointLightSet set, SVector3 min, SVector3 max, SVector3 camera, out int count )
	{
		var output = new PointLightGpu[PointLightSet.MaxPerDraw];
		count = set.Select( min, max, camera, output );
		return output;
	}

	[TestMethod]
	public void TheFalloffIsTheOriginalsFullAtTheCentreAndNothingAtTheRadius()
	{
		// (r2 - d2) x (1 / r2): tp.exe 0x5741B0 with the light record's +0x48 (r2) and +0x4C (1 / r2)
		Assert.AreEqual( 1f, PointLightMath.Falloff( 0f, 100f ) );
		Assert.AreEqual( 0f, PointLightMath.Falloff( 100f * 100f, 100f ) );
		Assert.AreEqual( 0f, PointLightMath.Falloff( 100f * 100f * 4f, 100f ), "beyond the radius the vertex is skipped" );
		Assert.AreEqual( 0.75f, PointLightMath.Falloff( 50f * 50f, 100f ), 1e-6f, "half the radius away: (r2 - r2/4) / r2" );
		Assert.AreEqual( 0.75f, PointLightMath.Falloff( 5f * 5f, 10f ), 1e-6f, "the share depends on d / r only" );
	}

	[TestMethod]
	public void TheOriginalsHundredUnitRadiusIsTenCellsOfEngineUnits()
	{
		Assert.AreEqual( 100f, PointLightMath.OriginalRadiusModelUnits );
		Assert.AreEqual( 20f, PointLightMath.DefaultRadius, 1e-5f, "1 model unit = 0.2 engine units" );
		Assert.AreEqual( 10f, PointLightMath.DefaultRadius / ObjectPlacement.CellSize, 1e-5f, "a cell is 10 model units = 2 engine units" );
		Assert.AreEqual( 3f, PointLightMath.ModelUnitsToEngine( 15f ), 1e-5f );
	}

	[TestMethod]
	public void TheSumOfNoLightsIsExactlyZeroSoTheShadingIsUntouched()
	{
		var shading = new SVector3( 0.4f + 0.123456f, 0.4f, 0.4f + 0.987654f );
		var sum = PointLightMath.Sum( ReadOnlySpan<PointLightGpu>.Empty, new SVector3( 3, 4, 5 ) );
		Assert.AreEqual( SVector3.Zero, sum );
		Assert.AreEqual( shading, shading + sum, "adding the empty sum changes no bit" );
		// a light out of reach of the point adds exactly zero too
		var far = new[] { PointLightMath.ToGpu( Light( 100, 0, 0, 5 ) ) };
		Assert.AreEqual( SVector3.Zero, PointLightMath.Sum( far, SVector3.Zero ) );
	}

	[TestMethod]
	public void ALightAddsItsColourTimesTheFalloffToTheSurface()
	{
		var colour = new SVector3( 1f, 0.5f, 0.25f );
		var gpu = new[] { PointLightMath.ToGpu( new PointLight( new SVector3( 10, 20, 30 ), colour, 8f ) ) };
		Assert.AreEqual( colour, PointLightMath.Sum( gpu, new SVector3( 10, 20, 30 ) ), "full colour at the centre" );
		var half = PointLightMath.Sum( gpu, new SVector3( 14, 20, 30 ) );
		Assert.AreEqual( colour.X * 0.75f, half.X, 1e-6f, "4 away of 8: 0.75" );
		var two = new[] { gpu[0], gpu[0] };
		Assert.AreEqual( colour * 2f, PointLightMath.Sum( two, new SVector3( 10, 20, 30 ) ), "lights add" );
	}

	[TestMethod]
	public void TheGpuLightHoldsTheSquaredRadiusAndItsInverse()
	{
		var gpu = PointLightMath.ToGpu( new PointLight( new SVector3( 1, 2, 3 ), new SVector3( 0.1f, 0.2f, 0.3f ), 4f ) );
		Assert.AreEqual( new SVector4( 1, 2, 3, 16f ), gpu.PositionRadiusSquared );
		Assert.AreEqual( new SVector4( 0.1f, 0.2f, 0.3f, 1f / 16f ), gpu.ColourInverseRadiusSquared );
	}

	[TestMethod]
	public void TheUniformLayoutIsTheBlockThenTheLightsAndZeroLightsUploadTheBlockAlone()
	{
		Assert.AreEqual( 272, Unsafe.SizeOf<ObjectUniformBuffer>(), "the block the shaders have always read" );
		Assert.AreEqual( 32, Unsafe.SizeOf<PointLightGpu>(), "two vec4 per light" );
		Assert.AreEqual( 272, (int)Marshal.OffsetOf<LitObjectUniformBuffer>( nameof( LitObjectUniformBuffer.Lights ) ), "std140: the vec4 array after the last member (a float at 268) starts at 272" );
		Assert.AreEqual( 272 + 16 * 32, Unsafe.SizeOf<LitObjectUniformBuffer>() );
		Assert.AreEqual( (uint)Unsafe.SizeOf<ObjectUniformBuffer>(), LitObjectUniformBuffer.UploadSize( 0 ), "no lights: the same bytes as before" );
		Assert.AreEqual( 272u + 3u * 32u, LitObjectUniformBuffer.UploadSize( 3 ) );
		Assert.IsTrue( LitObjectUniformBuffer.UploadSize( PointLightSet.MaxPerDraw ) <= 16 * 128, "inside the 2048-byte scratch buffer every material owns" );
		// the light count sits in the padding after the light colour (offset 220), where a draw without lights has always had a zero
		Assert.AreEqual( 220, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer._padding1 ) ) );
		Assert.AreEqual( 0f, new ObjectUniformBuffer().LightCount );
		var block = new ObjectUniformBuffer { LightCount = 5 };
		Assert.AreEqual( 5f, block._padding1 );
	}

	[TestMethod]
	public void TheBlockOfALitDrawIsByteForByteTheBlockOfAPlainOneExceptTheCount()
	{
		var plain = new ObjectUniformBuffer { g_mModel = Matrix4x4.CreateTranslation( 1, 2, 3 ), g_vLightPos = new SVector3( 4, 5, 6 ), g_vLightColor = SVector3.One, g_flTime = 7 }.WithFog();
		var lit = new LitObjectUniformBuffer { Base = plain };
		lit.Base.LightCount = 2;
		var before = MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in plain ) ).ToArray();
		var after = MemoryMarshal.AsBytes( new ReadOnlySpan<ObjectUniformBuffer>( in lit.Base ) ).ToArray();
		var differing = Enumerable.Range( 0, before.Length ).Where( index => before[index] != after[index] ).ToArray();
		CollectionAssert.AreEqual( new[] { 220, 221, 222, 223 }.Where( index => differing.Contains( index ) ).ToArray(), differing.ToArray(), "only the count's four bytes differ" );
	}

	[TestMethod]
	public void ALightReachesADrawOnlyWithinItsRadiusOfTheDrawsBox()
	{
		var set = new PointLightSet();
		set.Add( new FixedSource( Light( 0, 0, 0, 5 ), Light( 20, 0, 0, 5 ), Light( 26, 0, 0, 5 ) ) );
		set.Gather( null );
		Assert.AreEqual( 3, set.Count );
		// a box from x 10 to 20: the light at 0 is 10 away (radius 5), the one at 20 inside, the one at 26 six away (radius 5)
		var chosen = Select( set, new SVector3( 10, -1, -1 ), new SVector3( 20, 1, 1 ), SVector3.Zero, out var count );
		Assert.AreEqual( 1, count );
		Assert.AreEqual( 20f, chosen[0].PositionRadiusSquared.X );
		// the radius is exclusive: a light exactly its radius away adds nothing and is not sent
		set = new PointLightSet();
		set.Add( new FixedSource( Light( 25, 0, 0, 5 ) ) );
		set.Gather( null );
		Select( set, new SVector3( 10, -1, -1 ), new SVector3( 20, 1, 1 ), SVector3.Zero, out count );
		Assert.AreEqual( 0, count );
	}

	[TestMethod]
	public void ADrawGetsTheNearestLightsFirstThenTheOnesNearerTheCameraThenInSourceOrder()
	{
		var set = new PointLightSet();
		set.Add( new FixedSource( Light( 3, 0, 0, 50 ), Light( 1, 0, 0, 50 ), Light( 2, 0, 0, 50 ), Light( 0, 5, 0, 50 ), Light( 0, -5, 0, 50 ) ) );
		set.Gather( null );
		// a unit box at the origin: the lights at 0,+-5 are 4 away from its edge, 1 and 2 and 3 are 0, 1 and 2 away... the box spans -1..1 so x=1 is inside (0), x=2 is 1 away, x=3 is 2 away
		var chosen = Select( set, new SVector3( -1, -1, -1 ), new SVector3( 1, 1, 1 ), new SVector3( 0, 100, 0 ), out var count );
		Assert.AreEqual( 5, count );
		CollectionAssert.AreEqual( new[] { 1f, 2f, 3f, 0f, 0f }, chosen.Take( count ).Select( light => light.PositionRadiusSquared.X ).ToArray() );
		// the two 4-away lights tie on the box; the camera at +y picks the one at y = 5 first
		Assert.AreEqual( 5f, chosen[3].PositionRadiusSquared.Y );
		Assert.AreEqual( -5f, chosen[4].PositionRadiusSquared.Y );
	}

	[TestMethod]
	public void MoreLightsThanFitKeepTheNearestSixteenWhateverTheOrder()
	{
		var lights = Enumerable.Range( 0, 40 ).Select( index => Light( index, 0, 0, 100 ) ).ToArray();
		var forward = new PointLightSet();
		forward.Add( new FixedSource( lights ) );
		forward.Gather( null );
		var backward = new PointLightSet();
		backward.Add( new FixedSource( lights.Reverse().ToArray() ) );
		backward.Gather( null );
		var box = ( Min: new SVector3( -0.5f ), Max: new SVector3( 0.5f ) );
		var first = Select( forward, box.Min, box.Max, new SVector3( 0, 50, 0 ), out var count );
		var second = Select( backward, box.Min, box.Max, new SVector3( 0, 50, 0 ), out var secondCount );
		Assert.AreEqual( PointLightSet.MaxPerDraw, count );
		Assert.AreEqual( count, secondCount );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 16 ).Select( index => (float)index ).ToArray(), first.Select( light => light.PositionRadiusSquared.X ).ToArray(), "the nearest sixteen, nearest first" );
		CollectionAssert.AreEqual( first.Select( light => light.PositionRadiusSquared.X ).ToArray(), second.Select( light => light.PositionRadiusSquared.X ).ToArray(), "the registration order does not change the choice when distances differ" );
	}

	[TestMethod]
	public void LightsOutsideTheViewAreDroppedWhenTheFrameIsGathered()
	{
		var view = Matrix4x4.CreateLookAt( new SVector3( 0, -10, 0 ), SVector3.Zero, SVector3.UnitZ );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, 1f, 0.1f, 100f );
		var set = new PointLightSet();
		set.Add( new FixedSource( Light( 0, 0, 0, 2 ), Light( 500, 0, 0, 2 ), Light( 0, -500, 0, 2 ), Light( 14, 0, 0, 12 ) ) );
		set.Gather( view * projection );
		var kept = set.Lights.ToArray().Select( light => light.Position.X + "," + light.Position.Y ).ToArray();
		CollectionAssert.Contains( kept, "0,0", "a light in front of the camera stays" );
		CollectionAssert.DoesNotContain( kept, "500,0", "a light far to the side is outside the view even with its radius" );
		CollectionAssert.DoesNotContain( kept, "0,-500", "a light behind the camera is outside the view" );
		set.Gather( null );
		Assert.AreEqual( 4, set.Count, "without a view nothing is dropped" );
	}

	[TestMethod]
	public void UnusableLightsAndRemovedSourcesLeaveNothingBehind()
	{
		var source = new FixedSource( Light( 0, 0, 0, 0 ), Light( 0, 0, 0, -1 ), new PointLight( SVector3.Zero, SVector3.Zero, 5 ), Light( float.NaN, 0, 0, 5 ), Light( 0, 0, 0, float.PositiveInfinity ), Light( 1, 1, 1, 5 ) );
		var set = new PointLightSet();
		set.Add( source );
		set.Add( source );
		Assert.AreEqual( 1, set.SourceCount, "a source registers once" );
		set.Gather( null );
		Assert.AreEqual( 1, set.Count, "only the light with a position, a colour and a positive radius is kept" );
		set.Remove( source );
		set.Gather( null );
		Assert.AreEqual( 0, set.Count );
		Assert.AreEqual( 0, set.SourceCount );
	}

	[TestMethod]
	public void AWorldBoxIsTheBoxOfTheTransformedCorners()
	{
		PointLightSet.TransformBox( new SVector3( 0, 0, 0 ), new SVector3( 2, 4, 6 ), Matrix4x4.CreateScale( 0.5f ) * Matrix4x4.CreateTranslation( 10, 20, 30 ), out var min, out var max );
		Assert.AreEqual( new SVector3( 10, 20, 30 ), min );
		Assert.AreEqual( new SVector3( 11, 22, 33 ), max );
		Assert.AreEqual( 0f, PointLightSet.BoxDistanceSquared( min, max, new SVector3( 10.5f, 21, 31 ) ) );
		Assert.AreEqual( 9f, PointLightSet.BoxDistanceSquared( min, max, new SVector3( 7, 21, 31 ) ) );
	}

	// ---- the shaders ----

	private static string ShaderText( string shader )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return File.ReadAllText( Path.Combine( directory!.FullName, "content", "shaders", shader ) ).Replace( "\r\n", "\n" );
	}

	private static string FunctionOf( string text )
	{
		var start = text.IndexOf( "vec3 pointLights( vec3 positionWs )", StringComparison.Ordinal );
		Assert.IsTrue( start >= 0, "the shader defines pointLights()" );
		var end = text.IndexOf( "\n  }\n", start, StringComparison.Ordinal );
		if ( end < 0 )
			end = text.IndexOf( "\n    }\n", start, StringComparison.Ordinal );
		Assert.IsTrue( end > start );
		// indentation differs between the shader files; the statements do not
		return string.Join( "\n", text[start..end].Split( '\n' ).Select( line => line.Trim() ) );
	}

	[TestMethod]
	public void TheThreeLitShadersShareOnePointLightFunctionWhichIsTheCpuFalloff()
	{
		var texts = new[] { "test.shader", "pool-deck.shader", "pool-basin.shader" }.Select( ShaderText ).ToArray();
		var functions = texts.Select( FunctionOf ).ToArray();
		Assert.AreEqual( functions[0], functions[1] );
		Assert.AreEqual( functions[0], functions[2] );
		// the arithmetic PointLightMath.Sum repeats: (r2 - d2) x (1 / r2), clamped at 0, times the colour, summed in the lights' order
		StringAssert.Contains( functions[0], "float share = ( a.w - dot( d, d ) ) * b.w;" );
		StringAssert.Contains( functions[0], "sum += b.xyz * max( share, 0.0 );" );
		StringAssert.Contains( functions[0], "vec4 a = g_oUbo.g_vPointLight[i * 2];" );
		StringAssert.Contains( functions[0], "vec4 b = g_oUbo.g_vPointLight[i * 2 + 1];" );
		foreach ( var text in texts )
		{
			StringAssert.Contains( text, "float g_flLightCount;" );
			StringAssert.Contains( text, "vec4 g_vPointLight[32];" );
			StringAssert.Contains( text, "if ( g_oUbo.g_flLightCount > 0.5 )" );
		}
	}

	[TestMethod]
	public void OnlyTheLitShadersDeclareTheLightsAndMaterialsKnowWhich()
	{
		var lit = new[] { "test.shader", "pool-deck.shader", "pool-basin.shader" };
		foreach ( var shader in new[] { "3d.shader", "sprite.shader", "particle.shader", "unlit.shader", "water.shader", "pool-water.shader", "weather.shader", "advisor.shader" } )
			Assert.IsFalse( ShaderText( shader ).Contains( "g_vPointLight" ), $"{shader} does not read the lights, so a draw of it must never carry them" );
		foreach ( var shader in lit )
		{
			var text = ShaderText( shader );
			// the lights follow the block's last member, at byte 272 (std140: a vec4 array aligns to 16 after the float at 268 or the vec3 at 256)
			var last = text.IndexOf( shader == "test.shader" ? "vec3 g_vFogColor;" : "float g_flGlow;", StringComparison.Ordinal );
			Assert.IsTrue( last >= 0 && text.IndexOf( "g_vPointLight[32]", last, StringComparison.Ordinal ) > last, shader );
		}
		var method = typeof( Material ).GetMethod( "ShaderReadsPointLights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static )!;
		foreach ( var shader in lit.Concat( new[] { "sprite.shader", "pool-water.shader", "3d.shader" } ) )
			Assert.AreEqual( lit.Contains( shader ), (bool)method.Invoke( null, new object[] { "content/shaders/" + shader } )!, shader );
	}

	[TestMethod]
	public void ThePoolWaterKeepsItsWavePhaseInTheSlotThatCountsLights()
	{
		// pool-water.shader uses the padding after the light colour for the wave's phase, so it is not a lit shader; the deck and the basin never carry a phase
		StringAssert.Contains( ShaderText( "pool-water.shader" ), "float g_flWavePhase;" );
		Assert.IsFalse( ShaderText( "pool-water.shader" ).Contains( "g_flLightCount" ) );
	}
}
