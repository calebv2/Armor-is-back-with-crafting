using System;
using System.Collections.Generic;
using System.Reflection;
using Alta.Crafting;
using Alta.Pages;

namespace PlayerArmorMod
{
    // PageRenderer owns its cache per instance. Its private static singleton is
    // only the lookup point; passing null to useCounts.GetValue is invalid.
    internal static class ArmorRecipeRenderReferences
    {
        static readonly FieldInfo instance=typeof(PageRenderer).GetField("instance",BindingFlags.Static|BindingFlags.NonPublic)
            ??throw new MissingFieldException(typeof(PageRenderer).FullName,"instance");
        static readonly FieldInfo useCounts=typeof(PageRenderer).GetField("useCounts",BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new MissingFieldException(typeof(PageRenderer).FullName,"useCounts");

        internal static bool TryCollect(ISet<GenericCraftingRecipe> used)
        {
            if(used==null)throw new ArgumentNullException("used");
            try
            {
                // Keep this typed as object: even a retiring Unity singleton can
                // still own managed page references that must not be destroyed.
                object renderer=instance.GetValue(null);
                if(renderer==null)return false;
                var rendering=useCounts.GetValue(renderer) as Dictionary<PageData,int>;
                if(rendering==null)return false;
                foreach(var pair in rendering)
                {
                    if(pair.Value<=0||ReferenceEquals(pair.Key,null))continue;
                    var recipe=pair.Key.GetPageComponent<GenericCraftingRecipe>();
                    if(!ReferenceEquals(recipe,null))used.Add(recipe);
                }
                return true;
            }
            catch(TargetException){return false;}
            catch(ArgumentException){return false;}
            catch(MemberAccessException){return false;}
        }
    }
}
