using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace IMRCRaceControl.Services;

public sealed class LiveTimeBackupPasswordRequiredException : IOException
{
    public LiveTimeBackupPasswordRequiredException()
        : base("This backup may be encrypted. Enter its LiveTime backup password or unlock key to preview it.") { }
}

public static partial class LiveTimeBackupReader
{
    public static LiveTimeImportSource ReadEncryptedImport(string path, byte[] password) =>
        ReadEncrypted(path, password, ReadImportSource);

    public static LiveTimeRoster ReadEncryptedRoster(string path, byte[] password) =>
        ReadEncrypted(path, password, db => ReadRoster(db, includeHistoryCounts: false));

    private static T ReadEncrypted<T>(string path, byte[] password, Func<SqliteConnection, T> read) where T : class
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length == 0)
            throw new InvalidDataException("Enter the backup password or unlock key.");

        var fullPath = Path.GetFullPath(path);
        var length = new FileInfo(fullPath).Length;
        if (!CouldBeEncryptedBackup(length))
            throw new InvalidDataException("This file is not a supported encrypted LiveTime backup.");
        if (File.Exists(fullPath + "-wal") || File.Exists(fullPath + "-journal"))
            throw new InvalidDataException("Choose a standalone LiveTime backup without a pending journal.");

        // Use a separate, read-only connection for each attempt. No decrypted
        // file is created and the main Race Control database provider is unchanged.
        var uri = new Uri(fullPath).AbsoluteUri + "?mode=ro&immutable=1";
        var unlocked = (CouldBeSqlCipher(length)
                ? TryReadEncrypted(uri, password, "sqlcipher", "legacy", 4, 4096, read)
                : null)
            ?? TryReadEncrypted(uri, password, "rc4", "legacy_page_size", 0, 512, read);
        return unlocked ?? throw new InvalidDataException(
            "Could not unlock this backup. Check that the password or unlock key matches this file and that the backup is intact.");
    }

    private static T? TryReadEncrypted<T>(string uri, byte[] password, string cipherName, string option,
        int value, int minimumBytes, Func<SqliteConnection, T> read) where T : class
    {
        var result = CipherOpen(uri, out var connection, 0x01 | 0x40 | 0x40000, IntPtr.Zero);
        try
        {
            if (result != 0)
                throw new InvalidDataException("Could not open the LiveTime backup. Check that the file is readable.");
            var cipher = CipherIndex(cipherName);
            if (cipher <= 0 || CipherConfig(connection, "cipher", cipher) != cipher ||
                CipherConfigMode(connection, cipherName, option, value) != value)
                throw new InvalidOperationException($"The {cipherName} reader is not available.");
            if (CipherKey(connection, password, password.Length) != 0 ||
                CipherExec(connection, "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;",
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0 || !CheckEncryptedIntegrity(connection))
                return null;

            var memory = CipherSerialize(connection, "main", out var size, 0);
            try
            {
                if (memory == IntPtr.Zero || size < minimumBytes || size > MaxEncodedBytes)
                    throw new InvalidDataException("The decrypted backup exceeds the supported size or could not fit in memory.");
                var bytes = new byte[(int)size];
                try
                {
                    Marshal.Copy(memory, bytes, 0, bytes.Length);
                    if (!bytes.AsSpan().StartsWith(Header))
                        throw new InvalidDataException("The backup did not contain a readable LiveTime database.");
                    using var db = OpenInMemory(bytes);
                    return read(db);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                }
            }
            finally { if (memory != IntPtr.Zero) CipherFree(memory); }
        }
        finally { if (connection != IntPtr.Zero) CipherClose(connection); }
    }

    private static bool CouldBeEncryptedBackup(long size) =>
        size >= 512 && size <= MaxEncodedBytes && size % 512 == 0;

    private static bool CouldBeSqlCipher(long size) => size >= 4096 && size <= MaxEncodedBytes && size % 4096 == 0;

    private static bool CheckEncryptedIntegrity(IntPtr connection)
    {
        // Verify the source before serialization. SQLCipher also authenticates its pages.
        var prepared = CipherPrepare(connection, "PRAGMA integrity_check(1);", -1, out var statement, IntPtr.Zero);
        try
        {
            return prepared == 0 && statement != IntPtr.Zero && CipherStep(statement) == 100 &&
                Marshal.PtrToStringUTF8(CipherText(statement, 0)) == "ok" &&
                CipherStep(statement) == 101;
        }
        finally { if (statement != IntPtr.Zero) CipherFinalize(statement); }
    }

    private const string CipherLibrary = "sqlite3mc";
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherClose(IntPtr db);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3mc_cipher_index", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherIndex([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3mc_config", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherConfig(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3mc_config_cipher", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherConfigMode(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string cipher,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_key", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherKey(IntPtr db, byte[] key, int length);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherExec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherPrepare(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherStep(IntPtr statement);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CipherText(IntPtr statement, int column);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CipherFinalize(IntPtr statement);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_serialize", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CipherSerialize(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema, out long size, uint flags);
    [DllImport(CipherLibrary, EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CipherFree(IntPtr buffer);
}
