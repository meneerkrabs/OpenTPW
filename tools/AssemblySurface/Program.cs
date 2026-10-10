using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// Lists every type and member an assembly references from assemblies whose name starts with a
// prefix, read from the compiled metadata rather than the source (docs/WEB.md):
//   dotnet run --project tools/AssemblySurface -- path/to/OpenTPW.dll Veldrid
if ( args.Length != 2 )
{
    Console.Error.WriteLine( "usage: AssemblySurface <assembly.dll> <referenced assembly prefix>" );
    return 1;
}
var prefix = args[1];
using var pe = new PEReader( File.OpenRead( args[0] ) );
var md = pe.GetMetadataReader();
string Scope( TypeReferenceHandle h )
{
    var t = md.GetTypeReference( h );
    return t.ResolutionScope.Kind switch
    {
        HandleKind.AssemblyReference => md.GetString( md.GetAssemblyReference( (AssemblyReferenceHandle)t.ResolutionScope ).Name ),
        HandleKind.TypeReference => Scope( (TypeReferenceHandle)t.ResolutionScope ),
        _ => ""
    };
}
string Name( TypeReferenceHandle h )
{
    var t = md.GetTypeReference( h );
    var ns = md.GetString( t.Namespace );
    var n = md.GetString( t.Name );
    if ( t.ResolutionScope.Kind == HandleKind.TypeReference ) return Name( (TypeReferenceHandle)t.ResolutionScope ) + "+" + n;
    return ns.Length > 0 ? ns + "." + n : n;
}
var lines = new SortedSet<string>();
foreach ( var h in md.TypeReferences )
    if ( Scope( h ).StartsWith( prefix ) ) lines.Add( $"{Scope( h )}\t{Name( h )}" );
foreach ( var h in md.MemberReferences )
{
    var m = md.GetMemberReference( h );
    TypeReferenceHandle? parent = m.Parent.Kind switch
    {
        HandleKind.TypeReference => (TypeReferenceHandle)m.Parent,
        HandleKind.TypeSpecification => null,
        _ => null
    };
    if ( parent is { } p && Scope( p ).StartsWith( prefix ) )
        lines.Add( $"{Scope( p )}\t{Name( p )}::{md.GetString( m.Name )}" );
}
foreach ( var l in lines ) Console.WriteLine( l );
return 0;
