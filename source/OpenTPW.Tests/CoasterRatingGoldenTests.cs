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
/// (docs/reverse/ORACLE.md, X46; COAST-032, COAST-014): every row of <see cref="CoasterRatingGolden"/> is a synthetic type record and track, and the C# port must reproduce the oracle's bits.
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

	[TestMethod]
	public void EveryRowOfTheOraclesTrainPlacementIsReproducedBitForBit()
	{
		var rows = 0;
		var multi = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "place | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var definition = DefinitionOf( Singles( parts[1] ), false, false );
			var counters = parts[2].Split( ' ' ).Select( int.Parse ).ToArray();
			var (trains, release, reference) = (counters[0], counters[1], counters[2]);
			var scalars = Singles( parts[3] );
			var (spacing, cap, end, lead) = (scalars[0], scalars[1], scalars[2], scalars[3]);
			var cars = parts[4].Split( " ; " ).Select( Singles ).ToArray();
			var samples = parts[5].Split( " ; " ).Select( item =>
			{
				var fields = item.Split( ' ' );
				var values = fields.Take( 6 ).Select( field => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( field, 16 ) ) ).ToArray();
				return new CoasterSample { Position = new SVector3( values[0], values[1], values[2] ), Yaw = values[3], Pitch = values[4], Bank = values[5], Flags = (CoasterSampleFlags)uint.Parse( fields[6] ), Section = int.Parse( fields[7] ) };
			} ).ToArray();
			CoasterRating.RatePassAt( definition, samples, spacing, cap, 1, end );
			var speeds = samples.Select( sample => sample.Speed ).Append( samples[^1].Speed ).ToArray();
			var lasts = CoasterTrainPlacement.ReleaseSpacing( speeds, samples.Length, spacing, end, CoasterTrainPlacement.ReleaseSeconds( release ), lead, trains );
			var expected = sides[1].Split( " ; " );
			var fronts = cars.Select( car => car[0] ).ToArray();
			var rears = cars.Select( car => car[1] ).ToArray();
			for ( var train = 0; train < trains; train++ )
			{
				var coordinates = CoasterTrainPlacement.CarCoordinates( lasts[train], fronts, rears, spacing );
				var position = coordinates[reference];
				var (front, rear) = CoasterTrainPlacement.Extents( position, cars[0][2], fronts[0], cars[^1][2], rears[^1], spacing );
				var speed = CoasterTrainPlacement.MeanSpeed( coordinates, index => speeds[index <= samples.Length ? index : index % samples.Length] );
				var actual = string.Join( ' ', new[] { lasts[train], position, speed, front, rear }.Concat( coordinates ).Select( value => Bits( value ).ToString( "x8" ) ) );
				Assert.AreEqual( expected[train], actual, $"train {train} of row {rows}: {row[..Math.Min( row.Length, 160 )]}" );
			}
			rows++;
			if ( trains > 1 )
				multi++;
		}
		Assert.IsTrue( rows >= 100 && multi > 50, $"{rows} place rows, {multi} with several trains" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesMotionTickIsReproducedBitForBit()
	{
		var rows = 0;
		var partial = 0;
		var bumped = 0;
		var cleared = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "tick | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var settings = CoasterTests.PcSettings();
			settings["sCoasterType.uiTrainReleaseRate"] = "15000";
			var definitionValues = Singles( parts[1] );
			for ( var index = 0; index < DefinitionKeys.Length; index++ )
				settings["sCoasterType." + DefinitionKeys[index]] = Text( definitionValues[index] );
			settings["sCoasterType.fMinSpeed"] = Text( definitionValues[DefinitionKeys.Length] );
			var definition = CoasterDefinition.FromValues( settings );
			CollectionAssert.AreEqual( new[] { 7f, 0f, -7f, -14f }, definition.CarOffsets.ToArray(), "the oracle rows lay a default four-car train" );
			Assert.AreEqual( (3.5f, 3.5f), (definition.CarMetrics( 0 ).FrontExtent, definition.CarMetrics( 0 ).RearExtent) );
			var controller = Singles( parts[2] );
			var (spacing, end, cap, braking, platform) = (controller[0], controller[1], controller[2], controller[3], controller[4]);
			var spec = parts[3].Split( " ; " ).Select( item =>
			{
				var fields = item.Split( ' ' );
				var values = fields.Skip( 1 ).Select( field => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( field, 16 ) ) ).ToArray();
				return (Position: values[0], Speed: values[1], Flags: (CoasterTrainFlags)uint.Parse( fields[0] ));
			} ).ToArray();
			var steps = parts[4].Split( ' ' ).Select( int.Parse ).ToArray();
			var winch = new List<int>();
			var samples = parts[5].Split( " ; " ).Select( ( item, number ) =>
			{
				var fields = item.Split( ' ' );
				var values = fields.Take( 4 ).Select( field => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( field, 16 ) ) ).ToArray();
				var flags = fields[4] == "1" ? CoasterSampleFlags.Friction : CoasterSampleFlags.None;
				if ( fields[5] == "1" )
					winch.Add( number );
				return new CoasterSample { Position = new SVector3( values[0], values[1], values[2] ), Speed = values[3], Flags = flags, Section = number };
			} ).ToArray();
			var ride = new CoasterRide( definition, new CoasterWorld( 1 ), "Oracle" );
			ride.LoadMotionScenario( samples, spacing, end, braking, platform, cap, winch, spec );
			var expectedTicks = sides[1].Split( " / " );
			Assert.AreEqual( steps.Length, expectedTicks.Length );
			for ( var tick = 0; tick < steps.Length; tick++ )
			{
				ride.MotionTickForOracle( tick == 0 ? 0f : steps[tick] * 0.001f );
				var expectedTrains = expectedTicks[tick].Split( " ; " );
				for ( var index = 0; index < ride.Trains.Count; index++ )
				{
					var train = ride.Trains[index];
					var expected = expectedTrains[index].Split( ' ' );
					var actual = string.Join( ' ', new[] { Bits( train.Position ), Bits( train.Speed ), Bits( train.Front ), Bits( train.Rear ) }.Select( value => value.ToString( "x8" ) ) );
					Assert.AreEqual( string.Join( ' ', expected.Skip( 1 ) ), actual, $"train {index} after tick {tick} of row {rows}: {row[..Math.Min( row.Length, 140 )]}" );
					Assert.AreEqual( uint.Parse( expected[0] ), (uint)train.Flags & 0xC0u, $"flags of train {index} after tick {tick} of row {rows}" );
					if ( (train.Flags & CoasterTrainFlags.Bumped) != 0 )
						bumped++;
				}
				if ( tick > 0 && steps[tick] <= 50 )
					partial++;
			}
			if ( spec.Any( item => (item.Flags & CoasterTrainFlags.Headway) != 0 ) && ride.Trains.Any( train => (train.Flags & CoasterTrainFlags.Headway) == 0 ) )
				cleared++;
			rows++;
		}
		Assert.IsTrue( rows >= 100 && partial > 400 && bumped > 20, $"{rows} tick rows, {partial} short ticks, {bumped} bumped train states, {cleared} headway clears" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesSpeedCapIsReproducedBitForBit()
	{
		var rows = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "cap | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var fields = sides[0].Split( " | " )[1].Split( ' ' );
			var settings = CoasterTests.PcSettings();
			settings["sCoasterType.fMaxSpeedAtMinSetting"] = Text( BitConverter.UInt32BitsToSingle( Convert.ToUInt32( fields[0], 16 ) ) );
			settings["sCoasterType.fMaxSpeedAtMaxSetting"] = Text( BitConverter.UInt32BitsToSingle( Convert.ToUInt32( fields[1], 16 ) ) );
			var definition = CoasterDefinition.FromValues( settings );
			var setting = int.Parse( fields[2] );
			Assert.AreEqual( sides[1].Split( ' ' )[0], Bits( CoasterRide.SpeedCapFor( definition, setting ) ).ToString( "x8" ), $"cap of setting {setting}: {row}" );
			rows++;
		}
		Assert.IsTrue( rows >= 100, $"{rows} cap rows" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesCarOffsetsIsReproducedBitForBit()
	{
		var rows = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "offsets | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var fields = sides[0].Split( " | " )[1].Split( ' ' );
			var pairs = fields.Take( 3 ).Select( field => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( field, 16 ) ) ).ToArray();
			var offsets = CoasterDefinition.CarOffsetsFor( (pairs[0], pairs[1], pairs[2]), int.Parse( fields[3] ) );
			Assert.AreEqual( sides[1], string.Join( ' ', offsets.Select( value => Bits( value ).ToString( "x8" ) ) ), row );
			rows++;
		}
		Assert.IsTrue( rows >= 70, $"{rows} offset rows" );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesTurnTestIsReproducedBitForBit()
	{
		var rows = 0;
		var passes = 0;
		foreach ( var row in Rows().Where( row => row.StartsWith( "turn | ", StringComparison.Ordinal ) ) )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			var cells = parts[1].Split( ' ' ).Select( int.Parse ).ToArray();
			var values = Singles( parts[2] );
			var entrance = cells[7] != 0;
			// the oracle's order of limits: entrance min at min, entrance min at max, entrance max at min, entrance max at max, then the same four for the normal window
			var entranceLimits = new CoasterTrack.TurnLimits( values[2], values[3], values[4], values[5] );
			var normalLimits = new CoasterTrack.TurnLimits( values[6], values[7], values[8], values[9] );
			var angle = CoasterTrack.TurnAngle( (cells[2], cells[3]), (cells[0], cells[1]), (cells[4], cells[5]) );
			var (low, high) = CoasterTrack.AngleWindowFor( MathF.Sqrt( cells[6] ), values[0], values[1], entrance ? entranceLimits : normalLimits );
			var passed = !(angle < low) && !(angle > high);
			var expected = sides[1].Split( ' ' );
			Assert.AreEqual( expected[0], Bits( angle ).ToString( "x8" ), $"angle of row {rows}: {row}" );
			Assert.AreEqual( expected[1] == "1", passed, $"verdict of row {rows}: {row}" );
			rows++;
			if ( passed )
				passes++;
		}
		Assert.IsTrue( rows >= 1000 && passes > 200 && passes < rows - 200, $"{rows} turn rows, {passes} passing" );
	}
}
