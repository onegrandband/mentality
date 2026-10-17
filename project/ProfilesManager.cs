// test comment
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Windows.Forms;

namespace Mentality
{
    [Serializable]
    public class Profile
    {
        public string Name { get; set; }
        public int IntervalMs { get; set; }
        public bool RightButton { get; set; }
        public string MouseClickType { get; set; }
        public int StartDelay { get; set; }
        public bool AutoMinimize { get; set; }
        public bool RunWhenMinimized { get; set; }
        public bool UseHotkey { get; set; }
        public string HotkeyKey { get; set; }
        public int HotkeyModifiers { get; set; }
        public bool LimitToSpecificWindow { get; set; }
        public string TargetWindows { get; set; }
        public int JitterPercent { get; set; }
        public bool PauseOnUserInput { get; set; }
        public bool ScheduleEnabled { get; set; }
        public DateTime ScheduledStartTime { get; set; }
        public int RunMinutes { get; set; }
        public int PauseIdleSeconds { get; set; }
    }

    [XmlRoot("MentalityData")]
    public sealed class MentalityData
    {
        [XmlAttribute]
        public int FormatVersion { get; set; }
        public string HotkeyKey { get; set; }
        public int HotkeyModifiers { get; set; }
        public Profile CurrentSettings { get; set; }
        [XmlArrayItem("Profile")]
        public List<Profile> Profiles { get; set; } = new List<Profile>();
    }

    public static class MentalityDataManager
    {
        private const int CurrentFormatVersion = 1;
        private const long MaxBackupBytes = 10 * 1024 * 1024;

        public static void Export(string path, MentalityData data)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A backup path is required.", nameof(path));
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.FormatVersion = CurrentFormatVersion;
            Validate(data, false);
            var fullPath = Path.GetFullPath(path);
            var tempPath = fullPath + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new XmlSerializer(typeof(MentalityData));
                    serializer.Serialize(stream, data);
                }

                if (new FileInfo(tempPath).Length > MaxBackupBytes) throw new InvalidDataException("The backup exceeds the maximum supported size.");
                if (File.Exists(fullPath)) File.Replace(tempPath, fullPath, null);
                else File.Move(tempPath, fullPath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        public static MentalityData Import(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("The selected backup file was not found.", path);
            if (new FileInfo(path).Length > MaxBackupBytes) throw new InvalidDataException("The backup exceeds the maximum supported size.");

            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxBackupBytes
            };
            using (var reader = XmlReader.Create(path, readerSettings))
            {
                var serializer = new XmlSerializer(typeof(MentalityData));
                var data = serializer.Deserialize(reader) as MentalityData;
                Validate(data, true);
                return data;
            }
        }

        private static void Validate(MentalityData data, bool normalize)
        {
            if (data == null || data.FormatVersion != CurrentFormatVersion) throw new InvalidDataException("This Mentality data file has an unsupported format version.");
            if (data.Profiles == null) data.Profiles = new List<Profile>();
            if (data.Profiles.Count > 500) throw new InvalidDataException("The backup contains too many profiles.");
            if (data.CurrentSettings == null) throw new InvalidDataException("The backup does not contain current settings.");
            if (data.HotkeyModifiers < 0 || (data.HotkeyModifiers & ~15) != 0) throw new InvalidDataException("The backup contains invalid hotkey modifiers.");
            if (!string.IsNullOrEmpty(data.HotkeyKey) && !Enum.TryParse<Keys>(data.HotkeyKey, out _)) throw new InvalidDataException("The backup contains an invalid hotkey.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in data.Profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 100 || !names.Add(profile.Name.Trim()))
                    throw new InvalidDataException("The backup contains an invalid or duplicate profile name.");
                if (normalize) NormalizeProfile(profile);
            }
            if (normalize) NormalizeProfile(data.CurrentSettings);
        }

        private static void NormalizeProfile(Profile profile)
        {
            profile.IntervalMs = Math.Max(0, Math.Min(10000, profile.IntervalMs));
            profile.StartDelay = Math.Max(0, Math.Min(3600, profile.StartDelay));
            profile.JitterPercent = Math.Max(0, Math.Min(100, profile.JitterPercent));
            profile.RunMinutes = Math.Max(0, Math.Min(10080, profile.RunMinutes));
            profile.PauseIdleSeconds = profile.PauseIdleSeconds <= 0 ? 3 : Math.Max(1, Math.Min(3600, profile.PauseIdleSeconds));
            if (profile.TargetWindows != null && profile.TargetWindows.Length > 4096) throw new InvalidDataException("A profile contains an oversized target-window filter.");
            if (profile.HotkeyModifiers < 0 || (profile.HotkeyModifiers & ~15) != 0) throw new InvalidDataException("A profile contains invalid hotkey modifiers.");
            if (!string.IsNullOrEmpty(profile.HotkeyKey) && !Enum.TryParse<Keys>(profile.HotkeyKey, out _)) throw new InvalidDataException("A profile contains an invalid hotkey.");
            if (profile.MouseClickType != "Left" && profile.MouseClickType != "DoubleLeft" && profile.MouseClickType != "Right" && profile.MouseClickType != "Middle")
                profile.MouseClickType = profile.RightButton ? "Right" : "Left";
        }
    }

    public static class ProfilesManager
    {
        private static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mentality");
        private static readonly string ProfilesFile = Path.Combine(AppDir, "profiles.xml");

        public static List<Profile> LoadProfiles()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                if (!File.Exists(ProfilesFile)) return new List<Profile>();
                using (var fs = File.OpenRead(ProfilesFile))
                {
                    var ser = new XmlSerializer(typeof(List<Profile>));
                    return (List<Profile>)ser.Deserialize(fs) ?? new List<Profile>();
                }
            }
            catch
            {
                return new List<Profile>();
            }
        }

        public static bool SaveProfiles(List<Profile> profiles)
        {
            var tempFile = ProfilesFile + ".tmp";
            try
            {
                if (profiles == null) return false;
                Directory.CreateDirectory(AppDir);
                using (var fs = File.Create(tempFile))
                {
                    var ser = new XmlSerializer(typeof(List<Profile>));
                    ser.Serialize(fs, profiles);
                }

                if (File.Exists(ProfilesFile)) File.Replace(tempFile, ProfilesFile, null);
                else File.Move(tempFile, ProfilesFile);
                return true;
            }
            catch
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                return false;
            }
        }
    }
}
