using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenTPW.Server;

/// <summary>Public build facts from the running server assembly; a source revision is not an image digest.</summary>
public sealed record ServerBuildInfo( string? Version, string? SourceRevision, bool RevisionKnown )
{
	public static ServerBuildInfo Current { get; } = FromInformationalVersion(
		typeof( ServerProgram ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion );

	/// <summary>Keep only a version and a full Git SHA; other build metadata is not exposed.</summary>
	public static ServerBuildInfo FromInformationalVersion( string? informationalVersion )
	{
		if ( informationalVersion == null || informationalVersion.Length > 200 )
			return new( null, null, false );
		var match = Regex.Match( informationalVersion,
			@"\A(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?)(?:\+(?<metadata>[0-9A-Za-z.-]+))?\z",
			RegexOptions.CultureInvariant );
		if ( !match.Success )
			return new( null, null, false );
		// The SDK appends SourceRevisionId after '+' or after existing build metadata and '.'.
		var metadata = match.Groups["metadata"].Value;
		var revision = metadata[(metadata.LastIndexOf( '.' ) + 1)..];
		var known = Regex.IsMatch( revision, @"\A[0-9a-fA-F]{40}\z", RegexOptions.CultureInvariant );
		return new( match.Groups["version"].Value, known ? revision.ToLowerInvariant() : null, known );
	}
}
