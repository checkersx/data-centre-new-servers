using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace NextGenHardware
{
    // Keeps saves vanilla on disk. SaveGame builds its data from the live devices in one synchronous
    // call, so for the length of that call each upgraded server and new switch is switched back to its
    // vanilla twin (prefab/type and shop item). The upgrades are written to "<save>.newservers.json"
    // next to the save, and LoadGame's result is re-mapped before anything spawns. Without the mod the
    // save simply loads with vanilla devices in those slots.
    internal static class SaveCompat
    {
        const string SidecarSuffix = ".newservers.json"; // the mod's old name; existing sidecars use it

        sealed class Sidecar
        {
            public int Version { get; set; } = 1;
            public List<Entry> Servers { get; set; } = new();
            public List<Entry> Switches { get; set; } = new();
        }

        sealed class Entry
        {
            public string Id { get; set; }
            public string Key { get; set; }
            public int Twin { get; set; }
            public float[] Pos { get; set; }
        }

        static readonly List<(Server server, Variant variant)> SwappedServers = new();
        static readonly List<(NetworkSwitch device, int prefabID)> SwappedSwitches = new();
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
            catch (Exception e) { NextGenHardwareMod.Log.Error($"SaveGamePrefix: {e}"); }
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
                var sidecar = new Sidecar
                {
                    Servers = SwappedServers.Select(x => MakeEntry(x.server.ServerID, x.variant.Key, x.variant.TwinIndex, x.server.transform)).ToList(),
                    Switches = SwappedSwitches.Select(x => MakeEntry(x.device.switchId, Switches.Key, Switches.TwinIndex, x.device.transform)).ToList(),
                };
                RestoreUpgrades();
                WriteSidecar(_saveName, sidecar);

                // Only tidy up once we know SaveDir() really is where the game writes saves.
                var savePath = Path.Combine(SaveDir(), BaseName(_saveName) ?? "") + ".save";
                if (File.Exists(savePath))
                    DeleteOrphanSidecars();
                else
                    NextGenHardwareMod.Log.Warning($"Expected the save at {savePath} but it isn't there; sidecars may be in the wrong folder.");
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"SaveGamePostfix: {e}"); }
            finally { _inSave = false; }
        }

        static Entry MakeEntry(string id, string key, int twin, Transform t) => new()
        {
            Id = id,
            Key = key,
            Twin = twin,
            Pos = new[] { t.position.x, t.position.y, t.position.z },
        };

        static bool IsLive(Component c)
        {
            if (c == null || string.IsNullOrEmpty(c.gameObject.scene.name)) return false;
            var holder = Registry.Holder;
            return holder == null || !c.transform.IsChildOf(holder); // skip our templates
        }

        static void SwapToVanilla()
        {
            SwappedServers.Clear();
            SwappedSwitches.Clear();
            if (!Registry.Ready) return;

            var servers = Resources.FindObjectsOfTypeAll<Server>();
            for (int i = 0; i < servers.Length; i++)
            {
                var s = servers[i];
                if (!IsLive(s)) continue;
                var v = Registry.ByIndex(s.prefabID);
                if (v == null) continue;
                SwappedServers.Add((s, v));
                s.prefabID = v.TwinIndex;
                s.shopItemSO = v.TwinSO;
            }

            if (!Switches.Ready) return;
            var switches = Resources.FindObjectsOfTypeAll<NetworkSwitch>();
            for (int i = 0; i < switches.Length; i++)
            {
                var n = switches[i];
                if (!IsLive(n) || !Switches.IsOurs(n)) continue;
                SwappedSwitches.Add((n, n.prefabID));
                n.switchType = Switches.TwinIndex;
                n.prefabID = Switches.TwinPrefabID;
                n.shopItemSO = Switches.TwinSO;
            }
        }

        static void RestoreUpgrades()
        {
            foreach (var (s, v) in SwappedServers)
            {
                if (s == null) continue;
                s.prefabID = v.Index;
                s.shopItemSO = v.SO;
            }
            SwappedServers.Clear();

            foreach (var (n, prefabID) in SwappedSwitches)
            {
                if (n == null) continue;
                n.switchType = Switches.Index;
                n.prefabID = prefabID;
                n.shopItemSO = Switches.SO;
            }
            SwappedSwitches.Clear();
        }

        // Safety net: if SaveGame threw, its postfix never ran and devices are still swapped.
        public static void RestoreIfStuck()
        {
            if (_inSave || (SwappedServers.Count == 0 && SwappedSwitches.Count == 0)) return;
            NextGenHardwareMod.Log.Warning($"Save did not finish normally; restoring {SwappedServers.Count} servers and {SwappedSwitches.Count} switches.");
            RestoreUpgrades();
        }

        // ---- loading ----

        public static void LoadGamePostfix(string __0, SaveData __result)
        {
            try
            {
                if (__result == null) return;
                NextGenHardwareMod.Setup("save loading");
                if (!Registry.Ready) return;

                var sidecar = ReadSidecar(__0);
                if (sidecar == null) return;
                var network = __result.networkData;
                if (network == null) return;

                int servers = 0, switches = 0, stale = 0;
                if (network.servers != null && sidecar.Servers?.Count > 0)
                {
                    var match = Matcher(sidecar.Servers);
                    for (int i = 0; i < network.servers.Count; i++)
                    {
                        var sd = network.servers[i];
                        var e = match(sd.serverID, sd.position);
                        if (e == null) continue;
                        var v = Registry.ByKey(e.Key);
                        // A different twin means the device was replaced (e.g. while the mod was removed).
                        if (v == null || sd.prefabID != v.TwinIndex) { stale++; continue; }
                        sd.prefabID = v.Index;
                        servers++;
                    }
                }

                if (network.switches != null && sidecar.Switches?.Count > 0 && Switches.Ready)
                {
                    var match = Matcher(sidecar.Switches);
                    for (int i = 0; i < network.switches.Count; i++)
                    {
                        var sd = network.switches[i];
                        var e = match(sd.switchID, sd.position);
                        if (e == null) continue;
                        if (e.Key != Switches.Key || sd.switchType != Switches.TwinIndex) { stale++; continue; }
                        sd.switchType = Switches.Index;
                        switches++;
                    }
                }

                NextGenHardwareMod.Log.Msg($"Loaded '{__0}': restored {servers} upgraded servers and {switches} switches" +
                                      (stale > 0 ? $", skipped {stale} that no longer match" : "") + ".");
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"LoadGamePostfix: {e}"); }
        }

        // Find a sidecar entry by device ID, falling back to position for devices saved without one.
        static Func<string, Vector3, Entry> Matcher(List<Entry> entries)
        {
            var byId = entries.Where(e => !string.IsNullOrEmpty(e.Id)).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            var byPos = entries.Where(e => string.IsNullOrEmpty(e.Id) && e.Pos?.Length == 3).ToList();
            return (id, pos) =>
            {
                if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var hit)) return hit;
                foreach (var e in byPos)
                    if ((new Vector3(e.Pos[0], e.Pos[1], e.Pos[2]) - pos).sqrMagnitude < 0.0001f) return e;
                return null;
            };
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

        static void WriteSidecar(string saveName, Sidecar sidecar)
        {
            int count = sidecar.Servers.Count + sidecar.Switches.Count;
            if (string.IsNullOrEmpty(saveName))
            {
                if (count > 0)
                    NextGenHardwareMod.Log.Error($"Could not tell which save file was written; {count} upgrades not recorded.");
                return;
            }
            var path = SidecarPath(saveName);
            if (count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            File.WriteAllText(path, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));
            NextGenHardwareMod.Log.Msg($"Saved '{BaseName(saveName)}' as vanilla; {sidecar.Servers.Count} servers and {sidecar.Switches.Count} switches recorded in {path}.");
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
            catch (Exception e) { NextGenHardwareMod.Log.Error($"MoveSaveFilesPostfix('{__0}', '{__1}'): {e.Message}"); }
        }

        public static void DeleteSaveFilePostfix(string __0)
        {
            try
            {
                var path = SidecarPath(__0);
                if (BaseName(__0) != null && File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"DeleteSaveFilePostfix('{__0}'): {e.Message}"); }
        }
    }
}
