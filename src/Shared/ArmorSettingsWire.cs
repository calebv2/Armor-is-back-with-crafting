using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PlayerArmorMod
{
    internal sealed class ArmorSettingsChunk
    {internal string Epoch,Data;internal uint Revision;internal int Index,Count;}
    internal static class ArmorSettingsWire
    {
        internal const int ChunkLength=768,MaximumParts=128,MaximumBytes=65536;
        internal static IEnumerable<string> Encode(string epoch,uint revision,string json)
        {
            epoch=ArmorProtocol.NormalizeToken(epoch,"epoch");if(revision==0)throw new ArgumentOutOfRangeException("revision");
            var bytes=Encoding.UTF8.GetBytes(json);if(bytes.Length>MaximumBytes)throw new ArgumentException("Armor configuration exceeds transport limit");
            string data=Convert.ToBase64String(bytes);int count=Math.Max(1,(data.Length+ChunkLength-1)/ChunkLength);
            for(int i=0;i<count;i++)yield return ArmorProtocol.Prefix+"C|"+epoch+"|"+revision.ToString(CultureInfo.InvariantCulture)+"|"+i.ToString(CultureInfo.InvariantCulture)+"|"+count.ToString(CultureInfo.InvariantCulture)+"|"+data.Substring(i*ChunkLength,Math.Min(ChunkLength,data.Length-i*ChunkLength));
        }
        internal static bool TryParse(string wire,out ArmorSettingsChunk chunk)
        {
            chunk=null;if(wire==null||wire.Length>1024||!wire.StartsWith(ArmorProtocol.Prefix+"C|",StringComparison.Ordinal))return false;
            var p=wire.Substring(ArmorProtocol.Prefix.Length).Split('|');uint rev,index,count;
            if(p.Length!=6||p[0]!="C"||!ArmorProtocol.IsToken(p[1])||!uint.TryParse(p[2],NumberStyles.None,CultureInfo.InvariantCulture,out rev)||rev==0
                ||!uint.TryParse(p[3],NumberStyles.None,CultureInfo.InvariantCulture,out index)||!uint.TryParse(p[4],NumberStyles.None,CultureInfo.InvariantCulture,out count)
                ||count<1||count>MaximumParts||index>=count||p[5].Length>ChunkLength||(index<count-1&&p[5].Length!=ChunkLength))return false;
            foreach(char c in p[5])if(!((c>='A'&&c<='Z')||(c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='+'||c=='/'||c=='='))return false;
            chunk=new ArmorSettingsChunk{Epoch=p[1].ToLowerInvariant(),Revision=rev,Index=(int)index,Count=(int)count,Data=p[5]};return true;
        }
    }
    internal sealed class ArmorSettingsAssembly
    {
        uint assembling,accepted;string epoch;string[] parts;int received;
        internal void Clear(){assembling=accepted=0;epoch=null;parts=null;received=0;}
        internal bool Accept(ArmorSettingsChunk chunk,string expectedEpoch,out string json)
        {
            json=null;if(chunk==null||chunk.Epoch!=expectedEpoch||chunk.Revision<=accepted||chunk.Count<1||chunk.Count>ArmorSettingsWire.MaximumParts||chunk.Index<0||chunk.Index>=chunk.Count||chunk.Data==null||chunk.Data.Length>ArmorSettingsWire.ChunkLength)return false;
            if(epoch!=expectedEpoch||chunk.Revision>assembling){epoch=expectedEpoch;assembling=chunk.Revision;parts=new string[chunk.Count];received=0;}
            if(chunk.Revision!=assembling||parts==null||parts.Length!=chunk.Count)return false;
            if(parts[chunk.Index]!=null){if(parts[chunk.Index]!=chunk.Data){parts=null;received=0;assembling=0;}return false;}
            parts[chunk.Index]=chunk.Data;if(++received!=parts.Length)return false;
            try
            {
                byte[] bytes=Convert.FromBase64String(string.Concat(parts));if(bytes.Length>ArmorSettingsWire.MaximumBytes){parts=null;assembling=0;return false;}
                json=new UTF8Encoding(false,true).GetString(bytes);accepted=assembling;parts=null;return true;
            }
            catch(FormatException){parts=null;assembling=0;return false;}catch(DecoderFallbackException){parts=null;assembling=0;return false;}
        }
    }
}
