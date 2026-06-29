using System;
using System.IO;

namespace ModelDownloader.Controls;

public class ModelSource : IDisposable
{
    public Stream ObjStream { get; }
    public Stream MtlStream { get; }

    private ModelSource(Stream objStream, Stream mtlStream)
    {
        ObjStream = objStream ?? throw new ArgumentNullException(nameof(objStream));
        MtlStream = mtlStream;
    }

    public static ModelSource FromStreams(Stream objStream, Stream mtlStream)
    {
        return new ModelSource(objStream, mtlStream);
    }

    public void Dispose()
    {
        ObjStream?.Dispose();
        MtlStream?.Dispose();
    }
}
