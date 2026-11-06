using System;
using System.IO;
using System.Text;

namespace PlayerArmorMod
{
    internal interface IArmorRecipeReferenceStream
    {
        bool IsWriting {get;}
        void Bits(ref uint value,int count);
        void Integer(ref int value,int minimum,int maximum);
    }
    internal static class ArmorCraftingWire
    {
        // Returns true for the extension. Ordinary native references retain
        // exactly their original sixteen bits, including null (zero).
        internal static bool Transfer(IArmorRecipeReferenceStream stream,ref uint hash,ref string descriptor,ref string scope)
        {
            bool writing=stream.IsWriting,armor=writing&&descriptor!=null;
            uint header=armor?ArmorCraftingRecipeCodec.Sentinel:hash;
            stream.Bits(ref header,16);
            if(header!=ArmorCraftingRecipeCodec.Sentinel){hash=header;descriptor=null;scope=null;return false;}
            Guid epoch;
            if(writing&&!Guid.TryParseExact(scope,"N",out epoch))throw new InvalidDataException("Invalid armor recipe server scope");
            var token=writing?Guid.ParseExact(scope,"N").ToByteArray():new byte[16];
            for(int i=0;i<token.Length;i++){uint value=writing?token[i]:0u;stream.Bits(ref value,8);if(!writing)token[i]=(byte)value;}
            if(!writing)scope=new Guid(token).ToString("N");
            if(writing&&(hash==0||hash>=ArmorCraftingRecipeCodec.Sentinel))throw new InvalidDataException("Invalid armor recipe version hash");
            stream.Bits(ref hash,16);
            if(hash==0||hash>=ArmorCraftingRecipeCodec.Sentinel)throw new InvalidDataException("Invalid armor recipe version reference");
            byte[] bytes=writing?Encoding.UTF8.GetBytes(descriptor):null;
            int length=bytes==null?0:bytes.Length;
            if(writing&&(length==0||length>ArmorCraftingRecipeCodec.MaximumDescriptorBytes))throw new InvalidDataException("Invalid armor recipe network descriptor size");
            stream.Integer(ref length,1,ArmorCraftingRecipeCodec.MaximumDescriptorBytes);
            if(length<1||length>ArmorCraftingRecipeCodec.MaximumDescriptorBytes)throw new InvalidDataException("Invalid armor recipe network descriptor length");
            if(!writing)bytes=new byte[length];
            for(int i=0;i<length;i++){uint value=writing?bytes[i]:0u;stream.Bits(ref value,8);if(!writing)bytes[i]=(byte)value;}
            if(!writing)descriptor=new UTF8Encoding(false,true).GetString(bytes);
            return true;
        }
    }
}
