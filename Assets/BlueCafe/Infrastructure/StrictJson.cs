using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlueCafe.Infrastructure
{
    // JsonUtility deliberately ignores unknown fields. Validate the actual JSON first,
    // including duplicate keys, required members and nonfinite numeric literals.
    internal sealed class StrictJson
    {
        readonly string text;
        Action<string, HashSet<string>> objectCheck;
        Action<string, char> valueCheck;
        Action<string, string> numberCheck;
        int offset;
        StrictJson(string text) { this.text = text ?? throw new ArgumentNullException(nameof(text)); }
        public static HashSet<string> Validate(string text)
        {
            var parser = new StrictJson(text);
            parser.Space();
            var keys = parser.Object(0);
            parser.Space();
            if (parser.offset != text.Length) parser.Fail();
            return keys;
        }
        public static void Require(HashSet<string> keys, params string[] names)
        {
            foreach (var name in names)
                if (!keys.Contains(name)) throw new FormatException("JSON is missing required member '" + name + "'.");
        }
        public static void Inspect(string text, Action<string, HashSet<string>> objectCheck, Action<string, char> valueCheck, Action<string, string> numberCheck = null)
        {
            var parser = new StrictJson(text) { objectCheck = objectCheck, valueCheck = valueCheck, numberCheck = numberCheck };
            parser.Space(); parser.Object(0, ""); parser.Space();
            if (parser.offset != text.Length) parser.Fail();
        }
        void Space() { while (offset < text.Length && (text[offset] == ' ' || text[offset] == '\t' || text[offset] == '\r' || text[offset] == '\n')) offset++; }
        void Fail() { throw new FormatException("Malformed JSON at character " + offset + "."); }
        bool Take(char value) { Space(); if (offset < text.Length && text[offset] == value) { offset++; return true; } return false; }
        void Expect(char value) { if (!Take(value)) Fail(); }
        HashSet<string> Object(int depth, string path = null)
        {
            Expect('{');
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (Take('}')) { objectCheck?.Invoke(path, keys); return keys; }
            do
            {
                Space(); var key = String();
                if (!keys.Add(key)) throw new FormatException("Duplicate JSON member '" + key + "'.");
                Expect(':'); Value(depth + 1, path == null ? null : path + "." + key, key == "triangles");
                if (Take('}')) { objectCheck?.Invoke(path, keys); return keys; }
            } while (Take(','));
            Fail(); return keys;
        }
        void Value(int depth, string path = null, bool integerValues = false)
        {
            if (depth > 64) throw new FormatException("JSON nesting exceeds 64 levels.");
            Space(); if (offset >= text.Length) Fail();
            char c = text[offset];
            if (integerValues && c != '[' && c != '-' && (c < '0' || c > '9')) throw new FormatException("Triangle indices must be integers.");
            valueCheck?.Invoke(path, c);
            if (c == '{') { Object(depth, path); return; }
            if (c == '[')
            {
                offset++; if (Take(']')) return;
                int index = 0;
                do { Value(depth + 1, path == null ? null : path + "[" + index++ + "]", integerValues); if (Take(']')) return; } while (Take(','));
                Fail();
            }
            if (c == '"') { String(); return; }
            if (c == 't') { Literal("true"); return; }
            if (c == 'f') { Literal("false"); return; }
            if (c == 'n') { Literal("null"); return; }
            Number(path, integerValues);
        }
        void Literal(string literal)
        {
            if (offset + literal.Length > text.Length || string.CompareOrdinal(text, offset, literal, 0, literal.Length) != 0) Fail();
            offset += literal.Length;
        }
        string String()
        {
            Expect('"'); var result = new System.Text.StringBuilder();
            while (offset < text.Length)
            {
                char c = text[offset++];
                if (c == '"') return result.ToString();
                if (c < 32) Fail();
                if (c != '\\') { result.Append(c); continue; }
                if (offset >= text.Length) Fail();
                c = text[offset++];
                switch (c)
                {
                    case '"': case '\\': case '/': result.Append(c); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (offset + 4 > text.Length || !ushort.TryParse(text.Substring(offset, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicode)) Fail();
                        else { result.Append((char)unicode); offset += 4; }
                        break;
                    default: Fail(); break;
                }
            }
            Fail(); return null;
        }
        void Number(string path, bool integerValues)
        {
            int start = offset;
            if (offset < text.Length && text[offset] == '-') offset++;
            if (offset >= text.Length) Fail();
            if (text[offset] == '0') offset++;
            else { if (text[offset] < '1' || text[offset] > '9') Fail(); Digits(); }
            if (offset < text.Length && text[offset] == '.') { offset++; int digits = offset; Digits(); if (digits == offset) Fail(); }
            if (offset < text.Length && (text[offset] == 'e' || text[offset] == 'E'))
            {
                offset++; if (offset < text.Length && (text[offset] == '+' || text[offset] == '-')) offset++;
                int digits = offset; Digits(); if (digits == offset) Fail();
            }
            if (!double.TryParse(text.Substring(start, offset - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) || double.IsNaN(number)) Fail();
            if (integerValues && !int.TryParse(text.Substring(start, offset - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) throw new FormatException("Triangle indices must be integers.");
            numberCheck?.Invoke(path, text.Substring(start, offset - start));
        }
        void Digits() { while (offset < text.Length && text[offset] >= '0' && text[offset] <= '9') offset++; }
    }
}
