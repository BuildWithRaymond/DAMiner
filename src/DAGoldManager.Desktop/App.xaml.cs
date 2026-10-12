namespace DAGoldManager.Desktop;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
        var window = new Window(new MainPage()) { Title = "DAMiner", Width = 1280, Height = 880, MinimumWidth = 760, MinimumHeight = 600 };
        // Unpackaged Windows apps need an explicit title-bar/taskbar icon.
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                nativeWindow.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "DAMiner.ico"));
        };
        return window;
	}
}
