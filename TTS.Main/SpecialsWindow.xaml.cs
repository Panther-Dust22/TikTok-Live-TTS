using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TTS.Shared.Configuration;
using TTS.Shared.Utils;

namespace TTS.Main
{
    public partial class SpecialsWindow : Window
    {
        private Settings? _settings;
        private Dictionary<string, ComboBox> _voiceMappingControls = new();

        public SpecialsWindow()
        {
            InitializeComponent();
            LoadSettings();
            PopulateVoiceOptions();
            
            // Use Dispatcher to ensure UI is fully loaded before setting values
            Dispatcher.BeginInvoke(new Action(async () => 
            {
                // Wait a bit for the ComboBoxes to fully initialize
                await Task.Delay(100);
                LoadCurrentSettings();
            }), DispatcherPriority.Loaded);
        }

        private void LoadSettings()
        {
            try
            {
                _settings = Settings.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateVoiceOptions()
        {
            var voiceOptions = new List<string> { "Default" };
            
            if (_settings?.Voices?.Voice_List_cheat_sheet != null)
            {
                // Get actual voice names from all categories
                foreach (var category in _settings.Voices.Voice_List_cheat_sheet.Values)
                {
                    foreach (var voice in category)
                    {
                        if (!string.IsNullOrEmpty(voice.name))
                        {
                            voiceOptions.Add(voice.name);
                        }
                    }
                }
                Logger.Write($"[SPECIALS] Loaded {voiceOptions.Count - 1} actual voices from cheat sheet");
            }
            else
            {
                Logger.Write("[SPECIALS] Voice_List_cheat_sheet is null");
            }

            Logger.Write($"[SPECIALS] Total voice options: {voiceOptions.Count}");
            foreach (var voice in voiceOptions.Take(10)) // Show first 10 voices
            {
                Logger.Write($"[SPECIALS] Voice option: '{voice}'");
            }

            // Initialize voice mapping controls - using exact keys from options.json
            _voiceMappingControls = new Dictionary<string, ComboBox>
            {
                { "Subscriber", SubscriberVoiceComboBox },
                { "Moderator", ModeratorVoiceComboBox },
                { "Follow Role 0", VipVoiceComboBox }, // Map VIP to Follow Role 0
                { "Follow Role 1", FollowerVoiceComboBox }, // Map Follower to Follow Role 1
                { "Follow Role 2", FollowRole2VoiceComboBox }, // Map Follow Role 2 to its own ComboBox
                { "Top Gifter 1", TopGifter1VoiceComboBox },
                { "Top Gifter 2", TopGifter2VoiceComboBox },
                { "Top Gifter 3", TopGifter3VoiceComboBox },
                { "Top Gifter 4", TopGifter4VoiceComboBox },
                { "Top Gifter 5", TopGifter5VoiceComboBox },
                { "BadWordVoice", FilterReplyVoiceComboBox },
                { "Default", DefaultVoiceComboBox }
            };

            Logger.Write($"[SPECIALS] Initialized {_voiceMappingControls.Count} voice mapping controls");

            // Populate all combo boxes by adding items directly
            foreach (var comboBox in _voiceMappingControls.Values)
            {
                comboBox.Items.Clear();
                foreach (var voice in voiceOptions)
                {
                    comboBox.Items.Add(voice);
                }
            }
        }

        private void LoadCurrentSettings()
        {
            try
            {
                // Load default speed
                if (_settings?.App?.playback_speed != null)
                {
                    DefaultSpeedTextBox.Text = _settings.App.playback_speed.ToString();
                }

                // Load voice mappings
                if (_settings?.Options?.D_voice_map != null)
                {
                    Logger.Write($"[SPECIALS] Loading voice mappings. Count: {_settings.Options.D_voice_map.Count}");
                    foreach (var mapping in _settings.Options.D_voice_map)
                    {
                        Logger.Write($"[SPECIALS] Key: '{mapping.Key}', Value: '{mapping.Value}'");
                        if (_voiceMappingControls.TryGetValue(mapping.Key, out var comboBox))
                        {
                            Logger.Write($"[SPECIALS] Found ComboBox for key '{mapping.Key}', setting to '{mapping.Value}'");
                            
                            // Find and set the selected item directly
                            var matchingItem = comboBox.Items.Cast<string>().FirstOrDefault(item => item == mapping.Value);
                            if (matchingItem != null)
                            {
                                // Try a different approach - clear selection first, then set
                                comboBox.SelectedIndex = -1;
                                comboBox.SelectedItem = null;
                                
                                // Force a layout update
                                comboBox.UpdateLayout();
                                
                                // Now set the selected item
                                comboBox.SelectedItem = matchingItem;
                                
                                // Force another update
                                comboBox.UpdateLayout();
                                comboBox.InvalidateVisual();
                                
                                // Verify the selection was set
                                var currentSelection = comboBox.SelectedItem?.ToString();
                                Logger.Write($"[SPECIALS] Set SelectedItem to '{matchingItem}', current selection: '{currentSelection}'");
                            }
                            else
                            {
                                Logger.Write($"[SPECIALS] Could not find matching item '{mapping.Value}' in ComboBox items");
                                var availableItems = comboBox.Items.Cast<string>().Take(5).ToList();
                                Logger.Write($"[SPECIALS] Available items: {string.Join(", ", availableItems)}...");
                            }
                        }
                        else
                        {
                            Logger.Write($"[SPECIALS] No ComboBox found for key '{mapping.Key}'");
                        }
                    }
                }
                else
                {
                    Logger.Write("[SPECIALS] D_voice_map is null or empty");
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[SPECIALS] Error loading current settings: {ex.Message}");
                MessageBox.Show($"Error loading current settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate default speed
                if (!double.TryParse(DefaultSpeedTextBox.Text, out double defaultSpeed) || defaultSpeed <= 0)
                {
                    MessageBox.Show("Please enter a valid default speed (positive number).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Update default speed in config
                if (_settings?.App != null)
                {
                    _settings.App.playback_speed = (float)defaultSpeed;
                }

                // Update voice mappings
                if (_settings?.Options != null)
                {
                    _settings.Options.D_voice_map = new Dictionary<string, string>();
                    
                    foreach (var mapping in _voiceMappingControls)
                    {
                        var selectedVoice = mapping.Value.SelectedItem?.ToString();
                        if (!string.IsNullOrEmpty(selectedVoice))
                        {
                            _settings.Options.D_voice_map[mapping.Key] = selectedVoice;
                        }
                    }
                }

                // Save settings
                JsonUtil.SaveJson(_settings.Options, Settings.ResolveDataPath("options.json"));
                JsonUtil.SaveJson(_settings.App, Settings.ResolveDataPath("config.json"));

                MessageBox.Show("Special settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ApplyCurrentTheme()
        {
            // Copy theme resources from main window
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                this.Resources.MergedDictionaries.Clear();
                foreach (var dict in mainWindow.Resources.MergedDictionaries)
                {
                    this.Resources.MergedDictionaries.Add(dict);
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyCurrentTheme();
        }
    }
}
