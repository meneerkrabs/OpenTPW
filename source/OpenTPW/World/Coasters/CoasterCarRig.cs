using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One car type (<c>asCarTypes[i]</c>): the mesh name, the distances added in front of and behind the model's extent, the height offset and the bend flag.</summary>
public sealed record CoasterCarType( string MeshName, float FrontAdd, float RearAdd, float YOffset, bool Bend );

/// <summary>The car types of a coaster and which of them its trains use at the front, in the middle and at the rear (<c>sTrainType</c>).</summary>
// [BIN:TPW-EXE:0x42f1a0 car type setup] each car type record (0x34 bytes) holds the bend flag, the unique id, the mesh name, fFrontDistanceAdd (+0x10), fRearDistanceAdd (+0x14), fYOffset (+0x18) and a default 0.1 (+0x1c, the wheel animation speed factor); the train's front, centre and rear type indices select among them (definition +0x2fc, +0x300, +0x304)
// [BIN:TPW-EXE:0x430130 car extents] a car type's front extent (+0x24) is fFrontDistanceAdd plus the model bounds' maximum Z minus the root node's Z, its rear extent (+0x28) fRearDistanceAdd plus the root node's Z minus the minimum Z; the seat count (+0x30) is the number of head sockets of the model ("Car type %s can receive %d heads")
public sealed class CoasterCarTypes
{
	private static readonly Regex CarKey = new( @"^asCarTypes\[(\d+)\]\.(\w+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );

	public IReadOnlyList<CoasterCarType> Types { get; private set; } = Array.Empty<CoasterCarType>();
	public int FrontIndex { get; private set; }
	public int CentreIndex { get; private set; }
	public int RearIndex { get; private set; }

	public static CoasterCarTypes FromSettings( ObjectSettings settings ) => FromValues( settings.Values );

	public static CoasterCarTypes FromValues( IReadOnlyDictionary<string, string> values )
	{
		ArgumentNullException.ThrowIfNull( values );
		var types = new SortedDictionary<int, (string Mesh, float Front, float Rear, float Y, bool Bend)>();
		var result = new CoasterCarTypes();
		foreach ( var (key, value) in values )
		{
			if ( CarKey.Match( key ) is { Success: true } match )
			{
				var index = int.Parse( match.Groups[1].Value, CultureInfo.InvariantCulture );
				types.TryGetValue( index, out var entry );
				switch ( match.Groups[2].Value.ToLowerInvariant() )
				{
					case "pcmeshfilename": entry.Mesh = value.Trim().Trim( '"' ); break;
					case "ffrontdistanceadd": entry.Front = Float( value ); break;
					case "freardistanceadd": entry.Rear = Float( value ); break;
					case "fyoffset": entry.Y = Float( value ); break;
					case "bbendcar": entry.Bend = Float( value ) != 0; break;
				}
				types[index] = entry;
			}
			else if ( string.Equals( key, "sTrainType.uiFrontCarTypeIndex", StringComparison.OrdinalIgnoreCase ) )
				result.FrontIndex = (int)Float( value );
			else if ( string.Equals( key, "sTrainType.uiCentreCarTypeIndex", StringComparison.OrdinalIgnoreCase ) )
				result.CentreIndex = (int)Float( value );
			else if ( string.Equals( key, "sTrainType.uiRearCarTypeIndex", StringComparison.OrdinalIgnoreCase ) )
				result.RearIndex = (int)Float( value );
		}
		if ( types.Count > 0 )
		{
			var list = new CoasterCarType[Math.Min( types.Keys.Max() + 1, 16 )];
			Array.Fill( list, new CoasterCarType( "", 0, 0, 0, false ) );
			foreach ( var (index, entry) in types )
			{
				if ( index < list.Length )
					list[index] = new CoasterCarType( entry.Mesh ?? "", entry.Front, entry.Rear, entry.Y, entry.Bend );
			}
			result.Types = list;
		}
		return result;
	}

	/// <summary>The car type index of car <paramref name="car"/> of a train of <paramref name="count"/> cars: the first car is a front car, the last a rear car, the others centre cars.</summary>
	// [BIN:TPW-EXE:0x42fad0 train length] the loader walks the cars 0..n−1 and takes the front car type for car 0, the rear car type for car n−1 and the centre car type for the others
	public int TypeIndexFor( int car, int count ) => car == 0 ? FrontIndex : car == count - 1 ? RearIndex : CentreIndex;

	private static float Float( string text ) => float.TryParse( text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) ? value : 0;
}

/// <summary>A car pose on the track: the point, the yaw and pitch of its direction and the roll.</summary>
public readonly record struct CoasterCarPose( NVector3 Position, float Yaw, float Pitch, float Roll );

/// <summary>
/// A train car model (an MD2 such as <c>CrocCar.MD2</c>, <c>cart.MD2</c> or <c>ape.MD2</c>) baked at its rest pose around its root node: the mesh parts, the extents
/// along the car (the model's forward axis is +Z, the car's front is its maximum Z) and the head sockets of its seats.
/// </summary>
// [BIN:TPW-EXE:0x43ce20 car pose] each frame a car stands at the curve point of its coordinate (the train's coordinate plus the car's offset over the sample spacing, interpolated between two samples, 0x43c0a0) with the yaw, pitch and roll of that point turned into a matrix (0x43d8f0: roll, then pitch, then yaw) and the car type's fYOffset added along the turned up axis; the car's own wheel animation runs at +0x1c times the train speed
// [APPROX:COAST-034] cars are posed at the interpolated sample point with the sample direction and bank, spaced by their model extents plus the car type's distance adds, and drawn as the static rest mesh: the bend of flume cars (bBendCar with fBendElevationLow/High), the wheel and head animation clips (M1..M3) and the train's front-car reference are not reproduced, and the model's forward axis is taken as +Z — evidence needed: the car matrix consumer of node +0x78 (0x474050) and the bend-car pose
public sealed class CoasterCarRig
{
	public CoasterCarRig( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "A car needs a geometry model.", nameof( model ) );
		Model = model;
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		Pivot = model.RootNodeIndex >= 0 ? rest[model.RootNodeIndex].Translation : NVector3.Zero;
		var shift = Matrix4x4.CreateTranslation( -Pivot );
		var parts = new List<CoasterModelPart>();
		var maxZ = float.MinValue;
		var minZ = float.MaxValue;
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is { } reason && reason != "more than 16 texture slots" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			var bake = (node >= 0 ? rest[node] : Matrix4x4.Identity) * shift;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
			{
				foreach ( var vertex in vertices )
				{
					// Engine axis order: Y is the model's forward Z.
					var z = vertex.Position.GetSystemVector3().Y;
					maxZ = MathF.Max( maxZ, z );
					minZ = MathF.Min( minZ, z );
				}
				parts.Add( new CoasterModelPart( mesh.Name, textures, vertices, indices ) );
			}
		}
		Parts = parts;
		FrontExtent = parts.Count == 0 ? 0 : MathF.Max( maxZ, 0 );
		RearExtent = parts.Count == 0 ? 0 : MathF.Max( -minZ, 0 );
		var seats = new List<(int Node, Matrix4x4 Local)>();
		for ( var id = 1; id < 64; id++ )
		{
			var node = RideHeadSpace.NodeForHead( model, id );
			if ( node == RideHeadSpace.NoRecord )
				break;
			if ( node >= 0 )
				seats.Add( (node, rest[node] * shift) );
		}
		Seats = seats;
		var seat = RideSeat.FindNode( model );
		CameraSeat = seat >= 0 ? rest[seat] * shift : null;
	}

	public ModelFile Model { get; }
	/// <summary>The root node's position in the model (subtracted so the car's origin is its pivot).</summary>
	public NVector3 Pivot { get; }
	public IReadOnlyList<CoasterModelPart> Parts { get; }
	/// <summary>Distance from the pivot to the car's front (maximum Z) in model units.</summary>
	public float FrontExtent { get; }
	/// <summary>Distance from the pivot to the car's rear (minimum Z) in model units.</summary>
	public float RearExtent { get; }
	/// <summary>The head sockets in head-id order: the model node and its matrix relative to the pivot (MD2 axes).</summary>
	public IReadOnlyList<(int Node, Matrix4x4 Local)> Seats { get; }
	/// <summary>The car model's camera seat (<see cref="RideSeat"/>) relative to the pivot in MD2 axes, null when the model has none.</summary>
	// [BIN:TPW-EXE:0x42a560 ride camera] a coaster's ride camera takes the node the seat lookup 0x44b220(model, 0x1000, 1) finds in the car model (coasta/megacost cart, minecart cart node 5 "camera")
	public Matrix4x4? CameraSeat { get; }

	/// <summary>
	/// The distance from the front car's pivot back to each car's pivot: the previous car's rear extent plus the car's front extent (each with the type's distance add).
	/// </summary>
	// [BIN:TPW-EXE:0x42fad0 car spacing] the loader sums the rear extent of the car before and the front extent of the car itself between neighbouring cars (definition +0x30c..+0x314 for front, centre and rear pairs) and adds one such distance per car to the train length
	public static float[] CarOffsets( IReadOnlyList<(float Front, float Rear)> extents )
	{
		var offsets = new float[extents.Count];
		for ( var car = 1; car < offsets.Length; car++ )
			offsets[car] = offsets[car - 1] + extents[car - 1].Rear + extents[car].Front;
		return offsets;
	}

	/// <summary>The pose at a position in samples of a closed track: the point interpolated between the two samples, the direction and bank interpolated the same way.</summary>
	public static CoasterCarPose PoseAt( IReadOnlyList<CoasterSample> samples, float position )
	{
		if ( samples.Count == 0 )
			return default;
		var count = samples.Count;
		var wrapped = position % count;
		if ( wrapped < 0 )
			wrapped += count;
		var index = Math.Min( (int)wrapped, count - 1 );
		var fraction = wrapped - index;
		var a = samples[index];
		var b = samples[(index + 1) % count];
		var point = NVector3.Lerp( a.Position, b.Position, fraction );
		var direction = NVector3.Lerp( a.Direction, b.Direction, fraction );
		if ( direction.LengthSquared() < 1e-8f )
			direction = a.Direction;
		direction = NVector3.Normalize( direction );
		var yaw = MathF.Atan2( direction.X, direction.Z );
		var pitch = MathF.Atan2( -direction.Y, MathF.Sqrt( direction.X * direction.X + direction.Z * direction.Z ) );
		return new CoasterCarPose( point, yaw, pitch, a.Bank + (b.Bank - a.Bank) * fraction );
	}

	/// <summary>
	/// The car-to-world matrix (row vectors, original axes with Y up): the pose's right, up and forward axes, with the car type's height offset added along the up axis.
	/// </summary>
	public static Matrix4x4 Matrix( CoasterCarPose pose, float yOffset )
	{
		var (right, up) = CoasterTrackMesh.Frame( pose.Yaw, pose.Pitch, pose.Roll );
		var forward = NVector3.Cross( right, up );
		var position = pose.Position + up * yOffset;
		return new Matrix4x4( right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, position.X, position.Y, position.Z, 1 );
	}
}
