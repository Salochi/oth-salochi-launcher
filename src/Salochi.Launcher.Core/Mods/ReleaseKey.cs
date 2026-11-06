using System.Security.Cryptography;

namespace Salochi.Launcher.Core.Mods;

/// <summary>
/// The Salochi release key's public half (ECDSA P-256, SHA-256, signatures as base64 of r||s): the same key and scheme
/// Salochi Core's in-game installer checks. Only the private key, kept outside the repos, can sign a manifest this
/// accepts; tools/sign.cs makes and uses it.
/// </summary>
public static class ReleaseKey
{
    public const string X = "dbe096f23d5a5bd0fd325e0db6dc4e4a6e25b0d8b66b906a00084d90c4e6cb04";
    public const string Y = "22100439c1b7da663b93ea0e5c29ef25a4d5d1d5fb2feabce2fcae93a5fda109";

    public static bool Verify(byte[] data, string? sigBase64) => Verify(data, sigBase64, X, Y);

    public static bool Verify(byte[] data, string? sigBase64, string xHex, string yHex)
    {
        if (string.IsNullOrWhiteSpace(sigBase64)) return false;
        try
        {
            var sig = Convert.FromBase64String(sigBase64.Trim());
            if (sig.Length != 64) return false;
            using var ec = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = { X = Convert.FromHexString(xHex), Y = Convert.FromHexString(yHex) },
            });
            return ec.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }
}
