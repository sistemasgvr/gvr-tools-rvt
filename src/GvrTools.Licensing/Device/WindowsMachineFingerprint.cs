using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace GvrTools.Licensing.Device
{
    /// <summary>
    /// Huella estable del PC: hash SHA-256 de MachineGuid + volumen sistema + SID de usuario.
    /// Solo se envía el hex del hash, nunca el dato crudo.
    /// </summary>
    public sealed class WindowsMachineFingerprint : IMachineFingerprint
    {
        private string _cached;

        public string GetFingerprint()
        {
            if (_cached != null) return _cached;

            var sb = new StringBuilder(256);
            sb.Append(ReadMachineGuid());
            sb.Append('|');
            sb.Append(ReadSystemVolumeSerial());
            sb.Append('|');
            sb.Append(WindowsIdentity.GetCurrent()?.User?.Value ?? "unknown-sid");

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    hex.Append(b.ToString("x2"));
                _cached = hex.ToString();
            }

            return _cached;
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    return key?.GetValue("MachineGuid") as string ?? "no-machine-guid";
                }
            }
            catch
            {
                return "no-machine-guid";
            }
        }

        private static string ReadSystemVolumeSerial()
        {
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (string.IsNullOrEmpty(root) || root.Length < 3)
                    return "no-volume";

                var drive = root.Substring(0, 3);
                if (GetVolumeInformation(drive, null, 0, out uint serial, out _, out _, null, 0))
                    return serial.ToString("X8");
            }
            catch
            {
                // fall through
            }

            return "no-volume";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int fileSystemNameSize);
    }
}
