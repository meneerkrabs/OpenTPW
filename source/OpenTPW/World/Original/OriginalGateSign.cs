namespace OpenTPW;

/// <summary>
/// Demonstration of in-world TrueType sign text in an imported original level: the park gate
/// (<c>global.sam</c> <c>ParkName.GateObjectId</c>, e.g. 1601 "Gates" in jungle) gets the park name
/// on the faces of its gate model that use the runtime sign textures <c>sign1</c>/<c>sign2</c>, with
/// the font and em height of text slot 0 in the gate's <c>gates.sgn</c> (jungle: "Young Itch AOE",
/// 144 pixels). Only the sign faces are drawn (the gate model itself belongs to the object renderer);
/// they sit where the original gate's sign is because the gate archive sets
/// <c>Info.DontApplyOffset 1</c>, i.e. its MD2 coordinates are level coordinates.
/// The park name is the gate's two sign lines (<see cref="ParkNameLines"/>): the gate's OBJECT_NAMES row when the park
/// starts, the player's two fields after a rename (the ticket price window's park name), and the saved lines when a park
/// is resumed.
/// </summary>
public sealed class OriginalGateSign : Entity
{
	/// <summary>Theme order of THEMENAMES.str: Lost Kingdom, Halloween World, Wonder Land, Space Zone.</summary>
	public static readonly IReadOnlyList<string> ThemeLevels = new[] { "jungle", "hallow", "fantasy", "space" };
	// [EXT:gate-sign-depth-lift] the sign faces are lifted along their normal to win the depth test; an OpenTPW rendering choice, not an original rule
	private const float SurfaceOffset = 0.05f;

	private readonly ModelEntity part = null!;
	private readonly Model model;

	private OriginalGateSign( Model model, ObjectSignImages images, ParkNameLines lines, int textPixels, SignTextSlot slot, string archivePath, SignFile sign )
	{
		this.model = model;
		this.sign = sign;
		Images = images;
		Lines = lines;
		TextPixelCount = textPixels;
		Slot = slot;
		ArchivePath = archivePath;
		// a half-built entity must not stay in Entity.All: its deletion would find no part
		try { part = new ModelEntity { Model = model, Name = "gate sign" }; }
		catch { Delete(); throw; }
	}

	private readonly SignFile sign;

	/// <summary>The park name's two lines as the gate holds them.</summary>
	public ParkNameLines Lines { get; private set; }
	/// <summary>The park name on one line (<see cref="ParkNameLines.DisplayName"/>).</summary>
	public string ParkName => Lines.DisplayName;
	public ObjectSignImages Images { get; }
	public int TextPixelCount { get; private set; }
	public SignTextSlot Slot { get; }
	public string ArchivePath { get; }

	/// <summary>
	/// The lines a level's gate starts with: the gate's OBJECT_NAMES row in the current language (<see cref="ParkNameLines.Default"/>),
	/// else the theme name split at its middle space.
	/// </summary>
	public static ParkNameLines DefaultLines( string levelName, int gateInfoId )
	{
		if ( GameEditions.Current == GameEdition.ThemeParkWorld )
		{
			try
			{
				if ( ParkNameLines.Default( gateInfoId, GameLanguage.Current.LoadStrings( ObjectNames.FileName ).Entries ) is { } lines )
					return lines;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
			{
				Log.Warning( $"Park name: {ObjectNames.FileName} unavailable ({exception.Message}); using the theme name." );
			}
		}
		return ThemeNameLines( levelName );
	}

	/// <summary>The lines that stand in for a gate without its OBJECT_NAMES row: the theme name split at its middle space.</summary>
	public static ParkNameLines ThemeNameLines( string levelName )
	{
		// [EXT:park-name-fallback] without the gate's OBJECT_NAMES row (Theme Park Inc, a gate id outside tp.exe's table, missing language files) the theme name stands in, split at its middle space; no original rule applies
		var (line1, line2) = SignCanvas.SplitAtMiddleSpace( DefaultParkName( levelName ) );
		var fallback = new ParkNameLines( BonusNames.Truncate( line1 ), BonusNames.Truncate( line2 ) );
		return fallback.IsValid ? fallback : new ParkNameLines( "", "" );
	}

	/// <summary>The theme name of a level: its THEMENAMES.str entry, else the level name (the fallback park name).</summary>
	public static string DefaultParkName( string levelName )
	{
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
	/// <param name="lines">The park name's lines; null for the gate's default (<see cref="DefaultLines"/>).</param>
	/// <param name="reloaded">The lines come from a saved park: the sign is drawn as the rebuild after loading draws it (<see cref="ParkNameLines.ReloadedSignLines"/>).</param>
	public static OriginalGateSign? TryCreate( OriginalPark park, SettingsFile global, ParkNameLines? lines = null, bool reloaded = false )
	{
		var created = new Creation();
		foreach ( var _ in CreateSteps( park, global, created, lines, reloaded ) ) { }
		return created.Sign;
	}

	/// <summary>The result of <see cref="CreateSteps"/>: the sign, or null when the gate data is missing.</summary>
	internal sealed class Creation
	{
		public OriginalGateSign? Sign { get; set; }
	}

	/// <summary>
	/// <see cref="TryCreate"/> in steps, for a park built over frames (docs/BROWSER-PERFORMANCE.md, "Build steps"): a value after the gate's
	/// files and faces are read and after each step of the sign's composition (<see cref="SignSurfaceComposer.ComposeSteps"/>); the last step
	/// sets <paramref name="into"/>. The work and its order are those of <see cref="TryCreate"/>, so the sign's pixels are the same.
	/// </summary>
	internal static IEnumerable<bool> CreateSteps( OriginalPark park, SettingsFile global, Creation into, ParkNameLines? lines = null, bool reloaded = false )
	{
		// [DATA:levels/<level>/global.sam:ParkName.GateObjectId]
		if ( !int.TryParse( global["ParkName.GateObjectId"], out var gateId ) || !park.Catalog.TryGetValue( gateId, out var gate ) )
		{
			Log.Warning( $"Gate sign: no ParkName.GateObjectId object in {park.LevelName}." );
			yield break;
		}
		var sign = SignTextRenderer.LoadSignFile( gate.ArchivePath );
		var modelMember = FileSystem.GetFiles( gate.ArchivePath ).Select( Path.GetFileName )
			.FirstOrDefault( name => string.Equals( name, "gates.MD2", StringComparison.OrdinalIgnoreCase ) );
		if ( sign == null || modelMember == null )
		{
			Log.Warning( $"Gate sign: {gate.ArchivePath} has no .sgn or gates.MD2." );
			yield break;
		}
		ModelFile gateModel;
		using ( var modelStream = FileSystem.OpenRead( $"{gate.ArchivePath}/{modelMember}" ) )
			gateModel = new ModelFile( modelStream );
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var mesh in gateModel.Meshes )
			AppendSignFaces( park, gateModel, mesh, vertices, indices );
		if ( indices.Count == 0 )
		{
			Log.Warning( $"Gate sign: {gate.ArchivePath}/{modelMember} has no sign1/sign2 faces." );
			yield break;
		}
		lines ??= DefaultLines( park.LevelName, gateId );
		// [APPROX:COMPAT-004] the gate is composed from its decoded board with the park name as two lines; gate pixels (board placement, relief strength) are compared with a 720p capture only
		// [BIN:TPW-EXE:0x004629D0 object builder] a sign built from the catalogue is the info-record name split at the middle space (0x467A80); for the four shipped gates (rows of one-word texts) that is exactly the pair of lines the placed gate gets (0x4DB6F9), so the lines are drawn as they are
		// The stored paint is not overridden: in gates.sgn it is a 60 % black shadow offset by (-1, 1) under dark relief stone text, which is what the video shows (the former cream-paint and upward-normal decision gate-paint-legibility, COMPAT-031, is contradicted by the video)
		var (line1, line2) = reloaded ? lines.ReloadedSignLines : (lines.Line1, lines.Line2);
		yield return true;
		var diagnostics = new List<string>();
		var composition = new SignSurfaceComposer.Composition();
		foreach ( var part in SignTextRenderer.Shared.ComposeSignSteps( sign, new[] { line1, line2 }, diagnostics, composition ) )
			yield return part;
		var (left, right, textPixels) = composition.Result;
		foreach ( var diagnostic in diagnostics )
			Log?.Warning( $"Gate sign: {diagnostic}" );
		var images = new ObjectSignImages( line1, line2, left, right, textPixels, diagnostics );
		var model = new Model( vertices.ToArray(), indices.ToArray(), SignTextRenderer.CreateMaterial( images.LeftTexture, images.RightTexture ) );
		var slot = sign.Slots[0];
		Log.Trace( $"Gate sign: \"{lines.DisplayName}\" in {slot.FontFileName} ('{slot.FaceName}', {slot.EmHeightPixels}px em) on {indices.Count / 3} sign faces of {gate.ArchivePath}/{modelMember}." );
		into.Sign = new OriginalGateSign( model, images, lines, textPixels, slot, gate.ArchivePath, sign );
	}

	/// <summary>
	/// Gives the gate new lines and redraws its sign with them as they are (the rename dialog's OK). The sign's textures keep
	/// their identity, so the gate model and these faces show the new name at once.
	/// </summary>
	// [BIN:TPW-EXE:0x004993E0 park rename OK] the two fields are stored as the gate's lines (0x4E2950, 0x4E2A50) and the gate's sign is redrawn from the two fields (0x4682B0 -> 0x467A80 with explicit lines, no split)
	public void Rename( ParkNameLines lines )
	{
		ArgumentNullException.ThrowIfNull( lines );
		if ( !lines.IsValid )
			throw new ArgumentException( "The park name lines are not valid.", nameof( lines ) );
		var (left, right, diagnostics, textPixels) = ComposeHalves( sign, lines.Line1, lines.Line2 );
		Images.Replace( lines.Line1, lines.Line2, left, right, textPixels, diagnostics );
		Lines = lines;
		TextPixelCount = textPixels;
	}

	/// <summary>
	/// The CPU halves of the gate's sign with two lines drawn as they are (no middle-space split): the <c>sign1</c> (left) and <c>sign2</c> (right)
	/// textures the gate model shows. Drawing problems are logged, not thrown. Also used by the sign export (<see cref="SignExport"/>).
	/// </summary>
	internal static (byte[] Left, byte[] Right, IReadOnlyList<string> Diagnostics, int TextPixelCount) ComposeHalves( SignFile sign, string line1, string line2 )
	{
		var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { line1, line2 }, default );
		foreach ( var diagnostic in composed.Diagnostics )
			Log?.Warning( $"Gate sign: {diagnostic}" );
		return composed;
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
					var engineNormal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( normal.X, normal.Z, normal.Y ) ) : System.Numerics.Vector3.UnitZ;
					// Lift the text slightly off the board so it wins the depth test against the gate model.
					var engine = OriginalParkPlacement.ToEngine( park.Heightfield, position + normal * (SurfaceOffset / OriginalParkPlacement.ModelScale) );
					index = (uint)vertices.Count;
					vertices.Add( new Vertex
					{
						Position = engine,
						// Model axes (X, Y, Z) become engine axes (X, Z, Y) like the positions (OriginalTerrain does the same); a zero normal lights as up
						Normal = engineNormal,
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
		part?.Delete();
		model.ReleaseBuffers();
		// The gate's sign material and images are its own (its park's name), unlike the shared type signs.
		model.Material.ReleaseOwnedResources();
		Images.ReleaseTextures();
	}
}
