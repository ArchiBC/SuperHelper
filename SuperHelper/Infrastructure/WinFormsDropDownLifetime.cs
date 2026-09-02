using System;
using System.Windows.Forms;

namespace SuperHelper;

internal static class WinFormsDropDownLifetime
{
    public static void DisposeOnIdleAfterClosed(ToolStripDropDown dropDown)
    {
        ArgumentNullException.ThrowIfNull(dropDown);

        var disposalScheduled = false;
        dropDown.Closed += (_, _) =>
        {
            if (disposalScheduled)
            {
                return;
            }

            disposalScheduled = true;
            EventHandler disposeOnIdle = null;
            disposeOnIdle = (_, _) =>
            {
                Application.Idle -= disposeOnIdle;
                if (!dropDown.IsDisposed)
                {
                    dropDown.Dispose();
                }
            };
            Application.Idle += disposeOnIdle;
        };
    }
}
