﻿using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class CustomMessageBox : Window
    {
        #region Public Constructors

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

        #endregion Public Constructors

        #region Public Properties

        public MessageBoxResult Result { get; private set; }

        #endregion Public Properties

        #region Private Methods

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
        public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
        {
            var messageBox = new CustomMessageBox(message, caption, buttons);
            messageBox.ShowDialog(); // Show it modally
            return messageBox.Result; // Return the result (OK or Cancel)
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        #endregion Private Methods
    }
}