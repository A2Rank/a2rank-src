// Signs the update list (releases.win.json) so the app can tell it really comes from us (docs/RELEASE.md).
//   dotnet run --project tools/SignFeed -- gen-key [--key <file>] [--public-out <file>]
//   dotnet run --project tools/SignFeed -- sign <file> [--key <file>]      -> writes <file>.sig
// The private key stays outside the repository: %USERPROFILE%\.aion2meter\update-signing.key (PKCS#8 PEM).
// Back it up: if it is lost, installed apps cannot be updated any more (they only trust this key).
using System.Security.Cryptography;

var keyPath = Option("--key") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion2meter", "update-signing.key");
switch (args.FirstOrDefault())
{
    case "gen-key":
        if (File.Exists(keyPath)) return Fail($"{keyPath} already exists - not overwriting (delete it yourself if you really want a new key; installed apps would stop updating).");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            Console.WriteLine($"private key: {keyPath}");
            Console.WriteLine($"public key (SPKI, base64): {publicKey}");
            if (Option("--public-out") is { } outFile) { File.WriteAllText(outFile, publicKey); Console.WriteLine($"public key written to {outFile}"); }
        }
        return 0;

    case "sign":
        var file = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && a != keyPath);
        if (file is null || !File.Exists(file)) return Fail("usage: sign <file> [--key <file>]");
        if (!File.Exists(keyPath)) return Fail($"signing key not found: {keyPath} (create it with gen-key)");
        using (var key = ECDsa.Create())
        {
            key.ImportFromPem(File.ReadAllText(keyPath));
            var signature = key.SignData(File.ReadAllBytes(file), HashAlgorithmName.SHA256);   // IEEE P1363, 64 bytes
            File.WriteAllBytes(file + ".sig", signature);
            Console.WriteLine($"signed {file} -> {file}.sig ({signature.Length} bytes)");
        }
        return 0;

    default:
        return Fail("usage: gen-key | sign <file>");
}

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
