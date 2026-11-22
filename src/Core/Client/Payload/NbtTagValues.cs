using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Terraria.ModLoader.IO;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Keeps tModLoader NBT numeric types intact through readable JSON and repairs widened legacy tags.</summary>
    internal static class NbtTagValues
    {
        private const string TypeProperty = "$nbt";
        private const string ValueProperty = "v";

        /// <summary>Writes one tag value, tagging byte, short, long, float, and double so JSON does not collapse them to int.</summary>
        public static JToken ToJson(object? value)
        {
            // --- Preserve the CLR type tML will later unbox; plain JSON numbers stay ints.

            switch (value)
            {
                case null:
                    return JValue.CreateNull();
                case string s:
                    return s;
                case bool b:
                    // tML stores bool as a byte. A JSON true/false comes back as Boolean and GetBool throws.
                    return Typed("byte", b ? 1 : 0);
                case byte b:
                    return Typed("byte", (int)b);
                case short s:
                    return Typed("short", (int)s);
                case int i:
                    return i;
                case long l:
                    return Typed("long", l);
                case float f:
                    return Typed("float", f);
                case double d:
                    return Typed("double", d);
                case TagCompound compound:
                    return CompoundToJson(compound);
                case IList<string> strings:
                    return StringListToJson(strings);
                case IList<int> ints:
                    return IntListToJson(ints);
                case IList<TagCompound> tags:
                    return CompoundListToJson(tags);
                default:
                    return JValue.CreateNull();
            }
        }

        /// <summary>Reads a JSON value back into the boxed type TagCompound.Get expects.</summary>
        public static object? FromJson(JToken token)
        {
            // --- Typed objects restore width; bare numbers stay int for documents exported before type tags.

            switch (token.Type)
            {
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Integer:
                    return BoxInteger(token);
                case JTokenType.Float:
                    return token.Value<float>();
                case JTokenType.Boolean:
                    return (byte)(token.Value<bool>() ? 1 : 0);
                case JTokenType.Object:
                    return ObjectFromJson((JObject)token);
                case JTokenType.Array:
                    return ArrayFromJson((JArray)token);
                default:
                    return null;
            }
        }

        /// <summary>Rewrites a legacy tag so LoadData and ItemIO.Load can unbox values JSON stored as int.</summary>
        public static void PrepareForLoader(TagCompound tag)
        {
            // --- Walk every nested compound; fix bools, known byte fields, and Point16 shapes in place.

            var pairs = new List<KeyValuePair<string, object>>(tag.Count);
            foreach (KeyValuePair<string, object> pair in tag)
                pairs.Add(pair);

            for (int i = 0; i < pairs.Count; i++)
            {
                string key = pairs[i].Key;
                object prepared = PrepareValue(key, pairs[i].Value);
                if (!ReferenceEquals(prepared, pairs[i].Value))
                    tag[key] = prepared;
            }
        }

        /// <summary>Prepares one value, recursing into compounds and compound lists.</summary>
        private static object PrepareValue(string key, object value)
        {
            // --- Leave strings and already-correct widths alone.

            if (value is TagCompound compound)
            {
                if (TryNarrowPoint16(compound))
                    return compound;

                PrepareForLoader(compound);
                return compound;
            }

            if (value is IList<TagCompound> nested)
            {
                for (int i = 0; i < nested.Count; i++)
                {
                    TagCompound child = nested[i];
                    if (child is null)
                        continue;
                    PrepareForLoader(child);
                }

                return nested;
            }

            if (value is bool flag)
                return (byte)(flag ? 1 : 0);

            if (value is int number && TryNarrowKnownByte(key, number, out byte narrowed))
                return narrowed;

            return value;
        }

        /// <summary>Maps JSON-widened ints back to the byte ItemIO and Magic Storage read for a known key.</summary>
        private static bool TryNarrowKnownByte(string key, int number, out byte narrowed)
        {
            // --- prefix is a raw byte; fav and Inactive are bools stored as 0 or 1.

            narrowed = 0;
            if (key.Equals("prefix", StringComparison.Ordinal))
            {
                if (number < byte.MinValue || number > byte.MaxValue)
                    return false;

                narrowed = (byte)number;
                return true;
            }

            if (key.Equals("fav", StringComparison.Ordinal) || key.Equals("Inactive", StringComparison.Ordinal))
            {
                narrowed = (byte)(number != 0 ? 1 : 0);
                return true;
            }

            return false;
        }

        /// <summary>Converts a compound that is only Point16 coordinates from int to short.</summary>
        private static bool TryNarrowPoint16(TagCompound tag)
        {
            // --- Point16Serializer unboxes short x/y. A larger compound that merely has x/y stays untouched.

            if (tag.Count == 0 || tag.Count > 4)
                return false;

            bool hasX = false;
            bool hasY = false;
            var pairs = new List<KeyValuePair<string, object>>(tag.Count);
            foreach (KeyValuePair<string, object> pair in tag)
            {
                if (pair.Key is not ("x" or "y" or "X" or "Y"))
                    return false;
                if (!TryReadInt(pair.Value, out int number))
                    return false;
                if (number < short.MinValue || number > short.MaxValue)
                    return false;

                if (pair.Key is "x" or "X")
                    hasX = true;
                else
                    hasY = true;

                pairs.Add(pair);
            }

            if (!hasX || !hasY)
                return false;

            for (int i = 0; i < pairs.Count; i++)
            {
                TryReadInt(pairs[i].Value, out int number);
                tag[pairs[i].Key] = (short)number;
            }

            return true;
        }

        /// <summary>Reads an integral tag value that may already be byte, short, or int.</summary>
        private static bool TryReadInt(object value, out int number)
        {
            // --- Accept the widths JSON import and live SaveData both produce.

            switch (value)
            {
                case int i:
                    number = i;
                    return true;
                case short s:
                    number = s;
                    return true;
                case byte b:
                    number = b;
                    return true;
                case long l when l >= int.MinValue && l <= int.MaxValue:
                    number = (int)l;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        /// <summary>Serializes a compound, writing each entry with its numeric width preserved.</summary>
        private static JObject CompoundToJson(TagCompound compound)
        {
            // --- Recurse so nested item and Point16 tags keep their boxed types.

            var obj = new JObject();
            foreach (KeyValuePair<string, object> pair in compound)
                obj[pair.Key] = ToJson(pair.Value);
            return obj;
        }

        /// <summary>Reads a JSON object as either a typed scalar or a nested compound.</summary>
        private static object ObjectFromJson(JObject obj)
        {
            // --- Two-property {$nbt,v} objects are scalars; everything else is a TagCompound.

            if (obj.Count == 2
                && obj.TryGetValue(TypeProperty, out JToken? kindToken)
                && kindToken.Type == JTokenType.String
                && obj.TryGetValue(ValueProperty, out JToken? raw)
                && TryReadTyped(kindToken.Value<string>(), raw, out object? typed)
                && typed is not null)
            {
                return typed;
            }

            var tag = new TagCompound();
            foreach (JProperty property in obj.Properties())
            {
                object? converted = FromJson(property.Value);
                if (converted is not null)
                    tag[property.Name] = converted;
            }

            return tag;
        }

        /// <summary>Restores one typed scalar from its $nbt kind and v payload.</summary>
        private static bool TryReadTyped(string? kind, JToken raw, out object? typed)
        {
            // --- Ignore unknown kinds so a real compound named like a type tag is not swallowed.

            typed = null;
            switch (kind)
            {
                case "byte":
                    if (!TryTokenInt(raw, out int byteValue) || byteValue < byte.MinValue || byteValue > byte.MaxValue)
                        return false;
                    typed = (byte)byteValue;
                    return true;
                case "short":
                    if (!TryTokenInt(raw, out int shortValue) || shortValue < short.MinValue || shortValue > short.MaxValue)
                        return false;
                    typed = (short)shortValue;
                    return true;
                case "int":
                    if (!TryTokenInt(raw, out int intValue))
                        return false;
                    typed = intValue;
                    return true;
                case "long":
                    if (raw.Type != JTokenType.Integer)
                        return false;
                    typed = raw.Value<long>();
                    return true;
                case "float":
                    if (raw.Type != JTokenType.Float && raw.Type != JTokenType.Integer)
                        return false;
                    typed = raw.Value<float>();
                    return true;
                case "double":
                    if (raw.Type != JTokenType.Float && raw.Type != JTokenType.Integer)
                        return false;
                    typed = raw.Value<double>();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Reads a JSON array into the list shape TagCompound already uses.</summary>
        private static object ArrayFromJson(JArray array)
        {
            // --- Empty and object arrays stay compound lists; integral arrays stay List of int.

            if (array.Count == 0)
                return new List<TagCompound>();

            object? first = FromJson(array[0]);
            if (first is TagCompound)
            {
                var compounds = new List<TagCompound>(array.Count);
                for (int i = 0; i < array.Count; i++)
                {
                    if (FromJson(array[i]) is TagCompound compound)
                        compounds.Add(compound);
                }

                return compounds;
            }

            if (first is string)
            {
                var strings = new List<string>(array.Count);
                for (int i = 0; i < array.Count; i++)
                    strings.Add(array[i].Type == JTokenType.String ? array[i].Value<string>() ?? string.Empty : array[i].ToString());
                return strings;
            }

            if (first is int or byte or short or long)
            {
                var ints = new List<int>(array.Count);
                for (int i = 0; i < array.Count; i++)
                {
                    object? element = FromJson(array[i]);
                    if (TryReadInt(element ?? 0, out int number))
                        ints.Add(number);
                }

                return ints;
            }

            return new List<TagCompound>();
        }

        /// <summary>Boxes a JSON integer as int when it fits, otherwise as long.</summary>
        private static object BoxInteger(JToken token)
        {
            // --- Cell and stack fields stay int; values outside Int32 keep long width.

            long number = token.Value<long>();
            if (number >= int.MinValue && number <= int.MaxValue)
                return (int)number;
            return number;
        }

        /// <summary>Reads an integer JSON token into an int when it fits.</summary>
        private static bool TryTokenInt(JToken token, out int number)
        {
            // --- Typed scalars store v as a JSON number.

            number = 0;
            if (token.Type != JTokenType.Integer)
                return false;

            long wide = token.Value<long>();
            if (wide < int.MinValue || wide > int.MaxValue)
                return false;

            number = (int)wide;
            return true;
        }

        /// <summary>Builds the {$nbt,v} object used for non-int numeric values.</summary>
        private static JObject Typed(string kind, JToken value)
        {
            // --- Kind names match the CLR type LoadData unboxes.

            return new JObject
            {
                [TypeProperty] = kind,
                [ValueProperty] = value,
            };
        }

        /// <summary>Writes a string list as a JSON array.</summary>
        private static JArray StringListToJson(IList<string> strings)
        {
            // --- Preserve order and empty entries.

            var array = new JArray();
            for (int i = 0; i < strings.Count; i++)
                array.Add(strings[i]);
            return array;
        }

        /// <summary>Writes an int list as a JSON array of bare numbers.</summary>
        private static JArray IntListToJson(IList<int> ints)
        {
            // --- Int lists have no narrower width to recover.

            var array = new JArray();
            for (int i = 0; i < ints.Count; i++)
                array.Add(ints[i]);
            return array;
        }

        /// <summary>Writes a compound list as a JSON array of objects.</summary>
        private static JArray CompoundListToJson(IList<TagCompound> tags)
        {
            // --- Each element keeps its own numeric types.

            var array = new JArray();
            for (int i = 0; i < tags.Count; i++)
                array.Add(CompoundToJson(tags[i]));
            return array;
        }
    }
}
