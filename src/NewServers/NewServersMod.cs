using System;
using MelonLoader;

[assembly: MelonInfo(typeof(NewServers.NewServersMod), "New Servers", "0.1.0", "checkersx")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace NewServers
{
    // Adds Gen 2/3/4 versions of every vanilla server: same size and model, more IOPS.
    // Saves stay vanilla on disk (see SaveCompat), so removing the mod never breaks a save.
    public class NewServersMod : MelonMod
    {
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Patching.Install(HarmonyInstance);
        }

        // Covers a new game. A loaded game also gets a call from SaveCompat before servers spawn.
        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => Setup($"scene '{sceneName}' loaded");

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => Setup($"scene '{sceneName}' initialized");

        public override void OnUpdate() => SaveCompat.RestoreIfStuck();

        internal static void Setup(string reason)
        {
            try
            {
                if (Registry.EnsureRegistered(reason))
                    ShopCards.EnsureCards();
            }
            catch (Exception e)
            {
                Log.Error($"Setup failed ({reason}): {e}");
            }
        }
    }
}
