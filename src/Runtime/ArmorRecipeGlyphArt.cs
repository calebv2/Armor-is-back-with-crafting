using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PlayerArmorMod
{
    // Artwork only: native result/title identities and page layout stay owned
    // by their existing registration paths. No pixel work runs on the server.
    internal static class ArmorRecipeGlyphArt
    {
        internal const int MaximumDimension=2048,MaximumEncodedBytes=2*1024*1024;
        internal const long MaximumCachedPixels=128L*1024*1024/4;
        internal const string ResourcePrefix="PlayerArmor.Glyphs.";
        static readonly Dictionary<string,ArmorPieceDefinition> known=ArmorCatalog.RecipePagePieces
            .ToDictionary(p=>p.PrefabName,p=>p,StringComparer.Ordinal);
        internal static bool Owns(ArmorPieceDefinition piece)
        {ArmorPieceDefinition expected;return piece!=null&&piece.PrefabName!=null&&known.TryGetValue(piece.PrefabName,out expected)&&expected.Style==piece.Style&&expected.Slot==piece.Slot;}
        internal static string ResourceName(ArmorPieceDefinition piece){return Owns(piece)?ResourcePrefix+piece.PrefabName+".png":null;}
        static int BigEndian(byte[] data,int at){return(data[at]<<24)|(data[at+1]<<16)|(data[at+2]<<8)|data[at+3];}
        internal static bool ValidatePng(byte[] data,out int width,out int height,out string reason)
        {
            width=height=0;reason="invalid PNG header";
            byte[] signature={137,80,78,71,13,10,26,10};
            if(data==null||data.Length<45||data.Length>MaximumEncodedBytes)return false;
            for(int i=0;i<signature.Length;i++)if(data[i]!=signature[i])return false;
            if(BigEndian(data,8)!=13||data[12]!=73||data[13]!=72||data[14]!=68||data[15]!=82)return false;
            width=BigEndian(data,16);height=BigEndian(data,20);
            if(width<16||height<16||width>MaximumDimension||height>MaximumDimension){reason="PNG dimensions exceed glyph limits";return false;}
            if(data[24]!=8||(data[25]!=6&&data[25]!=4)||data[26]!=0||data[27]!=0||data[28]>1){reason="glyph PNG needs eight-bit RGBA or grayscale alpha";return false;}
            reason=null;return true;
        }
        internal static bool ValidateInk(Color32[] pixels,int width,int height,out string reason)
        {
            reason="glyph has no usable transparent ink";if(pixels==null||pixels.Length!=(long)width*height)return false;
            int ink=0,clear=0;foreach(var p in pixels){if(p.a==0)clear++;else ink++;}
            if(clear==0||ink<16||ink>(long)pixels.Length*4/5)return false;reason=null;return true;
        }
        sealed class Entry{internal Texture2D Texture;internal Sprite Sprite;}
        static readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>(StringComparer.Ordinal);
        static readonly HashSet<string> failed=new HashSet<string>(StringComparer.Ordinal);
        static long cachedPixels;
        static byte[] Read(Stream input)
        {
            using(var output=new MemoryStream())
            {
                var buffer=new byte[8192];int count;
                while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>MaximumEncodedBytes)throw new InvalidDataException("encoded glyph exceeds budget");output.Write(buffer,0,count);}
                return output.ToArray();
            }
        }
        static void Destroy(UnityEngine.Object value)
        {if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        static void Reject(string name,string reason)
        {if(failed.Add(name)&&failed.Count<=known.Count)Core.Log("PLAYER_ARMOR_RECIPE_GLYPH_FALLBACK name="+name+" reason="+(reason==null?"unavailable":reason.Substring(0,Math.Min(reason.Length,180))));}
        internal static bool TryGet(ArmorPieceDefinition piece,out Sprite sprite)
        {
            sprite=null;if(Core.IsServer||!Owns(piece))return false;
            Entry existing;if(cache.TryGetValue(piece.PrefabName,out existing)){sprite=existing.Sprite;return sprite!=null;}
            if(failed.Contains(piece.PrefabName))return false;Texture2D texture=null;Sprite created=null;
            try
            {
                byte[] encoded;using(var stream=typeof(ArmorRecipeGlyphArt).Assembly.GetManifestResourceStream(ResourceName(piece)))
                {if(stream==null){Reject(piece.PrefabName,"embedded artwork missing");return false;}encoded=Read(stream);}
                int width,height;string reason;
                if(!ValidatePng(encoded,out width,out height,out reason)){Reject(piece.PrefabName,reason);return false;}
                if(cachedPixels+(long)width*height>MaximumCachedPixels){Reject(piece.PrefabName,"decoded glyph cache budget exceeded");return false;}
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name=piece.PrefabName+" recipe ink",hideFlags=HideFlags.DontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(texture,encoded,false)||texture.width!=width||texture.height!=height)throw new InvalidDataException("PNG decode dimensions differ");
                var pixels=texture.GetPixels32();if(!ValidateInk(pixels,width,height,out reason))throw new InvalidDataException(reason);
                for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(0,0,0,pixels[i].a);
                texture.SetPixels32(pixels);texture.Apply(false,true);
                created=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),100);
                if(created==null)throw new InvalidDataException("glyph sprite could not be created");created.name=piece.PrefabName+" recipe glyph";created.hideFlags=HideFlags.DontSave;
                cache.Add(piece.PrefabName,new Entry{Texture=texture,Sprite=created});cachedPixels+=(long)width*height;sprite=created;texture=null;created=null;return true;
            }
            catch(Exception error){Reject(piece.PrefabName,error.GetBaseException().Message);return false;}
            finally{Destroy(created);Destroy(texture);}
        }
        internal static void Dispose()
        {
            if(Core.IsServer)return;
            foreach(var entry in cache.Values){Destroy(entry.Sprite);Destroy(entry.Texture);}cache.Clear();failed.Clear();cachedPixels=0;
        }
    }
}
