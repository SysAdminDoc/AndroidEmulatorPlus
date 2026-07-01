using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AndroidEmulatorPlus.Services;

public sealed class ToastService
{
    private const string AppId = "AndroidEmulatorPlus";

    public void Show(string title, string body)
    {
        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = xml.GetElementsByTagName("text");
            textNodes[0].AppendChild(xml.CreateTextNode(title));
            textNodes[1].AppendChild(xml.CreateTextNode(body));
            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch
        {
            // Toast failures are non-fatal — the user still gets the log panel.
        }
    }
}
