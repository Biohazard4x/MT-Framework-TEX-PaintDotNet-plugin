using System;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    public enum Re5SaveProfile
    {
        Generic = 0,
        BM = 1,
        MM = 2,
        NM = 3,
    }

    public enum Re5CompressionMode
    {
        Auto = 0,
        Dxt1 = 1,
        Dxt3 = 2,
        Dxt5 = 3,
    }

    public enum Re5AlphaMode
    {
        Preserve = 0,
        ForceOpaque = 1,
    }

    [Serializable]
    public sealed class MtfTexSaveConfigToken : SaveConfigToken
    {
        public Re5SaveProfile Profile { get; set; }
        public Re5CompressionMode Compression { get; set; }
        public bool GenerateMipmaps { get; set; }
        public Re5AlphaMode AlphaMode { get; set; }

        public MtfTexSaveConfigToken()
        {
            Profile = Re5SaveProfile.Generic;
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
                Profile = Re5SaveProfile.Generic;
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
