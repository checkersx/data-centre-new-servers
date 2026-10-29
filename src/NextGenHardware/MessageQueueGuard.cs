using System;
using Il2Cpp;
using UnityEngine;

namespace NextGenHardware
{
    // The game's message ticker keeps two parallel lists: messageQueue and messageExpireTimes.
    // Some mods (AutoLACP 1.0.0) enqueue messages directly without adding an expiry time, so the
    // lists drift apart. The next time the queue overflows, AddMeesageInField throws inside the
    // route-evaluation coroutine, which then stops for the rest of the session: newly racked servers
    // never connect to their customer until the game is restarted.
    // Re-aligning the lists before the game touches them makes that harmless.
    internal static class MessageQueueGuard
    {
        static bool _logged;

        // Prefix on StaticUIElements.AddMeesageInField and UpdateMessageDisplay.
        public static void Prefix(StaticUIElements __instance)
        {
            try
            {
                var queue = __instance.messageQueue;
                var times = __instance.messageExpireTimes;
                if (queue == null || times == null || queue.Count == times.Count) return;

                int before = times.Count;
                while (times.Count < queue.Count) times.Add(Time.time + StaticUIElements.MESSAGE_DURATION);
                while (times.Count > queue.Count) times.RemoveAt(times.Count - 1);

                if (_logged) return;
                _logged = true;
                NextGenHardwareMod.Log.Warning($"Game message list was out of sync ({queue.Count} messages, {before} timers), " +
                                          "probably from another mod adding messages directly. Repaired so route evaluation keeps running.");
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"MessageQueueGuard: {e.Message}"); }
        }
    }
}
