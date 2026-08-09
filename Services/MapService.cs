using WebWeaver.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WebWeaver.Services
{
    public static class MapService
    {
        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void Save(MapSaveData data, string path)
        {
            var json = JsonSerializer.Serialize(data, _opts);
            File.WriteAllText(path, json);
        }

        public static MapSaveData? Load(string path)
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MapSaveData>(json, _opts);
        }
    }
}
