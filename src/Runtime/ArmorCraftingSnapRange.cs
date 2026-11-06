using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Alta.Crafting;
using HarmonyLib;

namespace PlayerArmorMod
{
    internal static partial class ArmorCrafting
    {
        internal const float IngredientSnapRadius=.65f;
        static readonly MethodInfo nativeSnapRange=AccessTools.PropertyGetter(typeof(GenericCraftingSettings),"MaxDistanceToAnimateSquared");
        static readonly MethodInfo armorSnapRange=AccessTools.Method(typeof(ArmorCrafting),"CraftingSnapDistanceSquared");

        // Keep the native nearest-piece search and callbacks, changing only its
        // threshold for an exact registered armor recipe (including old pages).
        static float CraftingSnapDistanceSquared(GenericCraftingStation station)
        {
            var project=station==null?null:currentProject.GetValue(station) as IProject;
            return project!=null&&OwnsRecipe(project.Recipe)
                ?IngredientSnapRadius*IngredientSnapRadius:GenericCraftingSettings.MaxDistanceToAnimateSquared;
        }
        static IEnumerable<CodeInstruction> ExpandArmorSnapRange(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(nativeSnapRange))
                {
                    var station=new CodeInstruction(OpCodes.Ldarg_0);
                    station.labels.AddRange(instruction.labels);instruction.labels.Clear();
                    station.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();
                    yield return station;
                    instruction.opcode=OpCodes.Call;instruction.operand=armorSnapRange;replaced++;
                }
                yield return instruction;
            }
            if(replaced!=2)throw new InvalidOperationException("Native armor snap-range contract changed: expected two threshold reads, found "+replaced);
        }
    }
}
