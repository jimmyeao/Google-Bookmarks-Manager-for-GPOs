using System;
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

        public App()
        {
            darkTheme = new ResourceDictionary { Source = new Uri("DarkTheme.xaml", UriKind.Relative) };
            lightTheme = new ResourceDictionary { Source = new Uri("LightTheme.xaml", UriKind.Relative) };
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