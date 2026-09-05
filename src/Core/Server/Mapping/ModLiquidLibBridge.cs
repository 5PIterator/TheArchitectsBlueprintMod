using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>Optional reflection bridge to ModLiquidLib liquid registry and textures.</summary>
    internal static class ModLiquidLibBridge
    {
        private const string ModInternalName = "ModLiquidLib";
        private const string LoaderTypeName = "ModLiquidLib.ModLoader.LiquidLoader";

        private static bool _resolved;
        private static bool _available;
        private static Type? _loaderType;
        private static PropertyInfo? _liquidCountProperty;
        private static MethodInfo? _getLiquidMethod;
        private static FieldInfo? _liquidAssetsField;
        private static FieldInfo? _liquidBlockAssetsField;
        private static PropertyInfo? _modTypeFullNameProperty;
        private static PropertyInfo? _modTypeNameProperty;
        private static PropertyInfo? _assetValueProperty;

        /// <summary>Returns whether ModLiquidLib is loaded and its LiquidLoader API is usable.</summary>
        public static bool IsAvailable()
        {
            // --- Resolve the optional library once, then cache availability.

            EnsureResolved();
            return _available;
        }

        /// <summary>Returns ModLiquidLib's liquid count, or vanilla <see cref="LiquidID.Count"/> when unavailable.</summary>
        public static int GetLiquidCount()
        {
            // --- Prefer the library count; otherwise report only the four vanilla liquid IDs.

            EnsureResolved();
            if (!_available || _liquidCountProperty is null)
                return LiquidID.Count;

            object? value = _liquidCountProperty.GetValue(null);
            return value is int count ? count : LiquidID.Count;
        }

        /// <summary>Returns the ModLiquid FullName for a registered mod liquid ID, or null.</summary>
        public static string? TryGetLiquidFullName(int type)
        {
            // --- Look up the ModLiquid instance and read its ModType.FullName.

            object? liquid = TryGetLiquid(type);
            if (liquid is null)
                return null;

            EnsureModTypeProperties(liquid.GetType());
            return _modTypeFullNameProperty?.GetValue(liquid) as string;
        }

        /// <summary>Resolves a ModLiquid FullName or Name to its runtime liquid ID.</summary>
        public static int? TryResolveLiquidName(string name)
        {
            // --- Scan registered ModLiquid entries for a matching FullName or Name.

            if (string.IsNullOrEmpty(name) || !IsAvailable())
                return null;

            int count = GetLiquidCount();
            for (int type = LiquidID.Count; type < count; type++)
            {
                object? liquid = TryGetLiquid(type);
                if (liquid is null)
                    continue;

                EnsureModTypeProperties(liquid.GetType());
                string? fullName = _modTypeFullNameProperty?.GetValue(liquid) as string;
                if (string.Equals(fullName, name, StringComparison.OrdinalIgnoreCase))
                    return type;

                string? shortName = _modTypeNameProperty?.GetValue(liquid) as string;
                if (string.Equals(shortName, name, StringComparison.OrdinalIgnoreCase))
                    return type;
            }

            return null;
        }

        /// <summary>Returns the primary liquid texture for a ModLiquidLib liquid, preferring block texture fallback.</summary>
        public static Texture2D? TryGetLiquidTexture(int type)
        {
            // --- Read LiquidAssets[type], then LiquidBlockAssets[type], and unwrap Asset&lt;Texture2D&gt;.Value.

            EnsureResolved();
            if (!_available || type < LiquidID.Count || type >= GetLiquidCount())
                return null;

            Texture2D? texture = TryGetAssetTexture(_liquidAssetsField, type);
            if (texture is not null)
                return texture;

            return TryGetAssetTexture(_liquidBlockAssetsField, type);
        }

        /// <summary>Lazily binds ModLiquidLib loader members when the mod is present.</summary>
        private static void EnsureResolved()
        {
            // --- Locate ModLiquidLib once and cache reflection handles for LiquidLoader.

            if (_resolved)
                return;

            _resolved = true;
            if (!ModLoader.TryGetMod(ModInternalName, out Mod? mod) || mod.Code is null)
                return;

            _loaderType = mod.Code.GetType(LoaderTypeName);
            if (_loaderType is null)
                return;

            _liquidCountProperty = _loaderType.GetProperty("LiquidCount", BindingFlags.Public | BindingFlags.Static);
            _getLiquidMethod = _loaderType.GetMethod("GetLiquid", BindingFlags.Public | BindingFlags.Static, [typeof(int)]);
            _liquidAssetsField = _loaderType.GetField("LiquidAssets", BindingFlags.Public | BindingFlags.Static);
            _liquidBlockAssetsField = _loaderType.GetField("LiquidBlockAssets", BindingFlags.Public | BindingFlags.Static);

            _available = _liquidCountProperty is not null && _getLiquidMethod is not null;
        }

        /// <summary>Returns the ModLiquid instance for a type, or null when unavailable.</summary>
        private static object? TryGetLiquid(int type)
        {
            // --- Invoke LiquidLoader.GetLiquid(type) through the cached method handle.

            EnsureResolved();
            if (!_available || _getLiquidMethod is null || type < LiquidID.Count)
                return null;

            return _getLiquidMethod.Invoke(null, [type]);
        }

        /// <summary>Caches FullName/Name/Type property handles from a ModLiquid runtime type.</summary>
        private static void EnsureModTypeProperties(Type liquidType)
        {
            // --- Discover ModType identity properties once from the first live ModLiquid instance.

            if (_modTypeFullNameProperty is not null)
                return;

            _modTypeFullNameProperty = liquidType.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
            _modTypeNameProperty = liquidType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>Reads one entry from a LiquidLoader Asset&lt;Texture2D&gt;[] field.</summary>
        private static Texture2D? TryGetAssetTexture(FieldInfo? assetsField, int type)
        {
            // --- Index the asset array and unwrap the Asset&lt;Texture2D&gt;.Value property.

            if (assetsField is null)
                return null;

            object? assetsObj = assetsField.GetValue(null);
            if (assetsObj is not Array assets || type < 0 || type >= assets.Length)
                return null;

            object? asset = assets.GetValue(type);
            if (asset is null)
                return null;

            _assetValueProperty ??= asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return _assetValueProperty?.GetValue(asset) as Texture2D;
        }
    }
}
