using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Draws the flying cars of a level's tour rides (docs/TOURS.md): every live car of every controller is the ride's supplemental mesh 0 (the bird, balloon, craft or kite),
/// placed at the car's position with its heading, pitch and roll, and its riders hang from the mesh's head dummies like the go-karts' (<see cref="CollectHeads"/>).
/// </summary>
// [APPROX:TOUR-008] the cars are drawn from the ride's supplemental mesh 0 at the controller's pose; the mesh's forward axis (taken as +X), the signs of its pitch and roll and the original's car animations (ids 0, 3, 4, 5, 6, 9, 10 on the car's own clips) are assumptions, and the car shadow is not drawn
public sealed class TourRenderer : IDisposable
{
	private const string TextureArrayName = "Color";
	private const int MaximumSeats = 12;

	private sealed record CarGeometry( List<Model> Models, Matrix4x4[] HeadNodes, uint[] HeadFlags );

	private sealed class CarVisual
	{
		public List<ModelEntity> Body = new();
		public TourController? Controller;
		public TourCar? Car;
		public Matrix4x4 Transform = Matrix4x4.Identity;
	}

	private readonly Func<TourController, ObjectCatalogEntry?> entryOf;
	private readonly Func<Vector2> gridOrigin;
	private readonly Dictionary<string, CarGeometry?> meshes = new();
	private readonly List<CarVisual> visuals = new();
	private readonly List<(TourController Controller, TourCar Car)> wanted = new();
	private bool disposed;

	/// <param name="entryOf">The catalogue entry of the ride a controller belongs to (null: its cars are not drawn).</param>
	/// <param name="gridOrigin">Engine position of grid corner (0, 0).</param>
	public TourRenderer( Func<TourController, ObjectCatalogEntry?> entryOf, Func<Vector2> gridOrigin )
	{
		this.entryOf = entryOf;
		this.gridOrigin = gridOrigin;
	}

	/// <summary>Car instances drawn after the last <see cref="Update"/>.</summary>
	public int CarCount => visuals.Count( visual => visual.Body.Count > 0 );

	/// <summary>Moves one instance to every live car of <paramref name="controllers"/>, creating and removing instances as cars come and go.</summary>
	public void Update( IEnumerable<TourController> controllers )
	{
		wanted.Clear();
		foreach ( var controller in controllers )
		{
			foreach ( var car in controller.Cars )
			{
				if ( car.IsLive )
					wanted.Add( (controller, car) );
			}
		}
		while ( visuals.Count > wanted.Count )
		{
			Delete( visuals[^1] );
			visuals.RemoveAt( visuals.Count - 1 );
		}
		while ( visuals.Count < wanted.Count )
			visuals.Add( new CarVisual() );
		var origin = gridOrigin();
		for ( var index = 0; index < wanted.Count; index++ )
		{
			var (controller, car) = wanted[index];
			var visual = visuals[index];
			if ( visual.Controller != controller || visual.Body.Count == 0 )
			{
				Delete( visual );
				if ( entryOf( controller ) is { } entry && Mesh( entry ) is { } geometry )
					visual.Body = geometry.Models.Select( model => new ModelEntity { Model = model, Name = "tour car" } ).ToList();
			}
			visual.Controller = controller;
			visual.Car = car;
			if ( visual.Body.Count == 0 )
				continue;
			var heading = car.Heading * 2 * MathF.PI / 4096f;
			var pitch = car.Pitch * 2 * MathF.PI / 4096f;
			var roll = car.Roll * 2 * MathF.PI / 4096f;
			var transform = Matrix4x4.CreateRotationX( roll ) * Matrix4x4.CreateRotationY( -pitch ) * Matrix4x4.CreateRotationZ( heading )
				* Matrix4x4.CreateTranslation( car.X / 300f, car.Z / 300f, car.Y / 300f )
				* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
				* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
			visual.Transform = transform;
			foreach ( var entity in visual.Body )
			{
				entity.TransformOverride = transform;
				entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
			}
		}
	}

	/// <summary>The riders of the cars as head sources: each rider hangs from the head dummy of its seat, newest seat last.</summary>
	internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var visual in visuals )
		{
			if ( visual.Car is not { } car || visual.Controller is not { AttractionId: >= 0 } controller || onlyAttraction is { } only && only != controller.AttractionId
				|| entryOf( controller ) is not { } entry || Mesh( entry ) is not { } geometry )
				continue;
			var riders = Math.Min( Math.Abs( car.RiderCount ), TourCar.MaximumRiders );
			for ( var seat = 0; seat < riders && seat < geometry.HeadNodes.Length; seat++ )
			{
				var node = ObjectPlacement.SwapAxes( geometry.HeadNodes[seat] ) * visual.Transform;
				sources.Add( new HeadSource( car.Riders[seat], controller.AttractionId, ObjectPlacement.SwapAxes( node ), geometry.HeadFlags[seat] ) );
			}
		}
	}

	private CarGeometry? Mesh( ObjectCatalogEntry entry )
	{
		if ( meshes.TryGetValue( entry.ArchivePath, out var cached ) )
			return cached;
		CarGeometry? geometry = null;
		var name = entry.Settings["SupplementalMeshes[0].FileName"];
		try
		{
			var path = name == null ? null : entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name.Trim( '"', ' ' ), StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, path );
				if ( model.Kind == ModelFileKind.Geometry )
					geometry = Build( model, entry );
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log.Warning( $"The tour ride's car mesh {name} could not be read: {exception.Message}" );
		}
		meshes[entry.ArchivePath] = geometry;
		return geometry;
	}

	private static CarGeometry Build( ModelFile model, ObjectCatalogEntry entry )
	{
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var models = new List<Model>();
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) != null && ObjectAssets.GetUnrenderableReason( mesh ) != "more than 16 texture slots" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
			{
				var slots = new Texture[16];
				Array.Fill( slots, Texture.Missing );
				for ( var index = 0; index < textures.Length; index++ )
					slots[index] = LoadTexture( entry, textures[index] );
				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				material.Set( TextureArrayName, slots );
				models.Add( new Model( vertices, indices, material, false ) );
			}
		}
		var heads = new List<Matrix4x4>();
		var flags = new List<uint>();
		for ( var id = 1; id <= MaximumSeats; id++ )
		{
			var node = RideHeadSpace.NodeForHead( model, id );
			if ( node < 0 || node >= rest.Length )
				break;
			heads.Add( rest[node] );
			flags.Add( model.Nodes[node].Type );
		}
		return new CarGeometry( models, heads.ToArray(), flags.ToArray() );
	}

	private static Texture LoadTexture( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return Texture.Missing;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		using var stream = fileSystem.OpenRead( path );
		var data = new TextureFile( stream ).Data;
		return new Texture( data.Data, data.Width, data.Height, TextureFlags.Repeat );
	}

	private static void Delete( CarVisual visual )
	{
		foreach ( var entity in visual.Body )
			entity.Delete();
		visual.Body = new List<ModelEntity>();
		visual.Controller = null;
		visual.Car = null;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var visual in visuals )
			Delete( visual );
		visuals.Clear();
		foreach ( var geometry in meshes.Values.Where( item => item != null ) )
		{
			foreach ( var model in geometry!.Models )
			{
				model.ReleaseBuffers();
				model.Material.ReleaseOwnedResources();
			}
		}
		meshes.Clear();
	}
}

/// <summary>The riders of the tour cars as head sources (the ride-head renderer asks for them each frame).</summary>
internal sealed class TourHeadSources : IHeadSourceProvider
{
	private readonly Func<TourRenderer?> renderer;

	public TourHeadSources( Func<TourRenderer?> renderer ) => this.renderer = renderer;

	public void Collect( List<HeadSource> sources, int? onlyAttraction ) => renderer()?.CollectHeads( sources, onlyAttraction );
}
