using System.IO;
using System.Security.Cryptography;

namespace IMRCRaceControl.Services;

internal static class LiveTimePasswordBundle
{
    internal const string ResourceName = "IMRCRaceControl.LiveTimePassword.bundle";
    private const int NonceSize = 12, TagSize = 16, HeaderSize = 1 + NonceSize + TagSize;
    internal const int MaxPasswordBytes = 2048;

    // The reader must also have this wrapping key. This keeps the passcode out of
    // plain text in the installer and is not a security boundary for a recipient.
    private static ReadOnlySpan<byte> WrappingKey => [
        0x87, 0x6e, 0x36, 0x92, 0x83, 0x5c, 0x28, 0xc0, 0xb6, 0x51, 0xb9, 0x1a, 0x04, 0x17, 0x24, 0x69,
        0x1d, 0x46, 0x23, 0xae, 0x68, 0xe2, 0x1f, 0x73, 0x40, 0x96, 0x94, 0x8d, 0xc5, 0x2a, 0x08, 0x4b
    ];

    internal static byte[] Wrap(ReadOnlySpan<byte> password)
    {
        if (password.Length is < 1 or > MaxPasswordBytes)
            throw new ArgumentException("Provide a nonempty LiveTime passcode within the supported length.", nameof(password));
        var payload = new byte[HeaderSize + password.Length];
        payload[0] = 1;
        var nonce = payload.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(WrappingKey, TagSize);
        aes.Encrypt(nonce, password, payload.AsSpan(HeaderSize), payload.AsSpan(1 + NonceSize, TagSize));
        return payload;
    }

    internal static byte[]? Read()
    {
        using var resource = typeof(LiveTimePasswordBundle).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null || resource.Length is <= HeaderSize or > HeaderSize + MaxPasswordBytes)
            return null;
        var payload = new byte[(int)resource.Length];
        resource.ReadExactly(payload);
        return Unwrap(payload);
    }

    internal static byte[]? Unwrap(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is <= HeaderSize or > HeaderSize + MaxPasswordBytes || payload[0] != 1)
            return null;
        var password = new byte[payload.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(WrappingKey, TagSize);
            aes.Decrypt(payload.Slice(1, NonceSize), payload[HeaderSize..], payload.Slice(1 + NonceSize, TagSize), password);
            return password;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(password);
            return null;
        }
    }
}
