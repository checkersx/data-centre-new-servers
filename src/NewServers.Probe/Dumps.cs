using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using static NewServers.Probe.ProbeMod;

namespace NewServers.Probe
{
    internal static class Dumps
    {
        public static void All(string reason)
        {
            Log($"===== full dump ({reason}) =====", console: true);
            Safe("server prefabs", ServerPrefabs);
            Safe("default ports", DefaultPorts);
            Safe("shop item SOs", ShopItemSOs);
            Safe("shop cards", ShopCards);
            Safe("racks", Racks);
            Safe("network devices", NetworkDevices);
            Safe("sfp modules", SfpModules);
            Live(reason);
            Log("===== dump done =====", console: true);
        }

        public static void Live(string reason)
        {
            Log($"----- live state ({reason}) -----");
            Safe("player", Player);
            Safe("customers", Customers);
            Safe("live servers", LiveServers);
        }

        public static void Safe(string name, Action dump)
        {
            Log($"--- {name}");
            try { dump(); }
            catch (Exception e) { Log($"!!! {name} failed: {e}", console: true); }
        }

        static void ServerPrefabs()
        {
            var mgm = MainGameManager.instance;
            var prefabs = mgm.serverPrefabs;
            Log($"serverPrefabs.Length={prefabs.Length}");
            for (int i = 0; i < prefabs.Length; i++)
            {
                var go = prefabs[i];
                if (go == null) { Log($"[{i}] null"); continue; }
                var s = go.GetComponent<Server>();
                if (s == null) { Log($"[{i}] '{go.name}' has no Server component"); continue; }

                Log($"[{i}] '{go.name}' serverType={s.serverType} sizeInU={s.sizeInU} maxProcessingSpeed={s.maxProcessingSpeed} " +
                    $"prefabID={s.prefabID} hand={s.objectInHandType} eolTime={s.eolTime} timeToBrake={s.timeToBrake} lscale={V(go.transform.localScale)}");
                Log($"     {SO(s.shopItemSO)}");
                var item = s.item;
                if (item != null)
                    Log($"     item '{item.itemName}' size={item.size} price={item.price} unlockedFromXP={item.unlockedFromXP} weight={item.weight} deprecation={item.deprecation}");
                if (s.rb != null)
                    Log($"     rigidbody mass={s.rb.mass}");
                var viaGetter = mgm.GetServerPrefab(i);
                Log($"     GetServerPrefab({i}) -> {N(viaGetter)} same={Same(viaGetter, go)}");
                if (s.canvas != null)
                    Log($"     screen canvas {PathOf(s.canvas.transform, go.transform)} lpos={V(s.canvas.transform.localPosition)} lscale={V(s.canvas.transform.localScale)}");
                Ports(s);
                Visuals(go);
            }
        }

        static void Ports(Server s)
        {
            var links = s.cablelinks;
            if (links == null) { Log("     cablelinks=null"); return; }
            var parts = new List<string>();
            for (int k = 0; k < links.Length; k++)
            {
                var l = links[k];
                parts.Add(l == null ? "null"
                    : $"{l.typeOfLink}/{l.connectionSpeed}G/sfpPort={l.isSFPPort}:supported={l.sfpTypeSupported}:inserted={l.sfpTypeInserted}/fibre={l.isFibrePort}");
            }
            Log($"     ports({links.Length}): {string.Join(" | ", parts)}");
        }

        static void Visuals(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            Log($"     renderers={renderers.Length}");
            for (int k = 0; k < renderers.Length && k < 40; k++)
            {
                var r = renderers[k];
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf == null ? null : mf.sharedMesh;
                var mats = r.sharedMaterials;
                var matNames = new List<string>();
                for (int m = 0; m < mats.Length; m++)
                    matNames.Add(mats[m] == null ? "null" : mats[m].name);
                var t = r.transform;
                var meshText = mesh == null ? "-" : $"{mesh.name} sub={mesh.subMeshCount} size={V(mesh.bounds.size)} center={V(mesh.bounds.center)}";
                Log($"       {PathOf(t, go.transform)} [{r.GetIl2CppType().Name}] lpos={V(t.localPosition)} lscale={V(t.localScale)} mesh={meshText} mats=[{string.Join(", ", matNames)}]");
            }

            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int k = 0; k < colliders.Length && k < 20; k++)
            {
                var c = colliders[k];
                var box = c.TryCast<BoxCollider>();
                var shape = box != null ? $"center={V(box.center)} size={V(box.size)}" : "";
                Log($"       collider {PathOf(c.transform, go.transform)} [{c.GetIl2CppType().Name}] {shape} trigger={c.isTrigger}");
            }
        }

        static void DefaultPorts()
        {
            var arr = MainGameManager.defaultPortsPerServerType;
            if (arr == null) { Log("defaultPortsPerServerType=null"); return; }
            for (int i = 0; i < arr.Length; i++)
                Log($"serverType {i}: [{J(arr[i])}]");
        }

        static void ShopItemSOs()
        {
            var all = Resources.FindObjectsOfTypeAll<ShopItemSO>();
            var list = new List<ShopItemSO>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) list.Add(all[i]);
            Log($"ShopItemSO count={list.Count}");
            foreach (var so in list.OrderBy(x => (int)x.itemType).ThenBy(x => x.itemID))
                Log("  " + SO(so));
        }

        public static void ShopCards()
        {
            var mgm = MainGameManager.instance;
            var shop = mgm.computerShop;
            if (shop == null) { Log("computerShop=null"); return; }
            var items = shop.shopItems;
            Log($"ComputerShop.shopItems={(items == null ? "null" : items.Length.ToString())} shopItemParent={N(shop.shopItemParent)}");
            if (items != null)
            {
                var serverPrefabs = mgm.serverPrefabs;
                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it == null) { Log($"  [{i}] null"); continue; }
                    var parent = it.transform.parent;
                    Log($"  [{i}] '{it.name}' guid={it.guid} display='{it.itemDisplayName}' unlocked={it.isUnlocked} " +
                        $"parent={(parent == null ? "-" : parent.name)} sibling={it.transform.GetSiblingIndex()} active={it.gameObject.activeSelf}");
                    var so = it.shopItemSO;
                    Log($"       {SO(so)}");
                    if (so == null || !IsServer(so.itemType)) continue;
                    try
                    {
                        var viaShop = shop.GetPrefabForItem(so.itemID, so.itemType);
                        var viaMgm = mgm.GetPrefabForItem(so.itemType, so.itemID);
                        var viaArray = so.itemID >= 0 && so.itemID < serverPrefabs.Length ? serverPrefabs[so.itemID] : null;
                        Log($"       shop.GetPrefabForItem->{N(viaShop)} mgm.GetPrefabForItem->{N(viaMgm)} serverPrefabs[itemID]->{N(viaArray)} " +
                            $"same(shop,array)={Same(viaShop, viaArray)} isBigItem={shop.IsBigItem(so.itemType)}");
                    }
                    catch (Exception e) { Log($"       prefab lookup failed: {e.Message}"); }
                }
            }

            var states = SaveData.instance?.shopItemUnlockStates;
            if (states == null) { Log("shopItemUnlockStates=null"); return; }
            Log($"shopItemUnlockStates count={states.Count}");
            foreach (var kv in states)
                Log($"  {kv.Key}={kv.Value}");
        }

        static void Racks()
        {
            var racks = Resources.FindObjectsOfTypeAll<Rack>();
            Log($"Rack objects={racks.Length}");
            var seen = new HashSet<string>();
            for (int i = 0; i < racks.Length; i++)
            {
                var r = racks[i];
                if (r == null) continue;
                var pos = r.positions;
                int len = pos == null ? -1 : pos.Length;
                bool inScene = !string.IsNullOrEmpty(r.gameObject.scene.name);
                if (!seen.Add($"{len}/{inScene}")) continue;
                string spacing = "-";
                if (len >= 2 && pos[0] != null && pos[1] != null)
                    spacing = $"world {V(pos[1].transform.position - pos[0].transform.position)} local {V(pos[1].transform.localPosition - pos[0].transform.localPosition)} " +
                              $"first->last world {V(pos[len - 1].transform.position - pos[0].transform.position)}";
                Log($"  '{r.name}' inScene={inScene} positions={len} spacing(1-0): {spacing}");
            }
            var rackPrefab = MainGameManager.instance.rackPrefab;
            if (rackPrefab != null)
            {
                var rr = rackPrefab.GetComponentInChildren<Rack>(true);
                Log($"rackPrefab '{rackPrefab.name}' positions={(rr == null || rr.positions == null ? -1 : rr.positions.Length)}");
            }
        }

        // Port layouts of switches/routers/firewalls and one server: what an SFP or QSFP port looks like
        // (CableLink settings and the cage mesh), for giving Gen 3/4 servers fibre ports.
        static void NetworkDevices()
        {
            var mgm = MainGameManager.instance;
            DevicePorts("switch", mgm.switchesPrefabs);
            DevicePorts("router", mgm.routersPrefabs);
            DevicePorts("firewall", mgm.firewallsPrefabs);
            var server = mgm.serverPrefabs;
            if (server != null && server.Length > 0)
                DevicePorts("server", new Il2CppReferenceArray<GameObject>(new[] { server[0] }));
        }

        static void DevicePorts(string kind, Il2CppReferenceArray<GameObject> prefabs)
        {
            if (prefabs == null) { Log($"{kind} prefabs=null"); return; }
            for (int i = 0; i < prefabs.Length; i++)
            {
                var go = prefabs[i];
                if (go == null) continue;
                var usable = go.GetComponent<UsableObject>();
                var links = go.GetComponentsInChildren<CableLink>(true);
                var groups = new SortedDictionary<string, int>();
                var examples = new Dictionary<string, CableLink>();
                for (int k = 0; k < links.Length; k++)
                {
                    var l = links[k];
                    var key = $"sfpPort={l.isSFPPort} supported={l.sfpTypeSupported} inserted={l.sfpTypeInserted} fibre={l.isFibrePort} speed={l.connectionSpeed} type={l.typeOfLink}";
                    groups[key] = groups.TryGetValue(key, out var n) ? n + 1 : 1;
                    if (!examples.ContainsKey(key)) examples[key] = l;
                }
                Log($"{kind}[{i}] '{go.name}' size={(usable == null ? -1 : usable.sizeInU)}U ports={links.Length}");
                foreach (var kv in groups)
                {
                    var l = examples[kv.Key];
                    var r = l.GetComponent<Renderer>() ?? l.GetComponentInChildren<Renderer>(true);
                    var mf = r == null ? null : r.GetComponent<MeshFilter>();
                    var mats = new List<string>();
                    if (r != null)
                        for (int m = 0; m < r.sharedMaterials.Length; m++)
                            mats.Add(r.sharedMaterials[m] == null ? "null" : r.sharedMaterials[m].name);
                    var mesh = mf == null || mf.sharedMesh == null ? "-" : $"{mf.sharedMesh.name} size={V(mf.sharedMesh.bounds.size)}";
                    Log($"   {kv.Value} x {kv.Key}");
                    Log($"      e.g. {PathOf(l.transform, go.transform)} lpos={V(l.transform.localPosition)} lscale={V(l.transform.localScale)} " +
                        $"renderer={(r == null ? "-" : PathOf(r.transform, go.transform))} mesh={mesh} mats=[{string.Join(", ", mats)}]");
                }
            }
        }

        static void SfpModules()
        {
            var prefabs = MainGameManager.instance.sfpPrefabs;
            if (prefabs == null) { Log("sfpPrefabs=null"); return; }
            for (int i = 0; i < prefabs.Length; i++)
            {
                var go = prefabs[i];
                if (go == null) continue;
                var m = go.GetComponent<SFPModule>();
                Log(m == null ? $"sfp[{i}] '{go.name}' (no SFPModule)" : $"sfp[{i}] '{go.name}' speed={m.speed} sfpType={m.sfpType} prefabID={m.prefabID} {SO(m.shopItemSO)}");
            }
        }

        static void Player()
        {
            var p = PlayerManager.instance?.playerClass;
            Log(p == null ? "player=null" : $"player money={p.money} xp={p.xp} reputation={p.reputation}");
        }

        static void Customers()
        {
            var mgm = MainGameManager.instance;
            var items = mgm.customerItems;
            Log($"customerItems={(items == null ? "null" : items.Length.ToString())}");
            if (items != null)
                for (int i = 0; i < items.Length; i++)
                {
                    var c = items[i];
                    if (c == null) continue;
                    string total;
                    try { total = mgm.GetCustomerTotalRequirement(c).ToString("F0"); }
                    catch (Exception e) { total = "err " + e.Message; }
                    Log($"  id={c.customerID} '{c.customerName}' difficulty={c.difficulty} rep={c.reputation} sla={c.sla} fine/h={c.downtimeFinePerHour} appTypes=[{J(c.appTypes)}] totalRequirement={total}");
                }

            var bases = Resources.FindObjectsOfTypeAll<CustomerBase>();
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || b.customerItem == null || string.IsNullOrEmpty(b.gameObject.scene.name)) continue;
                Log($"  base {b.customerBaseID} customer={b.customerID} '{b.customerItem.customerName}' req=[{J(b.appsSpeedRequirements)}] " +
                    $"current=[{J(b.appsSpeedCurrent)}] totalReq={b.currentTotalAppSpeeRequirements} maxReq={b.maximumAppRequirementsSpeedTotal} currentSpeed={b.currentSpeed}");
            }
        }

        static void LiveServers()
        {
            var servers = Resources.FindObjectsOfTypeAll<Server>();
            var groups = new SortedDictionary<string, int>();
            var live = new List<Server>();
            int notInRack = 0;
            for (int i = 0; i < servers.Length; i++)
            {
                var s = servers[i];
                if (s == null || string.IsNullOrEmpty(s.gameObject.scene.name)) continue;
                live.Add(s);
                var key = $"prefabID={s.prefabID} type={s.serverType} size={s.sizeInU}U max={s.maxProcessingSpeed}";
                groups[key] = groups.TryGetValue(key, out var n) ? n + 1 : 1;
                if (s.currentRackPosition == null) notInRack++;
            }
            Log($"live servers={live.Count} notInRack={notInRack}");
            foreach (var kv in groups)
                Log($"  {kv.Value} x {kv.Key}");

            // New Servers variants (prefabID beyond the vanilla 8): do they reach their max?
            foreach (var s in live.Where(x => x.prefabID >= 8).Take(40))
                Log($"  upgraded: {s.ServerID} prefabID={s.prefabID} current={s.currentProcessingSpeed} max={s.maxProcessingSpeed} on={s.isOn} customer={s.GetCustomerID()}");

            // Busiest servers and their link speeds: does link speed cap throughput?
            foreach (var s in live.OrderByDescending(x => x.currentProcessingSpeed).Take(8))
            {
                var speeds = new List<string>();
                var links = s.activeLinks;
                if (links != null)
                    for (int k = 0; k < links.Count; k++)
                        speeds.Add(links[k] == null ? "null" : $"{links[k].connectionSpeed}G");
                Log($"  busy: {s.ServerID} prefabID={s.prefabID} current={s.currentProcessingSpeed} max={s.maxProcessingSpeed} on={s.isOn} activeLinks=[{string.Join(",", speeds)}]");
            }
        }

        public static void Save(string where, SaveData sd)
        {
            if (sd == null) { Log($"[{where}] SaveData=null"); return; }
            var servers = sd.networkData?.servers;
            Log($"[{where}] SaveData name='{sd.nameOfSave}' version={sd.version} servers={Count(servers?.Count)} interactObjects={Count(sd.interactObjectData?.Count)} " +
                $"rackMountObjects={Count(sd.rackMountObjectData?.Count)} modItems={Count(sd.modItemData?.Count)} templates={Count(sd.rackTemplates?.Count)} " +
                $"unlockStates={Count(sd.shopItemUnlockStates?.Count)} repairJobs={Count(sd.repairJobQueue?.Count)} saveComplete={sd.saveComplete}");

            if (servers != null)
            {
                var groups = new SortedDictionary<string, int>();
                int uidNegative = 0, uidZero = 0;
                for (int i = 0; i < servers.Count; i++)
                {
                    var s = servers[i];
                    var key = $"prefabID={s.prefabID} type={s.serverType}";
                    groups[key] = groups.TryGetValue(key, out var n) ? n + 1 : 1;
                    if (s.rackPositionUID < 0) uidNegative++;
                    else if (s.rackPositionUID == 0) uidZero++;
                }
                Log($"   servers by prefab: {string.Join("; ", groups.Select(kv => $"{kv.Value} x {kv.Key}"))}; rackPositionUID<0: {uidNegative}, ==0: {uidZero}");
                for (int i = 0; i < servers.Count && i < 3; i++)
                {
                    var s = servers[i];
                    Log($"   sample serverID={s.serverID} prefabID={s.prefabID} type={s.serverType} rackUID={s.rackPositionUID} customer={s.customerID} " +
                        $"eol={s.eolTime} on={s.isOn} broken={s.isBroken} label='{s.label}' pos={V(s.position)}");
                }
            }

            var cables = sd.networkData?.cables;
            if (cables != null)
            {
                var bySpeed = new SortedDictionary<string, int>();
                for (int i = 0; i < cables.Count; i++)
                {
                    var c = cables[i];
                    var key = $"maxSpeed={c.maxSpeed} {c.startPoint?.type}->{c.endPoint?.type}";
                    bySpeed[key] = bySpeed.TryGetValue(key, out var n) ? n + 1 : 1;
                }
                Log($"   cables={cables.Count}: {string.Join("; ", bySpeed.Select(kv => $"{kv.Value} x {kv.Key}"))}");
            }
            var sfps = sd.networkData?.sfpModules;
            if (sfps != null)
            {
                var byPrefab = new SortedDictionary<string, int>();
                for (int i = 0; i < sfps.Count; i++)
                {
                    var key = $"prefabID={sfps[i].prefabID} inserted={sfps[i].isInserted}";
                    byPrefab[key] = byPrefab.TryGetValue(key, out var n) ? n + 1 : 1;
                }
                Log($"   sfpModules={sfps.Count}: {string.Join("; ", byPrefab.Select(kv => $"{kv.Value} x {kv.Key}"))}");
            }

            var templates = sd.rackTemplates;
            if (templates != null)
                for (int i = 0; i < templates.Count; i++)
                {
                    var t = templates[i];
                    var devices = new List<string>();
                    if (t.devices != null)
                        for (int d = 0; d < t.devices.Count; d++)
                            devices.Add($"kind{t.devices[d].kind}:prefab{t.devices[d].prefabID}:{t.devices[d].sizeInU}U");
                    Log($"   template '{t.displayName}' id={t.templateId} devices=[{string.Join(",", devices)}]");
                }

            var jobs = sd.repairJobQueue;
            if (jobs != null)
                for (int i = 0; i < jobs.Count; i++)
                    Log($"   repairJob server={jobs[i].serverID} switch={jobs[i].switchID}");
        }

        internal static bool IsServer(PlayerManager.ObjectInHand t) =>
            t == PlayerManager.ObjectInHand.Server1U || t == PlayerManager.ObjectInHand.Server2U || t == PlayerManager.ObjectInHand.Server3U;

        internal static string SO(ShopItemSO so) => so == null ? "shopItemSO=null"
            : $"SO '{so.name}' itemName='{so.itemName}' itemID={so.itemID} itemType={so.itemType} price={so.price} xpToUnlock={so.xpToUnlock} eol={so.eol} customColor={so.isCustomColor}";

        internal static string N(UnityEngine.Object o) => o == null ? "null" : $"'{o.name}'";

        static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.GetInstanceID() == b.GetInstanceID();

        static string Count(int? n) => n.HasValue ? n.Value.ToString() : "null";

        static string V(Vector3 v) => $"({v.x:F4},{v.y:F4},{v.z:F4})";

        static string J(Il2CppStructArray<int> a)
        {
            if (a == null) return "null";
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append(a[i]); }
            return sb.ToString();
        }

        static string J(Il2CppStructArray<float> a)
        {
            if (a == null) return "null";
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append(a[i].ToString("F0")); }
            return sb.ToString();
        }

        static string PathOf(Transform t, Transform root)
        {
            var names = new List<string>();
            while (t != null && t != root) { names.Add(t.name); t = t.parent; }
            names.Reverse();
            return names.Count == 0 ? "<root>" : string.Join("/", names);
        }
    }
}
