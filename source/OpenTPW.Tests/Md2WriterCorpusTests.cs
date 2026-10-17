using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The MD2 writer against the original corpus (docs/MD2-MODELS.md, "Writing and authoring"). Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class Md2WriterCorpusTests
{
	/// <summary>Every 221.203 geometry member is written back byte for byte from its captured layout.</summary>
	[TestMethod]
	public void EveryOriginalGeometryMemberIsWrittenBackByteForByte()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		int geometry = 0, identical = 0, animation = 0, refused = 0, withGaps = 0, dataBlocks = 0;
		var different = new List<string>();
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				var original = file.GetData();
				file.Free();
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( original ), captureLayout: true );
				}
				catch ( NotSupportedException )
				{
					refused++;
					continue;
				}
				if ( model.Kind == ModelFileKind.Animation )
				{
					animation++;
					continue;
				}
				geometry++;
				withGaps += model.Layout!.Gaps.Count > 0 ? 1 : 0;
				dataBlocks += model.DummyAttributes.Count( attribute => attribute.Data != null );
				var written = ModelFileWriter.Write( model );
				if ( written.AsSpan().SequenceEqual( original ) )
				{
					identical++;
					continue;
				}
				var at = 0;
				while ( at < Math.Min( written.Length, original.Length ) && written[at] == original[at] )
					at++;
				different.Add( $"{Path.GetRelativePath( dataPath, wad )}/{name}: first difference at 0x{at:X} (written {written.Length}, original {original.Length})" );
			}
		}
		Console.WriteLine( $"MD2 writer roundtrip: {identical} of {geometry} geometry members byte-identical ({animation} animation members skipped, {refused} refused versions); {withGaps} need gap bytes, {dataBlocks} attribute data blocks" );
		Assert.AreEqual( 0, different.Count, string.Join( Environment.NewLine, different.Take( 20 ) ) );
		Assert.AreEqual( 838, geometry );
		Assert.AreEqual( 838, identical );
		// Every non-zero byte is accounted for by a decoded field: the gap mechanism is a last resort and nothing in the corpus needs it.
		Assert.AreEqual( 0, withGaps );
		// Type flag 0x40 records and their 16-byte blocks (28 members).
		Assert.AreEqual( 248, dataBlocks );
		Assert.AreEqual( 1278, animation );
		Assert.AreEqual( 2, refused );
	}

	/// <summary>
	/// A model read without a layout is written with a planned one: the member differs from the original in offsets and padding, but
	/// reading it back gives every field of the original (so the planned layout, which authored models use, is a valid member).
	/// </summary>
	[TestMethod]
	public void EveryOriginalGeometryMemberReadsBackEqualFromAPlannedLayout()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		int geometry = 0;
		var different = new List<string>();
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				var original = file.GetData();
				file.Free();
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( original ) );
				}
				catch ( NotSupportedException )
				{
					continue;
				}
				if ( model.Kind != ModelFileKind.Geometry )
					continue;
				geometry++;
				try
				{
					var planned = ModelFileWriter.Write( model );
					var again = new ModelFile( new MemoryStream( planned ) );
					var difference = Md2ModelComparison.Difference( model, again );
					if ( difference != null )
						different.Add( $"{Path.GetRelativePath( dataPath, wad )}/{name}: {difference}" );
				}
				catch ( Exception exception )
				{
					different.Add( $"{Path.GetRelativePath( dataPath, wad )}/{name}: {exception.Message}" );
				}
			}
		}
		Assert.AreEqual( 0, different.Count, string.Join( Environment.NewLine, different.Take( 20 ) ) );
		Assert.AreEqual( 838, geometry );
	}
}

/// <summary>Field-by-field comparison of two parsed models (everything the reader keeps except file offsets).</summary>
internal static class Md2ModelComparison
{
	public static string? Difference( ModelFile left, ModelFile right )
	{
		if ( left.SourceName != right.SourceName || left.HeaderFlags != right.HeaderFlags || left.HeaderHeight != right.HeaderHeight )
			return "header";
		if ( left.Counts != right.Counts )
			return $"counts {left.Counts} / {right.Counts}";
		if ( left.HeaderOpaque != right.HeaderOpaque )
			return $"opaque header words {left.HeaderOpaque} / {right.HeaderOpaque}";
		if ( left.BoundsMin != right.BoundsMin || left.BoundsMax != right.BoundsMax )
			return "bounds";
		if ( left.RootNodeIndex != right.RootNodeIndex )
			return "root";
		if ( left.Textures.Count != right.Textures.Count )
			return "texture count";
		for ( var index = 0; index < left.Textures.Count; index++ )
		{
			var (a, b) = (left.Textures[index], right.Textures[index]);
			if ( a.Flags != b.Flags || a.Unknown != b.Unknown || !a.FrameNames.SequenceEqual( b.FrameNames ) )
				return $"texture {index}";
		}
		for ( var index = 0; index < left.Nodes.Count; index++ )
		{
			if ( left.Nodes[index] != right.Nodes[index] )
				return $"node {index}";
		}
		if ( left.DummyAttributes.Count != right.DummyAttributes.Count )
			return "attribute count";
		for ( var index = 0; index < left.DummyAttributes.Count; index++ )
		{
			var (a, b) = (left.DummyAttributes[index], right.DummyAttributes[index]);
			if ( a.Type != b.Type || a.Value != b.Value || !a.Reserved.SequenceEqual( b.Reserved ) || a.Data != b.Data || ((a.Type & 0x40) != 0) != (a.Data != null) )
				return $"attribute {index}";
		}
		if ( left.ExtraRecords.Count != right.ExtraRecords.Count )
			return "path count";
		for ( var index = 0; index < left.Paths.Count; index++ )
		{
			var (a, b) = (left.Paths[index], right.Paths[index]);
			if ( a.Kind != b.Kind || a.Keys != b.Keys || a.Reserved != b.Reserved || !a.Points.SequenceEqual( b.Points ) )
				return $"path {index}";
		}
		if ( (left.Heightfield == null) != (right.Heightfield == null) )
			return "heightfield presence";
		if ( left.Heightfield is { } terrain )
		{
			var other = right.Heightfield!;
			for ( var z = 0; z < terrain.CellCountZ; z++ )
				for ( var x = 0; x < terrain.CellCountX; x++ )
					if ( terrain.GetCellWord( x, z ) != other.GetCellWord( x, z ) || terrain.GetCornerHeight( x, z ) != other.GetCornerHeight( x, z ) )
						return $"heightfield cell {x},{z}";
			if ( terrain.CellSizeX != other.CellSizeX || terrain.CellSizeZ != other.CellSizeZ || terrain.StoredLowerHeight != other.StoredLowerHeight
				|| terrain.StoredUpperHeight != other.StoredUpperHeight || !terrain.OpaqueHeaderWords.SequenceEqual( other.OpaqueHeaderWords ) )
				return "heightfield header";
		}
		for ( var index = 0; index < left.Meshes.Count; index++ )
		{
			var (a, b) = (left.Meshes[index], right.Meshes[index]);
			if ( a.Name != b.Name || !a.Positions.SequenceEqual( b.Positions ) || !a.CornerPositionIndices.SequenceEqual( b.CornerPositionIndices )
				|| !a.TexCoords.SequenceEqual( b.TexCoords ) || !a.Indices.SequenceEqual( b.Indices ) || !a.FaceNormalIndices.SequenceEqual( b.FaceNormalIndices )
				|| !a.CornerNormals.SequenceEqual( b.CornerNormals ) || !a.FaceNormals.SequenceEqual( b.FaceNormals ) || a.BoundsMin != b.BoundsMin || a.BoundsMax != b.BoundsMax )
				return $"mesh {index} geometry";
			if ( a.Materials.Length != b.Materials.Length )
				return $"mesh {index} material count";
			for ( var material = 0; material < a.Materials.Length; material++ )
			{
				var (c, d) = (a.Materials[material], b.Materials[material]);
				if ( c.a != d.a || c.b != d.b || c.StartIndex != d.StartIndex || c.EndIndex != d.EndIndex || c.Pad != d.Pad || c.TextureIndex != d.TextureIndex
					|| c.Name != d.Name || c.Flags != d.Flags )
					return $"mesh {index} material {material}";
			}
		}
		return null;
	}
}
