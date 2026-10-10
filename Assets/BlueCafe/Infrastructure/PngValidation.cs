using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BlueCafe.Infrastructure
{
    /// <summary>Off-thread PNG integrity checks; no Texture2D allocation or Unity thread affinity.</summary>
    internal static class PngValidation
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        static readonly uint[] CrcTable = MakeCrcTable();
        public static void Validate(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 57) throw new InvalidDataException("Truncated PNG.");
            for (int i = 0; i < Signature.Length; i++) if (bytes[i] != Signature[i]) throw new InvalidDataException("Expected a PNG signature.");
            int offset = 8, width = 0, height = 0, bits = 0, channels = 0, interlace = 0;
            bool header = false, end = false, palette = false;
            int color = 0;
            using (var compressed = new MemoryStream())
            {
                while (offset < bytes.Length)
                {
                    if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
                    uint size = U32(bytes, offset);
                    if (size > int.MaxValue || size > bytes.Length - offset - 12) throw new InvalidDataException("Invalid PNG chunk length.");
                    int length = (int)size;
                    string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                    uint crc = 0xffffffff;
                    for (int i = offset + 4; i < offset + 8 + length; i++) crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
                    if ((crc ^ 0xffffffff) != U32(bytes, offset + 8 + length)) throw new InvalidDataException("PNG " + type + " checksum failed.");
                    if (!header && type != "IHDR") throw new InvalidDataException("PNG must begin with IHDR.");
                    if (type == "IHDR")
                    {
                        if (header || length != 13) throw new InvalidDataException("Invalid PNG header.");
                        uint w = U32(bytes, offset + 8), h = U32(bytes, offset + 12);
                        if (w == 0 || h == 0 || w > 16384 || h > 16384 || (long)w * h > 67108864) throw new InvalidDataException("PNG dimensions exceed supported limits.");
                        width = (int)w; height = (int)h; bits = bytes[offset + 16]; color = bytes[offset + 17];
                        channels = color == 0 || color == 3 ? 1 : color == 2 ? 3 : color == 4 ? 2 : color == 6 ? 4 : 0;
                        bool validBits = color == 0 ? bits == 1 || bits == 2 || bits == 4 || bits == 8 || bits == 16 : color == 3 ? bits == 1 || bits == 2 || bits == 4 || bits == 8 : bits == 8 || bits == 16;
                        interlace = bytes[offset + 20];
                        if (channels == 0 || !validBits || bytes[offset + 18] != 0 || bytes[offset + 19] != 0 || interlace > 1) throw new InvalidDataException("Unsupported PNG encoding.");
                        header = true;
                    }
                    else if (type == "PLTE")
                    {
                        if (length == 0 || length > 768 || length % 3 != 0 || compressed.Length != 0) throw new InvalidDataException("Invalid PNG palette.");
                        palette = true;
                    }
                    else if (type == "IDAT") compressed.Write(bytes, offset + 8, length);
                    else if (type == "IEND")
                    {
                        if (length != 0 || offset + 12 != bytes.Length) throw new InvalidDataException("Invalid PNG end chunk or trailing data.");
                        end = true; break;
                    }
                    else if (type.Length != 4 || (type[0] >= 'A' && type[0] <= 'Z')) throw new InvalidDataException("Unknown critical PNG chunk: " + type);
                    offset += 12 + length;
                }
                if (!end || compressed.Length < 6 || (color == 3 && !palette)) throw new InvalidDataException("Incomplete PNG image data.");
                byte[] data = compressed.ToArray();
                if ((data[0] & 15) != 8 || (data[0] >> 4) > 7 || ((data[0] << 8) + data[1]) % 31 != 0 || (data[1] & 32) != 0) throw new InvalidDataException("Invalid PNG zlib header.");
                uint adlerA = 1, adlerB = 0;
                using (var raw = new MemoryStream(data, 2, data.Length - 6))
                using (var deflate = new DeflateStream(raw, CompressionMode.Decompress))
                {
                    byte[] buffer = new byte[32768];
                    Action<int, int> rows = (w, h) =>
                    {
                        int rowBytes = checked((w * channels * bits + 7) / 8);
                        for (int row = 0; row < h; row++)
                        {
                            int filter = deflate.ReadByte();
                            if (filter < 0 || filter > 4) throw new InvalidDataException("Invalid PNG scanline filter or truncated data.");
                            adlerA = (adlerA + (uint)filter) % 65521; adlerB = (adlerB + adlerA) % 65521;
                            int remaining = rowBytes;
                            while (remaining > 0)
                            {
                                int read = deflate.Read(buffer, 0, Math.Min(remaining, buffer.Length));
                                if (read == 0) throw new InvalidDataException("Truncated PNG pixels.");
                                for (int i = 0; i < read; i++) { adlerA = (adlerA + buffer[i]) % 65521; adlerB = (adlerB + adlerA) % 65521; }
                                remaining -= read;
                            }
                        }
                    };
                    if (interlace == 0) rows(width, height);
                    else
                    {
                        int[] xs = { 0, 4, 0, 2, 0, 1, 0 }, ys = { 0, 0, 4, 0, 2, 0, 1 };
                        int[] dx = { 8, 8, 4, 4, 2, 2, 1 }, dy = { 8, 8, 8, 4, 4, 2, 2 };
                        for (int pass = 0; pass < 7; pass++)
                        {
                            int w = width <= xs[pass] ? 0 : (width - xs[pass] + dx[pass] - 1) / dx[pass];
                            int h = height <= ys[pass] ? 0 : (height - ys[pass] + dy[pass] - 1) / dy[pass];
                            if (w > 0 && h > 0) rows(w, h);
                        }
                    }
                    if (deflate.ReadByte() != -1) throw new InvalidDataException("PNG has excess pixel data.");
                }
                if ((adlerB << 16 | adlerA) != U32(data, data.Length - 4)) throw new InvalidDataException("PNG pixel checksum failed.");
            }
        }
        static uint U32(byte[] data, int offset) => (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 | (uint)data[offset + 2] << 8 | data[offset + 3];
        static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) { uint c = i; for (int j = 0; j < 8; j++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1; table[i] = c; }
            return table;
        }
    }
}
