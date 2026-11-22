using System;
using System.Reflection;
using Alta.Chunks;
using Alta.Intelligence;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class PhantomArmorLoot
    {
        const int DropChanceDenominator = 100;
        static readonly System.Random random = new System.Random();
        static readonly MethodInfo death = AccessTools.DeclaredMethod(typeof(Phantom), "Killed", new[] { typeof(HealthObject) });
        static readonly MethodInfo before = AccessTools.Method(typeof(PhantomArmorLoot), nameof(BeforeDeath));
        static readonly MethodInfo after = AccessTools.Method(typeof(PhantomArmorLoot), nameof(AfterDeath));
        static HarmonyLib.Harmony owner;
        static ArmorPrefabRegistry assets;

        struct DeathLocation
        {
            internal Chunk Chunk;
            internal Vector3 Position;
        }

        internal static void Install(HarmonyLib.Harmony harmony, ArmorPrefabRegistry registry)
        {
            try
            {
                harmony.Patch(death, prefix: new HarmonyMethod(before), postfix: new HarmonyMethod(after));
                owner = harmony;
                assets = registry;
                Core.Log("PLAYER_ARMOR_PHANTOM_LOOT_READY odds=1/" + DropChanceDenominator);
            }
            catch (Exception error) { Core.Error("PLAYER_ARMOR_PHANTOM_LOOT_UNAVAILABLE " + error); }
        }

        internal static void Dispose()
        {
            assets = null;
            if (owner == null) return;
            owner.Unpatch(death, before);
            owner.Unpatch(death, after);
            owner = null;
        }

        static void BeforeDeath(Phantom __instance, HealthObject __0, out DeathLocation __state)
        {
            __state = default(DeathLocation);
            // The native daylight and failed-action fades both call Killed(null).
            if (__0 == null || !NetworkSceneManager.IsServer || assets == null || !assets.Ready || !__instance.IsAlive) return;
            var healthEntity = __0.Entity;
            if (healthEntity == null) return;
            var root = healthEntity.PrefabRoot ?? healthEntity;
            __state = new DeathLocation { Chunk = root.Chunk, Position = __instance.transform.position + Vector3.up * .35f };
        }

        static void AfterDeath(Phantom __instance, DeathLocation __state)
        {
            if (__state.Chunk == null || __instance.IsAlive || random.Next(DropChanceDenominator) != 0) return;
            var piece = assets.PhantomPieces[random.Next(ArmorCatalog.SlotCount)];
            try
            {
                var item = SpawnHelper.Spawn(piece, SpawnData.DontGenerate, __state.Chunk, __state.Position, Quaternion.identity);
                if (item != null) Core.Log("PLAYER_ARMOR_PHANTOM_LOOT piece=" + piece.name);
                else Core.Error("PLAYER_ARMOR_PHANTOM_LOOT_FAILED spawn returned null for " + piece.name);
            }
            catch (Exception error) { Core.Error("PLAYER_ARMOR_PHANTOM_LOOT_FAILED " + error); }
        }
    }
}
