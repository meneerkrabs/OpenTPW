using System.Numerics;

namespace OpenTPW;

public sealed class PrototypeRide : Entity
{
	public const float FootprintRadius = 5f;
	public const float ModelScale = 0.2f;
	public const string DisplayName = "Inca Totem (prototype)";
	internal const string ArchivePath = "/levels/jungle/rides/totem";
	internal const string CarriageMeshName = "tp_cart";
	internal const string ScriptPath = ArchivePath + "/Totem.RSE";

	/// <summary>
	/// <c>ANIM_Main</c> in ScriptDefs and the first operand of the docs' <c>TRIGANIM ANIM_Main 0 0</c> example.
	/// Totem.RSE triggers it once per ride cycle after setting VAR_RUNNING and waits for it with WAIT4ANIM.
	/// </summary>
	internal const int MainAnimation = 5;

	private readonly RideMotion motion = new();
	private readonly RideVM script;
	private readonly List<(ModelEntity Entity, Vector3 Offset, bool IsCarriage)> children = new();
	private readonly List<Model> models = new();
	private bool deleted;

	/// <summary>The ride is open: the original script sees VAR_RIDECLOSED = 0.</summary>
	public bool IsOpen => script[RideVariables.VAR_RIDECLOSED] == 0;
	public float MotionHeight => motion.Height;
	public double Phase => motion.Phase;

	/// <summary>The original Totem.RSE, driven by the fixed simulation tick.</summary>
	public RideVM Script => script;

	public PrototypeRide( Vector3 position )
	{
		try
		{
			if ( !float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) )
				throw new ArgumentOutOfRangeException( nameof( position ) );

			Position = position;
			Rotation = Quaternion.Identity;
			var settings = new SettingsFile( $"{ArchivePath}/Totem.sam" );
			if ( settings["Info.Id"] != "1110" )
				throw new InvalidDataException( "The ride settings do not identify the original Inca Totem." );
			Name = DisplayName;
			using ( var scriptStream = FileSystem.OpenRead( ScriptPath ) )
				script = new RideVM( scriptStream, new RideVMOptions { Effects = new TotemEffects( this ), SourceName = ScriptPath } );
			// Upgrades[0].InitCapacity is the only capacity the .sam gives for a new ride. VAR_DURATION has no
			// source yet and stays 0, which Totem.RSE treats as a single ride cycle per run.
			script[RideVariables.VAR_CAPACITY] = int.Parse( settings["Upgrades[0].InitCapacity"], System.Globalization.CultureInfo.InvariantCulture );
			script[RideVariables.VAR_RIDECLOSED] = 1;
			var modelFile = new ModelFile( $"{ArchivePath}/totem.MD2" );
			if ( !modelFile.Meshes.Any( mesh => IsCarriage( mesh.Name ) ) )
				throw new InvalidDataException( "The original Totem model does not contain its tp_cart carriage mesh." );

			var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
			var missingTexture = Texture.Missing;
			foreach ( var mesh in modelFile.Meshes )
			{
				var vertices = ConvertMesh( mesh );
				if ( !Matrix4x4.Decompose( mesh.TransformMatrix, out var scale, out var rotation, out var translation ) )
					throw new InvalidDataException( $"Cannot decompose ride mesh transform: {mesh.Name}" );

				var textureSlots = new Texture[16];
				Array.Fill( textureSlots, missingTexture );
				for ( var textureIndex = 0; textureIndex < mesh.Materials.Length; ++textureIndex )
				{
					var texturePath = ResolveTexturePath( FileSystem, mesh.Materials[textureIndex].Name );
					if ( !textures.TryGetValue( texturePath, out var texture ) )
					{
						texture = new Texture( texturePath, TextureFlags.Repeat );
						textures.Add( texturePath, texture );
					}
					textureSlots[textureIndex] = texture;
				}

				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				material.Set( "Color", textureSlots );
				var model = new Model( vertices, mesh.Indices, material );
				models.Add( model );
				var offset = (ConvertAxes( translation ) - new Vector3( 15, 20, 0 )) * ModelScale;
				var child = new ModelEntity
				{
					Model = model,
					Name = mesh.Name.TrimEnd( '\0' ),
					Scale = ConvertAxes( scale ) * ModelScale,
					Rotation = new Quaternion( rotation.X, rotation.Z, rotation.Y, -rotation.W ),
					Position = Position + offset
				};
				children.Add( (child, offset, IsCarriage( mesh.Name )) );
			}
		}
		catch
		{
			Delete();
			throw;
		}
	}

	/// <summary>Opens the ride. The script decides when the carriage moves (Totem.RSE waits up to 10 s for passengers).</summary>
	public void Start()
	{
		if ( !deleted )
			script[RideVariables.VAR_RIDECLOSED] = 0;
	}

	/// <summary>Closes the ride; the script finishes its current cycle and then idles.</summary>
	public void Stop()
	{
		if ( !deleted )
			script[RideVariables.VAR_RIDECLOSED] = 1;
	}

	internal void Simulate( float deltaTime )
	{
		if ( deleted )
			return;
		script.Advance( deltaTime );
		motion.Update( deltaTime );
		if ( motion.IsRunning && motion.ElapsedSeconds >= motion.CycleDuration )
			motion.Stop();
		UpdateChildren();
	}

	private void UpdateChildren()
	{
		foreach ( var child in children )
			child.Entity.Position = Position + child.Offset + (child.IsCarriage ? Vector3.Up * motion.Height : Vector3.Zero);
	}

	protected override void OnDelete()
	{
		deleted = true;
		script?.Stop();
		motion.Stop();
		foreach ( var child in children )
			child.Entity.Delete();
		children.Clear();
		foreach ( var model in models )
		{
			Asset.All.Remove( model );
			global::Global.Render.ScheduleDelete( () =>
			{
				model.VertexBuffer.Dispose();
				model.IndexBuffer?.Dispose();
			} );
		}
		models.Clear();
	}

	/// <summary>
	/// Routes Totem.RSE effects. Only ANIM_Main is connected: it plays one cycle of the procedural
	/// <see cref="RideMotion"/> as a stand-in for the undecoded MD2 animation and reports that cycle's length.
	/// Everything else (sounds, objects, visitors, channel animations) is an unimplemented effect.
	/// </summary>
	private sealed class TotemEffects : IRideScriptEffects
	{
		private readonly PrototypeRide ride;

		public TotemEffects( PrototypeRide ride ) => this.ride = ride;

		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode is Opcode.TRIGANIM or Opcode.WAITANIM or Opcode.TRIGWAITANIM && call.Argument( 0 ) == MainAnimation )
			{
				ride.motion.Stop();
				ride.motion.Start();
				return (int)(ride.motion.CycleDuration * 1000);
			}
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
	}

	internal static bool IsCarriage( string meshName ) => string.Equals( meshName.TrimEnd( '\0' ), CarriageMeshName, StringComparison.OrdinalIgnoreCase );

	internal static Vector3 ConvertAxes( Vector3 position ) => new( position.X, position.Z, position.Y );

	internal static Vertex[] ConvertMesh( ModelFile.Mesh mesh )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0 || mesh.Materials.Length == 0 || mesh.Materials.Length > 16 || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
			throw new InvalidDataException( $"Invalid ride mesh buffers: {mesh.Name}" );
		if ( mesh.Indices.Any( index => index >= mesh.Vertices.Length ) )
			throw new InvalidDataException( $"Invalid ride mesh indices: {mesh.Name}" );

		var vertices = new Vertex[mesh.Vertices.Length];
		for ( var vertexIndex = 0; vertexIndex < vertices.Length; ++vertexIndex )
		{
			var sourceVertex = mesh.Vertices[vertexIndex];
			if ( sourceVertex.TextureIndex >= mesh.Materials.Length )
				throw new InvalidDataException( $"Invalid ride material index: {mesh.Name}" );
			if ( !IsFinite( sourceVertex.Position ) || !IsFinite( mesh.Normals[vertexIndex] ) || !float.IsFinite( mesh.TexCoords[vertexIndex].X ) || !float.IsFinite( mesh.TexCoords[vertexIndex].Y ) )
				throw new InvalidDataException( $"Nonfinite ride mesh vertex: {mesh.Name}" );
			vertices[vertexIndex] = new Vertex
			{
				Position = ConvertAxes( sourceVertex.Position ),
				Normal = ConvertAxes( mesh.Normals[vertexIndex] ),
				TexCoords = mesh.TexCoords[vertexIndex],
				TexIndex = (int)sourceVertex.TextureIndex,
				MatFlags = mesh.Materials[(int)sourceVertex.TextureIndex].Flags
			};
		}
		return vertices;
	}

	private static bool IsFinite( Vector3 position ) => float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z );

	internal static string ResolveTexturePath( BaseFileSystem fileSystem, string textureName )
	{
		var name = textureName.TrimEnd( '\0' );
		foreach ( var directory in new[] { $"{ArchivePath}/textures", "/levels/jungle/sharetex" } )
		{
			var path = fileSystem.GetFiles( directory ).FirstOrDefault( entry => string.Equals( Path.GetFileNameWithoutExtension( entry ), name, StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
				return path;
		}
		throw new FileNotFoundException( $"Original Totem texture was not found: {name}" );
	}
}
