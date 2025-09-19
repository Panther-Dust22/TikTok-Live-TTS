using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TTS.Shared.Configuration;
using TTS.Shared.Utils;

namespace TTS.Main
{
    public partial class EditBadWordsWindow : Window
    {
        private Settings? _settings;

        public EditBadWordsWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadCurrentFilterData();
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

        private void LoadCurrentFilterData()
        {
            try
            {
                // Load bad words
                if (_settings?.Filter?.B_word_filter != null && _settings.Filter.B_word_filter.Count > 0)
                {
                    BadWordsTextBox.Text = string.Join("\n", _settings.Filter.B_word_filter);
                }
                else
                {
                    BadWordsTextBox.Text = "# Add bad words here, one per line\n# Example:\n# badword1\n# badword2";
                }

                // Load reply messages
                if (_settings?.Filter?.B_filter_reply != null && _settings.Filter.B_filter_reply.Count > 0)
                {
                    ReplyMessagesTextBox.Text = string.Join("\n", _settings.Filter.B_filter_reply);
                }
                else
                {
                    ReplyMessagesTextBox.Text = "# Add reply messages here, one per line\n# These are sent when bad words are detected\n# Example:\n# Please keep chat family friendly!\n# That language is not allowed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading filter data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Parse bad words
                var badWords = new List<string>();
                var badWordsText = BadWordsTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(badWordsText))
                {
                    badWords = badWordsText.Split('\n')
                        .Select(word => word.Trim())
                        .Where(word => !string.IsNullOrEmpty(word) && !word.StartsWith("#"))
                        .ToList();
                }

                // Parse reply messages
                var replyMessages = new List<string>();
                var replyText = ReplyMessagesTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(replyText))
                {
                    replyMessages = replyText.Split('\n')
                        .Select(message => message.Trim())
                        .Where(message => !string.IsNullOrEmpty(message) && !message.StartsWith("#"))
                        .ToList();
                }

                // Update settings
                if (_settings?.Filter != null)
                {
                    _settings.Filter.B_word_filter = badWords;
                    _settings.Filter.B_filter_reply = replyMessages;
                }

                // Save settings
                JsonUtil.SaveJson(_settings.Filter, Settings.ResolveDataPath("filter.json"));

                MessageBox.Show($"Filter settings saved successfully!\nBad words: {badWords.Count}\nReply messages: {replyMessages.Count}", 
                               "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving filter settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
