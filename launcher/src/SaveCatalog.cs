using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Ra2CommandCenter
{
    // Red Alert 2 and Yuri's Revenge save games are OLE compound files. Their summary
    // property set holds the description shown in the Load menu (title), the player's
    // country (subject), creation and last-save times, and the executable name
    // (SUN.EXE = Red Alert 2, GAMEMD.EXE... = Yuri's Revenge). The same description is
    // also stored in a "Scenario Description" stream.
    public sealed class SaveInfo
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public string House { get; set; }
        public string Executable { get; set; }
        public DateTime Saved { get; set; }
        public long Size { get; set; }
        public string Error { get; set; }
        public bool IsYuri { get { return (Executable ?? "").StartsWith("GAMEMD", StringComparison.OrdinalIgnoreCase); } }
        public bool IsRa2 { get { return (Executable ?? "").StartsWith("SUN", StringComparison.OrdinalIgnoreCase); } }
        public string Kind
        {
            get
            {
                string name = (FileName ?? "").ToUpperInvariant();
                if (name == "SAVEFFFF.SAV") return "Quick save";
                if (name == "SAVEFFF0.SAV" || name == "SAVEFFF1.SAV" || name == "SAVEFFF2.SAV") return "Autosave";
                return "Saved game";
            }
        }
    }

    public static class SaveCatalog
    {
        public const int MaxTitleLength = 40;

        // Saves in the edition folder and its SavesDir, newest first.
        public static List<SaveInfo> List(string gameRoot)
        {
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(gameRoot, "*.sav", SearchOption.TopDirectoryOnly)) files.Add(file);
            string saves = System.IO.Path.Combine(gameRoot, "SavesDir");
            if (Directory.Exists(saves) && (new DirectoryInfo(saves).Attributes & FileAttributes.ReparsePoint) == 0)
                files.AddRange(Directory.GetFiles(saves, "*.sav", SearchOption.AllDirectories));
            List<SaveInfo> result = files.Select(Read).ToList();
            result.Sort(delegate(SaveInfo a, SaveInfo b) { return b.Saved.CompareTo(a.Saved); });
            return result;
        }

        public static SaveInfo Read(string path)
        {
            FileInfo file = new FileInfo(path);
            SaveInfo info = new SaveInfo { Path = file.FullName, FileName = file.Name, Size = file.Exists ? file.Length : 0, Saved = file.Exists ? file.LastWriteTime : DateTime.MinValue };
            try
            {
                CompoundFile cfb = new CompoundFile(File.ReadAllBytes(path));
                byte[] summary = cfb.Stream("\u0005SummaryInformation");
                if (summary != null)
                {
                    Dictionary<int, object> props = PropertySet(summary);
                    object value;
                    if (props.TryGetValue(2, out value)) info.Title = value as string;
                    if (props.TryGetValue(3, out value)) info.House = value as string;
                    if (props.TryGetValue(18, out value)) info.Executable = value as string;
                    if (props.TryGetValue(13, out value) && value is DateTime) info.Saved = ((DateTime)value).ToLocalTime();
                }
                if (String.IsNullOrEmpty(info.Title))
                {
                    byte[] description = cfb.Stream("Scenario Description");
                    if (description != null) info.Title = Encoding.Unicode.GetString(description).TrimEnd('\0');
                }
                if (String.IsNullOrEmpty(info.Executable))
                {
                    byte[] exe = cfb.Stream("Executable Name");
                    if (exe != null) info.Executable = Encoding.Unicode.GetString(exe).TrimEnd('\0');
                }
            }
            catch (Exception)
            {
                // Any malformed or unreadable file is listed as such; nothing is written.
                info.Error = "This file is not a readable saved game.";
            }
            return info;
        }

        public static void ValidateTitle(string title)
        {
            if (title == null || title.Trim().Length == 0) throw new ArgumentException("Enter a name for the save.");
            if (title.Trim().Length > MaxTitleLength) throw new ArgumentException("Save names can be at most " + MaxTitleLength + " characters.");
            if (title.Any(Char.IsControl)) throw new ArgumentException("Save names cannot contain line breaks or control characters.");
        }

        // Changes the description shown in the Load menu. A copy is kept in backupFolder;
        // if the file does not read back with the new name, the copy is put back.
        public static string Rename(string path, string title, string backupFolder)
        {
            ValidateTitle(title);
            title = title.Trim();
            SaveInfo before = Read(path);
            if (before.Error != null) throw new InvalidDataException(before.Error);
            Directory.CreateDirectory(backupFolder);
            string backup = System.IO.Path.Combine(backupFolder, System.IO.Path.GetFileNameWithoutExtension(path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + System.IO.Path.GetExtension(path));
            File.Copy(path, backup);
            if (!File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(path))) throw new IOException("The backup copy of the save could not be verified; the save was not changed.");
            try
            {
                StructuredStorage.SetTitle(path, title);
                SaveInfo after = Read(path);
                if (after.Error != null || after.Title != title || after.Executable != before.Executable || after.House != before.House)
                    throw new InvalidDataException("The renamed save did not read back correctly.");
            }
            catch
            {
                File.Copy(backup, path, true);
                throw;
            }
            return backup;
        }

        // "Delete" moves the save into deletedFolder, where it can be moved back.
        public static string MoveToDeleted(string path, string deletedFolder)
        {
            Directory.CreateDirectory(deletedFolder);
            string target = System.IO.Path.Combine(deletedFolder, System.IO.Path.GetFileName(path));
            if (File.Exists(target))
                target = System.IO.Path.Combine(deletedFolder, System.IO.Path.GetFileNameWithoutExtension(path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + System.IO.Path.GetExtension(path));
            File.Move(path, target);
            return target;
        }

        public static string Describe(SaveInfo save)
        {
            List<string> parts = new List<string>();
            parts.Add(save.Kind);
            if (!String.IsNullOrEmpty(save.House)) parts.Add(save.House);
            if (save.Saved > DateTime.MinValue) parts.Add(save.Saved.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture));
            parts.Add(save.FileName);
            parts.Add((save.Size / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB");
            return String.Join("  ·  ", parts.ToArray());
        }

        // ---- Property set (MS-OLEPS) ----
        internal static Dictionary<int, object> PropertySet(byte[] data)
        {
            Dictionary<int, object> result = new Dictionary<int, object>();
            if (data.Length < 48) throw new InvalidDataException("Short property set.");
            int section = (int)BitConverter.ToUInt32(data, 44);
            if (section < 48 || section + 8 > data.Length) throw new InvalidDataException("Bad property section.");
            int count = (int)BitConverter.ToUInt32(data, section + 4);
            if (count < 0 || count > 1000 || section + 8 + count * 8 > data.Length) throw new InvalidDataException("Bad property count.");
            for (int i = 0; i < count; i++)
            {
                int id = (int)BitConverter.ToUInt32(data, section + 8 + i * 8);
                int at = section + (int)BitConverter.ToUInt32(data, section + 12 + i * 8);
                if (at < section || at + 8 > data.Length) continue;
                int type = BitConverter.ToUInt16(data, at);
                if (type == 31) // VT_LPWSTR
                {
                    int chars = (int)BitConverter.ToUInt32(data, at + 4);
                    if (chars >= 0 && chars < 4096 && at + 8 + chars * 2 <= data.Length) result[id] = Encoding.Unicode.GetString(data, at + 8, chars * 2).TrimEnd('\0');
                }
                else if (type == 30) // VT_LPSTR
                {
                    int bytes = (int)BitConverter.ToUInt32(data, at + 4);
                    if (bytes >= 0 && bytes < 4096 && at + 8 + bytes <= data.Length) result[id] = Encoding.Default.GetString(data, at + 8, bytes).TrimEnd('\0');
                }
                else if (type == 64) // VT_FILETIME
                {
                    long ticks = BitConverter.ToInt64(data, at + 4);
                    if (ticks > 0 && ticks < DateTime.MaxValue.ToFileTimeUtc()) result[id] = DateTime.FromFileTimeUtc(ticks);
                }
                else if (type == 3 || type == 2) result[id] = BitConverter.ToInt32(data, at + 4);
            }
            return result;
        }

        // ---- Read-only compound file reader (MS-CFB) ----
        internal sealed class CompoundFile
        {
            private const uint EndOfChain = 0xFFFFFFFA;
            private readonly byte[] data;
            private readonly int sectorSize, miniSectorSize;
            private readonly uint miniCutoff;
            private readonly List<uint> fat = new List<uint>(), miniFat = new List<uint>();
            private readonly List<Entry> entries = new List<Entry>();
            private readonly byte[] miniStream;
            private struct Entry { public string Name; public int Type; public uint Start; public long Size; }

            public CompoundFile(byte[] bytes)
            {
                data = bytes;
                if (data.Length < 512 || BitConverter.ToUInt64(data, 0) != 0xE11AB1A1E011CFD0UL) throw new InvalidDataException("Not a compound file.");
                int shift = BitConverter.ToUInt16(data, 0x1E), miniShift = BitConverter.ToUInt16(data, 0x20);
                if ((shift != 9 && shift != 12) || miniShift != 6) throw new InvalidDataException("Unsupported compound file.");
                sectorSize = 1 << shift; miniSectorSize = 1 << miniShift;
                uint fatSectors = BitConverter.ToUInt32(data, 0x2C), directory = BitConverter.ToUInt32(data, 0x30);
                miniCutoff = BitConverter.ToUInt32(data, 0x38);
                uint miniFatStart = BitConverter.ToUInt32(data, 0x3C), difat = BitConverter.ToUInt32(data, 0x44);
                List<uint> fatLocations = new List<uint>();
                for (int i = 0; i < 109; i++) fatLocations.Add(BitConverter.ToUInt32(data, 0x4C + i * 4));
                for (int guard = 0; difat < EndOfChain && guard < 10000; guard++)
                {
                    int at = Offset(difat);
                    for (int i = 0; i < sectorSize / 4 - 1; i++) fatLocations.Add(BitConverter.ToUInt32(data, at + i * 4));
                    difat = BitConverter.ToUInt32(data, at + sectorSize - 4);
                }
                if (fatSectors > fatLocations.Count) throw new InvalidDataException("Bad FAT size.");
                for (int f = 0; f < fatSectors; f++)
                {
                    int at = Offset(fatLocations[f]);
                    for (int i = 0; i < sectorSize / 4; i++) fat.Add(BitConverter.ToUInt32(data, at + i * 4));
                }
                byte[] dir = Chain(directory, -1);
                for (int i = 0; i + 128 <= dir.Length; i += 128)
                {
                    int nameLength = BitConverter.ToUInt16(dir, i + 64);
                    string name = nameLength >= 2 && nameLength <= 64 ? Encoding.Unicode.GetString(dir, i, nameLength - 2) : "";
                    entries.Add(new Entry { Name = name, Type = dir[i + 66], Start = BitConverter.ToUInt32(dir, i + 116), Size = BitConverter.ToUInt32(dir, i + 120) });
                }
                if (entries.Count == 0 || entries[0].Type != 5) throw new InvalidDataException("Missing root entry.");
                miniStream = Chain(entries[0].Start, entries[0].Size);
                byte[] miniFatBytes = Chain(miniFatStart, -1);
                for (int i = 0; i + 4 <= miniFatBytes.Length; i += 4) miniFat.Add(BitConverter.ToUInt32(miniFatBytes, i));
            }

            // Header structures must be whole sectors; stream data may end in a short
            // final sector, because the game does not pad its save files.
            private int Offset(uint sector)
            {
                long at = (long)(sector + 1) * sectorSize;
                if (sector >= EndOfChain || at + sectorSize > data.Length) throw new InvalidDataException("Sector out of range.");
                return (int)at;
            }

            private byte[] Chain(uint start, long size)
            {
                MemoryStream output = new MemoryStream();
                for (int guard = 0; start < EndOfChain; guard++)
                {
                    if (guard > data.Length / sectorSize + 1 || start >= fat.Count) throw new InvalidDataException("Broken sector chain.");
                    long at = (long)(start + 1) * sectorSize;
                    if (at >= data.Length) throw new InvalidDataException("Sector out of range.");
                    output.Write(data, (int)at, (int)Math.Min(sectorSize, data.Length - at));
                    start = fat[(int)start];
                }
                byte[] all = output.ToArray();
                if (size < 0) return all;
                if (size > all.Length) throw new InvalidDataException("Stream shorter than recorded.");
                Array.Resize(ref all, (int)size);
                return all;
            }

            private byte[] MiniChain(uint start, long size)
            {
                MemoryStream output = new MemoryStream();
                for (int guard = 0; start < EndOfChain && output.Length < size; guard++)
                {
                    if (guard > miniFat.Count || start >= miniFat.Count || (long)(start + 1) * miniSectorSize > miniStream.Length) throw new InvalidDataException("Broken mini chain.");
                    output.Write(miniStream, (int)start * miniSectorSize, miniSectorSize);
                    start = miniFat[(int)start];
                }
                byte[] all = output.ToArray();
                if (size > all.Length) throw new InvalidDataException("Mini stream shorter than recorded.");
                Array.Resize(ref all, (int)size);
                return all;
            }

            public List<string> StreamNames()
            {
                return entries.Where(delegate(Entry e) { return e.Type == 2; }).Select(delegate(Entry e) { return e.Name; }).ToList();
            }

            public byte[] Stream(string name)
            {
                foreach (Entry entry in entries)
                    if (entry.Type == 2 && entry.Name == name)
                        return entry.Size < miniCutoff ? MiniChain(entry.Start, entry.Size) : Chain(entry.Start, entry.Size);
                return null;
            }
        }
    }

    // Writes through the Windows structured-storage API, the same one the game uses.
    internal static class StructuredStorage
    {
        private const uint STGM_READWRITE = 0x2, STGM_SHARE_EXCLUSIVE = 0x10, STGM_TRANSACTED = 0x10000, STGM_CREATE = 0x1000;
        private static readonly Guid SummaryInformation = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
        private static Guid IStorageId = new Guid("0000000B-0000-0000-C000-000000000046");

        [DllImport("ole32.dll")]
        private static extern int StgOpenStorageEx([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint format, uint attrs, IntPtr options, IntPtr reserved, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object storage);
        [DllImport("ole32.dll")]
        private static extern int StgCreateStorageEx([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint format, uint attrs, IntPtr options, IntPtr reserved, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object storage);

        [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IStorage
        {
            [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out System.Runtime.InteropServices.ComTypes.IStream stream);
            [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr r1, uint mode, uint r2, out System.Runtime.InteropServices.ComTypes.IStream stream);
            [PreserveSig] int CreateStorage(IntPtr a, uint b, uint c, uint d, out IntPtr e);
            [PreserveSig] int OpenStorage(IntPtr a, IntPtr b, uint c, IntPtr d, uint e, out IntPtr f);
            [PreserveSig] int CopyTo(uint a, IntPtr b, IntPtr c, IntPtr d);
            [PreserveSig] int MoveElementTo(IntPtr a, IntPtr b, IntPtr c, uint d);
            [PreserveSig] int Commit(uint flags);
            [PreserveSig] int Revert();
        }

        [ComImport, Guid("0000013A-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPropertySetStorage
        {
            [PreserveSig] int Create(ref Guid fmtid, IntPtr clsid, uint flags, uint mode, out IPropertyStorage storage);
            [PreserveSig] int Open(ref Guid fmtid, uint mode, out IPropertyStorage storage);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PropSpec { public uint Kind; public IntPtr IdOrName; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PropVariant { public ushort Type; public ushort R1, R2, R3; public IntPtr Value; public IntPtr Value2; }

        [ComImport, Guid("00000138-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPropertyStorage
        {
            [PreserveSig] int ReadMultiple(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PropSpec[] specs, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PropVariant[] values);
            [PreserveSig] int WriteMultiple(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PropSpec[] specs, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PropVariant[] values, uint firstNameId);
            [PreserveSig] int DeleteMultiple(uint count, IntPtr specs);
            [PreserveSig] int ReadPropertyNames(uint count, IntPtr ids, IntPtr names);
            [PreserveSig] int WritePropertyNames(uint count, IntPtr ids, IntPtr names);
            [PreserveSig] int DeletePropertyNames(uint count, IntPtr ids);
            [PreserveSig] int Commit(uint flags);
        }

        private static void Check(int hr, string what) { if (hr < 0) throw new IOException(what + " failed (0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + ")."); }

        public static void SetTitle(string path, string title)
        {
            object root;
            Check(StgOpenStorageEx(path, STGM_READWRITE | STGM_SHARE_EXCLUSIVE | STGM_TRANSACTED, 0, 0, IntPtr.Zero, IntPtr.Zero, ref IStorageId, out root), "Opening the save");
            try
            {
                WriteText((IPropertySetStorage)root, (IStorage)root, title, null);
                Check(((IStorage)root).Commit(0), "Saving the new name");
            }
            finally { Marshal.ReleaseComObject(root); }
        }

        // Test helper: a minimal save-like file written by Windows itself.
        internal static void CreateTestSave(string path, string title, string executable)
        {
            object root;
            Check(StgCreateStorageEx(path, STGM_READWRITE | STGM_SHARE_EXCLUSIVE | STGM_CREATE, 0, 0, IntPtr.Zero, IntPtr.Zero, ref IStorageId, out root), "Creating a test save");
            try
            {
                IPropertySetStorage sets = (IPropertySetStorage)root;
                Guid fmtid = SummaryInformation;
                IPropertyStorage props;
                Check(sets.Create(ref fmtid, IntPtr.Zero, 0, STGM_READWRITE | STGM_SHARE_EXCLUSIVE | STGM_CREATE, out props), "Creating summary");
                Marshal.ReleaseComObject(props);
                System.Runtime.InteropServices.ComTypes.IStream stream;
                Check(((IStorage)root).CreateStream("Scenario Description", STGM_READWRITE | STGM_SHARE_EXCLUSIVE | STGM_CREATE, 0, 0, out stream), "Creating description");
                Marshal.ReleaseComObject(stream);
                WriteText(sets, (IStorage)root, title, executable);
                Check(((IStorage)root).Commit(0), "Committing test save");
            }
            finally { Marshal.ReleaseComObject(root); }
        }

        private static void WriteText(IPropertySetStorage sets, IStorage storage, string title, string executable)
        {
            Guid fmtid = SummaryInformation;
            IPropertyStorage props;
            Check(sets.Open(ref fmtid, STGM_READWRITE | STGM_SHARE_EXCLUSIVE, out props), "Opening the save summary");
            List<IntPtr> strings = new List<IntPtr>();
            try
            {
                List<PropSpec> specs = new List<PropSpec>();
                List<PropVariant> values = new List<PropVariant>();
                IntPtr text = Marshal.StringToCoTaskMemUni(title); strings.Add(text);
                specs.Add(new PropSpec { Kind = 1, IdOrName = new IntPtr(2) });
                values.Add(new PropVariant { Type = 31, Value = text });
                if (executable != null)
                {
                    IntPtr exe = Marshal.StringToCoTaskMemUni(executable); strings.Add(exe);
                    specs.Add(new PropSpec { Kind = 1, IdOrName = new IntPtr(18) });
                    values.Add(new PropVariant { Type = 31, Value = exe });
                }
                Check(props.WriteMultiple((uint)specs.Count, specs.ToArray(), values.ToArray(), 2), "Writing the save name");
                Check(props.Commit(0), "Committing the save summary");
            }
            finally
            {
                Marshal.ReleaseComObject(props);
                foreach (IntPtr p in strings) Marshal.FreeCoTaskMem(p);
            }
            System.Runtime.InteropServices.ComTypes.IStream stream;
            int hr = storage.OpenStream("Scenario Description", IntPtr.Zero, STGM_READWRITE | STGM_SHARE_EXCLUSIVE, 0, out stream);
            if (hr < 0) return; // Older saves may not have the stream; the summary title is what the Load menu shows.
            try
            {
                byte[] bytes = Encoding.Unicode.GetBytes(title + "\0");
                stream.SetSize(bytes.Length);
                stream.Seek(0, 0, IntPtr.Zero);
                stream.Write(bytes, bytes.Length, IntPtr.Zero);
                stream.Commit(0);
            }
            finally { Marshal.ReleaseComObject(stream); }
        }
    }
}
