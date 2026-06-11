using Microsoft.UI.Xaml;

namespace FixedEditor.App;

public partial class App : Application
{
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        window.Activate();
    }
}
