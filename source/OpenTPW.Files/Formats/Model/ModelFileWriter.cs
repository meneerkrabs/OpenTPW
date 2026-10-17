using System.Buffers.Binary;
using System.Text;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// Writes a version 221.203 geometry member (a static model; animation members are not written). Every field the reader keeps is
/// written from the <see cref="ModelFile"/>; the offsets and the bytes between structures come from <see cref="ModelFile.Layout"/>
/// when the model was read with <c>captureLayout: true</c> (then the output equals the original member byte for byte, proven over the
/// whole local corpus by <c>Md2WriterCorpusTests</c>), otherwise from a planned layout (see <see cref="Plan"/>), which is how
/// <see cref="ModelBuilder"/> models are written. Header counts are always recomputed from the content.
/// </summary>
public static class ModelFileWriter
{
	/// <summary>The member as bytes.</summary>
	public static byte[] Write( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new NotSupportedException( "Only geometry members are written; animation members are not supported." );
		return Serialize( model, model.Layout ?? Plan( model ), null );
	}

	/// <summary>Writes the member to <paramref name="stream"/> (left open).</summary>
	public static void Write( ModelFile model, Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		stream.Write( Write( model ) );
	}

	/// <summary>The header counts the content implies; <see cref="ModelHeaderCounts.Unknown46"/> is the model's own.</summary>
	internal static ModelHeaderCounts ComputeCounts( ModelFile model )
	{
		long vertexBlocks = 0, uvBlocks = 0, materials = 0, faces = 0, normals = 0;
		foreach ( var mesh in model.Meshes )
		{
			vertexBlocks += RoundUp4( mesh.Positions.Length ) / 4;
			uvBlocks += RoundUp4( mesh.CornerPositionIndices.Length ) / 4;
			materials += mesh.Materials.Length;
			faces += mesh.Indices.Length / 3;
			normals += mesh.CornerPositionIndices.Length + mesh.Indices.Length / 3;
		}
		return new ModelHeaderCounts( U16( model.Textures.Count ), U16( vertexBlocks ), U16( uvBlocks ), U16( materials ), U16( faces ), U16( model.ExtraRecords.Count ),
			U16( model.Nodes.Count ), U16( model.Meshes.Count ), model.Counts == null ? (ushort)0xFFFF : model.Counts.Unknown46, U16( model.DummyAttributes.Count ), U16( normals ) );
	}

	/// <summary>
	/// Builds the layout that was read from <paramref name="data"/>: the header, the offsets, the node links, and the non-zero bytes
	/// that the writer does not cover itself (found by writing the model with this layout and comparing which bytes it touched).
	/// </summary>
	internal static ModelLayout CaptureLayout( ModelFile model, byte[] data, uint[] offsets, byte[] header, int fileLength, int[] firstChild, int[] nextSibling,
		uint[] nodeNames, IReadOnlyList<ModelMeshLayout> meshes )
	{
		var extraOffset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0xAC ) );
		var pathPoints = new uint[model.ExtraRecords.Count];
		for ( var index = 0; index < pathPoints.Length; index++ )
			pathPoints[index] = BinaryPrimitives.ReadUInt32LittleEndian( model.ExtraRecords[index].AsSpan( 8 ) );
		uint heights = 0, cells = 0;
		if ( offsets[7] != 0 )
		{
			heights = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( (int)offsets[7] + 40 ) );
			cells = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( (int)offsets[7] + 44 ) );
		}
		var layout = new ModelLayout
		{
			Header = header,
			FileLength = fileLength,
			FlagTable = offsets[0],
			SlotTable = offsets[1],
			Positions = offsets[2],
			Normals = offsets[3],
			TextureCoordinates = offsets[4],
			Materials = offsets[5],
			Faces = offsets[6],
			Heightfield = offsets[7],
			HeightfieldHeights = heights,
			HeightfieldCells = cells,
			MeshTable = offsets[8],
			DummyTable = offsets[9],
			Attributes = offsets[11],
			Paths = extraOffset,
			NodeNames = nodeNames,
			FirstChild = firstChild,
			NextSibling = nextSibling,
			Meshes = meshes.ToArray(),
			PathPoints = pathPoints
		};
		var covered = new bool[fileLength];
		Serialize( model, layout, covered );
		var gaps = new List<ModelLayoutGap>();
		for ( var index = 0; index < fileLength; )
		{
			if ( covered[index] )
			{
				index++;
				continue;
			}
			var start = index;
			while ( index < fileLength && !covered[index] )
				index++;
			var run = data.AsSpan( start, index - start );
			if ( run.IndexOfAnyExcept( (byte)0 ) >= 0 )
				gaps.Add( new ModelLayoutGap( start, run.ToArray() ) );
		}
		return layout with { Gaps = gaps };
	}

	/// <summary>
	/// The layout of a model without a captured one, in the order of the original members (header, texture flags, names and slots,
	/// positions, normals, materials, faces, corner tables, texture coordinates, node names, terrain block, path points, attributes,
	/// path records, mesh and dummy records). Position and texture-coordinate blocks start 16-byte aligned, everything else 4 or 8.
	/// The original's alignment rules are not verified beyond what the corpus shows.
	/// </summary>
	internal static ModelLayout Plan( ModelFile model )
	{
		var meshes = model.Meshes;
		var nodes = model.Nodes;
		var position = (long)ModelFile.HeaderBytes;
		var frames = model.Textures.Sum( texture => texture.FrameNames.Count );
		var flagTable = position;
		position += model.Textures.Count * 8L + frames * 20L;
		var slotTable = position;
		position += model.Textures.Count * 16L;

		position = Align( position, 16 );
		var positions = position;
		var meshLayouts = new long[meshes.Count, 6];
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 0] = position;
			position += RoundUp4( meshes[index].Positions.Length ) * 12L;
		}
		var normals = position;
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 1] = position;
			position += (meshes[index].CornerPositionIndices.Length + meshes[index].Indices.Length / 3) * 12L;
		}
		position = Align( position, 8 );
		var materials = position;
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 3] = position;
			position += meshes[index].Materials.Length * 16L;
		}
		position = Align( position, 8 );
		var faces = position;
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 4] = position;
			position += meshes[index].Indices.Length / 3 * 8L;
		}
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 5] = position;
			position += meshes[index].CornerPositionIndices.Length * 2L;
		}
		position = Align( position, 16 );
		var uvs = position;
		for ( var index = 0; index < meshes.Count; index++ )
		{
			meshLayouts[index, 2] = position;
			position += RoundUp4( meshes[index].CornerPositionIndices.Length ) * 8L;
		}
		var names = new uint[nodes.Count];
		for ( var index = 0; index < nodes.Count; index++ )
		{
			names[index] = (uint)position;
			position += Encoding.ASCII.GetByteCount( nodes[index].Name ) + 1;
		}

		long heightfield = 0, heights = 0, cells = 0;
		if ( model.Heightfield is { } terrain )
		{
			position = Align( position, 4 );
			heightfield = position;
			position += ModelHeightfield.HeaderBytes;
			heights = position;
			position += terrain.Heights.Count * 4L;
			cells = position;
			position += terrain.CellWords.Count * 4L;
		}
		var pathPoints = new uint[model.ExtraRecords.Count];
		for ( var index = 0; index < pathPoints.Length; index++ )
		{
			position = Align( position, 4 );
			pathPoints[index] = (uint)position;
			position += model.Paths[index].Points.Count * 12L;
		}

		position = Align( position, 8 );
		long attributes = 0;
		if ( model.DummyAttributes.Count > 0 )
		{
			attributes = position;
			position += model.DummyAttributes.Count * 20L;
		}
		long paths = 0;
		if ( model.ExtraRecords.Count > 0 )
		{
			paths = position;
			position += model.ExtraRecords.Count * 16L;
		}
		position = Align( position, 8 );
		var meshTable = position;
		position += meshes.Count * (long)ModelFile.MeshRecordBytes;
		long dummyTable = 0;
		var dummies = nodes.Count - meshes.Count;
		if ( dummies > 0 )
		{
			dummyTable = position;
			position += dummies * (long)ModelFile.DummyRecordBytes;
		}
		if ( position > ModelFile.MaximumFileBytes )
			throw new InvalidOperationException( "The model exceeds the 16 MiB member limit." );

		var firstChild = new int[nodes.Count];
		var nextSibling = new int[nodes.Count];
		Array.Fill( firstChild, -1 );
		Array.Fill( nextSibling, -1 );
		var lastChild = new int[nodes.Count];
		Array.Fill( lastChild, -1 );
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var parent = nodes[index].ParentIndex;
			if ( parent < 0 )
				continue;
			if ( lastChild[parent] < 0 )
				firstChild[parent] = index;
			else
				nextSibling[lastChild[parent]] = index;
			lastChild[parent] = index;
		}

		var header = new byte[ModelFile.HeaderBytes];
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 0 ), ModelFile.Magic );
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 4 ), ModelFile.SupportedVersionMajor );
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 8 ), ModelFile.SupportedVersionMinor );
		// Opaque words: the most common corpus values (0x0C/0x10 pair in 61 members, 0x14 a 1999 Unix time); 0x2C sits a few bytes below the file length.
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 0x0C ), OpaqueWord0C );
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 0x10 ), OpaqueWord10 );
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 0x14 ), OpaqueWord14 );
		BinaryPrimitives.WriteUInt32LittleEndian( header.AsSpan( 0x2C ), (uint)Math.Max( 0, position - OpaqueWord2CBelowEnd ) );

		return new ModelLayout
		{
			Header = header,
			FileLength = (int)position,
			FlagTable = (uint)flagTable,
			SlotTable = (uint)slotTable,
			Positions = (uint)positions,
			Normals = (uint)normals,
			TextureCoordinates = (uint)uvs,
			Materials = (uint)materials,
			Faces = (uint)faces,
			Heightfield = (uint)heightfield,
			HeightfieldHeights = (uint)heights,
			HeightfieldCells = (uint)cells,
			MeshTable = (uint)meshTable,
			DummyTable = (uint)dummyTable,
			Attributes = (uint)attributes,
			Paths = (uint)paths,
			NodeNames = names,
			FirstChild = firstChild,
			NextSibling = nextSibling,
			Meshes = Enumerable.Range( 0, meshes.Count ).Select( index => new ModelMeshLayout( (uint)meshLayouts[index, 0], (uint)meshLayouts[index, 1],
				(uint)meshLayouts[index, 2], (uint)meshLayouts[index, 3], (uint)meshLayouts[index, 4], (uint)meshLayouts[index, 5] ) ).ToArray(),
			PathPoints = pathPoints
		};
	}

	/// <summary>Unverified defaults for header words whose meaning is unknown (the most common corpus values).</summary>
	public const uint OpaqueWord0C = 1142;
	public const uint OpaqueWord10 = 281;
	public const uint OpaqueWord14 = 939121370;
	public const int OpaqueWord2CBelowEnd = 11;

	private static byte[] Serialize( ModelFile model, ModelLayout layout, bool[]? covered )
	{
		var meshes = model.Meshes;
		var nodes = model.Nodes;
		if ( layout.Meshes.Count != meshes.Count || layout.NodeNames.Count != nodes.Count || layout.FirstChild.Count != nodes.Count )
			throw new InvalidOperationException( "The layout does not match the model." );
		var image = new Image( new byte[layout.FileLength], covered );
		foreach ( var gap in layout.Gaps )
			gap.Bytes.CopyTo( image.Data, gap.Offset );

		WriteHeader( image, model, layout );
		var textures = model.Textures;
		var nameCursor = layout.FlagTable + textures.Count * 8L;
		for ( var index = 0; index < textures.Count; index++ )
		{
			image.Mark( layout.FlagTable + index * 8, 8 );
			image.U32( layout.FlagTable + index * 8, textures[index].Flags );
			var slot = layout.SlotTable + index * 16;
			image.Mark( slot, 16 );
			image.U32( slot, textures[index].Unknown );
			image.U16( slot + 10, U16( textures[index].FrameNames.Count ) );
			image.U32( slot + 12, (uint)nameCursor );
			foreach ( var name in textures[index].FrameNames )
			{
				if ( Encoding.ASCII.GetByteCount( name ) > 19 )
					throw new InvalidOperationException( $"Texture frame name '{name}' does not fit its 20-byte field." );
				image.Mark( nameCursor, 20 );
				Encoding.ASCII.GetBytes( name, image.Data.AsSpan( (int)nameCursor, 20 ) );
				nameCursor += 20;
			}
		}

		var meshCount = meshes.Count;
		long RecordOffset( int node ) => node < meshCount ? layout.MeshTable + node * (long)ModelFile.MeshRecordBytes : layout.DummyTable + (node - meshCount) * (long)ModelFile.DummyRecordBytes;
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			var record = RecordOffset( index );
			image.Mark( record, index < meshCount ? ModelFile.MeshRecordBytes : ModelFile.DummyRecordBytes );
			image.U32( record, node.Type );
			image.U32( record + 4, node.ParentIndex < 0 ? 0 : (uint)RecordOffset( node.ParentIndex ) );
			image.U32( record + 8, layout.NextSibling[index] < 0 ? 0 : (uint)RecordOffset( layout.NextSibling[index] ) );
			image.U32( record + 12, layout.FirstChild[index] < 0 ? 0 : (uint)RecordOffset( layout.FirstChild[index] ) );
			var matrix = node.Transform;
			var cells = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
				matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
			for ( var cell = 0; cell < cells.Length; cell++ )
				image.F32( record + 16 + cell * 4, cells[cell] );
			image.U32( record + 80, node.StoredIndex );
			image.U32( record + 84, layout.NodeNames[index] );
			var nameBytes = Encoding.ASCII.GetByteCount( node.Name );
			image.Mark( layout.NodeNames[index], nameBytes + 1 );
			Encoding.ASCII.GetBytes( node.Name, image.Data.AsSpan( (int)layout.NodeNames[index], nameBytes ) );
		}

		for ( var index = 0; index < meshCount; index++ )
		{
			var mesh = meshes[index];
			var at = layout.Meshes[index];
			var record = RecordOffset( index );
			var corners = mesh.CornerPositionIndices.Length;
			var faces = mesh.Indices.Length / 3;
			image.U16( record + 88, U16( mesh.Positions.Length ) );
			image.U16( record + 90, U16( mesh.Materials.Length ) );
			image.U16( record + 92, U16( faces ) );
			image.U16( record + 94, U16( corners ) );
			image.U32( record + 96, at.Positions );
			image.U32( record + 100, at.Normals );
			image.U32( record + 104, at.TextureCoordinates );
			image.U32( record + 108, at.Materials );
			image.U32( record + 112, at.Faces );
			image.F32( record + 120, mesh.BoundsMin.X );
			image.F32( record + 124, mesh.BoundsMin.Y );
			image.F32( record + 128, mesh.BoundsMin.Z );
			image.F32( record + 132, mesh.BoundsMax.X );
			image.F32( record + 136, mesh.BoundsMax.Y );
			image.F32( record + 140, mesh.BoundsMax.Z );
			image.U32( record + 148, at.Corners );

			// Positions and texture coordinates are stored as blocks of four (X×4, Y×4, Z×4 / U×4, V×4); block padding stays zero.
			image.Mark( at.Positions, RoundUp4( mesh.Positions.Length ) * 12L );
			for ( var vertex = 0; vertex < mesh.Positions.Length; vertex++ )
			{
				var block = at.Positions + vertex / 4 * 48L + vertex % 4 * 4;
				image.F32( block, mesh.Positions[vertex].X );
				image.F32( block + 16, mesh.Positions[vertex].Y );
				image.F32( block + 32, mesh.Positions[vertex].Z );
			}
			image.Mark( at.TextureCoordinates, RoundUp4( corners ) * 8L );
			for ( var corner = 0; corner < corners; corner++ )
			{
				var block = at.TextureCoordinates + corner / 4 * 32L + corner % 4 * 4;
				image.F32( block, mesh.TexCoords[corner].X );
				image.F32( block + 16, mesh.TexCoords[corner].Y );
			}
			image.Mark( at.Corners, corners * 2L );
			for ( var corner = 0; corner < corners; corner++ )
				image.U16( at.Corners + corner * 2L, mesh.CornerPositionIndices[corner] );
			image.Mark( at.Normals, (corners + faces) * 12L );
			for ( var corner = 0; corner < corners; corner++ )
				image.Vector( at.Normals + corner * 12L, mesh.CornerNormals[corner] );
			for ( var face = 0; face < faces; face++ )
				image.Vector( at.Normals + (corners + face) * 12L, mesh.FaceNormals[face] );
			image.Mark( at.Faces, faces * 8L );
			for ( var face = 0; face < faces; face++ )
			{
				image.U16( at.Faces + face * 8L, mesh.FaceNormalIndices[face] );
				for ( var corner = 0; corner < 3; corner++ )
					image.U16( at.Faces + face * 8L + 2 + corner * 2, U16( mesh.Indices[face * 3 + corner] ) );
			}
			image.Mark( at.Materials, mesh.Materials.Length * 16L );
			for ( var material = 0; material < mesh.Materials.Length; material++ )
			{
				var data = mesh.Materials[material];
				var materialRecord = at.Materials + material * 16L;
				image.U32( materialRecord, data.TextureIndex < 0 ? 0 : (uint)(layout.FlagTable + data.TextureIndex * 8L) );
				image.U16( materialRecord + 4, data.a );
				image.U16( materialRecord + 6, data.b );
				image.U16( materialRecord + 8, data.StartIndex );
				image.U16( materialRecord + 10, data.EndIndex );
				image.U32( materialRecord + 12, data.Pad );
			}
		}

		image.Mark( layout.Attributes, model.DummyAttributes.Count * 20L );
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			var record = layout.Attributes + index * 20L;
			image.U32( record, attribute.Type );
			image.U32( record + 4, attribute.Value );
			attribute.Reserved.CopyTo( image.Data, (int)record + 8 );
		}

		image.Mark( layout.Paths, model.ExtraRecords.Count * 16L );
		for ( var index = 0; index < model.ExtraRecords.Count; index++ )
		{
			var record = layout.Paths + index * 16L;
			model.ExtraRecords[index].CopyTo( image.Data, (int)record );
			image.U32( record + 8, layout.PathPoints[index] );
			var points = model.Paths[index].Points;
			image.Mark( layout.PathPoints[index], points.Count * 12L );
			for ( var point = 0; point < points.Count; point++ )
			{
				image.F32( layout.PathPoints[index] + point * 12L, points[point].X );
				image.F32( layout.PathPoints[index] + point * 12L + 4, points[point].Y );
				image.F32( layout.PathPoints[index] + point * 12L + 8, points[point].Z );
			}
		}

		if ( model.Heightfield is { } terrain )
		{
			var at = layout.Heightfield;
			image.Mark( at, ModelHeightfield.HeaderBytes );
			for ( var word = 0; word < 4; word++ )
				image.U32( at + word * 4L, terrain.OpaqueHeaderWords[word] );
			image.F32( at + 16, terrain.CellSizeX );
			image.F32( at + 20, terrain.CellSizeZ );
			image.U32( at + 24, (uint)terrain.CellCountX );
			image.U32( at + 28, (uint)terrain.CellCountZ );
			image.F32( at + 32, terrain.StoredLowerHeight );
			image.F32( at + 36, terrain.StoredUpperHeight );
			image.U32( at + 40, layout.HeightfieldHeights );
			image.U32( at + 44, layout.HeightfieldCells );
			image.Mark( layout.HeightfieldHeights, terrain.Heights.Count * 4L );
			for ( var index = 0; index < terrain.Heights.Count; index++ )
				image.F32( layout.HeightfieldHeights + index * 4L, terrain.Heights[index] );
			image.Mark( layout.HeightfieldCells, terrain.CellWords.Count * 4L );
			for ( var index = 0; index < terrain.CellWords.Count; index++ )
				image.U32( layout.HeightfieldCells + index * 4L, terrain.CellWords[index] );
		}
		return image.Data;
	}

	private static void WriteHeader( Image image, ModelFile model, ModelLayout layout )
	{
		layout.Header.CopyTo( image.Data, 0 );
		image.Mark( 0, ModelFile.HeaderBytes );
		image.U32( 0, ModelFile.Magic );
		image.U32( 4, model.VersionMajor );
		image.U32( 8, model.VersionMinor );
		// The name field keeps its stored bytes while it still decodes to the model's name.
		var stored = layout.Header.AsSpan( 0x18, 20 );
		var end = stored.IndexOf( (byte)0 );
		if ( Encoding.Latin1.GetString( end < 0 ? stored : stored[..end] ) != model.SourceName )
		{
			if ( Encoding.Latin1.GetByteCount( model.SourceName ) > 20 )
				throw new InvalidOperationException( "The source name does not fit its 20-byte field." );
			image.Data.AsSpan( 0x18, 20 ).Clear();
			Encoding.Latin1.GetBytes( model.SourceName, image.Data.AsSpan( 0x18, 20 ) );
		}
		image.U32( 0x30, model.HeaderFlags );
		var counts = ComputeCounts( model );
		ushort[] values = { counts.TextureCount, counts.VertexBlockCount, counts.UvBlockCount, counts.MaterialCount, counts.FaceCount, counts.ExtraRecordCount,
			counts.NodeCount, counts.MeshCount, counts.Unknown46, counts.DummyAttributeCount, counts.NormalCount };
		for ( var index = 0; index < values.Length; index++ )
			image.U16( 0x36 + index * 2, values[index] );
		uint[] offsets = { layout.FlagTable, layout.SlotTable, layout.Positions, layout.Normals, layout.TextureCoordinates, layout.Materials, layout.Faces,
			layout.Heightfield, layout.MeshTable, layout.DummyTable, RootOffset( model, layout ), layout.Attributes };
		for ( var index = 0; index < offsets.Length; index++ )
			image.U32( 0x50 + index * 4, offsets[index] );
		image.F32( 0x80, model.BoundsMin.X );
		image.F32( 0x84, model.BoundsMin.Y );
		image.F32( 0x88, model.BoundsMin.Z );
		image.F32( 0x8C, model.BoundsMax.X );
		image.F32( 0x90, model.BoundsMax.Y );
		image.F32( 0x94, model.BoundsMax.Z );
		image.F32( 0xA0, model.HeaderHeight );
		image.U32( 0xAC, layout.Paths );
	}

	private static uint RootOffset( ModelFile model, ModelLayout layout )
	{
		var root = model.RootNodeIndex;
		if ( root < 0 || root >= model.Nodes.Count )
			throw new InvalidOperationException( "The model has no root node." );
		return root < model.Meshes.Count ? layout.MeshTable + (uint)(root * ModelFile.MeshRecordBytes) : layout.DummyTable + (uint)((root - model.Meshes.Count) * ModelFile.DummyRecordBytes);
	}

	private static ushort U16( long value ) => value is >= 0 and <= ushort.MaxValue ? (ushort)value : throw new InvalidOperationException( $"The count {value} does not fit a 16-bit field." );
	private static int RoundUp4( int value ) => (value + 3) & ~3;
	private static long Align( long value, int alignment ) => (value + alignment - 1) / alignment * alignment;

	/// <summary>A zero-filled output buffer that remembers which bytes a structure wrote (for the gap capture).</summary>
	private sealed class Image
	{
		public Image( byte[] data, bool[]? covered )
		{
			Data = data;
			this.covered = covered;
		}

		private readonly bool[]? covered;
		public byte[] Data { get; }

		public void Mark( long offset, long length )
		{
			if ( covered != null && length > 0 )
				Array.Fill( covered, true, (int)offset, (int)length );
		}

		public void U16( long offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( Data.AsSpan( (int)offset ), value );
		public void U32( long offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( Data.AsSpan( (int)offset ), value );
		public void F32( long offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( Data.AsSpan( (int)offset ), value );

		public void Vector( long offset, Vector3 value )
		{
			F32( offset, value.X );
			F32( offset + 4, value.Y );
			F32( offset + 8, value.Z );
		}
	}
}
