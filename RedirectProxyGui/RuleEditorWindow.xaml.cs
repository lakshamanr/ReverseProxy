// RuleEditorWindow.xaml.cs
using RedirectProxyGui.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RedirectProxyGui
{
    public partial class RuleEditorWindow : Window
    {
        public Rule EditedRule { get; private set; }
        public bool Saved { get; private set; }

        private ObservableCollection<SimpleHeader> _headers = new();
        private ObservableCollection<SimpleHeader> _loginHeaders = new();
        private ObservableCollection<CookiePair> _cookiePairs = new();

        public RuleEditorWindow(Rule? existingRule = null)
        {
            InitializeComponent();

            HeadersGrid.ItemsSource = _headers;
            LoginHeadersGrid.ItemsSource = _loginHeaders;
            CookiePairsGrid.ItemsSource = _cookiePairs;

            BtnSave.Click += BtnSave_Click;
            BtnCancel.Click += BtnCancel_Click;

            EditedRule = existingRule ?? new Rule();
            LoadRule(EditedRule);
        }

        private void LoadRule(Rule rule)
        {
            TxtMatch.Text = rule.Match;
            TxtTo.Text = rule.To;
            TxtApiKey.Text = rule.ApiKey;
            ChkEnabled.IsChecked = rule.Enabled;

            // Load headers
            _headers.Clear();
            if (rule.Headers != null)
                foreach (var h in rule.Headers)
                    _headers.Add(new SimpleHeader { Name = h.Name, Value = h.Value });

            // Load login config
            if (rule.Login != null)
            {
                TxtLoginUrl.Text = rule.Login.LoginUrl;
                CmbLoginMethod.SelectedIndex = rule.Login.Method switch
                {
                    "GET" => 1,
                    "PUT" => 2,
                    _ => 0 // POST
                };
                CmbContentType.Text = rule.Login.ContentType ?? "application/json";
                TxtLoginBody.Text = rule.Login.Body;

                // Load login headers
                _loginHeaders.Clear();
                if (rule.Login.Headers != null)
                    foreach (var h in rule.Login.Headers)
                        _loginHeaders.Add(new SimpleHeader { Name = h.Name, Value = h.Value });

                // Load cookie pairs
                _cookiePairs.Clear();
                if (rule.Login.CookiePairs != null)
                    foreach (var cp in rule.Login.CookiePairs)
                        _cookiePairs.Add(new CookiePair
                        {
                            Name = cp.Name,
                            Value = cp.Value,
                            Domain = cp.Domain,
                            Path = cp.Path
                        });
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var match = TxtMatch.Text.Trim();
            var to = TxtTo.Text.Trim();

            if (string.IsNullOrEmpty(match) || string.IsNullOrEmpty(to))
            {
                MessageBox.Show("Match and To are required fields", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build the rule
            EditedRule = new Rule
            {
                Match = match,
                To = to,
                ApiKey = TxtApiKey.Text.Trim(),
                Enabled = ChkEnabled.IsChecked ?? true,
                Headers = _headers.Where(h => !string.IsNullOrWhiteSpace(h.Name)).ToList()
            };

            // Add login config if LoginUrl is provided
            if (!string.IsNullOrWhiteSpace(TxtLoginUrl.Text))
            {
                EditedRule.Login = new LoginConfig
                {
                    LoginUrl = TxtLoginUrl.Text.Trim(),
                    Method = (CmbLoginMethod.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content.ToString() ?? "POST",
                    ContentType = CmbContentType.Text.Trim(),
                    Body = TxtLoginBody.Text,
                    Headers = _loginHeaders.Where(h => !string.IsNullOrWhiteSpace(h.Name)).ToList(),
                    CookiePairs = _cookiePairs.Where(cp => !string.IsNullOrWhiteSpace(cp.Name)).ToList()
                };
            }

            Saved = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Saved = false;
            DialogResult = false;
            Close();
        }
    }
}
