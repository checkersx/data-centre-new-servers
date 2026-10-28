using System;
using System.IO;
using System.Text;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(NewServers.Probe.ProbeMod), "NewServers.Probe", "0.2.0", "checkersx")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace NewServers.Probe
{
    // Read-only diagnostics for the New Servers mod. Logs vanilla server prefabs, shop cards,
    // racks, customers and the save/load pipeline to UserData\NewServers.Probe\probe_*.log.
    // Changes nothing in the game or the save.
    public class ProbeMod : MelonMod
    {
        const float DumpDelaySeconds = 15f;

        static readonly object Gate = new object();
        static StreamWriter _out;
        static MelonLogger.Instance _console;

        // Set from patches that may run off the main thread; consumed in OnUpdate.
        internal static volatile bool PendingLiveDump;

        float _dumpAt = -1f;

        public override void OnInitializeMelon()
        {
            _console = LoggerInstance;
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "NewServers.Probe");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"probe_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _out = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };
            Log($"NewServers.Probe 0.2.0 writing to {path}", console: true);
            Patches.Install(HarmonyInstance);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Log($"scene initialized: '{sceneName}' ({buildIndex})", console: true);
            _dumpAt = Time.unscaledTime + DumpDelaySeconds;
            Patches.ShopDumped = false;
        }

        public override void OnUpdate()
        {
            if (_dumpAt > 0f && Time.unscaledTime >= _dumpAt)
            {
                _dumpAt = -1f;
                if (MainGameManager.instance == null)
                    Log("no MainGameManager in this scene, skipping dump");
                else
                    Dumps.All("scene loaded + 15s");
            }

            if (PendingLiveDump)
            {
                PendingLiveDump = false;
                Dumps.Live("after save");
            }
        }

        internal static void Log(string message, bool console = false)
        {
            lock (Gate)
                _out?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
            if (console)
                _console?.Msg(message);
        }
    }
}
