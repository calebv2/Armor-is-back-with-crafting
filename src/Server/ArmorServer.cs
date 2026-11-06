using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Character;
using Alta.Inventory;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.PlayerStates;
using ATT.Character.QuickAccessMenu;
using HarmonyLib;
using UnityEngine;
using Stream=Alta.Serialization.Stream;

namespace PlayerArmorMod
{
    internal sealed class ArmorServer : IDisposable
    {
        static ArmorServer current;
        readonly ArmorPrefabRegistry assets;
        readonly HashSet<Player> observed=new HashSet<Player>();
        readonly HashSet<PlayerCharacter> loaded=new HashSet<PlayerCharacter>();
        readonly HashSet<PlayerCharacter> spiritPlayers=new HashSet<PlayerCharacter>();
        readonly Dictionary<Player,Session> clients=new Dictionary<Player,Session>();
        readonly Dictionary<PlayerCharacter,ArmorHarness> harnesses=new Dictionary<PlayerCharacter,ArmorHarness>();
        readonly HashSet<ArmorHarness> watched=new HashSet<ArmorHarness>();
        readonly Dictionary<Player,Dictionary<ArmorSlot,PendingEquip>> pendingEquips=new Dictionary<Player,Dictionary<ArmorSlot,PendingEquip>>();
        static readonly FieldInfo equipmentEffects=AccessTools.Field(typeof(EquipmentState),"remoteDummyEffects");
        static bool missingAnchorLogged;
        readonly string epoch=Guid.NewGuid().ToString("N");
        uint revision;float nextScan,nextPush;bool dirty=true;
        sealed class Session
        {
            internal string Nonce;
            internal bool LegacyWire;
            internal uint SettingsRevision;
            internal float LastSeen,NextRequest,NextEquipWarning,NextSettings;
            internal readonly float[] NextEquip=new float[ArmorCatalog.SlotCount];
        }
        sealed class PendingEquip{internal string Nonce,LastReason;internal ArmorSlot Slot;internal uint PickupId,ControllerId;internal Transform Anchor;internal bool Body;internal float Expires,NextAttempt;}
        internal ArmorServer(ArmorPrefabRegistry registry){assets=registry;current=this;ArmorCrafting.SetServerEpoch(epoch);ArmorSettings.Changed+=ConfigurationChanged;ArmorDurability.ConditionChanged+=ConditionChanged;}
        void ConfigurationChanged(){dirty=true;nextPush=0;}
        void ConditionChanged(ArmorPickup pickup){dirty=true;nextPush=Math.Min(nextPush,Time.unscaledTime+.1f);}
        internal void Install(HarmonyLib.Harmony harmony)
        {
            Player.OnPlayerAdded+=Observe;Player.OnPlayerRemoved+=Removed;
            harmony.Patch(AccessTools.Method(typeof(CommandSync),"SyncCommand",new[]{typeof(IPlayer),typeof(Stream)}),postfix:new HarmonyMethod(typeof(ArmorServer),"CommandPostfix"));
            foreach(var player in Player.AllPlayers.OfType<Player>())Observe(player);
        }
        void Observe(Player player)
        {
            if(player==null||!observed.Add(player))return;
            player.FinishedLoadingController+=ControllerLoaded;
            // At initialization no controllers exist. Existing loaded harnesses
            // may be adopted, but never synthesize a second rig before load ends.
            var character=player.PlayerController as PlayerCharacter;
            if(character!=null&&FindHarness(character)!=null)loaded.Add(character);
        }
        void ControllerLoaded(PlayerController controller)
        {
            var character=controller as PlayerCharacter;if(character==null||character.NetworkPlayer==null)return;
            loaded.Add(character);dirty=true;nextScan=0;
        }
        void Removed(Player player)
        {
            if(player==null)return;player.FinishedLoadingController-=ControllerLoaded;observed.Remove(player);clients.Remove(player);pendingEquips.Remove(player);
            foreach(var character in loaded.Where(c=>c==null||c.NetworkPlayer==player).ToArray())
            {
                loaded.Remove(character);harnesses.Remove(character);spiritPlayers.Remove(character);
            }
            // Native player saves own the Child harness. Never destroy or respawn
            // its inventory as part of a disconnect callback.
            dirty=true;
        }
        internal void Update()
        {
            if(!NetworkSceneManager.IsServer||!assets.Ready)return;
            float now=Time.unscaledTime;
            if(now>=nextScan)
            {
                nextScan=now+1f;
                foreach(var player in Player.AllPlayers.OfType<Player>())Observe(player);
                foreach(var character in loaded.ToArray())
                {
                    if(character==null||character.Entity==null||character.Entity.IsBeingDestroyed||character.NetworkPlayer==null)
                    {
                        loaded.Remove(character);harnesses.Remove(character);spiritPlayers.Remove(character);
                        dirty=true;continue;
                    }
                    ArmorHarness harness;
                    if(!harnesses.TryGetValue(character,out harness)||harness==null)
                    {
                        harness=FindHarness(character);
                        Session supported;
                        bool active=clients.TryGetValue(character.NetworkPlayer,out supported)&&now-supported.LastSeen<=35f;
                        if(harness==null&&active)harness=CreateHarness(character);
                        if(harness==null)continue;harnesses[character]=harness;dirty=true;
                    }
                    harness.SetOwner(character);
                    Session session;
                    harness.SessionActive=clients.TryGetValue(character.NetworkPlayer,out session)&&now-session.LastSeen<=35f;
                    bool inSpirit=character.IsSpirit;
                    bool spiritChanged=inSpirit?spiritPlayers.Add(character):spiritPlayers.Remove(character);
                    if(spiritChanged)
                    {
                        if(inSpirit)pendingEquips.Remove(character.NetworkPlayer);
                        dirty=true;
                        nextPush=0;
                    }
                    if(watched.Add(harness))
                    {
                        harness.EquipmentChanged+=Changed;
                        ArmorWeight.RefreshWearer(character);
                    }
                }
            }
            UpdatePendingEquips(now);
            ArmorProtection.Update(harnesses.Values);
            if((dirty&&now>=nextPush)||now>=nextPush+4f)
            {
                dirty=false;nextPush=now+1f;PushAll();
            }
        }
        static ArmorHarness FindHarness(PlayerCharacter owner)
        {
            if(owner==null||owner.Entity==null)return null;
            ArmorHarness result=null;
            foreach(var child in owner.Entity.Children)
            {
                if(child==null)continue;var found=child.GetComponent<ArmorHarness>();if(found==null)continue;
                if(result!=null){Core.Error("PLAYER_ARMOR_DUPLICATE_HARNESS owner="+owner.Entity.Identifier+"; preserving existing inventories and skipping automatic merge");return result;}
                result=found;
            }
            return result;
        }
        ArmorHarness CreateHarness(PlayerCharacter owner)
        {
            if(assets.Harness==null||owner.Entity.PositionChunk==null)return null;
            try
            {
                var entity=SpawnHelper.Spawn(assets.Harness.Hash,SpawnData.Default,owner.Entity.PositionChunk,owner.transform.position,Quaternion.identity,null);
                if(entity==null)return null;entity.Prefab.SaveMode=ATT.Saving.SaveMode.Child;entity.ChunkingParent=owner.Entity;
                var harness=entity.GetComponent<ArmorHarness>();if(harness==null)throw new InvalidOperationException("Registered harness has no runtime component");
                harness.SetOwner(owner);Core.Log("PLAYER_ARMOR_PLAYER_READY controller="+owner.Entity.Identifier+" harness="+entity.Identifier);
                return harness;
            }
            catch(Exception error){Core.Error("PLAYER_ARMOR_HARNESS_FAILED "+error.GetBaseException().Message);return null;}
        }
        void Changed(ArmorHarness harness)
        {
            dirty=true;nextPush=Math.Min(nextPush,Time.unscaledTime+.1f);
            ArmorWeight.RefreshWearer(harness.Owner);
        }
        internal static float WornWeight(PlayerController player, PlayerWeight stat)
        {
            var character = player as PlayerCharacter;
            if (current == null || character == null || !character.IsAlive || character.IsSpirit ||
                !current.harnesses.TryGetValue(character, out var harness) || harness == null || harness.Owner != character)
                return 0;
            float weight = 0;
            foreach (var dock in harness.Docks)
            {
                if (dock == null || dock.QuantityStored != 1 || !dock.HasDockedPickup || stat.Docks.Contains(dock)) continue;
                var pickup = dock.DockedPickup;
                if (pickup == null || pickup.Dock != dock || pickup.GetComponent<ArmorPickup>() == null) continue;
                if (character.LeftController?.Interactor?.InteractingWith == pickup ||
                    character.RightController?.Interactor?.InteractingWith == pickup) continue;
                weight += pickup.TotalWeight;
            }
            return weight;
        }
        static void CommandPostfix(CommandSync __instance,IPlayer player,Stream stream)
        {
            if(current==null||!NetworkSceneManager.IsServer||stream==null||!stream.IsReading||player==null)return;
            var field=AccessTools.Field(typeof(CommandSync),"commandToSync");var text=field==null?null:field.GetValue(__instance) as string;
            ArmorCommand command;if(!ArmorProtocol.TryParseCommand(text,out command))return;
            var actual=player.PlayerController==null?null:player.PlayerController.NetworkPlayer;
            if(actual==null||!object.ReferenceEquals(actual,player))return;
            current.Handle(actual,command);
        }
        void Handle(Player player,ArmorCommand command)
        {
            if(!assets.Ready||PlayerCommunicationManager.Instance==null)return;
            Session session;float now=Time.unscaledTime;
            if(command.Kind==ArmorCommandKind.Hello)
            {
                if(clients.TryGetValue(player,out session)&&now<session.NextRequest)return;
                bool newSession=session==null||session.Nonce!=command.Nonce;
                if(newSession)pendingEquips.Remove(player);
                bool legacyWire=ArmorClientPrefabSupport.UsesLegacyWire(player.ConnectionToRemotePlayer,assets.Harness,ArmorWorkshopPrefabs.RecipeLectern);
                if(legacyWire&&(newSession||!session.LegacyWire))
                    Core.Log("PLAYER_ARMOR_LEGACY_SYNC controller="+player.PlayerController.Entity?.Identifier+" protocol=24");
                session=new Session
                {
                    Nonce=command.Nonce,
                    LastSeen=now,
                    NextRequest=now+.5f,
                    LegacyWire=legacyWire
                };
                clients[player]=session;nextScan=0;
                Send(player,ArmorProtocol.EncodeAck(command.Nonce,epoch));SendSnapshot(player);return;
            }
            if(!clients.TryGetValue(player,out session))return;
            if(session.Nonce!=command.Nonce)return;
            session.LastSeen=now;
            // Physical releases must not wait behind hello/refresh throttling.
            // Each destination has a bounded independent queue, including both hands.
            if(command.Kind==ArmorCommandKind.Equip||command.Kind==ArmorCommandKind.Wear)
            {
                if(session.Nonce==command.Nonce)QueueEquip(player,session,command,now);
                return;
            }
            if(command.Kind==ArmorCommandKind.Take){TakeFromEquipmentDummy(player,session,command,now);return;}
            if(now<session.NextRequest)return;
            session.NextRequest=now+.5f;
            if(command.Kind==ArmorCommandKind.Refresh)
            {
                if(session.Nonce==command.Nonce){Send(player,ArmorProtocol.EncodeAck(command.Nonce,epoch));SendSnapshot(player);}return;
            }
        }
        void QueueEquip(Player player,Session session,ArmorCommand command,float now)
        {
            if(!ArmorCatalog.IsValidSlot(command.Slot)||command.PickupId==0)return;
            Dictionary<ArmorSlot,PendingEquip> requests;PendingEquip existing;
            if(pendingEquips.TryGetValue(player,out requests)&&requests.TryGetValue(command.Slot,out existing))
            {
                // Repeated network attempts never extend the original deadline.
                if(existing.PickupId==command.PickupId&&existing.Nonce==command.Nonce)return;
                return;
            }
            int index=(int)command.Slot;if(now<session.NextEquip[index])return;
            session.NextEquip[index]=now+.08f;
            PlayerCharacter owner;ArmorHarness harness;Transform anchor;Pickup pickup;string reason;
            bool body=command.Kind==ArmorCommandKind.Wear;
            if(!ResolveSlotEquip(player,command.Slot,command.PickupId,body,out owner,out harness,out anchor,out pickup,out reason))
            {EquipWarning(session,command.Slot,reason,now);return;}
            if(requests==null){requests=new Dictionary<ArmorSlot,PendingEquip>();pendingEquips[player]=requests;}
            var pending=new PendingEquip{Nonce=command.Nonce,Slot=command.Slot,PickupId=command.PickupId,ControllerId=owner.Entity.Identifier,Anchor=anchor,Body=body,Expires=now+1f,NextAttempt=now,LastReason="WaitingForRelease"};
            requests.Add(command.Slot,pending);
            AttemptEquip(player,session,pending,now);
            if(pending.LastReason=="Equipped"){requests.Remove(command.Slot);if(requests.Count==0)pendingEquips.Remove(player);}
        }
        void UpdatePendingEquips(float now)
        {
            foreach(var playerEntry in pendingEquips.ToArray())
            {
                var player=playerEntry.Key;Session session;
                if(player==null||!clients.TryGetValue(player,out session)){pendingEquips.Remove(player);continue;}
                var requests=playerEntry.Value;
                foreach(var entry in requests.ToArray())
                {
                    var pending=entry.Value;
                    if(session.Nonce!=pending.Nonce){requests.Remove(entry.Key);continue;}
                    if(now>pending.Expires){EquipWarning(session,pending.Slot,pending.LastReason,now);requests.Remove(entry.Key);continue;}
                    if(now<pending.NextAttempt)continue;
                    AttemptEquip(player,session,pending,now);
                    if(pending.LastReason=="Equipped"||pending.LastReason=="InvalidContext"||pending.LastReason=="ContextChanged"||pending.LastReason=="Occupied"||pending.LastReason=="NotOwned"||pending.LastReason=="InvalidArmor")
                    {
                        if(pending.LastReason!="Equipped")EquipWarning(session,pending.Slot,pending.LastReason,now);
                        requests.Remove(entry.Key);
                    }
                }
                if(requests.Count==0)pendingEquips.Remove(player);
            }
        }
        void AttemptEquip(Player player,Session session,PendingEquip pending,float now)
        {
            pending.NextAttempt=now+.04f;
            PlayerCharacter owner;ArmorHarness harness;Transform anchor;Pickup pickup;string reason;
            if(!ResolveSlotEquip(player,pending.Slot,pending.PickupId,pending.Body,out owner,out harness,out anchor,out pickup,out reason))
            {pending.LastReason=reason;return;}
            if(owner.Entity.Identifier!=pending.ControllerId||(!pending.Body&&(pending.Anchor==null||anchor!=pending.Anchor)))
            {pending.LastReason="ContextChanged";return;}
            if(pending.Body?harness.TryEquipToBody(pending.Slot,pickup,out reason):harness.TryEquipFromDummy(pending.Slot,pickup,anchor,out reason))
            {pending.LastReason="Equipped";Changed(harness);ShowCondition(player,pickup);return;}
            pending.LastReason=reason;
        }
        bool ResolveSlotEquip(Player player,ArmorSlot slot,uint pickupId,bool body,out PlayerCharacter owner,out ArmorHarness harness,out Transform anchor,out Pickup pickup,out string reason)
        {
            owner=null;harness=null;anchor=null;pickup=null;reason="InvalidContext";
            if(player==null||player.UserInfo==null||!ArmorCatalog.IsValidSlot(slot))return false;
            owner=player.PlayerController as PlayerCharacter;
            if(owner==null||owner.NetworkPlayer!=player||owner.Entity==null||!owner.Entity.IsInitialized||owner.Entity.IsBeingDestroyed||!owner.IsAlive||owner.IsSpirit)return false;
            if(!body&&!TryGetEquipmentAnchor(player,out anchor))return false;
            if(!harnesses.TryGetValue(owner,out harness)||harness==null||harness.Owner!=owner||harness.Entity==null||harness.Entity.IsBeingDestroyed)
            {reason="HarnessNotReady";return false;}
            if(!harness.SessionActive){reason="InvalidContext";return false;}
            var entity=owner.Entity.Scene.GetEntity(pickupId);
            if(entity==null||entity.IsBeingDestroyed||entity.Prefab==null){reason="InvalidArmor";return false;}
            pickup=entity.GetComponent<Pickup>();var marker=entity.GetComponent<ArmorPickup>();int index=(int)slot;
            if(pickup==null||marker==null||marker.Slot!=index||!ArmorCatalog.IsValidStyle(marker.Style))
            {reason="InvalidArmor";return false;}
            var pieces=assets.PiecesFor(marker.Style);
            var items=harness.ItemsFor(marker.Style);
            if(pieces==null||index>=pieces.Length||pieces[index]==null||entity.Prefab.Hash!=pieces[index].Hash
                ||pickup.Item==null||items==null||index>=items.Length||items[index]==null||pickup.Item.Hash!=items[index].Hash)
            {reason="InvalidArmor";return false;}
            if(pickup.LastPlayerInteractor!=owner&&pickup.LastInteractorPlayerId!=player.UserInfo.Identifier){reason="NotOwned";return false;}
            foreach(var hand in pickup.Interactors)
                if(hand==null||hand.Controller==null||hand.Controller.PlayerController!=owner){reason="NotOwned";return false;}
            return true;
        }
        static bool TryGetEquipmentAnchor(Player player,out Transform anchor)
        {
            anchor=null;var state=player.StateManager==null?null:player.StateManager.CurrentState as EquipmentState;
            if(state==null)return false;
            if(equipmentEffects==null)
            {if(!missingAnchorLogged){missingAnchorLogged=true;Core.Error("PLAYER_ARMOR_DUMMY_ANCHOR_UNAVAILABLE native EquipmentState field missing");}return false;}
            var effect=equipmentEffects.GetValue(state) as RemoteEquipmentModeVisuals;
            if(effect==null||!effect.gameObject.activeInHierarchy)return false;anchor=effect.transform;return true;
        }
        void TakeFromEquipmentDummy(Player player,Session session,ArmorCommand command,float now)
        {
            if(!ArmorCatalog.IsValidSlot(command.Slot)||command.PickupId==0)return;
            int slot=(int)command.Slot;if(now<session.NextEquip[slot])return;session.NextEquip[slot]=now+.5f;
            var owner=player.PlayerController as PlayerCharacter;ArmorHarness harness;Transform anchor;
            if(owner==null||owner.NetworkPlayer!=player||owner.Entity==null||owner.Entity.IsBeingDestroyed||!owner.IsAlive||owner.IsSpirit||!TryGetEquipmentAnchor(player,out anchor)
                ||!harnesses.TryGetValue(owner,out harness)||harness==null||harness.Owner!=owner||!harness.SessionActive)return;
            float scale=Mathf.Clamp(owner.transform.lossyScale.y,.5f,2f);
            if(Vector3.Distance(owner.PlayerFeetPosition,anchor.position)>3.5f*scale)return;
            var dock=harness.Docks[slot];var pickup=dock==null?null:dock.DockedPickup;
            if(pickup==null||pickup.Entity==null||pickup.Entity.IsBeingDestroyed||pickup.Entity.Identifier!=command.PickupId||pickup.Dock!=dock||dock.QuantityStored!=1
                ||pickup.IsInteractedWith||pickup.Interactors.Count>0)return;
            // A request is an empty hand deliberately gripping the matching
            // mannequin slot. The native server exposes its feet anchor, so
            // validate the reachable body envelope plus an actual empty hand.
            Controller hand=command.RightHand?owner.RightController:owner.LeftController;
            if(hand==null||hand.PlayerController!=owner||!NearDummyHand(hand,anchor,scale))return;
            // Native PickupDock.CheckForward selects the stored pickup. Pickup.Interact
            // undocks it with this hand, creates the joint and broadcasts its native
            // grab pose. A client that has already let go may release normally.
            if(dock.ForwardTo.Count!=0||pickup.ForwardTo.Any(target=>target!=dock))return;
            bool forwarded=pickup.ForwardTo.Contains(dock);
            try
            {
                var held=hand.Interactor.StartInteract(dock,isFromNetwork:false,isForced:true,isForcingHold:false);
                if(held!=pickup||hand.Interactor.InteractingWith!=pickup||pickup.Dock!=null)return;
            }
            finally
            {
                // CheckForward removes the dock from the pickup before native
                // acceptance; restore that edge if the handoff was declined.
                if(forwarded&&pickup.Dock==dock&&!pickup.ForwardTo.Contains(dock))pickup.ForwardTo.Add(dock);
            }
            ShowCondition(player,pickup);
            dirty=true;Core.Log("PLAYER_ARMOR_REMOVED slot="+command.Slot+" pickup="+command.PickupId+" source=quick-wheel");
        }
        static bool NearDummyHand(Controller hand,Transform anchor,float scale)
        {
            if(hand==null||hand.Interactor==null||hand.Interactor.Controller!=hand||hand.Interactor.InteractingWith!=null)return false;
            var grip=hand.Interactor.InteractionPoint;
            if(grip==null)return false;
            Vector3 p=grip.position-anchor.position;
            return !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z))
                &&p.x*p.x+p.z*p.z<=1.25f*1.25f*scale*scale&&p.y>=-.25f*scale&&p.y<=2.5f*scale;
        }
        static void EquipWarning(Session session,ArmorSlot slot,string reason,float now)
        {
            if(now<session.NextEquipWarning)return;session.NextEquipWarning=now+5f;
            Core.Log("PLAYER_ARMOR_DUMMY_EQUIP_REFUSED slot="+slot+" reason="+reason);
        }
        ArmorSnapshot Snapshot()
        {
            var actors=new List<ArmorActorState>();
            foreach(var entry in harnesses)
                if(entry.Key!=null&&entry.Key.Entity!=null&&entry.Key.NetworkPlayer!=null&&entry.Value!=null&&entry.Value.Entity!=null&&!entry.Value.Entity.IsBeingDestroyed)
                    actors.Add(CaptureArmorState(entry.Key.Entity.Identifier,entry.Value.Entity.Identifier,entry.Value,entry.Key.IsSpirit));
            if(actors.Count>ArmorProtocol.MaxActors)throw new InvalidOperationException("Player armor actor capacity exceeded");
            revision=revision==uint.MaxValue?1:revision+1;
            return new ArmorSnapshot(epoch,revision,actors);
        }
        static ArmorActorState CaptureArmorState(uint controllerId,uint harnessId,ArmorHarness harness,bool isSpirit)
        {
            // Spirit mode hides armor just as it hides the native inventory.
            // Keep the real pickups in their saved docks for exit and reconnect.
            if(isSpirit)return new ArmorActorState(controllerId,harnessId,0);
            return new ArmorActorState(controllerId,harnessId,harness.EquippedMask,harness.LeatherMask,
                Conditions(harness),PickupIds(harness),harness.StyleBits);
        }
        void PushAll()
        {
            if(clients.Count==0)return;
            string wire=ArmorProtocol.EncodeSnapshot(Snapshot());
            foreach(var player in clients.Keys.ToArray())if(player!=null&&player.PlayerController!=null){SendSettings(player);Send(player,wire);}
        }
        static ushort[] Conditions(ArmorHarness harness)
        {
            var values=new ushort[ArmorCatalog.SlotCount];if(harness.Docks==null)return values;
            for(int i=0;i<values.Length;i++)if(harness.Docks[i]!=null&&harness.Docks[i].QuantityStored>0&&harness.Docks[i].HasDockedPickup&&harness.Docks[i].DockedPickup!=null)
                values[i]=(ushort)Math.Round(ArmorDurability.Condition(harness.Docks[i].DockedPickup.GetComponent<ArmorPickup>())*1000);
            return values;
        }
        static void ShowCondition(Player player,Pickup pickup)
        {
            var marker=pickup==null?null:pickup.GetComponent<ArmorPickup>();if(marker==null||PlayerCommunicationManager.Instance==null)return;
            float condition=ArmorDurability.Condition(marker);string name=ArmorCatalog.Get(marker.Style,(ArmorSlot)marker.Slot).PrefabName.Replace("ATT_","").Replace("_"," ");
            PlayerCommunicationManager.Instance.SendMessageToPlayer(player,name+": "+(condition<=0?"Broken — repair needed":Math.Round(condition*100)+"% condition"),4f);
        }
        static uint[] PickupIds(ArmorHarness harness)
        {
            var ids=new uint[ArmorCatalog.SlotCount];if(harness.Docks==null)return ids;
            for(int i=0;i<ids.Length;i++)if(harness.Docks[i]!=null&&harness.Docks[i].QuantityStored>0&&harness.Docks[i].HasDockedPickup&&harness.Docks[i].DockedPickup!=null&&harness.Docks[i].DockedPickup.Entity!=null)
                ids[i]=harness.Docks[i].DockedPickup.Entity.Identifier;
            return ids;
        }
        void SendSettings(Player player)
        {
            Session session;if(!clients.TryGetValue(player,out session)||(session.SettingsRevision==ArmorSettings.Revision&&Time.unscaledTime<session.NextSettings))return;
            foreach(string wire in ArmorSettingsWire.Encode(epoch,ArmorSettings.Revision,ArmorSettings.Serialize()))Send(player,wire);
            session.SettingsRevision=ArmorSettings.Revision;session.NextSettings=Time.unscaledTime+15;
        }
        void SendSnapshot(Player player){SendSettings(player);Send(player,ArmorProtocol.EncodeSnapshot(Snapshot()));}
        void Send(Player player,string wire)
        {
            if(player==null||player.PlayerController==null||PlayerCommunicationManager.Instance==null||!clients.TryGetValue(player,out var session))return;
            if(session.LegacyWire)wire=ArmorClientPrefabSupport.ToLegacyWire(wire);
            PlayerCommunicationManager.Instance.SendMessageToPlayer(player,wire,0f);
        }
        public void Dispose()
        {
            ArmorSettings.Changed-=ConfigurationChanged;ArmorDurability.ConditionChanged-=ConditionChanged;
            Player.OnPlayerAdded-=Observe;Player.OnPlayerRemoved-=Removed;
            foreach(var player in observed)if(player!=null)player.FinishedLoadingController-=ControllerLoaded;
            foreach(var harness in watched)if(harness!=null)harness.EquipmentChanged-=Changed;
            observed.Clear();loaded.Clear();spiritPlayers.Clear();clients.Clear();harnesses.Clear();watched.Clear();pendingEquips.Clear();if(current==this)current=null;
        }
    }
}
