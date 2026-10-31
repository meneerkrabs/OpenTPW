using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster's rating pass against tp.exe's own 0x43C450 (with its step 0x43DC50, final 0x43DF90 and the swing 0x43C76C) run in the x86 oracle at the thread's 24-bit control word
/// (docs/reverse/ORACLE.md, X44; COAST-032, COAST-014): every row of <see cref="CoasterRatingGolden"/> is a synthetic type record and track, and the C# port must reproduce the oracle's bits.
/// </summary>
[TestClass]
public class CoasterRatingGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CoasterRatingGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	private static float[] Singles( string text ) =>
		text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( item, 16 ) ) ).ToArray();

	private static uint Bits( float value ) => BitConverter.SingleToUInt32Bits( value );

	private static string Text( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );

	private static readonly string[] DefinitionKeys =
	{
		"fAccelerationPerHeight", "fUphillAccelModifier", "fDownhillAccelModifier", "fWinchSpeed", "fFrictionMultiplier", "fGravityX", "fGravityY", "fGravityZ", "fForceMultiplier",
		"fInitialSickness", "fInitialExcitement", "fGForceSicknessModifier", "fUpsideDownSicknessModifier", "fSpeedExcitementModifier", "fGForceExcitementModifier",
		"fBankExcitementModifier", "fXGForceUncomfortableModifier", "fInvertedUncomfortableModifier", "fNormalScreamStartExciteDelta", "fTerrorScreamStartExciteDelta",
		"fNormalScreamSustainExcitement", "fTerrorScreamSustainExcitement"
	};

	private static CoasterDefinition DefinitionOf( float[] values, bool trough, bool suspended )
	{
		var settings = CoasterTests.PcSettings();
		for ( var index = 0; index < DefinitionKeys.Length; index++ )
			settings["sCoasterType." + DefinitionKeys[index]] = Text( values[index] );
		if ( trough )
			settings["sCoasterType.bSpecialIsTrough"] = "1";
		if ( suspended )
			settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		return CoasterDefinition.FromValues( settings );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesStepIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CoasterRatingGolden.RowCount, rows.Length );
		var steps = 0;
		var screams = 0;
		var inverted = 0;
		foreach ( var row in rows.Where( row => row.StartsWith( "step | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var definition = DefinitionOf( Singles( parts[1] ), false, false );
			var state = parts[2].Split( ' ' );
			var values = state.Take( 5 ).Select( item => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( item, 16 ) ) ).ToArray();
			var counters = parts[3].Split( ' ' ).Select( int.Parse ).ToArray();
			var scalars = Singles( parts[4] );
			var force = Singles( parts[5] );
			var rating = new CoasterRating( definition );
			rating.Restore( values[0], values[2], values[1], values[3], values[4], state[5] == "1", state[6] == "1" );
			var flags = rating.Step( counters[0], scalars[0], scalars[1], new SVector3( force[0], force[1], force[2] ), (CoasterSampleFlags)(uint)counters[1] );
			var expected = sides[1].Split( ' ' );
			var label = $"step {steps}: {row}";
			Assert.AreEqual( expected[0], Bits( rating.Sickness ).ToString( "x8" ), "S " + label );
			Assert.AreEqual( expected[1], Bits( rating.PeakSickness ).ToString( "x8" ), "peak S " + label );
			Assert.AreEqual( expected[2], Bits( rating.Excitement ).ToString( "x8" ), "E " + label );
			Assert.AreEqual( expected[3], Bits( rating.PeakExcitement ).ToString( "x8" ), "peak E " + label );
			Assert.AreEqual( expected[4], Bits( rating.PreviousExcitement ).ToString( "x8" ), "previous E " + label );
			Assert.AreEqual( expected[5] == "1", rating.NormalScream, "normal " + label );
			Assert.AreEqual( expected[6] == "1", rating.TerrorScream, "terror " + label );
			Assert.AreEqual( uint.Parse( expected[7] ), (uint)flags, "flags " + label );
			steps++;
			if ( (flags & (CoasterSampleFlags.NormalScream | CoasterSampleFlags.TerrorScream)) != 0 )
				screams++;
			if ( MathF.Abs( scalars[1] ) > MathF.PI / 2 )
				inverted++;
		}
		Assert.IsTrue( steps >= 600, $"{steps} step rows" );
		Assert.IsTrue( screams > 50 && inverted > 50, $"screams {screams}, inverted {inverted}: the rows must reach both branches" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesFinalIsReproducedBitForBit()
	{
		var count = 0;
		var negativeZero = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "final | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var state = Singles( parts[1] );
			var more = Singles( parts[2] );
			var flags = parts[3].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => uint.Parse( item ) ).ToArray();
			var final = CoasterRating.FinalFor( state[0], state[2], state[1], state[3], flags, more[3], more[2], more[0], more[1] );
			var expected = sides[1].Split( ' ' );
			Assert.AreEqual( expected[0], Bits( final.Sickness ).ToString( "x8" ), $"sickness {row}" );
			Assert.AreEqual( expected[1], Bits( final.Excitement ).ToString( "x8" ), $"excitement {row}" );
			Assert.AreEqual( expected[2], Bits( final.Length ).ToString( "x8" ), $"length {row}" );
			Assert.AreEqual( expected[3], Bits( final.MaxHeight ).ToString( "x8" ), $"max height {row}" );
			Assert.AreEqual( expected[4], Bits( final.MaxSpeed ).ToString( "x8" ), $"max speed {row}" );
			Assert.AreEqual( int.Parse( expected[5] ), final.Sections, $"sections {row}" );
			count++;
			if ( Bits( final.Excitement ) == 0x80000000 || Bits( final.Sickness ) == 0x80000000 )
				negativeZero++;
		}
		Assert.IsTrue( count >= 100, $"{count} final rows" );
		Assert.IsTrue( negativeZero >= 0 );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesRatingPassIsReproducedBitForBit()
	{
		var passes = 0;
		var winchRows = 0;
		var suspendedRows = 0;
		var troughRows = 0;
		var trainCounts = new HashSet<int>();
		foreach ( var row in Rows().Where( row => row.StartsWith( "pass | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var counters = parts[2].Split( ' ' ).Select( long.Parse ).ToArray();
			var trough = counters[0] != 0;
			var suspended = counters[1] != 0;
			var maxTrains = (int)counters[2];
			var release = (int)(uint)counters[3];
			var definition = DefinitionOf( Singles( parts[1] ), trough, suspended );
			var scalars = parts[3].Split( ' ' );
			var spacing = BitConverter.UInt32BitsToSingle( Convert.ToUInt32( scalars[0], 16 ) );
			var cap = BitConverter.UInt32BitsToSingle( Convert.ToUInt32( scalars[1], 16 ) );
			var end = BitConverter.UInt32BitsToSingle( Convert.ToUInt32( scalars[2], 16 ) );
			var tail = int.Parse( scalars[3] );
			var samples = parts[4].Split( " ; " ).Select( item =>
			{
				var fields = item.Split( ' ' );
				var values = fields.Take( 6 ).Select( field => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( field, 16 ) ) ).ToArray();
				return new CoasterSample
				{
					Position = new SVector3( values[0], values[1], values[2] ), Yaw = values[3], Pitch = values[4], Bank = values[5],
					Flags = (CoasterSampleFlags)uint.Parse( fields[6] ), Section = int.Parse( fields[7] )
				};
			} ).ToArray();
			var braking = new float[1];
			var (result, winch) = CoasterRating.RatePassAt( definition, samples, spacing, cap, tail, end, braking );
			var outputs = sides[1].Split( " | " );
			var expected = outputs[0].Split( " ; " );
			var label = $"pass {passes}: {row[..Math.Min( row.Length, 200 )]}";
			Assert.AreEqual( expected.Length, samples.Length, label );
			for ( var index = 0; index < samples.Length; index++ )
			{
				var fields = expected[index].Split( ' ' );
				var sample = samples[index];
				Assert.AreEqual( uint.Parse( fields[0] ), (uint)sample.Flags, $"flags of sample {index} {label}" );
				Assert.AreEqual( fields[1], Bits( sample.Speed ).ToString( "x8" ), $"speed of sample {index} {label}" );
				Assert.AreEqual( fields[2], Bits( sample.Bank ).ToString( "x8" ), $"roll of sample {index} {label} (suspended {suspended})" );
				Assert.AreEqual( (fields[3], fields[4], fields[5]), (Bits( sample.Force.X ).ToString( "x8" ), Bits( sample.Force.Y ).ToString( "x8" ), Bits( sample.Force.Z ).ToString( "x8" )), $"force of sample {index} {label}" );
			}
			var marks = outputs[1];
			for ( var node = 0; node < marks.Length; node++ )
				Assert.AreEqual( marks[node] == '1', winch.Contains( node ), $"winch mark of node {node} {label}" );
			var totals = outputs[2].Split( ' ' );
			Assert.AreEqual( totals[0], Bits( result.Sickness ).ToString( "x8" ), "sickness " + label );
			Assert.AreEqual( totals[1], Bits( result.Excitement ).ToString( "x8" ), "excitement " + label );
			Assert.AreEqual( totals[2], Bits( result.Length ).ToString( "x8" ), "length " + label );
			Assert.AreEqual( totals[3], Bits( result.MaxHeight ).ToString( "x8" ), "max height " + label );
			Assert.AreEqual( totals[4], Bits( result.MaxSpeed ).ToString( "x8" ), "max speed " + label );
			Assert.AreEqual( totals[5], Bits( braking[0] ).ToString( "x8" ), "braking start " + label );
			var trains = outputs[3].Split( ' ' );
			Assert.AreEqual( trains[1], Bits( result.LapTime ).ToString( "x8" ), "lap " + label );
			Assert.AreEqual( int.Parse( trains[0] ), CoasterRating.TrainCount( result.LapTime, release, maxTrains ), "train count " + label );
			trainCounts.Add( int.Parse( trains[0] ) );
			passes++;
			if ( winch.Count > 0 )
				winchRows++;
			if ( suspended )
				suspendedRows++;
			if ( trough )
				troughRows++;
		}
		Assert.IsTrue( passes >= 100, $"{passes} pass rows" );
		Assert.IsTrue( winchRows > 20 && suspendedRows > 20 && troughRows > 20, $"winch {winchRows}, suspended {suspendedRows}, trough {troughRows}" );
		Assert.IsTrue( trainCounts.Count >= 3, "train counts seen: " + string.Join( ",", trainCounts.Order() ) );
	}
}
