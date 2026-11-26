namespace OpenTPW;

/// <summary>
/// Which meshes of a Theme Park Inc advisor model are shown when one of its clips starts (docs/reverse/TPI-ADVISOR.md §4.4): the engine's clip start (the clip's node list hidden, as in
/// Theme Park World, <see cref="AdvisorAccessories.SequenceHiddenNodes"/>), then the advisor's clip-start update <c>0x715ED0</c> with its own accessory selector <c>0x715CF0</c>. GPU-free:
/// it works on the model's nodes and dummy attributes only.
/// </summary>
internal static class ThemeParkIncAdvisorAccessories
{
	/// <summary>The mesh record the clip-start update shows, or hides for a head without its parts: index 32 of the model's mesh table, the body of a level advisor.</summary>
	public const int BodyMesh = 32;

	/// <summary>The selectors that swap the award ribbons' textures (<c>0x31</c>–<c>0x38</c>) instead of showing a node.</summary>
	public const int FirstAwardSelector = 0x31, LastAwardSelector = 0x38;

	/// <summary>
	/// Applies accessory selector <paramref name="selector"/> (a response's word <c>+0x14</c> or <c>+0x18</c>) to <paramref name="hidden"/> in the original's order: the theme costume (−1) shows
	/// its add-ons and hides its sub-offs, 0 and 2 show the antennae (0x13, 0x14), 3–7, 9–11, 13 and 24 hide them, 14 hides the right hand (0x15); then a nonzero selector shows its own node
	/// unless the clip's accessory bit hides the costume. The award selectors swap textures, which no ported response asks for: they change nothing here.
	/// </summary>
	// [BIN:TPI-EXE:0x715CF0 accessory selector] 0x31-0x38: the award ribbons' textures (0x9DD1E4/0x9DD2A4 by 0x4D7BB0) and return; otherwise the byte table 0x715E50 indexed by the selector + 1
	// picks 0x715E3C's handler: -1 (0x715DD5) each of the four slots shows its nonzero add-on (0xBEF1C0, unless 0x10000 in 0xBEF068) and hides its nonzero sub-off (0xBEF1D0); 0 and 2 (0x715DB5)
	// show 0x13 and 0x14 (0x4653A0); 3-7, 9-11, 13, 24 (0x715D82) hide them (0x465440); 14 (0x715DA2) hides 0x15; any other none (0x715E1A); then a nonzero selector without 0x10000 shows its
	// node. [BIN:TPI-EXE:0x4653A0 show node] clears bit 0x10 of the node 0x49A3D0 finds for the attribute (type mask 0x400; Theme Park World's 0x44B220), 0x465440 sets it.
	// [EXT:tpi-capability-gates] the award ribbons' texture swap is not ported: no response OpenTPW plays has an award selector
	public static void ApplySelector( AdvisorNodeMap model, int selector, AdvisorThemeCostume theme, bool costumeSuppressed, ISet<int> hidden )
	{
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( theme );
		ArgumentNullException.ThrowIfNull( hidden );
		if ( selector is >= FirstAwardSelector and <= LastAwardSelector )
			return;
		void Show( int value )
		{
			var node = model.FindNode( AdvisorAccessories.SelectableTypeFlag, value );
			if ( node >= 0 )
				hidden.Remove( node );
		}
		void Hide( int value )
		{
			var node = model.FindNode( AdvisorAccessories.SelectableTypeFlag, value );
			if ( node >= 0 )
				hidden.Add( node );
		}
		switch ( selector )
		{
			case -1:
				for ( var slot = 0; slot < AdvisorThemeCostume.Slots; slot++ )
				{
					if ( theme.AddOns[slot] != 0 && !costumeSuppressed )
						Show( theme.AddOns[slot] );
					if ( theme.SubOffs[slot] != 0 )
						Hide( theme.SubOffs[slot] );
				}
				break;
			case 0 or 2:
				Show( AdvisorAccessories.FirstAntenna );
				Show( AdvisorAccessories.SecondAntenna );
				break;
			case >= 3 and <= 7 or >= 9 and <= 11 or 13 or 24:
				Hide( AdvisorAccessories.FirstAntenna );
				Hide( AdvisorAccessories.SecondAntenna );
				break;
			case 14:
				Hide( AdvisorAccessories.RightHand );
				break;
		}
		if ( selector != 0 && !costumeSuppressed )
			Show( selector );
	}

	/// <summary>
	/// The hidden nodes when a clip of a speech has just started: the clip's node list (every other node shown, as the engine leaves it at the previous clip's end), the body mesh shown (hidden
	/// for a head without its parts), the two selectors (skipped for a head without its parts) and, for a clip with <see cref="AdvisorAccessories.SuppressCostumeBit"/>, the bug head hidden. The
	/// mouths follow the presentation's mouth (<see cref="ThemeParkIncAdvisorView"/>); the clip's blink toggles are applied per frame (<see cref="AdvisorAccessories.ApplyToggles"/>).
	/// </summary>
	/// <param name="accessoryBits">The clip's accessory bits (<see cref="ThemeParkIncAdvisorPresentation.Bits"/>, <c>(entry &amp; 0x7F0000) &gt;&gt; 16</c>).</param>
	/// <param name="headSuppressed">The speech shows the head without its parts (<see cref="ThemeParkIncAdvisorPresentation.HeadSuppressed"/>, <c>0xBF7241</c>).</param>
	// [BIN:TPI-EXE:0x4DF610 animation start] the engine's clip start is Theme Park World's 0x4732A0, so a clip hides its node list and the previous clip's end has shown every node (AdvisorAccessories.SequenceHiddenNodes)
	// [BIN:TPI-EXE:0x715ED0 clip-start update] run for each clip the speech (0x7167C3) or the frame (0x716E69, 0x716E94 from 0x716E30) starts, unless the head is hidden (0xBF7240): both models
	// back on screen (0x464DB0), bit 0x10 cleared on mesh record 32 of the playing model (+0x70 + 0x1400) and set again for a head without its parts (0xBF7241), the five mouths hidden
	// (0xBEF0A8 + 20 per model; 0xBEF1A8 = -1, so the next frame shows the mouth again), the selectors +0x14 (0xBEF08C) and +0x18 (0xBEF090, when nonzero) unless the head is without its parts,
	// and with bit 0x10000 the bug head (0xBEF0A0, 0x49A510(model, 0, -1): the mesh named "bug head") hidden. Mesh record 32 is the body of a level advisor; a model with fewer meshes (the
	// global advisor has 29) has none, where the original writes past its mesh table: nothing is shown or hidden for it here
	public static HashSet<int> ClipStartHidden( AdvisorNodeMap model, ModelAnimation? clip, int accessory, int secondAccessory, AdvisorThemeCostume theme, int accessoryBits, bool headSuppressed )
	{
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( theme );
		var hidden = new HashSet<int>();
		if ( clip != null )
			foreach ( var node in clip.NodeList )
				if ( node < model.NodeCount )
					hidden.Add( node );
		if ( BodyMesh < model.Meshes.Count )
		{
			var body = model.Meshes[BodyMesh].Node;
			if ( headSuppressed )
				hidden.Add( body );
			else
				hidden.Remove( body );
		}
		var suppressed = (accessoryBits & AdvisorAccessories.SuppressCostumeBit) != 0;
		if ( !headSuppressed )
		{
			ApplySelector( model, accessory, theme, suppressed, hidden );
			if ( secondAccessory != 0 )
				ApplySelector( model, secondAccessory, theme, suppressed, hidden );
		}
		if ( suppressed )
			foreach ( var (name, node) in model.Meshes )
				if ( string.Equals( name, AdvisorAccessories.BugHead, StringComparison.OrdinalIgnoreCase ) )
				{
					// 0x49A510 stops at the first mesh of that name
					hidden.Add( node );
					break;
				}
		return hidden;
	}

	/// <summary>The mouth mesh of presentation mouth <paramref name="mouth"/> (1 closed to 5), null for none (0, the init's) or a clip that hides the mouth.</summary>
	// [BIN:TPI-EXE:0x7171A7 mouth nodes] when the mouth changes the frame hides the previous node and shows the new one (0xBEF0A4 + 20 per model + 4 per mouth) unless it is 0, the clip has bit
	// 0x10000 (0xBEF068) or the head is hidden (0xBF7240). [BIN:TPI-EXE:0x7158B2 mouth node lookup] the init finds mouths 1-5 by name (0x49A510: "mouth - normal", "aah", "eee", "ooh", "sss", Advisor.Mouths)
	public static string? MouthMesh( int mouth, int accessoryBits ) =>
		mouth is >= 1 and <= ThemeParkIncAdvisorPresentation.Mouths && (accessoryBits & AdvisorAccessories.SuppressCostumeBit) == 0 ? Advisor.Mouths[mouth - 1] : null;
}

/// <summary>A clip of a Theme Park Inc speech playing on the model: the clip, its index in the sequence, the tick into it and its accessory bits.</summary>
internal readonly record struct ThemeParkIncAdvisorClip( int Clip, int Index, float Tick, int Bits );

/// <summary>
/// The clips of a Theme Park Inc speech as the model plays them: the first starts with the speech, each next one when the one before has ended, each for its own length (the clip's trailer on the
/// model that plays it, not the sequence table's), and no clip starts once the speech has ended. After the last clip the model is hidden.
/// </summary>
internal static class ThemeParkIncAdvisorTimeline
{
	/// <summary>The highest clip number a sequence names (a canned row's clip 26: <c>advisor_xxxm26</c> of a level advisor).</summary>
	public const int MaxClip = 26;

	/// <summary>
	/// The clip playing <paramref name="elapsed"/> milliseconds after the speech started, or null once the model is hidden. <paramref name="lengths"/> are the playing model's clip lengths by clip
	/// number (0 for a clip it lacks, which ends at once); <paramref name="ended"/> is when the speech ended, after its start, or null while it speaks.
	/// </summary>
	// [BIN:TPI-EXE:0x717067 next clip] while the speech is active (0xBEF074) and the sequence's next word is a clip, the frame asks the model's channel (0x4E09F0) and starts the next clip only when
	// it reads the idle set 12 (0x4DF610: AnimID 5, the clip less 1, flags 0), the end of the clip before; the speech's end (0x716F83, before this step in the same frame) stops the sequence.
	// [BIN:TPI-EXE:0x4E09F0 animation sampler] the engine is Theme Park World's: a clip runs to its own length (trailer word 2 × 1000 / 30) and the advisor model (load flags 0x50120,
	// 0x464F4A) is hidden at the end of a clip with nothing after it (Advisor.DrawnSpanMilliseconds). [EXT:tpi-advisor-tick-clock] the next clip starts at the end of the one before, as an
	// ideal frame pacing has it, where the original starts it at its first frame after that end
	public static ThemeParkIncAdvisorClip? At( IReadOnlyList<int> clips, IReadOnlyList<int> bits, IReadOnlyList<uint> lengths, uint elapsed, uint? ended )
	{
		ArgumentNullException.ThrowIfNull( clips );
		ArgumentNullException.ThrowIfNull( bits );
		ArgumentNullException.ThrowIfNull( lengths );
		var start = 0uL;
		for ( var index = 0; index < clips.Count; index++ )
		{
			// a later clip starts only from a frame of the speech, which has ended by the end of the clip before
			if ( index > 0 && ended is { } end && start >= end )
				return null;
			var clip = clips[index];
			var length = clip >= 0 && clip < lengths.Count ? lengths[clip] : 0u;
			if ( elapsed < start + length )
				return new ThemeParkIncAdvisorClip( clip, index, (elapsed - (uint)start) * AdvisorTimeline.TicksPerSecond / 1000f, index < bits.Count ? bits[index] : 0 );
			start += length;
		}
		return null;
	}

	/// <summary>The play lengths of <paramref name="clipOf"/>'s clips 0 to <see cref="MaxClip"/> (index = clip number; 0 for one the model lacks).</summary>
	public static uint[] Lengths( Func<int, ModelAnimation?> clipOf )
	{
		ArgumentNullException.ThrowIfNull( clipOf );
		var lengths = new uint[MaxClip + 1];
		for ( var clip = 1; clip <= MaxClip; clip++ )
			lengths[clip] = clipOf( clip ) is { Duration: > 0 } animation ? AdvisorClipDurations.FromTrailer( (uint)animation.Duration ) : 0;
		return lengths;
	}
}
