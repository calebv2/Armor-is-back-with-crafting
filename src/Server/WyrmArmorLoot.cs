using System;
using System.Globalization;
using System.Reflection;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class WyrmArmorLoot
    {
        internal const int KillsPerDropOnAverage = 20;
        static readonly System.Random random = new System.Random();
        static readonly MethodInfo death = AccessTools.DeclaredMethod(typeof(Wyrm), "Killed", new[] { typeof(HealthObject) });
        static readonly MethodInfo before = AccessTools.Method(typeof(WyrmArmorLoot), nameof(BeforeDeath));
        static readonly MethodInfo after = AccessTools.Method(typeof(WyrmArmorLoot), nameof(AfterDeath));
        static HarmonyLib.Harmony patchOwner;
        static ArmorPrefabRegistry assets;

        internal static void Install(HarmonyLib.Harmony harmony, ArmorPrefabRegistry registry)
        {
            try
            {
                harmony.Patch(death, prefix: new HarmonyMethod(before), postfix: new HarmonyMethod(after));
                patchOwner = harmony;
                assets = registry;
                string chance = (100d / KillsPerDropOnAverage).ToString("0.##", CultureInfo.InvariantCulture);
                Core.Log("PLAYER_ARMOR_WYRM_LOOT_READY chance=" + chance + "%");
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_WYRM_LOOT_UNAVAILABLE " + error);
            }
        }

        internal static void Dispose()
        {
            assets = null;
            if (patchOwner == null) return;
            patchOwner.Unpatch(death, before);
            patchOwner.Unpatch(death, after);
            patchOwner = null;
        }

        // The original Target.Killed flips IsAlive synchronously. Later
        // death notifications from the same entity cannot roll again.
        static void BeforeDeath(Wyrm __instance, out bool __state)
        {
            __state = NetworkSceneManager.IsServer && assets != null && assets.Ready && __instance.IsAlive;
        }

        static void AfterDeath(Wyrm __instance, bool __state)
        {
            if (!__state || __instance.IsAlive) return;
            var entity = __instance.Entity;
            if (entity == null || entity.Chunk == null || entity.IsBeingDestroyed) return;
            int slot = RollSlot(random);
            if (slot < 0) return;
            try
            {
                var position = __instance.transform.position + Vector3.up * .35f;
                var prefab = assets.WyrmPieces[slot];
                var item = SpawnHelper.Spawn(prefab, SpawnData.DontGenerate, entity.Chunk, position, Quaternion.identity);
                if (item != null) Core.Log("PLAYER_ARMOR_WYRM_LOOT wyrm=" + entity.Identifier + " piece=" + prefab.name);
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_WYRM_LOOT_FAILED " + error);
            }
        }

        internal static int RollSlot(System.Random rng)
        {
            return rng.Next(KillsPerDropOnAverage) == 0 ? rng.Next(ArmorCatalog.SlotCount) : -1;
        }
    }
}
