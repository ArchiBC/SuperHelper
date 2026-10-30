using Eto.Forms;

namespace SuperHelper;

internal sealed class ObjectInfoPanel : Panel
{
    public ObjectInfoPanel()
    {
        SizeChanged += (_, _) => FitContent();
        Load += (_, _) => FitContent();
    }

    public void SetObject(HelpObject obj)
    {
        var previous = Content;
        Content = obj == null ? null : obj.BuildParameterInfo?.Invoke();
        previous?.Dispose();
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
