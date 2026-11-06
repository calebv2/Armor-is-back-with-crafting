using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Character;
using Alta.Networking;
using ATT.Character.QuickAccessMenu;
using HarmonyLib;
using UnityEngine;
using Stream=Alta.Serialization.Stream;

namespace PlayerArmorMod
{
    internal sealed class ArmorClient : IDisposable
    {
        static ArmorClient current;
        readonly ArmorPrefabRegistry assets;
        readonly Dictionary<uint,ArmorActorState> state=new Dictionary<uint,ArmorActorState>();
        readonly Dictionary<uint,PlayerArmorRigBinding> views=new Dictionary<uint,PlayerArmorRigBinding>();
        readonly HashSet<string> presentationWarnings=new HashSet<string>(StringComparer.Ordinal);
        object server,localPlayer;
        string nonce,epoch;
        uint revision;
        float nextHello,nextRefresh,nextResolve,retry=1;
        bool confirmed,helloSent;
        readonly ArmorConnectionGuide indicator;
        string guideStatus;
        float nextGuideLog;
        readonly ArmorDummyInteraction dummyInteraction;
        readonly ArmorDummyMeshSelection dummyMesh=new ArmorDummyMeshSelection();
        readonly ArmorSettingsAssembly settingsAssembly=new ArmorSettingsAssembly();
        internal ArmorClient(ArmorPrefabRegistry registry){assets=registry;dummyInteraction=new ArmorDummyInteraction(Send,DummySurfaceDistance);indicator=new ArmorConnectionGuide(Core.Error);current=this;}
        float? DummySurfaceDistance(Transform root,int slot,uint pickup,Vector3 hand)
        {
            var player=PlayerController.Current;
            PlayerArmorRigBinding binding;
            if(player==null||player.Entity==null||!views.TryGetValue(player.Entity.Identifier,out binding))return null;
            return dummyMesh.Distance(binding.GetDummyRenderers(root,slot,pickup),hand);
        }
        internal void Install(HarmonyLib.Harmony harmony)
        {
            ArmorFabricMeshPacking.Install(harmony);
            ArmorHandInteraction.Install(harmony,Core.Log);
            Core.Log("PLAYER_ARMOR_HAND_GUARD_READY armor targets filtered before native hand overlap collection");
            harmony.Patch(AccessTools.Method(typeof(PlayerTextMessage),"Serialize"),postfix:new HarmonyMethod(typeof(ArmorClient),"MessagePostfix"));
            harmony.Patch(AccessTools.Method(typeof(PlayerMessageDisplay),"Display",new[]{typeof(string),typeof(float),typeof(DisplayMessageType)}),prefix:new HarmonyMethod(typeof(ArmorClient),"DisplayPrefix"));
        }
        internal void Update()
        {
            object nowServer=JoinedServerInfo.Current,nowPlayer=Player.Current;
            if(!object.ReferenceEquals(server,nowServer)||!object.ReferenceEquals(localPlayer,nowPlayer))
            {
                Clear(server!=null||localPlayer!=null);server=nowServer;localPlayer=nowPlayer;nonce=Guid.NewGuid().ToString("N");
                epoch=null;revision=0;confirmed=false;helloSent=false;nextHello=nextRefresh=nextResolve=0;retry=1;
            }
            if(server==null||localPlayer==null||!assets.Ready)return;
            float now=Time.unscaledTime;
            if(!confirmed&&now>=nextHello)
            {
                if(Send(ArmorProtocol.EncodeHello(nonce)))
                {
                    if(!helloSent){Core.Log("PLAYER_ARMOR_HELLO_SENT native authenticated message channel");helloSent=true;}
                    retry=Math.Min(retry*2,15);
                }
                nextHello=now+retry;
            }
            if(confirmed&&now>=nextRefresh){Send(ArmorProtocol.EncodeRefresh(nonce));nextRefresh=now+10;}
            if(confirmed&&now>=nextResolve){nextResolve=now+.25f;Resolve();}
        }
        internal void LateUpdate()
        {
            if(!confirmed||NetworkSceneManager.Current==null){dummyInteraction.Reset();HideIndicator();return;}
            foreach(var entry in views)
            {
                ArmorActorState actor;if(!state.TryGetValue(entry.Key,out actor))continue;
                var entity=NetworkSceneManager.Current.GetEntity(actor.ControllerId);
                UpdateView(entry.Value,entity==null?null:entity.GetComponent<PlayerCharacter>(),actor);
            }
            var owner=PlayerController.Current as PlayerCharacter;
            ArmorActorState localState;
            if(owner!=null&&owner.Entity!=null&&state.TryGetValue(owner.Entity.Identifier,out localState))
            {
                var entity=NetworkSceneManager.Current.GetEntity(localState.HarnessId);
                dummyInteraction.SetWornItems(entity==null?null:entity.GetComponent<ArmorHarness>());
                dummyInteraction.Update(owner,localState.EquippedMask,nonce,Time.unscaledTime);
            }
            else dummyInteraction.Reset();
            UpdateIndicator();
        }
        void Resolve()
        {
            var scene=NetworkSceneManager.Current;if(scene==null||assets.VisualTemplate==null)return;
            foreach(var actor in state.Values)
            {
                var controllerEntity=scene.GetEntity(actor.ControllerId);var harnessEntity=scene.GetEntity(actor.HarnessId);
                var character=controllerEntity==null?null:controllerEntity.GetComponent<PlayerCharacter>();
                var harness=harnessEntity==null?null:harnessEntity.GetComponent<ArmorHarness>();
                if(character==null||character.NetworkPlayer==null||harness==null)continue;
                harness.SetOwnerEntityId(actor.ControllerId);
                ApplyConditions(harness,actor);
                ArmorHandInteraction.UpdateHarness(harness);
                PlayerArmorRigBinding binding;
                if(!views.TryGetValue(actor.ControllerId,out binding))views[actor.ControllerId]=binding=new PlayerArmorRigBinding(assets.VisualTemplate,Core.Log,assets.WyrmTemplate);
                UpdateView(binding,character,actor);
            }
        }
        void UpdateView(PlayerArmorRigBinding binding,PlayerCharacter character,ArmorActorState actor)
        {
            // A presentation failure must not prevent the independent native
            // equipment-dummy interaction path from returning a saved item.
            try{binding.Update(character,actor.EquippedMask,actor.LeatherMask,actor.PickupIds,actor.StyleBits);}
            catch(Exception error)
            {
                string reason=error.GetBaseException().Message;
                if(presentationWarnings.Count<32&&presentationWarnings.Add(reason))
                    Core.Error("PLAYER_ARMOR_PRESENTATION_FAILED controller="+actor.ControllerId+" reason="+reason);
            }
        }
        void Receive(string message)
        {
            ArmorSettingsChunk chunk;
            if(confirmed&&ArmorSettingsWire.TryParse(message,out chunk))
            {
                if(!object.ReferenceEquals(server,JoinedServerInfo.Current)||!object.ReferenceEquals(localPlayer,Player.Current))return;
                string json;if(settingsAssembly.Accept(chunk,epoch,out json))
                    try{ArmorSettings.ApplyRemote(json);Core.Log("PLAYER_ARMOR_SERVER_SETTINGS_READY revision="+chunk.Revision);}
                    catch(Exception error){settingsAssembly.Clear();Core.Error("PLAYER_ARMOR_REMOTE_SETTINGS_REJECTED "+error.GetBaseException().Message);}
                return;
            }
            ArmorAck ack;
            if(ArmorProtocol.TryParseAck(message,out ack))
            {
                if(server==null||localPlayer==null||nonce==null||ack.Nonce!=nonce)return;
                if(!object.ReferenceEquals(server,JoinedServerInfo.Current)||!object.ReferenceEquals(localPlayer,Player.Current))return;
                if(epoch!=ack.Epoch){if(epoch!=null)Clear(false);epoch=ack.Epoch;revision=0;}
                bool first=!confirmed;confirmed=true;nextRefresh=Time.unscaledTime+10;
                if(first)Core.Log("PLAYER_ARMOR_SERVER_READY original equipment slots and native quick wheel active");return;
            }
            ArmorSnapshot snapshot;
            if(!confirmed||!ArmorProtocol.TryParseSnapshot(message,out snapshot)||snapshot.Epoch!=epoch||snapshot.Revision<=revision)return;
            if(!object.ReferenceEquals(server,JoinedServerInfo.Current)||!object.ReferenceEquals(localPlayer,Player.Current))return;
            revision=snapshot.Revision;
            var ids=new HashSet<uint>(snapshot.Actors.Select(a=>a.ControllerId));
            foreach(var id in views.Keys.Where(id=>!ids.Contains(id)).ToArray()){views[id].Dispose();views.Remove(id);}
            state.Clear();foreach(var actor in snapshot.Actors)state.Add(actor.ControllerId,actor);
            nextResolve=0;
        }
        void ApplyConditions(ArmorHarness harness,ArmorActorState actor)
        {
            if(harness.Docks==null)return;
            for(int i=0;i<ArmorCatalog.SlotCount;i++)
            {
                var dock=harness.Docks[i];if(dock==null||!dock.HasDockedPickup||dock.DockedPickup==null||(actor.EquippedMask&(1<<i))==0)continue;
                var marker=dock.DockedPickup.GetComponent<ArmorPickup>();if(marker==null||marker.Entity==null||actor.PickupIds[i]==0||marker.Entity.Identifier!=actor.PickupIds[i]||marker.Slot!=i||marker.Style!=ArmorCatalog.StyleAt(actor.StyleBits,(ArmorSlot)i))continue;
                ArmorDurability.ApplyClientCondition(marker,actor.Durability[i]/1000f);
            }
        }
        void UpdateIndicator()
        {
            var owner=PlayerController.Current as PlayerCharacter;
            if(owner==null||owner.Entity==null){HideIndicator();return;}
            Transform point=dummyInteraction.GuideTarget;
            bool dummy=dummyInteraction.GuideIsDummy;
            if(point==null){HideIndicator();return;}
            ArmorPickup held=Held(owner.LeftController)??Held(owner.RightController);
            float distance=dummyInteraction.GuideDistance;
            bool ready=dummyInteraction.GuideReady;
            int slot=dummyInteraction.GuideSlot;
            GuideDiagnostic((dummy?"dummy_":"body_")+(ready?"ready":distance<=ArmorDummyInteraction.CaptureRadius?"occupied":"out_of_range"),slot,distance);
            // The native socket is inside the body. Bring only the visual cue
            // toward the viewer; eligibility still uses the original bone.
            Vector3 center=point.position;
            if(owner.Head!=null)
            {
                Vector3 towardViewer=owner.Head.position-center;
                if(towardViewer.sqrMagnitude>.0001f)center+=towardViewer.normalized*Mathf.Min(.12f,towardViewer.magnitude*.5f);
            }
            indicator.Show(assets.VisualTemplate,center,dummy?dummyInteraction.GuideRight:owner.transform.right,ready,distance,Time.unscaledTime);
        }
        static ArmorPickup Held(Controller hand)
        {
            if(hand==null||hand.Interactor==null)return null;
            var pickup=hand.Interactor.InteractingWith as Pickup;return pickup==null?null:pickup.GetComponent<ArmorPickup>();
        }
        void HideIndicator(){indicator.Hide();}
        void GuideDiagnostic(string status,int slot,float distance)
        {
            if(status==guideStatus||Time.unscaledTime<nextGuideLog)return;
            guideStatus=status;nextGuideLog=Time.unscaledTime+5;
            Core.Log("PLAYER_ARMOR_GUIDE status="+status+" slot="+slot+" distanceCm="+(distance*100).ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
        }
        static bool Send(string message)
        {
            return ArmorClientCommandBridge.Send(message);
        }
        static void MessagePostfix(PlayerTextMessage __instance,Stream stream)
        {
            if(current!=null&&stream!=null&&stream.IsReading&&__instance.Message!=null&&__instance.Message.StartsWith(ArmorProtocol.Prefix,StringComparison.Ordinal))current.Receive(__instance.Message);
        }
        static bool DisplayPrefix(ref string message)
        {
            if(message==null||!message.StartsWith(ArmorProtocol.Prefix,StringComparison.Ordinal))return true;
            if(current!=null)current.Receive(message);return false;
        }
        void Clear(bool resetSettings=true)
        {
            if(epoch!=null)ArmorCrafting.ResetClientSession(epoch);
            settingsAssembly.Clear();if(resetSettings)ArmorSettings.ResetRemote();
            foreach(var view in views.Values)view.Dispose();views.Clear();state.Clear();dummyInteraction.Reset();HideIndicator();guideStatus=null;nextGuideLog=0;ArmorHandInteraction.ResetDiagnostics();
            dummyMesh.Clear();ArmorMaterialPalette.Clear();
            presentationWarnings.Clear();
        }
        public void Dispose()
        {
            Clear();ArmorFabricMeshPacking.Dispose();indicator.Dispose();ArmorHandInteraction.Dispose();if(current==this)current=null;
        }
    }
}
