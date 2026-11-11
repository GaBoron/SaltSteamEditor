using System.IO;
using System.Reflection;
using System.Text;

namespace SaltSteamEditor;

public partial class AboutWindow : Wpf.Ui.Controls.FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        var notices = new StringBuilder();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Notices.", StringComparison.Ordinal)).Order())
        {
            using var resource = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(resource);
            notices.AppendLine(name["Notices.".Length..]);
            notices.AppendLine(new string('─', 48));
            notices.AppendLine(reader.ReadToEnd());
            notices.AppendLine();
        }
        NoticeText.Text = notices.ToString();
    }
}
