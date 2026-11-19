using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using MonitorPlugin;

namespace deskmonitor
{
    internal sealed class InstalledPlugin
    {
        public InstalledPlugin(Manifest manifest, string location, bool singleFile)
        {
            Manifest = manifest;
            Location = location;
            SingleFile = singleFile;
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(Exe);
            FileLength = new FileInfo(Exe).Length;
        }

        public Manifest Manifest { get; }
        public string Location { get; }
        public bool SingleFile { get; }
        public string Exe => SingleFile ? Location : Path.Combine(Location, PluginCatalog.Executable);
        public string Name => Manifest.Name;
        public DateTime LastWriteTimeUtc { get; }
        public long FileLength { get; }

        public bool SameFile(InstalledPlugin other) => Exe == other.Exe
            && Manifest.Version == other.Manifest.Version && LastWriteTimeUtc == other.LastWriteTimeUtc
            && FileLength == other.FileLength && Name == other.Name
            && Manifest.IntervalSeconds == other.Manifest.IntervalSeconds;
    }

    internal sealed class PluginCatalog
    {
        internal const string Executable = "plugin.exe";
        internal const string SingleFileExtension = ".plugin";
        private static readonly byte[] TrailerMagic = Encoding.ASCII.GetBytes("DeskMonitorPlugin1");
        private const string ManifestFile = "manifest.json";
        private const int MaxManifestBytes = 16 * 1024;
        private const int MaxProgramBytes = 100 * 1024 * 1024;
        private static readonly Regex PluginId = new Regex(@"^[a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+$");
        private static readonly Regex InputFile = new Regex(@"^[\p{L}\p{N}_ -][\p{L}\p{N} ._()-]{0,79}\.[\p{L}\p{N}]{1,10}$");
        private readonly string _root;

        public PluginCatalog(string root) => _root = Path.GetFullPath(root);
        public string Root => _root;

        internal static bool IsSingleFile(string path) =>
            string.Equals(Path.GetExtension(path), SingleFileExtension, StringComparison.OrdinalIgnoreCase);

        public void EnsureRoot()
        {
            Directory.CreateDirectory(_root);
            RejectReparsePoint(_root);
        }

        public IReadOnlyList<InstalledPlugin> List()
        {
            var found = new List<InstalledPlugin>();
            if (!Directory.Exists(_root)) return found;
            RejectReparsePoint(_root);
            foreach (string file in Directory.EnumerateFiles(_root).Where(IsSingleFile))
            {
                try { found.Add(new InstalledPlugin(ReadSingleFile(file), file, singleFile: true)); }
                catch (Exception ex) when (IsCatalogError(ex))
                {
                    Logger.WarnOnce(file, "读取单文件插件失败", ex);
                }
            }
            foreach (string directory in Directory.EnumerateDirectories(_root, "plugin-*"))
            {
                try
                {
                    string manifest = Path.Combine(directory, ManifestFile);
                    string executable = Path.Combine(directory, Executable);
                    RejectReparsePoint(directory);
                    RejectReparsePoint(manifest);
                    RejectReparsePoint(executable);
                    var definition = ReadManifest(ManifestFile, () => File.OpenRead(manifest),
                        new FileInfo(manifest).Length);
                    if (!string.Equals(Path.GetFileName(directory), FolderName(definition.Id), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("插件目录与 ID 不匹配");
                    found.Add(new InstalledPlugin(definition, directory, singleFile: false));
                }
                catch (Exception ex) when (IsCatalogError(ex))
                {
                    Logger.WarnOnce(directory, "读取插件清单失败", ex);
                }
            }
            return found.GroupBy(plugin => plugin.Manifest.Id, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(plugin => plugin.SingleFile)
                    .ThenByDescending(plugin => Version.Parse(plugin.Manifest.Version))
                    .ThenBy(plugin => plugin.Location, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(plugin => plugin.Name, StringComparer.CurrentCulture)
                .ThenBy(plugin => plugin.Manifest.Id, StringComparer.Ordinal).ToArray();
        }

        public InstalledPlugin Install(string path)
        {
            if (IsSingleFile(path)) return InstallSingleFile(path);
            if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                return InstallLegacyZip(path);
            throw new InvalidDataException("请选择 .plugin 或旧版 ZIP 插件");
        }

        private InstalledPlugin InstallSingleFile(string path)
        {
            var manifest = ReadSingleFile(path);
            EnsureRoot();
            string target = Path.Combine(_root, FolderName(manifest.Id) + SingleFileExtension);
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), _root, StringComparison.OrdinalIgnoreCase))
                return new InstalledPlugin(manifest, Path.GetFullPath(path), singleFile: true);

            string staging = Path.Combine(_root, ".install-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.Copy(path, staging, overwrite: false);
                var copied = ReadSingleFile(staging);
                if (copied.Id != manifest.Id) throw new InvalidDataException("导入期间插件 ID 发生变化");
                if (File.Exists(target))
                {
                    RejectReparsePoint(target);
                    File.Replace(staging, target, null);
                }
                else File.Move(staging, target);
                return new InstalledPlugin(copied, target, singleFile: true);
            }
            finally { File.Delete(staging); }
        }

        private static Manifest ReadSingleFile(string path)
        {
            RejectReparsePoint(path);
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                int trailerLength = sizeof(int) + TrailerMagic.Length;
                if (stream.Length <= 2 + trailerLength || stream.Length > MaxProgramBytes + MaxManifestBytes + trailerLength)
                    throw new InvalidDataException("单文件插件大小无效");
                if (reader.ReadByte() != 'M' || reader.ReadByte() != 'Z')
                    throw new InvalidDataException("单文件插件必须包含 Windows 可执行程序");
                stream.Seek(-trailerLength, SeekOrigin.End);
                int length = reader.ReadInt32();
                if (!reader.ReadBytes(TrailerMagic.Length).SequenceEqual(TrailerMagic)
                    || length <= 0 || length > MaxManifestBytes || stream.Length - trailerLength - length < 2)
                    throw new InvalidDataException("缺少有效的 DeskMonitor 插件清单");
                stream.Seek(-trailerLength - length, SeekOrigin.End);
                byte[] manifest = reader.ReadBytes(length);
                if (manifest.Length != length) throw new EndOfStreamException();
                return ReadManifest(ManifestFile, () => new MemoryStream(manifest, writable: false), length);
            }
        }

        private InstalledPlugin InstallLegacyZip(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var metadata = FindEntry(zip, ManifestFile);
                var manifest = ReadManifest(ManifestFile, metadata.Open, metadata.Length);
                var binary = FindEntry(zip, Executable);
                if (binary.Length < 2 || binary.Length > MaxProgramBytes)
                    throw new InvalidDataException("plugin.exe 大小超出支持范围");
                using (var header = binary.Open())
                    if (header.ReadByte() != 'M' || header.ReadByte() != 'Z')
                        throw new InvalidDataException("plugin.exe 必须是 Windows 可执行文件");

                EnsureRoot();
                string target = Path.Combine(_root, FolderName(manifest.Id));
                if (Directory.Exists(target)) throw new InvalidDataException("此插件已安装");
                string staging = Path.Combine(_root, ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    CopyEntry(metadata, staging, MaxManifestBytes);
                    CopyEntry(binary, staging, MaxProgramBytes);
                    Directory.Move(staging, target);
                }
                finally
                {
                    if (Directory.Exists(staging))
                    {
                        File.Delete(Path.Combine(staging, ManifestFile));
                        File.Delete(Path.Combine(staging, Executable));
                        Directory.Delete(staging);
                    }
                }
                return new InstalledPlugin(manifest, target, singleFile: false);
            }
        }

        public void Remove(InstalledPlugin plugin)
        {
            RejectReparsePoint(_root);
            string location = Path.GetFullPath(plugin.Location);
            if (!string.Equals(Path.GetDirectoryName(location), _root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件文件应位于插件目录中");
            RejectReparsePoint(location);
            // Move out of discovery in one operation and retain the files for recovery.
            string destination = Path.Combine(_root, ".removed-" + Guid.NewGuid().ToString("N"));
            if (plugin.SingleFile) File.Move(location, destination);
            else Directory.Move(location, destination);
        }

        private static bool IsCatalogError(Exception ex) => ex is IOException || ex is UnauthorizedAccessException
            || ex is SerializationException || ex is InvalidDataException || ex is System.Xml.XmlException;

        private static string FolderName(string id) => "plugin-" + id;

        private static Manifest ReadManifest(string name, Func<Stream> open, long length)
        {
            if (length <= 0 || length > MaxManifestBytes)
                throw new InvalidDataException(name + " 大小无效");
            Manifest manifest;
            using (var stream = open()) manifest = Protocol.Read<Manifest>(stream);
            if (manifest == null || manifest.ApiVersion != Protocol.Version)
                throw new InvalidDataException("插件 API 版本不受支持");
            if (manifest.Id == null || manifest.Id.Length > 64 || !PluginId.IsMatch(manifest.Id)
                || string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 60
                || !Version.TryParse(manifest.Version, out _) || manifest.IntervalSeconds < 5 || manifest.IntervalSeconds > 3600
                || manifest.Description?.Length > 500 || manifest.InputFileLabel?.Length > 60
                || manifest.InputFileName != null && !InputFile.IsMatch(manifest.InputFileName))
                throw new InvalidDataException(name + " 字段无效");
            return manifest;
        }

        private static ZipArchiveEntry FindEntry(ZipArchive zip, string name)
        {
            ZipArchiveEntry found = null;
            foreach (var entry in zip.Entries)
            {
                if (!string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) throw new InvalidDataException("ZIP 有重复的 " + name);
                found = entry;
            }
            return found ?? throw new InvalidDataException("ZIP 根目录缺少 " + name);
        }

        private static void CopyEntry(ZipArchiveEntry entry, string directory, int maxBytes)
        {
            string name = entry.FullName.Equals(Executable, StringComparison.OrdinalIgnoreCase) ? Executable : ManifestFile;
            using (var input = entry.Open())
            using (var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[8192];
                for (int count; (count = input.Read(buffer, 0, buffer.Length)) != 0;)
                {
                    if (output.Length + count > maxBytes) throw new InvalidDataException("插件包条目超过大小限制");
                    output.Write(buffer, 0, count);
                }
            }
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("插件不支持使用链接作为目录或程序文件");
        }
    }
}
