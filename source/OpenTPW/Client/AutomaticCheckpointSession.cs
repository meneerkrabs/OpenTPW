using System.Security.Cryptography;
using OpenTPW.FrontEnd;

namespace OpenTPW;

/// <summary>
/// One caller-owned automatic checkpoint attempt. Progress flush precedes fresh capture;
/// pre-write failures leave retirement to the caller. No gameplay/menu route is installed here.
/// </summary>
internal sealed class AutomaticCheckpointSession
{
	private readonly BaseFileSystem files;
	private readonly string theme;
	private readonly Func<BaseFileSystem> currentFiles;
	private readonly Func<bool> ownsWorld;
	private string? profileDigest;
	private bool cancelled;
	private bool attempting;
	internal bool CheckpointCommitted { get; private set; }
	internal bool Completed { get; private set; }
	internal string? BoundProfileDigest => profileDigest;

	internal AutomaticCheckpointSession( BaseFileSystem files, string theme, Func<BaseFileSystem> currentFiles, Func<bool> ownsWorld )
	{
		this.files = files ?? throw new ArgumentNullException( nameof( files ) );
		this.theme = AutomaticThemeContinuation.Theme( theme );
		this.currentFiles = currentFiles ?? throw new ArgumentNullException( nameof( currentFiles ) );
		this.ownsWorld = ownsWorld ?? throw new ArgumentNullException( nameof( ownsWorld ) );
		if ( !ownsWorld() || !ReferenceEquals( files, currentFiles() ) ) { cancelled = true; return; }
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves )
			throw new NotSupportedException( "Automatic world checkpoints are available only for Theme Park World." );
		profileDigest = ProfileDigest();
	}

	internal void Cancel() => cancelled = true;
	private bool Current => !cancelled && !CheckpointCommitted && ownsWorld() && ReferenceEquals( files, currentFiles() );

	/// <summary>
	/// Retry calls flush and capture again, never reuses a snapshot. A retirement exception propagates
	/// with CheckpointCommitted=true; it must not be presented as a pre-write rollback.
	/// </summary>
	internal bool TryCheckpoint( Func<string?> flushProgress, Func<ParkResumeSnapshot> capture, Action retire )
	{
		ArgumentNullException.ThrowIfNull( flushProgress ); ArgumentNullException.ThrowIfNull( capture ); ArgumentNullException.ThrowIfNull( retire );
		if ( attempting || !Current ) return false;
		attempting = true;
		try
		{
			if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves )
				throw new NotSupportedException( "Automatic world checkpoints are available only for Theme Park World." );
			if ( !string.Equals( ProfileDigest(), profileDigest, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "The application save profile changed before the checkpoint attempt." );
			var expectedProfile = flushProgress(); // Receipt from owned progress state, never a blind read of the replacement file.
			if ( !Current ) return false;
			if ( !string.Equals( ProfileDigest(), expectedProfile, StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidDataException( "The application save profile does not match the owned progress flush receipt." );
			profileDigest = expectedProfile; // Retain legitimately committed progress even if the later checkpoint fails.
			var snapshot = capture();
			if ( !Current ) return false;
			AutomaticThemeContinuation.RequireSupported( snapshot, theme );
			if ( !string.Equals( ProfileDigest(), profileDigest, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "The application save profile changed during checkpoint capture." );
			if ( !Current ) return false;
			ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( files, theme ), snapshot );
			CheckpointCommitted = true;
			retire();
			Completed = true;
			return true;
		}
		finally { attempting = false; }
	}

	// Opaque bytes only: malformed/missing history is not parsed, reset or adopted as a ledger.
	private string? ProfileDigest()
	{
		try
		{
			using var stream = new FileStream( files.GetAbsolutePath( PlayerTicketFile.FileName ), FileMode.Open, FileAccess.Read, FileShare.Read );
			if ( stream.Length > PlayerTicketFile.MaximumFileSize ) throw new InvalidDataException( "Application profile binding exceeds the existing size limit." );
			var bytes = new byte[stream.Length]; stream.ReadExactly( bytes );
			return Convert.ToHexString( SHA256.HashData( bytes ) );
		}
		catch ( FileNotFoundException ) { return null; }
		catch ( DirectoryNotFoundException ) { return null; }
	}
}
