using System.IO;
using System.Reflection;
using Alta.Pages;
using UnityEngine;

namespace PlayerArmorMod
{
    internal static class ArmorRecipeBookAppearance
    {
        static Material coverMaterial;
        static Glyph coverGlyph;
        static Texture2D coverTexture;
        static Sprite coverSprite;

        internal static FlairSettings CreateFlairs(FlairSettings source)
        {
            if (source == null) return null;
            var result = new FlairSettings();
            foreach (var field in typeof(FlairSettings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                field.SetValue(result, field.GetValue(source));
            if (Core.IsServer) return result;

            if (source.BookMaterial != null)
            {
                // Keep ATT's leather sampler and native lighting. Only this
                // dedicated book receives the desaturated blue cover palette.
                coverMaterial = new Material(source.BookMaterial)
                {
                    name = "Armor Recipe Book - blue leather",
                    hideFlags = HideFlags.DontSave
                };
                coverMaterial.SetColor("_Color1", new Color(.10f, .17f, .22f, 1));
                coverMaterial.SetColor("_Color2", new Color(.23f, .34f, .39f, 1));
                ArmorNative.Set(result, "bookMaterial", coverMaterial);
            }

            var piece = ArmorCatalog.Get(ArmorStyle.Metal, ArmorSlot.Chest);
            using (var input = typeof(ArmorRecipeBookAppearance).Assembly.GetManifestResourceStream(ArmorRecipeGlyphArt.ResourceName(piece)))
            {
                if (input == null) return result;
                using (var bytes = new MemoryStream())
                {
                    input.CopyTo(bytes);
                    var encoded = bytes.ToArray();
                    if (!ArmorRecipeGlyphArt.ValidatePng(encoded, out _, out _, out _)) return result;
                    coverTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = "Armor Recipe Book - cuirass stamp",
                        hideFlags = HideFlags.DontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!ImageConversion.LoadImage(coverTexture, encoded, false)) return result;
                }
            }
            var pixels = coverTexture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(235, 203, 142, pixels[i].a);
            coverTexture.SetPixels32(pixels);
            coverTexture.Apply(false, true);
            coverSprite = Sprite.Create(coverTexture, new Rect(0, 0, coverTexture.width, coverTexture.height), new Vector2(.5f, .5f), 100);
            coverSprite.name = "Armor Recipe Book - cuirass stamp";
            coverSprite.hideFlags = HideFlags.DontSave;
            coverGlyph = ScriptableObject.CreateInstance<Glyph>();
            coverGlyph.name = "Armor Recipe Book - cuirass";
            coverGlyph.hideFlags = HideFlags.DontSave;
            ArmorNative.Set(coverGlyph, "sprite", coverSprite);
            ArmorNative.Set(result, "glyph", coverGlyph);
            return result;
        }

        internal static void Dispose()
        {
            UnityEngine.Object.Destroy(coverGlyph);
            UnityEngine.Object.Destroy(coverSprite);
            UnityEngine.Object.Destroy(coverTexture);
            UnityEngine.Object.Destroy(coverMaterial);
            coverGlyph = null;
            coverSprite = null;
            coverTexture = null;
            coverMaterial = null;
        }
    }
}
