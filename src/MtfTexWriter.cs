using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    internal readonly struct Re5ResolvedSaveSettings
    {
        public Re5ResolvedSaveSettings(Re5CompressionMode compression, bool generateMipmaps, bool forceOpaque, MtfMipResamplingAlgorithm mipResampling)
        {
            Compression = compression;
            GenerateMipmaps = generateMipmaps;
            ForceOpaque = forceOpaque;
            MipResampling = mipResampling;
        }

        public Re5CompressionMode Compression { get; }
        public bool GenerateMipmaps { get; }
        public bool ForceOpaque { get; }
        public MtfMipResamplingAlgorithm MipResampling { get; }
    }

    internal static class Re5SaveDefaults
    {
        public static Re5ResolvedSaveSettings Resolve(MtfTexSaveConfigToken? token)
        {
            token ??= new MtfTexSaveConfigToken();

            return new Re5ResolvedSaveSettings(
                token.Compression,
                token.GenerateMipmaps,
                token.AlphaMode == Re5AlphaMode.ForceOpaque,
                token.MipResampling);
        }
    }

    internal static class MtfTexWriter
    {


        public static void WriteRe6Pc(Stream output, int width, int height, byte[] rgba32, Re5ResolvedSaveSettings settings)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (rgba32 == null) throw new ArgumentNullException(nameof(rgba32));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba32.Length != checked(width * height * 4))
            {
                throw new ArgumentException("RGBA buffer size does not match image dimensions.", nameof(rgba32));
            }

            byte[] preparedRgba = settings.ForceOpaque ? MakeOpaqueCopy(rgba32) : rgba32;

            Re5CompressionMode chosenCompression = settings.Compression;
            if (chosenCompression == Re5CompressionMode.Auto)
            {
                chosenCompression = NeedsAlpha(preparedRgba) ? Re5CompressionMode.Dxt5 : Re5CompressionMode.Dxt1;
            }

            // RE6 common 2D save path supports BC1, BC3, BC5, and RGBA8.
            // DXT3 is coerced to BC3 because RE6 does not use a DXT3-tagged path here.
            if (chosenCompression == Re5CompressionMode.Dxt3)
            {
                chosenCompression = Re5CompressionMode.Dxt5;
            }

            uint formatCode = chosenCompression switch
            {
                Re5CompressionMode.Dxt5 => 0x00011801u,
                Re5CompressionMode.Rgba8 => 0x00012801u,
                Re5CompressionMode.Bc5 => 0x00011F01u,
                _ => 0x00011401u,
            };

            BcFormat format = chosenCompression switch
            {
                Re5CompressionMode.Dxt5 => BcFormat.BC3,
                Re5CompressionMode.Rgba8 => BcFormat.RGBA8,
                Re5CompressionMode.Bc5 => BcFormat.BC5,
                _ => BcFormat.BC1,
            };

            if (format != BcFormat.RGBA8 && (width & 3) != 0)
            {
                throw new NotSupportedException("RE6 BC-compressed TEX save currently requires a width divisible by 4. Use RGBA8 for NPOT/raw output.");
            }

            List<MipLevel> mips = BuildMipChain(width, height, preparedRgba, settings.GenerateMipmaps, settings.MipResampling);
            List<byte[]> mipPayloads = new List<byte[]>(mips.Count);
            foreach (MipLevel mip in mips)
            {
                mipPayloads.Add(EncodeMip(mip.Width, mip.Height, mip.Rgba32, format));
            }

            int headerSize = 0x14 + Math.Max(0, mips.Count - 1) * 4;
            List<int> mipOffsets = new List<int>(mips.Count);
            int runningOffset = headerSize;
            foreach (byte[] payload in mipPayloads)
            {
                mipOffsets.Add(runningOffset);
                runningOffset += payload.Length;
            }

            int packedDims = PackRe6Dims(width, height);

            using BinaryWriter bw = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);

            bw.Write((byte)'T');
            bw.Write((byte)'E');
            bw.Write((byte)'X');
            bw.Write((byte)0);
            bw.Write((byte)0x9A);
            bw.Write((byte)0x00);
            bw.Write((byte)0x00);
            bw.Write((byte)0x20);
            bw.Write((byte)mips.Count);
            bw.Write((byte)(packedDims & 0xFF));
            bw.Write((byte)((packedDims >> 8) & 0xFF));
            bw.Write((byte)((packedDims >> 16) & 0xFF));
            bw.Write(formatCode);
            bw.Write(headerSize);

            for (int i = 1; i < mipOffsets.Count; i++)
            {
                bw.Write(mipOffsets[i]);
            }

            foreach (byte[] payload in mipPayloads)
            {
                bw.Write(payload);
            }
        }

        public static void WriteRe5Pc(Stream output, int width, int height, byte[] rgba32, Re5ResolvedSaveSettings settings)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (rgba32 == null) throw new ArgumentNullException(nameof(rgba32));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba32.Length != checked(width * height * 4))
            {
                throw new ArgumentException("RGBA buffer size does not match image dimensions.", nameof(rgba32));
            }

            byte[] preparedRgba = settings.ForceOpaque ? MakeOpaqueCopy(rgba32) : rgba32;

            Re5CompressionMode chosenCompression = settings.Compression;
            if (chosenCompression == Re5CompressionMode.Auto)
            {
                chosenCompression = NeedsAlpha(preparedRgba) ? Re5CompressionMode.Dxt5 : Re5CompressionMode.Dxt1;
            }

            string tag = chosenCompression switch
            {
                Re5CompressionMode.Dxt3 => "DXT3",
                Re5CompressionMode.Dxt5 => "DXT5",
                _ => "DXT1",
            };

            BcFormat format = chosenCompression switch
            {
                Re5CompressionMode.Dxt3 => BcFormat.BC2,
                Re5CompressionMode.Dxt5 => BcFormat.BC3,
                _ => BcFormat.BC1,
            };

            List<MipLevel> mips = BuildMipChain(width, height, preparedRgba, settings.GenerateMipmaps, settings.MipResampling);
            List<byte[]> mipPayloads = new List<byte[]>(mips.Count);
            foreach (MipLevel mip in mips)
            {
                mipPayloads.Add(EncodeMip(mip.Width, mip.Height, mip.Rgba32, format));
            }

            int headerSize = 0x28 + mips.Count * 4;
            List<int> mipOffsets = new List<int>(mips.Count);
            int runningOffset = headerSize;
            foreach (byte[] payload in mipPayloads)
            {
                mipOffsets.Add(runningOffset);
                runningOffset += payload.Length;
            }

            using BinaryWriter bw = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);

            bw.Write((byte)'T');
            bw.Write((byte)'E');
            bw.Write((byte)'X');
            bw.Write((byte)0);
            bw.Write((byte)0x70);
            bw.Write((byte)0x00);
            bw.Write((byte)0x22);
            bw.Write((byte)0x00);
            bw.Write((byte)mips.Count);
            bw.Write((byte)0x01);
            bw.Write((ushort)0x0001);
            bw.Write((ushort)width);
            bw.Write((ushort)height);
            bw.Write(0u);
            bw.Write(Encoding.ASCII.GetBytes(tag));
            bw.Write(1.0f);
            bw.Write(1.0f);
            bw.Write(1.0f);
            bw.Write(1.0f);

            foreach (int mipOffset in mipOffsets)
            {
                bw.Write(mipOffset);
            }

            foreach (byte[] payload in mipPayloads)
            {
                bw.Write(payload);
            }
        }

        private static byte[] MakeOpaqueCopy(byte[] rgba32)
        {
            byte[] copy = new byte[rgba32.Length];
            Buffer.BlockCopy(rgba32, 0, copy, 0, rgba32.Length);
            for (int i = 3; i < copy.Length; i += 4)
            {
                copy[i] = 255;
            }
            return copy;
        }

        private static bool NeedsAlpha(byte[] rgba32)
        {
            for (int i = 3; i < rgba32.Length; i += 4)
            {
                if (rgba32[i] != 255)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<MipLevel> BuildMipChain(int width, int height, byte[] rgba32, bool generateMipmaps, MtfMipResamplingAlgorithm mipResampling)
        {
            List<MipLevel> mips = new List<MipLevel>();
            mips.Add(new MipLevel(width, height, rgba32));

            if (!generateMipmaps)
            {
                return mips;
            }

            using Surface sourceSurface = SurfaceFromRgba(width, height, rgba32);
            using Surface? opaqueSourceSurface = HasTransparency(rgba32) ? SurfaceFromRgba(width, height, MakeOpaqueCopy(rgba32)) : null;

            int w = width;
            int h = height;
            while (w > 1 || h > 1)
            {
                int nextW = Math.Max(1, w / 2);
                int nextH = Math.Max(1, h / 2);
                byte[] next = ResizeRgba(sourceSurface, opaqueSourceSurface, nextW, nextH, mipResampling);
                mips.Add(new MipLevel(nextW, nextH, next));
                w = nextW;
                h = nextH;
            }

            return mips;
        }

        private static bool HasTransparency(byte[] rgba32)
        {
            for (int i = 3; i < rgba32.Length; i += 4)
            {
                if (rgba32[i] != 255)
                {
                    return true;
                }
            }

            return false;
        }

        private static Surface SurfaceFromRgba(int width, int height, byte[] rgba32)
        {
            Surface surface = new Surface(width, height);
            int src = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = rgba32[src + 0];
                    byte g = rgba32[src + 1];
                    byte b = rgba32[src + 2];
                    byte a = rgba32[src + 3];
                    surface[x, y] = ColorBgra.FromBgra(b, g, r, a);
                    src += 4;
                }
            }
            return surface;
        }

        private static byte[] RgbaFromSurface(Surface surface)
        {
            byte[] rgba = new byte[checked(surface.Width * surface.Height * 4)];
            int dst = 0;
            for (int y = 0; y < surface.Height; y++)
            {
                for (int x = 0; x < surface.Width; x++)
                {
                    ColorBgra c = surface[x, y];
                    rgba[dst + 0] = c.R;
                    rgba[dst + 1] = c.G;
                    rgba[dst + 2] = c.B;
                    rgba[dst + 3] = c.A;
                    dst += 4;
                }
            }
            return rgba;
        }

        private static byte[] ResizeRgba(Surface sourceSurface, Surface? opaqueSourceSurface, int width, int height, MtfMipResamplingAlgorithm mipResampling)
        {
            ResamplingAlgorithm algorithm = ToPaintDotNetResampling(mipResampling);
            const FitSurfaceOptions options = FitSurfaceOptions.Default;

            using Surface mipSurface = new Surface(width, height);
            mipSurface.FitSurface(algorithm, sourceSurface, options);

            if (opaqueSourceSurface != null)
            {
                using Surface colorSurface = new Surface(width, height);
                colorSurface.FitSurface(algorithm, opaqueSourceSurface, options);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        ColorBgra color = colorSurface[x, y];
                        ColorBgra alpha = mipSurface[x, y];
                        mipSurface[x, y] = ColorBgra.FromBgra(color.B, color.G, color.R, alpha.A);
                    }
                }
            }

            return RgbaFromSurface(mipSurface);
        }

        private static ResamplingAlgorithm ToPaintDotNetResampling(MtfMipResamplingAlgorithm mipResampling)
        {
            return mipResampling switch
            {
                MtfMipResamplingAlgorithm.CubicSmooth => ResamplingAlgorithm.CubicSmooth,
                MtfMipResamplingAlgorithm.Linear => ResamplingAlgorithm.Linear,
                MtfMipResamplingAlgorithm.LinearLowQuality => ResamplingAlgorithm.LinearLowQuality,
                MtfMipResamplingAlgorithm.AdaptiveHighQuality => ResamplingAlgorithm.AdaptiveHighQuality,
                MtfMipResamplingAlgorithm.Lanczos3 => ResamplingAlgorithm.Lanczos3,
                MtfMipResamplingAlgorithm.Fant => ResamplingAlgorithm.Fant,
                MtfMipResamplingAlgorithm.NearestNeighbor => ResamplingAlgorithm.NearestNeighbor,
                _ => ResamplingAlgorithm.Cubic,
            };
        }

        private static byte[] EncodeMip(int width, int height, byte[] rgba32, BcFormat format)
        {
            if (format == BcFormat.RGBA8)
            {
                byte[] raw = new byte[rgba32.Length];
                Buffer.BlockCopy(rgba32, 0, raw, 0, rgba32.Length);
                return raw;
            }

            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = (format == BcFormat.BC1 || format == BcFormat.BC4) ? 8 : 16;
            byte[] output = new byte[checked(blocksX * blocksY * blockSize)];

            byte[] block = new byte[16 * 4];
            int dst = 0;
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    GatherBlockRgba(rgba32, width, height, bx * 4, by * 4, block);
                    switch (format)
                    {
                        case BcFormat.BC1:
                            EncodeBc1Block(block, output, dst);
                            dst += 8;
                            break;
                        case BcFormat.BC2:
                            EncodeBc2Block(block, output, dst);
                            dst += 16;
                            break;
                        case BcFormat.BC3:
                            EncodeBc3Block(block, output, dst);
                            dst += 16;
                            break;
                        case BcFormat.BC5:
                            EncodeBc5Block(block, output, dst);
                            dst += 16;
                            break;
                    }
                }
            }

            return output;
        }

        private static void GatherBlockRgba(byte[] src, int width, int height, int srcX, int srcY, byte[] block)
        {
            for (int py = 0; py < 4; py++)
            {
                int y = Math.Min(height - 1, srcY + py);
                for (int px = 0; px < 4; px++)
                {
                    int x = Math.Min(width - 1, srcX + px);
                    int srcIndex = (y * width + x) * 4;
                    int dstIndex = (py * 4 + px) * 4;
                    block[dstIndex + 0] = src[srcIndex + 0];
                    block[dstIndex + 1] = src[srcIndex + 1];
                    block[dstIndex + 2] = src[srcIndex + 2];
                    block[dstIndex + 3] = src[srcIndex + 3];
                }
            }
        }

        private static void EncodeBc2Block(byte[] blockRgba, byte[] output, int outputOffset)
        {
            for (int i = 0; i < 16; i += 2)
            {
                byte a0 = (byte)((blockRgba[i * 4 + 3] + 8) / 17);
                byte a1 = (byte)((blockRgba[(i + 1) * 4 + 3] + 8) / 17);
                output[outputOffset + (i / 2)] = (byte)((a1 << 4) | (a0 & 0x0F));
            }

            EncodeBc1Block(blockRgba, output, outputOffset + 8);
        }

        private static void EncodeBc3Block(byte[] blockRgba, byte[] output, int outputOffset)
        {
            EncodeAlphaBlock(blockRgba, output, outputOffset);
            EncodeBc1Block(blockRgba, output, outputOffset + 8);
        }

        private static void EncodeBc5Block(byte[] blockRgba, byte[] output, int outputOffset)
        {
            EncodeBc4ChannelBlock(blockRgba, 0, output, outputOffset + 0);
            EncodeBc4ChannelBlock(blockRgba, 1, output, outputOffset + 8);
        }

        private static void EncodeBc4ChannelBlock(byte[] blockRgba, int channelOffset, byte[] output, int outputOffset)
        {
            byte minV = 255;
            byte maxV = 0;
            Span<byte> values = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                byte v = blockRgba[i * 4 + channelOffset];
                values[i] = v;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            byte a0 = maxV;
            byte a1 = minV;
            Span<byte> palette = stackalloc byte[8];
            BuildAlphaPalette(a0, a1, palette);

            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0;
                int bestDist = int.MaxValue;
                int v = values[i];
                for (int p = 0; p < 8; p++)
                {
                    int dist = Math.Abs(v - palette[p]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = p;
                    }
                }
                bits |= ((ulong)best & 0x7UL) << (i * 3);
            }

            output[outputOffset + 0] = a0;
            output[outputOffset + 1] = a1;
            for (int i = 0; i < 6; i++)
            {
                output[outputOffset + 2 + i] = (byte)((bits >> (i * 8)) & 0xFF);
            }
        }

        private static void EncodeAlphaBlock(byte[] blockRgba, byte[] output, int outputOffset)
        {
            byte minA = 255;
            byte maxA = 0;
            Span<byte> alphas = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                byte a = blockRgba[i * 4 + 3];
                alphas[i] = a;
                if (a < minA) minA = a;
                if (a > maxA) maxA = a;
            }

            byte a0 = maxA;
            byte a1 = minA;
            Span<byte> palette = stackalloc byte[8];
            BuildAlphaPalette(a0, a1, palette);

            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0;
                int bestDist = int.MaxValue;
                int a = alphas[i];
                for (int p = 0; p < 8; p++)
                {
                    int dist = Math.Abs(a - palette[p]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = p;
                    }
                }
                bits |= ((ulong)best & 0x7UL) << (i * 3);
            }

            output[outputOffset + 0] = a0;
            output[outputOffset + 1] = a1;
            for (int i = 0; i < 6; i++)
            {
                output[outputOffset + 2 + i] = (byte)((bits >> (i * 8)) & 0xFF);
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

        private static void EncodeBc1Block(byte[] blockRgba, byte[] output, int outputOffset)
        {
            ushort c0 = 0;
            ushort c1 = 0;
            int bestDistance = int.MaxValue;

            ushort[] candidates = CollectColorCandidates(blockRgba);
            for (int i = 0; i < candidates.Length; i++)
            {
                ushort a = candidates[i];
                for (int j = i; j < candidates.Length; j++)
                {
                    ushort b = candidates[j];
                    EvaluateBc1Pair(blockRgba, a, b, ref c0, ref c1, ref bestDistance);
                    EvaluateBc1Pair(blockRgba, b, a, ref c0, ref c1, ref bestDistance);
                }
            }

            if (c0 < c1)
            {
                (c0, c1) = (c1, c0);
            }

            Span<(int R, int G, int B)> palette = stackalloc (int R, int G, int B)[4];
            BuildBc1Palette(c0, c1, palette);

            uint indices = 0;
            for (int i = 0; i < 16; i++)
            {
                int r = blockRgba[i * 4 + 0];
                int g = blockRgba[i * 4 + 1];
                int b = blockRgba[i * 4 + 2];
                int best = 0;
                int bestDist = int.MaxValue;
                for (int p = 0; p < 4; p++)
                {
                    int dr = r - palette[p].R;
                    int dg = g - palette[p].G;
                    int db = b - palette[p].B;
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = p;
                    }
                }
                indices |= (uint)best << (i * 2);
            }

            output[outputOffset + 0] = (byte)(c0 & 0xFF);
            output[outputOffset + 1] = (byte)(c0 >> 8);
            output[outputOffset + 2] = (byte)(c1 & 0xFF);
            output[outputOffset + 3] = (byte)(c1 >> 8);
            output[outputOffset + 4] = (byte)(indices & 0xFF);
            output[outputOffset + 5] = (byte)((indices >> 8) & 0xFF);
            output[outputOffset + 6] = (byte)((indices >> 16) & 0xFF);
            output[outputOffset + 7] = (byte)((indices >> 24) & 0xFF);
        }

        private static ushort[] CollectColorCandidates(byte[] blockRgba)
        {
            HashSet<ushort> set = new HashSet<ushort>();
            for (int i = 0; i < 16; i++)
            {
                int r = blockRgba[i * 4 + 0];
                int g = blockRgba[i * 4 + 1];
                int b = blockRgba[i * 4 + 2];
                set.Add(PackRgb565(r, g, b));
            }

            ushort[] values = new ushort[set.Count];
            set.CopyTo(values);
            return values;
        }

        private static void EvaluateBc1Pair(byte[] blockRgba, ushort first, ushort second, ref ushort best0, ref ushort best1, ref int bestDistance)
        {
            Span<(int R, int G, int B)> palette = stackalloc (int R, int G, int B)[4];
            BuildBc1Palette(first, second, palette);

            int total = 0;
            for (int i = 0; i < 16; i++)
            {
                int r = blockRgba[i * 4 + 0];
                int g = blockRgba[i * 4 + 1];
                int b = blockRgba[i * 4 + 2];
                int localBest = int.MaxValue;
                for (int p = 0; p < 4; p++)
                {
                    int dr = r - palette[p].R;
                    int dg = g - palette[p].G;
                    int db = b - palette[p].B;
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist < localBest)
                    {
                        localBest = dist;
                    }
                }
                total += localBest;
                if (total >= bestDistance)
                {
                    return;
                }
            }

            bestDistance = total;
            best0 = first;
            best1 = second;
        }

        private static void BuildBc1Palette(ushort c0, ushort c1, Span<(int R, int G, int B)> palette)
        {
            Unpack565(c0, out int r0, out int g0, out int b0);
            Unpack565(c1, out int r1, out int g1, out int b1);

            palette[0] = (r0, g0, b0);
            palette[1] = (r1, g1, b1);
            palette[2] = ((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3);
            palette[3] = ((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3);
        }

        private static ushort PackRgb565(int r, int g, int b)
        {
            int r5 = (r * 31 + 127) / 255;
            int g6 = (g * 63 + 127) / 255;
            int b5 = (b * 31 + 127) / 255;
            return (ushort)((r5 << 11) | (g6 << 5) | b5);
        }

        private static void Unpack565(ushort value, out int r, out int g, out int b)
        {
            r = ((value >> 11) & 31) * 255 / 31;
            g = ((value >> 5) & 63) * 255 / 63;
            b = (value & 31) * 255 / 31;
        }



        private static int PackRe6Dims(int width, int height)
        {
            if ((width & 3) != 0)
            {
                throw new NotSupportedException("RE6 TEX width must be divisible by 4.");
            }

            int widthUnits = width / 4;
            if ((widthUnits & ~0x7FF) != 0)
            {
                throw new NotSupportedException($"RE6 TEX width {width} is out of range.");
            }
            if ((height & ~0x1FFF) != 0)
            {
                throw new NotSupportedException($"RE6 TEX height {height} is out of range.");
            }

            return widthUnits | (height << 11);
        }

        private readonly struct MipLevel
        {
            public MipLevel(int width, int height, byte[] rgba32)
            {
                Width = width;
                Height = height;
                Rgba32 = rgba32;
            }

            public int Width { get; }
            public int Height { get; }
            public byte[] Rgba32 { get; }
        }
    }
}
