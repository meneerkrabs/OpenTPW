using System.Buffers.Binary;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Position keys of one track. <see cref="IsBezier"/> tracks (header word 0x12) store
/// <c>P0, (C, C, P)</c> per segment: <c>3 × keys − 2</c> points evaluated as cubic Bézier
/// segments. Linear tracks (header word 0x18) store one point per key. Times are ticks.
/// </summary>
public sealed record ModelPositionKeys( uint Kind, IReadOnlyList<uint> Times, IReadOnlyList<NVector3> Points )
{
	public const uint BezierKind = 0x12;
	public const uint LinearKind = 0x18;
	public bool IsBezier => Kind == BezierKind;
}

/// <summary>
/// 20-byte rotation key: tick, easing curve index for the segment that starts at this key
/// (-1 = plain slerp) and a unit quaternion (x, y, z, w) in <see cref="Quaternion"/> convention.
/// </summary>
public readonly record struct ModelRotationKey( ushort Time, short Ease, Quaternion Rotation );

/// <summary>16-byte scale key: tick, a u16 that is zero throughout the corpus, and per-axis scale.</summary>
public readonly record struct ModelScaleKey( ushort Time, ushort Unknown, NVector3 Scale );

/// <summary>
/// One 64-byte animation record bound to a base-model node. Position, rotation, scale, the
/// quantised vertex block (flag 0x1000 without 0x4000), the per-corner texture-coordinate tracks
/// (flag 0x10000) and the node-flag toggles (flag 0x20000) are decoded; the 12-byte block (flag
/// 0x4000) is parsed into <see cref="VertexPose"/> (one stored pose per tick, read by the PC sampler 0x471860), and the
/// path-parameter block of flag 0x200 into <see cref="PathParameters"/>, which moves a node along its parent dummy's
/// <see cref="ModelPath"/> (<see cref="TrySamplePath"/>). The bits in
/// <see cref="MetadataFlags"/> are never read by the original's animation code.
/// Sampling follows the record sampler of the Feral Mac build (SimThemePark 0xa4f68,
/// docs/reverse/PPC-formats.md, docs/reverse/ANIM-plan.md); negative times are outside the traced clock domain.
/// </summary>
public sealed class ModelAnimationTrack
{
	public const uint PositionFlag = 0x1;
	public const uint RotationFlags = 0x78;
	public const uint ScaleFlags = 0x180;
	public const uint VertexAnimationFlag = 0x1000;
	public const uint VertexLayoutFlag = 0x4000;
	public const uint NodeFlagToggleFlag = 0x20000;
	/// <summary>Per-corner texture-coordinate tracks at record +44 (sampler tail 0xa56d8 → 0xa83e8).</summary>
	public const uint UvAnimationFlag = 0x10000;
	/// <summary>Path parameter (0x200, <see cref="ModelPathParameters"/>) and path orientation (0x400), played by <see cref="TrySamplePath"/>.</summary>
	public const uint PathFlags = 0x600;
	public const uint PathParameterFlag = 0x200;
	public const uint PathOrientationFlag = 0x400;
	/// <summary>
	/// Exporter metadata: bits that no animation routine of the Mac build tests (the sampler
	/// 0xa4f68–0xa56f8, the bind 0xa3e24, the engine relocation and the clip bind 0xa5894; witness
	/// anim_witness.py). Each occurs only with a read bit: 0x10/0x20/0x40/0x8000 with 0x8, 0x100 with
	/// 0x80, 0x800 with 0x200 or 0x400, 0x2000 with 0x1000. They carry no payload and are not unsupported.
	/// </summary>
	public const uint MetadataFlags = 0x10 | 0x20 | 0x40 | 0x100 | 0x800 | 0x2000 | 0x8000;
	/// <summary>Node-state bit set or cleared by <see cref="NodeFlagToggles"/>.</summary>
	public const uint ToggledNodeStateBit = 0x10;

	public int NodeIndex { get; init; }
	public uint Flags { get; init; }
	/// <summary>Per-record duration in ticks; keys may end before or after it.</summary>
	public int Duration { get; init; }
	public ModelPositionKeys? Position { get; init; }
	public IReadOnlyList<ModelRotationKey> Rotations { get; init; } = Array.Empty<ModelRotationKey>();
	public IReadOnlyList<ModelScaleKey> Scales { get; init; } = Array.Empty<ModelScaleKey>();
	/// <summary>8-byte easing curves referenced by <see cref="ModelRotationKey.Ease"/>.</summary>
	public IReadOnlyList<byte[]> EaseCurves { get; init; } = Array.Empty<byte[]>();
	/// <summary>Quantised vertex block (record +40); null when absent or in the 12-byte layout.</summary>
	public ModelVertexAnimation? VertexAnimation { get; init; }
	/// <summary>The 12-byte block (record +40, flag 0x4000): one stored pose per tick; played by <see cref="ModelVertexPose.ApplyPose"/>.</summary>
	public ModelVertexPose? VertexPose { get; init; }
	/// <summary>Per-corner texture-coordinate tracks (record +44, flag 0x10000).</summary>
	public ModelUvAnimation? UvAnimation { get; init; }
	/// <summary>Path-parameter block (record +36, flag 0x200); see <see cref="TrySamplePath"/>.</summary>
	public ModelPathParameters? PathParameters { get; init; }
	/// <summary>Signed tick entries (record +48, count u16 +22) of a flag-0x20000 record.</summary>
	public IReadOnlyList<short> NodeFlagToggles { get; init; } = Array.Empty<short>();
	/// <summary>The raw 16 record words.</summary>
	public IReadOnlyList<uint> Words { get; init; } = Array.Empty<uint>();
	/// <summary>
	/// True when the record carries more than rigid position/rotation/scale tracks (it may still be
	/// decoded; see <see cref="UnsupportedFlags"/>). <see cref="MetadataFlags"/> do not count.
	/// </summary>
	public bool HasUndecodedPayload => (Flags & ~(PositionFlag | RotationFlags | ScaleFlags | MetadataFlags)) != 0;
	/// <summary>
	/// Flag bits whose payload this reader does not play. Only 0x400 with position keys (flag 0x1) remains: the original then
	/// turns the node along its position curve, which no corpus record does. <see cref="MetadataFlags"/> are not counted.
	/// </summary>
	public uint UnsupportedFlags => (Flags & ~(PositionFlag | RotationFlags | ScaleFlags | NodeFlagToggleFlag | UvAnimationFlag | MetadataFlags
		| VertexAnimationFlag | VertexLayoutFlag | PathFlags)) | ((Flags & (PathOrientationFlag | PositionFlag)) == (PathOrientationFlag | PositionFlag) ? PathOrientationFlag : 0);

	/// <summary>
	/// The node's place on <paramref name="path"/> at <paramref name="tick"/> (PC sampler 0x471860): false without a
	/// path-parameter block, with position keys (they take precedence) or outside the block's tick range, where the
	/// node keeps its translation. <paramref name="direction"/> is the unit direction to face with flag 0x400, else null.
	/// </summary>
	public bool TrySamplePath( ModelPath path, float tick, out NVector3 position, out NVector3? direction )
	{
		ArgumentNullException.ThrowIfNull( path );
		position = default;
		direction = null;
		if ( PathParameters == null || (Flags & PositionFlag) != 0 || PathParameters.Sample( tick, path.IsClosed ) is not { } percent
			|| !path.TryEvaluate( percent, out position, out var facing ) )
			return false;
		if ( (Flags & PathOrientationFlag) != 0 )
			direction = facing;
		return true;
	}
	/// <summary>The exporter-metadata bits of this record (<see cref="MetadataFlags"/>).</summary>
	public uint MetadataBits => Flags & MetadataFlags;

	/// <summary>
	/// Record sampler 0xa4f68: scanning from the last entry backwards, the first whose magnitude is at
	/// most the truncated time decides; a positive entry clears <see cref="ToggledNodeStateBit"/>
	/// (false), zero or negative sets it (true); null leaves it unchanged. What the bit controls
	/// was not traced (the entries are usually read as visibility toggles).
	/// </summary>
	public bool? SampleNodeFlag( float time )
	{
		var tick = TruncateTick( time );
		for ( var index = NodeFlagToggles.Count - 1; index >= 0; index-- )
		{
			// abs() of the sign-extended entry, compared unsigned: -32768 stays 0xFFFF8000.
			var magnitude = (uint)(int)(short)Math.Abs( (int)NodeFlagToggles[index] );
			if ( magnitude <= tick )
				return NodeFlagToggles[index] <= 0;
		}
		return null;
	}

	/// <summary>
	/// Position at a tick; null without a position track or before its first key (the original
	/// leaves the channel unchanged). In the corpus the last key is never before the clip end.
	/// </summary>
	public NVector3? SampleTranslation( float tick )
	{
		if ( Position == null )
			return null;
		var times = Position.Times;
		var points = Position.Points;
		var segment = FindKey( new PositionTimes( times ), tick, out var fraction );
		if ( segment < 0 )
			return null;
		if ( Position.IsBezier )
		{
			if ( fraction <= 0 )
				return points[segment * 3];
			var inverse = 1 - fraction;
			return inverse * inverse * inverse * points[segment * 3]
				+ 3 * inverse * inverse * fraction * points[segment * 3 + 1]
				+ 3 * inverse * fraction * fraction * points[segment * 3 + 2]
				+ fraction * fraction * fraction * points[segment * 3 + 3];
		}
		return fraction <= 0 ? points[segment] : NVector3.Lerp( points[segment], points[segment + 1], fraction );
	}

	/// <summary>
	/// Rotation at a tick; null without keys or before the first key. The final key holds
	/// (0xa820c pairs key i with min(i + 1, count − 1)). Interpolation is the sign-preserving slerp.
	/// The original takes a linear blend when global option bit 0x2 is set; the only store to that word
	/// writes 0 at setup (0xa7eec from 0x54c08), so it uses its table-sine slerp 0xa7fc8 (PC 0x4742A0), which
	/// <see cref="ModelRotationBlend"/> reproduces.
	/// </summary>
	public Quaternion? SampleRotation( float tick )
	{
		if ( Rotations.Count == 0 )
			return null;
		var segment = FindKey( new RotationTimes( Rotations ), tick, out var fraction );
		if ( segment < 0 )
			return null;
		var key = Rotations[segment];
		if ( fraction <= 0 )
			return key.Rotation;
		if ( key.Ease >= 0 )
			fraction = Ease( EaseCurves[key.Ease], fraction );
		return Slerp( key.Rotation, Rotations[segment + 1].Rotation, fraction );
	}

	/// <summary>
	/// The rotation keys <see cref="SampleRotation"/> blends at a tick, with the eased fraction, for a caller that blends them with its own arithmetic
	/// (simulation code, docs/DETERMINISM.md "Browser and native"); false where <see cref="SampleRotation"/> returns null. A fraction of 0 means <paramref name="from"/> holds.
	/// </summary>
	public bool TryRotationKeys( float tick, out Quaternion from, out Quaternion to, out float fraction )
	{
		from = to = Quaternion.Identity;
		fraction = 0;
		if ( Rotations.Count == 0 )
			return false;
		var segment = FindKey( new RotationTimes( Rotations ), tick, out fraction );
		if ( segment < 0 )
			return false;
		var key = Rotations[segment];
		from = to = key.Rotation;
		if ( fraction <= 0 )
		{
			fraction = 0;
			return true;
		}
		if ( key.Ease >= 0 )
			fraction = Ease( EaseCurves[key.Ease], fraction );
		to = Rotations[segment + 1].Rotation;
		return true;
	}

	/// <summary>
	/// The two position keys <see cref="SampleTranslation"/> blends linearly at a tick, for a caller that blends them with its own arithmetic; false where
	/// <see cref="SampleTranslation"/> returns null or the track is a Bézier one (which has no linear blend). A fraction of 0 means <paramref name="from"/> holds.
	/// </summary>
	public bool TryLinearTranslationKeys( float tick, out NVector3 from, out NVector3 to, out float fraction )
	{
		from = to = NVector3.Zero;
		fraction = 0;
		if ( Position == null || Position.IsBezier )
			return false;
		var segment = FindKey( new PositionTimes( Position.Times ), tick, out fraction );
		if ( segment < 0 )
			return false;
		from = to = Position.Points[segment];
		if ( fraction <= 0 )
		{
			fraction = 0;
			return true;
		}
		to = Position.Points[segment + 1];
		return true;
	}

	/// <summary>Per-axis scale at a tick; null without keys or before the first key; the last key holds.</summary>
	public NVector3? SampleScale( float tick )
	{
		if ( Scales.Count == 0 )
			return null;
		var segment = FindKey( new ScaleTimes( Scales ), tick, out var fraction );
		if ( segment < 0 )
			return null;
		if ( fraction <= 0 )
			return Scales[segment].Scale;
		// (1 − t) × a + (t × b), the second product rounded first (0xa4f68 fmuls/fmadds).
		var from = Scales[segment].Scale;
		var to = Scales[segment + 1].Scale;
		var inverse = 1 - fraction;
		return new NVector3( MathF.FusedMultiplyAdd( inverse, from.X, fraction * to.X ), MathF.FusedMultiplyAdd( inverse, from.Y, fraction * to.Y ),
			MathF.FusedMultiplyAdd( inverse, from.Z, fraction * to.Z ) );
	}

	/// <summary>Literal of SimThemePark 0xa50d4 (TOC 0x5198): the fraction is scaled by this, not by 9.</summary>
	public const float EaseScale = 8.999995f;

	/// <summary>
	/// Easing of 0xa50d4: <c>s = fraction × 8.999995</c>, segment <c>i = trunc(s)</c> (negative → 0),
	/// then linear between 0 and b0 (i = 0), b[i−1] and b[i] (1 ≤ i ≤ 7) or b7 and 1 (i ≥ 8), bytes
	/// divided by 255; so fraction 1 ends just short of 1.
	/// </summary>
	public static float Ease( byte[] curve, float fraction )
	{
		ArgumentNullException.ThrowIfNull( curve );
		if ( curve.Length < 8 )
			throw new ArgumentException( "An easing curve has eight samples.", nameof( curve ) );
		var scaled = fraction * EaseScale;
		var segment = TruncateTick( scaled );
		var local = scaled - segment;
		var from = segment == 0 ? 0f : segment < 8 ? curve[segment - 1] / 255f : curve[7] / 255f;
		var to = segment < 8 ? curve[segment] / 255f : 1f;
		return MathF.FusedMultiplyAdd( 1 - local, from, local * to );
	}

	/// <summary>Runtime double → unsigned conversion (0x1c3fbc) of a tick: truncates, negative → 0.</summary>
	internal static uint TruncateTick( float time )
	{
		if ( float.IsNaN( time ) )
			throw new ArgumentOutOfRangeException( nameof( time ) );
		return time <= 0 ? 0 : time >= uint.MaxValue ? uint.MaxValue : (uint)time;
	}

	/// <summary>
	/// The sampler's spherical interpolation without hemisphere flipping (the stored keys keep sign continuity, e.g. half-turn keys alternate w = 1 / -1, so flipping would reverse spin direction):
	/// the original's table-sine blend (<see cref="ModelRotationBlend"/>) with the platform's table and arc cosine.
	/// </summary>
	public static Quaternion Slerp( Quaternion from, Quaternion to, float fraction ) => ModelRotationBlend.Blend( from, to, fraction, ModelRotationBlend.PlatformTable, PlatformAcos );

	private static readonly Func<float, float> PlatformAcos = value => MathF.Acos( value );

	// Key search 0xa3ff0: the last key (scanning forward) whose tick is at most trunc(time), or -1
	// before the first key. Fraction is 0 at the last key, where every channel here holds.
	// The key times are read through a struct per key list instead of a delegate: the search runs for every track of every animated node on every tick, and a delegate over a captured list allocates on each call.
	private interface IKeyTimes
	{
		int Count { get; }
		uint TimeAt( int index );
	}

	private readonly struct PositionTimes( IReadOnlyList<uint> times ) : IKeyTimes
	{
		public int Count => times.Count;
		public uint TimeAt( int index ) => times[index];
	}

	private readonly struct RotationTimes( IReadOnlyList<ModelRotationKey> keys ) : IKeyTimes
	{
		public int Count => keys.Count;
		public uint TimeAt( int index ) => keys[index].Time;
	}

	private readonly struct ScaleTimes( IReadOnlyList<ModelScaleKey> keys ) : IKeyTimes
	{
		public int Count => keys.Count;
		public uint TimeAt( int index ) => keys[index].Time;
	}

	private static int FindKey<TTimes>( TTimes time, float tick, out float fraction ) where TTimes : struct, IKeyTimes
	{
		if ( !(tick >= 0) )
			throw new ArgumentOutOfRangeException( nameof( tick ), "Negative or NaN ticks are outside the traced clock domain." );
		var whole = TruncateTick( tick );
		var count = time.Count;
		var key = -1;
		while ( key + 1 < count && time.TimeAt( key + 1 ) <= whole )
			key++;
		fraction = 0;
		if ( key >= 0 && key + 1 < count )
		{
			float start = time.TimeAt( key );
			fraction = (tick - start) / (time.TimeAt( key + 1 ) - start);
		}
		return key;
	}
}

/// <summary>Decoded animation member: duration in ticks, per-node tracks and texture-frame tracks.</summary>
public sealed class ModelAnimation
{
	/// <summary>Trailer word 0 bit that enables <see cref="TextureFrameTracks"/> (clip update 0xa56f8).</summary>
	public const uint TextureFrameFlag = 0x2;

	public int Duration { get; init; }
	/// <summary>Trailer word 1, subtracted from <see cref="Duration"/> by the set-clip routine 0xa6398; 0 in every corpus trailer.</summary>
	public int FirstTick { get; init; }
	public IReadOnlyList<ModelAnimationTrack> Tracks { get; init; } = Array.Empty<ModelAnimationTrack>();
	/// <summary>Raw trailer word 0.</summary>
	public uint TrailerFlags { get; init; }
	/// <summary>
	/// True when the clip update applies <see cref="TextureFrameTracks"/>; the original also requires
	/// global option bit 0x8 to be clear, and the only store to that word writes 0 at setup.
	/// </summary>
	public bool TextureFramesEnabled => (TrailerFlags & TextureFrameFlag) != 0;
	public IReadOnlyList<ModelTextureFrameTrack> TextureFrameTracks { get; init; } = Array.Empty<ModelTextureFrameTrack>();
	/// <summary>
	/// u16 node-index list (trailer words 14 and 6). The clip start 0xa67d8 → 0xa65b8 sets node-state bit
	/// <see cref="ModelAnimationTrack.ToggledNodeStateBit"/> on each listed node whose state bit 31 is clear.
	/// </summary>
	public IReadOnlyList<ushort> NodeList { get; init; } = Array.Empty<ushort>();
	/// <summary>
	/// Payload bytes the decoder read as tables, counting a span again each time a pointer reuses it.
	/// Never more than the payload in the PC baseline and Patch 2 corpora (no table is shared).
	/// </summary>
	public long TableBytes { get; init; }

	/// <summary>
	/// OpenTPW resource limit, not original behaviour: tables may reuse payload spans, but all of them
	/// together may read at most this many times the payload (the original loader has no such check).
	/// Every table is charged before its array is allocated, so decoded arrays stay proportional to the
	/// member size.
	/// </summary>
	public const int TableBytesPerPayloadByte = 4;

	internal static ModelAnimation Decode( byte[] data, int trailer, uint[] words )
	{
		var budget = TableBytesPerPayloadByte * (long)(trailer - ModelFile.HeaderBytes);
		long charged = 0;

		// Checks one table against the payload and charges it before the caller allocates for it.
		int Pointer( uint value, long length, string what )
		{
			Probe( value, length, what );
			charged += length;
			if ( charged > budget )
				throw new InvalidDataException( $"MD2 animation tables exceed {TableBytesPerPayloadByte}× the payload at {what}." );
			return (int)value;
		}

		int Probe( uint value, long length, string what )
		{
			if ( value < ModelFile.HeaderBytes || value > trailer || length < 0 || length > trailer - (long)value )
				throw new InvalidDataException( $"MD2 animation {what} lies outside the payload." );
			return (int)value;
		}

		var trackCount = (int)(words[4] >> 16);
		var records = trackCount == 0 ? 0 : Pointer( words[11], trackCount * 64L, "record table" );
		var tracks = new ModelAnimationTrack[trackCount];
		long rotationKeys = 0, scaleKeys = 0, positionTracks = 0;
		for ( var index = 0; index < trackCount; index++ )
		{
			var record = records + index * 64;
			var raw = new uint[16];
			for ( var word = 0; word < raw.Length; word++ )
				raw[word] = U32( data, record + word * 4 );
			var flags = raw[1];
			if ( raw[0] != index )
				throw new InvalidDataException( $"MD2 animation record {index} has index {raw[0]}." );
			if ( ((flags & ModelAnimationTrack.PositionFlag) != 0) != (raw[6] != 0)
				|| ((flags & ModelAnimationTrack.RotationFlags) != 0) != (raw[7] != 0)
				|| ((flags & ModelAnimationTrack.ScaleFlags) != 0) != (raw[8] != 0) )
				throw new InvalidDataException( $"MD2 animation record {index} flags disagree with its key pointers." );
			for ( var word = 9; word <= 13; word++ )
			{
				if ( raw[word] != 0 )
					Probe( raw[word], 1, $"record {index} word {word}" );
			}

			ModelVertexAnimation? vertexAnimation = null;
			ModelVertexPose? vertexPose = null;
			if ( (flags & ModelAnimationTrack.VertexLayoutFlag) != 0 )
			{
				var block = Pointer( raw[10], ModelVertexPose.HeaderBytes, $"record {index} 12-byte block" );
				vertexPose = ModelVertexPose.Decode( data, block, Pointer, raw[3] + 1L );
			}
			else if ( (flags & ModelAnimationTrack.VertexAnimationFlag) != 0 )
			{
				var block = Pointer( raw[10], ModelVertexAnimation.HeaderBytes, $"record {index} vertex block" );
				vertexAnimation = ModelVertexAnimation.Decode( data, block, Pointer );
			}
			ModelPathParameters? pathParameters = null;
			if ( (flags & 0x200) != 0 )
			{
				if ( raw[9] == 0 )
					throw new InvalidDataException( $"MD2 animation record {index} has flag 0x200 without a path-parameter block." );
				var block = Pointer( raw[9], ModelPathParameters.HeaderBytes, $"record {index} path parameters" );
				pathParameters = ModelPathParameters.Decode( data, block, Pointer );
			}
			ModelUvAnimation? uvAnimation = null;
			if ( (flags & ModelAnimationTrack.UvAnimationFlag) != 0 )
			{
				var block = Pointer( raw[11], ModelUvAnimation.HeaderBytes, $"record {index} UV block" );
				uvAnimation = ModelUvAnimation.Decode( data, block, Pointer );
			}
			var toggleCount = (flags & ModelAnimationTrack.NodeFlagToggleFlag) != 0 ? (int)(raw[5] >> 16) : 0;
			var toggles = Array.Empty<short>();
			if ( toggleCount > 0 )
			{
				var offset = Pointer( raw[12], toggleCount * 2L, $"record {index} node-flag toggles" );
				toggles = new short[toggleCount];
				for ( var entry = 0; entry < toggles.Length; entry++ )
					toggles[entry] = BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( offset + entry * 2 ) );
			}

			ModelPositionKeys? position = null;
			if ( raw[6] != 0 )
			{
				var header = Pointer( raw[6], 16, "position header" );
				var kind = U32( data, header );
				var pointCount = U16( data, header + 4 );
				var keyCount = U16( data, header + 6 );
				var expected = kind switch
				{
					ModelPositionKeys.BezierKind => keyCount * 3 - 2,
					ModelPositionKeys.LinearKind => keyCount,
					_ => throw new InvalidDataException( $"MD2 animation position kind 0x{kind:X} is not supported." )
				};
				if ( keyCount == 0 || pointCount != expected )
					throw new InvalidDataException( $"MD2 animation record {index} position counts disagree." );
				var pointOffset = Pointer( U32( data, header + 8 ), pointCount * 12L, "position points" );
				var timeOffset = Pointer( U32( data, header + 12 ), keyCount * 4L, "position times" );
				var times = new uint[keyCount];
				for ( var key = 0; key < keyCount; key++ )
				{
					times[key] = U32( data, timeOffset + key * 4 );
					if ( key > 0 && times[key] < times[key - 1] )
						throw new InvalidDataException( $"MD2 animation record {index} position times decrease." );
				}
				var points = new NVector3[pointCount];
				for ( var point = 0; point < pointCount; point++ )
					points[point] = Finite( ReadVector( data, pointOffset + point * 12 ), "position" );
				position = new ModelPositionKeys( kind, times, points );
				positionTracks++;
			}

			var rotationCount = (int)(raw[4] & 0xFFFF);
			var scaleCount = (int)(raw[4] >> 16);
			if ( (raw[7] == 0 && rotationCount != 0) || (raw[8] == 0 && scaleCount != 0) )
				throw new InvalidDataException( $"MD2 animation record {index} counts keys without a key table." );
			var rotations = Array.Empty<ModelRotationKey>();
			var curveCount = 0;
			if ( rotationCount > 0 )
			{
				var offset = Pointer( raw[7], rotationCount * 20L, "rotation keys" );
				rotations = new ModelRotationKey[rotationCount];
				for ( var key = 0; key < rotationCount; key++ )
				{
					var entry = offset + key * 20;
					var rotation = new Quaternion( F32( data, entry + 4 ), F32( data, entry + 8 ), F32( data, entry + 12 ), F32( data, entry + 16 ) );
					var length = rotation.Length();
					if ( !float.IsFinite( length ) || MathF.Abs( length - 1 ) > 0.01f )
						throw new InvalidDataException( $"MD2 animation record {index} rotation key {key} is not a unit quaternion." );
					rotations[key] = new ModelRotationKey( U16( data, entry ), BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( entry + 2 ) ), rotation );
					if ( key > 0 && rotations[key].Time <= rotations[key - 1].Time )
						throw new InvalidDataException( $"MD2 animation record {index} rotation times do not increase." );
					if ( rotations[key].Ease < -1 )
						throw new InvalidDataException( $"MD2 animation record {index} rotation key {key} has an invalid easing index." );
					curveCount = Math.Max( curveCount, rotations[key].Ease + 1 );
				}
			}
			var curves = Array.Empty<byte[]>();
			if ( curveCount > 0 )
			{
				var offset = Pointer( raw[13], curveCount * 8L, "easing curves" );
				curves = new byte[curveCount][];
				for ( var curve = 0; curve < curveCount; curve++ )
					curves[curve] = data.AsSpan( offset + curve * 8, 8 ).ToArray();
			}
			var scales = Array.Empty<ModelScaleKey>();
			if ( scaleCount > 0 )
			{
				var offset = Pointer( raw[8], scaleCount * 16L, "scale keys" );
				scales = new ModelScaleKey[scaleCount];
				for ( var key = 0; key < scaleCount; key++ )
				{
					var entry = offset + key * 16;
					scales[key] = new ModelScaleKey( U16( data, entry ), U16( data, entry + 2 ), Finite( ReadVector( data, entry + 4 ), "scale" ) );
					if ( key > 0 && scales[key].Time <= scales[key - 1].Time )
						throw new InvalidDataException( $"MD2 animation record {index} scale times do not increase." );
				}
			}
			rotationKeys += rotationCount;
			scaleKeys += scaleCount;
			tracks[index] = new ModelAnimationTrack
			{
				NodeIndex = (int)(raw[5] & 0xFFFF),
				Flags = flags,
				Duration = (int)raw[3],
				Position = position,
				Rotations = rotations,
				Scales = scales,
				EaseCurves = curves,
				VertexAnimation = vertexAnimation,
				VertexPose = vertexPose,
				UvAnimation = uvAnimation,
				NodeFlagToggles = toggles,
				PathParameters = pathParameters,
				Words = raw
			};
		}
		if ( rotationKeys != (words[4] & 0xFFFF) || positionTracks != (words[3] & 0xFFFF) || scaleKeys != (words[3] >> 16) )
			throw new InvalidDataException( "MD2 animation key totals disagree with the trailer." );

		var nodeCount = (int)(words[6] >> 16);
		var nodeList = Array.Empty<ushort>();
		if ( nodeCount > 0 )
		{
			var offset = Pointer( words[14], nodeCount * 2L, "node list" );
			nodeList = new ushort[nodeCount];
			for ( var index = 0; index < nodeList.Length; index++ )
				nodeList[index] = U16( data, offset + index * 2 );
		}

		var frameTrackCount = (int)(words[6] & 0xFFFF);
		var frameTracks = Array.Empty<ModelTextureFrameTrack>();
		if ( frameTrackCount > 0 )
		{
			var table = Pointer( words[12], frameTrackCount * 8L, "texture-frame tracks" );
			frameTracks = new ModelTextureFrameTrack[frameTrackCount];
			for ( var index = 0; index < frameTracks.Length; index++ )
			{
				var entry = table + index * 8;
				var keyCount = U16( data, entry + 2 );
				var keys = Array.Empty<ModelTextureFrameKey>();
				if ( keyCount > 0 )
				{
					var offset = Pointer( U32( data, entry + 4 ), keyCount * 4L, $"texture-frame track {index} keys" );
					keys = new ModelTextureFrameKey[keyCount];
					for ( var key = 0; key < keys.Length; key++ )
						keys[key] = new ModelTextureFrameKey( U16( data, offset + key * 4 ), U16( data, offset + key * 4 + 2 ) );
				}
				frameTracks[index] = new ModelTextureFrameTrack( U16( data, entry ), keys );
			}
		}
		return new ModelAnimation
		{
			Duration = (int)words[2], FirstTick = (int)words[1], Tracks = tracks, NodeList = nodeList, TrailerFlags = words[0], TextureFrameTracks = frameTracks,
			TableBytes = charged
		};
	}

	private static NVector3 Finite( NVector3 value, string what )
	{
		if ( !float.IsFinite( value.X ) || !float.IsFinite( value.Y ) || !float.IsFinite( value.Z ) )
			throw new InvalidDataException( $"MD2 animation {what} key is not finite." );
		return value;
	}

	private static NVector3 ReadVector( byte[] data, int offset ) => new( F32( data, offset ), F32( data, offset + 4 ), F32( data, offset + 8 ) );
	private static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
	private static float F32( byte[] data, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) );
}

/// <summary>
/// Plays a decoded animation against its base model. Node matrices are parent-relative
/// (row-vector convention, world = local × parent world). Animated nodes replace the
/// corresponding translation/rotation/scale of their stored local matrix.
/// </summary>
public sealed class ModelAnimationPlayer
{
	private readonly ModelFile model;
	private readonly ModelAnimationTrack?[] trackByNode;
	private readonly (NVector3 Scale, Quaternion Rotation, NVector3 Translation)[] rest;
	private readonly int[] order;

	public ModelAnimation Animation { get; }
	public float TicksPerSecond { get; }
	public bool Loop { get; set; } = true;
	public float Tick { get; private set; }

	public ModelAnimationPlayer( ModelFile model, ModelAnimation animation, float ticksPerSecond )
	{
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( animation );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "Animations play against a geometry model.", nameof( model ) );
		if ( !float.IsFinite( ticksPerSecond ) || ticksPerSecond <= 0 )
			throw new ArgumentOutOfRangeException( nameof( ticksPerSecond ) );
		this.model = model;
		Animation = animation;
		TicksPerSecond = ticksPerSecond;
		trackByNode = new ModelAnimationTrack?[model.Nodes.Count];
		foreach ( var track in animation.Tracks )
		{
			if ( track.NodeIndex >= trackByNode.Length )
				throw new InvalidDataException( $"MD2 animation track targets node {track.NodeIndex}, but the model has {trackByNode.Length} nodes." );
			if ( trackByNode[track.NodeIndex] != null )
				throw new InvalidDataException( $"MD2 animation has several tracks for node {track.NodeIndex}." );
			trackByNode[track.NodeIndex] = track;
		}
		rest = new (NVector3, Quaternion, NVector3)[model.Nodes.Count];
		for ( var index = 0; index < rest.Length; index++ )
		{
			if ( !Matrix4x4.Decompose( model.Nodes[index].Transform, out var scale, out var rotation, out var translation ) )
			{
				scale = NVector3.One;
				rotation = Quaternion.Identity;
				translation = model.Nodes[index].Transform.Translation;
			}
			rest[index] = (scale, rotation, translation);
		}
		// Parents before children.
		var sorted = new List<int>( model.Nodes.Count );
		var pending = new Stack<int>();
		pending.Push( model.RootNodeIndex );
		var children = model.Nodes.ToLookup( node => node.ParentIndex, node => node.Index );
		while ( pending.Count > 0 )
		{
			var node = pending.Pop();
			sorted.Add( node );
			foreach ( var child in children[node] )
				pending.Push( child );
		}
		order = sorted.ToArray();
	}

	public void Reset() => Tick = 0;

	public void Advance( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( seconds ) );
		SetTick( Tick + seconds * TicksPerSecond );
	}

	public void SetTick( float tick )
	{
		if ( !float.IsFinite( tick ) || tick < 0 )
			throw new ArgumentOutOfRangeException( nameof( tick ) );
		var duration = Animation.Duration;
		Tick = duration <= 0 ? 0 : Loop ? tick % duration : Math.Min( tick, duration );
	}

	/// <summary>Local matrix of a node at the current tick (stored matrix when not animated).</summary>
	public Matrix4x4 LocalTransform( int node )
	{
		var track = trackByNode[node];
		if ( track == null )
			return model.Nodes[node].Transform;
		var (scale, rotation, translation) = rest[node];
		var sampledRotation = track.SampleRotation( Tick ) ?? rotation;
		var sampledTranslation = track.SampleTranslation( Tick ) ?? translation;
		// A flag-0x200 record sets the node on its parent dummy's path; with 0x400 its rows face along it (see TrySamplePath).
		if ( model.GetPathOf( node ) is { } path && track.TrySamplePath( path, Tick, out var onPath, out var direction ) )
		{
			if ( direction is { } along )
				return ModelPath.Facing( along, (track.Flags & 0x8) != 0 ? NVector3.Transform( NVector3.UnitY, sampledRotation ) : NVector3.UnitY )
					* Matrix4x4.CreateTranslation( onPath );
			sampledTranslation = onPath;
		}
		return Matrix4x4.CreateScale( track.SampleScale( Tick ) ?? scale )
			* Matrix4x4.CreateFromQuaternion( sampledRotation )
			* Matrix4x4.CreateTranslation( sampledTranslation );
	}

	/// <summary>Fills model-space matrices for every node at the current tick.</summary>
	public void ComputeWorldTransforms( Matrix4x4[] output )
	{
		ArgumentNullException.ThrowIfNull( output );
		if ( output.Length < model.Nodes.Count )
			throw new ArgumentException( "Output is smaller than the node count.", nameof( output ) );
		foreach ( var node in order )
		{
			var local = LocalTransform( node );
			var parent = model.Nodes[node].ParentIndex;
			output[node] = parent < 0 ? local : local * output[parent];
		}
	}

	/// <summary>Model-space matrices of the stored (bind) hierarchy.</summary>
	public static Matrix4x4[] ComputeRestTransforms( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		var output = new Matrix4x4[model.Nodes.Count];
		var done = new bool[output.Length];
		Matrix4x4 World( int node )
		{
			if ( done[node] )
				return output[node];
			var parent = model.Nodes[node].ParentIndex;
			output[node] = parent < 0 ? model.Nodes[node].Transform : model.Nodes[node].Transform * World( parent );
			done[node] = true;
			return output[node];
		}
		for ( var node = 0; node < output.Length; node++ )
			World( node );
		return output;
	}
}
