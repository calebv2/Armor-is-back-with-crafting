using MelonLoader;
using UnityEngine;

namespace PlayerArmorMod
{
    public sealed class Core : MelonMod
    {
        internal static Core Instance;
        internal ArmorPrefabRegistry Assets;
#if SERVER
        ArmorServer server;
#else
        ArmorClient client;
#endif
        public override void OnInitializeMelon()
        {
#if SERVER
            if(!Application.isBatchMode)return;
#else
            if(Application.isBatchMode)return;
#endif
            Instance=this;
#if SERVER
            ArmorSettings.InitializeServer(MelonLoader.Utils.MelonEnvironment.UserDataDirectory);
#endif
            ArmorDurability.Install(HarmonyInstance);ArmorCraftRecord.Install(HarmonyInstance);ArmorCrafting.Install(HarmonyInstance);
            ArmorWeight.Install(HarmonyInstance);
            Assets=new ArmorPrefabRegistry(this);Assets.Subscribe();Assets.Preload();
            ArmorSurfaceGrip.Install(HarmonyInstance);
#if SERVER
            MushroomChestLoot.Install(HarmonyInstance, Assets);
            WyrmArmorLoot.Install(HarmonyInstance, Assets);
            ArmorProtection.Install(HarmonyInstance);
            server=new ArmorServer(Assets);server.Install(HarmonyInstance);
#else
            client=new ArmorClient(Assets);client.Install(HarmonyInstance);
#endif
            Log("Player Armor 0.9.4 initialized. Armor recipes use the dedicated Armor Workshop.");
        }
        public override void OnLateInitializeMelon(){if(Assets!=null)Assets.Preload();}
        public override void OnUpdate()
        {
            ArmorCrafting.Update();
#if SERVER
            ArmorSettings.Poll(Time.unscaledTime);if(server!=null)server.Update();
#else
            if(client!=null)client.Update();
#endif
        }
        public override void OnLateUpdate()
        {
#if CLIENT
            if(client!=null)client.LateUpdate();
#endif
        }
        public override void OnDeinitializeMelon()
        {
#if SERVER
            MushroomChestLoot.Dispose();
            WyrmArmorLoot.Dispose();
            if(server!=null)server.Dispose();ArmorProtection.Dispose();
#else
            if(client!=null)client.Dispose();ArmorStereo.Dispose();
#endif
            ArmorCrafting.Dispose();ArmorCraftRecord.Dispose();ArmorDurability.Dispose();Instance=null;
            ArmorSurfaceGrip.Dispose();
        }
        internal static void Log(string value){if(Instance!=null)Instance.LoggerInstance.Msg(value);}
        internal static void Error(string value){if(Instance!=null)Instance.LoggerInstance.Error(value);}
    }
}
