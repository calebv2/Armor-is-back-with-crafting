using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Crafting;
using Alta.Networking;
using HarmonyLib;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        static readonly HashSet<GenericCraftingStation> pendingWorkshopRefunds = new HashSet<GenericCraftingStation>();
        static readonly MethodInfo refundProject = AccessTools.Method(typeof(GenericCraftingStation), "ClearProject");

        static void InstallWorkshopMigration(HarmonyLib.Harmony harmony)
        {
            var restore = typeof(GenericCraftingStation).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name.EndsWith(".Load", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType.Name == "GenericCraftingStationData");
            harmony.Patch(restore, postfix: new HarmonyMethod(typeof(ArmorCrafting), nameof(QueueWorkshopRefund)));
        }

        static void QueueWorkshopRefund(GenericCraftingStation __instance)
        {
            if (!NetworkSceneManager.IsServer || !ArmorWorkshopPrefabs.Ready || ArmorWorkshopPrefabs.IsBench(__instance)) return;
            var project = currentProject.GetValue(__instance) as IProject;
            if (project != null && OwnsRecipe(project.Recipe)) pendingWorkshopRefunds.Add(__instance);
        }

        static void UpdateWorkshopRefunds()
        {
            if (pendingWorkshopRefunds.Count == 0) return;
            foreach (var station in pendingWorkshopRefunds.ToArray())
            {
                if (station == null || station.Entity == null || station.Entity.IsBeingDestroyed)
                {
                    pendingWorkshopRefunds.Remove(station);
                    continue;
                }
                if (station.Entity.Chunk == null || !station.Entity.IsInitialized) continue;
                pendingWorkshopRefunds.Remove(station);
                var project = currentProject.GetValue(station) as IProject;
                if (project == null || !OwnsRecipe(project.Recipe)) continue;
                try
                {
                    refundProject.Invoke(station, new object[] { true });
                    Core.Log("PLAYER_ARMOR_WORKSHOP_LEGACY_PROJECT_REFUNDED bench=" + station.Entity.Identifier);
                }
                catch (Exception error)
                {
                    Core.Error("PLAYER_ARMOR_WORKSHOP_LEGACY_REFUND_FAILED bench=" + station.Entity.Identifier + " " + error.GetBaseException());
                }
            }
        }
    }
}
