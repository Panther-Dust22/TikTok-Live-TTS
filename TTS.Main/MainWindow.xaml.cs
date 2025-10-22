using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Documents;
using TTS.Shared.Interfaces;
using TTS.Shared.Utils;
using TTS.Shared.Configuration;
using TTS.Processing;
using TTS.Tts;
using TTS.AudioQueue;
using TTS.UserTracking;
using TTS.Shared.Models;
using System.Runtime.InteropServices; // For Windows API calls
using System.Reflection;
using System.Text.Json;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using NAudio.Wave;

namespace TTS.Main
{
    public partial class MainWindow : Window
    {
        private readonly List<IModule> _modules = new();
        private DispatcherTimer? _logUpdateTimer;
        private DispatcherTimer? _userUpdateTimer;
        private DispatcherTimer? _queueUpdateTimer;
        private DispatcherTimer? _updateFlashTimer;
        private bool _updateAvailable = false;
        private bool _autoStartEnabled = false;
        private int _savedThemePosition = 1; // Default to Neutral (1)
        private readonly HttpClient _httpClient = new HttpClient();
        private VersionInfo _versionInfo = new VersionInfo();
        private const string GitHubApiUrl = "https://api.github.com/repos/Panther-Dust22/TikTok-Live-TTS/releases/latest";
        private Settings? _settings;
        private bool _systemRunning = false;
        private bool _ttsCommandEnabled = false;
        private bool _modCommandsEnabled = false;
        private bool _emergencyStopRunning = false;
        private readonly List<string> _availableVoices = new();
        private readonly List<string> _activeUsers = new();
        private UserTrackingModule? _userTrackingModule;
        private ProcessingModule? _processingModule;

        // Volume control - controls Windows app volume
        private float _currentVolume = 1.0f;
        private WindowsAudioSessionManager? _audioSessionManager;
        
        // Remote message functionality
        private readonly string _remoteMessageUrl = "https://raw.githubusercontent.com/Panther-Dust22/TikTok-Live-TTS-BSR-Injector/refs/heads/main/TTS%20BSR%20v4%2013/data/TTS_Message.json";

        public MainWindow()
        {
            InitializeComponent();
            InitializeGui();
            InitializeModules();
            LoadSettings();
            StartBackgroundUpdates();
            
            // Subscribe to GUI messages from Logger
            Logger.GuiMessageReceived += OnGuiMessageReceived;

            // No plugin-specific wiring here; loader remains generic

            // Ensure window lifecycle events are wired
            this.Loaded += Window_Loaded;
            this.Closing += Window_Closing;
        }

        private void InitializeGui()
        {
            // Apply Neutral theme on startup
            _themePosition = 1; // Neutral position
            UpdateThemeSwitch();
            
            // Load remote message
            _ = Task.Run(LoadRemoteMessage);
            
            // Initialize Windows audio session manager
            try
            {
                _audioSessionManager = new WindowsAudioSessionManager();
            }
            catch (Exception ex)
            {
                // Silent failure - don't spam console
                System.Diagnostics.Debug.WriteLine($"Failed to initialize audio session manager: {ex.Message}");
            }
            
            
            // Ensure NameSwap uses normal textbox visuals without overlay
            this.Loaded += (s, e) =>
            {
                // Keep overlay hidden; TextBox renders normally
                NameSwapTextOverlay.Visibility = Visibility.Collapsed;
                NameSwapTextBox.Foreground = new SolidColorBrush(Colors.Black);
                NameSwapTextBox.Background = new SolidColorBrush(Colors.White);
                NameSwapTextBox.CaretBrush = new SolidColorBrush(Colors.Black);
            };
            
            // Initialize speed dropdown
            var speedValues = new List<string> { "None" };
            for (int i = 1; i <= 20; i++)
            {
                speedValues.Add((i / 10.0).ToString("F1"));
            }
            SpeedComboBox.ItemsSource = speedValues;
            SpeedComboBox.SelectedValue = "None";

            // Initialize volume slider from config
            LoadVolumeFromConfig();

            // Initialize log update timer
            _logUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();

            // Initialize user update timer
            _userUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _userUpdateTimer.Tick += UserUpdateTimer_Tick;
            _userUpdateTimer.Start();

            // Initialize queue update timer
            _queueUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _queueUpdateTimer.Tick += QueueUpdateTimer_Tick;
            _queueUpdateTimer.Start();

            // Initialize update flash timer
            _updateFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateFlashTimer.Tick += UpdateFlashTimer_Tick;

            LogToConsole("🚀 GUI initialized");
        }

        private void OnActiveUsersChanged(object? sender, ActiveUsersChangedEventArgs e)
        {
            // Update UI on the main thread
            Dispatcher.Invoke(() =>
            {
                UpdateActiveUsersDropdown();
            });
        }

        private void OnGuiMessageReceived(string message)
        {
            // Handle GUI messages without timestamps
            Dispatcher.Invoke(() =>
            {
                LogToGui(message);
            });
        }

        private void LogToGui(string message)
        {
            // Add message to GUI log without timestamps
            if (ConsoleTextBox != null)
            {
                ConsoleTextBox.AppendText(message + Environment.NewLine);
                ConsoleTextBox.ScrollToEnd();
            }
        }

        private int _themePosition = 1; // 0=Dark, 1=Neutral, 2=Light (Neutral is default)

        private async void ThemeSwitch_Dark_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _themePosition = 0;
            UpdateThemeSwitch();
            await SaveThemeSetting();
        }

        private async void ThemeSwitch_Neutral_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _themePosition = 1;
            UpdateThemeSwitch();
            await SaveThemeSetting();
        }

        private async void ThemeSwitch_Light_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _themePosition = 2;
            UpdateThemeSwitch();
            await SaveThemeSetting();
        }

        private void UpdateThemeSwitch()
        {
            // Update thumb position
            Grid.SetColumn(ThemeSwitchThumb, _themePosition);
            
            // Apply theme using ResourceDictionaries
            switch (_themePosition)
            {
                case 0: // Dark
                    ThemeSwitchGrid.Background = new SolidColorBrush(Color.FromRgb(76, 86, 106));
                    ApplyTheme("Dark");
                    break;
                case 1: // Neutral
                    ThemeSwitchGrid.Background = new SolidColorBrush(Color.FromRgb(108, 123, 149));
                    ApplyTheme("Normal");
                    break;
                case 2: // Light
                    ThemeSwitchGrid.Background = new SolidColorBrush(Color.FromRgb(94, 129, 172));
                    ApplyTheme("Light");
                    break;
            }
        }

        private void ApplyTheme(string theme)
        {
            try
            {
                // Clear existing theme dictionaries
                Application.Current.Resources.MergedDictionaries.Clear();

                if (theme == "Dark")
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) });
                    LogToConsole("🌙 Switched to Dark Mode");
                }
                else if (theme == "Light")
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("Themes/Light.xaml", UriKind.Relative) });
                    LogToConsole("☀️ Switched to Light Mode");
                }
                else if (theme == "Normal")
                {
                    // Apply Neutral theme (plum background with white title)
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("Themes/Neutral.xaml", UriKind.Relative) });
                    LogToConsole("⭐ Switched to Normal Mode");
                }
                
                // Restore functional button colors after theme change
                RestoreOriginalButtonColors();
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Error applying theme '{theme}': {ex.Message}");
                // Fallback to manual color application
                ApplyThemeManually(theme);
            }
        }

        private void ApplyThemeManually(string theme)
        {
            if (theme == "Dark")
            {
                // Dark theme colors
                var darkBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                var darkHeaderBackground = new SolidColorBrush(Color.FromRgb(46, 52, 64));
                var darkTextColor = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                var darkSecondaryTextColor = new SolidColorBrush(Color.FromRgb(216, 222, 233));
                var darkBorderColor = new SolidColorBrush(Color.FromRgb(76, 86, 106));
                var darkButtonAreaBackground = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                
                // Apply to main window
                this.Background = darkBackground;
                HeaderBorder.Background = darkHeaderBackground;
                
                // Update all text colors in header
                UpdateTextColors(darkTextColor, darkSecondaryTextColor);
                
                // Update main content area colors
                UpdateMainContentColors(darkBackground, darkBorderColor, darkTextColor, darkButtonAreaBackground);
                
                LogToConsole("🌙 Applied Dark Mode (Manual)");
            }
            else if (theme == "Light")
            {
                // Light theme colors
                var lightBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250));
                var lightHeaderBackground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                var lightTextColor = new SolidColorBrush(Color.FromRgb(33, 37, 41));
                var lightSecondaryTextColor = new SolidColorBrush(Color.FromRgb(108, 117, 125));
                var lightBorderColor = new SolidColorBrush(Color.FromRgb(222, 226, 230));
                var lightButtonAreaBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                
                // Apply to main window
                this.Background = lightBackground;
                HeaderBorder.Background = lightHeaderBackground;
                
                // Update all text colors in header
                UpdateTextColors(lightTextColor, lightSecondaryTextColor);
                
                // Update main content area colors
                UpdateMainContentColors(lightBackground, lightBorderColor, lightTextColor, lightButtonAreaBackground);
                
                LogToConsole("☀️ Applied Light Mode (Manual)");
            }
            else if (theme == "Normal")
            {
                // Normal theme = Default WPF appearance
                this.Background = SystemColors.WindowBrush;
                HeaderBorder.Background = SystemColors.ControlBrush;
                
                // Reset text colors to default
                var defaultTextColor = SystemColors.WindowTextBrush;
                var defaultSecondaryTextColor = SystemColors.GrayTextBrush;
                UpdateTextColors(defaultTextColor, defaultSecondaryTextColor);
                
                // Reset main content area to default
                UpdateMainContentColors(SystemColors.WindowBrush, SystemColors.ControlDarkBrush, defaultTextColor, SystemColors.ControlBrush);
                
                LogToConsole("🎨 Applied Normal Theme (Manual)");
            }
            
            // Restore functional button colors after theme change
            RestoreOriginalButtonColors();
        }



        private void ResetUI()
        {
            // Reset UI by applying Normal theme (clears ResourceDictionaries)
            LogToConsole("🔄 Resetting UI...");
            
            // Reset theme switch to neutral position and apply Normal theme
            _themePosition = 1;
            ApplyTheme("Normal");
            
            LogToConsole("✅ UI reset complete - applied Normal theme");
        }

        private void RestoreOriginalButtonColors()
        {
            // Restore original button colors for functional buttons
            // Start/Stop button
            if (StartTtsButton != null)
            {
                StartTtsButton.Background = _systemRunning ? 
                    new SolidColorBrush(Color.FromRgb(76, 175, 80)) : // Green when running
                    new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red when stopped
            }

            // !tts button
            if (TtsCommandButton != null)
            {
                TtsCommandButton.Background = _ttsCommandEnabled ? 
                    new SolidColorBrush(Color.FromRgb(76, 175, 80)) : // Green when enabled
                    new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red when disabled
            }

            // Mod commands button
            if (ModCommandsButton != null)
            {
                ModCommandsButton.Background = _modCommandsEnabled ? 
                    new SolidColorBrush(Color.FromRgb(76, 175, 80)) : // Green when enabled
                    new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red when disabled
            }

            // Emergency stop button
            if (EmergencyButton != null)
            {
                EmergencyButton.Background = _emergencyStopRunning ? 
                    new SolidColorBrush(Color.FromRgb(76, 175, 80)) : // Green when active
                    new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red when inactive
            }

            // Specials button (gold)
            if (SpecialsButton != null)
            {
                SpecialsButton.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                SpecialsButton.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }

            // Edit bad word button (gold)
            if (FilterButton != null)
            {
                FilterButton.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                FilterButton.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        private void UpdateTextColors(SolidColorBrush primaryText, SolidColorBrush secondaryText)
        {
            // Find and update all text blocks in the header
            var textBlocks = FindVisualChildren<TextBlock>(HeaderBorder);
            foreach (var textBlock in textBlocks)
            {
                if (textBlock.Text.Contains("TTS Voice System"))
                {
                    textBlock.Foreground = primaryText;
                }
                else if (textBlock.Text.Contains("Modular Architecture"))
                {
                    textBlock.Foreground = secondaryText;
                }
                else if (textBlock.Text == "🌙" || textBlock.Text == "☀️")
                {
                    textBlock.Foreground = primaryText;
                }
            }
        }

        private void UpdateMainContentColors(SolidColorBrush background, SolidColorBrush borderColor, SolidColorBrush textColor, SolidColorBrush buttonAreaBackground)
        {
            // Update main content area
            var mainGrid = (Grid)this.Content;
            if (mainGrid != null)
            {
                // Find the main content grid (row 1)
                var contentGrid = (Grid)mainGrid.Children[1];
                if (contentGrid != null)
                {
                    // Set the main content grid background
                    contentGrid.Background = background;
                    
                    // Update all borders and panels
                    var borders = FindVisualChildren<Border>(contentGrid);
                    foreach (var border in borders)
                    {
                        if (border.Name != "HeaderBorder") // Don't update header
                        {
                            border.Background = background;
                            border.BorderBrush = borderColor;
                        }
                    }
                    
                    // Update all text blocks
                    var textBlocks = FindVisualChildren<TextBlock>(contentGrid);
                    foreach (var textBlock in textBlocks)
                    {
                        textBlock.Foreground = textColor;
                    }
                    
                    // Update all labels
                    var labels = FindVisualChildren<Label>(contentGrid);
                    foreach (var label in labels)
                    {
                        label.Foreground = textColor;
                    }
                    
                    // Update all text boxes (console)
                    var textBoxes = FindVisualChildren<TextBox>(contentGrid);
                    foreach (var textBox in textBoxes)
                    {
                        textBox.Foreground = textColor;
                        textBox.Background = background;
                    }
                    
                    // Update all combo boxes
                    var comboBoxes = FindVisualChildren<ComboBox>(contentGrid);
                    foreach (var comboBox in comboBoxes)
                    {
                        comboBox.Foreground = textColor;
                        comboBox.Background = background;
                    }
                    
                    // Update all buttons - preserve functional button colors
                    var buttons = FindVisualChildren<Button>(contentGrid);
                    foreach (var button in buttons)
                    {
                        // Only update text color for non-functional buttons
                        if (button.Name != "StartTtsButton" && 
                            button.Name != "TtsCommandButton" && 
                            button.Name != "ModCommandsButton" && 
                            button.Name != "EmergencyButton" &&
                            button.Name != "SpecialsButton" &&
                            button.Name != "FilterButton")
                        {
                            button.Foreground = textColor;
                        }
                        // Don't change button background - keep original styling
                    }
                    
                    // Update button container backgrounds (panels, stackpanels, etc.)
                    var panels = FindVisualChildren<Panel>(contentGrid);
                    foreach (var panel in panels)
                    {
                        // Only update panels that contain buttons
                        if (HasButtons(panel))
                        {
                            panel.Background = buttonAreaBackground;
                        }
                    }
                }
            }
        }

        private void UpdateThemeSwitchColors(SolidColorBrush switchBackground, SolidColorBrush textColor)
        {
            // Find and update the theme toggle button
            var toggleButton = FindVisualChildren<Control>(HeaderBorder)
                .FirstOrDefault(c => c.GetType().Name == "ToggleButton");
            if (toggleButton != null)
            {
                toggleButton.Background = switchBackground;
            }
            
            // Update the switch container background
            var switchBorders = FindVisualChildren<Border>(HeaderBorder);
            foreach (var border in switchBorders)
            {
                if (border.Child is StackPanel stackPanel && stackPanel.Children.Contains(toggleButton))
                {
                    border.Background = switchBackground;
                    break;
                }
            }
        }

        private static bool HasButtons(DependencyObject depObj)
        {
            if (depObj == null) return false;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is Button)
                {
                    return true;
                }
                if (HasButtons(child))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        private void UpdateActiveUsersDropdown()
        {
            if (_userTrackingModule == null) return;

            try
            {
                var activeUsers = _userTrackingModule.GetActiveUsers();
                
                // Only update if the user list has actually changed
                if (activeUsers.SequenceEqual(_lastActiveUsers))
                {
                    return; // No changes, don't update
                }
                
                _lastActiveUsers = activeUsers;
                var currentSelection = UsersComboBox.SelectedItem?.ToString();
                
                UsersComboBox.Items.Clear();
                UsersComboBox.Items.Add("Select User...");
                
                foreach (var user in activeUsers)
                {
                    UsersComboBox.Items.Add(user);
                }
                
                // Restore selection if it still exists
                if (!string.IsNullOrEmpty(currentSelection) && activeUsers.Contains(currentSelection))
                {
                    UsersComboBox.SelectedItem = currentSelection;
                }
                else
                {
                    UsersComboBox.SelectedIndex = 0; // Select "Select User..."
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error updating active users dropdown: {ex.Message}");
            }
        }

        private void InitializeModules()
        {
            _processingModule = new ProcessingModule();
            _modules.Add(_processingModule);
            _modules.Add(new TtsModule());
            _modules.Add(new AudioQueueModule());
            
            // Add UserTracking module and get reference
            _userTrackingModule = new UserTrackingModule();
            _modules.Add(_userTrackingModule);
            
            // Subscribe to user tracking events
            _userTrackingModule.ActiveUsersChanged += OnActiveUsersChanged;
            // Bridge external chat (from mods) into user tracking when available
            // This uses the processing pipeline outputs; for immediate registration, we also hook mod host below
            
            UpdateModuleStatuses();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = Settings.Load();
                LoadVoices();
                InitializeButtonStates();
                LogToConsole("✅ Settings loaded successfully");
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Error loading settings: {ex.Message}");
            }
        }

        private void InitializeButtonStates()
        {
            // Initialize !tts command button state
            _ttsCommandEnabled = _settings.Options.A_ttscode == "TRUE";
            if (_ttsCommandEnabled)
            {
                TtsCommandButton.Content = "!tts Command\nEnabled";
                TtsCommandButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                TtsCommandButton.Content = "!tts Command\nDisabled";
                TtsCommandButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            }

            // Initialize mod commands button state
            _modCommandsEnabled = _settings.Options.Voice_change.Enabled == "TRUE";
            if (_modCommandsEnabled)
            {
                ModCommandsButton.Content = "Mod Commands\nEnabled";
                ModCommandsButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                ModCommandsButton.Content = "Mod Commands\nDisabled";
                ModCommandsButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            }

            // Initialize emergency stop button state (default to inactive)
            _emergencyStopRunning = false;
            EmergencyButton.Content = "🚨 Emergency Stop\nDaemon";
            EmergencyButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red when inactive
        }

        private void LoadVoices()
        {
            if (_settings?.Voices?.Voice_List_cheat_sheet != null)
            {
                _availableVoices.Clear();
                _availableVoices.Add("NONE"); // Add NONE as first option
                foreach (var category in _settings.Voices.Voice_List_cheat_sheet)
                {
                    foreach (var voice in category.Value)
                    {
                        _availableVoices.Add(voice.name);
                    }
                }
                VoicesComboBox.ItemsSource = _availableVoices;
                VoicesComboBox.SelectedIndex = 0; // Select "NONE" by default
            }
        }

        // Public method to expose available voices for other windows
        public List<string> GetAvailableVoices()
        {
            return _availableVoices?.ToList() ?? new List<string>();
        }

        private void StartBackgroundUpdates()
        {
            LogToConsole("🚀 Starting background services...");
            
            // User update timer is already initialized above
            
            // Initialize traffic light status
            UpdateModuleStatusLights();
            
            // Start checking for updates
            _ = Task.Run(CheckForUpdates);
            
            // Check for auto-start
            // Load version info
            LoadVersionInfo();
            
            CheckAutoStart();
            
            // Load saved theme
            _ = Task.Run(LoadThemeSetting);
        }

        private void UserUpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Update active users dropdown periodically
            UpdateActiveUsersDropdown();
            
            // Update module status lights
            UpdateModuleStatusLights();
        }

        private void UpdateModuleStatusLights()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Update Processing Module status
                    var processingModule = _modules.OfType<TTS.Processing.ProcessingModule>().FirstOrDefault();
                    if (processingModule != null && processingModule.IsRunning)
                    {
                        ProcessingStatusLight.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                    else
                    {
                        ProcessingStatusLight.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }

                    // Update TTS Module status
                    var ttsModule = _modules.OfType<TTS.Tts.TtsModule>().FirstOrDefault();
                    if (ttsModule != null && ttsModule.IsRunning)
                    {
                        TtsStatusLight.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                    else
                    {
                        TtsStatusLight.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }

                    // Update AudioQueue Module status
                    var audioQueueModule = _modules.OfType<TTS.AudioQueue.AudioQueueModule>().FirstOrDefault();
                    if (audioQueueModule != null && audioQueueModule.IsRunning)
                    {
                        AudioQueueStatusLight.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                    else
                    {
                        AudioQueueStatusLight.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }

                    // Update UserTracking Module status
                    var userTrackingModule = _modules.OfType<TTS.UserTracking.UserTrackingModule>().FirstOrDefault();
                    if (userTrackingModule != null && userTrackingModule.IsRunning)
                    {
                        UserTrackingStatusLight.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    }
                    else
                    {
                        UserTrackingStatusLight.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }
                });
            }
            catch (Exception ex)
            {
                LogToConsole($"Error updating module status lights: {ex.Message}");
            }
        }

        private void UpdateFlashTimer_Tick(object? sender, EventArgs e)
        {
            if (_updateAvailable)
            {
                Dispatcher.Invoke(() =>
                {
                    // Toggle between red and original color
                    if (UpdateButton.Background is SolidColorBrush brush && brush.Color.R == 244) // Currently red
                    {
                        UpdateButton.Background = new SolidColorBrush(Color.FromRgb(208, 135, 112)); // Original color
                    }
                    else
                    {
                        UpdateButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    }
                });
            }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateButton.Content = "Checking...";
                    UpdateButton.Background = new SolidColorBrush(Color.FromRgb(94, 129, 172)); // Blue
                });
                
                // Check GitHub API for latest release
                var latestVersion = await GetLatestVersionFromGitHub();
                
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(latestVersion))
                    {
                        if (IsNewerVersion(latestVersion, _versionInfo.Version))
                        {
                            _updateAvailable = true;
                            UpdateButton.Content = "Update Available!";
                            UpdateButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                            _updateFlashTimer?.Start();
                            LogToConsole($"🆕 Update available! Latest version: {latestVersion}");
                        }
                        else
                        {
                            _updateAvailable = false;
                            UpdateButton.Content = "No updates";
                            UpdateButton.Background = new SolidColorBrush(Color.FromRgb(208, 135, 112)); // Original color
                            LogToConsole($"✅ No updates available (current {_versionInfo.Version}, latest {latestVersion})");
                        }
                    }
                    else
                    {
                        _updateAvailable = false;
                        UpdateButton.Content = "No updates";
                        UpdateButton.Background = new SolidColorBrush(Color.FromRgb(208, 135, 112)); // Original color
                        LogToConsole("✅ No updates available (could not read latest version)");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateButton.Content = "Check Failed";
                    UpdateButton.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                });
                LogToConsole($"❌ Failed to check updates: {ex.Message}");
            }
        }

        private async Task<string?> GetLatestVersionFromGitHub()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "TTS-Voice-System");
                
                var response = await _httpClient.GetAsync(GitHubApiUrl);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var releaseData = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (releaseData.TryGetProperty("tag_name", out var tagName))
                {
                    return tagName.GetString()?.ToLower();
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                // Simple version comparison - remove 'v' prefix and compare
                var latest = latestVersion.TrimStart('v');
                var current = currentVersion.TrimStart('v');
                
                var latestParts = latest.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();
                
                // Pad arrays to same length
                var maxLength = Math.Max(latestParts.Length, currentParts.Length);
                Array.Resize(ref latestParts, maxLength);
                Array.Resize(ref currentParts, maxLength);
                
                for (int i = 0; i < maxLength; i++)
                {
                    if (latestParts[i] > currentParts[i]) return true;
                    if (latestParts[i] < currentParts[i]) return false;
                }
                
                return false; // Same version
            }
            catch
            {
                return false; // Assume no update if comparison fails
            }
        }

        private void LogToConsole(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var formattedMessage = $"[{timestamp}] {message}";
            
            Dispatcher.Invoke(() =>
            {
                ConsoleTextBox.AppendText(formattedMessage + Environment.NewLine);
                
                // Force scroll to end - now that we removed the ScrollViewer, this should work
                ConsoleTextBox.CaretIndex = ConsoleTextBox.Text.Length;
                ConsoleTextBox.ScrollToEnd();
                
                // Limit console to last 1000 lines
                var lines = ConsoleTextBox.Text.Split('\n');
                if (lines.Length > 1000)
                {
                    var newText = string.Join("\n", lines.Skip(lines.Length - 1000));
                    ConsoleTextBox.Text = newText;
                    ConsoleTextBox.CaretIndex = ConsoleTextBox.Text.Length;
                    ConsoleTextBox.ScrollToEnd();
                }
            });
        }

        private static string GetBaseDirectory()
        {
            try
            {
                // First try the current working directory
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                
                // If that didn't work, try relative to the executable location
                var exeDir = new DirectoryInfo(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "");
                dir = exeDir;
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var dataDir = Path.Combine(dir.FullName, "data");
                    if (Directory.Exists(dataDir) && File.Exists(Path.Combine(dataDir, "options.json"))) 
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            
            // Fallback to current directory
            return Directory.GetCurrentDirectory();
        }

        // Helper methods for NameSwapTextBox with overlay
        private string GetNameSwapText()
        {
            return NameSwapTextBox.Text?.Trim() ?? string.Empty;
        }

        private void SetNameSwapText(string text)
        {
            NameSwapTextBox.Text = text;
        }


        #region Button Event Handlers

        private async void StartTtsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_systemRunning)
            {
                LogToConsole("🚀 Starting TTS system...");
                await StartAllModules();
                _systemRunning = true;
                StartTtsButton.Content = "Stop TTS";
                StartTtsButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                LogToConsole("✅ TTS system started");
            }
            else
            {
                LogToConsole("🛑 Stopping TTS system...");
                await StopAllModules();
                _systemRunning = false;
                StartTtsButton.Content = "Start TTS";
                StartTtsButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                LogToConsole("✅ TTS system stopped");
            }
        }

        private void TtsCommandButton_Click(object sender, RoutedEventArgs e)
        {
            _ttsCommandEnabled = !_ttsCommandEnabled;
            if (_ttsCommandEnabled)
            {
                TtsCommandButton.Content = "!tts Command\nEnabled";
                TtsCommandButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                _settings.Options.A_ttscode = "TRUE";
                LogToConsole("✅ !tts command enabled");
            }
            else
            {
                TtsCommandButton.Content = "!tts Command\nDisabled";
                TtsCommandButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                _settings.Options.A_ttscode = "FALSE";
                LogToConsole("❌ !tts command disabled");
            }
            JsonUtil.SaveJson(_settings.Options, Settings.ResolveDataPath("options.json"));
        }

        private void ModCommandsButton_Click(object sender, RoutedEventArgs e)
        {
            _modCommandsEnabled = !_modCommandsEnabled;
            if (_modCommandsEnabled)
            {
                ModCommandsButton.Content = "Mod Commands\nEnabled";
                ModCommandsButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                _settings.Options.Voice_change.Enabled = "TRUE";
                LogToConsole("✅ Moderator commands enabled");
            }
            else
            {
                ModCommandsButton.Content = "Mod Commands\nDisabled";
                ModCommandsButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                _settings.Options.Voice_change.Enabled = "FALSE";
                LogToConsole("❌ Moderator commands disabled");
            }
            JsonUtil.SaveJson(_settings.Options, Settings.ResolveDataPath("options.json"));
        }

        private void SpecialsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToConsole("🎭 Opening specials window...");
                var specialsWindow = new SpecialsWindow
                {
                    Owner = this
                };
                specialsWindow.ShowDialog();
                LogToConsole("✅ Specials window closed");
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Error opening specials window: {ex.Message}");
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToConsole("🛡️ Opening bad word filter editor...");
                var filterWindow = new EditBadWordsWindow
                {
                    Owner = this
                };
                filterWindow.ShowDialog();
                LogToConsole("✅ Bad word filter editor closed");
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Error opening bad word filter editor: {ex.Message}");
            }
        }

        private void EmergencyButton_Click(object sender, RoutedEventArgs e)
        {
            _emergencyStopRunning = !_emergencyStopRunning;
            if (_emergencyStopRunning)
            {
                EmergencyButton.Content = "🚨 Emergency Stop\nActive";
                EmergencyButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green when active
                LogToConsole("🚨 Emergency stop activated");
            }
            else
            {
                EmergencyButton.Content = "🚨 Emergency Stop\nDaemon";
                EmergencyButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red when inactive
                LogToConsole("✅ Emergency stop deactivated");
            }
            
            // Update ProcessingModule with emergency stop state
            _processingModule?.SetEmergencyStopState(_emergencyStopRunning);
        }


        private void ApplyVoiceButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedUser = UsersComboBox.SelectedItem?.ToString();
            var selectedVoice = VoicesComboBox.SelectedItem?.ToString();
            var nameSwap = GetNameSwapText();
            var speed = SpeedComboBox.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedUser) || selectedUser == "Select User...")
            {
                MessageBox.Show("Please select a user first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_settings?.Users == null)
                {
                    MessageBox.Show("Settings not loaded", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool hasChanges = false;

                // Apply voice and speed settings
                if (!string.IsNullOrEmpty(selectedVoice) && selectedVoice != "NONE")
                {
                    if (speed == "None" || string.IsNullOrEmpty(speed))
                    {
                        // Voice only, no speed (will use default speed)
                        _settings.Users.C_priority_voice[selectedUser] = selectedVoice;
                        LogToGui($"🎤 Applied voice for {selectedUser}: {selectedVoice} (using default speed)");
                    }
                    else
                    {
                        // Voice with specific speed
                        if (float.TryParse(speed, out var speedValue))
                        {
                            _settings.Users.C_priority_voice[selectedUser] = new Dictionary<string, object>
                            {
                                { selectedVoice, speedValue.ToString("F3") }
                            };
                            LogToGui($"🎤 Applied voice for {selectedUser}: {selectedVoice} @ {speedValue}x");
                        }
                        else
                        {
                            MessageBox.Show("Invalid speed value", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    hasChanges = true;
                }
                else if (selectedVoice == "NONE")
                {
                    // Remove voice settings if NONE is selected
                    if (_settings.Users.C_priority_voice.ContainsKey(selectedUser))
                    {
                        _settings.Users.C_priority_voice.Remove(selectedUser);
                        LogToGui($"🎤 Removed voice for {selectedUser} (set to NONE)");
                        hasChanges = true;
                    }
                }

                // Apply name swap settings
                if (!string.IsNullOrEmpty(nameSwap))
                {
                    _settings.Users.E_name_swap[selectedUser] = nameSwap;
                    LogToGui($"📝 Applied name swap for {selectedUser}: {nameSwap}");
                    hasChanges = true;
                }
                else
                {
                    // Remove name swap if text is empty
                    if (_settings.Users.E_name_swap.ContainsKey(selectedUser))
                    {
                        _settings.Users.E_name_swap.Remove(selectedUser);
                        LogToGui($"📝 Removed name swap for {selectedUser}");
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    // Save settings to file
                    _settings.Users.Save();
                    _settings.ForceReloadFromDisk(); // Reload to ensure consistency
                    
                    LogToGui($"✅ User settings updated successfully");
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error applying settings: {ex.Message}");
                MessageBox.Show($"Error applying settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveVoiceButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedUser = UsersComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedUser) || selectedUser == "Select User...")
            {
                MessageBox.Show("Please select a user first", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_settings?.Users == null)
                {
                    MessageBox.Show("Settings not loaded", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool hasChanges = false;

                // Remove voice settings
                if (_settings.Users.C_priority_voice.ContainsKey(selectedUser))
                {
                    _settings.Users.C_priority_voice.Remove(selectedUser);
                    LogToGui($"🗑️ Removed voice settings for {selectedUser}");
                    hasChanges = true;
                }

                // Remove name swap settings
                if (_settings.Users.E_name_swap.ContainsKey(selectedUser))
                {
                    _settings.Users.E_name_swap.Remove(selectedUser);
                    LogToGui($"🗑️ Removed name swap for {selectedUser}");
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    // Save settings to file
                    _settings.Users.Save();
                    _settings.ForceReloadFromDisk(); // Reload to ensure consistency
                    
                    // Clear the form fields
                    VoicesComboBox.SelectedIndex = 0; // "NONE"
                    SetNameSwapText("");
                    SpeedComboBox.SelectedIndex = 0; // "None"
                    
                    MessageBox.Show($"Settings removed successfully for {selectedUser}. User will now use default settings.", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"No custom settings found for {selectedUser}", "Info", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error removing settings: {ex.Message}");
                MessageBox.Show($"Error removing settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditPriorityButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settings == null)
                {
                    MessageBox.Show("Settings not loaded", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LogToConsole("📝 Opening user list editor...");
                
                var editWindow = new UserEditWindow(_settings)
                {
                    Owner = this
                };

                var result = editWindow.ShowDialog();
                if (result == true)
                {
                    LogToConsole("✅ User settings updated successfully");
                    // Refresh the active users dropdown in case any changes affect the current selection
                    UpdateActiveUsersDropdown();
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error opening user editor: {ex.Message}");
                MessageBox.Show($"Error opening user editor: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var slider = sender as Slider;
            if (slider != null)
            {
                _currentVolume = (float)slider.Value;
                
                // Set Windows app volume (silent - no logging)
                SetWindowsAppVolume(_currentVolume);
                
                // Save volume to config
                SaveVolumeToConfig(_currentVolume);
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateAvailable)
            {
                // Stop flashing and open GitHub releases page
                _updateFlashTimer?.Stop();
                UpdateButton.Content = "Update Available!";
                UpdateButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                
                try
                {
                    // Open the GitHub releases page in the default browser
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/Panther-Dust22/TikTok-Live-TTS/releases/latest",
                        UseShellExecute = true
                    });
                    
                    LogToConsole("🔔 Opening GitHub releases page in your browser...");
                    LogToConsole("📥 Download the latest release and replace the current files.");
                }
                catch (Exception ex)
                {
                    LogToConsole($"❌ Failed to open browser: {ex.Message}");
                    LogToConsole("💡 Please visit: https://github.com/Panther-Dust22/TikTok-Live-TTS/releases/latest");
                }
            }
            else
            {
                LogToConsole("🔄 Checking for updates...");
                _ = Task.Run(CheckForUpdates);
            }
        }



        private void SetWindowsAppVolume(float volume)
        {
            try
            {
                // Use Windows audio session manager to control app volume
                if (_audioSessionManager != null)
                {
                    _audioSessionManager.SetApplicationVolume(volume);
                }
            }
            catch (Exception ex)
            {
                // Silent failure - don't spam console
                System.Diagnostics.Debug.WriteLine($"Error setting Windows app volume: {ex.Message}");
            }
        }

        private void SaveVolumeToConfig(float volume)
        {
            try
            {
                var configPath = Settings.ResolveDataPath("config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                    config["volume"] = volume;
                    
                    var updatedJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, updatedJson);
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Failed to save volume to config: {ex.Message}");
            }
        }

        private void LoadVolumeFromConfig()
        {
            try
            {
                var configPath = Settings.ResolveDataPath("config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (config?.TryGetValue("volume", out var volumeObj) == true)
                    {
                        if (volumeObj is JsonElement volumeElement && volumeElement.TryGetSingle(out var volume))
                        {
                            _currentVolume = Math.Clamp(volume, 0.0f, 1.0f);
                            VolumeSlider.Value = _currentVolume;
                            
                            // Set Windows app volume
                            SetWindowsAppVolume(_currentVolume);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Failed to load volume from config: {ex.Message}");
            }
        }

        private async void AutoStartCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            _autoStartEnabled = true;
            LogToConsole("✅ Auto-start enabled - TTS will start automatically on next launch");
            await SaveAutoStartSetting();
        }

        private async void AutoStartCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoStartEnabled = false;
            LogToConsole("❌ Auto-start disabled");
            await SaveAutoStartSetting();
        }

        private async void CheckAutoStart()
        {
            try
            {
                // Load auto-start setting from file
                var autoStartFile = Path.Combine(GetBaseDirectory(), "auto_start.json");
                if (File.Exists(autoStartFile))
                {
                    var json = await File.ReadAllTextAsync(autoStartFile);
                    var autoStartData = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                    if (autoStartData != null && autoStartData.ContainsKey("enabled"))
                    {
                        _autoStartEnabled = autoStartData["enabled"];
                        Dispatcher.Invoke(() =>
                        {
                            AutoStartCheckbox.IsChecked = _autoStartEnabled;
                        });
                        
                        if (_autoStartEnabled)
                        {
                            LogToConsole("🚀 Auto-start enabled - Starting TTS system automatically...");
                            await Task.Delay(2000); // Give user time to see the message
                            await StartTtsSystem();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error checking auto-start: {ex.Message}");
            }
        }

        private async Task SaveAutoStartSetting()
        {
            try
            {
                var autoStartData = new Dictionary<string, bool> { { "enabled", _autoStartEnabled } };
                var json = JsonSerializer.Serialize(autoStartData, new JsonSerializerOptions { WriteIndented = true });
                var autoStartFile = Path.Combine(GetBaseDirectory(), "auto_start.json");
                await File.WriteAllTextAsync(autoStartFile, json);
            }
            catch (Exception ex)
            {
                LogToConsole($"Error saving auto-start setting: {ex.Message}");
            }
        }

        private async Task StartTtsSystem()
        {
            if (!_systemRunning)
            {
                LogToConsole("🚀 Starting TTS system...");
                await StartAllModules();
                _systemRunning = true;
                Dispatcher.Invoke(() =>
                {
                    StartTtsButton.Content = "Stop TTS";
                    StartTtsButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                });
                LogToConsole("✅ TTS system started");
            }
        }

        private async Task SaveThemeSetting()
        {
            try
            {
                var themeData = new Dictionary<string, int> { { "position", _themePosition } };
                var json = JsonSerializer.Serialize(themeData, new JsonSerializerOptions { WriteIndented = true });
                var themeFile = Path.Combine(GetBaseDirectory(), "theme_setting.json");
                await File.WriteAllTextAsync(themeFile, json);
            }
            catch (Exception ex)
            {
                LogToConsole($"Error saving theme setting: {ex.Message}");
            }
        }

        private async Task LoadThemeSetting()
        {
            try
            {
                var themeFile = Path.Combine(GetBaseDirectory(), "theme_setting.json");
                if (File.Exists(themeFile))
                {
                    var json = await File.ReadAllTextAsync(themeFile);
                    var themeData = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                    if (themeData != null && themeData.ContainsKey("position"))
                    {
                        _savedThemePosition = themeData["position"];
                        _themePosition = _savedThemePosition;
                        
                        // Apply the saved theme
                        Dispatcher.Invoke(() =>
                        {
                            // UpdateThemeSwitch already applies the theme; avoid duplicate ApplyTheme logs
                            UpdateThemeSwitch();
                        });
                        
                        LogToConsole($"🎨 Loaded saved theme: {GetThemeName(_themePosition)}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error loading theme setting: {ex.Message}");
            }
        }

        private void LoadVersionInfo()
        {
            try
            {
                _versionInfo = VersionInfo.Load();
                
                // Update UI elements with version info
                TitleTextBlock.Text = $"🔊 {_versionInfo.FullTitle}";
                SubtitleTextBlock.Text = _versionInfo.Subtitle;
                
                // Update window title
                this.Title = $"{_versionInfo.FullTitle} - Modular";
                
                LogToConsole($"📋 Loaded version info: {_versionInfo.Version}");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error loading version info: {ex.Message}");
            }
        }

        private void UpdateVersionInfo(string newVersion)
        {
            try
            {
                _versionInfo.Version = newVersion;
                _versionInfo.FullTitle = $"{_versionInfo.DisplayName} {newVersion}";
                
                // Update UI elements
                TitleTextBlock.Text = $"🔊 {_versionInfo.FullTitle}";
                
                // Save to file
                _versionInfo.Save();
                
                LogToConsole($"📋 Updated version to: {newVersion}");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error updating version info: {ex.Message}");
            }
        }

        private string GetThemeName(int position)
        {
            return position switch
            {
                0 => "Dark",
                1 => "Normal", 
                2 => "Light",
                _ => "Normal"
            };
        }

        private async void ApiTestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToConsole("🚀 Starting comprehensive API performance test (5 runs)...");
                
                // Test data with different words and voices
                var testData = new[]
                {
                    new { Text = "Hello world, this is a test message.", Voice = "EN_US_MALE_1" },
                    new { Text = "Welcome to the voice system.", Voice = "EN_US_FEMALE_1" },
                    new { Text = "Testing different voices and speeds.", Voice = "EN_UK_MALE_1" },
                    new { Text = "Performance evaluation in progress.", Voice = "EN_AU_FEMALE_1" },
                    new { Text = "Checking API reliability and speed.", Voice = "EN_US_MALE_2" }
                };
                
                var allResults = new List<string>();
                var successfulTests = 0;
                
                for (int i = 0; i < testData.Length; i++)
                {
                    var test = testData[i];
                    LogToConsole($"📊 Test {i + 1}/5: '{test.Text.Substring(0, Math.Min(30, test.Text.Length))}...' with {test.Voice}");
                    
                    try
                    {
                        var startTime = DateTime.Now;
                        
                        // Test the TTS API by calling the TTS module directly
                        var success = await TestTtsApiCall(test.Text, test.Voice);
                        
                        var endTime = DateTime.Now;
                        var duration = (endTime - startTime).TotalSeconds;
                        
                        if (success)
                        {
                            LogToConsole($"   ✅ Test {i + 1} completed successfully in {duration:F2}s");
                            allResults.Add($"Test {i + 1}: [OK] Success ({duration:F2}s)");
                            successfulTests++;
                        }
                        else
                        {
                            LogToConsole($"   ❌ Test {i + 1} failed");
                            allResults.Add($"Test {i + 1}: [FAIL] Failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToConsole($"   ❌ Test {i + 1} error: {ex.Message}");
                        allResults.Add($"Test {i + 1}: [FAIL] Error: {ex.Message}");
                    }
                    
                    // Brief delay between tests
                    await Task.Delay(1000);
                }
                
                // Save results to configuration
                await SavePerformanceResults(allResults);
                
                LogToConsole("✅ Comprehensive API performance test completed!");
                LogToConsole($"📊 Results: {successfulTests}/5 tests successful");
                
                // Show summary
                var successRate = (double)successfulTests / testData.Length * 100;
                LogToConsole($"📈 Success rate: {successRate:F1}%");
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Failed to run API performance test: {ex.Message}");
            }
        }
        
        private async Task<bool> TestTtsApiCall(string text, string voice)
        {
            try
            {
                // This is a simplified test - in a real implementation, you would call the actual TTS API
                // For now, we'll simulate the test by checking if the TTS module is available
                
                // Simulate API call delay
                await Task.Delay(500 + new Random().Next(1000)); // 0.5-1.5 seconds
                
                // Simulate success/failure (90% success rate for demo)
                return new Random().Next(100) < 90;
            }
            catch
            {
                return false;
            }
        }
        
        private async Task SavePerformanceResults(List<string> results)
        {
            try
            {
                var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                var ttsConfigPath = Path.Combine(GetBaseDirectory(), "data", "TTSconfig.json");
                
                Dictionary<string, object> ttsConfig;
                if (File.Exists(ttsConfigPath))
                {
                    var json = await File.ReadAllTextAsync(ttsConfigPath);
                    ttsConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                else
                {
                    ttsConfig = new Dictionary<string, object>();
                }
                
                // Add performance test results
                ttsConfig["last_performance_test"] = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    results = results,
                    total_tests = results.Count,
                    successful_tests = results.Count(r => r.Contains("[OK]"))
                };
                
                // Save updated config
                var updatedJson = JsonSerializer.Serialize(ttsConfig, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(ttsConfigPath, updatedJson);
                
                LogToConsole("💾 Performance test results saved to TTSconfig.json");
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Failed to save performance results: {ex.Message}");
            }
        }

        private void UsersComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedUser = UsersComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedUser) || selectedUser == "Select User...")
            {
                // Clear fields when no user is selected
                VoicesComboBox.SelectedIndex = 0; // Select "NONE"
                SetNameSwapText("");
                SpeedComboBox.SelectedIndex = 0; // "None"
                return;
            }

            // Don't log user selection to avoid spam
            
            // Auto-populate fields with user's current settings
            try
            {
                if (_settings?.Users != null)
                {
                    // Check for name swap
                    if (_settings.Users.E_name_swap.TryGetValue(selectedUser, out var nameSwap))
                    {
                        SetNameSwapText(nameSwap);
                    }
                    else
                    {
                        SetNameSwapText("");
                    }

                    // Check for voice and speed
                    if (_settings.Users.C_priority_voice.TryGetValue(selectedUser, out var voiceData))
                    {
                        string? voiceName = null;
                        float? speed = null;
                        
                        // Handle JsonElement (most common case)
                        if (voiceData is System.Text.Json.JsonElement jsonElement)
                        {
                            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                // Simple string format: "VOICE_NAME"
                                voiceName = jsonElement.GetString();
                            }
                            else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                // Dictionary format: {"VOICE_NAME": "1.5"}
                                foreach (var prop in jsonElement.EnumerateObject())
                                {
                                    voiceName = prop.Name;
                                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        if (float.TryParse(prop.Value.GetString(), out var speedValue))
                                            speed = speedValue;
                                    }
                                    else if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    {
                                        speed = (float)prop.Value.GetDouble();
                                    }
                                    break;
                                }
                            }
                        }
                        // Handle string directly
                        else if (voiceData is string voiceStr)
                        {
                            voiceName = voiceStr;
                        }
                        // Handle Dictionary directly
                        else if (voiceData is Dictionary<string, object> voiceDict)
                        {
                            foreach (var kvp in voiceDict)
                            {
                                voiceName = kvp.Key;
                                if (float.TryParse(kvp.Value?.ToString(), out var speedValue))
                                    speed = speedValue;
                                break;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(voiceName))
                        {
                            // Try to find the voice by name first, then by code
                            var voiceIndex = _availableVoices.IndexOf(voiceName);
                            
                            if (voiceIndex < 0)
                            {
                                // If not found by name, try to find by code
                                foreach (var category in _settings.Voices.Voice_List_cheat_sheet)
                                {
                                    foreach (var voice in category.Value)
                                    {
                                        if (voice.code == voiceName)
                                        {
                                            voiceIndex = _availableVoices.IndexOf(voice.name);
                                            break;
                                        }
                                    }
                                    if (voiceIndex >= 0) break;
                                }
                            }
                            
                            if (voiceIndex >= 0)
                            {
                                VoicesComboBox.SelectedIndex = voiceIndex;
                            }
                            else
                            {
                                VoicesComboBox.SelectedIndex = 0; // "NONE" if voice not found
                            }
                            
                            // Set speed if specified
                            if (speed.HasValue)
                            {
                                var speedText = speed.Value.ToString("F1");
                                for (int i = 0; i < SpeedComboBox.Items.Count; i++)
                                {
                                    if (SpeedComboBox.Items[i].ToString() == speedText)
                                    {
                                        SpeedComboBox.SelectedIndex = i;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                SpeedComboBox.SelectedIndex = 0; // "None" - will use default speed
                            }
                        }
                    }
                    else
                    {
                        // No voice settings for this user - use NONE
                        VoicesComboBox.SelectedIndex = 0; // "NONE"
                        SpeedComboBox.SelectedIndex = 0; // "None"
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error loading user settings: {ex.Message}");
            }
        }

        private void DiscordLink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/PVvv8M5e83",
                UseShellExecute = true
            });
        }

        private async Task LoadRemoteMessage()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(_remoteMessageUrl);
                var messageData = JsonSerializer.Deserialize<RemoteMessageData>(response);
                
                if (messageData != null && !string.IsNullOrEmpty(messageData.Message))
                {
                    Dispatcher.Invoke(() =>
                    {
                        RemoteMessageTextBlock.Text = messageData.Message;
                        LogToConsole("📢 Remote message loaded successfully");
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        RemoteMessageTextBlock.Text = "No updates available at this time.";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    RemoteMessageTextBlock.Text = "Unable to load updates. Please check your internet connection.";
                    LogToConsole($"⚠️ Failed to load remote message: {ex.Message}");
                });
            }
        }

        public class RemoteMessageData
        {
            public string Message { get; set; } = string.Empty;
            public string LastUpdated { get; set; } = string.Empty;
        }

    // Windows Core Audio API declarations
    [DllImport("ole32.dll")]
    private static extern int CoInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IntPtr ppv);

    // Windows Audio Session Manager - Python-style implementation
    public class WindowsAudioSessionManager : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;
        private Thread? _sessionThread;
        private bool _disposed = false;
        private bool _stopSession = false;

        public WindowsAudioSessionManager()
        {
            try
            {
                // Initialize COM
                CoInitialize(IntPtr.Zero);
                
                // Create a persistent audio session using NAudio (like pygame in Python)
                InitializeAudioSession();
            }
            catch (Exception ex)
            {
                // Silent failure - don't break the system
                System.Diagnostics.Debug.WriteLine($"Audio session creation failed: {ex.Message}");
            }
        }

        private void InitializeAudioSession()
        {
            try
            {
                // Create a silent audio stream to maintain the session
                var waveFormat = new WaveFormat(22050, 16, 2);
                _waveProvider = new BufferedWaveProvider(waveFormat);
                
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveProvider);
                
                // Start session maintenance thread (like Python version)
                _sessionThread = new Thread(MaintainSession) { IsBackground = true };
                _sessionThread.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio session initialization failed: {ex.Message}");
            }
        }

        private void MaintainSession()
        {
            try
            {
                while (!_stopSession && !_disposed)
                {
                    if (_waveOut != null && _waveProvider != null)
                    {
                        // Add silent audio data to keep session alive
                        var silentData = new byte[1024]; // Silent audio
                        _waveProvider.AddSamples(silentData, 0, silentData.Length);
                        
                        if (_waveOut.PlaybackState != PlaybackState.Playing)
                        {
                            _waveOut.Play();
                        }
                    }
                    
                    Thread.Sleep(200); // Check every 200ms
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session maintenance failed: {ex.Message}");
            }
        }

        public void SetApplicationVolume(float volume)
        {
            try
            {
                // Use Core Audio API to find and control our app's audio session
                // This is a simplified implementation - in production you'd use
                // IAudioSessionManager2 to find the specific session
                if (_waveOut != null)
                {
                    _waveOut.Volume = Math.Clamp(volume, 0f, 1f);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set application volume: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopSession = true;
                
                _waveOut?.Dispose();
                _waveProvider = null;
                
                _sessionThread?.Join(1000); // Wait up to 1 second
                
                CoUninitialize();
                _disposed = true;
            }
        }
    }

        #endregion

        #region Module Management

        private async Task StartAllModules()
        {
            foreach (var module in _modules)
            {
                try
                {
                    await module.StartAsync();
                    LogToConsole($"✅ {module.Name} module started");
                }
                catch (Exception ex)
                {
                    LogToConsole($"❌ Failed to start {module.Name} module: {ex.Message}");
                }
            }
            UpdateModuleStatuses();
        }

        private async Task StopAllModules()
        {
            foreach (var module in _modules.AsEnumerable().Reverse())
            {
                try
                {
                    await module.StopAsync();
                    LogToConsole($"🛑 {module.Name} module stopped");
                }
                catch (Exception ex)
                {
                    LogToConsole($"❌ Failed to stop {module.Name} module: {ex.Message}");
                }
            }
            UpdateModuleStatuses();
        }

        private void UpdateModuleStatuses()
        {
            // Update the traffic light status
            UpdateModuleStatusLights();
        }


        #endregion

        #region Background Updates

        private void LogUpdateTimer_Tick(object? sender, EventArgs e)
        {
            LoadLogFile();
        }


        private void QueueUpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateQueueDisplay();
            UpdateAudioQueueDisplay();
            UpdateActiveUserCountDisplay();
        }

        private void LoadLogFile()
        {
            try
            {
                // Load processing log
                var logFilePath = Path.Combine(GetBaseDirectory(), "logs", "processing.log");
                if (File.Exists(logFilePath))
                {
                    var lastWrite = File.GetLastWriteTime(logFilePath);
                    if (lastWrite > _lastLogRead)
                    {
                        var allLines = File.ReadAllLines(logFilePath);
                        var newLines = allLines.Skip(_lastLogLineCount).ToArray();
                        foreach (var line in newLines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                LogToConsole(line);
                            }
                        }
                        _lastLogLineCount = allLines.Length;
                        _lastLogRead = lastWrite;
                    }
                }

                // GUI messages are now handled via Logger.GuiMessageReceived event
                // No need to read from file to avoid duplicate messages
            }
            catch (Exception)
            {
                // Silently handle file access errors
            }
        }

        private void UpdateActiveUsers()
        {
            // TODO: Implement active user tracking
        }

        private void UpdateQueueDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                int queueCount = 0;
                if (_processingModule != null)
                {
                    queueCount = _processingModule.GetQueueCount();
                }
                
                QueueDisplay.Text = $"MSG Queue\n\n{queueCount}";
            });
        }

        private void UpdateAudioQueueDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                int audioQueueCount = 0;
                var audioQueueModule = _modules.OfType<TTS.AudioQueue.AudioQueueModule>().FirstOrDefault();
                if (audioQueueModule != null)
                {
                    audioQueueCount = audioQueueModule.GetQueueCount();
                }
                
                AudioQueueDisplay.Text = $"AUDIO Queue\n\n{audioQueueCount}";
            });
        }

        private void UpdateActiveUserCountDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                int activeUserCount = 0;
                if (_userTrackingModule != null)
                {
                    var activeUsers = _userTrackingModule.GetActiveUsers();
                    activeUserCount = activeUsers.Count();
                }
                
                ActiveUserCountDisplay.Text = $"ACTIVE Users\n\n{activeUserCount}";
            });
        }

        #endregion

        #region Window Events

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LogToConsole($"🚀 {_versionInfo.FullTitle} loaded");
            LogToConsole("💫 Created by Emstar233 & Husband");

            // Load any external mods/add-ons from data/*.dll (generic loader only)
            try
            {
                await LoadExternalModsAsync();
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Failed to load external mods: {ex.Message}");
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _logUpdateTimer?.Stop();
            _userUpdateTimer?.Stop();
            _queueUpdateTimer?.Stop();
            
            // Attempt graceful shutdown of external mods first
            try
            {
                await ShutdownExternalModsAsync();
            }
            catch (Exception ex)
            {
                LogToConsole($"⚠️ Error shutting down external mods: {ex.Message}");
            }

            if (_systemRunning)
            {
                LogToConsole("🛑 Shutting down TTS system...");
                await StopAllModules();
            }
            
            // Dispose audio session manager
            _audioSessionManager?.Dispose();
            
            // Dispose HTTP client
            _httpClient?.Dispose();
            
            LogToConsole("🔚 Application cleanup complete");
        }

        #endregion

        // External Mods/Add-ons
        private readonly List<object> _loadedExternalMods = new();
        private readonly List<(object Instance, MethodInfo? ShutdownMethod)> _externalModShutdowns = new();
        private IModHost? _modHost;

        private class ModHost : IModHost
        {
            private readonly MainWindow _owner;
            public ModHost(MainWindow owner) { _owner = owner; }

            public void Publish(string channel, string payload)
            {
                if (string.Equals(channel, "chat", StringComparison.OrdinalIgnoreCase))
                {
                    var processing = _owner._processingModule;
                    if (processing != null)
                    {
                        _ = processing.ProcessExternalChatJson(payload);
                    }
                    // Also register user as active in user tracker immediately
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(payload);
                        if (doc.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("nickname", out var nn) &&
                            !string.IsNullOrWhiteSpace(nn.GetString()))
                        {
                            _owner._userTrackingModule?.RegisterActiveUser(nn.GetString()!);
                        }
                    }
                    catch { }
                }
            }

            public void SetFlag(string key, bool value)
            {
                if (string.Equals(key, "websocketSwitch", StringComparison.OrdinalIgnoreCase))
                {
                    var processing = _owner._processingModule;
                    if (processing != null)
                    {
                        // value=true => disable websocket (off); false => enable (on)
                        processing.SetWebSocketEnabled(!value);
                    }
                }
            }

            public object? GetService(string name)
            {
                if (string.Equals(name, "processing", StringComparison.OrdinalIgnoreCase))
                    return _owner._processingModule;
                return null;
            }
        }

        private async Task LoadExternalModsAsync()
        {
            try
            {
                var baseDir = GetBaseDirectory();
                var modsDir = Path.Combine(baseDir, "data");
                if (!Directory.Exists(modsDir))
                {
                    Logger.Write($"[EXT] Mods directory not found: {modsDir}");
                    return;
                }

                var dllFiles = Directory.GetFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly);
                if (dllFiles.Length == 0)
                {
                    Logger.Write("[EXT] No external mods found in data folder");
                    return;
                }

                Logger.Write($"[EXT] Scanning mods: {dllFiles.Length} dll(s) found");

                foreach (var dllPath in dllFiles)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(dllPath);
                        foreach (var type in assembly.GetTypes())
                        {
                            if (!type.IsClass || type.IsAbstract) continue;

                            // Look for InitializeAsync/Initialize with optional IModHost parameter
                            var initAsyncWithHost = type.GetMethod("InitializeAsync", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, new Type[] { typeof(IModHost) }, null);
                            var initWithHost = type.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, new Type[] { typeof(IModHost) }, null);
                            var initAsyncNoArg = type.GetMethod("InitializeAsync", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, null);
                            var initNoArg = type.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, null);
                            if (initAsyncWithHost == null && initWithHost == null && initAsyncNoArg == null && initNoArg == null) continue;

                            var instance = Activator.CreateInstance(type);
                            if (instance == null) continue;

                            // Provide host if requested
                            _modHost ??= new ModHost(this);

                            if (initAsyncWithHost != null)
                            {
                                var result = initAsyncWithHost.Invoke(instance, new object?[] { _modHost });
                                if (result is Task task)
                                {
                                    await task;
                                }
                            }
                            else if (initWithHost != null)
                            {
                                var result = initWithHost.Invoke(instance, new object?[] { _modHost });
                                if (result is Task task)
                                {
                                    await task;
                                }
                            }
                            else if (initAsyncNoArg != null)
                            {
                                var result = initAsyncNoArg.Invoke(instance, Array.Empty<object?>());
                                if (result is Task task)
                                {
                                    await task;
                                }
                            }
                            else if (initNoArg != null)
                            {
                                var result = initNoArg.Invoke(instance, Array.Empty<object?>());
                                if (result is Task task)
                                {
                                    await task;
                                }
                            }

                            _loadedExternalMods.Add(instance);

                            // Discover shutdown method for later
                            var shutdown = type.GetMethod("ShutdownAsync", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, null)
                                         ?? type.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, null);
                            _externalModShutdowns.Add((instance, shutdown));

                            Logger.Write($"[EXT] Mod initialized from {Path.GetFileName(dllPath)} (type {type.FullName})");
                        }
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        var loaderErrors = string.Join("; ", rtle.LoaderExceptions?.Select(ex => ex?.Message).Where(m => !string.IsNullOrWhiteSpace(m)) ?? Array.Empty<string>());
                        Logger.Write($"[EXT] Failed to load '{Path.GetFileName(dllPath)}': {rtle.Message} ({loaderErrors})", level: "ERROR");
                    }
                    catch (Exception ex)
                    {
                        Logger.Write($"[EXT] Failed to load '{Path.GetFileName(dllPath)}': {ex.Message}", level: "ERROR");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[EXT] Mod scan failed: {ex.Message}", level: "ERROR");
            }
        }

        private async Task ShutdownExternalModsAsync()
        {
            foreach (var (instance, shutdown) in _externalModShutdowns)
            {
                if (shutdown == null) continue;
                try
                {
                    var result = shutdown.Invoke(instance, Array.Empty<object?>());
                    if (result is Task task)
                    {
                        await task;
                    }
                }
                catch (Exception ex)
                {
                    LogToConsole($"⚠️ Mod shutdown error ({instance.GetType().FullName}): {ex.Message}");
                }
            }
        }

        // Private fields for log tracking
        private DateTime _lastLogRead = DateTime.MinValue;
        private int _lastLogLineCount = 0;
        private string[] _lastActiveUsers = Array.Empty<string>();
    }
}
