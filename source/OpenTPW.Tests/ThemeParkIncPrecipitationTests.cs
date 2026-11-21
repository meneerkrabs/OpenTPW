using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class ThemeParkIncPrecipitationTests
{
	// Captured from 0x642230 up to the lightning branch by tpi_precipitation_oracle.py.
	[DataTestMethod]
	[DataRow(47, -1, 20, 600, 47, null, null, false)]
	[DataRow(0, 13, 20, 600, 0, null, null, false)]
	[DataRow(20, 21, 20, 600, 20, null, null, false)]
	[DataRow(0, 20, 20, 600, 20, 20, 3, false)]
	[DataRow(20, 0, 20, 600, 0, 0, 0, false)]
	[DataRow(19, 0, 20, 600, 0, 0, 0, true)]
	[DataRow(47, 0, 20, 600, 27, 27, 4, false)]
	[DataRow(500, 40, 20, 600, 480, 480, 80, false)]
	[DataRow(50, 500, 20, 600, 70, 70, 11, false)]
	[DataRow(590, 630, 20, 600, 610, 600, 101, false)]
	[DataRow(610, 630, 20, 600, 630, 600, 105, false)]
	[DataRow(-30, 40, 20, 600, -10, 0, -1, false)]
	[DataRow(123, 132, 10, 120, 123, null, null, false)]
	public void DropCountAndOutputRequestsMatchTheNativeUpdate( int current, int target, int speed, int maximum,
		int next, int? rendered, int? volume, bool stop )
	{
		Assert.AreEqual( new ThemeParkIncDropStep( next, rendered, volume, stop ), ThemeParkIncPrecipitation.Step( current, target, speed, maximum ) );
	}

	[DataTestMethod]
	[DataRow(0, 50, 0, 40, 0, 20, 600, 0, 2, null)]
	[DataRow(1, 98, 0, 40, 0, 20, 600, 0, 2, null)]
	[DataRow(2, 13, 0, 40, 0, 20, 600, 408, 0, 1075838976u)]
	[DataRow(2, 40, 0, 40, 0, 20, 600, 0, 0, 1075838976u)]
	[DataRow(3, 14, 0, 40, 0, 20, 600, 390, 0, 1083179008u)]
	[DataRow(3, -1, 0, 40, 0, 20, 600, 600, 0, 1083179008u)]
	[DataRow(4, 9, 0, 40, 0, 20, 601, 330, 1, 1061997773u)]
	[DataRow(4, 5, 0, 40, 5, 5, 600, 0, 1, 1061997773u)]
	public void DropTargetAndPresentationMatchTheNativeSpellStart( int kind, int quality, int rainLow, int rainHigh, int snowLow, int snowHigh, int maximum,
		int target, int mode, uint? speedBits )
	{
		Assert.AreEqual( target, ThemeParkIncPrecipitation.Target( new WeatherSpell( 3, kind, quality, 3 ), (rainLow, rainHigh), (snowLow, snowHigh), maximum ) );
		var presentation = ThemeParkIncPrecipitation.Presentation( kind );
		Assert.AreEqual( mode, presentation.Mode );
		Assert.AreEqual( speedBits, presentation.Speed is { } speed ? System.BitConverter.SingleToUInt32Bits( speed ) : (uint?)null );
	}

	[DataTestMethod]
	[DataRow(90, 21, 89u, 1124466688u, 1061830000u)]
	[DataRow(350, 21, 89u, 1107820544u, 1061830000u)]
	[DataRow(10, 120, 0u, 1108082688u, 0u)]
	[DataRow(0, -20, 0u, 1110704128u, 1065353216u)]
	[DataRow(359, 0, 46u, 0u, 1065353216u)]
	[DataRow(90, 50, 45u, 1119092736u, 1056964608u)]
	[DataRow(0, 100, 4294967295u, 1106247680u, 0u)]
	public void SpellWindMatchesTheNativeRendererHandoff( int angle, int quality, uint word, uint angleBits, uint forceBits )
	{
		Assert.AreEqual( angleBits, System.BitConverter.SingleToUInt32Bits( ThemeParkIncPrecipitation.NextWindDirection( angle, word ) ) );
		Assert.AreEqual( forceBits, System.BitConverter.SingleToUInt32Bits( ThemeParkIncPrecipitation.WindForce( quality ) ) );
	}

	[DataTestMethod]
	[DataRow(0, 0, true, 0u, 3212836864u, 0u)]
	[DataRow(0, 0, false, 0u, 3212836864u, 0u)]
	[DataRow(0, 100, true, 1060439283u, 3207922931u, 0u)]
	[DataRow(0, 100, false, 1060439283u, 3207922931u, 0u)]
	[DataRow(90, 50, true, 2997349178u, 3211065646u, 1055193390u)]
	[DataRow(90, 50, false, 3005023355u, 3211065646u, 1055193390u)]
	[DataRow(134, 79, true, 3202120179u, 3209224426u, 1055149909u)]
	[DataRow(134, 79, false, 3202120178u, 3209224425u, 1055149908u)]
	[DataRow(210, 100, true, 3206333551u, 3207922931u, 3199534327u)]
	[DataRow(210, 100, false, 3206333552u, 3207922931u, 3199534327u)]
	[DataRow(359, 25, true, 1048072596u, 3212335939u, 3146429183u)]
	[DataRow(359, 25, false, 1048072596u, 3212335938u, 3146429156u)]
	public void FallVectorMatchesTheNativeDirectionWords( int angle, int forcePercent, bool singlePrecision, uint x, uint y, uint z )
	{
		var vector = ThemeParkIncPrecipitation.FallVector( angle, forcePercent / 100f, singlePrecision );
		Assert.AreEqual( (x, y, z), (System.BitConverter.SingleToUInt32Bits( vector.X ),
			System.BitConverter.SingleToUInt32Bits( vector.Y ), System.BitConverter.SingleToUInt32Bits( vector.Z )) );
	}

	[DataTestMethod]
	[DataRow(1, 0, 0, 0, 500, 0.0f, -1.0f, 0.0f, 1073741824u, 3230138368u, 1073741824u)]
	[DataRow(1, 10, 20, 30, 250, 0.5f, -0.5f, 0.5f, 3202351104u, 3223584768u, 3202351104u)]
	[DataRow(2, 0, 0, 0, 500, 0.0f, -1.0f, 0.0f, 1073741824u, 3222903194u, 1073741824u)]
	public void ScrollAndParallaxMatchTheNativeDrawHandoff( int mode, int eyeX, int eyeY, int eyeZ, int elapsed,
		float dx, float dy, float dz, uint x, uint y, uint z )
	{
		var camera = WeatherCamera.Looking( new( eyeX, eyeY, eyeZ ) );
		var speed = ThemeParkIncPrecipitation.Presentation( mode == 1 ? 3 : 4 ).Speed!.Value;
		var (_, offset) = WeatherVisualState.StepTpiScroll( default, new( dx, dy, dz ), speed, elapsed * 0.001f, camera );
		Assert.AreEqual( (x, y, z), (System.BitConverter.SingleToUInt32Bits( offset.X ),
			System.BitConverter.SingleToUInt32Bits( offset.Y ), System.BitConverter.SingleToUInt32Bits( offset.Z )) );
	}
}
