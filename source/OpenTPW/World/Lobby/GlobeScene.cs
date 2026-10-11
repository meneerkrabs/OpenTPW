using OpenTPW.Online.Api;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The online world's globe (<c>lobby/globe/globe.MD2</c>), shown behind the Online World screen instead of the lobby
/// islands, with a pin for every city of the server (docs/ONLINE.md). It is placed far above the lobby, so the lobby
/// never shows behind it; the <see cref="GlobeCameraMode"/> circles it.
/// </summary>
public sealed class GlobeScene : OriginalModelScene
{
	private const string GlobeDirectory = "/lobby/globe";
	/// <summary>Engine-space centre of the globe, far above the lobby terrain.</summary>
	public static readonly Vector3 Centre = new( 400, 400, 6000 );
	// [BIN:TPW-EXE:0x005DC860 globe pick] the cursor ray meets a sphere of radius 116 (r² 13456); a city within 10 units (d² < 100) of that point is hovered
	public const float PickRadius = 116f;
	public const float PickDistance = 10f;
	// [APPROX:ONLINE-013] pins are drawn twice the decoded (s, s, p) scale and stand on the mesh surface (radius 119.7)
	// rather than the pick sphere (116); at the decoded size they barely clear the surface — evidence needed: the
	// scale of the original flag node's parent (globe node) and a capture of the original globe
	public const float PinScale = 2f;
	public const float PinRadius = 119.7f;
	// [BIN:TPW-EXE:0x005DC3E0 city flag] the city named "North Pole" gets the Bullfrog pin, every other city the hpin model
	public const string BullfrogCity = "North Pole";

	private readonly List<ModelEntity> pins = new();
	private readonly List<(CityInfo City, NVector3 Point)> cityPoints = new();
	private ModelFile? cityPin;
	private ModelFile? bullfrogPin;

	public GlobeScene()
	{
		try
		{
			AddModel( new ModelFile( $"{GlobeDirectory}/globe.MD2" ), NMatrix4x4.Identity );
			Place( parts );
		}
		catch
		{
			Delete();
			throw;
		}
	}

	protected override string TextureDirectory => $"{GlobeDirectory}/textures";

	public IReadOnlyList<CityInfo> Cities { get; private set; } = Array.Empty<CityInfo>();

	/// <summary>
	/// Direction of a geographic position in the globe model's (MD2) space. The 24 globe textures form an
	/// equirectangular world map in six 60° columns from 180° W and four 45° rows from the north; the model's pole is
	/// its +Y axis and longitude L lies at atan2(x, z) = 30° − L.
	/// </summary>
	// [DATA:lobby.wad:globe/globe.MD2 + Lo_glo1–24] tile i covers column (i − 1) mod 6 and row (i − 1) div 6 of a world map; vertex UVs put column c at atan2(x, z) = 180° − 60°·c and row r between 90° − 45°·r and 45° − 45°·r latitude
	public static NVector3 Direction( float latitude, float longitude )
	{
		var phi = latitude * MathF.PI / 180f;
		var theta = (30f - longitude) * MathF.PI / 180f;
		return new NVector3( MathF.Sin( theta ) * MathF.Cos( phi ), MathF.Sin( phi ), MathF.Cos( theta ) * MathF.Cos( phi ) );
	}

	/// <summary>Replaces the pins with one per city.</summary>
	public void SetCities( IReadOnlyList<CityInfo> cities )
	{
		if ( ReferenceEquals( cities, Cities ) )
			return;
		Cities = cities;
		foreach ( var pin in pins )
		{
			RemovePart( pin );
		}
		pins.Clear();
		cityPoints.Clear();
		foreach ( var city in cities )
		{
			try
			{
				var bullfrog = string.Equals( city.Name, BullfrogCity, StringComparison.OrdinalIgnoreCase );
				var model = bullfrog ? bullfrogPin ??= new ModelFile( $"{GlobeDirectory}/Bullfrog.MD2" ) : cityPin ??= new ModelFile( $"{GlobeDirectory}/Hpin.MD2" );
				var direction = Direction( city.Latitude, city.Longitude );
				var added = AddModel( model, PinPlacement( direction, city.Parks ) );
				Place( added );
				pins.AddRange( added );
				cityPoints.Add( (city, ToEngine( direction * PickRadius )) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
			{
				Log?.Warning( $"Globe pin for {city.Name}: {exception.Message}" );
			}
		}
		Visible = Visible;
	}

	/// <summary>
	/// A pin standing on the globe at <paramref name="direction"/>: radial height grows with the city's parks, in the
	/// frame right = up × direction, forward = direction × right.
	/// </summary>
	// [BIN:TPW-EXE:0x005DC3E0 city flag] scale (s, s, p) with p = clamp(0.02 + 0.0005·parks, 0.02, 0.1) and s = 0.035 + 0.2·p, at the city position, oriented along its direction from the globe centre
	public static NMatrix4x4 PinPlacement( NVector3 direction, int parks )
	{
		var p = Math.Clamp( 0.02f + 0.0005f * parks, 0.02f, 0.1f );
		var s = 0.035f + 0.2f * p;
		var right = NVector3.Cross( NVector3.UnitY, direction );
		right = right.LengthSquared() < 1e-6f ? NVector3.UnitX : NVector3.Normalize( right );
		var forward = NVector3.Normalize( NVector3.Cross( direction, right ) );
		// After their own node transform the pin models stand along +Y (head up, point down), so +Y is the radial axis
		// and the decoded radial scale p applies to it.
		var frame = new NMatrix4x4(
			right.X, right.Y, right.Z, 0,
			direction.X, direction.Y, direction.Z, 0,
			forward.X, forward.Y, forward.Z, 0,
			0, 0, 0, 1 );
		return NMatrix4x4.CreateScale( s * PinScale, p * PinScale, s * PinScale ) * frame * NMatrix4x4.CreateTranslation( direction * PinRadius );
	}

	/// <summary>The city under the cursor ray from the camera, if any.</summary>
	public CityInfo? Pick( NVector3 origin, NVector3 direction )
	{
		var centre = Centre.GetSystemVector3();
		var toOrigin = origin - centre;
		var b = NVector3.Dot( toOrigin, direction );
		var c = toOrigin.LengthSquared() - PickRadius * PickRadius;
		var discriminant = b * b - c;
		if ( discriminant < 0 )
			return null;
		var distance = -b - MathF.Sqrt( discriminant );
		if ( distance < 0 )
			return null;
		var hit = origin + direction * distance - centre;
		CityInfo? best = null;
		var bestDistance = PickDistance * PickDistance;
		foreach ( var (city, point) in cityPoints )
		{
			var squared = NVector3.DistanceSquared( hit, point );
			if ( squared < bestDistance )
			{
				best = city;
				bestDistance = squared;
			}
		}
		return best;
	}

	private void Place( IEnumerable<ModelEntity> placed )
	{
		var transform = NMatrix4x4.CreateTranslation( Centre.GetSystemVector3() );
		// [BIN:TPW-EXE:0x005D9230 globe light] dim grey ambient and one white light; OpenTPW puts the light behind the camera's start
		var light = Centre + new Vector3( 1500, 3000, 1500 );
		foreach ( var part in placed )
		{
			part.TransformOverride = transform;
			part.LightPosition = light;
			// The park's distance fog would hide the globe at its camera distance; the original globe has none.
			part.ViewDistanceScale = 1000;
		}
	}
}
