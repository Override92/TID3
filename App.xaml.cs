using System;
using System.Windows;
using TID3.Models;
using TID3.Services;
using TID3.Utils;
using TID3.Views;

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

            // Configure logging from user settings before anything else logs.
            // When logging is disabled no log directory or files are created.
            var settings = SettingsManager.LoadSettings();
            TID3Logger.Initialize(
                settings.EnableLogging,
                settings.EnableLogging ? LogLevel.Debug : LogLevel.Info);

            // Set up global exception handling
            DispatcherUnhandledException += (sender, args) =>
            {
                TID3Logger.Fatal("System", "Unhandled UI exception", args.Exception, component: "App");
                MessageBox.Show($"An unexpected error occurred: {args.Exception.Message}",
                              "TID3 Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                args.Handled = true;
            };

            // Set up unhandled exception handling for background threads
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                TID3Logger.Fatal("System", "Unhandled background-thread exception",
                    args.ExceptionObject as Exception, component: "App");
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