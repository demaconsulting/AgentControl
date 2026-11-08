// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Buffers.Binary;
using System.IO.Compression;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Builds small, valid PNG files in memory for the repo-picture tests, so no binary test assets
///     are needed and no imaging library is required to make them.
/// </summary>
internal static class TestPng
{
    /// <summary>
    ///     The eight bytes every PNG file starts with.
    /// </summary>
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     Creates a valid, single-color 8-bit RGBA PNG.
    /// </summary>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    /// <param name="alpha">The opacity, 255 being opaque and 0 fully transparent.</param>
    /// <returns>The PNG file's bytes.</returns>
    public static byte[] Create(int width, int height, byte red = 200, byte green = 30, byte blue = 30, byte alpha = 255)
    {
        // One row of pixels per line: a filter byte (0 = none) and then one RGBA quadruple per pixel
        var raw = new byte[height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * ((width * 4) + 1);
            for (var x = 0; x < width; x++)
            {
                var pixel = row + 1 + (x * 4);
                raw[pixel] = red;
                raw[pixel + 1] = green;
                raw[pixel + 2] = blue;
                raw[pixel + 3] = alpha;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = 6; // color type: RGBA

        using var png = new MemoryStream();
        png.Write(Signature);
        // cspell:disable
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        // cspell:enable
        return png.ToArray();
    }

    /// <summary>
    ///     Creates bytes that look like a PNG to a header check (valid signature and header chunk
    ///     with the given declared size) but are not a decodable image.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <param name="headerChunkLength">The length the header chunk declares for itself; a real PNG
    ///     header chunk is always 13 bytes long, so any other value makes the file malformed.</param>
    /// <returns>The bytes: a header only, with no image data.</returns>
    public static byte[] CreateHeaderOnly(uint width, uint height, uint headerChunkLength = 13)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;

        using var png = new MemoryStream();
        png.Write(Signature);
        // cspell:disable-next-line
        WriteChunk(png, "IHDR", header);
        var bytes = png.ToArray();

        // The chunk length is the four bytes straight after the signature
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(Signature.Length, 4), headerChunkLength);
        return bytes;
    }

    /// <summary>
    ///     Writes one PNG chunk: length, type, data and the CRC of type and data.
    /// </summary>
    /// <param name="stream">The PNG being built.</param>
    /// <param name="type">The four-letter chunk type.</param>
    /// <param name="data">The chunk's data.</param>
    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes.Concat(data).ToArray()));
        stream.Write(crc);
    }

    /// <summary>
    ///     Computes the CRC-32 PNG uses for each chunk.
    /// </summary>
    /// <param name="bytes">The bytes to checksum.</param>
    /// <returns>The checksum.</returns>
    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
