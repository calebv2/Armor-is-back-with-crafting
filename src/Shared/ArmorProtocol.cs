using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace PlayerArmorMod
{
    public enum ArmorCommandKind {Hello,Refresh,Equip,Wear,Take}

    public sealed class ArmorCommand
    {
        public readonly ArmorCommandKind Kind;
        public readonly string Nonce;
        public readonly ArmorSlot Slot;
        public readonly uint PickupId;
        public readonly bool RightHand;
        internal ArmorCommand(ArmorCommandKind kind,string nonce,ArmorSlot slot,uint pickupId=0,bool rightHand=false)
        {Kind=kind;Nonce=nonce;Slot=slot;PickupId=pickupId;RightHand=rightHand;}
    }

    public sealed class ArmorAck
    {
        public readonly string Nonce;
        public readonly string Epoch;
        public ArmorAck(string nonce,string epoch)
        {
            Nonce=ArmorProtocol.NormalizeToken(nonce,"nonce");
            Epoch=ArmorProtocol.NormalizeToken(epoch,"epoch");
        }
    }

    public sealed class ArmorActorState
    {
        public readonly uint ControllerId;
        public readonly uint HarnessId;
        public readonly byte EquippedMask;
        public readonly byte LeatherMask;
        public readonly uint StyleBits;
        public readonly ReadOnlyCollection<ushort> Durability;
        public readonly ReadOnlyCollection<uint> PickupIds;
        public ArmorActorState(uint controllerId,uint harnessId,byte equippedMask,byte leatherMask=0,IEnumerable<ushort> durability=null,IEnumerable<uint> pickupIds=null,uint? styleBits=null)
        {
            if(controllerId==0)throw new ArgumentOutOfRangeException("controllerId");
            if(harnessId==0)throw new ArgumentOutOfRangeException("harnessId");
            if(controllerId==harnessId)throw new ArgumentException("Controller and harness must be different entities.","harnessId");
            if(equippedMask>ArmorCatalog.AllEquippedMask)throw new ArgumentOutOfRangeException("equippedMask");
            if(leatherMask>ArmorCatalog.AllEquippedMask||(leatherMask&~equippedMask)!=0)throw new ArgumentOutOfRangeException("leatherMask");
            uint styles=styleBits??ArmorCatalog.PackLegacyLeather(leatherMask);
            if(!ArmorCatalog.ValidStyles(equippedMask,styles)||ArmorCatalog.LeatherMaskOf(styles)!=leatherMask)throw new ArgumentOutOfRangeException("styleBits");
            ControllerId=controllerId;HarnessId=harnessId;EquippedMask=equippedMask;LeatherMask=leatherMask;StyleBits=styles;
            var values=durability==null?new List<ushort>():new List<ushort>(durability);
            if(durability==null)for(int i=0;i<ArmorCatalog.SlotCount;i++)values.Add((ushort)((equippedMask&(1<<i))!=0?1000:0));
            if(values.Count!=ArmorCatalog.SlotCount)throw new ArgumentException("Expected seven condition values","durability");
            for(int i=0;i<values.Count;i++)if(values[i]>1000||((equippedMask&(1<<i))==0&&values[i]!=0))throw new ArgumentException("Invalid armor condition","durability");
            Durability=values.AsReadOnly();
            var ids=pickupIds==null?new List<uint>(new uint[ArmorCatalog.SlotCount]):new List<uint>(pickupIds);
            if(ids.Count!=ArmorCatalog.SlotCount)throw new ArgumentException("Expected seven pickup identities","pickupIds");
            var unique=new HashSet<uint>();
            for(int i=0;i<ids.Count;i++)if((ids[i]!=0&&((equippedMask&(1<<i))==0||ids[i]==controllerId||ids[i]==harnessId||!unique.Add(ids[i]))))throw new ArgumentException("Invalid armor pickup identity","pickupIds");
            PickupIds=ids.AsReadOnly();
        }
    }

    // A complete immutable replacement, never a partial patch. Account identifiers
    // do not appear on the wire; the server owns identity and equipment validation.
    public sealed class ArmorSnapshot
    {
        public readonly string Epoch;
        public readonly uint Revision;
        public readonly ReadOnlyCollection<ArmorActorState> Actors;
        public ArmorSnapshot(string epoch,uint revision,IEnumerable<ArmorActorState> actors)
        {
            Epoch=ArmorProtocol.NormalizeToken(epoch,"epoch");
            if(revision==0)throw new ArgumentOutOfRangeException("revision");
            if(actors==null)throw new ArgumentNullException("actors");
            var copy=new List<ArmorActorState>();
            var entities=new HashSet<uint>();
            foreach(var actor in actors)
            {
                if(actor==null)throw new ArgumentException("A snapshot contains a missing actor.","actors");
                if(copy.Count>=ArmorProtocol.MaxActors)throw new ArgumentException("Too many actors.","actors");
                if(!entities.Add(actor.ControllerId)||!entities.Add(actor.HarnessId))
                    throw new ArgumentException("Duplicate controller or harness identity.","actors");
                copy.Add(actor);
            }
            Revision=revision;Actors=copy.AsReadOnly();
        }
    }

    public static class ArmorProtocol
    {
        public const string Prefix="[PLAYERARMOR25]";
        public const string CommandName="playerarmor";
        public const int MaxActors=128;
        public const int MaxWireLength=24576;
        public const int MaxCommandLength=160;

        public static bool IsToken(string value)
        {
            if(value==null||value.Length!=32)return false;
            for(int i=0;i<value.Length;i++)
            {
                char c=value[i];
                if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;
            }
            return true;
        }
        internal static string NormalizeToken(string value,string parameter)
        {
            if(!IsToken(value))throw new ArgumentException("Expected exactly 32 hexadecimal characters.",parameter);
            return value.ToLowerInvariant();
        }
        public static string EncodeHello(string nonce){return CommandName+" hello23 "+NormalizeToken(nonce,"nonce");}
        public static string EncodeRefresh(string nonce){return CommandName+" refresh "+NormalizeToken(nonce,"nonce");}
        public static string EncodeEquip(string nonce,ArmorSlot slot,uint pickupId)
        {
            string token=NormalizeToken(nonce,"nonce");
            if(!ArmorCatalog.IsValidSlot(slot))throw new ArgumentOutOfRangeException("slot");
            if(pickupId==0)throw new ArgumentOutOfRangeException("pickupId");
            return CommandName+" equip "+token+" "+((int)slot).ToString(CultureInfo.InvariantCulture)
                +" "+pickupId.ToString(CultureInfo.InvariantCulture);
        }
        public static string EncodeWear(string nonce,ArmorSlot slot,uint pickupId)
        {return EncodeEquip(nonce,slot,pickupId).Replace(" equip "," wear ");}
        public static string EncodeTake(string nonce,ArmorSlot slot,uint pickupId,bool rightHand)
        {return EncodeEquip(nonce,slot,pickupId).Replace(" equip "," take ")+(rightHand?" right":" left");}
        public static bool TryParseCommand(string text,out ArmorCommand command)
        {
            command=null;
            if(text==null||text.Length==0||text.Length>MaxCommandLength)return false;
            // Only ordinary spaces separate tokens. Control characters are never
            // accepted as an alternative command, line or packet separator.
            for(int i=0;i<text.Length;i++)if(text[i]<' '||text[i]>'~')return false;
            var pieces=text.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            if((pieces.Length!=3&&pieces.Length!=5&&pieces.Length!=6)||!string.Equals(pieces[0],CommandName,StringComparison.OrdinalIgnoreCase))return false;
            if(pieces.Length>=5)
            {
                uint equipSlot,pickup;
                ArmorCommandKind kind;
                if(string.Equals(pieces[1],"equip",StringComparison.OrdinalIgnoreCase))kind=ArmorCommandKind.Equip;
                else if(string.Equals(pieces[1],"wear",StringComparison.OrdinalIgnoreCase))kind=ArmorCommandKind.Wear;
                else if(string.Equals(pieces[1],"take",StringComparison.OrdinalIgnoreCase))kind=ArmorCommandKind.Take;
                else return false;
                bool rightHand=false;
                if(kind==ArmorCommandKind.Take)
                {
                    if(pieces.Length!=6)return false;
                    rightHand=string.Equals(pieces[5],"right",StringComparison.OrdinalIgnoreCase);
                    if(!rightHand&&!string.Equals(pieces[5],"left",StringComparison.OrdinalIgnoreCase))return false;
                }
                else if(pieces.Length!=5)return false;
                if(!IsToken(pieces[2])
                    ||!TryUInt(pieces[3],out equipSlot)||equipSlot>=ArmorCatalog.SlotCount
                    ||!TryUInt(pieces[4],out pickup)||pickup==0)return false;
                command=new ArmorCommand(kind,pieces[2].ToLowerInvariant(),(ArmorSlot)equipSlot,pickup,rightHand);
                return true;
            }
            if(string.Equals(pieces[1],"hello23",StringComparison.OrdinalIgnoreCase)||string.Equals(pieces[1],"refresh",StringComparison.OrdinalIgnoreCase))
            {
                if(!IsToken(pieces[2]))return false;
                command=new ArmorCommand(string.Equals(pieces[1],"hello23",StringComparison.OrdinalIgnoreCase)?ArmorCommandKind.Hello:ArmorCommandKind.Refresh,pieces[2].ToLowerInvariant(),ArmorSlot.Head);
                return true;
            }
            return false;
        }
        public static string EncodeAck(string nonce,string epoch)
        {return Prefix+"A|"+NormalizeToken(nonce,"nonce")+"|"+NormalizeToken(epoch,"epoch");}
        public static bool TryParseAck(string wire,out ArmorAck ack)
        {
            ack=null;string payload;
            if(!Payload(wire,'A',out payload))return false;
            var fields=payload.Split('|');
            if(fields.Length!=3||fields[0]!="A"||!IsToken(fields[1])||!IsToken(fields[2]))return false;
            ack=new ArmorAck(fields[1],fields[2]);return true;
        }
        public static string EncodeSnapshot(ArmorSnapshot snapshot)
        {
            if(snapshot==null)throw new ArgumentNullException("snapshot");
            var text=new StringBuilder(Prefix).Append("S|").Append(snapshot.Epoch).Append('|')
                .Append(snapshot.Revision.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(snapshot.Actors.Count.ToString(CultureInfo.InvariantCulture)).Append('|');
            for(int i=0;i<snapshot.Actors.Count;i++)
            {
                if(i>0)text.Append(';');var actor=snapshot.Actors[i];
                text.Append(actor.ControllerId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(actor.HarnessId.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(actor.EquippedMask.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(actor.StyleBits.ToString(CultureInfo.InvariantCulture)).Append(',');
                for(int slot=0;slot<ArmorCatalog.SlotCount;slot++){if(slot>0)text.Append('.');text.Append(actor.Durability[slot].ToString(CultureInfo.InvariantCulture));}
                text.Append(',');for(int slot=0;slot<ArmorCatalog.SlotCount;slot++){if(slot>0)text.Append('.');text.Append(actor.PickupIds[slot].ToString(CultureInfo.InvariantCulture));}
            }
            if(text.Length>MaxWireLength)throw new ArgumentException("Snapshot exceeds wire limit.","snapshot");
            return text.ToString();
        }
        public static bool TryParseSnapshot(string wire,out ArmorSnapshot snapshot)
        {
            snapshot=null;string payload;
            if(!Payload(wire,'S',out payload))return false;
            var fields=payload.Split('|');uint revision,count;
            if(fields.Length!=5||fields[0]!="S"||!IsToken(fields[1])||!TryUInt(fields[2],out revision)||revision==0||!TryUInt(fields[3],out count)||count>MaxActors)return false;
            if(count==0)
            {
                if(fields[4].Length!=0)return false;
                snapshot=new ArmorSnapshot(fields[1],revision,new ArmorActorState[0]);return true;
            }
            if(fields[4].Length==0)return false;
            var entries=fields[4].Split(';');if(entries.Length!=(int)count)return false;
            var actors=new List<ArmorActorState>((int)count);var entities=new HashSet<uint>();
            foreach(string entry in entries)
            {
                var values=entry.Split(',');uint controller,harness,mask,styles;
                if(values.Length!=6||!TryUInt(values[0],out controller)||controller==0||!TryUInt(values[1],out harness)||harness==0
                    ||!TryUInt(values[2],out mask)||mask>ArmorCatalog.AllEquippedMask
                    ||!TryUInt(values[3],out styles)||!ArmorCatalog.ValidStyles((byte)mask,styles))return false;
                if(!entities.Add(controller)||!entities.Add(harness))return false;
                var conditionParts=values[4].Split('.');if(conditionParts.Length!=ArmorCatalog.SlotCount)return false;var condition=new ushort[ArmorCatalog.SlotCount];
                for(int slot=0;slot<condition.Length;slot++){uint v;if(!TryUInt(conditionParts[slot],out v)||v>1000||((mask&(1<<slot))==0&&v!=0))return false;condition[slot]=(ushort)v;}
                var idParts=values[5].Split('.');if(idParts.Length!=ArmorCatalog.SlotCount)return false;var ids=new uint[ArmorCatalog.SlotCount];var unique=new HashSet<uint>();
                for(int slot=0;slot<ids.Length;slot++)if(!TryUInt(idParts[slot],out ids[slot])||(ids[slot]!=0&&((mask&(1<<slot))==0||ids[slot]==controller||ids[slot]==harness||!unique.Add(ids[slot]))))return false;
                actors.Add(new ArmorActorState(controller,harness,(byte)mask,ArmorCatalog.LeatherMaskOf(styles),condition,ids,styles));
            }
            snapshot=new ArmorSnapshot(fields[1],revision,actors);return true;
        }
        static bool Payload(string wire,char kind,out string payload)
        {
            payload=null;
            if(wire==null||wire.Length>MaxWireLength||wire.Length<Prefix.Length+3||!wire.StartsWith(Prefix,StringComparison.Ordinal)||wire[Prefix.Length]!=kind||wire[Prefix.Length+1]!='|')return false;
            for(int i=Prefix.Length;i<wire.Length;i++)if(wire[i]<' '||wire[i]>'~')return false;
            payload=wire.Substring(Prefix.Length);return true;
        }
        static bool TryUInt(string text,out uint value)
        {
            value=0;if(string.IsNullOrEmpty(text)||text.Length>10)return false;
            for(int i=0;i<text.Length;i++)if(text[i]<'0'||text[i]>'9')return false;
            return uint.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value);
        }
    }
}
