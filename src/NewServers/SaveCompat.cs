using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace NewServers
{
    // Keeps saves vanilla on disk. SaveGame builds its data from the live servers in one synchronous
    // call, so for the length of that call each upgraded server is switched back to its vanilla twin
    // (prefabID and shop item). The upgrades are written to "<save>.newservers.json" next to the save,
    // and LoadGame's result is re-mapped before anything spawns. Without the mod the save simply loads
    // with vanilla servers in those slots.
    internal static class SaveCompat
    {
        const string SidecarSuffix = ".newservers.json";

        sealed class Sidecar
        {
            public int Version { get; set; } = 1;
            public List<Entry> Servers { get; set; } = new();
        }

        sealed class Entry
        {
            public string Id { get; set; }
            public string Key { get; set; }
            public int Twin { get; set; }
            public float[] Pos { get; set; }
        }

        static readonly List<(Server server, Variant variant)> Swapped = new();
        static bool _inSave;
        static string _saveName;

        // ---- saving ----

        public static void SaveGamePrefix(string __0)
        {
            try
            {
                _inSave = true;
                _saveName = string.IsNullOrEmpty(__0) ? null : __0;
                SwapToVanilla();
            }
            catch (Exception e) { NewServersMod.Log.Error($"SaveGamePrefix: {e}"); }
        }

        // WriteMeta runs inside SaveGame and is the only place a manual save's file name appears.
        public static void WriteMetaPrefix(string __0)
        {
            if (!string.IsNullOrEmpty(__0)) _saveName = __0;
        }

        public static void SaveGamePostfix()
        {
            try
            {
                var entries = Swapped.Select(x => new Entry
                {
                    Id = x.server.ServerID,
                    Key = x.variant.Key,
                    Twin = x.variant.TwinIndex,
                    Pos = new[] { x.server.transform.position.x, x.server.transform.position.y, x.server.transform.position.z },
                }).ToList();
                RestoreUpgrades();
                WriteSidecar(_saveName, entries);

                // Only tidy up once we know SaveDir() really is where the game writes saves.
                var savePath = Path.Combine(SaveDir(), BaseName(_saveName) ?? "") + ".save";
                if (File.Exists(savePath))
                    DeleteOrphanSidecars();
                else
                    NewServersMod.Log.Warning($"Expected the save at {savePath} but it isn't there; sidecars may be in the wrong folder.");
            }
            catch (Exception e) { NewServersMod.Log.Error($"SaveGamePostfix: {e}"); }
            finally { _inSave = false; }
        }

        static void SwapToVanilla()
        {
            Swapped.Clear();
            if (!Registry.Ready) return;
            var holder = Registry.Holder;
            var servers = Resources.FindObjectsOfTypeAll<Server>();
            for (int i = 0; i < servers.Length; i++)
            {
                var s = servers[i];
                if (s == null || string.IsNullOrEmpty(s.gameObject.scene.name)) continue;
                if (holder != null && s.transform.IsChildOf(holder)) continue; // our templates
                var v = Registry.ByIndex(s.prefabID);
                if (v == null) continue;
                Swapped.Add((s, v));
                s.prefabID = v.TwinIndex;
                s.shopItemSO = v.TwinSO;
            }
        }

        static void RestoreUpgrades()
        {
            foreach (var (s, v) in Swapped)
            {
                if (s == null) continue;
                s.prefabID = v.Index;
                s.shopItemSO = v.SO;
            }
            Swapped.Clear();
        }

        // Safety net: if SaveGame threw, its postfix never ran and servers are still swapped.
        public static void RestoreIfStuck()
        {
            if (_inSave || Swapped.Count == 0) return;
            NewServersMod.Log.Warning($"Save did not finish normally; restoring {Swapped.Count} upgraded servers.");
            RestoreUpgrades();
        }

        // ---- loading ----

        public static void LoadGamePostfix(string __0, SaveData __result)
        {
            try
            {
                if (__result == null) return;
                NewServersMod.Setup("save loading");
                if (!Registry.Ready) return;

                var sidecar = ReadSidecar(__0);
                var servers = __result.networkData?.servers;
                if (sidecar == null || sidecar.Servers.Count == 0 || servers == null) return;

                var byId = sidecar.Servers.Where(e => !string.IsNullOrEmpty(e.Id))
                    .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
                var byPos = sidecar.Servers.Where(e => string.IsNullOrEmpty(e.Id) && e.Pos?.Length == 3).ToList();

                int restored = 0, stale = 0;
                for (int i = 0; i < servers.Count; i++)
                {
                    var sd = servers[i];
                    var e = !string.IsNullOrEmpty(sd.serverID) && byId.TryGetValue(sd.serverID, out var hit) ? hit : NearestByPosition(byPos, sd.position);
                    if (e == null) continue;
                    var v = Registry.ByKey(e.Key);
                    // A different twin means the server was replaced (e.g. while the mod was removed).
                    if (v == null || sd.prefabID != v.TwinIndex) { stale++; continue; }
                    sd.prefabID = v.Index;
                    restored++;
                }
                NewServersMod.Log.Msg($"Loaded '{__0}': restored {restored} upgraded servers" + (stale > 0 ? $", skipped {stale} that no longer match" : "") + ".");
            }
            catch (Exception e) { NewServersMod.Log.Error($"LoadGamePostfix: {e}"); }
        }

        static Entry NearestByPosition(List<Entry> candidates, Vector3 pos)
        {
            foreach (var e in candidates)
            {
                var d = new Vector3(e.Pos[0], e.Pos[1], e.Pos[2]) - pos;
                if (d.sqrMagnitude < 0.0001f) return e;
            }
            return null;
        }

        // ---- sidecar files ----

        static string SaveDir()
        {
            var dir = SaveSystem.saveDirPath;
            return string.IsNullOrEmpty(dir) ? Path.Combine(Application.persistentDataPath, "saves") : dir;
        }

        // Save names may arrive as bare names or paths; normalise to the bare name.
        static string BaseName(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            var name = Path.GetFileName(nameOrPath);
            return name.EndsWith(".save") || name.EndsWith(".meta") ? Path.GetFileNameWithoutExtension(name) : name;
        }

        static string SidecarPath(string saveName) => Path.Combine(SaveDir(), BaseName(saveName) + SidecarSuffix);

        static void WriteSidecar(string saveName, List<Entry> entries)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                if (entries.Count > 0)
                    NewServersMod.Log.Error($"Could not tell which save file was written; {entries.Count} upgrades not recorded.");
                return;
            }
            var path = SidecarPath(saveName);
            if (entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new Sidecar { Servers = entries }, new JsonSerializerOptions { WriteIndented = true }));
            NewServersMod.Log.Msg($"Saved '{BaseName(saveName)}' as vanilla; {entries.Count} upgrades recorded in {path}.");
        }

        static Sidecar ReadSidecar(string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) return null;
            var path = SidecarPath(saveName);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(path));
        }

        static void DeleteOrphanSidecars()
        {
            var dir = SaveDir();
            if (!Directory.Exists(dir)) return;
            foreach (var path in Directory.GetFiles(dir, "*" + SidecarSuffix))
            {
                var saveName = Path.GetFileName(path).Substring(0, Path.GetFileName(path).Length - SidecarSuffix.Length);
                if (!File.Exists(Path.Combine(dir, saveName + ".save")))
                    File.Delete(path);
            }
        }

        // Autosave rotation renames save files; keep the sidecar with its save.
        public static void MoveSaveFilesPostfix(string __0, string __1)
        {
            try
            {
                var from = SidecarPath(__0);
                if (string.IsNullOrEmpty(BaseName(__1)) || !File.Exists(from)) return;
                var to = SidecarPath(__1);
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
            catch (Exception e) { NewServersMod.Log.Error($"MoveSaveFilesPostfix('{__0}', '{__1}'): {e.Message}"); }
        }

        public static void DeleteSaveFilePostfix(string __0)
        {
            try
            {
                var path = SidecarPath(__0);
                if (BaseName(__0) != null && File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) { NewServersMod.Log.Error($"DeleteSaveFilePostfix('{__0}'): {e.Message}"); }
        }
    }
}
