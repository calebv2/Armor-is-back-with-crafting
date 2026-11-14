using System;
using System.Reflection;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class SpriggullHelmetLoot
    {
        const int DropChanceDenominator = 100;
        static readonly System.Random random = new System.Random();
        static readonly MethodInfo death =
            AccessTools.DeclaredMethod(typeof(Spriggull), "Killed", new[] { typeof(HealthObject) });
        static readonly MethodInfo before = AccessTools.Method(typeof(SpriggullHelmetLoot), nameof(BeforeDeath));
        static readonly MethodInfo after = AccessTools.Method(typeof(SpriggullHelmetLoot), nameof(AfterDeath));
        static HarmonyLib.Harmony patchOwner;
        static ArmorPrefabRegistry assets;

        internal static void Install(HarmonyLib.Harmony harmony, ArmorPrefabRegistry registry)
        {
            try
            {
                harmony.Patch(death, prefix: new HarmonyMethod(before), postfix: new HarmonyMethod(after));
                patchOwner = harmony;
                assets = registry;
                Core.Log("PLAYER_ARMOR_SPRIGGULL_LOOT_READY chance=1%");
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_SPRIGGULL_LOOT_UNAVAILABLE " + error);
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

        static void BeforeDeath(Spriggull __instance, out NetworkPrefab __state)
        {
            __state = null;
            // Only the transition from alive to dead may roll.
            if (!NetworkSceneManager.IsServer || assets == null || !assets.Ready || !__instance.IsAlive) return;
            __state = assets.StandaloneHelmets[ArmorCatalog.HelmetIndex(ArmorStyle.SpriggullFullHelmet)];
        }

        static void AfterDeath(Spriggull __instance, NetworkPrefab __state)
        {
            if (__state == null || __instance.IsAlive) return;
            var entity = __instance.Entity;
            if (entity == null || entity.Chunk == null || entity.IsBeingDestroyed || random.Next(DropChanceDenominator) != 0) return;

            try
            {
                var position = __instance.transform.position + Vector3.up * .35f;
                var item = SpawnHelper.Spawn(__state, SpawnData.DontGenerate, entity.Chunk, position, Quaternion.identity);
                if (item != null) Core.Log("PLAYER_ARMOR_SPRIGGULL_LOOT spriggull=" + entity.Identifier + " piece=" + __state.name);
            }
            catch (Exception error)
            {
                Core.Error("PLAYER_ARMOR_SPRIGGULL_LOOT_FAILED " + error);
            }
        }
    }
}
