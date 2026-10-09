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
	/// <summary>Original running cycle: cart lift/drop plus counter-rotating cogs (430 ticks).</summary>
	internal const string AnimationName = "totemm1.MD2";
	/// <summary>Sandbox choice; the original animation tick rate is not verified.</summary>
	public const float AnimationTicksPerSecond = 30f;

	private readonly RideMotion motion = new();
	private readonly RideVM script;
	private readonly List<(ModelEntity Entity, int NodeIndex)> children = new();
	private readonly List<Model> models = new();
	private readonly ModelAnimationPlayer animation = null!;
	private readonly Matrix4x4[] restTransforms = Array.Empty<Matrix4x4>();
	private readonly Matrix4x4[] nodeTransforms = Array.Empty<Matrix4x4>();
	private readonly int carriageNode;
	private bool deleted;
	private static int nextAttractionId = 1;

	/// <summary>The ride is open: the original script sees VAR_RIDECLOSED = 0.</summary>
	public bool IsOpen => script[RideVariables.VAR_RIDECLOSED] == 0;
	/// <summary>Animated carriage height above its rest pose, in sandbox units.</summary>
	public float MotionHeight { get; private set; }
	public double Phase => motion.Phase;
	/// <summary>True while ANIM_Main (totemm1.MD2) is playing.</summary>
	public bool IsAnimating => motion.IsRunning;
	/// <summary>Current animation tick (0 when not playing).</summary>
	public float AnimationTick => animation.Tick;
	/// <summary>Model-space node matrices of the current pose.</summary>
	public IReadOnlyList<Matrix4x4> NodeTransforms => nodeTransforms;

	/// <summary>The original Totem.RSE, driven by the fixed simulation tick.</summary>
	public RideVM Script => script;

	/// <summary>Visitor side of Totem.RSE: queue, LETMEON/LETMEOFF host protocol and visitor opcodes (docs/GUESTS.md).</summary>
	public RideVisitorBridge Visitors { get; } = null!;

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
			Visitors = new RideVisitorBridge( nextAttractionId++, DisplayName, RideVisitorKind.Ride,
				ParseSetting( settings, "Upgrades[0].InitCapacity" ), ParseSetting( settings, "UsageInfo.ExcitementLevel" ), ParseSetting( settings, "Info.AttractionValue" ) );
			using ( var scriptStream = FileSystem.OpenRead( ScriptPath ) )
				script = new RideVM( scriptStream, new RideVMOptions { Effects = new TotemEffects( this ), SourceName = ScriptPath } );
			// Upgrades[0].InitCapacity is the only capacity the .sam gives for a new ride. VAR_DURATION has no
			// source yet and stays 0, which Totem.RSE treats as a single ride cycle per run.
			script[RideVariables.VAR_CAPACITY] = int.Parse( settings["Upgrades[0].InitCapacity"], System.Globalization.CultureInfo.InvariantCulture );
			script[RideVariables.VAR_RIDECLOSED] = 1;
			Visitors.Attach( script, () => !deleted && script[RideVariables.VAR_RIDECLOSED] == 0 );
			var modelFile = new ModelFile( $"{ArchivePath}/totem.MD2" );
			var carriage = modelFile.Meshes.FirstOrDefault( mesh => IsCarriage( mesh.Name ) )
				?? throw new InvalidDataException( "The original Totem model does not contain its tp_cart carriage mesh." );
			carriageNode = carriage.NodeIndex;
			var clip = new ModelFile( $"{ArchivePath}/{AnimationName}" ).Clip
				?? throw new InvalidDataException( "The original Totem animation is not an animation member." );
			animation = new ModelAnimationPlayer( modelFile, clip, AnimationTicksPerSecond ) { Loop = false };
			motion = new RideMotion( cycleDuration: clip.Duration / AnimationTicksPerSecond );
			restTransforms = ModelAnimationPlayer.ComputeRestTransforms( modelFile );
			nodeTransforms = (Matrix4x4[])restTransforms.Clone();

			var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
			var missingTexture = Texture.Missing;
			foreach ( var mesh in modelFile.Meshes )
			{
				var vertices = ConvertMesh( mesh );

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
				var child = new ModelEntity
				{
					Model = model,
					Name = mesh.Name.TrimEnd( '\0' )
				};
				children.Add( (child, mesh.NodeIndex) );
			}
			UpdateChildren();
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
		Visitors.HostStep();
		script.Advance( deltaTime );
		motion.Update( deltaTime );
		if ( motion.IsRunning && motion.ElapsedSeconds >= motion.CycleDuration )
			motion.Stop();
		UpdateChildren();
	}

	private void UpdateChildren()
	{
		if ( motion.IsRunning )
		{
			animation.SetTick( (float)motion.ElapsedSeconds * AnimationTicksPerSecond );
			animation.ComputeWorldTransforms( nodeTransforms );
		}
		else
		{
			animation.Reset();
			restTransforms.CopyTo( nodeTransforms, 0 );
		}
		MotionHeight = (nodeTransforms[carriageNode].M42 - restTransforms[carriageNode].M42) * ModelScale;
		foreach ( var child in children )
		{
			var transform = ToRenderTransform( nodeTransforms[child.NodeIndex], Position );
			child.Entity.TransformOverride = transform;
			child.Entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	/// <summary>
	/// Maps a model-space MD2 node matrix to the renderer: swap Y/Z on both sides (vertices are
	/// converted with <see cref="ConvertAxes"/>), centre the ride footprint, scale and place it.
	/// </summary>
	internal static Matrix4x4 ToRenderTransform( Matrix4x4 node, Vector3 position )
	{
		var swap = new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );
		return swap * node * swap
			* Matrix4x4.CreateTranslation( -15, -20, 0 )
			* Matrix4x4.CreateScale( ModelScale )
			* Matrix4x4.CreateTranslation( position.X, position.Y, position.Z );
	}

	protected override void OnDelete()
	{
		deleted = true;
		Visitors?.ReleaseAll();
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
	/// Routes Totem.RSE effects. Only ANIM_Main is connected: it plays the original totemm1.MD2 clip once
	/// (430 ticks at <see cref="AnimationTicksPerSecond"/>) and reports its length in milliseconds.
	/// Visitor opcodes go to <see cref="Visitors"/>. Everything else (sounds, objects, channel animations) is an unimplemented effect.
	/// </summary>
	private sealed class TotemEffects : IRideScriptEffects
	{
		private readonly PrototypeRide ride;

		public TotemEffects( PrototypeRide ride ) => this.ride = ride;

		public int Perform( RideEffectCall call )
		{
			if ( ride.Visitors.TryPerform( call, out var visitorResult ) )
				return visitorResult;
			if ( call.Opcode is Opcode.TRIGANIM or Opcode.WAITANIM or Opcode.TRIGWAITANIM && call.Argument( 0 ) == MainAnimation )
			{
				ride.motion.Stop();
				ride.motion.Start();
				return (int)(ride.motion.CycleDuration * 1000);
			}
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
	}

	private static int ParseSetting( SettingsFile settings, string key ) =>
		int.TryParse( settings[key], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : 0;

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
