using System;
using System.Linq;
using Alta.Inventory;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorStorage
    {
        // These are Item metadata for native scaled storage, never world or
        // worn transforms. A pouch applies its own additional half scale.
        internal static void ConfigureItem(Item item,Bounds localBounds)
        {
            if(item==null)throw new ArgumentNullException("item");
            var size=localBounds.size;var center=localBounds.center;
            float maximum=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
            if(!Finite(size.x)||!Finite(size.y)||!Finite(size.z)||size.x<=0||size.y<=0||size.z<=0
                ||!Finite(center.x)||!Finite(center.y)||!Finite(center.z))
                throw new InvalidOperationException("Armor storage requires valid local pickup bounds");
            var settings=CommonPrefabs.PouchDockSettings;
            var matches=settings==null?new PickupTag[0]:settings.IncludedTags.Where(tag=>tag!=null&&tag.name=="All").ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Native All storage tag is unavailable or ambiguous");
            // Native generic backpack slots and pouches whitelist this exact
            // ScriptableObject. An empty Tags list is rejected by CheckAllowed.
            if(!item.Tags.Contains(matches[0]))item.Tags.Add(matches[0]);
            float scale=Mathf.Min(.5f,.18f/maximum);
            ArmorNative.Set(item,"storeScale",Vector3.one*scale);
            ArmorNative.Set(item,"scaledPositioning",new Item.DockPositioning(-center*scale,Vector3.zero));
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
