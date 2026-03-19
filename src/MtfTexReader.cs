using System;
using System.IO;
using System.Text;

namespace MtfTexPaintDotNet
{
    internal enum BcFormat
    {
        BC1,
        BC2,
        BC3,
    }

    internal sealed class DecodedTex
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Rgba32 { get; }
        public string DebugInfo { get; }

        public DecodedTex(int width, int height, byte[] rgba32, string debugInfo)
        {
            Width = width;
            Height = height;
            Rgba32 = rgba32;
            DebugInfo = debugInfo;
        }
    }

    internal static class MtfTexReader
    {
        public static DecodedTex Read(Stream input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            byte[] data = ReadAllBytes(input);
            if (data.Length < 0x2C)
            {
                throw new InvalidDataException("File is too small to be an RE5 PC TEX.");
            }

            if (data[0] != (byte)'T' || data[1] != (byte)'E' || data[2] != (byte)'X' || data[3] != 0)
            {
                throw new InvalidDataException("Not an MT Framework TEX file.");
            }

            if (!LooksLikeRe5Pc(data, out string reason))
            {
                throw new NotSupportedException(
                    "This build only supports Resident Evil 5 PC TEX files with DXT1/DXT3/DXT5 mip chains. " +
                    $"Header check failed: {reason}");
            }

            return ReadRe5Pc(data);
        }

        private static bool LooksLikeRe5Pc(byte[] data, out string reason)
        {
            reason = string.Empty;

            int mipCount = data[0x08];
            if (mipCount <= 0 || mipCount > 16)
            {
                reason = $"invalid mip count {mipCount}";
                return false;
            }

            int width = ReadUInt16LE(data, 0x0C);
            int height = ReadUInt16LE(data, 0x0E);
            if (width <= 0 || height <= 0)
            {
                reason = $"invalid dimensions {width}x{height}";
                return false;
            }

            string tag = Encoding.ASCII.GetString(data, 0x14, 4);
            if (tag != "DXT1" && tag != "DXT3" && tag != "DXT5")
            {
                reason = $"unsupported compression tag '{tag}'";
                return false;
            }

            int mipTableOffset = 0x28;
            int mipTableBytes = mipCount * 4;
            if (data.Length < mipTableOffset + mipTableBytes)
            {
                reason = "truncated mip table";
                return false;
            }

            int firstMipOffset = ReadInt32LE(data, mipTableOffset);
            if (firstMipOffset < mipTableOffset + mipTableBytes || firstMipOffset >= data.Length)
            {
                reason = $"invalid first mip offset 0x{firstMipOffset:X}";
                return false;
            }

            return true;
        }

        private static DecodedTex ReadRe5Pc(byte[] data)
        {
            int mipCount = data[0x08];
            int width = ReadUInt16LE(data, 0x0C);
            int height = ReadUInt16LE(data, 0x0E);
            string dxt = Encoding.ASCII.GetString(data, 0x14, 4);

            BcFormat format = dxt switch
            {
                "DXT1" => BcFormat.BC1,
                "DXT3" => BcFormat.BC2,
                "DXT5" => BcFormat.BC3,
                _ => throw new NotSupportedException($"Unsupported RE5 TEX compression tag '{dxt}'.")
            };

            int[] mipOffsets = new int[mipCount];
            for (int i = 0; i < mipCount; i++)
            {
                mipOffsets[i] = ReadInt32LE(data, 0x28 + i * 4);
            }

            ValidateMipOffsets(data, mipOffsets);
            ValidateTopMipPayload(data, mipOffsets[0], width, height, format);

            byte[] rgba = DecodeTopMip(data, mipOffsets[0], width, height, format);
            return new DecodedTex(width, height, rgba, $"RE5 PC TEX, {dxt}, {width}x{height}, mips={mipCount}");
        }

        private static void ValidateMipOffsets(byte[] data, int[] mipOffsets)
        {
            for (int i = 0; i < mipOffsets.Length; i++)
            {
                int offset = mipOffsets[i];
                if (offset < 0 || offset >= data.Length)
                {
                    throw new InvalidDataException($"Mip {i} offset is outside the file: 0x{offset:X}.");
                }

                if (i > 0 && offset <= mipOffsets[i - 1])
                {
                    throw new InvalidDataException(
                        $"Mip offsets are not strictly increasing at mip {i}: 0x{mipOffsets[i - 1]:X} -> 0x{offset:X}.");
                }
            }
        }

        private static void ValidateTopMipPayload(byte[] data, int offset, int width, int height, BcFormat format)
        {
            int requiredBytes = GetRequiredBytes(width, height, format);
            if (offset + requiredBytes > data.Length)
            {
                throw new InvalidDataException(
                    $"Top mip payload is truncated. Need {requiredBytes} bytes at 0x{offset:X}, file ends at 0x{data.Length:X}.");
            }
        }

        private static int GetRequiredBytes(int width, int height, BcFormat format)
        {
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = format switch
            {
                BcFormat.BC1 => 8,
                BcFormat.BC2 => 16,
                BcFormat.BC3 => 16,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            return checked(blocksX * blocksY * blockSize);
        }

        private static byte[] DecodeTopMip(byte[] data, int offset, int width, int height, BcFormat format)
        {
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = format switch
            {
                BcFormat.BC1 => 8,
                BcFormat.BC2 => 16,
                BcFormat.BC3 => 16,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            int requiredBytes = checked(blocksX * blocksY * blockSize);
            if (offset + requiredBytes > data.Length)
            {
                throw new InvalidDataException("Top mip payload is truncated.");
            }

            byte[] rgba = new byte[checked(width * height * 4)];
            byte[] blockRgba = new byte[16 * 4];
            int src = offset;

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    switch (format)
                    {
                        case BcFormat.BC1:
                            DecodeBc1Block(data, src, blockRgba);
                            src += 8;
                            break;
                        case BcFormat.BC2:
                            DecodeBc2Block(data, src, blockRgba);
                            src += 16;
                            break;
                        case BcFormat.BC3:
                            DecodeBc3Block(data, src, blockRgba);
                            src += 16;
                            break;
                    }

                    WriteBlockRgba(blockRgba, rgba, width, height, bx * 4, by * 4);
                }
            }

            return rgba;
        }

        private static void WriteBlockRgba(ReadOnlySpan<byte> blockRgba, byte[] output, int width, int height, int dstX, int dstY)
        {
            for (int py = 0; py < 4; py++)
            {
                int y = dstY + py;
                if (y >= height) break;

                for (int px = 0; px < 4; px++)
                {
                    int x = dstX + px;
                    if (x >= width) break;

                    int srcIndex = (py * 4 + px) * 4;
                    int dstIndex = (y * width + x) * 4;

                    output[dstIndex + 0] = blockRgba[srcIndex + 0];
                    output[dstIndex + 1] = blockRgba[srcIndex + 1];
                    output[dstIndex + 2] = blockRgba[srcIndex + 2];
                    output[dstIndex + 3] = blockRgba[srcIndex + 3];
                }
            }
        }

        private static void DecodeBc1Block(byte[] data, int offset, Span<byte> rgba)
        {
            ushort c0 = ReadUInt16LE(data, offset + 0);
            ushort c1 = ReadUInt16LE(data, offset + 2);
            uint bits = (uint)ReadInt32LE(data, offset + 4);

            Span<byte> colors = stackalloc byte[4 * 4];
            Unpack565(c0, colors, 0);
            colors[3] = 255;
            Unpack565(c1, colors, 4);
            colors[7] = 255;

            if (c0 > c1)
            {
                for (int i = 0; i < 3; i++)
                {
                    colors[8 + i] = (byte)((2 * colors[0 + i] + colors[4 + i]) / 3);
                    colors[12 + i] = (byte)((colors[0 + i] + 2 * colors[4 + i]) / 3);
                }
                colors[11] = 255;
                colors[15] = 255;
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    colors[8 + i] = (byte)((colors[0 + i] + colors[4 + i]) / 2);
                }
                colors[11] = 255;
                colors[12] = 0;
                colors[13] = 0;
                colors[14] = 0;
                colors[15] = 0;
            }

            for (int i = 0; i < 16; i++)
            {
                int colorIndex = (int)((bits >> (i * 2)) & 0x3) * 4;
                int dst = i * 4;
                rgba[dst + 0] = colors[colorIndex + 0];
                rgba[dst + 1] = colors[colorIndex + 1];
                rgba[dst + 2] = colors[colorIndex + 2];
                rgba[dst + 3] = colors[colorIndex + 3];
            }
        }

        private static void DecodeBc2Block(byte[] data, int offset, Span<byte> rgba)
        {
            DecodeBc1Block(data, offset + 8, rgba);

            for (int i = 0; i < 16; i++)
            {
                byte packed = data[offset + (i / 2)];
                int nibble = (i & 1) == 0 ? (packed & 0x0F) : (packed >> 4);
                rgba[i * 4 + 3] = (byte)(nibble * 17);
            }
        }

        private static void DecodeBc3Block(byte[] data, int offset, Span<byte> rgba)
        {
            Span<byte> alphaValues = stackalloc byte[8];
            byte a0 = data[offset + 0];
            byte a1 = data[offset + 1];
            BuildAlphaPalette(a0, a1, alphaValues);
            ulong alphaBits = Read48LE(data, offset + 2);

            DecodeBc1Block(data, offset + 8, rgba);

            for (int i = 0; i < 16; i++)
            {
                int alphaIndex = (int)((alphaBits >> (i * 3)) & 0x7);
                rgba[i * 4 + 3] = alphaValues[alphaIndex];
            }
        }

        private static void BuildAlphaPalette(byte a0, byte a1, Span<byte> palette)
        {
            palette[0] = a0;
            palette[1] = a1;

            if (a0 > a1)
            {
                palette[2] = (byte)((6 * a0 + 1 * a1) / 7);
                palette[3] = (byte)((5 * a0 + 2 * a1) / 7);
                palette[4] = (byte)((4 * a0 + 3 * a1) / 7);
                palette[5] = (byte)((3 * a0 + 4 * a1) / 7);
                palette[6] = (byte)((2 * a0 + 5 * a1) / 7);
                palette[7] = (byte)((1 * a0 + 6 * a1) / 7);
            }
            else
            {
                palette[2] = (byte)((4 * a0 + 1 * a1) / 5);
                palette[3] = (byte)((3 * a0 + 2 * a1) / 5);
                palette[4] = (byte)((2 * a0 + 3 * a1) / 5);
                palette[5] = (byte)((1 * a0 + 4 * a1) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }
        }

        private static void Unpack565(ushort value, Span<byte> dst, int dstOffset)
        {
            int r = (value >> 11) & 31;
            int g = (value >> 5) & 63;
            int b = value & 31;

            dst[dstOffset + 0] = (byte)(r * 255 / 31);
            dst[dstOffset + 1] = (byte)(g * 255 / 63);
            dst[dstOffset + 2] = (byte)(b * 255 / 31);
        }

        private static byte[] ReadAllBytes(Stream input)
        {
            if (input is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> segment))
            {
                byte[] copy = new byte[segment.Count];
                Buffer.BlockCopy(segment.Array!, segment.Offset, copy, 0, segment.Count);
                return copy;
            }

            using MemoryStream copyStream = new MemoryStream();
            input.CopyTo(copyStream);
            return copyStream.ToArray();
        }

        private static ushort ReadUInt16LE(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static int ReadInt32LE(byte[] data, int offset)
        {
            return data[offset]
                 | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16)
                 | (data[offset + 3] << 24);
        }

        private static ulong Read48LE(byte[] data, int offset)
        {
            return (ulong)data[offset + 0]
                 | ((ulong)data[offset + 1] << 8)
                 | ((ulong)data[offset + 2] << 16)
                 | ((ulong)data[offset + 3] << 24)
                 | ((ulong)data[offset + 4] << 32)
                 | ((ulong)data[offset + 5] << 40);
        }
    }
}
