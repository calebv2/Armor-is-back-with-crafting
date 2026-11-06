using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Impact;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorProtection
    {
        static readonly Dictionary<PlayerCharacter,ArmorHarness> harnesses=new Dictionary<PlayerCharacter,ArmorHarness>();
        static readonly Dictionary<float,ArmorDefinition> definitions=new Dictionary<float,ArmorDefinition>();
        static uint definitionRevision;static float nextRefresh;
        sealed class HitState{internal ArmorPickup Piece;internal float HealthBefore,ReducedDamage,Multiplier,WearRate;}
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(HealthObject),"ReceiveDamage",new[]{typeof(float),typeof(float),typeof(DamageData)}),
                prefix:new HarmonyMethod(typeof(ArmorProtection),"ReceivePrefix"),postfix:new HarmonyMethod(typeof(ArmorProtection),"ReceivePostfix"));
        }
        internal static void Update(IEnumerable<ArmorHarness> values)
        {
            ArmorDurabilityRepairShop.Tick();
            if(values!=null&&Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.5f;
            harnesses.Clear();if(values==null)return;
            foreach(var harness in values)if(harness!=null&&harness.Owner!=null&&harness.Owner.Entity!=null&&!harness.Owner.Entity.IsBeingDestroyed)harnesses[harness.Owner]=harness;
        }
        static bool Region(ImpactHit hit,HealthObject health,out ArmorSlot slot)
        {
            slot=ArmorSlot.Head;var detector=hit.ImpactDetector;
            if(detector==null||detector.HealthTarget!=health)return false;
            for(var node=detector.transform;node!=null;node=node.parent)
                if(ArmorProtectionPolicy.TryRegion(node.name,out slot))return true;
            return false;
        }
        static ArmorDefinition Definition(float reduction)
        {
            if(definitionRevision!=ArmorSettings.Revision){ClearDefinitions();definitionRevision=ArmorSettings.Revision;}
            ArmorDefinition value;if(definitions.TryGetValue(reduction,out value))return value;
            if(definitions.Count>=128)ClearDefinitions();
            value=ScriptableObject.CreateInstance<ArmorDefinition>();value.name="Player Armor protection";
            var multipliers=new List<AttackTypeMultiplier>();
            foreach(ImpactType type in Enum.GetValues(typeof(ImpactType)))
            {
                var multiplier=new AttackTypeMultiplier();ArmorNative.Set(multiplier,"type",type);
                ArmorNative.Set(multiplier,"multiplier",ArmorProtectionPolicy.IsProtectedImpact(type)?1f-reduction:1f);multipliers.Add(multiplier);
            }
            ArmorNative.Set(value,"attackTypeMultipliers",multipliers.ToArray());ArmorNative.Set(value,"conductivityMultiplier",1f);definitions.Add(reduction,value);return value;
        }
        static void ReceivePrefix(HealthObject __instance,ref float healthDamage,ref float crippleDamage,DamageData impact,out HitState __state)
        {
            __state=null;
            if(!NetworkSceneManager.IsServer||!ArmorSettings.Current.protectionEnabled||__instance==null||__instance.Health<=0)return;
            var playerHealth=__instance as PlayerHealth;var owner=playerHealth==null?null:playerHealth.Player as PlayerCharacter;
            var hit=impact as ImpactHit;
            if(owner==null||!owner.IsAlive||owner.IsSpirit||hit==null||hit.IsDebugHit||!hit.IsValid||hit.Source!=DamageSource.Impact||!ArmorProtectionPolicy.IsProtectedImpact(hit.ImpactType)
                ||!ArmorDurabilityPolicy.Finite(healthDamage)||!ArmorDurabilityPolicy.Finite(crippleDamage)||healthDamage<=0)return;
            ArmorHarness harness;ArmorSlot slot;
            if(!harnesses.TryGetValue(owner,out harness)||harness==null||!harness.SessionActive||!Region(hit,__instance,out slot)||harness.Docks==null)return;
            int index=(int)slot;if(index>=harness.Docks.Length)return;var dock=harness.Docks[index];
            if(dock==null||dock.QuantityStored!=1||!dock.HasDockedPickup)return;
            var pickup=dock.DockedPickup;var marker=pickup==null?null:pickup.GetComponent<ArmorPickup>();
            if(marker==null||marker.Slot!=index||!ArmorCatalog.IsValidStyle(marker.Style)||pickup.Item==null)return;
            var items=harness.ItemsFor(marker.Style);
            if(items==null||index>=items.Length||items[index]==null||items[index].Hash!=pickup.Item.Hash)return;
            if(pickup.Entity==null||pickup.Entity.Prefab==null||pickup.Entity.Prefab.Hash!=items[index].Hash||pickup.DurabilityModule==null
                ||marker.GetComponent<DurabilityModule>()!=pickup.DurabilityModule)return;
            var balance=ArmorSettings.GetPiece(marker.Style,slot);
            float configured=ArmorWoodPolicy.EffectiveReduction(marker,balance.damageReduction);
            float reduction=ArmorDurabilityPolicy.Reduction(configured,ArmorDurability.Condition(marker),ArmorSettings.Current.durabilityEnabled);
            if(reduction<=0)return;
            // Reuse original ArmorDefinition's per-impact multiplier. Native
            // DetectImpact may already apply other equipment; our definition is
            // deliberately never assigned there, so our contribution occurs once.
            float multiplier=Definition(reduction).GetMultiplier(hit.ImpactType);
            if(!ArmorDurabilityPolicy.Finite(multiplier)||multiplier<=0||multiplier>=1)return;
            healthDamage*=multiplier;crippleDamage*=multiplier;
            __state=new HitState{Piece=marker,HealthBefore=__instance.Health,ReducedDamage=healthDamage,Multiplier=multiplier,WearRate=balance.wearPerAbsorbedDamage};
        }
        static void ReceivePostfix(HealthObject __instance,HitState __state)
        {
            if(__state==null||__state.Piece==null||!NetworkSceneManager.IsServer)return;
            float absorbed=ArmorDurabilityPolicy.Absorbed(__state.ReducedDamage,__state.Multiplier,__state.HealthBefore,__instance.Health);
            if(absorbed>0)ArmorDurability.Wear(__state.Piece,absorbed*__state.WearRate);
        }
        static void ClearDefinitions(){foreach(var definition in definitions.Values)if(definition!=null)UnityEngine.Object.Destroy(definition);definitions.Clear();}
        internal static void Dispose(){harnesses.Clear();ClearDefinitions();definitionRevision=0;nextRefresh=0;}
    }
}
