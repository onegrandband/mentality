using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Mentality
{
    [Serializable]
    public class Profile
    {
        public string Name { get; set; }
        public int IntervalMs { get; set; }
        public bool RightButton { get; set; }
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

        public static void SaveProfiles(List<Profile> profiles)
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                using (var fs = File.Create(ProfilesFile))
                {
                    var ser = new XmlSerializer(typeof(List<Profile>));
                    ser.Serialize(fs, profiles);
                }
            }
            catch { }
        }
    }
}
