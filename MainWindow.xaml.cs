using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TID3
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly TagService _tagService;
        private readonly MusicBrainzService _musicBrainzService;
        private readonly DiscogsService _discogsService;
        private readonly UpdateService _updateService;
        private readonly ObservableCollection<AudioFileInfo> _audioFiles;
        private readonly ObservableCollection<OnlineSourceItem> _onlineSourceItems;

        public ObservableCollection<AudioFileInfo> AudioFiles => _audioFiles;
        public ObservableCollection<OnlineSourceItem> OnlineSourceItems => _onlineSourceItems;

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
                // Clear the online source dropdown when switching files
                _onlineSourceItems.Clear();
                
                // Subscribe to new file's events
                if (_selectedFile != null)
                {
                    _selectedFile.PropertyChanged += SelectedFile_PropertyChanged;
                }
                
                // Update button state after initialization is complete
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateApplyButtonState();
                }));
            }
        }

        public bool IsFileSelected => SelectedFile != null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _tagService = new TagService();
            _musicBrainzService = new MusicBrainzService();
            _discogsService = new DiscogsService();
            _updateService = new UpdateService();
            _audioFiles = new ObservableCollection<AudioFileInfo>();
            _onlineSourceItems = new ObservableCollection<OnlineSourceItem>();

            // Subscribe to collection changes to update UI
            InitializeOnlineSourceDropdown();
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
        }

        private void SelectedFile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioFileInfo.TagComparison))
            {
                UpdateComparisonDisplay();
            }
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

        private void LoadFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select folder containing audio files";
                folderDialog.ShowNewFolderButton = false;

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var supportedExtensions = new[] { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma" };
                    var files = Directory.GetFiles(folderDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToArray();

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
            }
        }

        private async void LoadFilesAsync(string[] filePaths)
        {
            try
            {
                await Task.Run(() =>
                {
                    foreach (var fileName in filePaths)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var audioFile = _tagService.LoadFile(fileName);
                            if (audioFile != null)
                            {
                                audioFile.CreateSnapshot(); // Create initial snapshot for comparison
                                _audioFiles.Add(audioFile);
                            }
                        });
                    }
                });

                UpdateStatus($"Successfully loaded {_audioFiles.Count} audio files.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null)
            {
                MessageBox.Show("Please select a file to save.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_tagService.SaveFile(SelectedFile))
                {
                    UpdateStatus($"Successfully saved: {SelectedFile.FileName}");
                    MessageBox.Show("File saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to save file. Please check the file permissions.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                int saved = await Task.Run(() =>
                {
                    int count = 0;
                    foreach (var file in _audioFiles)
                    {
                        if (_tagService.SaveFile(file))
                            count++;
                    }
                    return count;
                });

                UpdateStatus($"Saved {saved} of {_audioFiles.Count} files successfully.");
                MessageBox.Show($"Saved {saved} of {_audioFiles.Count} files.", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during batch save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Online Database Integration - Legacy Methods (replaced with dropdown)

        // Note: These methods have been replaced with enhanced dropdown functionality
        // in the Online Source Management section

        private async void SearchMusicBrainz_Click_Legacy(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null)
            {
                MessageBox.Show("Please select a file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var query = $"{SelectedFile.Artist} {SelectedFile.Album}".Trim();
            if (string.IsNullOrEmpty(query))
            {
                query = Path.GetFileNameWithoutExtension(SelectedFile.FileName);
            }

            if (string.IsNullOrEmpty(query))
            {
                MessageBox.Show("Please select a file with artist, album information, or a descriptive filename.",
                              "Insufficient Information",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                return;
            }

            try
            {
                UpdateStatus("Searching MusicBrainz database...");
                var releases = await _musicBrainzService.SearchReleases(query);

                if (!releases.Any())
                {
                    MessageBox.Show("No results found on MusicBrainz. Try different search terms.",
                                  "No Results",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                    UpdateStatus("MusicBrainz search completed - no results found.");
                    return;
                }

                // For now, show results in a simple message box
                var message = $"Found {releases.Count} results:\n\n";
                foreach (var release in releases.Take(5))
                {
                    message += $"� {release.Artist} - {release.Title} ({release.Date}) [Score: {release.Score}]\n";
                }
                if (releases.Count > 5)
                    message += $"\n... and {releases.Count - 5} more results";

                // Create comparison with the first result for demonstration
                if (releases.Any())
                {
                    var bestMatch = releases.First();
                    var newTags = TagSnapshot.FromMusicBrainzRelease(bestMatch, null, SelectedFile);
                    SelectedFile.UpdateComparison(newTags, "MusicBrainz");
                    UpdateComparisonDisplay();
                    
                    // Automatically switch to tag comparison tab
                    InfoTabControl.SelectedIndex = 1;
                }

                MessageBox.Show(message, "MusicBrainz Results", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus($"Found {releases.Count} MusicBrainz results.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching MusicBrainz: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("MusicBrainz search failed.");
            }
        }

        private async void SearchDiscogs_Click_Legacy(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null)
            {
                MessageBox.Show("Please select a file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var query = $"{SelectedFile.Artist} {SelectedFile.Album}".Trim();
            if (string.IsNullOrEmpty(query))
            {
                query = Path.GetFileNameWithoutExtension(SelectedFile.FileName);
            }

            try
            {
                UpdateStatus("Searching Discogs database...");
                var releases = await _discogsService.SearchReleases(query);

                if (!releases.Any())
                {
                    MessageBox.Show("No results found on Discogs. Try different search terms.",
                                  "No Results",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                    return;
                }

                var message = $"Found {releases.Count} Discogs results!\n\n";
                foreach (var release in releases.Take(3))
                {
                    message += $"� {release.Artist} - {release.Title} ({release.Year})\n";
                }

                // Create comparison with the first result for demonstration
                if (releases.Any())
                {
                    var bestMatch = releases.First();
                    var newTags = TagSnapshot.FromDiscogsRelease(bestMatch, null, SelectedFile);
                    SelectedFile.UpdateComparison(newTags, "Discogs");
                    UpdateComparisonDisplay();
                    
                    // Automatically switch to tag comparison tab
                    InfoTabControl.SelectedIndex = 1;
                }

                MessageBox.Show(message, "Discogs Results", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus($"Found {releases.Count} results on Discogs.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching Discogs: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Batch Operations

        private void BatchEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!_audioFiles.Any())
            {
                MessageBox.Show("Please load some audio files first.", "No Files", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show($"Batch editing for {_audioFiles.Count} files will be implemented here.", "Batch Edit", MessageBoxButton.OK, MessageBoxImage.Information);
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

            var result = MessageBox.Show($"Are you sure you want to clear all {_audioFiles.Count} files from the list?\n\nUnsaved changes will be lost.",
                                       "Confirm Clear",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Clean up memory for each file before clearing
                foreach (var file in _audioFiles)
                {
                    file.Cleanup();
                }
                
                _audioFiles.Clear();
                SelectedFile = null!;
                
                // Force garbage collection to reclaim memory immediately
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                UpdateStatus("File list cleared and memory cleaned up.");
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

        private void ShowEmptyFields_Changed(object sender, RoutedEventArgs e)
        {
            UpdateComparisonDisplay();
        }

        private void UpdateComparisonDisplay()
        {
            if (SelectedFile?.TagComparison == null) return;

            var mode = (ComparisonMode)ComparisonModeComboBox.SelectedIndex;
            var showEmpty = ShowEmptyFieldsCheckBox.IsChecked ?? false;

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

            if (!showEmpty)
            {
                filteredComparison = filteredComparison.Where(c =>
                    !string.IsNullOrWhiteSpace(c.OriginalValue) || !string.IsNullOrWhiteSpace(c.NewValue));
            }

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
            foreach (var file in _audioFiles)
            {
                if (file.TagComparison != null && file.TagComparison.Any(c => c.IsAccepted && !c.IsRejected))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Online Source Management

        private void InitializeOnlineSourceDropdown()
        {
            OnlineSourceComboBox.ItemsSource = _onlineSourceItems;
        }

        private async void SearchMusicBrainz_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null) return;

            try
            {
                var query = $"{SelectedFile.Artist} {SelectedFile.Album}".Trim();
                if (string.IsNullOrEmpty(query))
                {
                    MessageBox.Show("Please ensure the selected file has artist or album information.", "Search Requirements", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var results = await _musicBrainzService.SearchReleases(query);
                
                // Clear existing MusicBrainz results and add new ones
                var existingMB = _onlineSourceItems.Where(x => x.SourceType == "MusicBrainz").ToList();
                foreach (var item in existingMB)
                {
                    _onlineSourceItems.Remove(item);
                }

                // Score and sort results by relevance to current file
                var scoredResults = results.Take(10) // Get more results for better matching
                    .Select(release => new { 
                        Release = release, 
                        Score = CalculateMatchScore(SelectedFile, release) 
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(5) // Then take top 5 after scoring
                    .ToList();

                foreach (var scoredResult in scoredResults)
                {
                    var displayName = $"MusicBrainz: {scoredResult.Release.Artist} - {scoredResult.Release.Title} ({scoredResult.Release.Date})";
                    if (scoredResult.Release.TrackCount > 0)
                        displayName += $" [{scoredResult.Release.TrackCount} tracks]";
                    if (scoredResult.Score > 0)
                        displayName += $" [Match: {scoredResult.Score:F1}]";
                        
                    _onlineSourceItems.Add(new OnlineSourceItem
                    {
                        DisplayName = displayName,
                        Source = scoredResult.Release,
                        SourceType = "MusicBrainz"
                    });
                }

                // Auto-select the best overall match across all sources
                SelectBestOverallMatch();

                UpdateApplyButtonState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching MusicBrainz: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SearchDiscogs_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFile == null) return;

            try
            {
                var query = $"{SelectedFile.Artist} {SelectedFile.Album}".Trim();
                if (string.IsNullOrEmpty(query))
                {
                    MessageBox.Show("Please ensure the selected file has artist or album information.", "Search Requirements", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var results = await _discogsService.SearchReleases(query);
                
                // Clear existing Discogs results and add new ones
                var existingDiscogs = _onlineSourceItems.Where(x => x.SourceType == "Discogs").ToList();
                foreach (var item in existingDiscogs)
                {
                    _onlineSourceItems.Remove(item);
                }

                // Score and sort results by relevance to current file
                var scoredResults = results.Take(10) // Get more results for better matching
                    .Select(release => new { 
                        Release = release, 
                        Score = CalculateMatchScore(SelectedFile, release) 
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(5) // Then take top 5 after scoring
                    .ToList();

                foreach (var scoredResult in scoredResults)
                {
                    var displayName = $"Discogs: {scoredResult.Release.Artist} - {scoredResult.Release.Title} ({scoredResult.Release.Year})";
                    if (scoredResult.Release.TrackCount > 0)
                        displayName += $" [{scoredResult.Release.TrackCount} tracks]";
                    if (scoredResult.Score > 0)
                        displayName += $" [Match: {scoredResult.Score:F1}]";
                        
                    _onlineSourceItems.Add(new OnlineSourceItem
                    {
                        DisplayName = displayName,
                        Source = scoredResult.Release,
                        SourceType = "Discogs"
                    });
                }

                // Auto-select the best overall match across all sources
                SelectBestOverallMatch();

                UpdateApplyButtonState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching Discogs: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnlineSource_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateApplyButtonState();
        }

        private void ApplyOnlineSource_Click(object sender, RoutedEventArgs e)
        {
            if (OnlineSourceComboBox.SelectedItem is not OnlineSourceItem selectedSource || SelectedFile == null)
                return;

            try
            {
                TagSnapshot newTags;

                if (selectedSource.SourceType == "MusicBrainz" && selectedSource.Source is MusicBrainzRelease mbRelease)
                {
                    newTags = TagSnapshot.FromMusicBrainzRelease(mbRelease, null, SelectedFile);
                }
                else if (selectedSource.SourceType == "Discogs" && selectedSource.Source is DiscogsRelease discogsRelease)
                {
                    newTags = TagSnapshot.FromDiscogsRelease(discogsRelease, null, SelectedFile);
                }
                else
                {
                    MessageBox.Show("Invalid source selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Apply the comparison using the existing tag comparison system
                SelectedFile.UpdateComparison(newTags, $"Online: {selectedSource.SourceType}");

                // Switch to comparison tab to show results
                InfoTabControl.SelectedIndex = 1; // Tag Comparison tab
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying online source: {ex.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateApplyButtonState()
        {
            ApplyOnlineSourceButton.IsEnabled = OnlineSourceComboBox.SelectedItem != null && SelectedFile != null;
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
                    SelectedFile.UpdateComparison(newTags, $"Auto-selected: {bestItem.SourceType} [Match: {bestScore:F1}]");
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
            catch
            {
                // Silently fail - don't interrupt user experience
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
            catch
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
                catch
                {
                    MessageBox.Show("Could not open the download page. Please visit the GitHub releases page manually.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateService?.Dispose();
            base.OnClosed(e);
        }

        #endregion
    }
}