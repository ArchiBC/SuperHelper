using Eto.Forms;
using Grasshopper.Kernel;

namespace SuperHelper;

internal sealed class ObjectInfoPanel : Panel
{
    public ObjectInfoPanel()
    {
        SizeChanged += (_, _) => FitContent();
        Load += (_, _) => FitContent();
    }

    public void SetObject(GH_DocumentObject obj)
    {
        Content = obj == null ? null : DocumentObjectInfoBuilder.Build(obj);
        FitContent();
    }

    private void FitContent()
    {
        if (Content != null &&
            ClientSize.Width > 0 &&
            ClientSize.Height > 0 &&
            Content.Size != ClientSize)
        {
            Content.Size = ClientSize;
        }
    }
}
