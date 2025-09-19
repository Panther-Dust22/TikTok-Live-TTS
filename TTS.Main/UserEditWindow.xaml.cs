using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using TTS.Shared.Configuration;
using TTS.Shared.Utils;

namespace TTS.Main
{
    public partial class UserEditWindow : Window
    {
        private readonly Settings _settings;
        private readonly ObservableCollection<UserEditItem> _userItems = new();

        public List<string> VoiceOptions { get; } = new();
        public List<string> SpeedOptions { get; } = new();

        public UserEditWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings;
            DataContext = this;
            
            // Apply current theme from main window
            ApplyCurrentTheme();
            
            PopulateVoiceAndSpeedOptions();
            LoadUserData();
        }

        private void ApplyCurrentTheme()
        {
            try
            {
                // Get the current theme from the main window's resources
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // Copy the merged dictionaries from main window
                    this.Resources.MergedDictionaries.Clear();
                    foreach (var dict in mainWindow.Resources.MergedDictionaries)
                    {
                        this.Resources.MergedDictionaries.Add(dict);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme to UserEditWindow: {ex.Message}");
            }
        }

        private void LoadUserData()
        {
            _userItems.Clear();

            // Get all users from both voice and name swap settings
            var allUsers = new HashSet<string>();
            
            if (_settings.Users.C_priority_voice != null)
            {
                foreach (var user in _settings.Users.C_priority_voice.Keys)
                {
                    allUsers.Add(user);
                }
            }
            
            if (_settings.Users.E_name_swap != null)
            {
                foreach (var user in _settings.Users.E_name_swap.Keys)
                {
                    allUsers.Add(user);
                }
            }

            // Create UserEditItem for each user
            foreach (var userName in allUsers.OrderBy(u => u))
            {
                var item = new UserEditItem
                {
                    OriginalName = userName,
                    NameSwap = _settings.Users.E_name_swap?.TryGetValue(userName, out var nameSwap) == true ? nameSwap : "",
                    Voice = "",
                    Speed = "None"
                };

                // Get voice and speed settings (same logic as main console)
                if (_settings.Users.C_priority_voice?.TryGetValue(userName, out var voiceData) == true)
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
                    // Fallback for other types
                    else if (voiceData is string stringVoice)
                    {
                        voiceName = stringVoice;
                    }
                    else if (voiceData is Dictionary<string, object> voiceDict)
                    {
                        foreach (var kvp in voiceDict)
                        {
                            voiceName = kvp.Key;
                            if (float.TryParse(kvp.Value?.ToString(), out var speedValue))
                            {
                                speed = speedValue;
                            }
                            break;
                        }
                    }
                    
                    // Set the values
                    if (!string.IsNullOrEmpty(voiceName))
                    {
                        item.Voice = voiceName;
                        item.Speed = speed?.ToString("F1") ?? "None";
                    }
                }

                // Speed is already handled in the voice data parsing above

                _userItems.Add(item);
            }

            UserListItemsControl.ItemsSource = _userItems;
            
            // Debug: Log what we're setting
            foreach (var item in _userItems)
            {
                System.Diagnostics.Debug.WriteLine($"User: {item.OriginalName}, Voice: '{item.Voice}', Speed: '{item.Speed}', NameSwap: '{item.NameSwap}'");
            }
        }

        private void PopulateVoiceAndSpeedOptions()
        {
            // Get voice options (same logic as main console)
            VoiceOptions.Clear();
            VoiceOptions.Add("NONE"); // Add NONE as first option
            
            if (_settings.Voices?.Voice_List_cheat_sheet != null)
            {
                foreach (var category in _settings.Voices.Voice_List_cheat_sheet)
                {
                    foreach (var voice in category.Value)
                    {
                        VoiceOptions.Add(voice.name);
                    }
                }
            }

            // Get speed options (same as main console)
            SpeedOptions.Clear();
            SpeedOptions.Add("None");
            for (int i = 1; i <= 20; i++)
            {
                SpeedOptions.Add((i / 10.0).ToString("F1"));
            }
            
            // Debug: Log available options
            System.Diagnostics.Debug.WriteLine($"Available Voices: {string.Join(", ", VoiceOptions)}");
            System.Diagnostics.Debug.WriteLine($"Available Speeds: {string.Join(", ", SpeedOptions)}");
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            UserEditItem item = null;
            
            if (sender is Button button && button.Tag is UserEditItem buttonItem)
            {
                item = buttonItem;
            }
            else if (sender is TextBlock textBlock && textBlock.Tag is UserEditItem textItem)
            {
                item = textItem;
            }
            
            if (item != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete all settings for '{item.OriginalName}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _userItems.Remove(item);
                    Logger.Write($"🗑️ Marked user '{item.OriginalName}' for deletion");
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool hasChanges = false;

                // Clear existing settings
                _settings.Users.C_priority_voice.Clear();
                _settings.Users.E_name_swap.Clear();

                // Apply current settings from the UI
                foreach (var item in _userItems)
                {
                    // Apply name swap if specified
                    if (!string.IsNullOrWhiteSpace(item.NameSwap))
                    {
                        _settings.Users.E_name_swap[item.OriginalName] = item.NameSwap.Trim();
                        hasChanges = true;
                    }

                    // Apply voice and speed if specified
                    if (!string.IsNullOrWhiteSpace(item.Voice) && item.Voice != "NONE")
                    {
                        if (item.Speed == "None" || string.IsNullOrWhiteSpace(item.Speed))
                        {
                            // Voice only, no speed (will use default speed)
                            _settings.Users.C_priority_voice[item.OriginalName] = item.Voice;
                        }
                        else
                        {
                            // Voice with specific speed
                            if (float.TryParse(item.Speed, out var speedValue))
                            {
                                _settings.Users.C_priority_voice[item.OriginalName] = new Dictionary<string, object>
                                {
                                    { item.Voice, speedValue.ToString("F3") }
                                };
                            }
                            else
                            {
                                // Invalid speed, just use voice
                                _settings.Users.C_priority_voice[item.OriginalName] = item.Voice;
                            }
                        }
                        hasChanges = true;
                    }
                    else if (item.Voice == "NONE")
                    {
                        // Remove voice settings if NONE is selected
                        if (_settings.Users.C_priority_voice.ContainsKey(item.OriginalName))
                        {
                            _settings.Users.C_priority_voice.Remove(item.OriginalName);
                            hasChanges = true;
                        }
                    }
                }

                if (hasChanges)
                {
                    // Save to file
                    _settings.Users.Save();
                    _settings.ForceReloadFromDisk();

                    Logger.Write("💾 User settings saved successfully");
                    MessageBox.Show("User settings saved successfully!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No changes to save.", "Info", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Logger.Write($"Error saving user settings: {ex.Message}", "ERROR");
                MessageBox.Show($"Error saving user settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class UserEditItem : INotifyPropertyChanged
    {
        private string _originalName = string.Empty;
        private string _nameSwap = string.Empty;
        private string _voice = string.Empty;
        private string _speed = "None";

        public string OriginalName 
        { 
            get => _originalName; 
            set { _originalName = value; OnPropertyChanged(); } 
        }
        
        public string NameSwap 
        { 
            get => _nameSwap; 
            set { _nameSwap = value; OnPropertyChanged(); } 
        }
        
        public string Voice 
        { 
            get => _voice; 
            set { _voice = value; OnPropertyChanged(); } 
        }
        
        public string Speed 
        { 
            get => _speed; 
            set { _speed = value; OnPropertyChanged(); } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
