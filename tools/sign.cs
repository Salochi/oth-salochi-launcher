#:property PublishAot=false
// The Salochi release key: ECDSA P-256 over SHA-256, signatures as base64 of the 64-byte r||s. This is the same
// scheme Core's in-game installer checks (Install/ReleaseKey.cs), and the launcher checks it too.
//
//   dotnet run tools/sign.cs -- keygen [key.pem]   make a new key (refuses to overwrite one), print the public half
//   dotnet run tools/sign.cs -- pub    [key.pem]   print the public half (X, Y as hex) to paste into the code
//   dotnet run tools/sign.cs -- sign   <file> [key.pem]  write <file>.sig
//
// The key defaults to %USERPROFILE%\.salochi\release-key.pem. Keep it out of every repo, and back it up: anyone who
// has it can ship code to players, and losing it means a new key and a Core update before the next release.
using System.Security.Cryptography;

string DefaultKey() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".salochi", "release-key.pem");

if (args.Length == 0) { Console.Error.WriteLine("usage: keygen [key] | pub [key] | sign <file> [key]"); return 2; }

switch (args[0])
{
    case "keygen":
    {
        string key = args.Length > 1 ? args[1] : DefaultKey();
        if (File.Exists(key)) { Console.Error.WriteLine($"{key} already exists; not overwriting it."); return 1; }
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(key, ec.ExportPkcs8PrivateKeyPem());
        Console.WriteLine($"wrote {key}");
        PrintPublic(ec);
        return 0;
    }
    case "pub":
    {
        using var ec = Load(args.Length > 1 ? args[1] : DefaultKey());
        PrintPublic(ec);
        return 0;
    }
    case "sign":
    {
        if (args.Length < 2) { Console.Error.WriteLine("sign <file> [key]"); return 2; }
        using var ec = Load(args.Length > 2 ? args[2] : DefaultKey());
        var sig = ec.SignData(File.ReadAllBytes(args[1]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        File.WriteAllText(args[1] + ".sig", Convert.ToBase64String(sig));
        Console.WriteLine($"signed {args[1]}");
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown command " + args[0]);
        return 2;
}

static ECDsa Load(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"no release key at {path} (run keygen first)");
    var ec = ECDsa.Create();
    ec.ImportFromPem(File.ReadAllText(path));
    return ec;
}

static void PrintPublic(ECDsa ec)
{
    var q = ec.ExportParameters(false).Q;
    Console.WriteLine("X = " + Convert.ToHexStringLower(q.X!));
    Console.WriteLine("Y = " + Convert.ToHexStringLower(q.Y!));
}
