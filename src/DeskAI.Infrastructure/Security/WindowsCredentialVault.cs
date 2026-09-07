using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;

namespace DeskAI.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialVault : ICredentialVault
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumSecretBytes = 4096;

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        cancellationToken.ThrowIfCancellationRequested();
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (secretBytes.Length > MaximumSecretBytes)
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            throw new ArgumentOutOfRangeException(nameof(secret), "The credential is too large for DeskAI storage.");
        }

        var targetPointer = Marshal.StringToCoTaskMemUni(reference);
        var userPointer = Marshal.StringToCoTaskMemUni("DeskAI");
        var blobPointer = Marshal.AllocCoTaskMem(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blobPointer, secretBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = secretBytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = userPointer,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the DeskAI credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            if (secretBytes.Length > 0)
            {
                Marshal.Copy(secretBytes, 0, blobPointer, secretBytes.Length);
            }
            Marshal.FreeCoTaskMem(blobPointer);
            Marshal.ZeroFreeCoTaskMemUnicode(targetPointer);
            Marshal.ZeroFreeCoTaskMemUnicode(userPointer);
        }

        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(reference, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw new Win32Exception(error, "Windows Credential Manager could not read the DeskAI credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<string?>(string.Empty);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(reference, CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not remove the DeskAI credential.");
        }

        return Task.CompletedTask;
    }

    private static void ValidateReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!reference.StartsWith("DeskAI/", StringComparison.Ordinal) || reference.Length > 128 ||
            reference.Any(char.IsControl))
        {
            throw new ArgumentException("Credential references must be short DeskAI-owned names.", nameof(reference));
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
