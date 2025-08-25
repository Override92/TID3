using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Microsoft.Win32;
using TID3.Models;
using TID3.Services;
using TID3.Utils;
using System.Globalization;
using System.Windows.Data;

namespace TID3.Views
{
    public class BooleanInverterConverter : IValueConverter
    {
        public static BooleanInverterConverter Instance { get; } = new BooleanInverterConverter();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return false;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;
            return true;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Win32 API for Dark Mode Title Bar
        
        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        
        private static void SetDarkTitleBar(IntPtr handle)
        {
            try
            {
                var darkMode = 1; // 1 = dark, 0 = light
                
                // Try Windows 11/newer Windows 10 attribute first
                int result = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                
                // If that fails, try the older attribute for Windows 10 versions before 20H1
                if (result != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                }
            }
            catch
            {
                // Silently fail if dark title bar isn't supported
                // Application will continue to work normally
            }
        }
        
        #endregion
        
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply dark title bar theme
            try
            {
                if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
                {
                    SetDarkTitleBar(hwndSource.Handle);
                }
            }
            catch (Exception ex)
            {
                // Silent fail - dark title bar is not critical
                System.Diagnostics.Debug.WriteLine($"Failed to apply dark title bar: {ex.Message}");
            }
        }
        
        private readonly TagService _tagService;
        private readonly MusicBrainzService _musicBrainzService;
        private readonly DiscogsService _discogsService;
        private readonly UpdateService _updateService;
        private readonly OnlineSourceCacheManager _cacheManager;
        private readonly CoverArtService _coverArtService;
        private readonly ObservableCollection<AudioFileInfo> _audioFiles;
        private readonly ObservableCollection<OnlineSourceItem> _onlineSourceItems;
        private readonly ObservableCollection<AlbumGroup> _hierarchicalItems;
        private readonly ObservableCollection<CoverSource> _batchAvailableCovers = new();
        private string _selectedBatchCoverSource = "";

        public ObservableCollection<AudioFileInfo> AudioFiles => _audioFiles;
        public ObservableCollection<OnlineSourceItem> OnlineSourceItems => _onlineSourceItems;
        public ObservableCollection<AlbumGroup> HierarchicalItems => _hierarchicalItems;

        private AudioFileInfo? _selectedFile;
        public AudioFileInfo? SelectedFile
        {
            get => _selectedFile;
            set
            {
                // Unsubscribe from previous file's events
                if (_selectedFile != null)
                {
                    _selectedFile.PropertyChanged -= SelectedFile_PropertyChanged;
                }

                _selectedFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFileSelected));
                
                // Load cached online source results for the selected file
                _onlineSourceItems.Clear();
                if (_selectedFile != null)
                {
                    var cachedResults = _cacheManager.GetResults(_selectedFile.FilePath);
                    // Sort results by score (extract percentage from DisplayName and sort descending)
                    var sortedResults = cachedResults
                        .OrderByDescending(result => ExtractScoreFromDisplayName(result.DisplayName))
                        .ThenBy(result => result.DisplayName);
                    
                    foreach (var result in sortedResults)
                    {
                        _onlineSourceItems.Add(result);
                    }
                }
                
                // Subscribe to new file's events
                if (_selectedFile != null)
                {
                    _selectedFile.PropertyChanged += SelectedFile_PropertyChanged;
                }
                
                // Update button state after initialization is complete
                Dispatcher.BeginInvoke(new Action(() =>
                {
                        }));
            }
        }

        public bool IsFileSelected => SelectedFile != null;

        private readonly ObservableCollection<AudioFileInfo> _selectedFiles = new ObservableCollection<AudioFileInfo>();
        private readonly ObservableCollection<AlbumGroup> _selectedAlbums = new ObservableCollection<AlbumGroup>();
        
        public ObservableCollection<AudioFileInfo> SelectedFiles => _selectedFiles;
        public ObservableCollection<AlbumGroup> SelectedAlbums => _selectedAlbums;
        
        public bool HasMultipleSelection => _selectedFiles.Count > 1 || _selectedAlbums.Count > 0;
        public bool HasAlbumSelection => _selectedAlbums.Count > 0;
        public bool HasTrackSelection => _selectedFiles.Count > 0;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // Apply dark title bar when window handle is available
            SourceInitialized += MainWindow_SourceInitialized;

            _tagService = new TagService();
            _musicBrainzService = new MusicBrainzService();
            _discogsService = new DiscogsService();
            _updateService = new UpdateService();
            _cacheManager = new OnlineSourceCacheManager();
            
            // Initialize cover art service with user settings
            var settings = SettingsManager.LoadSettings();
            _coverArtService = new CoverArtService(settings.CoverArtSettings);
            _audioFiles = new ObservableCollection<AudioFileInfo>();
            _onlineSourceItems = new ObservableCollection<OnlineSourceItem>();
            _hierarchicalItems = new ObservableCollection<AlbumGroup>();

            // Subscribe to collection changes to update UI
            InitializeOnlineSourceDropdown();
            _audioFiles.CollectionChanged += AudioFiles_CollectionChanged;
            _audioFiles.CollectionChanged += (sender, e) =>
            {
                OnPropertyChanged(nameof(AudioFiles));
            };

            // Subscribe to selected file changes to update comparison display
            PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(SelectedFile))
                {
                    UpdateComparisonDisplay();
                    UpdateChangeHistoryDisplay();
                }
            };

            // Subscribe to window size changes for efficient scrollbar rendering
            SizeChanged += MainWindow_SizeChanged_ScrollOptimization;
            
            // Subscribe to window state changes for real-time saving
            StateChanged += MainWindow_StateChanged;
            LocationChanged += MainWindow_LocationChanged;
        }

        private bool _isResizing = false;
        private DispatcherTimer? _scrollbarOptimizationTimer;

        // Removed automatic resize timer - caused unpredictable behavior

        private void MainWindow_SizeChanged_ScrollOptimization(object sender, SizeChangedEventArgs e)
        {
            OptimizeScrollbarRenderingDuringResize();
        }

        private void OptimizeScrollbarRenderingDuringResize()
        {
            try
            {
                var dataGrid = FindDataGrid();
                if (dataGrid == null) return;

                if (!_isResizing)
                {
                    // Start resize optimization
                    _isResizing = true;
                    
                    // Temporarily disable smooth scrolling for better performance
                    var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
                    if (scrollViewer != null)
                    {
                        scrollViewer.IsDeferredScrollingEnabled = false;
                        scrollViewer.CanContentScroll = false; // Use pixel scrolling for smoother resize
                    }
                }

                // Reset or create the optimization timer
                if (_scrollbarOptimizationTimer == null)
                {
                    _scrollbarOptimizationTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(150)
                    };
                    _scrollbarOptimizationTimer.Tick += ScrollbarOptimizationTimer_Tick;
                }

                _scrollbarOptimizationTimer.Stop();
                _scrollbarOptimizationTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scrollbar optimization error: {ex.Message}");
            }
        }

        private void ScrollbarOptimizationTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Resize operation has finished, restore normal scrolling behavior
                _scrollbarOptimizationTimer?.Stop();
                _isResizing = false;

                var dataGrid = FindDataGrid();
                if (dataGrid != null)
                {
                    var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
                    if (scrollViewer != null)
                    {
                        // Restore deferred scrolling and content scrolling
                        scrollViewer.IsDeferredScrollingEnabled = true;
                        scrollViewer.CanContentScroll = true;
                        
                        // Force scrollviewer to update its layout
                        scrollViewer.InvalidateScrollInfo();
                        scrollViewer.UpdateLayout();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scrollbar optimization timer error: {ex.Message}");
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                // Save window state changes immediately
                var settings = SettingsManager.LoadSettings();
                if (settings.RememberWindowState)
                {
                    settings.WindowState = (int)WindowState;
                    settings.WindowMaximized = WindowState == WindowState.Maximized;
                    settings.WindowMinimized = WindowState == WindowState.Minimized;
                    SettingsManager.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window state: {ex.Message}");
            }
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            try
            {
                // Debounce location changes to avoid excessive saves during dragging
                if (_locationChangeTimer == null)
                {
                    _locationChangeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500)
                    };
                    _locationChangeTimer.Tick += LocationChangeTimer_Tick;
                }

                _locationChangeTimer.Stop();
                _locationChangeTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling location change: {ex.Message}");
            }
        }

        private DispatcherTimer? _locationChangeTimer;

        private void LocationChangeTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _locationChangeTimer?.Stop();
                
                // Save window position if in normal state
                var settings = SettingsManager.LoadSettings();
                if (settings.RememberWindowState && WindowState == WindowState.Normal)
                {
                    settings.WindowX = Left;
                    settings.WindowY = Top;
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                    SettingsManager.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window location: {ex.Message}");
            }
        }

        private void SelectedFile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioFileInfo.TagComparison))
            {
                UpdateComparisonDisplay();
            }
        }

        private void AlbumCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is AlbumGroup albumGroup)
            {
                bool isChecked = checkBox.IsChecked == true;
                System.Diagnostics.Debug.WriteLine($"AlbumCheckBox_Click: Album '{albumGroup.Album}', IsChecked: {isChecked}");
                
                if (isChecked)
                {
                    // Album is being selected - clear any individual track selections and uncheck their boxes
                    _selectedFiles.Clear();
                    ClearAllTrackCheckboxes();
                    ClearAllTrackSelections();
                    
                    // Add album to selection and set IsSelected property for highlighting
                    if (!_selectedAlbums.Contains(albumGroup))
                    {
                        _selectedAlbums.Add(albumGroup);
                        albumGroup.IsSelected = true;
                        System.Diagnostics.Debug.WriteLine($"Added album to selection. Total selected albums: {_selectedAlbums.Count}");
                    }
                }
                else
                {
                    // Album is being deselected
                    _selectedAlbums.Remove(albumGroup);
                    albumGroup.IsSelected = false;
                    System.Diagnostics.Debug.WriteLine($"Removed album from selection. Total selected albums: {_selectedAlbums.Count}");
                }
                
                UpdateSelectionProperties();
            }
        }

        private void TrackCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is AudioFileInfo track)
            {
                bool isChecked = checkBox.IsChecked == true;
                System.Diagnostics.Debug.WriteLine($"TrackCheckBox_Click: Track '{track.Title}', IsChecked: {isChecked}");
                
                if (isChecked)
                {
                    // Track is being selected - clear any album selections only if this is the first track selection
                    if (_selectedFiles.Count == 0)
                    {
                        _selectedAlbums.Clear();
                        ClearAllAlbumCheckboxes();
                        ClearAllAlbumSelections();
                    }
                    
                    // Add track to selection and set IsSelected property for highlighting
                    if (!_selectedFiles.Contains(track))
                    {
                        _selectedFiles.Add(track);
                        track.IsSelected = true;
                        System.Diagnostics.Debug.WriteLine($"Added track to selection. Total selected files: {_selectedFiles.Count}");
                    }
                }
                else
                {
                    // Track is being deselected
                    _selectedFiles.Remove(track);
                    track.IsSelected = false;
                    System.Diagnostics.Debug.WriteLine($"Removed track from selection. Total selected files: {_selectedFiles.Count}");
                }
                
                UpdateSelectionProperties();
            }
        }

        private void ClearAllTrackCheckboxes()
        {
            // Find all track checkboxes in the TreeView and uncheck them
            foreach (var item in MainTreeView.Items)
            {
                if (MainTreeView.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem albumContainer)
                {
                    if (item is AlbumGroup album)
                    {
                        foreach (var track in album.Tracks)
                        {
                            if (albumContainer.ItemContainerGenerator.ContainerFromItem(track) is TreeViewItem trackContainer)
                            {
                                var trackCheckBox = FindVisualChild<CheckBox>(trackContainer);
                                if (trackCheckBox != null)
                                {
                                    trackCheckBox.IsChecked = false;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ClearAllAlbumCheckboxes()
        {
            // Find all album checkboxes in the TreeView and uncheck them
            foreach (var item in MainTreeView.Items)
            {
                if (MainTreeView.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem albumContainer)
                {
                    var albumCheckBox = FindVisualChild<CheckBox>(albumContainer);
                    if (albumCheckBox != null)
                    {
                        albumCheckBox.IsChecked = false;
                    }
                }
            }
        }

        private void ClearAllTrackSelections()
        {
            // Clear IsSelected property from all tracks for highlighting
            foreach (var album in _hierarchicalItems)
            {
                foreach (var track in album.Tracks)
                {
                    track.IsSelected = false;
                }
            }
        }

        private void ClearAllAlbumSelections()
        {
            // Clear IsSelected property from all albums for highlighting
            foreach (var album in _hierarchicalItems)
            {
                album.IsSelected = false;
            }
        }


        private void UpdateSelectionProperties()
        {
            OnPropertyChanged(nameof(HasMultipleSelection));
            OnPropertyChanged(nameof(HasAlbumSelection));
            OnPropertyChanged(nameof(HasTrackSelection));
            
            // Update editor panel visibility based on current selection
            UpdateEditorPanelVisibility();
        }

        #region File Operations

        private void LoadFiles_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.flac;*.m4a;*.aac;*.wav;*.wma|" +
                        "MP3 Files|*.mp3|" +
                        "FLAC Files|*.flac|" +
                        "M4A Files|*.m4a|" +
                        "AAC Files|*.aac|" +
                        "WAV Files|*.wav|" +
                        "WMA Files|*.wma|" +
                        "All Files|*.*",
                Multiselect = true,
                Title = "Select Audio Files"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadFilesAsync(openFileDialog.FileNames);
            }
        }

        private async void LoadFolder_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            folderDialog.Description = "Select folder containing audio files";
            folderDialog.ShowNewFolderButton = false;

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedPath = folderDialog.SelectedPath;
                
                // Show scanning feedback
                Title = "TID3 - Scanning folder for audio files...";
                
                try
                {
                    // Scan folder asynchronously to avoid blocking UI
                    var files = await Task.Run(() =>
                    {
                        var supportedExtensions = new[] { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma" };
                        return Directory.GetFiles(selectedPath, "*.*", SearchOption.AllDirectories)
                            .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                            .ToArray();
                    });

                    // Reset title
                    Title = "TID3 - Advanced ID3 Tag Editor";

                    if (files.Length == 0)
                    {
                        MessageBox.Show("No supported audio files found in the selected folder.",
                                      "No Files Found",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Information);
                        return;
                    }

                    LoadFilesAsync(files);
                }
                catch (Exception ex)
                {
                    // Reset title on error
                    Title = "TID3 - Advanced ID3 Tag Editor";
                    MessageBox.Show($"Error scanning folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void LoadFilesAsync(string[] filePaths)
        {
            try
            {
                // Filter out already loaded files
                var existingFilePaths = new HashSet<string>(_audioFiles.Select(f => Path.GetFullPath(f.FilePath)), StringComparer.OrdinalIgnoreCase);
                var newFilePaths = filePaths.Where(path => !existingFilePaths.Contains(Path.GetFullPath(path))).ToArray();
                var duplicateCount = filePaths.Length - newFilePaths.Length;
                
                if (duplicateCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipped {duplicateCount} duplicate file{(duplicateCount != 1 ? "s" : "")} already loaded");
                }
                
                if (newFilePaths.Length == 0)
                {
                    if (duplicateCount > 0)
                    {
                        UpdateStatus($"All {filePaths.Length} file{(filePaths.Length != 1 ? "s" : "")} already loaded");
                        MessageBox.Show($"All {filePaths.Length} selected file{(filePaths.Length != 1 ? "s are" : " is")} already loaded in the application.", 
                            "Duplicate Files", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }
                
                var totalFiles = newFilePaths.Length;
                var processedCount = 0;
                var successfulCount = 0;
                
                // Get max concurrent operations from settings
                var settings = SettingsManager.LoadSettings();
                var maxDegreeOfParallelism = Math.Max(1, Math.Min(settings.MaxConcurrentOperations, Environment.ProcessorCount));
                
                // Show the parallelism setting to user  
                var parallelismNote = maxDegreeOfParallelism > 1 
                    ? $"using {maxDegreeOfParallelism} parallel threads" 
                    : "using single thread";
                
                var statusMessage = duplicateCount > 0 
                    ? $"Loading {totalFiles} new files {parallelismNote} ({duplicateCount} duplicate{(duplicateCount != 1 ? "s" : "")} skipped)..."
                    : $"Loading {totalFiles} files {parallelismNote}...";
                UpdateStatus(statusMessage);
                

                // Create progress reporter with throttling for better performance
                var lastProgressUpdate = DateTime.MinValue;
                var progress = new Progress<(int current, int total, string fileName)>(report =>
                {
                    var now = DateTime.Now;
                    // Only update UI every 50ms to reduce overhead
                    if ((now - lastProgressUpdate).TotalMilliseconds > 50)
                    {
                        var percentage = (int)((double)report.current / report.total * 100);
                        Title = $"TID3 - Loading Files: {percentage}% ({report.current}/{report.total}) - {report.fileName}";
                        lastProgressUpdate = now;
                    }
                });

                // Update title to show we're starting
                Title = "TID3 - Loading Files: 0% (0/" + totalFiles + ") - Preparing...";

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var loadedFiles = await Task.Run(() =>
                {
                    var files = new ConcurrentBag<AudioFileInfo>();
                    var reportInterval = Math.Max(1, totalFiles / 20); // Report progress every 5% or at least every file
                    
                    // Use parallel processing with controlled degree of parallelism
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism
                    };

                    Parallel.ForEach(newFilePaths, parallelOptions, fileName =>
                    {
                        try
                        {
                            var audioFile = _tagService.LoadFile(fileName);
                            if (audioFile != null)
                            {
                                audioFile.CreateSnapshot(); // Create initial snapshot for comparison
                                files.Add(audioFile);
                                Interlocked.Increment(ref successfulCount);
                            }
                            
                            // Report progress less frequently to reduce thread contention
                            var currentCount = Interlocked.Increment(ref processedCount);
                            if (currentCount % reportInterval == 0 || currentCount == totalFiles)
                            {
                                var shortFileName = Path.GetFileName(fileName);
                                ((IProgress<(int, int, string)>)progress).Report((currentCount, totalFiles, shortFileName));
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log individual file errors but continue processing
                            System.Diagnostics.Debug.WriteLine($"Error loading file {fileName}: {ex.Message}");
                            Interlocked.Increment(ref processedCount); // Still count failed files for progress
                        }
                    });

                    // Sort by album, then by track number
                    return files.OrderBy(f => f.Album ?? "").ThenBy(f => f.Track).ToList();
                });
                
                stopwatch.Stop();

                // Update UI with final progress
                Title = "TID3 - Loading Files: 100% - Adding to list...";

                // Add files to UI collection in batches to improve performance
                const int batchSize = 50;
                for (int i = 0; i < loadedFiles.Count; i += batchSize)
                {
                    var batch = loadedFiles.Skip(i).Take(batchSize);
                    foreach (var file in batch)
                    {
                        _audioFiles.Add(file);
                    }
                    
                    // Allow UI to update between batches
                    await Task.Delay(1);
                }

                // Refresh folder-based cover art for all loaded files
                Title = "TID3 - Scanning for cover art files...";
                await Task.Run(() => {
                    _tagService.RefreshFolderCoverArt(_audioFiles);
                });

                // Reset title and show completion status
                Title = "TID3 - Advanced ID3 Tag Editor";
                var avgTimePerFile = totalFiles > 0 ? stopwatch.ElapsedMilliseconds / (double)totalFiles : 0;
                var threadsNote = maxDegreeOfParallelism > 1 ? $"{maxDegreeOfParallelism} threads" : "1 thread";
                var performanceNote = avgTimePerFile < 50 ? "Fast" : avgTimePerFile < 200 ? "Good" : "Slow";
                
                // Count files with cover art
                var filesWithCoverArt = _audioFiles.Count(f => f.AlbumCover != null);
                var coverArtNote = filesWithCoverArt > 0 ? $", {filesWithCoverArt} with cover art" : "";
                
                var duplicateNote = duplicateCount > 0 ? $" ({duplicateCount} duplicate{(duplicateCount != 1 ? "s" : "")} skipped)" : "";
                UpdateStatus($"Loaded {successfulCount}/{totalFiles} files in {stopwatch.ElapsedMilliseconds}ms ({performanceNote}: {avgTimePerFile:F0}ms/file, {threadsNote}){coverArtNote}{duplicateNote}.");
            }
            catch (Exception ex)
            {
                // Reset title on error
                Title = "TID3 - Advanced ID3 Tag Editor";
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void SaveSelected_Click(object sender, RoutedEventArgs e)
        {
            List<AudioFileInfo> filesToSave = new List<AudioFileInfo>();
            
            // Check multiselect collections first
            if (_selectedFiles.Count > 0)
            {
                filesToSave.AddRange(_selectedFiles);
            }
            else if (_selectedAlbums.Count > 0)
            {
                filesToSave.AddRange(_selectedAlbums.SelectMany(album => album.Tracks));
            }
            else if (SelectedFile != null)
            {
                filesToSave.Add(SelectedFile);
            }
            
            if (!filesToSave.Any())
            {
                MessageBox.Show("Please select files to save.", "No Files Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int successCount = 0;
                int coverArtSavedCount = 0;
                var failedFiles = new List<string>();

                foreach (var file in filesToSave)
                {
                    var replaceCoverArt = IsInBatchMode() ? BatchReplaceCoverArtCheckBox.IsChecked == true : ReplaceCoverArtCheckBox.IsChecked == true;
                    var (success, coverArtSaved) = _tagService.SaveFile(file, replaceCoverArt);
                    if (success)
                    {
                        successCount++;
                        if (coverArtSaved) coverArtSavedCount++;
                    }
                    else
                    {
                        failedFiles.Add(file.FileName);
                    }
                }

                if (successCount > 0)
                {
                    string message = $"Successfully saved {successCount} file{(successCount != 1 ? "s" : "")}!";
                    if (coverArtSavedCount > 0)
                    {
                        message += $"\nCover art also saved for {coverArtSavedCount} file{(coverArtSavedCount != 1 ? "s" : "")}.";
                    }
                    
                    if (failedFiles.Any())
                    {
                        message += $"\n\nFailed to save {failedFiles.Count} file{(failedFiles.Count != 1 ? "s" : "")}:\n{string.Join("\n", failedFiles)}";
                    }
                    
                    UpdateStatus($"Saved {successCount} of {filesToSave.Count} selected files");
                    MessageBox.Show(message, "Save Results", MessageBoxButton.OK, 
                        failedFiles.Any() ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to save any files. Please check the file permissions.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_audioFiles.Any())
            {
                MessageBox.Show("No files to save.", "No Files", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to save all {_audioFiles.Count} files?",
                                       "Confirm Save All",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var (saved, coverArtsSaved) = await Task.Run(() =>
                {
                    int savedCount = 0;
                    int coverArtCount = 0;
                    
                    foreach (var file in _audioFiles)
                    {
                        var replaceCoverArt = IsInBatchMode() ? BatchReplaceCoverArtCheckBox.IsChecked == true : ReplaceCoverArtCheckBox.IsChecked == true;
                        var (success, coverArtSaved) = _tagService.SaveFile(file, replaceCoverArt);
                        if (success) savedCount++;
                        if (coverArtSaved) coverArtCount++;
                    }
                    return (savedCount, coverArtCount);
                });

                string statusMessage = $"Saved {saved} of {_audioFiles.Count} files successfully.";
                if (coverArtsSaved > 0)
                {
                    statusMessage += $" {coverArtsSaved} cover arts saved to folders.";
                }
                
                UpdateStatus(statusMessage);
                MessageBox.Show($"Saved {saved} of {_audioFiles.Count} files.", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during batch save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion


        #region Batch Operations

        private void MainDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEditorPanelVisibility();
        }

        private void MainTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Only update SelectedFile if we don't have a multiselect scenario
            if (_selectedFiles.Count <= 1 && _selectedAlbums.Count == 0)
            {
                if (e.NewValue is AudioFileInfo audioFile)
                {
                    SelectedFile = audioFile;
                }
                else if (e.NewValue is AlbumGroup albumGroup)
                {
                    // When selecting an album group, select the first track if available
                    SelectedFile = albumGroup.Tracks.FirstOrDefault();
                }
            }
            
            UpdateEditorPanelVisibility();
            
            // Update match scores for the new selection if there's an online source selected
            if (OnlineSourceComboBox.SelectedItem != null)
            {
                UpdateMatchScores();
            }
        }

        private void MainTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedItems();
                e.Handled = true;
            }
        }

        private void AudioFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RebuildHierarchicalStructure();
        }

        private void RebuildHierarchicalStructure()
        {
            var albumGroups = _audioFiles
                .GroupBy(file => file.Album ?? "Unknown Album")
                .Select(group => new AlbumGroup
                {
                    Album = group.Key,
                    Artist = group.FirstOrDefault()?.AlbumArtist ?? group.FirstOrDefault()?.Artist ?? "Unknown Artist",
                    Year = group.Max(f => f.Year),
                    Genre = group.FirstOrDefault()?.Genre ?? "",
                    Tracks = new ObservableCollection<AudioFileInfo>(group.OrderBy(f => f.Track))
                })
                .OrderBy(g => g.Artist)
                .ThenBy(g => g.Year)
                .ThenBy(g => g.Album);

            _hierarchicalItems.Clear();
            foreach (var albumGroup in albumGroups)
            {
                _hierarchicalItems.Add(albumGroup);
            }

            OnPropertyChanged(nameof(HierarchicalItems));
        }

        private void MainDataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedFiles();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle Delete key if TreeView has focus
            if (e.Key == Key.Delete && MainTreeView.SelectedItem != null)
            {
                // Check if the focused element is the TreeView or one of its children
                var focusedElement = Keyboard.FocusedElement as DependencyObject;
                bool isTreeViewFocused = focusedElement != null && IsChildOfTreeView(focusedElement);
                
                // Also check if TreeView itself is focused or has keyboard focus within
                bool hasKeyboardFocusWithin = MainTreeView.IsKeyboardFocusWithin;
                
                if (isTreeViewFocused || hasKeyboardFocusWithin)
                {
                    RemoveSelectedItems();
                    e.Handled = true;
                }
            }
        }

        private bool IsChildOfTreeView(DependencyObject element)
        {
            while (element != null)
            {
                if (element == MainTreeView)
                    return true;
                element = VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void RemoveSelectedItems()
        {
            List<AudioFileInfo> filesToRemove = new List<AudioFileInfo>();
            
            // Check multiselect collections first
            if (_selectedFiles.Count > 0)
            {
                filesToRemove.AddRange(_selectedFiles);
            }
            else if (_selectedAlbums.Count > 0)
            {
                filesToRemove.AddRange(_selectedAlbums.SelectMany(album => album.Tracks));
            }
            else
            {
                // Fall back to TreeView selection for backward compatibility
                var selectedItem = MainTreeView.SelectedItem;
                if (selectedItem is AudioFileInfo audioFile)
                {
                    filesToRemove.Add(audioFile);
                }
                else if (selectedItem is AlbumGroup albumGroup)
                {
                    filesToRemove.AddRange(albumGroup.Tracks);
                }
            }
            
            if (!filesToRemove.Any()) return;
            
            // Confirm removal if multiple files
            if (filesToRemove.Count > 1)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to remove {filesToRemove.Count} selected files from the list?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                    
                if (result != MessageBoxResult.Yes) return;
            }
            
            // Remove files and clear caches
            foreach (var track in filesToRemove)
            {
                _audioFiles.Remove(track);
                _cacheManager.ClearResultsForFile(track.FilePath);
                
                // Clear from multiselect collections
                _selectedFiles.Remove(track);
            }
            
            // Clear selected albums that no longer have tracks
            var albumsToRemove = _selectedAlbums.Where(album => 
                album.Tracks.All(track => filesToRemove.Contains(track))).ToList();
            foreach (var album in albumsToRemove)
            {
                _selectedAlbums.Remove(album);
            }
            
            // Clear current UI display if any removed file was the selected file
            if (filesToRemove.Any(f => SelectedFile?.FilePath == f.FilePath))
            {
                _onlineSourceItems.Clear();
                SelectedFile = null;
            }
            
            // Update UI
            OnPropertyChanged(nameof(AudioFiles));
            UpdateEditorPanelVisibility();
        }

        private void RemoveSelectedFiles()
        {
            // This method is kept for backward compatibility but delegates to RemoveSelectedItems
            RemoveSelectedItems();
        }

        private void UpdateEditorPanelVisibility()
        {
            // Check multiselect scenario first
            if (_selectedFiles.Count > 1)
            {
                // Multiple tracks selected - show batch editing
                EditorHeaderText.Text = $"Batch Edit ({_selectedFiles.Count} tracks)";
                SelectionInfoText.Text = $"Editing {_selectedFiles.Count} selected tracks";
                SingleEditPanel.Visibility = Visibility.Collapsed;
                BatchEditPanel.Visibility = Visibility.Visible;
                
                // Update batch field values for selected tracks
                UpdateBatchFieldValues(_selectedFiles.ToList());
                
                // Update batch album cover display (may show mixed covers)
                UpdateBatchAlbumCoverForSelectedTracks(_selectedFiles.ToList());
                return;
            }
            else if (_selectedAlbums.Count > 0)
            {
                // One or more albums selected
                var totalTracks = _selectedAlbums.Sum(album => album.Tracks.Count);
                var albumNames = string.Join(", ", _selectedAlbums.Select(a => $"'{a.AlbumInfo}'"));
                
                EditorHeaderText.Text = $"Album Edit ({totalTracks} tracks)";
                SelectionInfoText.Text = $"Editing albums: {albumNames}";
                SingleEditPanel.Visibility = Visibility.Collapsed;
                BatchEditPanel.Visibility = Visibility.Visible;
                
                // Combine tracks from all selected albums
                var allTracks = _selectedAlbums.SelectMany(album => album.Tracks).ToList();
                UpdateBatchFieldValues(allTracks);
                UpdateBatchAlbumCoverForSelectedTracks(allTracks);
                return;
            }
            
            // Fall back to TreeView selection for single selection or no multiselect
            var selectedItem = MainTreeView?.SelectedItem;
            
            if (selectedItem == null && _selectedFiles.Count == 0 && _selectedAlbums.Count == 0)
            {
                // No selection
                EditorHeaderText.Text = "Tag Editor";
                SelectionInfoText.Text = "Select files to edit tags";
                SingleEditPanel.Visibility = Visibility.Collapsed;
                BatchEditPanel.Visibility = Visibility.Collapsed;
                ClearBatchAlbumCover();
            }
            else if (_selectedFiles.Count == 1)
            {
                // Single track selected via checkbox
                EditorHeaderText.Text = "Tag Editor";
                SelectionInfoText.Text = "Editing single file";
                SingleEditPanel.Visibility = Visibility.Visible;
                BatchEditPanel.Visibility = Visibility.Collapsed;
                ClearBatchAlbumCover();
                
                // Update SelectedFile for single edit panel
                SelectedFile = _selectedFiles.First();
            }
            else if (selectedItem is AudioFileInfo)
            {
                // Single file selected via TreeView
                EditorHeaderText.Text = "Tag Editor";
                SelectionInfoText.Text = "Editing single file";
                SingleEditPanel.Visibility = Visibility.Visible;
                BatchEditPanel.Visibility = Visibility.Collapsed;
                ClearBatchAlbumCover();
            }
            else if (selectedItem is AlbumGroup albumGroup)
            {
                // Album group selected via TreeView - show batch editing for all tracks in album
                var trackCount = albumGroup.Tracks.Count;
                EditorHeaderText.Text = $"Album Edit ({trackCount} tracks)";
                SelectionInfoText.Text = $"Editing all tracks in '{albumGroup.AlbumInfo}'";
                SingleEditPanel.Visibility = Visibility.Collapsed;
                BatchEditPanel.Visibility = Visibility.Visible;
                
                // Update batch album cover display
                UpdateBatchAlbumCover(albumGroup);
                
                // Update batch field values
                UpdateBatchFieldValues(albumGroup.Tracks.ToList());
            }
        }

        private void UpdateBatchAlbumCover(AlbumGroup albumGroup)
        {
            try
            {
                // Collect all available cover sources from all tracks
                PopulateBatchCoverSources(albumGroup.Tracks.ToList());
                
                // Update the display
                UpdateBatchAlbumCoverDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating batch album cover: {ex.Message}");
                BatchAlbumCoverImage.Source = null;
                BatchAlbumCoverText.Text = "Error Loading Cover";
                BatchAlbumCoverText.Visibility = Visibility.Visible;
                BatchCoverSourceComboBox.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateBatchAlbumCoverForSelectedTracks(List<AudioFileInfo> tracks)
        {
            try
            {
                // Collect all available cover sources from selected tracks
                PopulateBatchCoverSources(tracks);
                
                // Update the display
                UpdateBatchAlbumCoverDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating batch album cover for selected tracks: {ex.Message}");
                BatchAlbumCoverImage.Source = null;
                BatchAlbumCoverText.Text = "Error Loading Cover";
                BatchAlbumCoverText.Visibility = Visibility.Visible;
                BatchCoverSourceComboBox.Visibility = Visibility.Collapsed;
            }
        }

        private void PopulateBatchCoverSources(List<AudioFileInfo> tracks)
        {
            // Temporarily disable the SelectionChanged event to prevent recursion
            BatchCoverSourceComboBox.SelectionChanged -= BatchCoverSourceComboBox_SelectionChanged;
            
            try
            {
                _batchAvailableCovers.Clear();
                var coverSourceMap = new Dictionary<string, CoverSource>();

                foreach (var track in tracks)
                {
                    foreach (var coverSource in track.AvailableCovers)
                    {
                        if (!coverSourceMap.ContainsKey(coverSource.Name))
                        {
                            coverSourceMap[coverSource.Name] = new CoverSource
                            {
                                Name = coverSource.Name,
                                Image = coverSource.Image,
                                Source = coverSource.Source
                            };
                        }
                    }
                }

                foreach (var coverSource in coverSourceMap.Values)
                {
                    _batchAvailableCovers.Add(coverSource);
                }

                // Set up ComboBox
                BatchCoverSourceComboBox.ItemsSource = _batchAvailableCovers;
                
                // Select first available source if none is selected
                if (string.IsNullOrEmpty(_selectedBatchCoverSource) && _batchAvailableCovers.Count > 0)
                {
                    _selectedBatchCoverSource = _batchAvailableCovers.First().Name;
                }

                // Set selected item
                var selectedCover = _batchAvailableCovers.FirstOrDefault(c => c.Name == _selectedBatchCoverSource);
                if (selectedCover != null)
                {
                    BatchCoverSourceComboBox.SelectedItem = selectedCover;
                }

                // Show/hide ComboBox based on availability
                BatchCoverSourceComboBox.Visibility = _batchAvailableCovers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                // Re-enable the SelectionChanged event
                BatchCoverSourceComboBox.SelectionChanged += BatchCoverSourceComboBox_SelectionChanged;
            }
        }

        private void UpdateBatchAlbumCoverDisplay()
        {
            try
            {
                var selectedCover = _batchAvailableCovers.FirstOrDefault(c => c.Name == _selectedBatchCoverSource);
                
                if (selectedCover?.Image != null)
                {
                    // Show the selected cover
                    BatchAlbumCoverImage.Source = selectedCover.Image;
                    BatchAlbumCoverText.Visibility = Visibility.Collapsed;
                    
                    // Show resolution
                    BatchCoverResolutionText.Text = selectedCover.Resolution;
                    BatchCoverResolutionText.Visibility = !string.IsNullOrEmpty(selectedCover.Resolution) ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Show source
                    BatchCoverArtSourceText.Text = selectedCover.Source;
                    BatchCoverArtSourceText.Visibility = !string.IsNullOrEmpty(selectedCover.Source) ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (_batchAvailableCovers.Count > 0)
                {
                    // Has covers but none selected
                    BatchAlbumCoverImage.Source = null;
                    BatchAlbumCoverText.Text = "Select Cover Source";
                    BatchAlbumCoverText.Visibility = Visibility.Visible;
                    BatchCoverResolutionText.Visibility = Visibility.Collapsed;
                    BatchCoverArtSourceText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // No covers available
                    BatchAlbumCoverImage.Source = null;
                    BatchAlbumCoverText.Text = "No Album Cover";
                    BatchAlbumCoverText.Visibility = Visibility.Visible;
                    BatchCoverResolutionText.Visibility = Visibility.Collapsed;
                    BatchCoverArtSourceText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating batch cover display: {ex.Message}");
                BatchAlbumCoverImage.Source = null;
                BatchAlbumCoverText.Text = "Error Loading Cover";
                BatchAlbumCoverText.Visibility = Visibility.Visible;
            }
        }

        private void ClearBatchAlbumCover()
        {
            try
            {
                BatchAlbumCoverImage.Source = null;
                BatchAlbumCoverText.Text = "No Common Cover";
                BatchAlbumCoverText.Visibility = Visibility.Visible;
                BatchCoverResolutionText.Visibility = Visibility.Collapsed;
                BatchCoverArtSourceText.Visibility = Visibility.Collapsed;
                BatchCoverSourceComboBox.Visibility = Visibility.Collapsed;
                
                // Clear batch cover sources
                _batchAvailableCovers.Clear();
                _selectedBatchCoverSource = "";
                
                // Also clear batch field values
                ClearBatchFieldValues();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing batch album cover: {ex.Message}");
            }
        }
        
        private void ClearBatchFieldValues()
        {
            try
            {
                BatchAlbumText.Text = "";
                BatchAlbumArtistText.Text = "";
                BatchGenreText.Text = "";
                BatchYearText.Text = "";
                
                BatchAlbumText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                BatchAlbumArtistText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                BatchGenreText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                BatchYearText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                
                BatchAlbumCheck.IsChecked = false;
                BatchAlbumArtistCheck.IsChecked = false;
                BatchGenreCheck.IsChecked = false;
                BatchYearCheck.IsChecked = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing batch field values: {ex.Message}");
            }
        }

        private void UpdateBatchFieldValues(List<AudioFileInfo> tracks)
        {
            try
            {
                // Update Album field
                var albums = tracks.Select(t => t.Album).Distinct().ToList();
                if (albums.Count == 1)
                {
                    BatchAlbumText.Text = albums.First();
                    BatchAlbumText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }
                else if (albums.Count > 1)
                {
                    BatchAlbumText.Text = "[Multiple Values]";
                    BatchAlbumText.Foreground = (System.Windows.Media.Brush)Resources["SubTextBrush"];
                }
                else
                {
                    BatchAlbumText.Text = "";
                    BatchAlbumText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }

                // Update Album Artist field
                var albumArtists = tracks.Select(t => t.AlbumArtist).Distinct().ToList();
                if (albumArtists.Count == 1)
                {
                    BatchAlbumArtistText.Text = albumArtists.First();
                    BatchAlbumArtistText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }
                else if (albumArtists.Count > 1)
                {
                    BatchAlbumArtistText.Text = "[Multiple Values]";
                    BatchAlbumArtistText.Foreground = (System.Windows.Media.Brush)Resources["SubTextBrush"];
                }
                else
                {
                    BatchAlbumArtistText.Text = "";
                    BatchAlbumArtistText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }

                // Update Genre field
                var genres = tracks.Select(t => t.Genre).Distinct().ToList();
                if (genres.Count == 1)
                {
                    BatchGenreText.Text = genres.First();
                    BatchGenreText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }
                else if (genres.Count > 1)
                {
                    BatchGenreText.Text = "[Multiple Values]";
                    BatchGenreText.Foreground = (System.Windows.Media.Brush)Resources["SubTextBrush"];
                }
                else
                {
                    BatchGenreText.Text = "";
                    BatchGenreText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }

                // Update Year field
                var years = tracks.Select(t => t.Year).Where(y => y > 0).Distinct().ToList();
                if (years.Count == 1)
                {
                    BatchYearText.Text = years.First().ToString();
                    BatchYearText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }
                else if (years.Count > 1)
                {
                    BatchYearText.Text = "[Multiple Values]";
                    BatchYearText.Foreground = (System.Windows.Media.Brush)Resources["SubTextBrush"];
                }
                else
                {
                    BatchYearText.Text = "";
                    BatchYearText.Foreground = (System.Windows.Media.Brush)Resources["TextBrush"];
                }

                // Reset checkboxes to unchecked by default
                BatchAlbumCheck.IsChecked = false;
                BatchAlbumArtistCheck.IsChecked = false;
                BatchGenreCheck.IsChecked = false;
                BatchYearCheck.IsChecked = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating batch field values: {ex.Message}");
            }
        }

        private void ApplyBatchEdit_Click(object sender, RoutedEventArgs e)
        {
            List<AudioFileInfo> selectedFiles = new List<AudioFileInfo>();

            // Check multiselect collections first
            if (_selectedFiles.Count > 0)
            {
                // Use multiselected tracks
                selectedFiles.AddRange(_selectedFiles);
            }
            else if (_selectedAlbums.Count > 0)
            {
                // Use multiselected albums (all tracks from selected albums)
                selectedFiles.AddRange(_selectedAlbums.SelectMany(album => album.Tracks));
            }
            else
            {
                // Fall back to TreeView selection for backward compatibility
                var selectedItem = MainTreeView.SelectedItem;
                if (selectedItem is AudioFileInfo audioFile)
                {
                    selectedFiles.Add(audioFile);
                }
                else if (selectedItem is AlbumGroup albumGroup)
                {
                    selectedFiles.AddRange(albumGroup.Tracks);
                }
            }

            if (!selectedFiles.Any())
            {
                MessageBox.Show("Please select at least one file to update.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var changes = GetBatchChanges();
            if (!changes.Any())
            {
                MessageBox.Show("Please select at least one field to update.", "No Changes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Skip confirmation dialog if only updating album cover
            var onlyUpdatingCover = changes.Count == 1 && changes.ContainsKey("UpdateAlbumCover");
            var result = MessageBoxResult.Yes; // Default to Yes
            
            if (!onlyUpdatingCover)
            {
                result = MessageBox.Show($"Are you sure you want to update {selectedFiles.Count} files?",
                                       "Confirm Batch Update",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);
            }

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    ApplyBatchChanges(selectedFiles, changes);
                    
                    // Only show success message if not just updating cover art
                    if (!onlyUpdatingCover)
                    {
                        MessageBox.Show($"Successfully updated {selectedFiles.Count} files!");
                    }
                    
                    // Refresh data binding
                    OnPropertyChanged(nameof(AudioFiles));
                    OnPropertyChanged(nameof(SelectedFile));

                    // Clear batch edit fields
                    ClearBatchEditFields();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error updating files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private Dictionary<string, object> GetBatchChanges()
        {
            var changes = new Dictionary<string, object>();

            if (BatchAlbumCheck.IsChecked == true && !string.IsNullOrWhiteSpace(BatchAlbumText.Text))
                changes["Album"] = BatchAlbumText.Text.Trim();

            if (BatchAlbumArtistCheck.IsChecked == true && !string.IsNullOrWhiteSpace(BatchAlbumArtistText.Text))
                changes["AlbumArtist"] = BatchAlbumArtistText.Text.Trim();

            if (BatchGenreCheck.IsChecked == true && !string.IsNullOrWhiteSpace(BatchGenreText.Text))
                changes["Genre"] = BatchGenreText.Text.Trim();

            if (BatchYearCheck.IsChecked == true && !string.IsNullOrWhiteSpace(BatchYearText.Text))
            {
                if (uint.TryParse(BatchYearText.Text.Trim(), out uint year))
                    changes["Year"] = year;
            }

            if (BatchAutoNumberCheck.IsChecked == true)
                changes["AutoNumberTracks"] = true;

            if (BatchCleanupCheck.IsChecked == true)
                changes["CleanupTags"] = true;

            if (BatchAlbumCoverCheck.IsChecked == true)
                changes["UpdateAlbumCover"] = true;

            return changes;
        }

        private void ApplyBatchChanges(List<AudioFileInfo> files, Dictionary<string, object> changes)
        {
            // Check if we should save cover art once per album
            var saveCoverArt = changes.ContainsKey("UpdateAlbumCover");
            var replaceCoverArt = IsInBatchMode() ? BatchReplaceCoverArtCheckBox.IsChecked == true : ReplaceCoverArtCheckBox.IsChecked == true;
            var albumsCoverSaved = new HashSet<string>(); // Track which albums have had cover art saved

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];

                // Apply basic field changes
                if (changes.ContainsKey("Album"))
                    file.Album = changes["Album"]?.ToString() ?? "";

                if (changes.ContainsKey("AlbumArtist"))
                    file.AlbumArtist = changes["AlbumArtist"]?.ToString() ?? "";

                if (changes.ContainsKey("Genre"))
                    file.Genre = changes["Genre"]?.ToString() ?? "";

                if (changes.ContainsKey("Year"))
                    file.Year = (uint)changes["Year"];

                // Auto-number tracks
                if (changes.ContainsKey("AutoNumberTracks"))
                    file.Track = (uint)(i + 1);

                // Cleanup empty tags
                if (changes.ContainsKey("CleanupTags"))
                {
                    if (string.IsNullOrWhiteSpace(file.Title)) file.Title = "";
                    if (string.IsNullOrWhiteSpace(file.Artist)) file.Artist = "";
                    if (string.IsNullOrWhiteSpace(file.Album)) file.Album = "";
                    if (string.IsNullOrWhiteSpace(file.Genre)) file.Genre = "";
                    if (string.IsNullOrWhiteSpace(file.AlbumArtist)) file.AlbumArtist = "";
                    if (string.IsNullOrWhiteSpace(file.Comment)) file.Comment = "";
                }

                // Determine if we should save cover art for this file
                bool saveCoverForThisFile = false;
                if (saveCoverArt && file.AlbumCover != null)
                {
                    // Create a unique album identifier
                    var albumKey = $"{file.Artist?.Trim()}|{file.Album?.Trim()}";
                    
                    // Only save cover art once per unique album
                    if (!string.IsNullOrEmpty(albumKey) && !albumsCoverSaved.Contains(albumKey))
                    {
                        saveCoverForThisFile = true;
                        albumsCoverSaved.Add(albumKey);
                        System.Diagnostics.Debug.WriteLine($"Will save cover art for album: {albumKey}");
                    }
                }

                // Save the file (with or without cover art)
                var (success, coverArtSaved) = _tagService.SaveFile(file, replaceCoverArt && saveCoverForThisFile);
                if (coverArtSaved)
                {
                    System.Diagnostics.Debug.WriteLine($"Cover art saved to album folder for: {file.Album} by {file.Artist}");
                }
            }
        }

        private void ClearBatchEditFields()
        {
            BatchAlbumCheck.IsChecked = false;
            BatchAlbumArtistCheck.IsChecked = false;
            BatchGenreCheck.IsChecked = false;
            BatchYearCheck.IsChecked = false;
            BatchAutoNumberCheck.IsChecked = false;
            BatchCleanupCheck.IsChecked = false;

            BatchAlbumText.Text = "";
            BatchAlbumArtistText.Text = "";
            BatchGenreText.Text = "";
            BatchYearText.Text = "";
            
            // Reset cover art checkboxes to their default state
            BatchReplaceCoverArtCheckBox.IsChecked = true;
            BatchAlbumCoverCheck.IsChecked = false;
        }

        #endregion

        #region UI Operations

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;

                if (settingsWindow.ShowDialog() == true)
                {
                    // Reload settings and apply changes
                    var settings = SettingsManager.LoadSettings();
                    ApplySettings(settings);
                    UpdateStatus("Settings applied successfully.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplySettings(AppSettings settings)
        {
            // Apply font size changes
            FontSize = settings.FontSize;

            // Update services with new API credentials
            // This would typically involve recreating the services with new settings
            // For now, we'll just show a restart message if API settings changed

            // Apply window settings
            if (settings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            if (!_audioFiles.Any())
            {
                MessageBox.Show("File list is already empty.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasUnsavedChanges = HasUnsavedChanges();
            MessageBoxResult result = MessageBoxResult.Yes;

            if (hasUnsavedChanges)
            {
                result = MessageBox.Show($"Are you sure you want to clear all {_audioFiles.Count} files from the list?\n\nUnsaved changes will be lost.",
                                           "Confirm Clear",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Question);
            }

            if (result == MessageBoxResult.Yes)
            {
                // Clean up memory for each file before clearing
                foreach (var file in _audioFiles)
                {
                    file.Cleanup();
                }
                
                _audioFiles.Clear();
                SelectedFile = null!;
                
                // Clear all cached online source results
                _cacheManager.ClearAllResults();
                _onlineSourceItems.Clear();
                
                UpdateStatus("File list cleared.");
            }
        }

        private void CoverSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedFile == null) return;

            var comboBox = sender as ComboBox;
            var selectedCover = comboBox?.SelectedItem as CoverSource;
            if (selectedCover != null)
            {
                SelectedFile.SelectedCoverSource = selectedCover.Name;
            }
        }


        private void UpdateStatus(string message)
        {
            Title = $"TID3 - Advanced ID3 Tag Editor - {message}";

            // Reset title after 3 seconds
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (s, e) =>
            {
                Title = "TID3 - Advanced ID3 Tag Editor";
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateChangeHistoryDisplay()
        {
            if (SelectedFile?.ChangeHistory != null)
            {
                ChangeHistoryList.ItemsSource = SelectedFile.ChangeHistory;
            }
            else
            {
                ChangeHistoryList.ItemsSource = null;
            }
        }

        #endregion

        #region Tag Comparison Methods

        private void ComparisonMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateComparisonDisplay();
        }

        private void UpdateComparisonDisplay()
        {
            if (SelectedFile?.TagComparison == null) return;

            var mode = (ComparisonMode)ComparisonModeComboBox.SelectedIndex;

            var filteredComparison = SelectedFile.TagComparison.AsEnumerable();

            switch (mode)
            {
                case ComparisonMode.ChangedOnly:
                    filteredComparison = filteredComparison.Where(c => c.IsChanged || c.IsNew);
                    break;
                case ComparisonMode.MissingOnly:
                    filteredComparison = filteredComparison.Where(c => string.IsNullOrWhiteSpace(c.OriginalValue) && !string.IsNullOrWhiteSpace(c.NewValue));
                    break;
            }

            // Always show empty fields - removed the showEmpty filter

            TagComparisonDataGrid.ItemsSource = filteredComparison.ToList();
        }

        private void AcceptChange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is TagComparisonItem item)
            {
                SelectedFile?.AcceptChange(item);
                UpdateComparisonDisplay();
            }
        }

        private void RejectChange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is TagComparisonItem item)
            {
                SelectedFile?.RejectChange(item);
                UpdateComparisonDisplay();
            }
        }

        private void AcceptAllChanges_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null) return;

            var result = MessageBox.Show(
                "Are you sure you want to accept all pending changes?",
                "Accept All Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SelectedFile.AcceptAllChanges();
                UpdateComparisonDisplay();
                UpdateStatus("All changes accepted.");
            }
        }

        private void RevertChanges_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null) return;

            var result = MessageBox.Show(
                "Are you sure you want to revert all changes to their original values?",
                "Revert Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                SelectedFile.RevertAllChanges();
                UpdateComparisonDisplay();
                UpdateStatus("All changes reverted.");
            }
        }

        private void CopyComparison_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null) return;

            try
            {
                var summary = SelectedFile.GetComparisonSummary();
                System.Windows.Clipboard.SetText(summary);
                UpdateStatus("Comparison copied to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Window Events

        protected override void OnClosing(CancelEventArgs e)
        {
            // Check if there are any unsaved changes
            bool hasUnsavedChanges = HasUnsavedChanges();

            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Are you sure you want to exit without saving?",
                                           "Unsaved Changes",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }

            base.OnClosing(e);
        }

        private bool HasUnsavedChanges()
        {
            // Check if any files have pending changes in their tag comparison
            return _audioFiles.Where(file => file.TagComparison != null && file.TagComparison.Any(c => c.IsAccepted && !c.IsRejected)).Any();
        }

        private void RefreshOnlineSourceResults()
        {
            if (_selectedFile == null) return;
            
            // Get all cached results for the current file
            var cachedResults = _cacheManager.GetResults(_selectedFile.FilePath);
            
            // Clear and re-populate with sorted results
            _onlineSourceItems.Clear();
            var sortedResults = cachedResults
                .OrderByDescending(result => ExtractScoreFromDisplayName(result.DisplayName))
                .ThenBy(result => result.DisplayName);
            
            foreach (var result in sortedResults)
            {
                // Force ScoreCategory recalculation for existing items by triggering the getter
                _ = result.ScoreCategory; // This triggers the getter calculation
                _onlineSourceItems.Add(result);
            }
        }

        private double ExtractScoreFromDisplayName(string displayName)
        {
            // Extract percentage score from strings like "[85.2%]" or "[92%]"
            var match = System.Text.RegularExpressions.Regex.Match(displayName, @"\[(\d+\.?\d*)%\]");
            if (match.Success && double.TryParse(match.Groups[1].Value, out double score))
            {
                return score;
            }
            return 0.0; // Default score for items without explicit scores
        }


        #endregion

        private List<AudioFileInfo> GetSelectedFiles()
        {
            var selectedFiles = new List<AudioFileInfo>();

            // Check multiselect collections first (priority over TreeView selection)
            if (_selectedFiles.Count > 0)
            {
                selectedFiles.AddRange(_selectedFiles);
            }
            else if (_selectedAlbums.Count > 0)
            {
                selectedFiles.AddRange(_selectedAlbums.SelectMany(album => album.Tracks));
            }
            else
            {
                // Fall back to TreeView selection for backward compatibility
                var selectedItem = MainTreeView.SelectedItem;
                if (selectedItem is AudioFileInfo audioFile)
                {
                    selectedFiles.Add(audioFile);
                }
                else if (selectedItem is AlbumGroup albumGroup)
                {
                    selectedFiles.AddRange(albumGroup.Tracks);
                }
            }

            return selectedFiles;
        }

        #region Online Source Management

        private void InitializeOnlineSourceDropdown()
        {
            OnlineSourceComboBox.ItemsSource = _onlineSourceItems;
        }

        private void UpdateCoverArtFromOnlineSource(OnlineSourceItem selectedSource, AudioFileInfo targetFile)
        {
            try
            {
                System.Windows.Media.Imaging.BitmapImage? coverImage = null;
                string coverSource = "";

                string sourceName = "";
                
                if (selectedSource.SourceType == "MusicBrainz" && selectedSource.Source is MusicBrainzRelease mbRelease)
                {
                    coverImage = mbRelease.CoverArtImage;
                    coverSource = "MusicBrainz Cover Art Archive";
                    sourceName = "MusicBrainz";
                }
                else if (selectedSource.SourceType == "Discogs" && selectedSource.Source is DiscogsRelease discogsRelease)
                {
                    coverImage = discogsRelease.CoverArtImage;
                    coverSource = "Discogs";
                    sourceName = "Discogs";
                }

                if (coverImage != null)
                {
                    // Add the online cover to the available covers collection
                    targetFile.AddOnlineCover(sourceName, coverImage, coverSource);
                    
                    System.Diagnostics.Debug.WriteLine($"Cover art loaded from {coverSource} for {targetFile.FileName}");
                    
                    // Update batch editor if multiple files are selected
                    UpdateBatchAlbumCover();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating cover art: {ex.Message}");
            }
        }

        private async Task SearchMultiSourceCoverArtAsync(AudioFileInfo file)
        {
            if (file == null || string.IsNullOrEmpty(file.Artist) || string.IsNullOrEmpty(file.Album))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping cover art search - missing artist or album info");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Searching multi-source cover art for: {file.Artist} - {file.Album}");
                Console.WriteLine($"=== Starting cover art search for: {file.Artist} - {file.Album} ===");
                
                var coverSources = await _coverArtService.SearchCoverArtAsync(file.Artist, file.Album);
                
                System.Diagnostics.Debug.WriteLine($"CoverArtService returned {coverSources.Count} sources");
                
                foreach (var coverSource in coverSources)
                {
                    System.Diagnostics.Debug.WriteLine($"Processing cover source: {coverSource.Name}, HasImage: {coverSource.Image != null}");
                    
                    if (coverSource.Image != null)
                    {
                        file.AddOnlineCover(coverSource.Name, coverSource.Image, coverSource.Source);
                        System.Diagnostics.Debug.WriteLine($"Added cover from {coverSource.Name} for {file.FileName}");
                    }
                }

                if (coverSources.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Found {coverSources.Count} cover art sources for {file.FileName}");
                    
                    // Update batch editor if multiple files are selected
                    UpdateBatchAlbumCover();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No cover art found for {file.FileName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching multi-source cover art: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
            }
        }

        private async Task SearchMultiSourceCoverArtForAlbumAsync(AudioFileInfo sampleFile, List<AudioFileInfo> tracksInAlbum)
        {
            if (sampleFile == null || string.IsNullOrEmpty(sampleFile.Artist) || string.IsNullOrEmpty(sampleFile.Album))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping cover art search - missing artist or album info");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Searching multi-source cover art for album: {sampleFile.Artist} - {sampleFile.Album} ({tracksInAlbum.Count} tracks)");
                Console.WriteLine($"=== Starting cover art search for album: {sampleFile.Artist} - {sampleFile.Album} ({tracksInAlbum.Count} tracks) ===");
                
                var coverSources = await _coverArtService.SearchCoverArtAsync(sampleFile.Artist, sampleFile.Album);
                
                System.Diagnostics.Debug.WriteLine($"CoverArtService returned {coverSources.Count} sources for album");
                
                // Apply the found cover sources to ALL tracks in this album
                foreach (var track in tracksInAlbum)
                {
                    foreach (var coverSource in coverSources)
                    {
                        System.Diagnostics.Debug.WriteLine($"Processing cover source: {coverSource.Name}, HasImage: {coverSource.Image != null}");
                        
                        if (coverSource.Image != null)
                        {
                            track.AddOnlineCover(coverSource.Name, coverSource.Image, coverSource.Source);
                            System.Diagnostics.Debug.WriteLine($"Added cover from {coverSource.Name} for {track.FileName}");
                        }
                    }
                }

                if (coverSources.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Found {coverSources.Count} cover art sources for album {sampleFile.Album}, applied to {tracksInAlbum.Count} tracks");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No cover art found for album {sampleFile.Album}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching multi-source cover art for album: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
            }
        }

        private void UpdateBatchAlbumCover()
        {
            // Update batch cover for currently selected files
            var selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count > 1)
            {
                // Create a temporary album group for the selected files
                var tempGroup = new AlbumGroup();
                foreach (var file in selectedFiles)
                {
                    tempGroup.Tracks.Add(file);
                }
                UpdateBatchAlbumCover(tempGroup);
            }
        }

        private bool IsInBatchMode()
        {
            return BatchEditPanel.Visibility == Visibility.Visible;
        }

        private async void BatchSearchCoverArt_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Please select one or more files to search for cover art.", 
                              "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Disable the button during search
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "🔍 Searching...";
            }

            try
            {
                var searchTasks = new List<Task>();
                
                // Group files by album to avoid duplicate searches
                var albumGroups = selectedFiles
                    .Where(f => !string.IsNullOrEmpty(f.Artist) && !string.IsNullOrEmpty(f.Album))
                    .GroupBy(f => $"{f.Artist}|{f.Album}")
                    .ToList();

                foreach (var group in albumGroups)
                {
                    var firstFile = group.First();
                    var tracksInAlbum = group.ToList();
                    
                    // Search once per album and apply results to all tracks in that album
                    searchTasks.Add(SearchMultiSourceCoverArtForAlbumAsync(firstFile, tracksInAlbum));
                }

                if (searchTasks.Count > 0)
                {
                    await Task.WhenAll(searchTasks);
                    
                    // Count unique cover source names across all selected files
                    var uniqueCoverSources = selectedFiles
                        .SelectMany(f => f.AvailableCovers.Select(c => c.Name))
                        .Distinct()
                        .Count();
                    
                    MessageBox.Show($"Cover art search completed!\nFound {uniqueCoverSources} cover sources for {albumGroups.Count} album(s).", 
                                  "Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Update batch cover display
                    UpdateBatchAlbumCover();
                }
                else
                {
                    MessageBox.Show("No files with both artist and album information found.", 
                                  "Search Skipped", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching for cover art: {ex.Message}", 
                              "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Batch cover art search error: {ex}");
            }
            finally
            {
                // Re-enable the button
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "🔍 Search Cover Art";
                }
            }
        }

        private void BatchCoverSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BatchCoverSourceComboBox.SelectedItem is CoverSource selectedCover)
            {
                // Update the tracking variable
                _selectedBatchCoverSource = selectedCover.Name;
                
                var selectedFiles = GetSelectedFiles();
                
                // Apply the selected cover to all selected files that belong to the same album
                foreach (var file in selectedFiles)
                {
                    if (file.AvailableCovers.Any(c => c.Name == selectedCover.Name))
                    {
                        file.SelectedCoverSource = selectedCover.Name;
                    }
                }

                // Update the batch display WITHOUT calling UpdateBatchAlbumCover() to avoid recursion
                UpdateBatchAlbumCoverDisplay();
            }
        }

        private async void SearchCoverArt_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Please select one or more files to search for cover art.", 
                              "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Disable the button during search
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "🔍 Searching...";
            }

            try
            {
                var searchTasks = new List<Task>();
                
                // Group files by album to avoid duplicate searches
                var albumGroups = selectedFiles
                    .Where(f => !string.IsNullOrEmpty(f.Artist) && !string.IsNullOrEmpty(f.Album))
                    .GroupBy(f => $"{f.Artist}|{f.Album}")
                    .ToList();

                foreach (var group in albumGroups)
                {
                    var firstFile = group.First();
                    searchTasks.Add(SearchMultiSourceCoverArtAsync(firstFile));
                }

                if (searchTasks.Count > 0)
                {
                    await Task.WhenAll(searchTasks);
                    
                    var totalCoversFound = selectedFiles.Sum(f => f.AvailableCovers.Count);
                    MessageBox.Show($"Cover art search completed!\nFound {totalCoversFound} cover sources for {albumGroups.Count} album(s).", 
                                  "Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No files with both artist and album information found.", 
                                  "Search Skipped", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching for cover art: {ex.Message}", 
                              "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Cover art search error: {ex}");
            }
            finally
            {
                // Re-enable the button
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "🔍 Search Cover Art";
                }
            }
        }

        private async void SearchMusicBrainz_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (!selectedFiles.Any()) return;

            // Clear existing MusicBrainz results
            ClearOnlineSourceResults("MusicBrainz");
            
            // Clear match scores from previous searches
            ClearAllMatchScores();

            try
            {
                var originalCursor = Cursor;
                Cursor = System.Windows.Input.Cursors.Wait;

                var totalFiles = selectedFiles.Count;
                var processedCount = 0;
                var successfulSearches = 0;

                UpdateStatus($"Searching MusicBrainz for {totalFiles} files...");

                try
                {
                    foreach (var file in selectedFiles)
                    {
                        processedCount++;
                        UpdateStatus($"MusicBrainz search: {processedCount}/{totalFiles} - {file.FileName}");

                        var query = $"{file.Artist} {file.Album}".Trim();
                        if (!string.IsNullOrEmpty(query))
                        {
                            var results = await _musicBrainzService.SearchReleases(query);
                            
                            if (results.Any())
                            {
                                // Score and sort results by relevance to current file
                                var scoredResults = results.Take(5)
                                    .Select(release => new { 
                                        Release = release, 
                                        Score = CalculateMatchScore(file, release),
                                        File = file
                                    })
                                    .OrderByDescending(x => x.Score)
                                    .Take(3) // Top 3 results per file
                                    .ToList();

                                foreach (var scoredResult in scoredResults)
                                {
                                    var displayName = $"MB: {scoredResult.Release.Artist} - {scoredResult.Release.Title} ({scoredResult.Release.Date})";
                                    if (scoredResult.Release.TrackCount > 0)
                                        displayName += $" [{scoredResult.Release.TrackCount}T]";
                                    displayName += $" [For: {scoredResult.File.FileName}]";
                                    if (scoredResult.Score > 0)
                                        displayName += $" [{scoredResult.Score * 100:F1}%]";
                                        
                                    AddOnlineSourceResult(new OnlineSourceItem
                                    {
                                        DisplayName = displayName,
                                        Source = scoredResult.Release,
                                        SourceType = "MusicBrainz",
                                        Data = scoredResult.File // Store which file this result is for
                                    }, scoredResult.File);
                                    
                                    // Load cover art asynchronously
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var coverUrl = await _musicBrainzService.GetCoverArtUrl(scoredResult.Release.Id);
                                            if (!string.IsNullOrEmpty(coverUrl))
                                            {
                                                scoredResult.Release.CoverArtUrl = coverUrl;
                                                scoredResult.Release.CoverArtImage = await _musicBrainzService.LoadCoverArtImage(coverUrl);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"Error loading MusicBrainz cover art: {ex.Message}");
                                        }
                                    });
                                }

                                successfulSearches++;
                            }
                        }

                        // MusicBrainz rate limiting: 1 request per second
                        if (processedCount < totalFiles)
                        {
                            await Task.Delay(1100); // Wait 1.1 seconds between requests
                        }
                    }

                    UpdateStatus($"MusicBrainz search completed: {successfulSearches}/{totalFiles} files found results");
                        }
                finally
                {
                    Cursor = originalCursor;
                }
            }
            catch (Exception ex)
            {
                var friendlyMessage = HttpClientManager.GetFriendlyErrorMessage(ex);
                MessageBox.Show($"Error searching MusicBrainz: {friendlyMessage}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatus("MusicBrainz search failed due to network error");
            }
        }

        private void AddOnlineSourceResult(OnlineSourceItem result, AudioFileInfo targetFile)
        {
            // Update cache for the target file
            var currentResults = _cacheManager.GetResults(targetFile.FilePath);
            currentResults.Add(result);
            _cacheManager.StoreResults(targetFile.FilePath, currentResults);
            
            // If this is the currently selected file, refresh the sorted results
            if (SelectedFile?.FilePath == targetFile.FilePath)
            {
                RefreshOnlineSourceResults();
            }
        }

        private void ClearOnlineSourceResults(string sourceType, AudioFileInfo? specificFile = null)
        {
            // Remove from UI
            var existingItems = _onlineSourceItems.Where(x => x.SourceType == sourceType).ToList();
            foreach (var item in existingItems)
            {
                _onlineSourceItems.Remove(item);
            }
            
            // Update cache for all affected files
            if (specificFile != null)
            {
                var currentResults = _cacheManager.GetResults(specificFile.FilePath);
                var filteredResults = currentResults.Where(x => x.SourceType != sourceType).ToList();
                _cacheManager.StoreResults(specificFile.FilePath, filteredResults);
            }
            else
            {
                // Clear from all files in cache
                foreach (var filePath in _cacheManager.GetCachedFilePaths().ToList())
                {
                    var currentResults = _cacheManager.GetResults(filePath);
                    var filteredResults = currentResults.Where(x => x.SourceType != sourceType).ToList();
                    _cacheManager.StoreResults(filePath, filteredResults);
                }
            }
        }

        private async void FingerprintIdentify_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (!selectedFiles.Any()) return;

            var settings = SettingsManager.LoadSettings();
            if (!settings.HasValidAcoustIdCredentials())
            {
                var result = MessageBox.Show(
                    "AcoustID API key is not configured. Would you like to open settings to configure it?",
                    "AcoustID Configuration Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    Settings_Click(sender, e);
                }
                return;
            }

            // Clear existing fingerprint results
            ClearOnlineSourceResults("Fingerprint");
            
            // Clear match scores from previous searches
            ClearAllMatchScores();

            try
            {
                var originalCursor = Cursor;
                Cursor = System.Windows.Input.Cursors.Wait;

                var totalFiles = selectedFiles.Count;
                var processedCount = 0;
                var successfulIdentifications = 0;

                UpdateStatus($"Fingerprinting {totalFiles} files...");

                try
                {
                    var acoustIdService = new AcoustIdService(settings.AcoustIdApiKey);

                    foreach (var file in selectedFiles)
                    {
                        processedCount++;
                        UpdateStatus($"Fingerprinting: {processedCount}/{totalFiles} - {file.FileName}");

                        var results = await acoustIdService.IdentifyByFingerprintAsync(file.FilePath);

                        if (results.Any())
                        {
                            // Add fingerprint results for this file
                            foreach (var result in results.Take(2)) // Top 2 results per file
                            {
                                var title = result.Title ?? "Unknown Title";
                                var artist = result.Artist ?? "Unknown Artist";
                                var album = result.Album ?? "Unknown Album";
                                var score = (result.Score * 100).ToString("F1");
                                
                                var displayName = $"FP: {artist} - {title} ({score}%)";
                                if (!string.IsNullOrEmpty(album) && album != "Unknown Album")
                                    displayName += $" [{album}]";
                                displayName += $" [For: {file.FileName}]";
                                    
                                AddOnlineSourceResult(new OnlineSourceItem
                                {
                                    DisplayName = displayName,
                                    Source = result,
                                    SourceType = "Fingerprint",
                                    Title = title,
                                    Artist = artist,
                                    Album = album,
                                    Score = score + "%",
                                    AdditionalInfo = $"Duration: {result.Duration}s",
                                    Data = file // Store which file this result is for
                                }, file);
                            }

                            // Auto-apply best result to the file's comparison
                            var bestResult = results.OrderByDescending(r => r.Score).First();
                            var newTags = TagSnapshot.FromAcoustIdResult(bestResult, file);
                            file.UpdateComparison(newTags, $"Fingerprint: {bestResult.Score * 100:F1}% match");

                            successfulIdentifications++;
                        }

                        // Small delay to be respectful to the API
                        if (processedCount < totalFiles)
                        {
                            await Task.Delay(500); // 500ms delay between fingerprint requests
                        }
                    }

                    UpdateStatus($"Fingerprinting completed: {successfulIdentifications}/{totalFiles} files identified");
                    
                    // Switch to comparison tab if any results found
                    if (successfulIdentifications > 0)
                    {
                        InfoTabControl.SelectedIndex = 1; // Tag Comparison tab
                        UpdateComparisonDisplay();
                    }
                    
                        }
                finally
                {
                    Cursor = originalCursor;
                }
            }
            catch (Exception ex)
            {
                var friendlyMessage = HttpClientManager.GetFriendlyErrorMessage(ex);
                MessageBox.Show($"Error during fingerprinting: {friendlyMessage}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatus("Fingerprinting failed due to network error");
            }
        }

        private async void SearchDiscogs_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (!selectedFiles.Any()) return;

            // Clear existing Discogs results
            ClearOnlineSourceResults("Discogs");
            
            // Clear match scores from previous searches
            ClearAllMatchScores();

            try
            {
                var originalCursor = Cursor;
                Cursor = System.Windows.Input.Cursors.Wait;

                var totalFiles = selectedFiles.Count;
                var processedCount = 0;
                var successfulSearches = 0;

                UpdateStatus($"Searching Discogs for {totalFiles} files...");

                try
                {
                    foreach (var file in selectedFiles)
                    {
                        processedCount++;
                        UpdateStatus($"Discogs search: {processedCount}/{totalFiles} - {file.FileName}");

                        var query = $"{file.Artist} {file.Album}".Trim();
                        if (!string.IsNullOrEmpty(query))
                        {
                            var results = await _discogsService.SearchReleases(query);
                            
                            if (results.Any())
                            {
                                // Score and sort results by relevance to current file
                                var scoredResults = results.Take(5)
                                    .Select(release => new { 
                                        Release = release, 
                                        Score = CalculateMatchScore(file, release),
                                        File = file
                                    })
                                    .OrderByDescending(x => x.Score)
                                    .Take(3) // Top 3 results per file
                                    .ToList();

                                foreach (var scoredResult in scoredResults)
                                {
                                    var displayName = $"DC: {scoredResult.Release.Artist} - {scoredResult.Release.Title} ({scoredResult.Release.Year})";
                                    if (scoredResult.Release.TrackCount > 0)
                                        displayName += $" [{scoredResult.Release.TrackCount}T]";
                                    displayName += $" [For: {scoredResult.File.FileName}]";
                                    if (scoredResult.Score > 0)
                                        displayName += $" [{scoredResult.Score * 100:F1}%]";
                                        
                                    AddOnlineSourceResult(new OnlineSourceItem
                                    {
                                        DisplayName = displayName,
                                        Source = scoredResult.Release,
                                        SourceType = "Discogs",
                                        Data = scoredResult.File // Store which file this result is for
                                    }, scoredResult.File);
                                    
                                    // Load cover art asynchronously if URL is available
                                    if (!string.IsNullOrEmpty(scoredResult.Release.CoverArtUrl))
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                scoredResult.Release.CoverArtImage = await _musicBrainzService.LoadCoverArtImage(scoredResult.Release.CoverArtUrl);
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"Error loading Discogs cover art: {ex.Message}");
                                            }
                                        });
                                    }
                                }

                                successfulSearches++;
                            }
                        }

                        // Small delay to be respectful to the API
                        if (processedCount < totalFiles)
                        {
                            await Task.Delay(250); // 250ms delay between requests
                        }
                    }

                    UpdateStatus($"Discogs search completed: {successfulSearches}/{totalFiles} files found results");
                        }
                finally
                {
                    Cursor = originalCursor;
                }
            }
            catch (Exception ex)
            {
                var friendlyMessage = HttpClientManager.GetFriendlyErrorMessage(ex);
                MessageBox.Show($"Error searching Discogs: {friendlyMessage}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatus("Discogs search failed due to network error");
            }
        }

        private void OnlineSource_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateMatchScores();
            
            // Automatically apply the selected source to tag comparison
            if (OnlineSourceComboBox.SelectedItem is OnlineSourceItem selectedSource)
            {
                // Only auto-apply if there's a target file available
                if (selectedSource.Data is AudioFileInfo || SelectedFile != null)
                {
                    ApplyOnlineSourceToComparison(selectedSource);
                }
            }
        }

        private void UpdateMatchScores()
        {
            // Clear existing match scores
            foreach (var file in _audioFiles)
            {
                file.MatchScore = "";
            }

            if (OnlineSourceComboBox.SelectedItem is not OnlineSourceItem selectedSource)
                return;

            try
            {
                if (selectedSource.SourceType == "MusicBrainz" && selectedSource.Source is MusicBrainzRelease mbRelease)
                {
                    UpdateMusicBrainzMatchScores(mbRelease);
                }
                else if (selectedSource.SourceType == "Discogs" && selectedSource.Source is DiscogsRelease discogsRelease)
                {
                    UpdateDiscogsMatchScores(discogsRelease);
                }
                else if (selectedSource.SourceType == "Fingerprint" && selectedSource.Source is AcoustIdResult acoustIdResult)
                {
                    UpdateFingerprintMatchScores(acoustIdResult, selectedSource.Data as AudioFileInfo);
                }
            }
            catch (Exception ex)
            {
                // Silently handle errors in match score calculation
                System.Diagnostics.Debug.WriteLine($"Error updating match scores: {ex.Message}");
            }
        }

        private void UpdateMusicBrainzMatchScores(MusicBrainzRelease release)
        {
            foreach (var file in _audioFiles)
            {
                var score = CalculateMatchScore(file, release) * 100;
                file.MatchScore = score > 0 ? $"{score:F0}%" : "";
            }
        }

        private void UpdateDiscogsMatchScores(DiscogsRelease release)
        {
            foreach (var file in _audioFiles)
            {
                var score = CalculateMatchScore(file, release) * 100;
                file.MatchScore = score > 0 ? $"{score:F0}%" : "";
            }
        }

        private void UpdateFingerprintMatchScores(AcoustIdResult result, AudioFileInfo? targetFile)
        {
            if (targetFile != null)
            {
                var score = result.Score * 100;
                targetFile.MatchScore = $"{score:F0}%";
            }
        }

        private void ClearAllMatchScores()
        {
            foreach (var file in _audioFiles)
            {
                file.MatchScore = "";
            }
        }

        private void ApplyOnlineSourceToComparison(OnlineSourceItem selectedSource)
        {

            try
            {
                // Determine which file this result is for
                AudioFileInfo targetFile;
                
                if (selectedSource.Data is AudioFileInfo specificFile)
                {
                    // This result is for a specific file
                    targetFile = specificFile;
                }
                else
                {
                    // Fallback to currently selected file
                    if (SelectedFile == null)
                    {
                        MessageBox.Show("No target file selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    targetFile = SelectedFile;
                }

                TagSnapshot newTags;

                if (selectedSource.SourceType == "MusicBrainz" && selectedSource.Source is MusicBrainzRelease mbRelease)
                {
                    newTags = TagSnapshot.FromMusicBrainzRelease(mbRelease, null, targetFile);
                }
                else if (selectedSource.SourceType == "Discogs" && selectedSource.Source is DiscogsRelease discogsRelease)
                {
                    newTags = TagSnapshot.FromDiscogsRelease(discogsRelease, null, targetFile);
                }
                else if (selectedSource.SourceType == "Fingerprint" && selectedSource.Source is AcoustIdResult acoustIdResult)
                {
                    newTags = TagSnapshot.FromAcoustIdResult(acoustIdResult, targetFile);
                }
                else
                {
                    MessageBox.Show("Invalid source selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Apply the comparison using the existing tag comparison system
                targetFile.UpdateComparison(newTags, $"Online: {selectedSource.SourceType}");

                // Update cover art if available from online source
                UpdateCoverArtFromOnlineSource(selectedSource, targetFile);

                // Switch to comparison tab to show results
                InfoTabControl.SelectedIndex = 1; // Tag Comparison tab
                
                // Update the comparison display
                UpdateComparisonDisplay();
                
                // If the target file is not the currently selected file, select it
                if (targetFile != SelectedFile)
                {
                    SelectedFile = targetFile;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying online source: {ex.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectBestOverallMatch()
        {
            if (_onlineSourceItems.Count == 0 || SelectedFile == null) return;

            double bestScore = 0.0;
            int bestIndex = -1;
            OnlineSourceItem? bestItem = null;

            for (int i = 0; i < _onlineSourceItems.Count; i++)
            {
                var item = _onlineSourceItems[i];
                double score = 0.0;

                if (item.SourceType == "MusicBrainz" && item.Source is MusicBrainzRelease mbRelease)
                {
                    score = CalculateMatchScore(SelectedFile, mbRelease);
                }
                else if (item.SourceType == "Discogs" && item.Source is DiscogsRelease discogsRelease)
                {
                    score = CalculateMatchScore(SelectedFile, discogsRelease);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                    bestItem = item;
                }
            }

            // Auto-select and apply if we have a good match (70% threshold)
            if (bestIndex >= 0 && bestScore >= 0.7 && bestItem != null)
            {
                OnlineSourceComboBox.SelectedIndex = bestIndex;
                
                // Automatically apply the best match
                try
                {
                    TagSnapshot newTags;

                    if (bestItem.SourceType == "MusicBrainz" && bestItem.Source is MusicBrainzRelease mbRelease)
                    {
                        newTags = TagSnapshot.FromMusicBrainzRelease(mbRelease, null, SelectedFile);
                    }
                    else if (bestItem.SourceType == "Discogs" && bestItem.Source is DiscogsRelease discogsRelease)
                    {
                        newTags = TagSnapshot.FromDiscogsRelease(discogsRelease, null, SelectedFile);
                    }
                    else
                    {
                        return; // Invalid source, don't apply
                    }

                    // Apply the comparison and switch to comparison tab
                    SelectedFile.UpdateComparison(newTags, $"Auto-selected: {bestItem.SourceType} [Match: {bestScore * 100:F1}%]");
                    InfoTabControl.SelectedIndex = 1; // Tag Comparison tab
                    UpdateComparisonDisplay();
                }
                catch (Exception ex)
                {
                    // If auto-apply fails, just select the item without applying
                    MessageBox.Show($"Auto-apply failed: {ex.Message}", "Auto-Apply Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private double CalculateMatchScore(AudioFileInfo? file, MusicBrainzRelease release)
        {
            if (file == null) return 0.0;

            double score = 0.0;
            double maxScore = 0.0;

            // Artist match (most important - 35% weight)
            maxScore += 0.35;
            if (!string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(release.Artist))
            {
                score += 0.35 * CalculateStringSimilarity(file.Artist, release.Artist);
            }

            // Album/Title match (30% weight)
            maxScore += 0.30;
            if (!string.IsNullOrEmpty(file.Album) && !string.IsNullOrEmpty(release.Title))
            {
                score += 0.30 * CalculateStringSimilarity(file.Album, release.Title);
            }

            // Track count match (20% weight)
            maxScore += 0.20;
            if (release.TrackCount > 0)
            {
                var loadedTrackCount = _audioFiles.Count;
                if (loadedTrackCount > 0)
                {
                    // Perfect match gets full score, with decreasing score for differences
                    var trackCountDiff = Math.Abs(release.TrackCount - loadedTrackCount);
                    if (trackCountDiff == 0)
                    {
                        score += 0.20; // Perfect match
                    }
                    else if (trackCountDiff <= 2)
                    {
                        score += 0.20 * (1.0 - trackCountDiff / 3.0); // Partial credit for close matches
                    }
                    else if (trackCountDiff <= 5)
                    {
                        score += 0.20 * 0.3; // Small credit for reasonably close matches
                    }
                }
            }

            // Year match (10% weight)
            maxScore += 0.10;
            if (file.Year > 0 && !string.IsNullOrEmpty(release.Date))
            {
                if (ExtractYearFromDate(release.Date) == file.Year)
                {
                    score += 0.10;
                }
                else
                {
                    // Partial credit for close years
                    var yearDiff = Math.Abs((int)ExtractYearFromDate(release.Date) - (int)file.Year);
                    if (yearDiff <= 2) score += 0.10 * (1.0 - yearDiff / 3.0);
                }
            }

            // Title match (5% weight) - for when album is missing
            maxScore += 0.05;
            if (!string.IsNullOrEmpty(file.Title) && !string.IsNullOrEmpty(release.Title))
            {
                score += 0.05 * CalculateStringSimilarity(file.Title, release.Title);
            }

            return maxScore > 0 ? score / maxScore : 0.0;
        }

        private double CalculateMatchScore(AudioFileInfo? file, DiscogsRelease release)
        {
            if (file == null) return 0.0;

            double score = 0.0;
            double maxScore = 0.0;

            // Artist match (most important - 35% weight)
            maxScore += 0.35;
            if (!string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(release.Artist))
            {
                score += 0.35 * CalculateStringSimilarity(file.Artist, release.Artist);
            }

            // Album/Title match (30% weight)
            maxScore += 0.30;
            if (!string.IsNullOrEmpty(file.Album) && !string.IsNullOrEmpty(release.Title))
            {
                score += 0.30 * CalculateStringSimilarity(file.Album, release.Title);
            }

            // Track count match (20% weight)
            maxScore += 0.20;
            if (release.TrackCount > 0)
            {
                var loadedTrackCount = _audioFiles.Count;
                if (loadedTrackCount > 0)
                {
                    // Perfect match gets full score, with decreasing score for differences
                    var trackCountDiff = Math.Abs(release.TrackCount - loadedTrackCount);
                    if (trackCountDiff == 0)
                    {
                        score += 0.20; // Perfect match
                    }
                    else if (trackCountDiff <= 2)
                    {
                        score += 0.20 * (1.0 - trackCountDiff / 3.0); // Partial credit for close matches
                    }
                    else if (trackCountDiff <= 5)
                    {
                        score += 0.20 * 0.3; // Small credit for reasonably close matches
                    }
                }
            }

            // Year match (10% weight)
            maxScore += 0.10;
            if (file.Year > 0 && !string.IsNullOrEmpty(release.Year))
            {
                if (uint.TryParse(release.Year, out uint releaseYear) && releaseYear == file.Year)
                {
                    score += 0.10;
                }
                else if (uint.TryParse(release.Year, out releaseYear))
                {
                    // Partial credit for close years
                    var yearDiff = Math.Abs((int)releaseYear - (int)file.Year);
                    if (yearDiff <= 2) score += 0.10 * (1.0 - yearDiff / 3.0);
                }
            }

            // Title match (5% weight) - for when album is missing
            maxScore += 0.05;
            if (!string.IsNullOrEmpty(file.Title) && !string.IsNullOrEmpty(release.Title))
            {
                score += 0.05 * CalculateStringSimilarity(file.Title, release.Title);
            }

            return maxScore > 0 ? score / maxScore : 0.0;
        }

        private static double CalculateStringSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0.0;

            // Normalize strings for comparison
            str1 = NormalizeForComparison(str1);
            str2 = NormalizeForComparison(str2);

            // Exact match
            if (str1.Equals(str2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Contains match
            if (str1.Contains(str2, StringComparison.OrdinalIgnoreCase) || 
                str2.Contains(str1, StringComparison.OrdinalIgnoreCase))
                return 0.8;

            // Calculate Levenshtein distance similarity
            var distance = CalculateLevenshteinDistance(str1, str2);
            var maxLength = Math.Max(str1.Length, str2.Length);
            
            if (maxLength == 0) return 1.0;
            
            var similarity = 1.0 - (double)distance / maxLength;
            return Math.Max(0.0, similarity);
        }

        private static string NormalizeForComparison(string input)
        {
            return input.ToLowerInvariant()
                       .Replace("&", "and")
                       .Replace("'", "")
                       .Replace("-", " ")
                       .Replace("  ", " ")
                       .Trim();
        }

        private static int CalculateLevenshteinDistance(string str1, string str2)
        {
            var len1 = str1.Length;
            var len2 = str2.Length;
            var matrix = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            return matrix[len1, len2];
        }

        private static uint ExtractYearFromDate(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString) || dateString.Length < 4)
                return 0;

            var yearString = dateString.Length >= 4 ? dateString.Substring(0, 4) : dateString;
            return uint.TryParse(yearString, out uint year) ? year : 0;
        }

        #endregion

        #region Utility Methods

        private System.Windows.Controls.DataGrid? FindDataGrid()
        {
            // Find the main DataGrid by walking the visual tree
            return FindVisualChild<System.Windows.Controls.DataGrid>(this);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion

        #region Update Checking

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesOnStartup();
            LoadWindowAndColumnSettings();
            UpdateEditorPanelVisibility();
        }

        private void LoadWindowAndColumnSettings()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                
                if (settings.RememberWindowState)
                {
                    // Restore window size first
                    Width = settings.WindowWidth;
                    Height = settings.WindowHeight;
                    
                    // Restore window position if available
                    if (!double.IsNaN(settings.WindowX) && !double.IsNaN(settings.WindowY))
                    {
                        Left = settings.WindowX;
                        Top = settings.WindowY;
                        
                        // Ensure window is still visible on current screen setup
                        EnsureWindowVisible();
                    }
                    else if (settings.CenterOnStartup)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }
                    
                    // Restore window state (Normal, Minimized, Maximized)
                    switch (settings.WindowState)
                    {
                        case 1:
                            WindowState = WindowState.Minimized;
                            break;
                        case 2:
                            WindowState = WindowState.Maximized;
                            break;
                        default:
                            WindowState = WindowState.Normal;
                            break;
                    }
                }

                // Restore column widths if enabled
                if (settings.RememberColumnWidths)
                {
                    RestoreColumnWidths(settings.ColumnWidths);
                }
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading window/column settings: {ex.Message}");
                // Fallback to default window state
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch (ArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid window/column settings: {ex.Message}");
                // Fallback to default window state
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win32 error loading settings: {ex.Message}");
                // Fallback to default window state
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void EnsureWindowVisible()
        {
            try
            {
                // Get current window bounds
                var windowBounds = new System.Windows.Rect(Left, Top, Width, Height);
                
                // Check if window intersects with any screen
                bool isVisible = false;
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var screenBounds = new System.Windows.Rect(
                        screen.WorkingArea.X, screen.WorkingArea.Y, 
                        screen.WorkingArea.Width, screen.WorkingArea.Height);
                    
                    if (windowBounds.IntersectsWith(screenBounds))
                    {
                        isVisible = true;
                        break;
                    }
                }
                
                if (!isVisible)
                {
                    // Move window to primary screen
                    var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
                    if (primaryScreen.HasValue)
                    {
                        Left = primaryScreen.Value.X + (primaryScreen.Value.Width - Width) / 2;
                        Top = primaryScreen.Value.Y + (primaryScreen.Value.Height - Height) / 2;
                    }
                    else
                    {
                        // Fallback to WPF centering if no primary screen found
                        WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Fallback: center on screen if screen bounds operations fail
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Fallback: center on screen if Windows Forms screen access fails
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void RestoreColumnWidths(double[] columnWidths)
        {
            try
            {
                var dataGrid = FindDataGrid();
                if (dataGrid == null || columnWidths == null) return;

                var columns = dataGrid.Columns.OfType<DataGridTextColumn>().ToArray();
                
                for (int i = 0; i < Math.Min(columns.Length, columnWidths.Length); i++)
                {
                    columns[i].Width = new DataGridLength(columnWidths[i], DataGridLengthUnitType.Star);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring column widths: {ex.Message}");
            }
        }

        private void SaveWindowAndColumnSettings()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                
                if (settings.RememberWindowState)
                {
                    // Save current window state
                    settings.WindowState = (int)WindowState;
                    settings.WindowMaximized = WindowState == WindowState.Maximized;
                    settings.WindowMinimized = WindowState == WindowState.Minimized;
                    
                    // Save window position and size only when in Normal state
                    if (WindowState == WindowState.Normal)
                    {
                        settings.WindowX = Left;
                        settings.WindowY = Top;
                        settings.WindowWidth = Width;
                        settings.WindowHeight = Height;
                    }
                    else
                    {
                        // For maximized/minimized states, save the restore bounds
                        if (RestoreBounds != System.Windows.Rect.Empty)
                        {
                            settings.WindowX = RestoreBounds.X;
                            settings.WindowY = RestoreBounds.Y;
                            settings.WindowWidth = RestoreBounds.Width;
                            settings.WindowHeight = RestoreBounds.Height;
                        }
                    }
                }

                // Save column widths if enabled
                if (settings.RememberColumnWidths)
                {
                    var columnWidths = GetCurrentColumnWidths();
                    if (columnWidths != null && columnWidths.Length > 0)
                    {
                        settings.ColumnWidths = columnWidths;
                    }
                }

                SettingsManager.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window/column settings: {ex.Message}");
            }
        }

        private double[]? GetCurrentColumnWidths()
        {
            try
            {
                var dataGrid = FindDataGrid();
                if (dataGrid == null) return null;

                var columns = dataGrid.Columns.OfType<DataGridTextColumn>().ToArray();
                var widths = new double[columns.Length];

                for (int i = 0; i < columns.Length; i++)
                {
                    if (columns[i].Width.IsStar)
                    {
                        widths[i] = columns[i].Width.Value;
                    }
                    else
                    {
                        // Convert pixel width to approximate star value based on current total width
                        var totalWidth = dataGrid.ActualWidth;
                        widths[i] = totalWidth > 0 ? columns[i].ActualWidth / (totalWidth / 11.9) : 1.0; // 11.9 is sum of default star values
                    }
                }

                return widths;
            }
            catch
            {
                return null;
            }
        }

        private async Task CheckForUpdatesOnStartup()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                
                if (!settings.CheckForUpdates)
                    return;

                if (!_updateService.ShouldCheckForUpdates(settings.LastUpdateCheck))
                    return;

                var updateInfo = await _updateService.CheckForUpdatesAsync();
                if (updateInfo != null && _updateService.IsUpdateAvailable(settings.Version, updateInfo))
                {
                    ShowUpdateNotification(updateInfo);
                }

                // Update last check time
                settings.LastUpdateCheck = DateTime.Now;
                SettingsManager.SaveSettings(settings);
            }
            catch (HttpRequestException)
            {
                // Silently fail network issues - don't interrupt user experience
            }
            catch (TaskCanceledException)
            {
                // Silently fail timeout issues - don't interrupt user experience
            }
            catch (InvalidOperationException)
            {
                // Silently fail service issues - don't interrupt user experience
            }
        }

        public async Task<UpdateInfo?> CheckForUpdatesManually()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                var updateInfo = await _updateService.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                    settings.LastUpdateCheck = DateTime.Now;
                    SettingsManager.SaveSettings(settings);
                    
                    if (_updateService.IsUpdateAvailable(settings.Version, updateInfo))
                    {
                        return updateInfo;
                    }
                }
                
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            var message = $"A new version of TID3 is available!\n\n" +
                         $"Current Version: {SettingsManager.LoadSettings().Version}\n" +
                         $"Latest Version: {updateInfo.Version}\n" +
                         $"Released: {updateInfo.PublishedAt:yyyy-MM-dd}\n\n" +
                         $"Would you like to download the update?";

            var result = MessageBox.Show(message, "Update Available", 
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = updateInfo.DownloadUrl,
                        UseShellExecute = true
                    });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show("Could not open the download page. Please visit the GitHub releases page manually.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (InvalidOperationException)
                {
                    MessageBox.Show("Could not open the download page. Please visit the GitHub releases page manually.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Save window and column settings before closing
            SaveWindowAndColumnSettings();
            
            // Cleanup resources
            _updateService?.Dispose();
            
            // Clear online source cache
            _cacheManager.ClearAllResults();
            
            // Cleanup scrollbar optimization timer
            if (_scrollbarOptimizationTimer != null)
            {
                _scrollbarOptimizationTimer.Stop();
                _scrollbarOptimizationTimer.Tick -= ScrollbarOptimizationTimer_Tick;
                _scrollbarOptimizationTimer = null;
            }
            
            // Cleanup location change timer
            if (_locationChangeTimer != null)
            {
                _locationChangeTimer.Stop();
                _locationChangeTimer.Tick -= LocationChangeTimer_Tick;
                _locationChangeTimer = null;
            }
            
            // Unsubscribe from events
            SizeChanged -= MainWindow_SizeChanged_ScrollOptimization;
            StateChanged -= MainWindow_StateChanged;
            LocationChanged -= MainWindow_LocationChanged;
            SourceInitialized -= MainWindow_SourceInitialized;
            
            base.OnClosed(e);
        }

        #endregion
    }
}