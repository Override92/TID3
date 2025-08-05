using System;
using System.Windows;

namespace TID3
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"An unexpected error occurred: {args.Exception.Message}",
                              "TID3 Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                args.Handled = true;
            };

            // Set up unhandled exception handling for background threads
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                MessageBox.Show($"A critical error occurred: {args.ExceptionObject}",
                              "TID3 Critical Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup code if needed
            base.OnExit(e);
        }
    }
}