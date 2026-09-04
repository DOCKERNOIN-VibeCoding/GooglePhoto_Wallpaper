using System.Runtime.InteropServices;

namespace GooglePhotoWallpaper.Interop;

/// <summary>
/// DPAPI, called directly rather than through System.Security.Cryptography.ProtectedData.
///
/// Ciphertext is bound to the current Windows user account, so a token file copied to another
/// machine or another user profile is useless. Going straight to crypt32 keeps the project free of
/// NuGet dependencies, which matters for a build meant to be redistributed as a single file.
/// </summary>
public static class DataProtection
{
    private const uint CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] plaintext, byte[] entropy)
        => Transform(plaintext, entropy, protect: true);

    public static byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        => Transform(ciphertext, entropy, protect: false);

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        DataBlob inBlob = default;
        DataBlob entropyBlob = default;
        DataBlob outBlob = default;

        try
        {
            inBlob = Allocate(input);
            entropyBlob = Allocate(entropy);

            bool ok = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out outBlob);

            if (!ok)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            Release(ref inBlob);
            Release(ref entropyBlob);

            if (outBlob.pbData != IntPtr.Zero)
            {
                // DPAPI allocates its output with LocalAlloc.
                LocalFree(outBlob.pbData);
            }
        }
    }

    private static DataBlob Allocate(byte[] data)
    {
        var blob = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(Math.Max(data.Length, 1)) };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static void Release(ref DataBlob blob)
    {
        if (blob.pbData == IntPtr.Zero)
        {
            return;
        }

        // Wipe before freeing so plaintext does not linger in the heap.
        for (int i = 0; i < blob.cbData; i++)
        {
            Marshal.WriteByte(blob.pbData, i, 0);
        }

        Marshal.FreeHGlobal(blob.pbData);
        blob.pbData = IntPtr.Zero;
    }
}
