using Mono.Cecil;

// Writes a copy of an assembly in which no two types share a full name.
//
// YoutubeExplode 6.6 merges its helper libraries into its own DLL, and the merge leaves
// duplicates (two PowerKit copies, two <PrivateImplementationDetails>/__StaticArrayInitTypeSize=82,
// and so on). The .NET iOS trimmer records types by assembly and full name and crashes on the
// first repeat (MT2231), so the iOS build trims this copy instead. Only the repeats are renamed,
// by adding _2, _3, ... to the name; all of them are internal or compiler-generated.
//
// usage: DedupeTypeNames <input.dll> <output.dll>

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: DedupeTypeNames <input.dll> <output.dll>");
    return 2;
}

int renamed = 0;
using (var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { ReadSymbols = false }))
{
    var seen = new HashSet<string>(StringComparer.Ordinal);
    Rename(module.Types);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    module.Write(args[1]);

    // Parents first: once a parent is renamed, its nested types' full names are unique too.
    void Rename(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types.ToList())
        {
            if (!seen.Add(type.FullName))
            {
                // Keep a generic arity suffix (`1) at the end of the name.
                int tick = type.Name.IndexOf('`');
                string stem = tick < 0 ? type.Name : type.Name[..tick];
                string arity = tick < 0 ? "" : type.Name[tick..];
                for (int i = 2; ; i++)
                {
                    type.Name = $"{stem}_{i}{arity}";
                    if (seen.Add(type.FullName)) break;
                }
                renamed++;
            }
            Rename(type.NestedTypes);
        }
    }
}

Console.WriteLine($"DedupeTypeNames: {renamed} duplicate type name(s) renamed in {Path.GetFileName(args[0])}");
return 0;
