using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:object-lights] Light dummies in models, the content kit's way of writing them, the object's light bank and the four light opcodes (docs/LIGHTS.md). No GPU and no game data
/// (the example mod's end-to-end test uses a stand-in game folder).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectLightTests
{
	// ---- the model format ----

	private static ModelBuilder Lamp( Action<ModelBuilder, ModelNodeBuilder>? extra = null )
	{
		var builder = new ModelBuilder { SourceName = "lamp" };
		var body = builder.AddMesh( "body" );
		var material = body.Mesh!.AddMaterial( "quad_tex", wrap: true );
		var normal = new NVector3( 0, 0, 1 );
		var a = material.AddVertex( new NVector3( 0, 0, 0 ), normal, new NVector2( 0, 0 ) );
		var b = material.AddVertex( new NVector3( 4, 0, 0 ), normal, new NVector2( 1, 0 ) );
		var c = material.AddVertex( new NVector3( 4, 2, 0 ), normal, new NVector2( 1, 1 ) );
		var d = material.AddVertex( new NVector3( 0, 2, 0 ), normal, new NVector2( 0, 1 ) );
		material.AddQuad( a, b, c, d );
		extra?.Invoke( builder, body );
		return builder;
	}

	[TestMethod]
	public void ALightDummyWritesAndReadsBackWithItsNumberColourRadiusAndNode()
	{
		var model = Lamp( ( builder, body ) =>
		{
			builder.AddDummy( "lamp_a", body, Matrix4x4.CreateTranslation( 1, 5, 2 ) ).AddLight( 3, new NVector3( 1f, 0.5f, 0.25f ), 60 );
			builder.AddDummy( "lamp_b", body, Matrix4x4.CreateTranslation( 3, 5, 2 ) ).AddLight( 0, new NVector3( 0.1f, 0.2f, 0.3f ) );
		} ).Build();

		var lights = ModelLights.Read( model );
		Assert.AreEqual( 2, lights.Count );
		var first = lights.Single( light => light.Number == 3 );
		Assert.AreEqual( "lamp_a", model.Nodes[first.NodeIndex].Name );
		Assert.AreEqual( new NVector3( 1f, 0.5f, 0.25f ), first.Colour );
		Assert.AreEqual( 60, first.RadiusModelUnits );
		var second = lights.Single( light => light.Number == 0 );
		Assert.AreEqual( "lamp_b", model.Nodes[second.NodeIndex].Name );
		Assert.AreEqual( new NVector3( 0.1f, 0.2f, 0.3f ), second.Colour );
		Assert.IsNull( second.RadiusModelUnits, "a radius of 0 is the default" );
		var record = model.DummyAttributes[first.NodeIndex - model.Counts.Unknown46];
		Assert.AreEqual( ModelBuilder.LightType, record.Type );
		Assert.AreEqual( ModelDummyAttribute.LightFlag | ModelDummyAttribute.DataFlag, record.Type );
		Assert.AreEqual( 3u, record.Value );
	}

	[TestMethod]
	public void LightsAndRiderHeadsShareOneAttributeTable()
	{
		var model = Lamp( ( builder, body ) =>
		{
			builder.AddDummy( "seat", body ).AddHead( 1 );
			builder.AddDummy( "bulb", body ).AddLight( 0, NVector3.One, 40 );
			builder.AddDummy( "seat2", body ).AddHead( 2 );
		} ).Build();
		var lights = ModelLights.Read( model );
		Assert.AreEqual( 1, lights.Count );
		Assert.AreEqual( "bulb", model.Nodes[lights[0].NodeIndex].Name );
		Assert.AreEqual( 2, model.DummyAttributes.Count( attribute => (attribute.Type & 0x80) != 0 ), "both heads stay head records" );
		Assert.AreEqual( 2, RideHeadSpace.Create( model, seed: 1 )!.Count );
		// the written bytes read back to the same model
		var again = new ModelFile( new MemoryStream( ModelFileWriter.Write( model ) ) );
		Assert.AreEqual( 1, ModelLights.Read( again ).Count );
	}

	[TestMethod]
	public void AModelWithoutLightsHasNone()
	{
		Assert.AreEqual( 0, ModelLights.Read( Lamp().Build() ).Count );
		var heads = Lamp( ( builder, body ) => builder.AddDummy( "seat", body ).AddHead( 1 ) ).Build();
		Assert.AreEqual( 0, ModelLights.Read( heads ).Count, "a head record is not a light" );
	}

	[TestMethod]
	public void TheBuilderRefusesWhatALightRecordCannotHold()
	{
		Assert.ThrowsException<InvalidOperationException>( () => Lamp( ( builder, body ) =>
		{
			builder.AddDummy( "a", body ).AddLight( 1, NVector3.One );
			builder.AddDummy( "b", body ).AddLight( 1, NVector3.One );
		} ).Build(), "light numbers are unique" );
		Assert.ThrowsException<InvalidOperationException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddHead( 1 ).AddLight( 0, NVector3.One ) ), "a node has one attribute record" );
		Assert.ThrowsException<InvalidOperationException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 0, NVector3.One ).AddHead( 1 ) ) );
		Assert.ThrowsException<InvalidOperationException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 0, NVector3.One ).AddLight( 1, NVector3.One ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( -1, NVector3.One ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 40000, NVector3.One ) ) );
		Assert.ThrowsException<ArgumentException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 0, new NVector3( 1.5f, 0, 0 ) ) ) );
		Assert.ThrowsException<ArgumentException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 0, new NVector3( float.NaN, 0, 0 ) ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Lamp( ( builder, body ) => builder.AddDummy( "a", body ).AddLight( 0, NVector3.One, 70000 ) ) );
	}

	/// <summary>Needs <c>OPENTPW_GAME_PATH</c>: the claim of docs/LIGHTS.md that the shipped models carry no attribute of the light class.</summary>
	[TestMethod]
	public void NoShippedGeometryModelHasALightDummy()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		int geometry = 0, attributes = 0;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				var data = file.GetData();
				file.Free();
				ModelFile model;
				try
				{
					model = new ModelFile( new MemoryStream( data ) );
				}
				catch ( NotSupportedException )
				{
					continue;
				}
				if ( model.Kind != ModelFileKind.Geometry )
					continue;
				geometry++;
				attributes += model.DummyAttributes.Count;
				Assert.AreEqual( 0, model.DummyAttributes.Count( attribute => (attribute.Type & ModelDummyAttribute.LightFlag) != 0 ), $"{Path.GetFileName( wad )}/{name} has a light dummy" );
				Assert.AreEqual( 0, ModelLights.Read( model ).Count, $"{Path.GetFileName( wad )}/{name}" );
			}
		}
		Console.WriteLine( $"{geometry} geometry members, {attributes} attribute records, none of the light class (0x20000)." );
		Assert.IsTrue( geometry > 800 );
	}

	// ---- the glTF converter ----

	private static GltfModelConversion Convert( Func<GlbBuilder, int, int[]> nodes )
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "tile", glb.AddImage( "tile" ) );
		var quad = glb.AddNode( "quad", glb.AddMesh( "quad", GlbBuilder.Quad( material ) ) );
		var children = nodes( glb, quad );
		var root = glb.AddNode( "lamp", -1, null, null, new[] { quad }.Concat( children ).ToArray() );
		return GltfModelConverter.Convert( GltfScene.Read( glb.Build( root ) ) );
	}

	[TestMethod]
	public void APointLightOfTheGltfBecomesALightDummyWithItsColourAndRangeInModelUnits()
	{
		var conversion = Convert( ( glb, _ ) => new[] { glb.AddNode( "light0", -1, new NVector3( 5, 17, -5 ), null, null, "\"opentpw_light\": 0", glb.AddLight( "point", new NVector3( 1, 0.85f, 0.55f ), 1f, 60f ) ) } );
		var model = conversion.Model.Build();
		var light = ModelLights.Read( model ).Single();
		Assert.AreEqual( 0, light.Number );
		Assert.AreEqual( "light0", model.Nodes[light.NodeIndex].Name );
		Assert.AreEqual( new NVector3( 1, 0.85f, 0.55f ), light.Colour );
		Assert.AreEqual( 60, light.RadiusModelUnits, "a scene unit is a model unit" );
		var transform = model.Nodes[light.NodeIndex].Transform;
		Assert.AreEqual( new NVector3( 5, 17, 5 ), transform.Translation, "the dummy's place is the glTF node's, mirrored in Z like every node" );
		Assert.AreEqual( 0, conversion.Warnings.Count, string.Join( "; ", conversion.Warnings ) );
	}

	[TestMethod]
	public void OnlyAPointLightWithTheNumberExtraBecomesADummyAndTheOthersAreSkippedWithAWarning()
	{
		var conversion = Convert( ( glb, _ ) => new[]
		{
			glb.AddNode( "default_light", -1, null, null, null, null, glb.AddLight() ),
			glb.AddNode( "b", -1, null, null, null, "\"opentpw_light\": 4", glb.AddLight() ),
			glb.AddNode( "d", -1, null, null, null, "\"opentpw_light\": 5", glb.AddLight() ),
		} );
		var model = conversion.Model.Build();
		var numbers = ModelLights.Read( model ).ToDictionary( light => model.Nodes[light.NodeIndex].Name, light => light.Number );
		CollectionAssert.AreEquivalent( new Dictionary<string, int> { ["b"] = 4, ["d"] = 5 }.ToArray(), numbers.ToArray() );
		Assert.AreEqual( 1, conversion.Warnings.Count( warning => warning.Contains( "'default_light'" ) && warning.Contains( "without the extra opentpw_light" ) ) );
	}

	[TestMethod]
	public void TheRadiusExtraBeatsTheRangeAndNoRangeMeansTheDefault()
	{
		var conversion = Convert( ( glb, _ ) => new[]
		{
			glb.AddNode( "ranged", -1, null, null, null, "\"opentpw_light\": 0", glb.AddLight( range: 40 ) ),
			glb.AddNode( "extra", -1, null, null, null, "\"opentpw_light\": 1, \"opentpw_light_radius\": 75", glb.AddLight( range: 40 ) ),
			glb.AddNode( "open", -1, null, null, null, "\"opentpw_light\": 2", glb.AddLight() ),
		} );
		var model = conversion.Model.Build();
		var radii = ModelLights.Read( model ).ToDictionary( light => model.Nodes[light.NodeIndex].Name, light => light.RadiusModelUnits );
		Assert.AreEqual( 40, radii["ranged"] );
		Assert.AreEqual( 75, radii["extra"] );
		Assert.IsNull( radii["open"] );
	}

	[TestMethod]
	public void SpotAndSunLightsAreSkippedWithAWarningAndTheIntensityIsNotCarried()
	{
		var conversion = Convert( ( glb, _ ) => new[]
		{
			glb.AddNode( "spot", -1, null, null, null, null, glb.AddLight( "spot" ) ),
			glb.AddNode( "sun", -1, null, null, null, null, glb.AddLight( "directional" ) ),
			glb.AddNode( "bright", -1, null, null, null, "\"opentpw_light\": 0", glb.AddLight( intensity: 800 ) ),
			glb.AddNode( "bright2", -1, null, null, null, "\"opentpw_light\": 1", glb.AddLight( intensity: 800 ) ),
		} );
		var model = conversion.Model.Build();
		Assert.AreEqual( 2, ModelLights.Read( model ).Count );
		Assert.AreEqual( 1, conversion.Warnings.Count( warning => warning.Contains( "'spot'" ) && warning.Contains( "spot light is skipped" ) ) );
		Assert.AreEqual( 1, conversion.Warnings.Count( warning => warning.Contains( "'sun'" ) && warning.Contains( "directional light is skipped" ) ) );
		Assert.AreEqual( 1, conversion.Warnings.Count( warning => warning.Contains( "intensity is not carried" ) ), "once for the file");
	}

	[TestMethod]
	public void AColourOutsideZeroToOneIsClampedWithAWarning()
	{
		var conversion = Convert( ( glb, _ ) => new[] { glb.AddNode( "hot", -1, null, null, null, "\"opentpw_light\": 0", glb.AddLight( colour: new NVector3( 2f, 0.5f, -0.5f ) ) ) } );
		Assert.AreEqual( new NVector3( 1f, 0.5f, 0f ), ModelLights.Read( conversion.Model.Build() ).Single().Colour );
		Assert.IsTrue( conversion.Warnings.Any( warning => warning.Contains( "clamped" ) ) );
	}

	[TestMethod]
	public void TheConverterRefusesALightThatConflictsOrIsMisnamed()
	{
		void Refused( Func<GlbBuilder, int[]> nodes, string message )
		{
			var error = Assert.ThrowsException<InvalidDataException>( () => Convert( ( glb, _ ) => nodes( glb ) ) );
			StringAssert.Contains( error.Message, message );
		}
		Refused( glb => new[] { glb.AddNode( "x", -1, null, null, null, "\"opentpw_light\": 1", glb.AddLight() ), glb.AddNode( "y", -1, null, null, null, "\"opentpw_light\": 1", glb.AddLight() ) }, "used by two nodes" );
		Refused( glb => new[] { glb.AddNode( "x", -1, null, null, null, "\"opentpw_light\": 1" ) }, "needs a KHR_lights_punctual point light" );
		Refused( glb => new[] { glb.AddNode( "x", -1, null, null, null, "\"opentpw_light\": -2", glb.AddLight() ) }, "integer from 0 to 32767" );
		Refused( glb => new[] { glb.AddNode( "x", -1, null, null, null, "\"opentpw_light\": 0, \"opentpw_light_radius\": 0", glb.AddLight() ) }, "opentpw_light_radius" );
		Refused( glb => new[] { glb.AddNode( "x", -1, null, null, null, "\"opentpw_head\": 1, \"opentpw_light\": 0", glb.AddLight() ) }, "rider head and a light" );
	}

	[TestMethod]
	public void TheLightsExtensionMayBeRequiredAndAnyOtherRequiredExtensionIsStillRefused()
	{
		var scene = GltfSceneTests.Triangle( "", ", \"extensionsRequired\": [ \"KHR_lights_punctual\" ]" );
		Assert.AreEqual( 3, GltfScene.Read( scene ).Nodes.Count );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( GltfSceneTests.Triangle( "", ", \"extensionsRequired\": [ \"KHR_draco_mesh_compression\" ]" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( GltfSceneTests.Triangle( "", ", \"extensionsRequired\": [ \"KHR_lights_punctual\", \"KHR_materials_unlit\" ]" ) ) );
	}

	[TestMethod]
	public void WronglyTypedLightJsonIsAnInvalidDataExceptionNotACrash()
	{
		void Bad( string extensions, string nodeExtension = "" )
		{
			var json = $$"""
			{ "asset": { "version": "2.0" }, "scene": 0, "scenes": [ { "nodes": [ 0 ] } ], {{extensions}}
			  "nodes": [ { "name": "n"{{nodeExtension}} } ] }
			""";
			Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( GltfSceneTests.Glb( json, Array.Empty<byte>() ) ), json );
		}
		Bad( "\"extensions\": 5," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": 5 }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": 5 } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ 5 ] } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": 3 } ] } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": \"point\", \"color\": [ 1, \"x\", 1 ] } ] } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": \"point\", \"color\": 1 } ] } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": \"point\", \"range\": \"far\" } ] } }," );
		Bad( "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": \"point\", \"intensity\": [ 1 ] } ] } }," );
		const string valid = "\"extensions\": { \"KHR_lights_punctual\": { \"lights\": [ { \"type\": \"point\" } ] } },";
		Bad( valid, ", \"extensions\": 4" );
		Bad( valid, ", \"extensions\": { \"KHR_lights_punctual\": 4 }" );
		Bad( valid, ", \"extensions\": { \"KHR_lights_punctual\": { \"light\": \"0\" } }" );
		Bad( valid, ", \"extensions\": { \"KHR_lights_punctual\": { } }" );
		Bad( valid, ", \"extensions\": { \"KHR_lights_punctual\": { \"light\": 3 } }" );
		Bad( "\"extensionsRequired\": \"KHR_lights_punctual\"," );
		Bad( "\"extensionsRequired\": [ 7 ]," );
	}

	// ---- the object's light bank ----

	private static (ModelFile Model, ObjectSettings Settings) Pair( string sam, int? radius = 60 )
	{
		var model = Lamp( ( builder, body ) =>
		{
			builder.AddDummy( "l0", body, Matrix4x4.CreateTranslation( 2, 6, 3 ) ).AddLight( 0, new NVector3( 1f, 0.8f, 0.4f ), radius ?? 0 );
			builder.AddDummy( "l7", body, Matrix4x4.CreateTranslation( 4, 6, 3 ) ).AddLight( 7, NVector3.One );
		} ).Build();
		return (model, new ObjectSettings( new[] { new ObjectSettingsFile( sam ) } ));
	}

	[TestMethod]
	public void TheBankStartsFromTheModelsColourAndRadiusWithTheSamKeysOverThem()
	{
		var (model, settings) = Pair( "Lights[0].Radius\t70\nLights[7].Green\t50\nLights[7].Blue\t25\nLights[7].On\t0\n" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		var first = bank.Find( 0 )!;
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), first.BaseColour, "the model's colour" );
		Assert.AreEqual( 70f, first.RadiusModelUnits, "the .sam radius beats the model's 60" );
		Assert.IsTrue( first.On, "on by default" );
		var second = bank.Find( 7 )!;
		Assert.AreEqual( new NVector3( 1f, 0.5f, 0.25f ), second.BaseColour, "white with two channels set in percent" );
		Assert.AreEqual( PointLightMath.OriginalRadiusModelUnits, second.RadiusModelUnits, "no radius anywhere: the original's 100" );
		Assert.IsFalse( second.On, "Lights[n].On 0 starts it off" );
		Assert.AreEqual( second.BaseColour, second.Colour );
		Assert.IsNull( ObjectLightBank.Create( Lamp().Build(), settings, "none" ), "a model without light dummies has no bank" );
	}

	[TestMethod]
	public void ARadiusBelowOneModelUnitInTheSamIsIgnoredAndTheLightIsUnusableBelowTheMinimum()
	{
		var (model, settings) = Pair( "Lights[0].Radius\t0.5\nLights[7].Radius\t-3\n" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		Assert.AreEqual( 60f, bank.Find( 0 )!.RadiusModelUnits, "the model's radius stays" );
		Assert.AreEqual( PointLightMath.OriginalRadiusModelUnits, bank.Find( 7 )!.RadiusModelUnits );
		Assert.AreEqual( 1f, PointLightMath.MinimumRadiusModelUnits );
		Assert.IsFalse( new PointLight( NVector3.Zero, NVector3.One, 0.05f ).IsUsable, "a radius under one model unit (0.2 engine units) would make 1 / r2 explode" );
		Assert.IsTrue( new PointLight( NVector3.Zero, NVector3.One, PointLightMath.MinimumRadius ).IsUsable );
		Assert.IsTrue( new PointLight( NVector3.Zero, new NVector3( -1, 0, 0 ), 5f ).IsUsable, "a negative colour darkens" );
	}

	[TestMethod]
	public void TheFourOpcodesSwitchDimAndColourTheLightLikeTheOriginalsHandlers()
	{
		var (model, settings) = Pair( "" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		var light = bank.Find( 0 )!;
		Assert.IsTrue( bank.Apply( Opcode.DISABLELIGHT, 0 ) );
		Assert.IsFalse( light.On );
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), light.Colour, "switching leaves the colour" );
		Assert.IsTrue( bank.Apply( Opcode.ENABLELIGHT, 0 ) );
		Assert.IsTrue( light.On );

		// SETLIGHT: base colour x level x 0.01
		Assert.IsTrue( bank.Apply( Opcode.SETLIGHT, 0, 50 ) );
		Assert.AreEqual( 0.5f, light.Colour.X, 1e-6f );
		Assert.AreEqual( 0.4f, light.Colour.Y, 1e-6f );
		Assert.AreEqual( 0.2f, light.Colour.Z, 1e-6f );
		// COLOURLIGHT: the operands x 0.01, and a later SETLIGHT scales the BASE colour again, not this one
		Assert.IsTrue( bank.Apply( Opcode.COLOURLIGHT, 0, 100, 20, 0 ) );
		Assert.AreEqual( 1f, light.Colour.X, 1e-6f );
		Assert.AreEqual( 0.2f, light.Colour.Y, 1e-6f );
		Assert.AreEqual( 0f, light.Colour.Z );
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), light.BaseColour );
		Assert.IsTrue( bank.Apply( Opcode.SETLIGHT, 0, 100 ) );
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), light.Colour );
		// the colour is a float, unclamped like the original's (0x4587E0, 0x458890): over-bright and negative, darkening operands pass
		Assert.IsTrue( bank.Apply( Opcode.SETLIGHT, 0, 300 ) );
		Assert.AreEqual( 3f, light.Colour.X, 1e-6f );
		Assert.AreEqual( 2.4f, light.Colour.Y, 1e-5f );
		Assert.AreEqual( 1.2f, light.Colour.Z, 1e-5f );
		Assert.IsTrue( bank.Apply( Opcode.COLOURLIGHT, 0, -10, 250, 5 ) );
		Assert.AreEqual( -0.1f, light.Colour.X, 1e-6f );
		Assert.AreEqual( 2.5f, light.Colour.Y, 1e-6f );
		Assert.AreEqual( 0.05f, light.Colour.Z, 1e-6f );
		// only the +-4 bound keeps a huge operand finite and sane
		Assert.IsTrue( bank.Apply( Opcode.SETLIGHT, 0, 32767 ) );
		Assert.AreEqual( new NVector3( PointLightMath.ColourBound ), light.Colour );
		Assert.IsTrue( bank.Apply( Opcode.COLOURLIGHT, 0, -32768, 0, 0 ) );
		Assert.AreEqual( -PointLightMath.ColourBound, light.Colour.X );
		// the other light is untouched
		Assert.AreEqual( NVector3.One, bank.Find( 7 )!.Colour );
	}

	[TestMethod]
	public void ALightTheModelDoesNotHaveChangesNothingAndIsNotAFault()
	{
		var (model, settings) = Pair( "" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		var before = bank.Capture();
		foreach ( var opcode in new[] { Opcode.ENABLELIGHT, Opcode.DISABLELIGHT, Opcode.SETLIGHT, Opcode.COLOURLIGHT } )
			Assert.IsFalse( bank.Apply( opcode, 99, 10, 20, 30 ), opcode.ToString() );
		CollectionAssert.AreEqual( before.Select( record => (record.Number, record.On, record.Red, record.Green, record.Blue) ).ToArray(),
			bank.Capture().Select( record => (record.Number, record.On, record.Red, record.Green, record.Blue) ).ToArray() );
		Assert.IsTrue( bank.NoteMissing( Opcode.SETLIGHT, 99 ), "the first miss is logged" );
		Assert.IsFalse( bank.NoteMissing( Opcode.SETLIGHT, 99 ), "and only once per opcode and number" );
		Assert.IsTrue( bank.NoteMissing( Opcode.SETLIGHT, 98 ) );
		Assert.IsTrue( bank.NoteMissing( Opcode.ENABLELIGHT, 99 ) );
	}

	[TestMethod]
	public void LightsFollowTheirNodeInEngineUnitsAndOnlyShineWhileOn()
	{
		var (model, settings) = Pair( "" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		// the dummies hang on the body at the rest pose: node transforms composed (MD2 space, translation in the fourth row)
		var nodes = model.Nodes.Select( node => node.Transform ).ToList();
		var placement = new ObjectPlacement( 3, 2, 0, new NVector2( 100, 50 ), 4f ).ModelToEngine;
		var lights = new List<PointLight>();
		bank.AppendTo( lights, nodes, placement );
		Assert.AreEqual( 2, lights.Count );
		var l0 = lights[0];
		// node l0 at MD2 (2, 6, 3): swapped to the engine (2, 3, 6) model units, +30 for the anchor cell (3, 2) on x and +20 on y, times 0.2, plus the grid origin and the height
		Assert.AreEqual( 100f + (2 + 30) * 0.2f, l0.Position.X, 1e-4f );
		Assert.AreEqual( 50f + (3 + 20) * 0.2f, l0.Position.Y, 1e-4f );
		Assert.AreEqual( 4f + 6 * 0.2f, l0.Position.Z, 1e-4f );
		Assert.AreEqual( 60 * 0.2f, l0.Radius, 1e-5f, "model units to engine units: 60 -> 12, six cells" );
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), l0.Colour );
		bank.Apply( Opcode.DISABLELIGHT, 0 );
		lights.Clear();
		bank.AppendTo( lights, nodes, placement );
		Assert.AreEqual( 1, lights.Count, "an off light is not given to the renderer" );
		// a dummy hidden by its node state puts its light out
		bank.Apply( Opcode.ENABLELIGHT, 0 );
		lights.Clear();
		bank.AppendTo( lights, nodes, placement, node => node == bank.Find( 0 )!.NodeIndex );
		Assert.AreEqual( 1, lights.Count );
		Assert.AreEqual( 4f + 6 * 0.2f, lights[0].Position.Z, 1e-4f, "only the other light (l7, at the same height) is left" );
		Assert.AreEqual( 100f + (4 + 30) * 0.2f, lights[0].Position.X, 1e-4f );
		// a light whose node is not in the pose list is skipped rather than faulting
		lights.Clear();
		bank.AppendTo( lights, nodes.Take( 1 ).ToList(), placement );
		Assert.AreEqual( 0, lights.Count );
	}

	[TestMethod]
	public void TheLightStateRoundTripsThroughTheSnapshotAndIsValidated()
	{
		var (model, settings) = Pair( "" );
		var bank = ObjectLightBank.Create( model, settings, "Lamp" )!;
		bank.Apply( Opcode.COLOURLIGHT, 0, 100, 0, 50 );
		bank.Apply( Opcode.DISABLELIGHT, 7 );
		var saved = bank.Capture();
		var json = JsonSerializer.Serialize( saved );
		var restoredRecords = JsonSerializer.Deserialize<ObjectLightRecord[]>( json )!;

		var fresh = ObjectLightBank.Create( model, settings, "Lamp" )!;
		Assert.AreEqual( 0, fresh.Restore( restoredRecords ).Count );
		Assert.AreEqual( new NVector3( 1f, 0f, 0.5f ), fresh.Find( 0 )!.Colour );
		Assert.IsTrue( fresh.Find( 0 )!.On );
		Assert.IsFalse( fresh.Find( 7 )!.On );
		Assert.AreEqual( new NVector3( 1f, 0.8f, 0.4f ), fresh.Find( 0 )!.BaseColour, "the base colour is the model's, not saved" );
		// Restore applies the same validation as the snapshot's
		Assert.ThrowsException<InvalidDataException>( () => fresh.Restore( new[] { new ObjectLightRecord { Number = 0, On = true, Red = float.NaN, Green = 1, Blue = 1 } } ) );
		Assert.ThrowsException<InvalidDataException>( () => fresh.Restore( new[] { new ObjectLightRecord { Number = 0, Red = 1, Green = 1, Blue = 1 }, new ObjectLightRecord { Number = 0, Red = 1, Green = 1, Blue = 1 } } ) );
		// a number the model no longer has is reported, not an error
		CollectionAssert.AreEqual( new[] { 42 }, fresh.Restore( new[] { new ObjectLightRecord { Number = 42, On = true, Red = 1, Green = 1, Blue = 1 } } ).ToArray() );

		ObjectLightBank.ValidateRecords( saved );
		void Invalid( params ObjectLightRecord[] records ) => Assert.ThrowsException<InvalidDataException>( () => ObjectLightBank.ValidateRecords( records ) );
		Invalid( new ObjectLightRecord { Number = -1, Red = 1, Green = 1, Blue = 1 } );
		Invalid( new ObjectLightRecord { Number = 40000, Red = 1, Green = 1, Blue = 1 } );
		Invalid( new ObjectLightRecord { Number = 1, Red = 1, Green = 1, Blue = 1 }, new ObjectLightRecord { Number = 1, Red = 1, Green = 1, Blue = 1 } );
		Invalid( new ObjectLightRecord { Number = 1, Red = 4.5f, Green = 1, Blue = 1 } );
		Invalid( new ObjectLightRecord { Number = 1, Red = -5, Green = 1, Blue = 1 } );
		ObjectLightBank.ValidateRecords( new[] { new ObjectLightRecord { Number = 1, Red = -2, Green = 3.5f, Blue = 1 } } );
		Invalid( new ObjectLightRecord { Number = 1, Red = float.NaN, Green = 1, Blue = 1 } );
		Invalid( Enumerable.Range( 0, ObjectLightBank.MaximumLights + 1 ).Select( number => new ObjectLightRecord { Number = number, Red = 1, Green = 1, Blue = 1 } ).ToArray() );
	}

	[TestMethod]
	public void TheSnapshotsLightsMemberIsWrittenOnlyForAnObjectWithLights()
	{
		var property = typeof( OriginalObjectRuntimeSnapshot ).GetProperty( nameof( OriginalObjectRuntimeSnapshot.Lights ) )!;
		var ignore = (System.Text.Json.Serialization.JsonIgnoreAttribute)Attribute.GetCustomAttribute( property, typeof( System.Text.Json.Serialization.JsonIgnoreAttribute ) )!;
		Assert.AreEqual( System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, ignore.Condition, "an object without lights saves exactly as before lights existed" );
		Assert.AreEqual( OriginalObjectRuntimeSnapshot.CurrentSchemaVersion, 1, "an optional member needs no schema bump" );
	}

	// ---- the example mod ----

	private const string ExampleId = "opentpw.example.lamp-lights";
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		// saved first, so the cleanup puts back what was there even when the rest of the setup fails
		previousFileSystem = FileSystem;
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-lights-" ).FullName;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// a null file system is put back too: it is the default without game data, and the stand-in game folder (deleted below) left in its place
		// became the game data of every later test in the run
		FileSystem = previousFileSystem!;
		if ( root != null )
			Directory.Delete( root, true );
	}

	private static string ExampleFolder => Path.Combine( ModDiscovery.ProgramModsDirectory( AppContext.BaseDirectory ), ExampleId );

	private ModSet ResolveExample()
	{
		// a stand-in game folder: the mod pipeline needs a file system, the lamp's data comes from the mod
		var game = Path.Combine( root, "game" );
		var totem = Path.Combine( game, "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( game );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var found = ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, AppContext.BaseDirectory ).Single( mod => mod.Id == ExampleId );
		Assert.IsTrue( found.IsValid, found.Error );
		var core = new OpenTpwExtension( "opentpw.core-test", "Core test", new ModVersion( 1, 0, 0 ), true, false );
		var set = ModSet.Resolve( new[] { found }, new ModSettings { Enabled = new Dictionary<string, bool> { [ExampleId] = true } }, null, GameEdition.ThemeParkWorld, new IOpenTpwExtension[] { core } );
		Assert.AreEqual( 1, set.Mods.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		return set;
	}

	[TestMethod]
	public void TheExampleModIsOffByDefaultOfficialDataAndKeepsLfInItsTextFiles()
	{
		Assert.IsFalse( ModSettings.Default.IsEnabled( ExampleId, false ), "off by default" );
		foreach ( var file in Directory.GetFiles( ExampleFolder, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".json" ) || file.EndsWith( ".sam" ) ) )
			Assert.IsFalse( File.ReadAllBytes( file ).Contains( (byte)'\r' ), $"{Path.GetFileName( file )} has a CR: a Windows checkout would change the mod's digest" );
		var found = ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, AppContext.BaseDirectory ).Single( mod => mod.Id == ExampleId );
		Assert.IsTrue( found.IsValid, found.Error );
		Assert.AreEqual( ModCategory.Official, found.Manifest!.Category );
		Assert.IsNull( found.Manifest.Content?.Scripts, "a data mod: no TPW Script" );
	}

	[TestMethod]
	public void TheExampleLampLoadsThroughTheModPipelineWithItsLightAndSettingsAndItsScriptDrivesTheLight()
	{
		var set = ResolveExample();
		using var scope = ModContentRoots.Use( set, FileSystem );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 64001 );
		Assert.AreEqual( "Lamp Post", entry.DisplayName );
		Assert.AreEqual( ObjectCategory.Feature, entry.Category );
		Assert.IsNotNull( entry.ScriptPath );

		var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		var dummy = ModelLights.Read( model ).Single();
		Assert.AreEqual( 0, dummy.Number );
		Assert.AreEqual( 60, dummy.RadiusModelUnits, "the model carries the glTF range" );
		Assert.AreEqual( new NVector3( 1.0f, 0.85f, 0.55f ), dummy.Colour );
		Assert.AreEqual( "light0", model.Nodes[dummy.NodeIndex].Name );

		var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		var light = runtime.Lights!.Find( 0 )!;
		Assert.AreEqual( 70f, light.RadiusModelUnits, "Lights[0].Radius of the object's .sam beats the model's 60" );
		Assert.IsTrue( light.On );

		// ride the script for 20 seconds of fixed ticks and watch what it does to the light
		var colours = new HashSet<(int R, int G, int B)>();
		var sawOff = false;
		var dimmed = false;
		for ( var tick = 0; tick < 20 * 60; tick++ )
		{
			runtime.Simulate( 1.0 / 60 );
			Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
			sawOff |= !light.On;
			dimmed |= light.On && light.Colour.X < 0.7f && light.Colour.X > 0.5f && Math.Abs( light.Colour.Y / light.Colour.X - 0.85f / 1.0f ) < 0.01f;
			colours.Add( ((int)MathF.Round( light.Colour.X * 100 ), (int)MathF.Round( light.Colour.Y * 100 ), (int)MathF.Round( light.Colour.Z * 100 )) );
		}
		Assert.IsTrue( sawOff, "the script flickers the light off" );
		Assert.IsTrue( dimmed, "SETLIGHT scales the model's warm colour down" );
		CollectionAssert.IsSubsetOf( new[] { (100, 20, 20), (20, 100, 20), (20, 20, 100) }, colours.ToArray(), "COLOURLIGHT cycles red, green, blue" );
		Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.Count, "the light opcodes are served" );

		// the light stands at the dummy's node: 17.1 model units up, at the middle of its cell
		runtime.Lights!.Apply( Opcode.ENABLELIGHT, 0 );
		var lights = new List<PointLight>();
		runtime.Lights.AppendTo( lights, runtime.Animator.NodeTransforms, new ObjectPlacement( 4, 6, 0, NVector2.Zero, 0f ).ModelToEngine );
		Assert.AreEqual( 1, lights.Count );
		Assert.AreEqual( (4 * 10 + 5) * ObjectPlacement.ModelScale, lights[0].Position.X, 1e-3f );
		Assert.AreEqual( (6 * 10 + 5) * ObjectPlacement.ModelScale, lights[0].Position.Y, 1e-3f );
		Assert.AreEqual( 17.1f * ObjectPlacement.ModelScale, lights[0].Position.Z, 1e-3f );
		Assert.AreEqual( 70 * ObjectPlacement.ModelScale, lights[0].Radius, 1e-4f );

		// presentation: a snapshot carries the light, a restored candidate shows the same light
		var snapshot = runtime.CaptureSnapshot();
		Assert.AreEqual( 1, snapshot.Lights!.Count );
		snapshot.ValidateSnapshot();
		var restored = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), 1, true, snapshot.AttractionId );
		Assert.AreEqual( 0, restored.RestoreSnapshot( snapshot ).Count );
		Assert.AreEqual( light.On, restored.Lights!.Find( 0 )!.On );
		Assert.AreEqual( light.Colour, restored.Lights.Find( 0 )!.Colour );
	}
}
