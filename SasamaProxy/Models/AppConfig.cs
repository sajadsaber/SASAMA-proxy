using System.Collections.Generic;

namespace SasamaProxy.Models
{
    // Plain data classes persisted via XmlSerializer (no external
    // dependency needed - XmlSerializer ships with .NET Framework).

    public class AppRule
    {
        public string ProcessName { get; set; }
        public bool Enabled { get; set; }

        // Parameterless constructor required by XmlSerializer.
        public AppRule() { }

        public AppRule(string processName, bool enabled)
        {
            ProcessName = processName;
            Enabled = enabled;
        }
    }

    public class AppConfig
    {
        public bool RunOnStartup { get; set; } = true;
        public bool StartMinimizedToTray { get; set; } = true;

        public string ProxyName { get; set; } = "V2RAY";
        public string ProxyIp { get; set; } = "127.0.0.1";
        public int ProxyPort { get; set; } = 10808;

        public List<AppRule> Rules { get; set; } = new List<AppRule>();
    }
}
