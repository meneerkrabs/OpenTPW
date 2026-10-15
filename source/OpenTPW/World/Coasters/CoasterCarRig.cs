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

/// <summary>
/// What a car type contributes to its train (car type record <c>+0x24</c>, <c>+0x28</c>, <c>+0x30</c>, set by <c>0x430130</c>): the distance
/// from the car's pivot to its front and to its rear in world units, each with the type's distance add, and its head sockets (seats).
/// </summary>
// [BIN:TPW-EXE:0x430130 car type metrics] +0x24 = fFrontDistanceAdd + (model bounds max Z − root node Z), +0x28 = fRearDistanceAdd − (bounds min Z − root node Z) with the bounds of the MD2 header (+0x80..+0x94) and the root node's translation Z (node +0x48); +0x30 = the number of dummy attribute records with flag 0x80 (0x44b2b0); a type whose model does not load keeps the zero-initialised fields (no extent beyond nothing, no seat)
public readonly record struct CoasterCarMetrics( float FrontExtent, float RearExtent, int Heads );

/// <summary>A car pose on the track: the point, the yaw and pitch of its direction and the roll.</summary>
public readonly record struct CoasterCarPose( NVector3 Position, float Yaw, float Pitch, float Roll );

/// <summary>
/// A train car model (an MD2 such as <c>CrocCar.MD2</c>, <c>cart.MD2</c> or <c>ape.MD2</c>) baked at its rest pose around its root node: the mesh parts, the extents
/// along the car (the model's forward axis is +Z, the car's front is its maximum Z) and the head sockets of its seats.
/// </summary>
// [BIN:TPW-EXE:0x43ce20 car pose] each frame a car stands at the curve point of its coordinate (the train's coordinate plus the car's offset over the sample spacing, CoasterRide.CarPose, 0x43c0a0) with the yaw, pitch and roll of that point turned into a matrix (0x43d8f0: roll, then pitch, then yaw) and the car type's fYOffset added along the turned up axis; the car's own wheel animation runs at +0x1c times the train speed
// [BIN:TPW-EXE:0x43d8f0 car matrix] the car's model root node gets the rotation roll (+0xc), then pitch (+8), then yaw (+4) from identity, so the model's +Z runs along the track, and the root's translation (+0x40..+0x48) the car point plus the turned fYOffset (0x43d6b2)
// [APPROX:COAST-034] cars are drawn as their static rest mesh: the bend of bBendCar types (0x438800 with the angle tables of 0x438ab0: every vertex of the car's mesh, at its local Z over the samples around the car, takes the yaw and pitch of the track there relative to the car's and rises by fBendElevationLow, or fBendElevationHigh on flag-8 samples) and the model clip of slot 0 (set up by 0x43be10 and played at the car type's +0x1c × the train speed through 0x474050, on non-bend cars only) are not reproduced
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
		// [BIN:TPW-EXE:0x4301f5 car head sockets] the car type's socket j is the node of the (j + 1)-th attribute record with flag 0x80 (0x44b270(model, 0x80, j + 1), the record index; record i belongs to the node stored as header +0x46 + i, 0x44ab90), in record order whatever the records' values
		var seats = new List<(int Node, Matrix4x4 Local)>();
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			if ( (model.DummyAttributes[index].Type & RideHeadSpace.HeadFlag) == 0 )
				continue;
			var stored = model.Counts.Unknown46 + index;
			var node = model.Nodes.FirstOrDefault( candidate => candidate.StoredIndex == stored )?.Index ?? -1;
			if ( node >= 0 )
				seats.Add( (node, rest[node] * shift) );
		}
		Seats = seats;
		// [BIN:TPW-EXE:0x430130 car extents] the header bounds and the root node's local translation (the original reads the loaded header, +0x80..+0x94, and [+0x78]+0x48)
		var rootZ = model.RootNodeIndex >= 0 ? model.Nodes[model.RootNodeIndex].Transform.M43 : 0f;
		ModelFrontExtent = model.BoundsMax.Z - rootZ;
		ModelRearExtent = rootZ - model.BoundsMin.Z;
		// [BIN:TPW-EXE:0x44b2b0 head count] the dummy attribute records whose flag word has bit 0x80
		HeadSockets = model.DummyAttributes.Count( attribute => (attribute.Type & RideHeadSpace.HeadFlag) != 0 );
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
	/// <summary>The MD2 header's maximum Z minus the root node's Z: how far the model reaches in front of its pivot (<c>0x430130</c>).</summary>
	public float ModelFrontExtent { get; }
	/// <summary>The root node's Z minus the MD2 header's minimum Z: how far the model reaches behind its pivot.</summary>
	public float ModelRearExtent { get; }
	/// <summary>The number of head sockets (dummy attribute records with flag 0x80): the seats a car of this model offers.</summary>
	public int HeadSockets { get; }
	/// <summary>The car type's contribution to its train: the model's extents plus the type's distance adds, and the head sockets.</summary>
	public CoasterCarMetrics Metrics( CoasterCarType type ) => new( type.FrontAdd + ModelFrontExtent, type.RearAdd + ModelRearExtent, HeadSockets );
	/// <summary>The head sockets in head-id order: the model node and its matrix relative to the pivot (MD2 axes).</summary>
	public IReadOnlyList<(int Node, Matrix4x4 Local)> Seats { get; }
	/// <summary>The car model's camera seat (<see cref="RideSeat"/>) relative to the pivot in MD2 axes, null when the model has none.</summary>
	// [BIN:TPW-EXE:0x42a560 ride camera] a coaster's ride camera takes the node the seat lookup 0x44b220(model, 0x1000, 1) finds in the car model (coasta/megacost cart, minecart cart node 5 "camera")
	public Matrix4x4? CameraSeat { get; }

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
