namespace OpenTPW.Hud;

/// <summary>
/// The sort state each window list keeps for the session (the original's global sort words). The word starts at 1, column 0 ascending, and the
/// window writes it back whenever the sort changes, so the order survives closing and reopening the window; a new game session starts it over.
/// </summary>
// [BIN:TPW-EXE:0x6659af list sort word] the builders pass a global sort word (column = |w|-1, direction = w < 0) that starts at 1: visitors 0x7508bc, all staff 0x75118c, the four attractions categories 0x750d5c/0x750d60/0x750d64/0x750d68, staff 0x7523e4, buy 0x755140; the windows write it back on a sort change (0x493479, 0x495505..0x495544, 0x495fe0 (all staff), 0x49b85e, 0x4ac9dd)
public sealed class ListSortMemory
{
	/// <summary>The buy list's sorted column and direction (0x755140).</summary>
	public BuySortColumn BuyColumn { get; set; }
	public bool BuyDescending { get; set; }
	/// <summary>The hire list's sorted column (false: name, true: wage) and direction (0x7523e4).</summary>
	public bool StaffByWage { get; set; }
	public bool StaffDescending { get; set; }
	/// <summary>The All Attractions window's last category (0x7ca154; rides, shops, sideshows, features as BuildCategory), which a reopening without a category shows.</summary>
	public int AttractionCategory { get; set; }
	/// <summary>The All Attractions list's sorted column and direction per category tab (rides, shops, sideshows, features; 0x750d5c..0x750d68).</summary>
	public int[] AttractionColumn { get; } = new int[4];
	public bool[] AttractionDescending { get; } = new bool[4];
	/// <summary>The All Visitors list's sorted column (0-5) and direction (0x7508bc).</summary>
	public int VisitorColumn { get; set; }
	public bool VisitorDescending { get; set; }
	/// <summary>The All Staff list's sorted column (0-4) and direction (0x75118c).</summary>
	public int AllStaffColumn { get; set; }
	public bool AllStaffDescending { get; set; }
	/// <summary>The All Staff window's last role tab (0-4: cleaners, mechanics, entertainers, guards, scientists; 0x7ca16c), which a reopening without a tab shows.</summary>
	public int AllStaffTab { get; set; }
}
