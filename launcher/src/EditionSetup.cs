using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Ra2CommandCenter
{
    // Builds the separate Enhanced Red Alert 2 copy (RA2CommandCenter\SmartRA2) from the player's own
    // Steam installation. Nothing in the original game folder is written. The smarter AI file is made
    // from the game's own ai.ini plus the additions shipped in package\EnhancedRA2\ai-additions.json,
    // and both the input and the result are checked against fixed SHA-256 values.
    internal static class EditionSetup
    {
        internal const string PackageFolder = "package\\EnhancedRA2";
        internal const string AdditionsFile = "ai-additions.json";
        internal const string FeatureVersion = "0.6.0";
        // Files copied from the package into the new edition (the AI file is generated separately).
        internal static readonly string[] PackageFiles =
        {
            "RA2NativeUI.dll", "dsound.dll", "RA2NativeUI.ini",
            "RA2NativeUI-LICENSE.txt", "Phobos-LICENSE.md", "Enhanced-RA2-README.txt"
        };
        // Archives the game only reads; these are hard-linked when the edition is on the same drive.
        private static readonly string[] LinkExtensions = { ".mix", ".mmx", ".yro", ".wav" };
        // Root files never copied into the edition: saves, shortcuts, logs, Steam cloud state, and
        // anything that would already change the game (a loose ai.ini or another DirectSound proxy).
        private static readonly string[] SkipExtensions = { ".sav", ".lnk", ".log", ".tmp" };
        private static readonly string[] SkipNames = { "steam_autocloud.vdf", "ai.ini", "ai.ini.off", "dsound.dll", "RA2NativeUI.dll", "RA2NativeUI.ini" };

        internal static string PackagePath(string commandCenterRoot) { return Path.Combine(commandCenterRoot, PackageFolder); }

        internal static void CheckGame(string gameRoot)
        {
            string exe = Path.Combine(gameRoot, "game.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("game.exe was not found. Keep the RA2CommandCenter folder inside your Red Alert 2 game folder.");
            if (!String.Equals(LauncherApp.FileSha256(exe), LauncherApp.NativeHostSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Enhanced Red Alert 2 needs the Steam release of Red Alert 2. This game.exe is a different version, so the in-game extension cannot be installed safely. The standard games still work.");
            if (!File.Exists(Path.Combine(gameRoot, "ra2.mix"))) throw new InvalidOperationException("ra2.mix was not found in the game folder.");
        }

        // Creates <target> (normally RA2CommandCenter\SmartRA2). Work happens in a sibling
        // ".installing" folder that is renamed into place only after every step succeeded.
        internal static void CreateSmartRa2(string gameRoot, string packageDir, string target, Action<string> progress)
        {
            if (progress == null) progress = delegate { };
            if (Directory.Exists(target) || File.Exists(target)) throw new InvalidOperationException("Enhanced Red Alert 2 is already set up in " + target + ".");
            CheckGame(gameRoot);
            foreach (string name in PackageFiles.Concat(new[] { AdditionsFile }))
                if (!File.Exists(Path.Combine(packageDir, name)))
                    throw new InvalidOperationException("The Enhanced Red Alert 2 package is incomplete: " + name + " is missing from " + packageDir + ". Download Command Center again.");

            progress("Building the smarter AI from your game files…");
            byte[] ai = BuildSmartAi(gameRoot, Path.Combine(packageDir, AdditionsFile));

            string work = target + ".installing";
            string marker = Path.Combine(work, "SETUP-IN-PROGRESS.txt");
            if (Directory.Exists(work))
            {
                // Only remove a folder this setup created itself.
                if (!File.Exists(marker)) throw new InvalidOperationException("A folder named " + work + " already exists. Move or delete it, then try again.");
                Directory.Delete(work, true);
            }
            Directory.CreateDirectory(work);
            File.WriteAllText(marker, "Command Center is creating Enhanced Red Alert 2 here. If setup was interrupted, Command Center removes this folder on the next attempt.");
            try
            {
                bool linked = false, copied = false;
                var files = new List<string>();
                foreach (string file in Directory.GetFiles(gameRoot))
                {
                    string name = Path.GetFileName(file);
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    if (SkipExtensions.Contains(ext) || SkipNames.Any(s => String.Equals(s, name, StringComparison.OrdinalIgnoreCase))) continue;
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    files.Add(file);
                }
                string taunts = Path.Combine(gameRoot, "Taunts");
                if (Directory.Exists(taunts)) files.AddRange(Directory.GetFiles(taunts));
                int done = 0;
                foreach (string file in files)
                {
                    string relative = file.Substring(gameRoot.TrimEnd('\\').Length + 1);
                    string destination = Path.Combine(work, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    if (LinkExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) && CreateHardLink(destination, file, IntPtr.Zero)) linked = true;
                    else { File.Copy(file, destination); copied = true; }
                    if (++done % 25 == 0) progress("Copying game files… " + done + " of " + files.Count);
                }
                Directory.CreateDirectory(Path.Combine(work, "SavesDir"));
                if (!String.Equals(LauncherApp.FileSha256(Path.Combine(work, "game.exe")), LauncherApp.NativeHostSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The copied game.exe does not match the original. Setup was cancelled.");

                progress("Installing the in-game extension…");
                var entries = new List<string>();
                foreach (string name in PackageFiles)
                {
                    string destination = Path.Combine(work, name);
                    File.Copy(Path.Combine(packageDir, name), destination);
                    entries.Add(Entry(destination));
                }
                string aiPath = Path.Combine(work, LauncherApp.SmartAiOn);
                File.WriteAllBytes(aiPath, ai);
                entries.Insert(0, Entry(aiPath));
                string aiSha = Sha256(ai);
                string ready = "{\r\n  \"status\": \"ready\",\r\n  \"engine\": \"original-ra2\",\r\n  \"version\": \"0.1.0\",\r\n  \"edition\": \"smart-ai\",\r\n  \"aiVersion\": \"0.1.0\",\r\n" +
                    "  \"experimental\": true,\r\n  \"hostSha256\": \"" + LauncherApp.NativeHostSha256 + "\",\r\n  \"aiSha256\": \"" + aiSha + "\",\r\n" +
                    "  \"featureVersion\": \"" + FeatureVersion + "\",\r\n  \"createdBy\": \"Command Center setup\",\r\n" +
                    "  \"gameFiles\": \"" + (linked && copied ? "hard links and copies" : linked ? "hard links" : "copies") + "\",\r\n" +
                    "  \"files\": [\r\n    " + String.Join(",\r\n    ", entries) + "\r\n  ]\r\n}\r\n";
                File.WriteAllText(Path.Combine(work, "READY.json"), ready, new UTF8Encoding(false));
                File.Delete(marker);
                LauncherApp.VerifySmartAiPackage(work);
                Directory.Move(work, target);
                progress("Enhanced Red Alert 2 is ready.");
            }
            catch
            {
                try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        private static string Entry(string path)
        {
            return "{ \"path\": \"" + Path.GetFileName(path) + "\", \"sha256\": \"" + LauncherApp.FileSha256(path).ToLowerInvariant() + "\", \"length\": " + new FileInfo(path).Length + " }";
        }

        internal static string Sha256(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        // ai.ini = the game's own ai.ini with our text inserted at four recorded offsets. Every stock
        // byte is kept; removing the insertions gives back the original file exactly.
        internal static byte[] BuildSmartAi(string gameRoot, string additionsPath)
        {
            AiAdditions additions;
            using (var stream = File.OpenRead(additionsPath))
                additions = (AiAdditions)new DataContractJsonSerializer(typeof(AiAdditions)).ReadObject(stream);
            byte[] stock = MixArchive.Extract(Path.Combine(gameRoot, "ra2.mix"), "ai.ini");
            if (stock == null) throw new InvalidOperationException("The game's ai.ini was not found inside ra2.mix.");
            return ApplyAdditions(stock, additions);
        }

        internal static byte[] ApplyAdditions(byte[] stock, AiAdditions additions)
        {
            if (!String.Equals(Sha256(stock), additions.BaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This game's built-in AI file is a different version from the Steam release, so the smarter AI cannot be added safely.");
            Encoding cp1252 = Encoding.GetEncoding(1252);
            var output = new MemoryStream();
            int cursor = 0;
            foreach (AiInsertion insertion in additions.Insertions.OrderBy(i => i.Offset))
            {
                if (insertion.Offset < cursor || insertion.Offset > stock.Length) throw new InvalidDataException("The AI additions file is damaged.");
                output.Write(stock, cursor, insertion.Offset - cursor);
                byte[] text = cp1252.GetBytes(insertion.Text);
                output.Write(text, 0, text.Length);
                cursor = insertion.Offset;
            }
            output.Write(stock, cursor, stock.Length - cursor);
            byte[] result = output.ToArray();
            if (!String.Equals(Sha256(result), additions.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The smarter AI file did not come out exactly as expected, so it was not installed.");
            return result;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }

    [DataContract]
    internal sealed class AiAdditions
    {
        [DataMember(Name = "baseSha256")] public string BaseSha256 { get; set; }
        [DataMember(Name = "outputSha256")] public string OutputSha256 { get; set; }
        [DataMember(Name = "insertions")] public AiInsertion[] Insertions { get; set; }
    }

    [DataContract]
    internal sealed class AiInsertion
    {
        [DataMember(Name = "offset")] public int Offset { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
    }

    // Read-only reader for Westwood MIX archives (the XCC format used by Red Alert 2), including the
    // Blowfish-encrypted index of ra2.mix and archives nested inside it such as local.mix.
    internal static class MixArchive
    {
        private static readonly string[] Nested = { "local.mix", "cache.mix", "conquer.mix", "generic.mix" };

        internal static byte[] Extract(string archivePath, string name)
        {
            using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Find(file, 0, file.Length, name, 0);
        }

        private static byte[] Find(Stream stream, long origin, long length, string name, int depth)
        {
            Dictionary<uint, long[]> entries = Index(stream, origin, length);
            long[] hit;
            if (entries.TryGetValue(Id(name), out hit)) return Read(stream, origin + hit[0], (int)hit[1]);
            if (depth >= 3) return null;
            foreach (string nested in Nested)
                if (entries.TryGetValue(Id(nested), out hit))
                {
                    byte[] found = Find(stream, origin + hit[0], hit[1], name, depth + 1);
                    if (found != null) return found;
                }
            return null;
        }

        private static byte[] Read(Stream stream, long offset, int count)
        {
            if (count < 0) throw new InvalidDataException("Damaged MIX archive.");
            byte[] buffer = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) throw new EndOfStreamException("The MIX archive ended early.");
                read += n;
            }
            return buffer;
        }

        // Returns id -> { offset from the archive start, length }.
        internal static Dictionary<uint, long[]> Index(Stream stream, long origin, long length)
        {
            byte[] head = Read(stream, origin, (int)Math.Min(length, 4));
            byte[] header;
            long bodyStart;
            if (head.Length == 4 && head[0] == 0 && head[1] == 0)
            {
                uint flags = BitConverter.ToUInt32(head, 0);
                if ((flags & 0x20000) != 0)
                {
                    byte[] keySource = Read(stream, origin + 4, 80);
                    var blowfish = new Blowfish(RsaKey(keySource));
                    int count = BitConverter.ToUInt16(blowfish.DecryptEcb(Read(stream, origin + 84, 8)), 0);
                    int size = ((6 + 12 * count + 7) / 8) * 8;
                    header = blowfish.DecryptEcb(Read(stream, origin + 84, size));
                    bodyStart = 84 + size;
                }
                else
                {
                    int count = BitConverter.ToUInt16(Read(stream, origin + 4, 2), 0);
                    header = Read(stream, origin + 4, 6 + 12 * count);
                    bodyStart = 4 + 6 + 12 * count;
                }
            }
            else
            {
                int count = BitConverter.ToUInt16(Read(stream, origin, 2), 0);
                header = Read(stream, origin, 6 + 12 * count);
                bodyStart = 6 + 12 * count;
            }
            int entries = BitConverter.ToUInt16(header, 0);
            var result = new Dictionary<uint, long[]>();
            for (int i = 0; i < entries; i++)
            {
                int at = 6 + 12 * i;
                uint id = BitConverter.ToUInt32(header, at);
                long offset = bodyStart + BitConverter.ToUInt32(header, at + 4);
                long size = BitConverter.ToUInt32(header, at + 8);
                if (offset + size > length) throw new InvalidDataException("Damaged MIX archive index.");
                result[id] = new[] { offset, size };
            }
            return result;
        }

        // Westwood's public key; the 80-byte key block after the flags decrypts to the Blowfish key.
        private const string PublicKey = "AihRvNoIbTn85FZRYNZRcT+i6KpU+maCsEqr3Q5q+LDB5tH7Tz2qQ38V";

        private static byte[] RsaKey(byte[] block)
        {
            byte[] key = Convert.FromBase64String(PublicKey);
            BigInteger modulus = FromBigEndian(key, 2, key.Length - 2);
            int bits = 0;
            for (BigInteger m = modulus; m > 0; m >>= 1) bits++;
            int outLength = (bits - 1) / 8, inLength = outLength + 1;
            var result = new List<byte>();
            for (int i = 0; i < 80; i += inLength)
            {
                byte[] chunk = new byte[inLength + 1];
                Array.Copy(block, i, chunk, 0, Math.Min(inLength, 80 - i));
                BigInteger value = BigInteger.ModPow(new BigInteger(chunk), 65537, modulus);
                byte[] bytes = value.ToByteArray();
                for (int j = 0; j < outLength; j++) result.Add(j < bytes.Length ? bytes[j] : (byte)0);
            }
            return result.Take(56).ToArray();
        }

        private static BigInteger FromBigEndian(byte[] data, int offset, int count)
        {
            byte[] little = new byte[count + 1];
            for (int i = 0; i < count; i++) little[i] = data[offset + count - 1 - i];
            return new BigInteger(little);
        }

        // MIX file id: CRC-32 of the upper-case name, padded the Westwood way.
        internal static uint Id(string name)
        {
            var bytes = new List<byte>(Encoding.ASCII.GetBytes(name.ToUpperInvariant()));
            int rem = bytes.Count % 4;
            if (rem != 0)
            {
                byte fill = bytes[bytes.Count - rem];
                bytes.Add((byte)rem);
                for (int i = 0; i < 3 - rem; i++) bytes.Add(fill);
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }
    }

    // Standard Blowfish. Its initial tables are the hexadecimal digits of pi, computed here exactly.
    internal sealed class Blowfish
    {
        private static uint[] piWords;
        private readonly uint[] p = new uint[18];
        private readonly uint[][] s = new uint[4][];

        internal static uint[] PiWords()
        {
            if (piWords != null) return piWords;
            const int count = 18 + 1024;
            int digits = count * 8 + 16;
            BigInteger scale = BigInteger.One << (4 * digits);
            BigInteger pi = 4 * (4 * ArctanInverse(5, scale) - ArctanInverse(239, scale));
            BigInteger fraction = pi - 3 * scale;
            var words = new uint[count];
            for (int i = 0; i < count; i++)
                words[i] = (uint)((fraction >> (4 * digits - 32 * (i + 1))) & 0xFFFFFFFF);
            piWords = words;
            return words;
        }

        private static BigInteger ArctanInverse(int x, BigInteger scale)
        {
            BigInteger total = 0, term = scale / x, square = x * x;
            int n = 1;
            bool add = true;
            while (!term.IsZero)
            {
                total = add ? total + term / n : total - term / n;
                term /= square; n += 2; add = !add;
            }
            return total;
        }

        internal Blowfish(byte[] key)
        {
            uint[] words = PiWords();
            Array.Copy(words, p, 18);
            for (int i = 0; i < 4; i++) { s[i] = new uint[256]; Array.Copy(words, 18 + 256 * i, s[i], 0, 256); }
            int j = 0;
            for (int i = 0; i < 18; i++)
            {
                uint k = 0;
                for (int b = 0; b < 4; b++) { k = (k << 8) | key[j % key.Length]; j++; }
                p[i] ^= k;
            }
            uint l = 0, r = 0;
            for (int i = 0; i < 18; i += 2) { Encrypt(ref l, ref r); p[i] = l; p[i + 1] = r; }
            foreach (uint[] box in s)
                for (int i = 0; i < 256; i += 2) { Encrypt(ref l, ref r); box[i] = l; box[i + 1] = r; }
        }

        private uint F(uint x) { return ((s[0][x >> 24] + s[1][(x >> 16) & 255]) ^ s[2][(x >> 8) & 255]) + s[3][x & 255]; }

        internal void Encrypt(ref uint l, ref uint r)
        {
            for (int i = 0; i < 16; i++) { l ^= p[i]; r ^= F(l); uint t = l; l = r; r = t; }
            uint u = l; l = r; r = u;
            r ^= p[16]; l ^= p[17];
        }

        private void Decrypt(ref uint l, ref uint r)
        {
            for (int i = 17; i > 1; i--) { l ^= p[i]; r ^= F(l); uint t = l; l = r; r = t; }
            uint u = l; l = r; r = u;
            r ^= p[1]; l ^= p[0];
        }

        internal byte[] DecryptEcb(byte[] data)
        {
            byte[] output = new byte[data.Length - data.Length % 8];
            for (int i = 0; i < output.Length; i += 8)
            {
                uint l = (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
                uint r = (uint)(data[i + 4] << 24 | data[i + 5] << 16 | data[i + 6] << 8 | data[i + 7]);
                Decrypt(ref l, ref r);
                output[i] = (byte)(l >> 24); output[i + 1] = (byte)(l >> 16); output[i + 2] = (byte)(l >> 8); output[i + 3] = (byte)l;
                output[i + 4] = (byte)(r >> 24); output[i + 5] = (byte)(r >> 16); output[i + 6] = (byte)(r >> 8); output[i + 7] = (byte)r;
            }
            return output;
        }
    }
}
