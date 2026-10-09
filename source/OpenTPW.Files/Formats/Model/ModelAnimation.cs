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
/// One 64-byte animation record bound to a base-model node. Only position, rotation and scale
/// are decoded; the other payload kinds (flag bits 0x600, 0x7000, 0x10000, 0x20000) are
/// identified by their pointers in <see cref="Words"/> but not interpreted.
/// </summary>
public sealed class ModelAnimationTrack
{
	public const uint PositionFlag = 0x1;
	public const uint RotationFlags = 0x78;
	public const uint ScaleFlags = 0x180;

	public int NodeIndex { get; init; }
	public uint Flags { get; init; }
	/// <summary>Per-record duration in ticks; keys may end before or after it.</summary>
	public int Duration { get; init; }
	public ModelPositionKeys? Position { get; init; }
	public IReadOnlyList<ModelRotationKey> Rotations { get; init; } = Array.Empty<ModelRotationKey>();
	public IReadOnlyList<ModelScaleKey> Scales { get; init; } = Array.Empty<ModelScaleKey>();
	/// <summary>8-byte easing curves referenced by <see cref="ModelRotationKey.Ease"/>.</summary>
	public IReadOnlyList<byte[]> EaseCurves { get; init; } = Array.Empty<byte[]>();
	/// <summary>The raw 16 record words.</summary>
	public IReadOnlyList<uint> Words { get; init; } = Array.Empty<uint>();
	/// <summary>True when the record carries payload kinds this reader does not decode.</summary>
	public bool HasUndecodedPayload => (Flags & ~(PositionFlag | RotationFlags | ScaleFlags)) != 0;

	public NVector3? SampleTranslation( float tick )
	{
		if ( Position == null )
			return null;
		var times = Position.Times;
		var points = Position.Points;
		var segment = FindSegment( times.Count, index => times[index], tick, out var fraction );
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

	public Quaternion? SampleRotation( float tick )
	{
		if ( Rotations.Count == 0 )
			return null;
		var segment = FindSegment( Rotations.Count, index => Rotations[index].Time, tick, out var fraction );
		var key = Rotations[segment];
		if ( fraction <= 0 )
			return key.Rotation;
		if ( key.Ease >= 0 )
			fraction = Ease( EaseCurves[key.Ease], fraction );
		return Slerp( key.Rotation, Rotations[segment + 1].Rotation, fraction );
	}

	public NVector3? SampleScale( float tick )
	{
		if ( Scales.Count == 0 )
			return null;
		var segment = FindSegment( Scales.Count, index => Scales[index].Time, tick, out var fraction );
		return fraction <= 0 ? Scales[segment].Scale : NVector3.Lerp( Scales[segment].Scale, Scales[segment + 1].Scale, fraction );
	}

	/// <summary>
	/// Maps a segment fraction through an easing curve: the eight bytes are the slerp fraction
	/// ×255 at s = 1/9 … 8/9, with implied end points 0 and 1; values in between are linear.
	/// </summary>
	public static float Ease( byte[] curve, float fraction )
	{
		var position = Math.Clamp( fraction, 0f, 1f ) * 9;
		var index = Math.Min( (int)position, 8 );
		var from = index == 0 ? 0f : curve[index - 1] / 255f;
		var to = index == 8 ? 1f : curve[index] / 255f;
		return from + (to - from) * (position - index);
	}

	/// <summary>
	/// Spherical interpolation without hemisphere flipping: the stored keys keep sign continuity
	/// (e.g. half-turn keys alternate w = 1 / -1), so flipping would reverse spin direction.
	/// </summary>
	public static Quaternion Slerp( Quaternion from, Quaternion to, float fraction )
	{
		var dot = Math.Clamp( Quaternion.Dot( from, to ), -1f, 1f );
		var angle = MathF.Acos( dot );
		var sine = MathF.Sin( angle );
		Quaternion result;
		if ( sine < 1e-5f )
			result = Quaternion.Lerp( from, to, fraction );
		else
			result = Quaternion.Multiply( from, MathF.Sin( (1 - fraction) * angle ) / sine ) + Quaternion.Multiply( to, MathF.Sin( fraction * angle ) / sine );
		return Quaternion.Normalize( result );
	}

	// Returns the segment start key; fraction is 0 when the tick is clamped to a key.
	private static int FindSegment( int count, Func<int, uint> time, float tick, out float fraction )
	{
		fraction = 0;
		if ( count == 1 || tick <= time( 0 ) )
			return 0;
		if ( tick >= time( count - 1 ) )
			return count - 1;
		var segment = 0;
		while ( segment + 1 < count - 1 && tick >= time( segment + 1 ) )
			segment++;
		var start = time( segment );
		var end = time( segment + 1 );
		fraction = end > start ? (tick - start) / (end - start) : 0;
		return segment;
	}
}

/// <summary>Decoded animation member: duration in ticks and per-node tracks.</summary>
public sealed class ModelAnimation
{
	public int Duration { get; init; }
	public IReadOnlyList<ModelAnimationTrack> Tracks { get; init; } = Array.Empty<ModelAnimationTrack>();
	/// <summary>Opaque u16 node-index list (trailer words 14 and 6); meaning unknown.</summary>
	public IReadOnlyList<ushort> NodeList { get; init; } = Array.Empty<ushort>();

	internal static ModelAnimation Decode( byte[] data, int trailer, uint[] words )
	{
		int Pointer( uint value, long length, string what )
		{
			if ( value < ModelFile.HeaderBytes || value > trailer || length > trailer - (long)value )
				throw new InvalidDataException( $"MD2 animation {what} lies outside the payload." );
			return (int)value;
		}

		var trackCount = (int)(words[4] >> 16);
		var tracks = new ModelAnimationTrack[trackCount];
		var records = trackCount == 0 ? 0 : Pointer( words[11], trackCount * 64L, "record table" );
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
					Pointer( raw[word], 1, $"record {index} word {word}" );
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
			var rotations = new ModelRotationKey[rotationCount];
			var curveCount = 0;
			if ( rotationCount > 0 )
			{
				var offset = Pointer( raw[7], rotationCount * 20L, "rotation keys" );
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
			var curves = new byte[curveCount][];
			if ( curveCount > 0 )
			{
				var offset = Pointer( raw[13], curveCount * 8L, "easing curves" );
				for ( var curve = 0; curve < curveCount; curve++ )
					curves[curve] = data.AsSpan( offset + curve * 8, 8 ).ToArray();
			}
			var scales = new ModelScaleKey[scaleCount];
			if ( scaleCount > 0 )
			{
				var offset = Pointer( raw[8], scaleCount * 16L, "scale keys" );
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
				Words = raw
			};
		}
		if ( rotationKeys != (words[4] & 0xFFFF) || positionTracks != (words[3] & 0xFFFF) || scaleKeys != (words[3] >> 16) )
			throw new InvalidDataException( "MD2 animation key totals disagree with the trailer." );

		var nodeList = new ushort[words[6] >> 16];
		if ( nodeList.Length > 0 )
		{
			var offset = Pointer( words[14], nodeList.Length * 2L, "node list" );
			for ( var index = 0; index < nodeList.Length; index++ )
				nodeList[index] = U16( data, offset + index * 2 );
		}
		return new ModelAnimation { Duration = (int)words[2], Tracks = tracks, NodeList = nodeList };
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
		return Matrix4x4.CreateScale( track.SampleScale( Tick ) ?? scale )
			* Matrix4x4.CreateFromQuaternion( track.SampleRotation( Tick ) ?? rotation )
			* Matrix4x4.CreateTranslation( track.SampleTranslation( Tick ) ?? translation );
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
