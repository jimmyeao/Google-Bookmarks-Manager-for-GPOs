using Serilog;
using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        #region Private Fields

        private ResourceDictionary darkTheme;
        private ResourceDictionary lightTheme;

        #endregion Private Fields

        #region Public Constructors
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()  // Log to console for debugging
                .WriteTo.File("logs/bookmark-import-log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Application started.");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application exited.");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
        public App()
        {
        }

        #endregion Public Constructors

        #region Private Methods

        private void ToggleDarkModeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTheme();
        }

        private void ToggleTheme()
        {
            if (Resources.MergedDictionaries.Contains(lightTheme))
            {
                Resources.MergedDictionaries.Remove(lightTheme);
                Resources.MergedDictionaries.Add(darkTheme);
            }
            else
            {
                Resources.MergedDictionaries.Remove(darkTheme);
                Resources.MergedDictionaries.Add(lightTheme);
            }
        }

        #endregion Private Methods
    }
}