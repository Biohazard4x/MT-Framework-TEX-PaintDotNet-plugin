using System;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    public enum MtfGameProfile
    {
        RE5 = 0,
        RE6 = 1,
    }

    public enum Re5CompressionMode
    {
        Auto = 0,
        Dxt1 = 1,
        Dxt3 = 2,
        Dxt5 = 3,
        Rgba8 = 4,
        Bc5 = 5,
    }

    public enum Re5AlphaMode
    {
        Preserve = 0,
        ForceOpaque = 1,
    }

    [Serializable]
    public sealed class MtfTexSaveConfigToken : SaveConfigToken
    {
        public MtfGameProfile Profile { get; set; }
        public Re5CompressionMode Compression { get; set; }
        public bool GenerateMipmaps { get; set; }
        public Re5AlphaMode AlphaMode { get; set; }

        public MtfTexSaveConfigToken()
        {
            Profile = MtfGameProfile.RE5;
            Compression = Re5CompressionMode.Auto;
            GenerateMipmaps = true;
            AlphaMode = Re5AlphaMode.Preserve;
        }

        public MtfTexSaveConfigToken(MtfTexSaveConfigToken copyMe)
            : base(copyMe)
        {
            ArgumentNullException.ThrowIfNull(copyMe);

            Profile = copyMe.Profile;
            Compression = copyMe.Compression;
            GenerateMipmaps = copyMe.GenerateMipmaps;
            AlphaMode = copyMe.AlphaMode;
        }

        public override object Clone()
        {
            return new MtfTexSaveConfigToken(this);
        }

        public override void Validate()
        {
            base.Validate();

            if (!Enum.IsDefined(Profile))
            {
                Profile = MtfGameProfile.RE5;
            }

            if (!Enum.IsDefined(Compression))
            {
                Compression = Re5CompressionMode.Auto;
            }

            if (!Enum.IsDefined(AlphaMode))
            {
                AlphaMode = Re5AlphaMode.Preserve;
            }
        }
    }
}
