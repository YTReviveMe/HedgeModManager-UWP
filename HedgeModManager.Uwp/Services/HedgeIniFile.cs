using System;
using System.Collections.Generic;
using System.Linq;

namespace HedgeModManager.Uwp.Services
{
    public sealed class HedgeIniFile
    {
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> _groups =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);

        public static HedgeIniFile Parse(string text)
        {
            var ini = new HedgeIniFile();
            var group = string.Empty;

            foreach (var rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    group = line.Substring(1, line.Length - 2).Trim();
                    ini.EnsureGroup(group);
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                    continue;

                var key = line.Substring(0, equalsIndex).Trim();
                var value = Unquote(line.Substring(equalsIndex + 1).Trim());
                ini.Add(group, key, value);
            }

            return ini;
        }

        public void Add(string group, string key, string value)
        {
            EnsureGroup(group).Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        }

        public string Get(string group, string key)
        {
            return GetAll(group, key).LastOrDefault() ?? string.Empty;
        }

        public IReadOnlyList<string> GetAll(string group, string key)
        {
            if (!_groups.TryGetValue(group, out var values))
                return Array.Empty<string>();

            return values
                .Where(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Value)
                .ToList();
        }

        public IReadOnlyList<KeyValuePair<string, string>> GetGroup(string group)
        {
            if (!_groups.TryGetValue(group, out var values))
                return Array.Empty<KeyValuePair<string, string>>();

            return values;
        }

        private List<KeyValuePair<string, string>> EnsureGroup(string group)
        {
            group = group ?? string.Empty;
            if (!_groups.TryGetValue(group, out var values))
            {
                values = new List<KeyValuePair<string, string>>();
                _groups.Add(group, values);
            }

            return values;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);

            return value;
        }
    }
}
