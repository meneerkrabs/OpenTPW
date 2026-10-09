using System.Buffers.Binary;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// One 20-byte vertex group of a quantised vertex animation: <c>u16 keys, u16 vertices,
/// ptr→u16[vertices] mesh position indices, ptr→u16[keys] ticks, ptr→keys×vertices packed
/// words, u32 runtime key cursor</c> (the cursor is runtime state and not kept). Packed words
/// are key-major: key k of vertex j is word <c>k × vertices + j</c>.
/// </summary>
public sealed class ModelVertexGroup
{
	internal ModelVertexGroup( ushort[] vertexIndices, ushort[] ticks, uint[] packedKeys )
	{
		VertexIndices = vertexIndices;
		Ticks = ticks;
		PackedKeys = packedKeys;
	}

	public IReadOnlyList<ushort> VertexIndices { get; }
	public IReadOnlyList<ushort> Ticks { get; }
	public IReadOnlyList<uint> PackedKeys { get; }

	public uint PackedKey( int key, int vertex )
	{
		if ( (uint)key >= (uint)Ticks.Count || (uint)vertex >= (uint)VertexIndices.Count )
			throw new ArgumentOutOfRangeException( (uint)key >= (uint)Ticks.Count ? nameof( key ) : nameof( vertex ) );
		return PackedKeys[key * VertexIndices.Count + vertex];
	}

	/// <summary>
	/// Cursor search of SimThemePark 0xa4a58: from key 0, advance while
	/// <c>ticks[c + 1] &lt; time</c>, so a time on a key tick stays on the earlier segment with
	/// fraction 1 and a time before the first key extrapolates. The original keeps the cursor
	/// between frames and only resets it while node-state flag 0x00800000 is clear; for times
	/// that do not decrease since the reset that gives this result. Past the last key the
	/// original reads beyond the tick array, so that is rejected.
	/// </summary>
	public int FindKey( float time, out float fraction )
	{
		if ( float.IsNaN( time ) || Ticks.Count < 2 || time > Ticks[^1] )
			throw new ArgumentOutOfRangeException( nameof( time ), "Time lies outside the traced vertex-key domain." );
		var key = 0;
		while ( Ticks[key + 1] < time )
			key++;
		float start = Ticks[key];
		fraction = (time - start) / (Ticks[key + 1] - start);
		return key;
	}
}

/// <summary>
/// Quantised vertex animation block at record +40 (record flag 0x1000 with 0x4000 clear), read from
/// the Feral Mac build (docs/reverse/PPC-formats.md). 44 bytes: <c>u16 flags, u16 groups</c>,
/// u16 +4/+6/+8 and u32 +16 (not read by the sampler; kept in <see cref="UnreadFields"/>),
/// <c>ptr groups</c> (+12), <c>vec3 offset</c> (+20), <c>vec3 scale</c> (+32).
/// Group 0 holds two bound vertices, group 1 is static when flag 0x2 is set, and the remaining
/// groups animate mesh positions by index. The 12-byte layout (flag 0x4000) is not decoded.
/// </summary>
public sealed class ModelVertexAnimation
{
	public const int HeaderBytes = 44;
	public const int GroupBytes = 20;
	public const ushort StaticGroupFlag = 0x2;
	/// <summary>Added beyond one quantisation step to the group-0 vectors (SimThemePark 0xa468c).</summary>
	public const float BoundsPadding = 0.25f;

	internal ModelVertexAnimation( ushort flags, NVector3 offset, NVector3 scale, ModelVertexGroup[] groups, uint[] unreadFields )
	{
		Flags = flags;
		Offset = offset;
		Scale = scale;
		Groups = groups;
		UnreadFields = unreadFields;
	}

	public ushort Flags { get; }
	public NVector3 Offset { get; }
	public NVector3 Scale { get; }
	public IReadOnlyList<ModelVertexGroup> Groups { get; }
	/// <summary>Raw u16 +4, +6, +8 and u32 +16; zero in the whole corpus.</summary>
	public IReadOnlyList<uint> UnreadFields { get; }
	public bool HasStaticGroup => (Flags & StaticGroupFlag) != 0;
	public ModelVertexGroup BoundsGroup => Groups[0];
	public ModelVertexGroup? StaticGroup => HasStaticGroup ? Groups[1] : null;
	public IEnumerable<ModelVertexGroup> AnimatedGroups => Groups.Skip( HasStaticGroup ? 2 : 1 );

	/// <summary>Signed 10-bit X/Y/Z in little-endian bits 0–9/10–19/20–29; bits 30–31 are discarded.</summary>
	public static (int X, int Y, int Z) Unpack( uint word ) =>
		(SignExtend( word ), SignExtend( word >> 10 ), SignExtend( word >> 20 ));

	/// <summary>Per axis <c>q × scale + offset</c> as one fused single-precision step (0xa446c <c>fmadds</c>).</summary>
	public NVector3 Dequantise( uint word )
	{
		var (x, y, z) = Unpack( word );
		return new NVector3( MathF.FusedMultiplyAdd( x, Scale.X, Offset.X ), MathF.FusedMultiplyAdd( y, Scale.Y, Offset.Y ),
			MathF.FusedMultiplyAdd( z, Scale.Z, Offset.Z ) );
	}

	/// <summary>
	/// Group 0 (0xa468c): vertex 0 gives <c>lerp − scale − 0.25</c> and vertex 1
	/// <c>lerp + scale + 0.25</c>, stored to node-state +120 and +132. In the corpus vertex 0 ≤ vertex 1
	/// on every axis and every animated key lies between them (a padded bounding box); the consumer
	/// of the two node-state fields was not traced.
	/// </summary>
	public (NVector3 Lower, NVector3 Upper) SampleBounds( float time )
	{
		var group = BoundsGroup;
		var key = group.FindKey( time, out var fraction );
		var lower = Lerp( Dequantise( group.PackedKey( key, 0 ) ), Dequantise( group.PackedKey( key + 1, 0 ) ), fraction );
		var upper = Lerp( Dequantise( group.PackedKey( key, 1 ) ), Dequantise( group.PackedKey( key + 1, 1 ) ), fraction );
		return (lower - Scale - new NVector3( BoundsPadding ), Scale + upper + new NVector3( BoundsPadding ));
	}

	/// <summary>
	/// Writes (or, in add mode, adds) key 0 of the static group to <paramref name="positions"/>. The
	/// original does this only while node-state flag 0x00800000 is clear and sets that flag after a
	/// set-mode pass, so the static group is applied once per reset. Add mode is instance flag 4.
	/// </summary>
	public void ApplyStaticGroup( Span<NVector3> positions, bool add )
	{
		var group = StaticGroup ?? throw new InvalidOperationException( "The block has no static group." );
		RequireIndices( group, positions.Length );
		for ( var vertex = 0; vertex < group.VertexIndices.Count; vertex++ )
		{
			var value = Dequantise( group.PackedKey( 0, vertex ) );
			ref var target = ref positions[group.VertexIndices[vertex]];
			target = add ? target + value : value;
		}
	}

	/// <summary>
	/// Interpolates every group after group 0 and the static group (0xa4344) and writes or adds the
	/// result to <paramref name="positions"/>, indexed like <see cref="ModelFile.Mesh.Positions"/>
	/// (the original destination is <c>48·(v/4) + 4·(v%4)</c> in the stored X/Y/Z blocks of four).
	/// </summary>
	public void ApplyAnimatedGroups( float time, Span<NVector3> positions, bool add )
	{
		foreach ( var group in AnimatedGroups )
			RequireIndices( group, positions.Length );
		foreach ( var group in AnimatedGroups )
		{
			var key = group.FindKey( time, out var fraction );
			for ( var vertex = 0; vertex < group.VertexIndices.Count; vertex++ )
			{
				var value = Lerp( Dequantise( group.PackedKey( key, vertex ) ), Dequantise( group.PackedKey( key + 1, vertex ) ), fraction );
				ref var target = ref positions[group.VertexIndices[vertex]];
				target = add ? target + value : value;
			}
		}
	}

	// cur × (1 − t) + (next × t), the second product rounded first (fmuls, then fmadds).
	private static NVector3 Lerp( NVector3 current, NVector3 next, float fraction )
	{
		var inverse = 1 - fraction;
		return new NVector3( MathF.FusedMultiplyAdd( current.X, inverse, next.X * fraction ),
			MathF.FusedMultiplyAdd( current.Y, inverse, next.Y * fraction ), MathF.FusedMultiplyAdd( current.Z, inverse, next.Z * fraction ) );
	}

	private static int SignExtend( uint value ) => (int)(value << 22) >> 22;

	private static void RequireIndices( ModelVertexGroup group, int count )
	{
		foreach ( var index in group.VertexIndices )
		{
			if ( index >= count )
				throw new ArgumentException( $"Vertex index {index} lies outside the {count} mesh positions.", nameof( count ) );
		}
	}

	internal static ModelVertexAnimation Decode( byte[] data, int block, Func<uint, long, string, int> pointer )
	{
		var flags = U16( data, block );
		var groupCount = U16( data, block + 2 );
		var offset = Finite( data, block + 20, "offset" );
		var scale = Finite( data, block + 32, "scale" );
		var hasStatic = (flags & StaticGroupFlag) != 0;
		if ( groupCount < (hasStatic ? 2 : 1) )
			throw new InvalidDataException( "MD2 vertex animation lacks its bounds or static group." );
		var table = pointer( U32( data, block + 12 ), groupCount * (long)GroupBytes, "vertex groups" );
		var groups = new ModelVertexGroup[groupCount];
		for ( var index = 0; index < groupCount; index++ )
		{
			var entry = table + index * GroupBytes;
			var keyCount = U16( data, entry );
			var vertexCount = U16( data, entry + 2 );
			var isStatic = hasStatic && index == 1;
			if ( keyCount < (isStatic ? 1 : 2) )
				throw new InvalidDataException( $"MD2 vertex group {index} has {keyCount} keys." );
			if ( index == 0 && vertexCount < 2 )
				throw new InvalidDataException( "MD2 vertex bounds group needs two vertices." );
			var indices = new ushort[vertexCount];
			if ( vertexCount > 0 )
			{
				var at = pointer( U32( data, entry + 4 ), vertexCount * 2L, $"vertex group {index} indices" );
				for ( var vertex = 0; vertex < vertexCount; vertex++ )
					indices[vertex] = U16( data, at + vertex * 2 );
			}
			var ticks = new ushort[keyCount];
			var tickAt = pointer( U32( data, entry + 8 ), keyCount * 2L, $"vertex group {index} ticks" );
			for ( var key = 0; key < keyCount; key++ )
			{
				ticks[key] = U16( data, tickAt + key * 2 );
				if ( key > 0 && ticks[key] <= ticks[key - 1] )
					throw new InvalidDataException( $"MD2 vertex group {index} ticks do not increase." );
			}
			var packed = new uint[keyCount * vertexCount];
			if ( packed.Length > 0 )
			{
				var at = pointer( U32( data, entry + 12 ), packed.Length * 4L, $"vertex group {index} keys" );
				for ( var word = 0; word < packed.Length; word++ )
					packed[word] = U32( data, at + word * 4 );
			}
			groups[index] = new ModelVertexGroup( indices, ticks, packed );
		}
		var unread = new uint[] { U16( data, block + 4 ), U16( data, block + 6 ), U16( data, block + 8 ), U32( data, block + 16 ) };
		return new ModelVertexAnimation( flags, offset, scale, groups, unread );
	}

	private static NVector3 Finite( byte[] data, int offset, string what )
	{
		var value = new NVector3( F32( data, offset ), F32( data, offset + 4 ), F32( data, offset + 8 ) );
		if ( !float.IsFinite( value.X ) || !float.IsFinite( value.Y ) || !float.IsFinite( value.Z ) )
			throw new InvalidDataException( $"MD2 vertex animation {what} is not finite." );
		return value;
	}

	private static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
	private static float F32( byte[] data, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) );
}

/// <summary>Texture-frame key: from <see cref="Tick"/> on, the slot shows frame <see cref="Frame"/>.</summary>
public readonly record struct ModelTextureFrameKey( ushort Tick, ushort Frame );

/// <summary>
/// 8-byte texture-frame track from trailer +48 (count u16 at trailer +24):
/// <c>u16 slot, u16 count, ptr→count × (u16 tick, u16 frame)</c>. Slot and frame index the base
/// model's texture slots and their frame names; the parser does not check them against a model.
/// </summary>
public sealed record ModelTextureFrameTrack( ushort Slot, IReadOnlyList<ModelTextureFrameKey> Keys )
{
	/// <summary>
	/// SimThemePark 0xa4160: scanning from the last key backwards, the first key whose tick is at most
	/// the truncated time; null when none is, which leaves the slot's frame unchanged.
	/// </summary>
	public ushort? FrameAt( float time )
	{
		var tick = ModelAnimationTrack.TruncateTick( time );
		for ( var key = Keys.Count - 1; key >= 0; key-- )
		{
			if ( Keys[key].Tick <= tick )
				return Keys[key].Frame;
		}
		return null;
	}
}
