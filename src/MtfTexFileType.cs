using System;
using System.IO;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    public sealed class MtfTexFileType : FileType
    {
        public MtfTexFileType()
            : base(
                "MT Framework TEX (RE5 PC)",
                new FileTypeOptions
                {
                    LoadExtensions = new[] { ".tex" },
                    SaveExtensions = new[] { ".tex" },
                    SupportsCancellation = true,
                    SupportsLayers = false,
                })
        {
        }

        public override SaveConfigWidget CreateSaveConfigWidget()
        {
            return new MtfTexSaveConfigWidget();
        }

        protected override SaveConfigToken OnCreateDefaultSaveConfigToken()
        {
            return new MtfTexSaveConfigToken();
        }

        protected override Document OnLoad(Stream input)
        {
            DecodedTex decoded = MtfTexReader.Read(input);

            Surface surface = new Surface(decoded.Width, decoded.Height);
            byte[] rgba = decoded.Rgba32;

            int src = 0;
            for (int y = 0; y < decoded.Height; y++)
            {
                for (int x = 0; x < decoded.Width; x++)
                {
                    byte r = rgba[src + 0];
                    byte g = rgba[src + 1];
                    byte b = rgba[src + 2];
                    byte a = rgba[src + 3];
                    surface[x, y] = ColorBgra.FromBgra(b, g, r, a);
                    src += 4;
                }
            }

            BitmapLayer layer = new BitmapLayer(surface, true)
            {
                Name = "RE5 TEX"
            };

            Document document = new Document(decoded.Width, decoded.Height);
            document.Layers.Add(layer);
            return document;
        }

        protected override void OnSave(Document input, Stream output, SaveConfigToken token, Surface scratchSurface, ProgressEventHandler progressCallback)
        {
            input.Flatten(scratchSurface);

            int width = input.Width;
            int height = input.Height;
            byte[] rgba = new byte[checked(width * height * 4)];

            int dst = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ColorBgra c = scratchSurface[x, y];
                    rgba[dst + 0] = c.R;
                    rgba[dst + 1] = c.G;
                    rgba[dst + 2] = c.B;
                    rgba[dst + 3] = c.A;
                    dst += 4;
                }

                progressCallback?.Invoke(this, new ProgressEventArgs(((double)(y + 1) / height) * 75.0));
            }

            MtfTexSaveConfigToken typedToken = token as MtfTexSaveConfigToken ?? new MtfTexSaveConfigToken();
            Re5ResolvedSaveSettings resolved = Re5SaveDefaults.Resolve(typedToken);
            MtfTexWriter.WriteRe5Pc(output, width, height, rgba, resolved);

            progressCallback?.Invoke(this, new ProgressEventArgs(100.0));
        }
    }
}
