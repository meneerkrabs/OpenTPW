using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW;

internal sealed partial class FrontEndSmokeTest
{
	private string? staffResourceObservation;
	private Action? restoreStaffResources;

	// This fixture makes the current wrapper unavailable without submitting dead native handles.
	// Its original allocation remains alive until the real targeted reload retires it exactly once.
	private void PlanStaffTextureReload()
	{
		StaffResourceLease? lease = null;
		UiRect world = default, portrait = default;
		(byte[] Pixels, int Width, int Height) worldBefore = default, portraitBefore = default;
		Do( "D06 resource baseline", () =>
		{
			Require( staffResourceObservation == null && restoreStaffResources == null, "one resource fixture owns cleanup" );
			Require( staffSmokeMember!.Type == StaffType.Handyman && StaffEconomy.Speed == GameSpeed.Paused
				&& TexturePackSwitch.Current == null && flow.Level!.StaffPlacement!.Kind == StaffToolKind.None
				&& flow.Hud!.Stack.Screens.All( screen => screen.Name != "staff" ), "D06 uses stable Stand without a hire window, carry tool or pack switch" );
			var source = staffSmokeMember.BodySprite!.DrawRecord!; var clone = StaffPortrait.DrawRecord!;
			var resolved = StaffRenderer.ResolveRecord( source );
			Require( resolved.CanDraw && StaffRenderer.ResolveRecord( clone ).CanDraw && clone.Route == StaffSpriteRoute.Screen,
				"D06 begins with real drawable source and Screen clone" );
			world = StaffWorldRect( staffSmokeMember ); portrait = StaffPortraitRect(); RequireIsolatedStaffRect( world );
			lease = new StaffResourceLease( StaffRenderer, resolved.Atlas,
				() => flow.Level.GuestRenderer!.OwnedAssets.Concat( flow.Level.RideHeadRenderer!.OwnedAssets ).ToArray() );
			// Register before any scratch/fault allocation or material/wrapper mutation.
			restoreStaffResources = lease.Restore;
			staffResourceObservation = StaffObservation();
		} );
		PlanStaffPair( "d06-available-world", false, fixedRoi: () => world, observeVisible: capture => worldBefore = capture );
		PlanStaffPair( "d06-available-portrait", true, fixedRoi: () => portrait, observeVisible: capture => portraitBefore = capture );
		Do( "D06 disposed-handle resource fault", () =>
		{
			lease!.MakeUnavailable(); RequireStaffResourceObservation(); RequireStaffTextureUnavailable();
			StaffEvidence( "resource-fault-fixture", "d06-unavailable", new { lease.Atlas, lease.Key, Slots = lease.Aliases,
				Qualification = "Disposed test-native handles in the current wrapper; original real GPU allocation is still alive in custody. No device-loss claim." }, staffResourceObservation );
		} );
		Wait( "D06 unavailable normal frame", 1 );
		PlanStaffPair( "d06-unavailable-world", false, expectContribution: false, fixedRoi: () => world, observeVisible: capture =>
		{
			RequireStaffTextureUnavailable();
			Require( StaffPixelChanges( worldBefore, capture, world ) > 0, "D06 unavailable source removes its original world contribution" );
		} );
		PlanStaffPair( "d06-unavailable-portrait", true, expectContribution: false, fixedRoi: () => portrait, observeVisible: capture =>
		{
			RequireStaffTextureUnavailable();
			Require( StaffPixelChanges( portraitBefore, capture, portrait ) > 0, "D06 unavailable texture removes the original portrait contribution" );
		} );
		Do( "D06 actual targeted current-atlas reload", () =>
		{
			lease!.Reload(); RequireStaffResourceObservation();
		} );
		Wait( "D06 real retirement and recovered draw", 2 );
		Do( "D06 verifies current handle and real retirement", () =>
		{
			lease!.VerifyReload();
			Require( StaffRenderer.ResolveRecord( staffSmokeMember!.BodySprite!.DrawRecord! ).CanDraw
				&& StaffRenderer.ResolveRecord( StaffPortrait.DrawRecord! ).CanDraw
				&& StaffFigure.Frame()?.Texture == lease.Current.NativeTexture, "D06 world and actual employee callback resolve the new live current atlas" );
			StaffEvidence( "resource-reload", "d06-recovered", new { lease.Atlas, lease.Key, OldNativeRetired = true,
				OldAndQuarantineSetsRetired = true, OtherOwnersUnchanged = true, Qualification = "Actual existing LoadAtlas/replacement/retirement path; same current pack." }, staffResourceObservation );
		} );
		PlanStaffPair( "d06-recovered-world", false, fixedRoi: () => world, observeVisible: capture =>
			Require( StaffPixelChanges( worldBefore, capture, world ) == 0, "D06 recovered source matches the original stable world ROI" ) );
		PlanStaffPair( "d06-recovered-portrait", true, fixedRoi: () => portrait, observeVisible: capture =>
			Require( StaffPixelChanges( portraitBefore, capture, portrait ) == 0, "D06 recovered employee matches the original stable portrait ROI" ) );
		Do( "D06 safely releases scratch", RestoreStaffResources );
		Wait( "D06 scratch retirement frames", 2 );
		Do( "D06 verifies scratch retirement", () =>
		{
			lease!.VerifyScratchRetired(); RequireStaffResourceObservation();
			StaffEvidence( "resource-cleanup", "d06-scratch-retired", new { AllEightCurrentBindings = true, ScratchRetired = true }, staffResourceObservation );
			staffResourceObservation = null;
		} );
	}

	private void RequireStaffTextureUnavailable()
	{
		Require( staffSmokeMember!.BodySprite!.ReadSourceState()!.OutputValid != 0 && StaffPortrait.ReadCloneState()!.OutputValid != 0,
			"D06 resource loss does not clear published output words" );
		Require( StaffRenderer.ResolveRecord( staffSmokeMember.BodySprite.DrawRecord! ).Diagnostic == StaffSpriteDiagnostic.TextureUnavailable
			&& StaffRenderer.ResolveRecord( StaffPortrait.DrawRecord! ).Diagnostic == StaffSpriteDiagnostic.TextureUnavailable
			&& StaffFigure.Frame() == null, "D06 strict current-resource gate suppresses world and employee output" );
	}

	private void RequireStaffResourceObservation()
	{
		if ( staffResourceObservation != null )
			Require( staffResourceObservation == StaffObservation(), "D06 original full observation survives every normal outer update, readback and resource operation" );
	}

	private void RestoreStaffResources()
	{
		try { restoreStaffResources?.Invoke(); restoreStaffResources = null; }
		catch
		{
			// A partially rebound material must never reach another draw. Keep any bound scratch alive.
			GameFlow.Quit(); throw;
		}
	}

	internal static void StaffDisposeOnce( IDisposable? resource, ref bool disposed )
	{
		if ( resource == null || disposed ) return;
		disposed = true; resource.Dispose();
	}

	internal static UiRect StaffEvidenceRect( UiRect? fixedRect, Func<UiRect> resolve ) => fixedRect ?? resolve();

	internal static int[] StaffAtlasSlots( int count )
	{
		Require( count is > 0 and <= 8, "a staff material has one to eight loaded atlases" );
		return Enumerable.Range( 0, 8 ).Select( slot => Math.Min( slot, count - 1 ) ).ToArray();
	}

	internal static string StaffUniqueAtlasKey( IReadOnlyList<string> keys, int selected )
	{
		Require( selected >= 0 && selected < keys.Count, "the selected staff atlas is loaded" );
		var key = TexturePack.RelativeFileName( keys[selected] );
		Require( keys.Count( value => TexturePack.RelativeFileName( value ) == key ) == 1,
			"the normalized selected PackKey targets exactly one loaded staff atlas" );
		return key;
	}

	private sealed class StaffResourceLease
	{
		private readonly StaffRenderer renderer;
		private readonly Texture original;
		private readonly Veldrid.Texture originalNative;
		private readonly TextureView originalView;
		private readonly Material material;
		private readonly Asset[] others;
		private readonly Func<Asset[]> otherOwners;
		private readonly object[] otherObjects;
		private readonly Texture[] originalAtlases;
		private ResourceSet[] oldSets = Array.Empty<ResourceSet>(), quarantineSets = Array.Empty<ResourceSet>();
		private Texture[] bindings = Array.Empty<Texture>();
		private Texture? scratch;
		private Veldrid.Texture? faultNative;
		private TextureView? faultView;
		private bool faultNativeDisposed, faultViewDisposed, scratchRetirementQueued, restored;
		public int Atlas { get; }
		public string Key { get; }
		public int[] Aliases { get; }
		public Texture Current => renderer.OwnedAssets.OfType<Texture>().ElementAt( Atlas );

		public StaffResourceLease( StaffRenderer renderer, int atlas, Func<Asset[]> otherOwners )
		{
			this.renderer = renderer; this.otherOwners = otherOwners; Atlas = atlas;
			originalAtlases = renderer.OwnedAssets.OfType<Texture>().ToArray();
			var slots = StaffAtlasSlots( originalAtlases.Length );
			Key = StaffUniqueAtlasKey( renderer.AtlasPackKeys, atlas );
			Aliases = Enumerable.Range( 0, 8 ).Where( slot => slots[slot] == atlas ).ToArray();
			original = originalAtlases[atlas]; originalNative = original.NativeTexture; originalView = original.NativeTextureView;
			material = renderer.OwnedAssets.OfType<Material>().Single();
			others = OtherAssets();
			otherObjects = ResourceObjects( others );
			Require( AssetsLive( others ) && !originalNative.IsDisposed, "D06 begins with live owned resources" );
		}

		public void MakeUnavailable()
		{
			// Fault handles have no command submission. Each successful allocation is recorded before the next can fail.
			try
			{
				faultNative = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( 1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled ) );
				faultView = Device.ResourceFactory.CreateTextureView( faultNative );
			}
			catch { DisposeFault(); throw; }
			DisposeFault();
			Require( faultNative.IsDisposed, "D06 test-native texture is genuinely disposed" );
			scratch = new Texture( new byte[4], 1, 1 );
			Device.WaitForIdle();
			oldSets = material.GetResourceSets().ToArray();
			bindings = StaffAtlasSlots( originalAtlases.Length ).Select( index => index == Atlas ? scratch : originalAtlases[index] ).ToArray();
			material.Set( "Color", bindings ); // Complete array, including every padded selected alias.
			quarantineSets = material.GetResourceSets().ToArray();
			Require( Aliases.All( slot => ReferenceEquals( bindings[slot], scratch ) ), "D06 every selected material alias is live scratch" );
			original.NativeTexture = faultNative; original.NativeTextureView = faultView;
			Require( !originalNative.IsDisposed, "D06 original allocation remains alive in custody during the fault" );
		}

		public void Reload()
		{
			Device.WaitForIdle();
			Require( ReferenceEquals( Current, original ), "D06 original wrapper is still the selected current resource" );
			original.NativeTexture = originalNative; original.NativeTextureView = originalView;
			// No draw or observer between restoring live custody and the genuine targeted reload.
			renderer.ReloadAtlas( Atlas );
			BindCurrent();
		}

		private void BindCurrent()
		{
			var current = renderer.OwnedAssets.OfType<Texture>().ToArray();
			Require( current.Length == originalAtlases.Length && current.All( texture => !texture.NativeTexture.IsDisposed ),
				"D06 cleanup can bind every current owned live atlas" );
			bindings = StaffAtlasSlots( current.Length ).Select( index => current[index] ).ToArray();
			material.Set( "Color", bindings );
			material.GetResourceSets();
			Require( bindings.Length == 8 && bindings.Select( (texture, slot) => ReferenceEquals( texture, current[Math.Min( slot, current.Length - 1 )] ) ).All( equal => equal )
				&& bindings.All( texture => !ReferenceEquals( texture, scratch ) ), "D06 all eight material bindings follow current ownership without scratch" );
		}

		public void VerifyReload()
		{
			Device.WaitForIdle();
			Require( !ReferenceEquals( Current, original ) && !ReferenceEquals( Current.NativeTexture, originalNative )
				&& !ReferenceEquals( Current.NativeTexture, faultNative ) && !Current.NativeTexture.IsDisposed && Asset.All.Contains( Current ),
				"D06 targeted reload owns a different live current wrapper and native allocation" );
			Require( !Asset.All.Contains( original ) && originalNative.IsDisposed && AtlasViewReleased( original ), "D06 actual original GPU allocation is retired" );
			Require( oldSets.Length > 0 && quarantineSets.Length > 0 && oldSets.Concat( quarantineSets ).All( set => set.IsDisposed ),
				"D06 original and quarantine resource sets have actually retired" );
			Require( originalAtlases.Where( (texture, index) => index != Atlas ).SequenceEqual( renderer.OwnedAssets.OfType<Texture>().Where( (texture, index) => index != Atlas ) )
				&& others.SequenceEqual( OtherAssets() ) && otherObjects.SequenceEqual( ResourceObjects( others ) ) && AssetsLive( others ), "D06 other staff, guest and head identities stay live and unchanged" );
		}

		public void Restore()
		{
			if ( restored ) return;
			Device.WaitForIdle();
			// Reload replaces the owner list BEFORE material binding and retirement. Inspect that list even after a thrown reload.
			if ( ReferenceEquals( Current, original ) )
			{
				Require( !originalNative.IsDisposed, "D06 can restore original live custody before replacement" );
				original.NativeTexture = originalNative; original.NativeTextureView = originalView;
			}
			BindCurrent(); // If this fails, do not release scratch, resurrect an old wrapper, or permit another draw.
			Device.WaitForIdle();
			if ( !ReferenceEquals( Current, original ) && Asset.All.Contains( original ) )
			{
				// The reload failed after list replacement but before removing/queueing the old owner. Retire it once here.
				Require( !originalNative.IsDisposed && ReferenceEquals( original.NativeTexture, originalNative ), "D06 partial reload retains original retirement ownership" );
				original.ReleaseOwned();
			}
			DisposeFault();
			if ( scratch != null && !scratchRetirementQueued )
			{
				scratchRetirementQueued = true;
				scratch.ReleaseOwned();
			}
			restored = true;
		}

		private void DisposeFault()
		{
			// No using: Veldrid Vulkan disposal decrements ownership and is not assumed idempotent.
			StaffDisposeOnce( faultView, ref faultViewDisposed );
			StaffDisposeOnce( faultNative, ref faultNativeDisposed );
		}

		public void VerifyScratchRetired()
		{
			Device.WaitForIdle();
			Require( restored && scratch != null && scratchRetirementQueued && !Asset.All.Contains( scratch )
				&& scratch.NativeTexture.IsDisposed && AtlasViewReleased( scratch ) && bindings.All( texture => !ReferenceEquals( texture, scratch ) ),
				"D06 scratch is unbound from every slot and its GPU allocation is retired" );
		}

		private Asset[] OtherAssets() => renderer.OwnedAssets.Where( asset => !ReferenceEquals( asset, Current ) ).Concat( otherOwners() ).ToArray();

		private static bool AssetsLive( IEnumerable<Asset> assets ) => assets.All( asset => Asset.All.Contains( asset ) && asset switch
		{
			Texture texture => !texture.NativeTexture.IsDisposed && !texture.NativeTextureView.IsDisposed,
			Material value => !value.Pipeline.IsDisposed,
			Shader shader => shader.ShaderProgram.All( program => !program.IsDisposed ),
			_ => false
		} );

		private static object[] ResourceObjects( IEnumerable<Asset> assets ) => assets.SelectMany( asset => asset switch
		{
			Texture texture => new object[] { texture, texture.NativeTexture, texture.NativeTextureView },
			Material value => new object[] { value, value.Shader, value.Pipeline },
			Shader shader => new object[] { shader }.Concat( shader.ShaderProgram ),
			_ => new object[] { asset }
		} ).ToArray();
	}
}
