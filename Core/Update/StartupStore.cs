using System;
using System.IO;
using System.Text.Json;

namespace WinNotch.Core.Update
{
    /// <summary>
    /// startup.json and rollback.json in %AppData%\WinNotch, written like settings.json: a temporary file moved over the
    /// old one (a crash mid-write can't leave half a file), never through a link/junction planted in the folder.
    /// A missing or corrupt file reads as null (default state), never as an exception.
    /// </summary>
    public sealed class FileStartupStore : IStartupStore
    {
        private const long MaxFile = 64 * 1024;
        private readonly string _folder;

        public FileStartupStore(string folder) { _folder = folder; }

        public string StatePath => Path.Combine(_folder, "startup.json");
        public string NotePath => Path.Combine(_folder, "rollback.json");

        public StartupState Load() => Read<StartupState>(StatePath);

        public void Save(StartupState state) => Write(StatePath, state);

        public RollbackNote TakeRollbackNote()
        {
            var note = Read<RollbackNote>(NotePath);
            try { if (File.Exists(NotePath) && AppSettings.SafeToWrite(NotePath)) File.Delete(NotePath); } catch { }
            return note;
        }

        public void WriteRollbackNote(RollbackNote note) => Write(NotePath, note);

        public void DeleteRollbackNote()
        {
            if (File.Exists(NotePath) && AppSettings.SafeToWrite(NotePath)) File.Delete(NotePath);
        }

        private static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxFile) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        private void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(_folder);
            using var hold = AppSettings.HoldFolder();
            string tmp = path + ".tmp";
            // refused (a link in the folder, as administrator): the caller logs it instead of believing it was saved
            if (!AppSettings.SafeToWrite(path) || !AppSettings.SafeToWrite(tmp)) throw new IOException("scriere refuzată");
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, true);
        }
    }
}
