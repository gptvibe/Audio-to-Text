using System.Runtime.InteropServices;
using System.Text;

namespace App.Services.Security;

public sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 5120)
        {
            throw new InvalidOperationException("The secret is too large for Windows Credential Manager.");
        }

        var credential = new NativeCredential
        {
            Type = CredentialTypeGeneric,
            TargetName = key,
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob = Marshal.StringToCoTaskMemUni(secret),
            Persist = CredentialPersistLocalMachine
        };

        try
        {
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Credential Manager write failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);
        }

        return Task.CompletedTask;
    }

    public Task<string?> ReadSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CredRead(key, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            var secret = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return Task.FromResult<string?>(secret);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CredDelete(key, CredentialTypeGeneric, 0);
        return Task.CompletedTask;
    }

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
