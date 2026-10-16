namespace OpenTPW.UI.Original;

/// <summary>Which of the two tool-pointer flags of the original (0x816d60, 0x816d4c) overrides the tool's own pointer; their meaning is not traced (`[EXT:software-pointers]`).</summary>
public enum ToolPointerFlag { Link, End }

/// <summary>
/// One control of an original layout table (<see cref="OriginalLayoutTables"/>): the window builder 0x65fd58 of the Windows
/// executable makes it from a command stream. The rectangle is in the 2048×1536 virtual canvas; <see cref="Model"/> is the
/// root name of the ui.wad model the control is drawn with (null: none from the table); <see cref="Help"/> is the UIHELPTEXT
/// id (-1: none) and <see cref="AltHelp"/> the second help id (-1: none; a toggle shows it in its other state).
/// </summary>
public sealed class LayoutNode
{
	public LayoutNode( int type, uint attributes, uint id, int left, int top, int right, int bottom, int help, int altHelp, string? model, string? model2, int[]? inner, LayoutNode[] children )
	{
		Type = type;
		Attributes = attributes;
		Id = id;
		Rect = new UiRect( left, top, right - left, bottom - top );
		Help = help;
		AltHelp = altHelp;
		Model = model;
		Model2 = model2;
		Inner = inner is { Length: 4 } ? new UiRect( inner[0], inner[1], inner[2] - inner[0], inner[3] - inner[1] ) : null;
		Children = children;
	}

	/// <summary>The control kind: 1 panel or text box, 2 model button, 3 slider, 5 text field, 9 value text, 12 variable box, 13 light switch.</summary>
	public int Type { get; }
	public uint Attributes { get; }
	public uint Id { get; }
	public UiRect Rect { get; }
	public int Help { get; }
	public int AltHelp { get; }
	public string? Model { get; }
	/// <summary>The root name of the second model (command 2; the lobby panel's button background), null when none.</summary>
	public string? Model2 { get; }
	/// <summary>The inner rectangle of command 3 (the text area of a panel), when the table gives one.</summary>
	public UiRect? Inner { get; }
	public IReadOnlyList<LayoutNode> Children { get; }
	/// <summary>Attribute bit 0x10: a toggle.</summary>
	public bool IsToggle => (Attributes & 0x10) != 0;

	public IEnumerable<LayoutNode> Descendants()
	{
		foreach ( var child in Children )
		{
			yield return child;
			foreach ( var inner in child.Descendants() )
				yield return inner;
		}
	}
}

/// <summary>A decoded layout table: its roots and lookups by control id and model name.</summary>
public sealed class LayoutTable
{
	private readonly LayoutNode[] roots;

	public LayoutTable( string name, int address, LayoutNode[] roots )
	{
		Name = name;
		Address = address;
		this.roots = roots;
	}

	public string Name { get; }
	/// <summary>The virtual address of the table in the Windows executable.</summary>
	public int Address { get; }
	public IReadOnlyList<LayoutNode> Roots => roots;
	public LayoutNode Root => roots[0];
	public IEnumerable<LayoutNode> All => roots.SelectMany( root => new[] { root }.Concat( root.Descendants() ) );

	/// <summary>The first control with this model root name; the table must have one.</summary>
	public LayoutNode Model( string model ) => All.FirstOrDefault( node => node.Model == model )
		?? throw new InvalidOperationException( $"Layout table {Name} has no control with model {model}." );

	public LayoutNode? TryModel( string model ) => All.FirstOrDefault( node => node.Model == model );

	/// <summary>The control with this id; the table must have one (ids of the shared window buttons repeat, so use <see cref="Model"/> for those).</summary>
	public LayoutNode Control( uint id ) => All.FirstOrDefault( node => node.Id == id )
		?? throw new InvalidOperationException( $"Layout table {Name} has no control {id:x}." );
}
