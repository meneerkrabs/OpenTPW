namespace OpenTPW;

/// <summary>
/// Demonstration of in-world TrueType sign text in an imported original level: the park gate
/// (<c>global.sam</c> <c>ParkName.GateObjectId</c>, e.g. 1601 "Gates" in jungle) gets the park name
/// on the faces of its gate model that use the runtime sign textures <c>sign1</c>/<c>sign2</c>, with
/// the font and em height of text slot 0 in the gate's <c>gates.sgn</c> (jungle: "Young Itch AOE",
/// 144 pixels). Only the sign faces are drawn (the gate model itself belongs to the object renderer);
/// they sit where the original gate's sign is because the gate archive sets
/// <c>Info.DontApplyOffset 1</c>, i.e. its MD2 coordinates are level coordinates.
/// The park name defaults to the theme name from <c>THEMENAMES.str</c> (the original lets the player
/// rename the park; that state lives in saves owned by the economy slice).
/// </summary>
public sealed class OriginalGateSign : Entity
{
	/// <summary>Theme order of THEMENAMES.str: Lost Kingdom, Halloween World, Wonder Land, Space Zone.</summary>
	public static readonly IReadOnlyList<string> ThemeLevels = new[] { "jungle", "hallow", "fantasy", "space" };
	/// <summary>Opaque dark board behind the text (approximation: the .sgn background blocks are not decoded).</summary>
	// [APPROX:COMPAT-004] flat board colour; the .sgn board image (wavelet) is read but not decoded or composed — evidence needed: the Bitmap::load_wavelet decoder and the board blit.
	public static readonly (byte R, byte G, byte B, byte A) Background = (38, 28, 18, 255);
	// [APPROX:COMPAT-008] depth offset for the sign faces — evidence needed: none once the full gate model draws its runtime textures.
	private const float SurfaceOffset = 0.05f;

	private readonly ModelEntity part;
	private readonly Model model;

	private OriginalGateSign( Model model, string parkName, int textPixels, SignTextSlot slot, string archivePath )
	{
		this.model = model;
		part = new ModelEntity { Model = model, Name = "gate sign" };
		ParkName = parkName;
		TextPixelCount = textPixels;
		Slot = slot;
		ArchivePath = archivePath;
	}

	public string ParkName { get; }
	public int TextPixelCount { get; }
	public SignTextSlot Slot { get; }
	public string ArchivePath { get; }

	/// <summary>The default park name of a level: its THEMENAMES.str entry, else the level name.</summary>
	public static string DefaultParkName( string levelName )
	{
		// [APPROX:COMPAT-007] theme name as park name until a save supplies one — evidence needed: save park-name field and a capture.
		// [DATA:THEMENAMES.str:entries 0..3] Lost Kingdom, Halloween World, Wonder Land, Space Zone.
		var index = ThemeLevels.ToList().FindIndex( level => string.Equals( level, levelName, StringComparison.OrdinalIgnoreCase ) );
		try
		{
			var names = GameLanguage.Current.LoadStrings( "THEMENAMES.str" ).Entries;
			if ( index >= 0 && index < names.Length && !string.IsNullOrWhiteSpace( names[index] ) )
				return names[index];
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException )
		{
			Log.Warning( $"Park name: THEMENAMES.str unavailable ({exception.Message}); using the level name." );
		}
		return levelName;
	}

	/// <summary>Builds the sign, or returns null (with a logged reason) when the gate data is missing.</summary>
	public static OriginalGateSign? TryCreate( OriginalPark park, SettingsFile global, string? parkName = null )
	{
		// [DATA:levels/<level>/global.sam:ParkName.GateObjectId]
		if ( !int.TryParse( global["ParkName.GateObjectId"], out var gateId ) || !park.Catalog.TryGetValue( gateId, out var gate ) )
		{
			Log.Warning( $"Gate sign: no ParkName.GateObjectId object in {park.LevelName}." );
			return null;
		}
		var sign = SignTextRenderer.LoadSignFile( gate.ArchivePath );
		var modelMember = FileSystem.GetFiles( gate.ArchivePath ).Select( Path.GetFileName )
			.FirstOrDefault( name => string.Equals( name, "gates.MD2", StringComparison.OrdinalIgnoreCase ) );
		if ( sign == null || modelMember == null )
		{
			Log.Warning( $"Gate sign: {gate.ArchivePath} has no .sgn or gates.MD2." );
			return null;
		}
		using var modelStream = FileSystem.OpenRead( $"{gate.ArchivePath}/{modelMember}" );
		var gateModel = new ModelFile( modelStream );
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var mesh in gateModel.Meshes )
			AppendSignFaces( park, gateModel, mesh, vertices, indices );
		if ( indices.Count == 0 )
		{
			Log.Warning( $"Gate sign: {gate.ArchivePath}/{modelMember} has no sign1/sign2 faces." );
			return null;
		}
		parkName ??= DefaultParkName( park.LevelName );
		var (left, right, diagnostics, textPixels) = SignTextRenderer.Shared.RenderSign( sign, new[] { parkName }, Background );
		foreach ( var diagnostic in diagnostics )
			Log.Warning( $"Gate sign: {diagnostic}" );
		var model = new Model( vertices.ToArray(), indices.ToArray(), SignTextRenderer.CreateMaterial( left, right ) );
		var slot = sign.Slots[0];
		Log.Trace( $"Gate sign: \"{parkName}\" in {slot.FontFileName} ('{slot.FaceName}', {slot.EmHeightPixels}px em) on {indices.Count / 3} sign faces of {gate.ArchivePath}/{modelMember}." );
		return new OriginalGateSign( model, parkName, textPixels, slot, gate.ArchivePath );
	}

	private static void AppendSignFaces( OriginalPark park, ModelFile model, ModelFile.Mesh mesh, List<Vertex> vertices, List<uint> indices )
	{
		if ( mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length )
			return;
		var slotOfMaterial = mesh.Materials.Select( material => material.Name.ToLowerInvariant() switch { "sign1" => 0, "sign2" => 1, _ => -1 } ).ToArray();
		if ( slotOfMaterial.All( slot => slot < 0 ) )
			return;
		var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex );
		var remap = new Dictionary<uint, uint>();
		for ( var i = 0; i + 2 < mesh.Indices.Length; i += 3 )
		{
			var corners = new[] { mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2] };
			if ( corners.Any( corner => corner >= mesh.Vertices.Length ) )
				continue;
			var material = (int)Math.Min( mesh.Vertices[corners[0]].TextureIndex, (uint)mesh.Materials.Length - 1 );
			if ( slotOfMaterial[material] < 0 )
				continue;
			foreach ( var corner in corners )
			{
				if ( !remap.TryGetValue( corner, out var index ) )
				{
					var source = mesh.Vertices[corner];
					var position = System.Numerics.Vector3.Transform( source.Position.GetSystemVector3(), world );
					var normal = mesh.Normals.Length > corner ? System.Numerics.Vector3.TransformNormal( mesh.Normals[corner].GetSystemVector3(), world ) : System.Numerics.Vector3.Zero;
					if ( normal.LengthSquared() > 0 )
						normal = System.Numerics.Vector3.Normalize( normal );
					// Lift the text slightly off the board so it wins the depth test against the gate model.
					var engine = OriginalParkPlacement.ToEngine( park.Heightfield, position + normal * (SurfaceOffset / OriginalParkPlacement.ModelScale) );
					index = (uint)vertices.Count;
					vertices.Add( new Vertex
					{
						Position = engine,
						Normal = normal.LengthSquared() > 0 ? new System.Numerics.Vector3( normal.X, normal.Z, normal.Y ) : System.Numerics.Vector3.UnitZ,
						TexCoords = mesh.TexCoords[corner],
						TexIndex = slotOfMaterial[material],
						MatFlags = 0
					} );
					remap.Add( corner, index );
				}
				indices.Add( index );
			}
		}
	}

	protected override void OnDelete()
	{
		part.Delete();
		Asset.All.Remove( model );
		global::Global.Render.ScheduleDelete( () =>
		{
			model.VertexBuffer.Dispose();
			model.IndexBuffer?.Dispose();
		} );
	}
}
