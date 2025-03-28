﻿using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class CustomMessageBox : Window
    {

        #region Constructors

        public CustomMessageBox(string message, string caption, MessageBoxButton buttons)
        {
            InitializeComponent();

            Title = caption;
            MessageTextBlock.Text = message;

            if (buttons == MessageBoxButton.OK)
            {
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        #endregion Constructors

        #region Properties

        public MessageBoxResult Result { get; private set; }

        #endregion Properties

        #region Methods

        public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
        {
            var messageBox = new CustomMessageBox(message, caption, buttons);

            // Center relative to MainWindow
            if (Application.Current.MainWindow != null)
            {
                messageBox.Owner = Application.Current.MainWindow; // Set owner to main window
                messageBox.WindowStartupLocation = WindowStartupLocation.CenterOwner; // Center on owner
            }
            else
            {
                messageBox.WindowStartupLocation = WindowStartupLocation.CenterScreen; // Fallback if no main window
            }

            messageBox.ShowDialog(); // Show it modally
            return messageBox.Result; // Return the result (OK or Cancel)
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        #endregion Methods

    }
}
