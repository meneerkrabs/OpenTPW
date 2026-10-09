using System.Buffers.Binary;
using System.Text;

namespace OpenTPW.Reverse.Ui;

internal static class SyntheticTests
{
	private static int passed;

	public static void Run()
	{
		var cases = new (string Name, Action Test)[]
		{
			("signed rectangle and 32-bit fields", SignedFields),
			("nested controls preserve duplicate IDs and parent indexes", ParentIndexes),
			("all nineteen command shapes", AllCommands),
			("external-parent properties stay in outer scope", ExternalProperties),
			("unknown command diagnostic has source address", UnknownCommand),
			("truncated halfword diagnostics", Truncated),
			("unsupported control allocation", UnsupportedControl),
			("incompatible and unknown implied parents", ParentFailures),
			("duplicate implied children reject ambiguous allocation", DuplicateChild),
			("geometry subtype and array limits", GeometryFailures),
			("light index bounds", LightBounds),
			("word and control budgets", ResourceBudgets),
			("depth budget", DepthBudget),
			("input/origin limits and explicit prefix mode", InputBounds),
			("ordered properties retain repeated assignments", RepeatedProperties),
			("signed-byte root-name hashing", NameHash),
			("known source claims do not manufacture font bindings", FontMetadataGuard),
			("edition metadata maps explicit indexes", LabelMetadata),
			("unknown and mismatched label identities reject", LabelIdentityFailure)
		};
		foreach ( var item in cases )
		{
			item.Test();
			passed++;
			Console.WriteLine( $"PASS {item.Name}" );
		}
		Console.WriteLine( $"Synthetic tests: {passed} passed, 0 failed, 0 skipped." );
	}

	private static byte[] Words( IEnumerable<int> values )
	{
		var words = values.ToArray();
		var bytes = new byte[words.Length * 2];
		for ( var i = 0; i < words.Length; i++ )
			BinaryPrimitives.WriteUInt16BigEndian( bytes.AsSpan( i * 2, 2 ), unchecked((ushort)words[i]) );
		return bytes;
	}

	private static int[] Header( int type = 1, int id = 123 ) => new[]
	{
		0, type, 1, 0, id & 65535, unchecked((int)(uint)id >> 16) & 65535, -4, -3, 100, 200
	};

	private static byte[] Control( int type, params int[] body ) => Words( Header( type ).Concat( body ).Concat( new[] { 5, 5 } ) );

	private static void IsDiagnostic( Action action, OriginalLayoutDiagnostic expected, int? offset = null )
	{
		try { action(); }
		catch ( OriginalLayoutException error )
		{
			Program.Equal( error.Diagnostic, expected, "diagnostic kind" );
			if ( offset.HasValue ) Program.Equal( error.DataOffset, offset.Value, "diagnostic address" );
			return;
		}
		throw new InvalidOperationException( $"Expected {expected} diagnostic." );
	}

	private static void IsFormatFailure( Action action )
	{
		try { action(); }
		catch ( FormatException ) { return; }
		throw new InvalidOperationException( "Expected identity/format rejection." );
	}

	private static void SignedFields()
	{
		var words = Header( 1, -2 );
		words[2] = -1;
		words[3] = -1;
		var document = OriginalLayoutReader.Read( Words( words.Concat( new[] { 17, 2, 1, 18, -1, -1, 5, 5 } ) ),
			new OriginalLayoutOrigin( null, 4096 ) );
		var control = document.Controls[0];
		Program.Equal( control.Id, -2, "signed ID" );
		Program.Equal( control.Attributes, uint.MaxValue, "unsigned attributes" );
		Program.Equal( control.DataOffset, 4096, "source base" );
		Program.Equal( control.Bounds, new OriginalRect( -4, -3, 100, 200 ), "signed rectangle" );
		Program.Equal( control.Body.Commands[0].Scalar, 65538, "low/high scalar" );
		Program.Equal( control.Body.Commands[1].Scalar, -1, "sentinel scalar" );
	}

	private static void ParentIndexes()
	{
		var data = Words( Header().Concat( Header( 1, 7 ) ).Concat( new[] { 5 } )
			.Concat( Header( 1, 7 ) ).Concat( Header( 1, 7 ) ).Concat( new[] { 5, 5, 5, 5 } ) );
		var document = OriginalLayoutReader.Read( data );
		Program.Equal( document.Controls.Count, 4, "allocation count" );
		Program.Equal( document.Controls[1].ParentIndex, 0, "first child parent" );
		Program.Equal( document.Controls[2].ParentIndex, 0, "second child parent" );
		Program.Equal( document.Controls[3].ParentIndex, 2, "nested duplicate ID parent" );
	}

	private static void AllCommands()
	{
		var codes = new HashSet<ushort>();
		void Collect( OriginalScope scope )
		{
			foreach ( var item in scope.Commands )
			{
				codes.Add( item.Code );
				if ( item.Control is not null ) Collect( item.Control.Body );
			}
		}
		Collect( OriginalLayoutReader.Read( Control( 1, 1, 1, 0, 2, 2, 0, 3, -1, -2, 3, 4,
			4, 1, 1, 2, 3, 4, 17, 3, 0, 18, 4, 0 ) ).OuterScope );
		foreach ( var (code, parent) in new[] { (6, 3), (7, 3), (8, 3), (9, 4), (12, 7), (14, 12), (15, 12), (16, 13) } )
		{
			var payload = new List<int> { code };
			if ( code is 12 or 16 ) payload.Add( 1 );
			payload.AddRange( new[] { 1, 2, 30, 40, 5 } );
			var document = OriginalLayoutReader.Read( Control( parent, payload.ToArray() ) );
			Program.Equal( document.Controls.Count, 2, "implied child allocation" );
			Collect( document.OuterScope );
		}
		Collect( OriginalLayoutReader.Read( Control( 4, 10, 1, 2, 3, 4 ) ).OuterScope );
		Collect( OriginalLayoutReader.Read( Control( 7, 11, 2, 1, 2, 3, 4 ) ).OuterScope );
		Collect( OriginalLayoutReader.Read( Control( 11, 13, -2, 7 ) ).OuterScope );
		if ( !codes.SetEquals( Enumerable.Range( 0, 19 ).Select( value => (ushort)value ) ) )
			throw new InvalidOperationException( "Synthetic cases do not exercise every proved command shape." );
		foreach ( var subtype in new[] { 2, 3, 4 } )
		{
			var values = subtype == 2 ? new[] { 4, 2, 1, 2, 3 } : subtype == 3
				? new[] { 4, 3, 1, 2, 3, 4 } : new[] { 4, 4, 2, -1, -2, 3, 4 };
			var geometry = OriginalLayoutReader.Read( Control( 1, values ) ).Controls[0].Body.Commands[0].Geometry!;
			Program.Equal( (int)geometry.Subtype, subtype, "geometry subtype preserved" );
		}
	}

	private static void ExternalProperties()
	{
		var document = OriginalLayoutReader.Read( Words( new[] { 3, -2, -1, 4, 5, 17, -1, -1, 5 } ) );
		Program.Equal( document.Controls.Count, 0, "no invented outer parent control" );
		Program.Equal( document.OuterScope.Commands[0].Rectangle, new OriginalRect( -2, -1, 4, 5 ), "outer rectangle" );
	}

	private static void UnknownCommand() => IsDiagnostic( () => OriginalLayoutReader.Read( Words( new[] { 19 } ),
		new OriginalLayoutOrigin( null, 8192 ) ), OriginalLayoutDiagnostic.UnknownCommand, 8192 );

	private static void Truncated()
	{
		IsDiagnostic( () => OriginalLayoutReader.Read( new byte[] { 0 } ), OriginalLayoutDiagnostic.TruncatedWord, 0 );
		IsDiagnostic( () => OriginalLayoutReader.Read( Words( new[] { 0, 1 } ) ), OriginalLayoutDiagnostic.TruncatedWord, 4 );
	}

	private static void UnsupportedControl()
	{
		foreach ( var type in new[] { 0, 14, 65535 } )
			IsDiagnostic( () => OriginalLayoutReader.Read( Control( type ) ), OriginalLayoutDiagnostic.UnsupportedControlType, 0 );
	}

	private static void ParentFailures()
	{
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1, 6 ) ), OriginalLayoutDiagnostic.IncompatibleParent );
		IsDiagnostic( () => OriginalLayoutReader.Read( Words( new[] { 10, 1, 2, 3, 4, 5 } ) ), OriginalLayoutDiagnostic.UnknownExternalParentType );
	}

	private static void DuplicateChild() => IsDiagnostic( () => OriginalLayoutReader.Read( Control( 3,
		6, 1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 5 ) ), OriginalLayoutDiagnostic.DuplicateImpliedChild );

	private static void GeometryFailures()
	{
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1, 4, 5 ) ), OriginalLayoutDiagnostic.UnknownGeometrySubtype );
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1, 4, 4, -1 ) ), OriginalLayoutDiagnostic.ArrayLimit );
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 7, 11, 513 ) ), OriginalLayoutDiagnostic.ArrayLimit );
	}

	private static void LightBounds()
	{
		foreach ( var index in new[] { -1, 32 } )
			IsDiagnostic( () => OriginalLayoutReader.Read( Control( 13, 16, index ) ), OriginalLayoutDiagnostic.InvalidLightIndex );
	}

	private static void ResourceBudgets()
	{
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1 ), limits: new OriginalLayoutLimits( MaximumWords: 3 ) ), OriginalLayoutDiagnostic.WordLimit );
		var data = Words( Header().Concat( Header( 1, 7 ) ).Concat( new[] { 5, 5, 5 } ) );
		IsDiagnostic( () => OriginalLayoutReader.Read( data, limits: new OriginalLayoutLimits( MaximumControls: 1 ) ), OriginalLayoutDiagnostic.ControlLimit );
	}

	private static void DepthBudget() => IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1 ),
		limits: new OriginalLayoutLimits( MaximumDepth: 0 ) ), OriginalLayoutDiagnostic.DepthLimit );

	private static void InputBounds()
	{
		IsDiagnostic( () => OriginalLayoutReader.Read( Control( 1 ), limits: new OriginalLayoutLimits( MaximumInputBytes: 1 ) ), OriginalLayoutDiagnostic.InputLimit );
		IsDiagnostic( () => OriginalLayoutReader.Read( Words( new[] { 5 } ), new OriginalLayoutOrigin( null, int.MaxValue ) ), OriginalLayoutDiagnostic.InvalidOrigin );
		IsDiagnostic( () => OriginalLayoutReader.Read( Words( new[] { 5, 42 } ) ), OriginalLayoutDiagnostic.TrailingData );
		Program.Equal( OriginalLayoutReader.Read( Words( new[] { 5, 42 } ), requireWholeInput: false ).BytesConsumed, 2, "explicit prefix consumption" );
	}

	private static void RepeatedProperties()
	{
		var body = OriginalLayoutReader.Read( Control( 1, 17, 1, 0, 17, 2, 0 ) ).Controls[0].Body;
		Program.Equal( body.Commands.Count, 3, "both ordered assignments and end retained" );
		Program.Equal( body.Commands[0].Scalar, 1, "first assignment" );
		Program.Equal( body.Commands[1].Scalar, 2, "second assignment" );
	}

	private static void NameHash()
	{
		Program.Equal( OriginalNodeNameHash.Compute( Encoding.ASCII.GetBytes( "b_buy" ) ), 557179197, "button root hash" );
		Program.Equal( OriginalNodeNameHash.Compute( Encoding.ASCII.GetBytes( "base" ) ), 468387477, "main panel root hash" );
		Program.Equal( OriginalNodeNameHash.Compute( new byte[] { 255 } ), -47, "signed-byte extension" );
		try { OriginalNodeNameHash.Compute( new byte[] { 0 } ); }
		catch ( ArgumentException ) { return; }
		throw new InvalidOperationException( "Node name hashing accepted a C-string terminator as name content." );
	}

	private static void FontMetadataGuard()
	{
		var document = OriginalLayoutReader.Read( Control( 1 ), new OriginalLayoutOrigin(
			"04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5", 0x4ab38 ) );
		Program.Equal( OriginalLayoutFontMetadata.Describe( document ).Count, 0, "table digest guards constructor metadata" );
	}

	private static OriginalLabelIdentity MacIdentity() => new( OriginalUiEdition.FeralMacAmerican,
		"c3768d1f448c0f85952ae3edda41da750081e85996eb76750df53fad7688bdef",
		"f682ed03507d6ea20f5f1689c321f32b52f8a21e838e9440fbc3207e7236b67d", 474 );

	private static void LabelMetadata()
	{
		var mac = MacIdentity();
		var windows = new OriginalLabelIdentity( OriginalUiEdition.WindowsBaselineEnglish,
			"3fe8b89c994bdd177b7226a51f24222621cc7e27beee94668942dc1f821137cf",
			"69f23492ef61a27ed79dd4df67978536720d403f7e2733acb6b43e6f4f78c587", 473 );
		Program.Equal( OriginalUiLabelResolver.Describe( mac, OriginalUiLabel.GameOptions ).RawIndex, 315, "Mac options index" );
		Program.Equal( OriginalUiLabelResolver.Describe( windows, OriginalUiLabel.GameOptions ).RawIndex, 314, "PC options index" );
		Program.Equal( OriginalUiLabelResolver.Describe( mac, OriginalUiLabel.ParkRating ).RawIndex, 190, "early Mac index" );
		Program.Equal( OriginalUiLabelResolver.Describe( windows, OriginalUiLabel.ParkRating ).RawIndex, 190, "early PC index" );
		Program.Equal( OriginalUiLabelResolver.Describe( mac, OriginalUiLabel.SecondaryScrollMechanism ).RawIndex, 330, "Mac scroll description" );
	}

	private static void LabelIdentityFailure()
	{
		IsFormatFailure( () => OriginalUiLabelResolver.Describe( MacIdentity() with { EntryCount = 473 }, OriginalUiLabel.GameOptions ) );
		IsFormatFailure( () => OriginalUiLabelResolver.Identify( new byte[12], new byte[8] ) );
		var strings = new byte[16];
		"BFST"u8.CopyTo( strings );
		BinaryPrimitives.WriteUInt32LittleEndian( strings.AsSpan( 8, 4 ), 1 );
		var characters = new byte[10];
		"BFMU"u8.CopyTo( characters );
		BinaryPrimitives.WriteUInt16LittleEndian( characters.AsSpan( 6, 2 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( characters.AsSpan( 8, 2 ), 'A' );
		IsFormatFailure( () => OriginalUiLabelResolver.Identify( strings, characters ) );
	}
}
