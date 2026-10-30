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
/// The cross-section run builder against tp.exe's own 0x4402D0 run in the x86 oracle (docs/reverse/ORACLE.md, X42; COAST-026): every row of <see cref="CoasterRunsGolden"/> is a synthetic chain and select
/// table, and the C# port, over its own sampler's output, must write the same run table bit for bit.
/// </summary>
[TestClass]
public class CoasterRunsGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( CoasterRunsGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	private static float[] Singles( string text ) =>
		text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( item, 16 ) ) ).ToArray();

	private static string Text( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );

	/// <summary>The sam keys of a row's sections and selects (the ranges in degrees, as the original's loader reads them).</summary>
	private static Dictionary<string, string> Settings( float[] lengths, string selectText )
	{
		var values = new Dictionary<string, string>();
		for ( var index = 0; index < lengths.Length; index++ )
		{
			values[$"asCrossSections[{index}].usCSPointBlockIndex"] = "1";
			values[$"asCrossSections[{index}].usCSEdgeBlockIndex"] = "1";
			if ( lengths[index] != 0 )
			{
				values[$"asCrossSections[{index}].usBlendToCSPointBlockIndex"] = "1";
				values[$"asCrossSections[{index}].fBlendLength"] = Text( lengths[index] );
			}
		}
		var names = new[] { "fSpeedRangeMin", "fSpeedRangeMax", "fDirectionRangeMin", "fDirectionRangeMax", "fPitchRangeMin", "fPitchRangeMax", "fBankRangeMin", "fBankRangeMax" };
		var entries = selectText.Split( " ; " );
		for ( var index = 0; index < entries.Length; index++ )
		{
			var halves = entries[index].Split( " : " );
			var ranges = Singles( halves[0] );
			var numbers = halves[1].Split( ' ' ).Select( int.Parse ).ToArray();
			for ( var k = 0; k < ranges.Length; k++ )
				values[$"asCrossSectionSelects[{index}].{names[k]}"] = Text( ranges[k] );
			values[$"asCrossSectionSelects[{index}].usCrossSectionIndex"] = (numbers[0] + 1).ToString();
			if ( numbers[1] >= 0 )
				values[$"asCrossSectionSelects[{index}].usBlendFromCrossSectionIndex"] = (numbers[1] + 1).ToString();
			if ( numbers[2] >= 0 )
				values[$"asCrossSectionSelects[{index}].usBlendToCrossSectionIndex"] = (numbers[2] + 1).ToString();
		}
		return values;
	}

	[TestMethod]
	public void EveryRowOfTheOraclesRunBuilderIsReproducedBitForBit()
	{
		var rows = Rows();
		Assert.AreEqual( CoasterRunsGolden.RowCount, rows.Length );
		var transitionRows = 0;
		var mergedRows = 0;
		var backwardRows = 0;
		var forwardRows = 0;
		var runCount = 0;
		foreach ( var row in rows )
		{
			var sides = row.Split( " > " );
			var parts = sides[0].Split( " | " );
			Assert.AreEqual( "runs", parts[0] );
			var curviness = Singles( parts[1] )[0];
			var counts = parts[2].Split( ' ' ).Select( int.Parse ).ToArray();
			var values = Singles( parts[3] );
			var knots = new List<CoasterKnot>();
			var at = 0;
			for ( var node = 0; node < counts.Length; node++ )
			{
				for ( var j = 0; j < counts[node]; j++ )
				{
					knots.Add( new CoasterKnot( new SVector3( values[at], values[at + 1], values[at + 2] ), CoasterX87.Normalize( new SVector3( values[at + 3], values[at + 4], values[at + 5] ) ), 0f, node ) );
					at += 6;
				}
			}
			var lengths = Singles( parts[4] );
			var sections = CoasterCrossSections.FromValues( Settings( lengths, parts[5] ) );
			var sampling = HermiteCoasterCurve.Instance.Sample( knots, curviness );
			var runs = HermiteCoasterCurve.ExtrusionRuns( sections, knots, curviness, sampling );
			var outputs = sides[1].Split( " | " );
			var expectedCount = int.Parse( outputs[0] );
			var expected = outputs[1].Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			Assert.AreEqual( expectedCount, runs.Count, $"run count in {row}" );
			for ( var i = 0; i < runs.Count; i++ )
			{
				var floats = Singles( string.Join( ' ', expected.Skip( 4 * i ).Take( 3 ) ) );
				var label = $"run {i} of {runs.Count} in {row}";
				Assert.AreEqual( BitConverter.SingleToUInt32Bits( floats[0] ), BitConverter.SingleToUInt32Bits( runs[i].Key ), $"key {floats[0]:R} against {runs[i].Key:R}, {label}" );
				Assert.AreEqual( BitConverter.SingleToUInt32Bits( floats[1] ), BitConverter.SingleToUInt32Bits( runs[i].Position ), $"position {floats[1]:R} against {runs[i].Position:R}, {label}" );
				Assert.AreEqual( BitConverter.SingleToUInt32Bits( floats[2] ), BitConverter.SingleToUInt32Bits( runs[i].Weight ), $"weight {floats[2]:R} against {runs[i].Weight:R}, {label}" );
				Assert.AreEqual( int.Parse( expected[4 * i + 3] ), runs[i].Section, $"section, {label}" );
			}
			runCount += runs.Count;
			if ( sections.HasTransitions && runs.Any( run => run.Weight != 0 ) )
				transitionRows++;
			if ( runs.Any( run => run.Weight > 0 && run.Weight < 1 ) )
				mergedRows++;
			if ( runs.Any( run => run.Section >= 0 && run.Section < sections.Sections.Count && sections.Sections[run.Section].BlendLength < 0 ) )
				backwardRows++;
			if ( runs.Any( run => run.Section >= 0 && run.Section < sections.Sections.Count && sections.Sections[run.Section].BlendLength > 0 ) )
				forwardRows++;
		}
		// the scenarios cover both kinds of table: with blends the runs carry weights
		Assert.IsTrue( transitionRows >= 15 && mergedRows >= 5 && backwardRows >= 5 && forwardRows >= 5, $"{transitionRows} rows with transitions, {mergedRows} merged, {backwardRows} backward, {forwardRows} forward" );
		Assert.IsTrue( runCount > 300, $"{runCount} runs" );
	}
}
