using System.Buffers.Binary;
using System.Text.Json;

namespace OpenTPW.Reverse.Ui;

internal static class Program
{
	private static int Main( string[] args )
	{
		try
		{
			if ( args.Length == 0 || args.SequenceEqual( new[] { "--self-test" } ) )
			{
				SyntheticTests.Run();
				return 0;
			}
			if ( args.Length == 2 && args[0] == "--verify-private" )
			{
				VerifyPrivate( args[1] );
				return 0;
			}
			Console.Error.WriteLine( "Usage: --self-test | --verify-private /off-git/manifest.private.json" );
			return 2;
		}
		catch ( Exception error ) when ( error is FormatException or IOException or ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException )
		{
			Console.Error.WriteLine( error.Message );
			return 1;
		}
	}

	internal static void Equal<T>( T actual, T expected, string context )
	{
		if ( !EqualityComparer<T>.Default.Equals( actual, expected ) )
			throw new InvalidOperationException( $"{context}: got {actual}, expected {expected}" );
	}

	private static byte[] ReadBounded( string path, int limit )
	{
		using var input = File.OpenRead( path );
		if ( input.Length > limit ) throw new FormatException( "Private input exceeds its byte limit." );
		var data = new byte[(int)input.Length];
		input.ReadExactly( data );
		return data;
	}

	private static void VerifyPrivate( string manifestPath )
	{
		using var manifest = JsonDocument.Parse( ReadBounded( manifestPath, 4 * 1024 * 1024 ),
			new JsonDocumentOptions { MaxDepth = 64 } );
		var root = manifest.RootElement;
		Equal( root.GetProperty( "schemaVersion" ).GetInt32(), 1, "manifest schema" );
		Equal( root.GetProperty( "executableSha256" ).GetString(),
			"04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5", "source executable identity" );
		var tables = root.GetProperty( "tables" );
		Equal( tables.GetArrayLength(), 55, "identified table count" );
		var seen = new HashSet<int>();
		var total = 0;
		var fontBindings = 0;
		foreach ( var entry in tables.EnumerateArray() )
		{
			var offset = entry.GetProperty( "sourceDataOffset" ).GetInt32();
			if ( !seen.Add( offset ) ) throw new FormatException( "Duplicate table address in private manifest." );
			var data = ReadBounded( entry.GetProperty( "path" ).GetString()!, 8192 );
			var document = OriginalLayoutReader.Read( data,
				new OriginalLayoutOrigin( root.GetProperty( "executableSha256" ).GetString(), offset ) );
			Equal( document.TableSha256, entry.GetProperty( "sha256" ).GetString(), "table identity" );
			Equal( document.BytesConsumed / 2, entry.GetProperty( "wordsConsumed" ).GetInt32(), "table consumption" );
			var expected = entry.GetProperty( "controls" );
			Equal( document.Controls.Count, expected.GetArrayLength(), "table control count" );
			for ( var i = 0; i < document.Controls.Count; i++ )
			{
				var control = document.Controls[i];
				var row = expected[i];
				Equal( control.DataOffset, row.GetProperty( "data_offset" ).GetInt32(), "control address" );
				Equal( (int)control.Type, row.GetProperty( "type" ).GetInt32(), "control type" );
				Equal( control.Attributes, unchecked((uint)row.GetProperty( "attributes" ).GetInt32()), "attribute bits" );
				Equal( control.Id, row.GetProperty( "id" ).GetInt32(), "control ID" );
				Equal( (int)control.OriginCommand, row.GetProperty( "origin_command" ).GetInt32(), "control creation command" );
				CheckRectangle( control.Bounds, row.GetProperty( "rectangle" ) );
				int? parent = control.ParentIndex.HasValue ? document.Controls[control.ParentIndex.Value].Id : null;
				var expectedParent = row.GetProperty( "parent" );
				Equal( parent, expectedParent.ValueKind == JsonValueKind.Null ? null : expectedParent.GetInt32(), "parent ID" );
				CheckProperties( control.Body, row );
			}
			CheckProperties( document.OuterScope, entry.GetProperty( "externalProperties" ) );
			total += document.Controls.Count;
			fontBindings += OriginalLayoutFontMetadata.Describe( document ).Count;
		}
		Equal( total, 934, "aggregate control count" );
		Equal( fontBindings, 3, "identified constructor font bindings" );
		var labelCount = 0;
		foreach ( var resources in root.GetProperty( "labelResources" ).EnumerateArray() )
		{
			var strings = ReadBounded( resources.GetProperty( "strings" ).GetString()!, 16 * 1024 * 1024 );
			var characters = ReadBounded( resources.GetProperty( "characters" ).GetString()!, 518 );
			var identity = OriginalUiLabelResolver.Identify( strings, characters );
			Equal( identity.Edition.ToString(), resources.GetProperty( "edition" ).GetString(), "label edition" );
			var options = OriginalUiLabelResolver.Describe( identity, OriginalUiLabel.GameOptions );
			Equal( options.RawIndex, identity.Edition == OriginalUiEdition.FeralMacAmerican ? 315 : 314, "edition-specific options index" );
			Equal( OriginalUiLabelResolver.ReadText( options, strings, characters ), "Game Options", "identified options label" );
			labelCount++;
		}
		Console.WriteLine( $"Verified 55 private tables, {total} controls, {fontBindings} font bindings, {labelCount} identified label editions." );
	}

	private static void CheckRectangle( OriginalRect rectangle, JsonElement expected )
	{
		Equal( expected.GetArrayLength(), 4, "rectangle arity" );
		Equal( (int)rectangle.Left, expected[0].GetInt32(), "left" );
		Equal( (int)rectangle.Top, expected[1].GetInt32(), "top" );
		Equal( (int)rectangle.Right, expected[2].GetInt32(), "right" );
		Equal( (int)rectangle.Bottom, expected[3].GetInt32(), "bottom" );
	}

	private static void CheckProperties( OriginalScope scope, JsonElement expected )
	{
		var properties = scope.Commands.Where( item => item.Control is null && item.Code != 5 )
			.GroupBy( item => item.Code ).ToDictionary( group => group.Key, group => group.Last() );
		var expectedKeys = expected.EnumerateObject().Where( item => item.Name.StartsWith( "command_", StringComparison.Ordinal ) )
			.Select( item => ushort.Parse( item.Name[8..], System.Globalization.CultureInfo.InvariantCulture ) ).ToHashSet();
		if ( !expectedKeys.SetEquals( properties.Keys ) ) throw new InvalidOperationException( "Effective property command set differs." );
		foreach ( var code in expectedKeys )
		{
			var value = expected.GetProperty( $"command_{code}" );
			var item = properties[code];
			if ( item.Scalar.HasValue ) Equal( item.Scalar.Value, value.GetInt32(), "scalar property" );
			else if ( item.Rectangle.HasValue ) CheckRectangle( item.Rectangle.Value, value );
			else if ( item.Geometry is not null )
			{
				Equal( (int)item.Geometry.Subtype, value.GetProperty( "subtype" ).GetInt32(), "geometry subtype" );
				var values = value.GetProperty( "values" );
				if ( item.Geometry.Subtype == 4 )
				{
					Equal( item.Geometry.Points.Count, values.GetArrayLength(), "polygon point count" );
					for ( var i = 0; i < values.GetArrayLength(); i++ )
					{
						Equal( (int)item.Geometry.Points[i].X, values[i][0].GetInt32(), "polygon X" );
						Equal( (int)item.Geometry.Points[i].Y, values[i][1].GetInt32(), "polygon Y" );
					}
				}
				else
				{
					Equal( item.Geometry.Scalars.Count, values.GetArrayLength(), "scalar geometry arity" );
					for ( var i = 0; i < values.GetArrayLength(); i++ ) Equal( (int)item.Geometry.Scalars[i], values[i].GetInt32(), "geometry parameter" );
				}
			}
			else if ( item.Pairs is not null )
			{
				if ( code == 13 )
				{
					Equal( (int)item.Pairs[0].First, value[0].GetInt32(), "paired parameter 1" );
					Equal( (int)item.Pairs[0].Second, value[1].GetInt32(), "paired parameter 2" );
				}
				else
				{
					Equal( item.Pairs.Count, value.GetArrayLength(), "column pair count" );
					for ( var i = 0; i < item.Pairs.Count; i++ )
					{
						Equal( (int)item.Pairs[i].First, value[i][0].GetInt32(), "column first" );
						Equal( (int)item.Pairs[i].Second, value[i][1].GetInt32(), "column second" );
					}
				}
			}
			else throw new InvalidOperationException( "Property has no metadata payload." );
		}
	}
}
