using PaintDotNet;

namespace MtfTexPaintDotNet
{
    // Paint.NET discovers FileType plugins via an IFileTypeFactory implementation.
    // If your target Paint.NET build prefers the single-class pattern instead,
    // you can also move GetFileTypeInstances() onto the FileType class itself.
    public sealed class MtfTexFileTypeFactory : IFileTypeFactory
    {
        public FileType[] GetFileTypeInstances()
        {
            return new FileType[] { new MtfTexFileType() };
        }
    }
}
