using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A rejected candidate scene must not leave its GPU materials behind: without rollback every
/// failed construction would grow <see cref="Asset.All"/> and leak native pipeline and shader
/// objects forever. These tests use materials whose construction fails (no graphics device in the
/// test host), which is exactly the partial-construction path rollback has to survive.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MaterialRollbackTests
{
	private List<Asset> previousAssets = null!;
	private List<Entity> previousEntities = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousAssets = Asset.All.ToList();
		previousEntities = Entity.All;
		Entity.All = new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Entity.All = previousEntities;
	}

	private sealed class ThrowingDeleteEntity : Entity
	{
		protected override void OnDelete()
		{
			throw new InvalidOperationException( "injected resource cleanup failure" );
		}
	}

	[TestMethod]
	public void RejectedCandidateReleasesItsPartialMaterialAndShaderRegistrations()
	{
		// A live-scene shader registered before the scope must survive the rollback.
		var liveShader = new Shader( "content/shaders/live-scene.shader" );
		Assert.IsTrue( Asset.All.Contains( liveShader ), "the live shader is registered" );
		var baseline = Asset.All.ToList();

		var transaction = new EntityConstructionScope();
		Assert.ThrowsException<NullReferenceException>( () => new Material( "content/shaders/candidate.shader" ),
			"partial material construction must be reachable without a graphics device" );
		var candidateMaterial = Asset.All.OfType<Material>().Except( baseline.OfType<Material>() ).Single();
		var candidateShader = Asset.All.OfType<Shader>().Except( baseline.OfType<Shader>() ).Single();
		Assert.AreSame( candidateMaterial.Shader, candidateShader, "the failed material keeps its already registered shader" );

		transaction.Dispose();

		Assert.IsFalse( Asset.All.Contains( candidateMaterial ), "the rejected material leaves Asset.All" );
		Assert.IsFalse( Asset.All.Contains( candidateShader ), "the rejected material's shader leaves Asset.All" );
		Assert.IsTrue( Asset.All.Contains( liveShader ), "the live scene's shader stays registered" );
	}

	[TestMethod]
	public void ProcessLifetimeMaterialsFirstCreatedByTheCandidateSurviveRollback()
	{
		// A live-scene shader registered before the scope must survive the rollback.
		var liveShader = new Shader( "content/shaders/live-scene.shader" );
		var baseline = Asset.All.ToList();

		var transaction = new EntityConstructionScope();
		// The shared UI/default materials are created by whichever candidate first touches them; the
		// statics record their process lifetime at creation exactly like this (Material.UI.cs). A
		// shared material registers its shader BEFORE itself, so the pair must be kept together.
		Assert.ThrowsException<NullReferenceException>( () => new Material( "content/shaders/shared.shader" ) );
		var sharedMaterial = Asset.All.OfType<Material>().Except( baseline.OfType<Material>() ).Single();
		var sharedShader = sharedMaterial.Shader;
		sharedMaterial.MarkAsProcessLifetime();
		// An ordinary candidate asset in the same rejected scene must still be released.
		Assert.ThrowsException<NullReferenceException>( () => new Material( "content/shaders/ordinary.shader" ) );

		transaction.Dispose();

		Assert.IsTrue( Asset.All.Contains( sharedMaterial ), "a shared material first created by the candidate stays registered" );
		Assert.IsTrue( Asset.All.Contains( sharedShader ), "the shader of a kept shared material is not released out from under it" );
		Assert.IsFalse( Asset.All.OfType<Material>().Any( m => !baseline.Contains( m ) && !ReferenceEquals( m, sharedMaterial ) ),
			"the ordinary candidate material is still released" );
		Assert.IsFalse( Asset.All.OfType<Shader>().Any( s => !baseline.Contains( s ) && !ReferenceEquals( s, sharedShader ) && !ReferenceEquals( s, liveShader ) ),
			"the ordinary candidate shader is still released" );
		Assert.AreEqual( baseline.Count + 2, Asset.All.Count,
			"only the shared pair is kept; deciding ownership must not create or initialize anything else" );
		var createdDuringRollback = Asset.All.OfType<Shader>().Except( baseline.OfType<Shader>() ).ToList();
		Assert.IsFalse( createdDuringRollback.Any( s => s.Path.EndsWith( "ui.shader" ) || s.Path.EndsWith( "3d.shader" ) ),
			"cleanup never reads the shared statics, so their materials are not created behind the rollback" );
	}

	[TestMethod]
	public void RepeatedRejectedCandidatesDoNotGrowAssetRegistrations()
	{
		for ( var attempt = 0; attempt < 3; attempt++ )
		{
			var materialsBefore = Asset.All.OfType<Material>().Count();
			var shadersBefore = Asset.All.OfType<Shader>().Count();
			var transaction = new EntityConstructionScope();
			Assert.ThrowsException<NullReferenceException>( () => new Material( $"content/shaders/candidate-{attempt}.shader" ) );
			transaction.Dispose();
			Assert.AreEqual( materialsBefore, Asset.All.OfType<Material>().Count(),
				$"material registrations must not grow after rejected candidate {attempt}" );
			Assert.AreEqual( shadersBefore, Asset.All.OfType<Shader>().Count(),
				$"shader registrations must not grow after rejected candidate {attempt}" );
		}
	}

	[TestMethod]
	public void AssetReleaseStillHappensWhenCandidateEntityCleanupThrows()
	{
		var materialsBefore = Asset.All.OfType<Material>().Count();
		var shadersBefore = Asset.All.OfType<Shader>().Count();
		var transaction = new EntityConstructionScope();
		new ThrowingDeleteEntity();
		Assert.ThrowsException<NullReferenceException>( () => new Material( "content/shaders/candidate.shader" ) );
		Assert.ThrowsException<AggregateException>( transaction.Dispose,
			"the injected entity cleanup failure is still reported" );
		Assert.AreEqual( materialsBefore, Asset.All.OfType<Material>().Count(),
			"the candidate's material is released despite the entity cleanup failure" );
		Assert.AreEqual( shadersBefore, Asset.All.OfType<Shader>().Count(),
			"the candidate's shader is released despite the entity cleanup failure" );
	}

	[TestMethod]
	public void RollbackDisposesNativeShaderProgramsOfTheRejectedCandidate()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" || !OperatingSystem.IsMacOS() )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 on macOS with native Metal/SPIR-V libraries." );
		var previousDevice = global::Global.Device;
		Log = new();
		using var device = Veldrid.GraphicsDevice.CreateMetal( new Veldrid.GraphicsDeviceOptions() );
		global::Global.Device = device;
		try
		{
			var path = GetShaderPath( "text.shader" );

			var transaction = new EntityConstructionScope();
			var shader = new Shader( path );
			var program = shader.ShaderProgram;
			Assert.IsTrue( program.All( stage => stage is not null && !stage.IsDisposed ),
				"the candidate compiled real native shader programs" );
			transaction.Dispose();
			Assert.IsFalse( Asset.All.Contains( shader ), "the rejected shader leaves Asset.All" );
			Assert.IsTrue( program.All( stage => stage.IsDisposed ),
				"rollback retires the candidate's native shader programs" );

			// A later candidate must receive fresh working programs from the shared compile cache,
			// so one failure never poisons the next construction attempt.
			var successorTransaction = new EntityConstructionScope();
			var successor = new Shader( path );
			var successorProgram = successor.ShaderProgram;
			Assert.IsTrue( successorProgram.All( stage => stage is not null && !stage.IsDisposed ),
				"a later candidate compiles fresh native shader programs" );
			successorTransaction.Dispose();
			Assert.IsFalse( Asset.All.Contains( successor ) );
			Assert.IsTrue( successorProgram.All( stage => stage.IsDisposed ),
				"the second rejected candidate's programs are retired too" );
		}
		finally
		{
			global::Global.Device = previousDevice;
		}
	}

	[TestMethod]
	public void MarkedProcessLifetimeShadersKeepTheirNativeProgramsAcrossRollback()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" || !OperatingSystem.IsMacOS() )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 on macOS with native Metal/SPIR-V libraries." );
		var previousDevice = global::Global.Device;
		Log = new();
		using var device = Veldrid.GraphicsDevice.CreateMetal( new Veldrid.GraphicsDeviceOptions() );
		global::Global.Device = device;
		try
		{
			var shared = new Shader( GetShaderPath( "text.shader" ) );
			// The UI/default material statics mark the shaders they own at creation; the marker must
			// gate real native disposal, not just registry bookkeeping.
			shared.MarkAsProcessLifetime();
			var sharedProgram = shared.ShaderProgram;
			Assert.IsTrue( sharedProgram.All( stage => stage is not null && !stage.IsDisposed ),
				"the shared shader owns real native programs" );

			var transaction = new EntityConstructionScope();
			var candidate = new Shader( GetShaderPath( "text.shader" ) );
			var candidateProgram = candidate.ShaderProgram;
			transaction.Dispose();

			Assert.IsFalse( Asset.All.Contains( candidate ), "the ordinary candidate shader leaves Asset.All" );
			// both shaders of one file share one native program (issue #754): the candidate gives its reference back, the shared shader keeps the program
			Assert.IsFalse( candidate.HoldsNativeProgram, "the ordinary candidate gives its native program back" );
			CollectionAssert.AreEqual( sharedProgram, candidateProgram, "both shaders of one file hold the same native program" );
			Assert.IsTrue( Asset.All.Contains( shared ), "the process-lifetime shared shader stays registered" );
			Assert.IsTrue( sharedProgram.All( stage => stage is not null && !stage.IsDisposed ),
				"rollback keeps the native programs of a shared material's shader alive" );

			// The shared shader outlives the rollback on purpose; retire it so later tests start clean.
			shared.ReleaseOwnedResources();
			Assert.IsTrue( sharedProgram.All( stage => stage.IsDisposed ),
				"the manually retired shared shader's programs are disposed" );
		}
		finally
		{
			global::Global.Device = previousDevice;
		}
	}

	private static string GetShaderPath( string shader )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent!;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return Path.Combine( directory!.FullName, "content", "shaders", shader );
	}
}
