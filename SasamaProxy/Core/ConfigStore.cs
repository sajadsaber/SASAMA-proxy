using System;
using System.IO;
using System.Xml.Serialization;
using SasamaProxy.Models;

namespace SasamaProxy.Core
{
    public static class ConfigStore
    {
        private static string ConfigPath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SasamaProxy");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.xml");
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new AppConfig();

                var serializer = new XmlSerializer(typeof(AppConfig));
                using (var stream = File.OpenRead(ConfigPath))
                {
                    return (AppConfig)serializer.Deserialize(stream) ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ConfigStore: failed to load config, using defaults - " + ex.Message);
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(AppConfig));
                using (var stream = File.Create(ConfigPath))
                {
                    serializer.Serialize(stream, config);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ConfigStore: failed to save config - " + ex.Message);
            }
        }
    }
}
