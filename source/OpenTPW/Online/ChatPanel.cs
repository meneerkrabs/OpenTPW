using OpenTPW.Online.Chat;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI;

/// <summary>
/// The original chat overlay (tp.exe table 0x758AE8, built by 0x4BF460 when a park enters chat mode): over the chat frame (ui.wad <c>f_chat</c>), the
/// people list at the left with its two tabs (the park's people, the buddies) and its slider, the postcard, vote and earmuffs buttons under it, the
/// message list along the bottom with its slider, and the text entry under that. A right click on a name opens the original's name popup at the
/// pointer: six buttons for a buddy (table 0x758CD8), four for anyone else (table 0x758E18), none for the player's own name. The chat park screen
/// and a visit with other players show it.
/// </summary>
// [BIN:TPW-EXE:0x4BF460 chat mode] the overlay's window (0x7CC8BC) is table 0x758AE8 with the handler 0x4BD4A0; the panel 0xC3123 takes the name popups' handler 0x4BE290, the people list 0xC3124 the row template 0x4BF740 (colours 0x4BE730) and the list handler 0x4BE820, the tab group 0xC3125 starts on the people tab 0xC3126, the message list 0xC312B takes the row template 0x4BF8B0 (colours 0x4BE7C0) and the entry 0xC312C the key handler 0x4BE3A0, font slot 7 (0x758F08) and at most 255 characters; the two name popups (0x7CC8C4, 0x7CC8C8) are built hidden with the button handler 0x4BEDE0
public sealed class ChatPanel
{
	/// <summary>The name popup's screen.</summary>
	public const string PopupScreenName = "chatPopup";
	public static LayoutTable Table => OriginalLayoutTables.ChatOverlay;
	private static LayoutNode PeopleNode => Table.Control( 0xC3124 );
	private static LayoutNode MessagesNode => Table.Control( 0xC312B );
	/// <summary>The row area of the people list (its inner rectangle).</summary>
	public static UiRect PeopleRows => PeopleNode.Inner!.Value;
	/// <summary>The row area of the message list (its inner rectangle).</summary>
	public static UiRect MessageRowsRect => MessagesNode.Inner!.Value;
	public static UiRect EntryRect => Table.Control( 0xC312C ).Rect;

	private readonly UiStringTable strings;
	private readonly UiScreenStack popups;
	private readonly Func<IChatPanelSource?> source;
	private readonly UiAnchor anchor;
	private readonly List<UiElement> elements = new();
	private readonly PeopleList people;
	private readonly MessageList messages;
	private bool buddyTab;
	/// <summary>The vote button was pressed: it stays disabled, as the original's does (0x4BE820).</summary>
	private bool votePressed;
	private IReadOnlyList<ChatPerson> peopleRows = Array.Empty<ChatPerson>();

	/// <param name="popups">The stack the name popups open on (the screen the panel is on must be its top).</param>
	/// <param name="source">The chat the panel shows; null while there is none (the panel stays empty and its controls disabled).</param>
	public ChatPanel( UiStringTable strings, UiScreenStack popups, Func<IChatPanelSource?> source, UiAnchor anchor = UiAnchor.TopLeft )
	{
		this.strings = strings;
		this.popups = popups;
		this.source = source;
		this.anchor = anchor;
		T Add<T>( T element ) where T : UiElement
		{
			element.Anchor = anchor;
			elements.Add( element );
			return element;
		}
		var panel = Table.Control( 0xC3123 );
		// the chat frame behind the controls: its panels (the left column and the bottom strip of f_chat's triangles) keep clicks off the park, which shows
		// through its opening and takes the clicks there
		Add( new UiModelImage { Id = "chatFrame", Model = panel.Model!, Bounds = panel.Rect, CoversShape = true } );
		var tabs = Table.Control( 0xC3125 );
		var peopleTab = tabs.Children.Single( node => node.Id == 0xC3126 );
		var buddyTabNode = tabs.Children.Single( node => node.Id == 0xC3127 );
		// [BIN:TPW-EXE:0x4BE820 tabs] message 0x101 of the tab group: the people tab (0xC3126) fills the list with the park's people (0x4EE8D0), the other with the buddies (0x4EE9E0)
		// the tab group is a radio group (style bit 0x10), so its tabs click with cat_ui 0xBD (LayoutTable.ClickSoundFor)
		Add( new UiButton { Id = "chatPeopleTab", Model = peopleTab.Model, Help = strings.Help( 402 ), Bounds = peopleTab.Rect, ClickSound = Table.ClickSoundOf( peopleTab ),
			Selected = () => !buddyTab, Clicked = () => ShowTab( buddies: false ) } );
		Add( new UiButton { Id = "chatBuddyTab", Model = buddyTabNode.Model, Help = strings.Help( 403 ), Bounds = buddyTabNode.Rect, ClickSound = Table.ClickSoundOf( buddyTabNode ),
			Selected = () => buddyTab, Clicked = () => ShowTab( buddies: true ) } );
		people = Add( new PeopleList( this ) { Id = "chatPeople", Bounds = PeopleRows } );
		var peopleSlider = PeopleNode.Children.Single( node => node.Type == 3 );
		Add( new Scroller( people, peopleSlider ) { Id = "chatPeopleScroll", Bounds = peopleSlider.Rect, Help = strings.Help( 422 ) } );
		var upNode = peopleSlider.Children.Single( node => node.Model == "b_up" );
		var downNode = peopleSlider.Children.Single( node => node.Model == "b_down" );
		// the slider's arrows repeat while held, as the original's scroll arrows do (UiButton.ScrollArrowRepeat)
		Add( new UiButton { Id = "chatPeopleUp", Model = "b_up", Clicked = () => people.Scroll( -1 ), Repeat = UiButton.ScrollArrowRepeat( () => people.Scroll( -1 ) ),
			ClickSound = Table.ClickSoundOf( upNode ), Bounds = upNode.Rect } );
		Add( new UiButton { Id = "chatPeopleDown", Model = "b_down", Clicked = () => people.Scroll( 1 ), Repeat = UiButton.ScrollArrowRepeat( () => people.Scroll( 1 ) ),
			ClickSound = Table.ClickSoundOf( downNode ), Bounds = downNode.Rect } );
		// [BIN:TPW-EXE:0x4BE820 player buttons] message 0x100: 0xC312A runs the postcard 0x4A9380, 0xC3129 votes (0x4EB300) and disables itself, 0xC3128 turns the earmuffs on (0x4EBEE0) or off (0x4EBEF0) by its toggle state
		var postcard = Table.Control( 0xC312A );
		Add( new UiButton { Id = "chatPostcard", Model = postcard.Model, Help = strings.Help( 480 ), Bounds = postcard.Rect, ClickSound = Table.ClickSoundOf( postcard ), Clicked = () => Postcard?.Invoke() } );
		var vote = Table.Control( 0xC3129 );
		Add( new UiButton { Id = "chatVote", Model = vote.Model, Help = strings.Help( 461 ), Bounds = vote.Rect, ClickSound = Table.ClickSoundOf( vote ), Clicked = Vote } );
		var earmuffs = Table.Control( 0xC3128 );
		// [BIN:TPW-EXE:0x4BD400 earmuffs state] the server's earmuffs answer sets the toggle (0x668bec)
		Add( new UiButton { Id = "chatEarmuffs", Model = earmuffs.Model, HelpSource = () => strings.Help( Source?.Earmuffs == true ? 419 : 418 ), Bounds = earmuffs.Rect, ClickSound = Table.ClickSoundOf( earmuffs ),
			Selected = () => Source?.Earmuffs == true, Clicked = () => Source?.SendCommand( ChatCommand.Earmuffs, "" ) } );
		messages = Add( new MessageList( this ) { Id = "chatMessages", Bounds = MessageRowsRect } );
		var messageSlider = MessagesNode.Children.Single( node => node.Type == 3 );
		Add( new Scroller( messages, messageSlider ) { Id = "chatMessagesScroll", Bounds = messageSlider.Rect, Help = strings.Help( 422 ) } );
		// [BIN:TPW-EXE:0x4BE3A0 entry keys] Enter (0xD) says the typed line (0x4EA490) and empties the entry
		// the entry takes the keys on a click, not when the pointer crosses it on its way over the park; its help (UIHELPTEXT 420) is not shown, as the help box
		// lies over it
		Entry = Add( new UiTextField { Id = "chatEntry", MaximumLength = EntryMaximumLength, Background = false, Font = fonts => fonts.Small,
			OverView = true, Bounds = EntryRect } );
		Entry.Submitted = Say;
	}

	/// <summary>The most characters the entry takes (0x4BF460 creates it with 255), one fewer than a chat line may carry (<see cref="ChatCommands.MaximumLineLength"/>).</summary>
	public const int EntryMaximumLength = 255;

	/// <summary>The panel's elements, in drawing order, for the screen that shows it.</summary>
	public IReadOnlyList<UiElement> Elements => elements;
	/// <summary>The text entry (799020).</summary>
	public UiTextField Entry { get; }
	/// <summary>Opens the postcard composer (the postcard button); null disables the button.</summary>
	public Action? Postcard { get; init; }
	/// <summary>Takes the player to another player of the room (the goto button of a name popup) beside sending the command; null: the command only.</summary>
	public Action<string>? GoTo { get; init; }
	/// <summary>
	/// [EXT:online-coop-presence] The goto button's own way to the player, tried before the command: true when it took the player there, and no command goes out (a shared park's
	/// view moves to the player's figure, C2d-4); false leaves the goto to the command.
	/// </summary>
	public Func<string, bool>? TakeTo { get; init; }
	/// <summary>Whether the buddy tab is shown.</summary>
	public bool BuddyTab => buddyTab;
	private IChatPanelSource? Source => source();

	public void AddTo( UiScreen screen )
	{
		foreach ( var element in elements )
			screen.Add( element );
	}

	public bool Contains( UiElement element ) => elements.Contains( element );

	/// <summary>
	/// Keeps the lists and the controls' state, once a frame: the entry and the chat buttons work while the chat connection is open, the vote once and
	/// only in a published park, the postcard whenever there is a composer; a popup closes when the connection goes.
	/// </summary>
	public void Update()
	{
		peopleRows = Source is { } chat ? ChatPanelModel.People( chat, buddyTab ) : Array.Empty<ChatPerson>();
		var open = Source?.ChatOpen == true;
		foreach ( var element in elements )
		{
			if ( element is UiButton or UiTextField )
				element.Enabled = open;
		}
		foreach ( var id in new[] { "chatPeopleTab", "chatBuddyTab", "chatPeopleUp", "chatPeopleDown" } )
			if ( Find( id ) is { } always )
				always.Enabled = true;
		if ( Find( "chatPostcard" ) is { } postcardButton )
			postcardButton.Enabled = Postcard != null;
		// [BIN:TPW-EXE:0x4BD450 vote state] the server's vote state enables or disables the vote button (vfunc +0x14)
		if ( Find( "chatVote" ) is { } voteButton )
			voteButton.Enabled = open && !votePressed && Source?.CanVote == true;
		if ( !open && popups.Top?.Name == PopupScreenName )
			ClosePopup();
	}

	private UiElement? Find( string id ) => elements.FirstOrDefault( element => element.Id == id );

	/// <summary>The rows the people list shows (as of the last <see cref="Update"/>).</summary>
	public IReadOnlyList<ChatPerson> People => peopleRows;

	public void ShowTab( bool buddies )
	{
		if ( buddyTab == buddies )
			return;
		buddyTab = buddies;
		people.Reset();
		if ( buddies )
			Source?.AskBuddies();
		peopleRows = Source is { } chat ? ChatPanelModel.People( chat, buddyTab ) : Array.Empty<ChatPerson>();
	}

	private void Vote()
	{
		if ( Source is not { } chat )
			return;
		votePressed = true;
		chat.SendCommand( ChatCommand.Vote, "" );
	}

	private void Say()
	{
		if ( Entry.Text.Trim().Length > 0 )
			Source?.SendChatLine( Entry.Text );
		Entry.Text = "";
	}

	/// <summary>Opens the name popup of <paramref name="person"/> at <paramref name="point"/> (framebuffer pixels); nothing for the player's own name.</summary>
	// [BIN:TPW-EXE:0x4BE290 name popup] a right click on a row (0x402) reads the row's flags and player; the player's own row (0x4EA9A0) opens nothing, a buddy (bit 0) the six-button popup 0x1789 (focus 0x178C), anyone else the four-button popup 0x3003 (focus 0x3006); the ignore toggle takes bit 3, and 0x4BF370 places the popup at the pointer
	// [EXT:online-chat-popup-place] the popup's corner is at the pointer, kept inside the screen; the offset 0x4BF370 applies is not read
	public void OpenPopup( ChatPerson person, UiCanvas canvas, NVector2 point )
	{
		if ( person.Own || Source == null )
			return;
		ClosePopup();
		var table = (person.Flags & ChatPersonFlags.Buddy) != 0 ? OriginalLayoutTables.ChatBuddyPopup : OriginalLayoutTables.ChatPlayerPopup;
		var frame = table.Root.Children.Single( node => node.Model == "!f_plain" );
		var scale = canvas.Scale;
		var x = Math.Clamp( point.X / scale, 0, Math.Max( 0, canvas.Width / scale - frame.Rect.Width ) );
		var y = Math.Clamp( point.Y / scale, 0, Math.Max( 0, canvas.Height / scale - frame.Rect.Height ) );
		var popup = new NamePopup( this, new UiRect( x, y, frame.Rect.Width, frame.Rect.Height ) );
		popup.Add( new UiModelImage { Id = "popupFrame", Model = frame.Model!, Bounds = popup.Frame, Anchor = UiAnchor.TopLeft } );
		foreach ( var node in frame.Children )
		{
			var action = PopupAction( node.Model );
			var bounds = new UiRect( x + node.Rect.X - frame.Rect.X, y + node.Rect.Y - frame.Rect.Y, node.Rect.Width, node.Rect.Height );
			var ignore = action == ChatCommand.Ignore;
			popup.Add( new UiButton
			{
				Id = $"popup{action}",
				Model = node.Model,
				Help = strings.Help( node.Help ),
				HelpSource = ignore && node.AltHelp >= 0 ? () => strings.Help( Source?.Ignored.Contains( person.Name, StringComparer.OrdinalIgnoreCase ) == true ? node.AltHelp : node.Help ) : null,
				Selected = ignore ? () => Source?.Ignored.Contains( person.Name, StringComparer.OrdinalIgnoreCase ) == true : null,
				Bounds = bounds,
				Anchor = UiAnchor.TopLeft,
				ClickSound = table.ClickSoundOf( node ),
				Clicked = () => RunPopupAction( action, person.Name ),
			} );
		}
		popups.Push( popup );
	}

	/// <summary>The command of a name popup's button.</summary>
	// [BIN:TPW-EXE:0x4BEDE0 popup buttons] b_bmark black-marks the player (command 0xE), b_gopeep goes to them (0x13), b_ignore ignores them or stops (0xD), b_unbuddi adds them to the buddy list in the four-button popup and removes them in the six-button one (0xF), b_peeploc finds where they are (0x14) and b_page pages them (0x11)
	internal static ChatCommand PopupAction( string? model ) => model switch
	{
		"b_bmark" => ChatCommand.Mark,
		"b_gopeep" => ChatCommand.Goto,
		"b_ignore" => ChatCommand.Ignore,
		"b_unbuddi" => ChatCommand.Buddy,
		"b_peeploc" => ChatCommand.Locate,
		"b_page" => ChatCommand.Page,
		_ => throw new InvalidOperationException( $"The chat name popup has no button {model}." ),
	};

	private void RunPopupAction( ChatCommand action, string name )
	{
		ClosePopup();
		if ( Source is not { } chat )
			return;
		if ( action == ChatCommand.Goto && TakeTo?.Invoke( name ) == true )
			return;
		// a name with a space goes in quotes, as the server reads a command's player name
		chat.SendCommand( action, ChatCommands.NameArgument( name ) );
		if ( action == ChatCommand.Goto )
			GoTo?.Invoke( name );
	}

	public void ClosePopup() => popups.RemoveWhere( screen => screen.Name == PopupScreenName );

	/// <summary>The people list: the room's players or the buddies, each name in its colour; a right click opens the name's popup.</summary>
	private sealed class PeopleList : UiElement, IUiRowList, IChatScrollable
	{
		private readonly ChatPanel owner;
		private int first;
		private int hovered = -1;
		private string? selected;
		private float rowHeight = 40;
		private (UiCanvas Canvas, NVector2 Point) lastPoint;

		public PeopleList( ChatPanel owner )
		{
			this.owner = owner;
			HelpSource = () => hovered >= 0 && hovered < Rows.Count ? string.Join( "\n", ChatPanelModel.PersonHelp( Rows[hovered] ).Select( owner.strings.Help ) ) : null;
		}

		public override bool Focusable => Visible && Enabled;
		public bool ArrowsSelect => true;
		public int Selected
		{
			get
			{
				var rows = Rows;
				for ( var index = 0; index < rows.Count; index++ )
					if ( rows[index].Name == selected )
						return index;
				return -1;
			}
		}
		public int First => Math.Clamp( first, 0, MaxFirst );
		public int MaxFirst => Math.Max( 0, Rows.Count - VisibleRows );
		public int VisibleRows => Math.Max( 1, (int)(Bounds.Height / rowHeight) );
		private IReadOnlyList<ChatPerson> Rows => owner.People;

		public void Reset()
		{
			first = 0;
			selected = null;
		}

		public void Scroll( int rows ) => first = Math.Clamp( First + rows, 0, MaxFirst );
		public void ScrollTo( int row ) => first = Math.Clamp( row, 0, MaxFirst );

		public void Select( int index )
		{
			var rows = Rows;
			if ( rows.Count == 0 )
				return;
			index = Math.Clamp( index, 0, rows.Count - 1 );
			selected = rows[index].Name;
			if ( index < First )
				first = index;
			else if ( index >= First + VisibleRows )
				first = index - VisibleRows + 1;
		}

		// the pointer only picks the row whose help shows; it selects nothing
		public bool HoverSelects => true;
		public void Hover( int index ) => hovered = index;

		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			lastPoint = (canvas, point);
			var rect = ScreenRect( canvas );
			if ( !rect.Contains( point ) )
				return -1;
			var row = (int)MathF.Floor( (point.Y - rect.Y) / (rowHeight * canvas.Scale) );
			return row < 0 || row >= VisibleRows || First + row >= Rows.Count ? -1 : First + row;
		}

		public bool RowClicked( int index, bool left )
		{
			Select( index );
			if ( !left && index < Rows.Count )
				owner.OpenPopup( Rows[index], lastPoint.Canvas, lastPoint.Point );
			return true;
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var scale = context.Canvas.Scale;
			// [BIN:TPW-EXE:0x4BF760 row template] a row is as high as font slot 7's text plus 4 units
			rowHeight = context.Measure( context.Fonts.Small, "Ag" ).Height / scale + 4;
			var rows = Rows;
			var top = First;
			var height = rowHeight * scale;
			for ( var index = 0; index < VisibleRows && top + index < rows.Count; index++ )
			{
				var person = rows[top + index];
				var row = new UiRect( rect.X, rect.Y + index * height, rect.Width, height );
				// [EXT:online-chat-row-highlight] the chosen row is marked with a bar; the original's hilight art (0x659bc9 "hilight") is not drawn
				if ( person.Name == selected )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)150 : (byte)100 ) );
				context.DrawText( context.Fonts.Small, person.Name, row, ChatPanelModel.NameColor( person.Flags ) );
			}
		}
	}

	/// <summary>The message list: the chat lines cut to its width, the last <see cref="ChatPanelModel.MessageRows"/> rows, following the newest.</summary>
	private sealed class MessageList( ChatPanel owner ) : UiElement, IUiRowList, IChatScrollable
	{
		private int first;
		private bool following = true;
		private float rowHeight = 40;
		private IReadOnlyList<string> rows = Array.Empty<string>();
		private (int Count, string? Last, float Width, FontAtlas? Font) wrapped = (-1, null, 0, null);

		public override bool Focusable => Visible && Enabled;
		public bool ArrowsSelect => false;
		public int Selected => -1;
		public int First => Math.Clamp( first, 0, MaxFirst );
		public int MaxFirst => Math.Max( 0, rows.Count - VisibleRows );
		public int VisibleRows => Math.Max( 1, (int)(Bounds.Height / rowHeight) );

		public void Select( int index ) { }
		public void Scroll( int rows ) => ScrollTo( First + rows );
		public void ScrollTo( int row )
		{
			first = Math.Clamp( row, 0, MaxFirst );
			following = first >= MaxFirst;
		}
		public bool RowClicked( int index, bool left ) => true;

		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			if ( !rect.Contains( point ) )
				return -1;
			var row = (int)MathF.Floor( (point.Y - rect.Y) / (rowHeight * canvas.Scale) );
			return row < 0 || row >= VisibleRows || First + row >= rows.Count ? -1 : First + row;
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var scale = context.Canvas.Scale;
			var font = context.Fonts.Small;
			rowHeight = context.Measure( font, "Ag" ).Height / scale + 4;
			var lines = owner.Source?.ChatLines ?? Array.Empty<string>();
			var last = lines.Count > 0 ? lines[^1] : null;
			// the lines are cut again when a line came (the list is capped, so its last entry tells a new one apart once it is full) or the width or font changed
			if ( lines.Count != wrapped.Count || !ReferenceEquals( last, wrapped.Last ) || rect.Width != wrapped.Width || !ReferenceEquals( font, wrapped.Font ) )
			{
				rows = ChatPanelModel.MessageRowsOf( lines, text => context.Measure( font, text ).Width, rect.Width );
				wrapped = (lines.Count, last, rect.Width, font);
			}
			if ( following )
				first = MaxFirst;
			var height = rowHeight * scale;
			for ( var index = 0; index < VisibleRows && First + index < rows.Count; index++ )
				context.DrawText( font, rows[First + index], new UiRect( rect.X, rect.Y + index * height, rect.Width, height ), UiColors.Text );
		}
	}

	/// <summary>A list the slider can move.</summary>
	private interface IChatScrollable
	{
		int First { get; }
		int MaxFirst { get; }
		void ScrollTo( int row );
	}

	/// <summary>A list's slider: the scroller knob moves along the slider's track; a drag scrolls the list.</summary>
	private sealed class Scroller( IChatScrollable list, LayoutNode slider ) : UiElement, IUiDragTarget
	{
		private readonly UiRect track = slider.Inner ?? slider.Rect;
		private readonly UiRect knob = slider.Children.Single( node => node.Model == "b_scroller" ).Rect;

		public override bool Focusable => Visible && Enabled;

		private UiRect KnobRect()
		{
			var travel = track.Height - knob.Height;
			var top = track.Y + (list.MaxFirst == 0 ? 0 : travel * list.First / list.MaxFirst);
			return knob with { Y = top };
		}

		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			var travel = track.Height - knob.Height;
			if ( rect.Height <= 0 || travel <= 0 || list.MaxFirst == 0 )
				return;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (y - track.Y - knob.Height / 2) / travel, 0, 1 );
			list.ScrollTo( (int)MathF.Round( fraction * list.MaxFirst ) );
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( slider.Model2 ?? "!slider", 0, context.Canvas.Map( track, Anchor ) );
			context.DrawModel( "b_scroller", list.MaxFirst == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}

	/// <summary>
	/// A name popup: its buttons act on the name; a click beside it, a right click or Escape closes it. A left click beside it closes it on its release, so
	/// neither the press nor the release reaches what lies under it (the park, a button).
	/// </summary>
	private sealed class NamePopup : UiScreen
	{
		private readonly ChatPanel owner;
		private bool closing;

		public NamePopup( ChatPanel owner, UiRect frame ) : base( PopupScreenName )
		{
			this.owner = owner;
			Frame = frame;
			Modal = false;
			Back = owner.ClosePopup;
		}

		/// <summary>The popup's frame, canvas units from the top left.</summary>
		public UiRect Frame { get; }

		public override bool Update( UiContext context, UiInput input )
		{
			// the panel under the popup is not updated while the popup is on top: it is kept from here (a lost connection closes the popup)
			owner.Update();
			if ( IsRemoved )
				return true;
			if ( !input.PointerSuppressed && HandlePointer( context, input ) )
				return true;
			if ( closing )
				return true;
			base.Update( context, input );
			// the popup stands over the park: nothing under it takes the pointer while it is open
			return true;
		}

		internal override bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient )
		{
			if ( IsRemoved || HandlePointer( context, input ) ) return true;
			base.UpdatePointer( context, input, recipient );
			return true;
		}

		private bool HandlePointer( UiContext context, UiInput input )
		{
			if ( closing )
			{
				if ( input.LeftReleased || !input.LeftDown )
					owner.ClosePopup();
				return true;
			}
			var inside = context.Canvas.Map( Frame, UiAnchor.TopLeft ).Contains( input.Mouse );
			if ( input.LeftPressed && !inside )
			{
				closing = true;
				return true;
			}
			if ( input.RightPressed )
			{
				owner.ClosePopup();
				return true;
			}
			return false;
		}
	}

	/// <summary>
	/// A screen that shows the chat overlay: it keeps the panel each frame, Tab takes the focus off the entry (0x4BE3A0: key 9), Enter with nothing focused
	/// gives the entry the keys, the entry leaves the pointer to what lies under the screen outside its controls, and no help covers the entry while a line
	/// is being typed. The owner sets <see cref="UiScreen.Back"/> and <see cref="UiScreen.Modal"/> and adds what lies under the panel first.
	/// </summary>
	public sealed class ChatScreen( string name, ChatPanel panel ) : UiScreen( name )
	{
		public ChatPanel Panel => panel;

		public override bool Update( UiContext context, UiInput input )
		{
			panel.Update();
			if ( input.Has( UiKeys.Tab ) && ReferenceEquals( Focused, panel.Entry ) )
			{
				Focus( null );
				input = input with { Pressed = input.Pressed & ~UiKeys.Tab };
			}
			var consumed = base.Update( context, input );
			if ( IsRemoved )
				return consumed;
			// Enter with nothing focused (the pointer resting on the entry, which leaves nothing focused) gives the entry the keys; Alt+Enter is the window's
			if ( Focused == null && input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) && Input.ModifierMaskDown == 0 && panel.Entry is { Visible: true, Enabled: true } )
			{
				Focus( panel.Entry );
				consumed = true;
			}
			// the entry takes the keys, not the pointer: over the park, off the overlay's panels and controls, the park keeps its wheel, edges and drag
			else if ( consumed && ReferenceEquals( Focused, panel.Entry ) && !Covers( context.Canvas, input.Mouse ) )
				consumed = false;
			if ( ReferenceEquals( Focused, panel.Entry ) && panel.Entry.Text.Length > 0 )
				context.HoverHelp = null;
			return consumed;
		}

		internal override bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient )
		{
			var consumed = base.UpdatePointer( context, input, recipient );
			if ( !IsRemoved && ReferenceEquals( Focused, panel.Entry ) && panel.Entry.Text.Length > 0 )
				context.HoverHelp = null;
			return consumed;
		}
	}
}
