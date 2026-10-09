using System.Buffers.Binary;
using System.Text;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// Bounded reader for Bullfrog M3D2 (.MD2) members, version 221.203. See docs/MD2-MODELS.md
/// for the verified layout; unknown fields are retained raw or ignored, never interpreted.
/// </summary>
public partial class ModelFile : BaseFormat
{
	public const int MaximumFileBytes = 16 * 1024 * 1024;
	public const int HeaderBytes = 0xB8;
	public const int MeshRecordBytes = 160;
	public const int DummyRecordBytes = 88;
	public const int AnimationTrailerBytes = 72;
	public const uint Magic = 0x1CD15D46;
	public const uint SupportedVersionMajor = 221;
	public const uint SupportedVersionMinor = 203;
	private const int MaximumNodeNameBytes = 256;
	private static readonly int[] AnimationTrailerPointerWords = { 8, 9, 10, 11, 12, 14, 15 };

	public ModelFileKind Kind { get; private set; }
	public uint VersionMajor { get; private set; }
	public uint VersionMinor { get; private set; }
	public string SourceName { get; private set; } = "";
	public uint HeaderFlags { get; private set; }
	public ModelHeaderCounts Counts { get; private set; } = null!;
	public Vector3 BoundsMin { get; private set; }
	public Vector3 BoundsMax { get; private set; }
	public IReadOnlyList<ModelTexture> Textures { get; private set; } = Array.Empty<ModelTexture>();
	public IReadOnlyList<ModelNode> Nodes { get; private set; } = Array.Empty<ModelNode>();
	public int RootNodeIndex { get; private set; } = -1;
	public IReadOnlyList<ModelDummyAttribute> DummyAttributes { get; private set; } = Array.Empty<ModelDummyAttribute>();
	/// <summary>Opaque 16-byte records counted by the u16 at 0x40 and located by the u32 at 0xAC.</summary>
	public IReadOnlyList<byte[]> ExtraRecords { get; private set; } = Array.Empty<byte[]>();
	/// <summary>Opaque block pointer at 0x6C (only observed in terrain base models); 0 when absent.</summary>
	public uint UnknownBlockOffset { get; private set; }
	public ModelAnimationTrailer? Animation { get; private set; }
	/// <summary>Decoded position/rotation/scale tracks of an animation member.</summary>
	public ModelAnimation? Clip { get; private set; }
	public List<Mesh> Meshes { get; private set; } = new();

	public ModelFile( Stream stream )
	{
		ReadFromStream( stream );
	}

	public ModelFile( string path )
	{
		ReadFromFile( path );
	}

	public struct Vertex
	{
		public Vector3 Position { get; set; }
		public uint TextureIndex { get; set; }
	}

	public class Mesh
	{
		public string Name { get; set; } = "";
		public int NodeIndex { get; set; }
		public uint NodeType { get; set; }
		public uint VertexOffset { get; set; }
		public uint UvOffset { get; set; }
		/// <summary>Legacy name: holds the material count.</summary>
		public uint VertCnt { get; set; }
		public uint MaterialOffset { get; set; }
		public uint FaceOffset { get; set; }
		public uint FaceCount { get; set; }
		public uint VertexCount { get; set; }
		/// <summary>Number of corners (position/UV/normal combinations).</summary>
		public uint VertexOrderLen { get; set; }
		public uint VertexOrderOffset { get; set; }
		/// <summary>Unique stored positions (<see cref="VertexCount"/> entries, block padding dropped).</summary>
		public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
		/// <summary>Per-corner index into <see cref="Positions"/>.</summary>
		public ushort[] CornerPositionIndices { get; set; } = Array.Empty<ushort>();
		/// <summary>Per-corner vertices: position plus index of the material whose range contains the corner.</summary>
		public Vertex[] Vertices { get; set; } = Array.Empty<Vertex>();
		/// <summary>Triangle corner indices in stored order.</summary>
		public uint[] Indices { get; set; } = Array.Empty<uint>();
		/// <summary>First word of each face record; observed as an index into the mesh normal block.</summary>
		public ushort[] FaceNormalIndices { get; set; } = Array.Empty<ushort>();
		public Vector2[] TexCoords { get; set; } = Array.Empty<Vector2>();
		public Matrix4x4 TransformMatrix { get; set; }
		public Vector3 BoundsMin { get; set; }
		public Vector3 BoundsMax { get; set; }
		public MaterialData[] Materials { get; set; } = Array.Empty<MaterialData>();
		/// <summary>Stored per-corner normals (first part of the mesh normal block).</summary>
		public Vector3[] CornerNormals { get; set; } = Array.Empty<Vector3>();
		/// <summary>Stored per-face normals (second part of the mesh normal block).</summary>
		public Vector3[] FaceNormals { get; set; } = Array.Empty<Vector3>();
		/// <summary>Renderer normals: verified stored corner normals, recomputed smooth normals as fallback.</summary>
		public Vector3[] Normals { get; set; } = Array.Empty<Vector3>();
	}

	public class MaterialData
	{
		public uint FrameOffset;
		/// <summary>Opaque; zero throughout the inspected corpus.</summary>
		public ushort a;
		/// <summary>Observed as the number of faces whose corners lie in this material's range.</summary>
		public ushort b;
		public ushort StartIndex;
		public ushort EndIndex;
		/// <summary>Opaque; 0 or 1 in the inspected corpus.</summary>
		public uint Pad;

		/// <summary>Texture slot index, or -1 for a material without texture pointer.</summary>
		public int TextureIndex = -1;
		/// <summary>First frame name without extension; empty when untextured.</summary>
		public string Name = "";
		/// <summary>Raw texture slot flags (0 when untextured); bits are not interpreted.</summary>
		public uint Flags;
	}

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		Parse( ReadBounded( stream ) );
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var buffer = new byte[16384];
		while ( true )
		{
			var count = stream.Read( buffer, 0, (int)Math.Min( buffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "MD2 exceeds the input byte limit." );
			input.Write( buffer, 0, count );
		}
		return input.ToArray();
	}

	private void Parse( byte[] data )
	{
		if ( data.Length < HeaderBytes )
			throw new InvalidDataException( "MD2 header is truncated." );
		if ( U32( data, 0 ) != Magic )
			throw new InvalidDataException( "MD2 magic is missing." );
		VersionMajor = U32( data, 4 );
		VersionMinor = U32( data, 8 );
		if ( VersionMajor != SupportedVersionMajor || VersionMinor != SupportedVersionMinor )
			throw new NotSupportedException( $"Unsupported MD2 version {VersionMajor}.{VersionMinor}." );

		var nameBytes = data.AsSpan( 0x18, 20 );
		var nameLength = nameBytes.IndexOf( (byte)0 );
		SourceName = Encoding.Latin1.GetString( nameLength < 0 ? nameBytes : nameBytes[..nameLength] );
		HeaderFlags = U32( data, 0x30 );
		Counts = new ModelHeaderCounts( U16( data, 0x36 ), U16( data, 0x38 ), U16( data, 0x3A ), U16( data, 0x3C ), U16( data, 0x3E ),
			U16( data, 0x40 ), U16( data, 0x42 ), U16( data, 0x44 ), U16( data, 0x46 ), U16( data, 0x48 ), U16( data, 0x4A ) );
		BoundsMin = ReadVector3( data, 0x80 );
		BoundsMax = ReadVector3( data, 0x8C );

		var offsets = new uint[12];
		var anyOffset = false;
		for ( var index = 0; index < offsets.Length; index++ )
		{
			offsets[index] = U32( data, 0x50 + index * 4 );
			anyOffset |= offsets[index] != 0;
		}

		if ( anyOffset )
		{
			Kind = ModelFileKind.Geometry;
			ParseGeometry( data, offsets );
		}
		else
		{
			Kind = ModelFileKind.Animation;
			ParseAnimation( data );
		}
	}

	private void ParseAnimation( byte[] data )
	{
		var trailer = U32( data, 0x98 );
		if ( data.Length < HeaderBytes + AnimationTrailerBytes || trailer != data.Length - AnimationTrailerBytes )
			throw new InvalidDataException( "MD2 animation trailer pointer does not locate the final 72 bytes." );
		var words = new uint[AnimationTrailerBytes / 4];
		for ( var index = 0; index < words.Length; index++ )
			words[index] = U32( data, (int)trailer + index * 4 );
		foreach ( var index in AnimationTrailerPointerWords )
		{
			if ( words[index] != 0 && (words[index] < HeaderBytes || words[index] >= trailer) )
				throw new InvalidDataException( $"MD2 animation trailer word {index} points outside the payload." );
		}
		Animation = new ModelAnimationTrailer( (int)trailer, words );
		Clip = ModelAnimation.Decode( data, (int)trailer, words );
	}

	private void ParseGeometry( byte[] data, uint[] offsets )
	{
		var counts = Counts;
		var flagTable = offsets[0];
		var textureTable = offsets[1];
		UnknownBlockOffset = offsets[7];

		// Textures: 8-byte flag records, then contiguous 20-byte frame names, then 16-byte slot records.
		Require( data, flagTable, counts.TextureCount * 8L, "texture flag table" );
		Require( data, textureTable, counts.TextureCount * 16L, "texture slot table" );
		var namesStart = flagTable + counts.TextureCount * 8L;
		var frameTotal = 0L;
		var textures = new ModelTexture[counts.TextureCount];
		for ( var index = 0; index < textures.Length; index++ )
		{
			var slot = (int)textureTable + index * 16;
			var frames = U16( data, slot + 10 );
			var nameOffset = U32( data, slot + 12 );
			if ( frames == 0 || nameOffset != namesStart + frameTotal * 20 )
				throw new InvalidDataException( $"MD2 texture slot {index} has an invalid frame list." );
			Require( data, nameOffset, frames * 20L, "texture names" );
			var names = new string[frames];
			for ( var frame = 0; frame < frames; frame++ )
				names[frame] = ReadFixedName( data, (int)nameOffset + frame * 20, 20 );
			frameTotal += frames;
			textures[index] = new ModelTexture( U32( data, (int)flagTable + index * 8 ), U32( data, slot ), names );
		}
		if ( textureTable != namesStart + frameTotal * 20 )
			throw new InvalidDataException( "MD2 texture names are not contiguous with the slot table." );
		Textures = textures;

		ReadNodes( data, offsets, out var nodeRecords );
		ReadMeshes( data, offsets, nodeRecords, textures, flagTable );

		var attributes = new ModelDummyAttribute[counts.DummyAttributeCount];
		if ( attributes.Length > 0 )
		{
			Require( data, offsets[11], attributes.Length * 20L, "dummy attribute table" );
			for ( var index = 0; index < attributes.Length; index++ )
			{
				var record = (int)offsets[11] + index * 20;
				attributes[index] = new ModelDummyAttribute( U32( data, record ), U32( data, record + 4 ), data.AsSpan( record + 8, 12 ).ToArray() );
			}
		}
		DummyAttributes = attributes;

		var extra = new byte[counts.ExtraRecordCount][];
		if ( extra.Length > 0 )
		{
			var extraOffset = U32( data, 0xAC );
			Require( data, extraOffset, extra.Length * 16L, "extra record table" );
			for ( var index = 0; index < extra.Length; index++ )
				extra[index] = data.AsSpan( (int)extraOffset + index * 16, 16 ).ToArray();
		}
		ExtraRecords = extra;
		if ( UnknownBlockOffset != 0 )
			Require( data, UnknownBlockOffset, 1, "0x6C block" );
	}

	private void ReadNodes( byte[] data, uint[] offsets, out int[] nodeRecords )
	{
		var meshCount = Counts.MeshCount;
		var nodeCount = Counts.NodeCount;
		if ( nodeCount == 0 || nodeCount < meshCount )
			throw new InvalidDataException( "MD2 node count is inconsistent with its mesh count." );
		var dummyCount = nodeCount - meshCount;
		Require( data, offsets[8], meshCount * (long)MeshRecordBytes, "mesh table" );
		Require( data, offsets[9], dummyCount * (long)DummyRecordBytes, "dummy table" );

		nodeRecords = new int[nodeCount];
		var byOffset = new Dictionary<uint, int>( nodeCount );
		for ( var index = 0; index < nodeCount; index++ )
		{
			var record = index < meshCount
				? offsets[8] + (uint)(index * MeshRecordBytes)
				: offsets[9] + (uint)((index - meshCount) * DummyRecordBytes);
			if ( !byOffset.TryAdd( record, index ) )
				throw new InvalidDataException( "MD2 node records overlap." );
			nodeRecords[index] = (int)record;
		}

		int Resolve( uint pointer, string field, int node )
		{
			if ( pointer == 0 )
				return -1;
			if ( !byOffset.TryGetValue( pointer, out var target ) )
				throw new InvalidDataException( $"MD2 node {node} has an invalid {field} pointer." );
			return target;
		}

		var parents = new int[nodeCount];
		var siblings = new int[nodeCount];
		var children = new int[nodeCount];
		var nodes = new ModelNode[nodeCount];
		for ( var index = 0; index < nodeCount; index++ )
		{
			var record = nodeRecords[index];
			parents[index] = Resolve( U32( data, record + 4 ), "parent", index );
			siblings[index] = Resolve( U32( data, record + 8 ), "sibling", index );
			children[index] = Resolve( U32( data, record + 12 ), "child", index );
			nodes[index] = new ModelNode( index, U32( data, record ), U32( data, record + 80 ), ReadCString( data, U32( data, record + 84 ) ),
				parents[index], index < meshCount ? index : -1, ReadMatrix( data, record + 16 ) );
		}

		if ( !byOffset.TryGetValue( offsets[10], out var root ) )
			throw new InvalidDataException( "MD2 root node pointer is invalid." );
		if ( parents[root] != -1 || siblings[root] != -1 )
			throw new InvalidDataException( "MD2 root node has a parent or sibling." );

		// Every node must be reached exactly once from the root, with matching parent pointers.
		var visited = new bool[nodeCount];
		var pending = new Stack<int>();
		visited[root] = true;
		pending.Push( root );
		var reached = 1;
		while ( pending.Count > 0 )
		{
			var node = pending.Pop();
			for ( var child = children[node]; child != -1; child = siblings[child] )
			{
				if ( visited[child] )
					throw new InvalidDataException( "MD2 node hierarchy contains a cycle or shared node." );
				if ( parents[child] != node )
					throw new InvalidDataException( $"MD2 node {child} parent pointer disagrees with the hierarchy." );
				visited[child] = true;
				reached++;
				pending.Push( child );
			}
		}
		if ( reached != nodeCount )
			throw new InvalidDataException( "MD2 node hierarchy leaves unreachable nodes." );

		RootNodeIndex = root;
		Nodes = nodes;
	}

	private void ReadMeshes( byte[] data, uint[] offsets, int[] nodeRecords, ModelTexture[] textures, uint flagTable )
	{
		var counts = Counts;
		// Header-declared shared regions; every mesh array must lie inside its region.
		Require( data, offsets[2], counts.VertexBlockCount * 48L, "position region" );
		Require( data, offsets[3], counts.NormalCount * 12L, "normal region" );
		Require( data, offsets[4], counts.UvBlockCount * 32L, "texture coordinate region" );
		Require( data, offsets[5], counts.MaterialCount * 16L, "material region" );
		Require( data, offsets[6], counts.FaceCount * 8L, "face region" );
		long vertexBlocks = 0, uvBlocks = 0, materials = 0, faces = 0, normals = 0;
		Meshes = new List<Mesh>( counts.MeshCount );
		for ( var meshIndex = 0; meshIndex < counts.MeshCount; meshIndex++ )
		{
			var record = nodeRecords[meshIndex];
			var vertexCount = U16( data, record + 88 );
			var materialCount = U16( data, record + 90 );
			var faceCount = U16( data, record + 92 );
			var cornerCount = U16( data, record + 94 );
			var vertexOffset = U32( data, record + 96 );
			var normalOffset = U32( data, record + 100 );
			var uvOffset = U32( data, record + 104 );
			var materialOffset = U32( data, record + 108 );
			var faceOffset = U32( data, record + 112 );
			var orderOffset = U32( data, record + 148 );
			var paddedVertices = RoundUp4( vertexCount );
			var paddedCorners = RoundUp4( cornerCount );
			RequireWithin( offsets[2], counts.VertexBlockCount * 48L, vertexOffset, paddedVertices * 12L, $"mesh {meshIndex} positions" );
			RequireWithin( offsets[3], counts.NormalCount * 12L, normalOffset, (cornerCount + faceCount) * 12L, $"mesh {meshIndex} normals" );
			RequireWithin( offsets[4], counts.UvBlockCount * 32L, uvOffset, paddedCorners * 8L, $"mesh {meshIndex} texture coordinates" );
			RequireWithin( offsets[5], counts.MaterialCount * 16L, materialOffset, materialCount * 16L, $"mesh {meshIndex} materials" );
			RequireWithin( offsets[6], counts.FaceCount * 8L, faceOffset, faceCount * 8L, $"mesh {meshIndex} faces" );
			Require( data, orderOffset, cornerCount * 2L, $"mesh {meshIndex} corner table" );
			vertexBlocks += paddedVertices / 4;
			uvBlocks += paddedCorners / 4;
			materials += materialCount;
			faces += faceCount;
			normals += cornerCount + faceCount;

			// Positions and texture coordinates are stored as blocks of four (X×4, Y×4, Z×4 / U×4, V×4).
			var positions = new Vector3[vertexCount];
			for ( var index = 0; index < vertexCount; index++ )
			{
				var block = (int)vertexOffset + index / 4 * 48 + index % 4 * 4;
				positions[index] = new Vector3( F32( data, block ), F32( data, block + 16 ), F32( data, block + 32 ) );
			}
			var texCoords = new Vector2[cornerCount];
			for ( var index = 0; index < cornerCount; index++ )
			{
				var block = (int)uvOffset + index / 4 * 32 + index % 4 * 4;
				texCoords[index] = new Vector2( F32( data, block ), F32( data, block + 16 ) );
			}
			var order = new ushort[cornerCount];
			for ( var index = 0; index < cornerCount; index++ )
			{
				order[index] = U16( data, (int)orderOffset + index * 2 );
				if ( order[index] >= vertexCount )
					throw new InvalidDataException( $"MD2 mesh {meshIndex} corner {index} references a missing position." );
			}
			var cornerNormals = new Vector3[cornerCount];
			for ( var index = 0; index < cornerCount; index++ )
				cornerNormals[index] = ReadVector3( data, (int)normalOffset + index * 12 );
			var faceNormals = new Vector3[faceCount];
			for ( var index = 0; index < faceCount; index++ )
				faceNormals[index] = ReadVector3( data, (int)normalOffset + (cornerCount + index) * 12 );

			var indices = new uint[faceCount * 3];
			var faceNormalIndices = new ushort[faceCount];
			for ( var face = 0; face < faceCount; face++ )
			{
				var faceRecord = (int)faceOffset + face * 8;
				faceNormalIndices[face] = U16( data, faceRecord );
				for ( var corner = 0; corner < 3; corner++ )
				{
					var value = U16( data, faceRecord + 2 + corner * 2 );
					if ( value >= cornerCount )
						throw new InvalidDataException( $"MD2 mesh {meshIndex} face {face} references a missing corner." );
					indices[face * 3 + corner] = value;
				}
			}

			var meshMaterials = new MaterialData[materialCount];
			for ( var index = 0; index < materialCount; index++ )
			{
				var materialRecord = (int)materialOffset + index * 16;
				var material = new MaterialData
				{
					FrameOffset = U32( data, materialRecord ),
					a = U16( data, materialRecord + 4 ),
					b = U16( data, materialRecord + 6 ),
					StartIndex = U16( data, materialRecord + 8 ),
					EndIndex = U16( data, materialRecord + 10 ),
					Pad = U32( data, materialRecord + 12 )
				};
				if ( material.StartIndex > material.EndIndex || material.EndIndex >= cornerCount )
					throw new InvalidDataException( $"MD2 mesh {meshIndex} material {index} has an invalid corner range." );
				if ( material.FrameOffset != 0 )
				{
					var relative = (long)material.FrameOffset - flagTable;
					if ( relative < 0 || relative % 8 != 0 || relative / 8 >= textures.Length )
						throw new InvalidDataException( $"MD2 mesh {meshIndex} material {index} has an invalid texture pointer." );
					material.TextureIndex = (int)(relative / 8);
					material.Name = Path.GetFileNameWithoutExtension( textures[material.TextureIndex].FrameNames[0] );
					material.Flags = textures[material.TextureIndex].Flags;
				}
				meshMaterials[index] = material;
			}

			var vertices = new Vertex[cornerCount];
			for ( var corner = 0; corner < cornerCount; corner++ )
			{
				var textureIndex = 0;
				for ( var material = 0; material < meshMaterials.Length; material++ )
				{
					if ( corner >= meshMaterials[material].StartIndex && corner <= meshMaterials[material].EndIndex )
					{
						textureIndex = material;
						break;
					}
				}
				vertices[corner] = new Vertex { Position = positions[order[corner]], TextureIndex = (uint)textureIndex };
			}

			var node = Nodes[meshIndex];
			var mesh = new Mesh
			{
				Name = node.Name,
				NodeIndex = meshIndex,
				NodeType = node.Type,
				VertCnt = materialCount,
				UvOffset = uvOffset,
				VertexOffset = vertexOffset,
				MaterialOffset = materialOffset,
				FaceOffset = faceOffset,
				FaceCount = faceCount,
				VertexCount = vertexCount,
				VertexOrderLen = cornerCount,
				VertexOrderOffset = orderOffset,
				TransformMatrix = node.Transform,
				BoundsMin = ReadVector3( data, record + 120 ),
				BoundsMax = ReadVector3( data, record + 132 ),
				Positions = positions,
				CornerPositionIndices = order,
				Vertices = vertices,
				Indices = indices,
				FaceNormalIndices = faceNormalIndices,
				TexCoords = texCoords,
				Materials = meshMaterials,
				CornerNormals = cornerNormals,
				FaceNormals = faceNormals
			};
			CalculateNormals( mesh );
			Meshes.Add( mesh );
		}

		if ( vertexBlocks != counts.VertexBlockCount || uvBlocks != counts.UvBlockCount || materials != counts.MaterialCount
			|| faces != counts.FaceCount || normals != counts.NormalCount )
			throw new InvalidDataException( "MD2 mesh totals disagree with the header counts." );
	}

	/// <summary>
	/// Renderer normals: the stored corner normal when it is unit length and faces the same
	/// hemisphere as every triangle using the corner (true for 297,540 of 301,629 original corner
	/// uses); otherwise the recomputed smooth normal of the corner's faces.
	/// </summary>
	private static void CalculateNormals( Mesh mesh )
	{
		var normals = new Vector3[mesh.Vertices.Length];
		var usable = new bool[mesh.Vertices.Length];
		for ( var index = 0; index < usable.Length; index++ )
		{
			var stored = index < mesh.CornerNormals.Length ? mesh.CornerNormals[index] : Vector3.Zero;
			usable[index] = float.IsFinite( stored.X ) && float.IsFinite( stored.Y ) && float.IsFinite( stored.Z ) && MathF.Abs( stored.Length - 1 ) <= 1e-3f;
		}
		for ( var index = 0; index < mesh.Indices.Length; index += 3 )
		{
			var i1 = mesh.Indices[index];
			var i2 = mesh.Indices[index + 1];
			var i3 = mesh.Indices[index + 2];
			var v1 = mesh.Vertices[i1].Position;
			var faceNormal = (mesh.Vertices[i2].Position - v1).Cross( mesh.Vertices[i3].Position - v1 ).Normal;
			foreach ( var corner in new[] { i1, i2, i3 } )
			{
				normals[corner] += faceNormal;
				if ( usable[corner] && faceNormal != Vector3.Zero && mesh.CornerNormals[corner].Dot( faceNormal ) <= 0 )
					usable[corner] = false;
			}
		}
		for ( var index = 0; index < normals.Length; index++ )
		{
			if ( usable[index] )
				normals[index] = mesh.CornerNormals[index];
			else if ( normals[index] != Vector3.Zero )
				normals[index] = normals[index].Normal;
		}
		mesh.Normals = normals;
	}

	private static void Require( byte[] data, uint offset, long length, string what )
	{
		if ( length == 0 )
			return;
		if ( offset < HeaderBytes || offset > data.Length || length > data.Length - (long)offset )
			throw new InvalidDataException( $"MD2 {what} is outside the file." );
	}

	private static void RequireWithin( uint regionOffset, long regionLength, uint offset, long length, string what )
	{
		if ( length == 0 )
			return;
		if ( offset < regionOffset || length > regionLength || offset - (long)regionOffset > regionLength - length )
			throw new InvalidDataException( $"MD2 {what} lie outside the header-declared region." );
	}

	private static string ReadFixedName( byte[] data, int offset, int length )
	{
		var span = data.AsSpan( offset, length );
		var end = span.IndexOf( (byte)0 );
		if ( end < 0 )
			throw new InvalidDataException( "MD2 fixed-length name is not terminated." );
		return ReadPrintable( span[..end] );
	}

	private static string ReadCString( byte[] data, uint offset )
	{
		if ( offset < HeaderBytes || offset >= data.Length )
			throw new InvalidDataException( "MD2 node name pointer is outside the file." );
		var span = data.AsSpan( (int)offset, (int)Math.Min( MaximumNodeNameBytes, data.Length - offset ) );
		var end = span.IndexOf( (byte)0 );
		if ( end < 0 )
			throw new InvalidDataException( "MD2 node name is not terminated." );
		return ReadPrintable( span[..end] );
	}

	private static string ReadPrintable( ReadOnlySpan<byte> bytes )
	{
		foreach ( var value in bytes )
		{
			if ( value < 0x20 || value > 0x7E )
				throw new InvalidDataException( "MD2 name contains non-printable bytes." );
		}
		return Encoding.ASCII.GetString( bytes );
	}

	private static Matrix4x4 ReadMatrix( byte[] data, int offset )
	{
		return new Matrix4x4(
			F32( data, offset ), F32( data, offset + 4 ), F32( data, offset + 8 ), F32( data, offset + 12 ),
			F32( data, offset + 16 ), F32( data, offset + 20 ), F32( data, offset + 24 ), F32( data, offset + 28 ),
			F32( data, offset + 32 ), F32( data, offset + 36 ), F32( data, offset + 40 ), F32( data, offset + 44 ),
			F32( data, offset + 48 ), F32( data, offset + 52 ), F32( data, offset + 56 ), F32( data, offset + 60 ) );
	}

	private static Vector3 ReadVector3( byte[] data, int offset ) => new( F32( data, offset ), F32( data, offset + 4 ), F32( data, offset + 8 ) );
	private static int RoundUp4( int value ) => (value + 3) & ~3;
	private static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
	private static float F32( byte[] data, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) );
}
