using System.Windows;
using System.Windows.Controls;

namespace TTS.Main.Controls
{
    public partial class WhiteTextBox : UserControl
    {
        public WhiteTextBox()
        {
            InitializeComponent();
        }

        public string Text
        {
            get => InnerTextBox.Text;
            set => InnerTextBox.Text = value;
        }

        public new bool IsEnabled
        {
            get => InnerTextBox.IsEnabled;
            set => InnerTextBox.IsEnabled = value;
        }

        public new bool IsReadOnly
        {
            get => InnerTextBox.IsReadOnly;
            set => InnerTextBox.IsReadOnly = value;
        }
    }
}
