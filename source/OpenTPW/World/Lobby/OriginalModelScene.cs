using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// A 3D front-end scene built from original MD2 models (the lobby, the online globe): meshes are composed
/// through their node hierarchy (parent-relative, docs/MD2-MODELS.md), MD2 (x, y-up, z) maps to engine
/// (x, z, y-up), and up to 16 textures share one <see cref="Model"/> per part.
/// </summary>
public abstract class OriginalModelScene : Entity
{
	protected const int MaximumTextureSlots = 16;
	protected readonly List<ModelEntity> parts = new();
	private readonly List<Model> models = new();
	private readonly Dictionary<string, Texture> textures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Folder the scene's texture names are looked up in.</summary>
	protected abstract string TextureDirectory { get; }

	public int PartCount => parts.Count;

	private bool visible = true;
	private readonly List<AnimatedModel> animatedModels = new();

	/// <summary>Shows or hides every part (an animated model only while it is <see cref="AnimatedModel.Shown"/>); the scene stays loaded.</summary>
	public bool Visible
	{
		get => visible;
		set
		{
			visible = value;
			foreach ( var part in parts )
				part.Hidden = !value;
			foreach ( var model in animatedModels )
				model.ApplyVisibility();
		}
	}

	protected static NVector3 ToEngine( NVector3 md2 ) => new( md2.X, md2.Z, md2.Y );

	/// <summary>Adds the model's meshes, placed by <paramref name="placement"/>; returns the parts it created.</summary>
	protected IReadOnlyList<ModelEntity> AddModel( ModelFile model, NMatrix4x4 placement )
	{
		var first = parts.Count;
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length || mesh.Normals.Length != mesh.Vertices.Length )
				continue;
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex ) * placement;
			int MaterialOf( uint vertex ) => (int)Math.Min( mesh.Vertices[vertex].TextureIndex, (uint)mesh.Materials.Length - 1 );
			// A draw binds at most 16 textures; a mesh with more (the online globe has 24) is split by material.
			for ( var low = 0; low < mesh.Materials.Length; low += MaximumTextureSlots )
			{
				var high = Math.Min( mesh.Materials.Length, low + MaximumTextureSlots );
				var meshNames = mesh.Materials[low..high].Select( material => material.TextureIndex < 0 ? "" : material.Name ).ToArray();
				if ( names.Union( meshNames, StringComparer.OrdinalIgnoreCase ).Count() > MaximumTextureSlots )
					Flush( names, vertices, indices );
				var slots = meshNames.Select( name =>
				{
					var slot = names.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
					if ( slot < 0 )
					{
						names.Add( name );
						slot = names.Count - 1;
					}
					return slot;
				} ).ToArray();
				var remap = new Dictionary<uint, uint>();
				for ( var triangle = 0; triangle + 2 < mesh.Indices.Length; triangle += 3 )
				{
					var material = MaterialOf( mesh.Indices[triangle] );
					if ( material < low || material >= high )
						continue;
					for ( var corner = 0; corner < 3; corner++ )
					{
						var index = mesh.Indices[triangle + corner];
						if ( !remap.TryGetValue( index, out var mapped ) )
						{
							var source = mesh.Vertices[index];
							var normal = NVector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world );
							mapped = (uint)vertices.Count;
							vertices.Add( new Vertex
							{
								Position = ToEngine( NVector3.Transform( source.Position.GetSystemVector3(), world ) ),
								Normal = normal.LengthSquared() > 0 ? NVector3.Normalize( ToEngine( normal ) ) : NVector3.UnitZ,
								TexCoords = mesh.TexCoords[index],
								TexIndex = slots[material - low],
								MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[material] )
							} );
							remap[index] = mapped;
						}
						indices.Add( mapped );
					}
				}
			}
		}
		Flush( names, vertices, indices );
		return parts.GetRange( first, parts.Count - first );
	}

	protected void Flush( List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < names.Count ? LoadTexture( names[index] ) : slots[0] ?? Texture.Missing;
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			var model = new Model( vertices.ToArray(), indices.ToArray(), material );
			models.Add( model );
			parts.Add( new ModelEntity { Model = model, Name = $"{GetType().Name} part {parts.Count}" } );
		}
		names.Clear();
		vertices.Clear();
		indices.Clear();
	}

	protected Texture LoadTexture( string name )
	{
		if ( name.Length == 0 )
			return Texture.Missing;
		// the shipped sign1/sign2 art is the magenta "SIGN1 SIGN2" placeholder the game draws over at run time: a sign slot of a model without a runtime sign shows the plain board
		if ( ObjectSigns.IsSignSlot( name ) )
			return SignSlotPlan.FallbackBoard;
		if ( textures.TryGetValue( name, out var texture ) )
			return texture;
		var path = OpenTPW.UI.Original.UiImages.Resolve( name, TextureDirectory );
		texture = path == null ? Texture.Missing : new Texture( path, TextureFlags.Repeat );
		textures[name] = texture;
		return texture;
	}

	/// <summary>A sign slot of a named model: the material and array element it is bound to, and the texture bound there now.</summary>
	private sealed class SignSlot( string model, int side, Material material, int index, Texture bound )
	{
		public string Model { get; } = model;
		public int Side { get; } = side;
		public Material Material { get; } = material;
		public int Index { get; } = index;
		public Texture Bound { get; set; } = bound;
	}

	private readonly List<SignSlot> signSlots = new();

	/// <summary>
	/// The runtime sign of the scene's model <paramref name="model"/> (its file name without extension), or null while it has none (not composed yet, or
	/// it cannot be): its sign1/sign2 slots then show the plain board, never the shipped placeholder.
	/// </summary>
	protected virtual ObjectSignImages? SignOf( string model ) => null;

	/// <summary>The texture of one side (0 <c>sign1</c>, 1 <c>sign2</c>) of a model's runtime sign; the scene owns it and releases it with itself.</summary>
	protected virtual Texture? SignTexture( string model, int side ) => null;

	private Texture SignSlotTexture( string? model, string textureName )
	{
		var plan = SignSlotPlan.ForScene( model == null ? null : SignOf( model ) );
		return plan.SourceOf( textureName ) == SignSlotSource.RuntimeSign && model != null && SignTexture( model, ObjectSigns.SlotOf( textureName ) ) is { } sign
			? sign
			: SignSlotPlan.FallbackBoard;
	}

	/// <summary>Whether the scene has a model <paramref name="model"/> with sign slots.</summary>
	protected bool HasSignSlots( string model ) => signSlots.Any( slot => string.Equals( slot.Model, model, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The models of the scene with sign slots.</summary>
	protected IReadOnlyList<string> SignSlotModels => signSlots.Select( slot => slot.Model ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();

	/// <summary>Where each sign slot of <paramref name="model"/> takes its bound texture from: the model's runtime sign, the plain board, or (never expected) the shipped art.</summary>
	private protected IReadOnlyList<SignSlotSource> SignSlotSourcesOf( string model ) => signSlots
		.Where( slot => string.Equals( slot.Model, model, StringComparison.OrdinalIgnoreCase ) )
		.Select( slot => ReferenceEquals( slot.Bound, SignSlotPlan.FallbackBoard ) ? SignSlotSource.FallbackBoard
			: SignTexture( slot.Model, slot.Side ) is { } sign && ReferenceEquals( slot.Bound, sign ) ? SignSlotSource.RuntimeSign
			: SignSlotSource.Shipped )
		.ToList();

	/// <summary>Binds the sign slots of <paramref name="model"/> to its sign as it is now (after it was composed).</summary>
	protected void RebindSigns( string model )
	{
		foreach ( var slot in signSlots )
		{
			if ( !string.Equals( slot.Model, model, StringComparison.OrdinalIgnoreCase ) )
				continue;
			slot.Bound = SignSlotTexture( slot.Model, slot.Side == 0 ? "sign1" : "sign2" );
			slot.Material.SetTextureArrayElement( "Color", slot.Index, slot.Bound );
		}
	}

	/// <summary>
	/// An MD2 model drawn with its own <see cref="ObjectAnimator"/>: one part per mesh (split at 16 textures) moved by its
	/// node's animated matrix and the model's <see cref="Placement"/>, and rewritten when a vertex or texture-coordinate
	/// track changes the mesh, the way placed park objects are drawn (<see cref="OriginalObject"/>).
	/// </summary>
	protected sealed class AnimatedModel
	{
		private readonly List<(ModelEntity Entity, int Node, int Mesh, Vertex[] Vertices, Model Model, int Version)> meshParts = new();

		private readonly OriginalModelScene scene;
		private bool shown = true;

		internal AnimatedModel( OriginalModelScene scene, ModelFile file, bool animatedMeshes, string? name )
		{
			this.scene = scene;
			scene.animatedModels.Add( this );
			File = file;
			Animator = new ObjectAnimator( file );
			dynamicMeshes = animatedMeshes;
			for ( var meshIndex = 0; meshIndex < file.Meshes.Count; meshIndex++ )
			{
				var mesh = file.Meshes[meshIndex];
				if ( ObjectAssets.GetUnrenderableReason( mesh ) is "no geometry" or "no materials" )
					continue;
				var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < file.Nodes.Count ? mesh.NodeIndex : file.RootNodeIndex;
				IReadOnlyList<(Vertex[] Vertices, uint[] Indices, string[] Textures, int[] Slots)> converted;
				try
				{
					converted = ObjectAssets.ConvertMeshParts( mesh );
				}
				catch ( InvalidDataException exception )
				{
					Log?.Warning( $"Lobby model mesh {mesh.Name}: {exception.Message}" );
					continue;
				}
				foreach ( var (vertices, indices, textures, _) in converted )
				{
					var slots = new Texture[MaximumTextureSlots];
					for ( var index = 0; index < slots.Length; index++ )
						slots[index] = index >= textures.Length ? slots[0] ?? Texture.Missing
							: ObjectSigns.IsSignSlot( textures[index] ) ? scene.SignSlotTexture( name, textures[index] )
							: scene.LoadTexture( textures[index] );
					var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
					material.Set( "Color", slots );
					if ( name != null )
						for ( var index = 0; index < textures.Length && index < slots.Length; index++ )
							if ( ObjectSigns.SlotOf( textures[index] ) is >= 0 and var side )
								scene.signSlots.Add( new SignSlot( name, side, material, index, slots[index] ) );
					var model = new Model( vertices, indices, material, animatedMeshes );
					scene.models.Add( model );
					var entity = new ModelEntity { Model = model, Name = $"{scene.GetType().Name} part {scene.parts.Count}" };
					scene.parts.Add( entity );
					meshParts.Add( (entity, node, meshIndex, vertices, model, -1) );
				}
			}
			UpdateParts();
		}

		private readonly bool dynamicMeshes;

		public ModelFile File { get; }
		public ObjectAnimator Animator { get; }

		/// <summary>Whether the model is drawn while its scene is visible (a model built ahead of its use stays hidden until then).</summary>
		public bool Shown
		{
			get => shown;
			set
			{
				shown = value;
				ApplyVisibility();
			}
		}

		internal void ApplyVisibility()
		{
			foreach ( var part in meshParts )
				part.Entity.Hidden = !shown || !scene.visible;
		}

		/// <summary>Model-to-lobby matrix in MD2 axes (row vectors).</summary>
		public NMatrix4x4 Placement { get; set; } = NMatrix4x4.Identity;

		/// <summary>Advances the animator by <paramref name="seconds"/> and moves and reshapes the parts.</summary>
		public void Advance( double seconds )
		{
			Animator.Advance( seconds );
			UpdateParts();
		}

		/// <summary>Moves and reshapes the parts for the current pose and placement.</summary>
		public void UpdateParts()
		{
			var nodes = Animator.NodeTransforms;
			for ( var index = 0; index < meshParts.Count; index++ )
			{
				var (entity, node, mesh, vertices, model, version) = meshParts[index];
				var current = Animator.GetVertexVersion( mesh );
				if ( dynamicMeshes && current != version )
				{
					ObjectRenderParts.WritePositions( File.Meshes[mesh], Animator.GetVertexPositions( mesh ), vertices );
					ObjectRenderParts.WriteTexCoords( File.Meshes[mesh], Animator.GetTexCoords( mesh ), vertices );
					model.UpdateVertices( vertices );
					meshParts[index] = (entity, node, mesh, vertices, model, current);
				}
				var transform = ObjectPlacement.SwapAxes( (node >= 0 && node < nodes.Count ? nodes[node] : NMatrix4x4.Identity) * Placement );
				entity.TransformOverride = transform;
				entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
			}
		}
	}

	/// <summary>
	/// Adds an animated model (see <see cref="AnimatedModel"/>); <paramref name="animatedMeshes"/> makes its vertices rewritable. A model with a
	/// <paramref name="name"/> (its file name without extension) gets its runtime sign on its sign slots (<see cref="SignOf"/>), the others the plain board.
	/// </summary>
	protected AnimatedModel AddAnimatedModel( ModelFile file, bool animatedMeshes, string? name = null ) => new( this, file, animatedMeshes, name );

	/// <summary>Removes one part and frees its buffers.</summary>
	protected void RemovePart( ModelEntity part )
	{
		if ( !parts.Remove( part ) )
			return;
		part.Delete();
		if ( part.Model is { } model && models.Remove( model ) )
			Free( model );
	}

	/// <summary>
	/// Retires a model and its material. Every model of a scene has a material of its own (<see cref="Flush"/> and the animated meshes make one each), and the material
	/// carries its own shader copy, pipeline and uniform buffer: releasing only the buffers left all of those in <see cref="Asset.All"/> after the scene went, about 160 materials
	/// and 5 MB of shader source for every visit of the lobby (docs/BROWSER-PERFORMANCE.md, "Transfer and memory").
	/// </summary>
	private static void Free( Model model )
	{
		model.ReleaseBuffers();
		model.Material.ReleaseOwnedResources();
	}

	protected override void OnDelete()
	{
		foreach ( var part in parts )
			part.Delete();
		parts.Clear();
		signSlots.Clear();
		foreach ( var model in models )
			Free( model );
		models.Clear();
	}
}
