// MainWindow.xaml.cs
using RedirectProxyGui.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace RedirectProxyGui
{
    public partial class MainWindow : Window
    {
        private ProxyEngine _engine;
        private readonly string _rulesFile = Path.Combine(AppContext.BaseDirectory, "rules.json");

        public MainWindow()
        {
            InitializeComponent();

            _engine = new ProxyEngine(_rulesFile);
            _engine.OnIntercept += Engine_OnIntercept;
            _engine.RulesReloaded += Engine_RulesReloaded;

            BtnStart.Click += BtnStart_Click;
            BtnStop.Click += BtnStop_Click;
            BtnReload.Click += BtnReload_Click;
            RulesList.SelectionChanged += RulesList_SelectionChanged;
            BtnSaveRule.Click += BtnSaveRule_Click;
            BtnPerformLogin.Click += BtnPerformLogin_Click;

            RefreshRulesList();
        }

        private void Engine_RulesReloaded() => Dispatcher.Invoke(RefreshRulesList);

        private void Engine_OnIntercept(InterceptLog log)
        {
            Dispatcher.Invoke(() =>
            {
                LogList.Items.Insert(0, log);
                if (LogList.Items.Count > 500)
                    LogList.Items.RemoveAt(LogList.Items.Count - 1);
            });
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _engine.Start(8000);
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                MessageBox.Show("Proxy started on http://localhost:8000", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Start failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            MessageBox.Show("Proxy stopped", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => RefreshRulesList();

        private void RefreshRulesList()
        {
            RulesList.Items.Clear();
            foreach (var r in _engine.Rules)
                RulesList.Items.Add(r);
        }

        private void RulesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RulesList.SelectedItem is Rule r)
            {
                TxtMatch.Text = r.Match;
                TxtTo.Text = r.To;
                TxtApiKey.Text = r.ApiKey;
                TxtLoginUrl.Text = r.Login?.LoginUrl ?? "";
            }
        }

        private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
        {
            var match = TxtMatch.Text.Trim();
            var to = TxtTo.Text.Trim();

            if (string.IsNullOrEmpty(match) || string.IsNullOrEmpty(to))
            {
                MessageBox.Show("Match and To are required", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // load current rules
            var rules = new List<Rule>();
            if (File.Exists(_rulesFile))
            {
                var j = File.ReadAllText(_rulesFile);
                rules = JsonSerializer.Deserialize<List<Rule>>(j,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }

            // if a rule selected, update it otherwise add new
            if (RulesList.SelectedItem is Rule sel)
            {
                var idx = rules.FindIndex(rr => rr.Match == sel.Match && rr.To == sel.To);
                if (idx >= 0)
                {
                    rules[idx].Match = match;
                    rules[idx].To = to;
                    rules[idx].ApiKey = TxtApiKey.Text.Trim();
                    rules[idx].Login = string.IsNullOrWhiteSpace(TxtLoginUrl.Text)
                        ? null
                        : new LoginConfig { LoginUrl = TxtLoginUrl.Text.Trim() };
                }
            }
            else
            {
                rules.Add(new Rule
                {
                    Match = match,
                    To = to,
                    ApiKey = TxtApiKey.Text.Trim(),
                    Login = string.IsNullOrWhiteSpace(TxtLoginUrl.Text)
                        ? null
                        : new LoginConfig { LoginUrl = TxtLoginUrl.Text.Trim() }
                });
            }

            File.WriteAllText(_rulesFile, JsonSerializer.Serialize(rules,
                new JsonSerializerOptions { WriteIndented = true }));

            RefreshRulesList();
            MessageBox.Show("Saved rules.json (proxy will auto-reload)", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnPerformLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!(RulesList.SelectedItem is Rule sel))
            {
                MessageBox.Show("Select a rule first", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rules = _engine.Rules;
            var idx = Array.IndexOf(rules.ToArray(), sel);
            if (idx < 0)
            {
                MessageBox.Show("Could not find rule index", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnPerformLogin.IsEnabled = false;
            BtnPerformLogin.Content = "Logging in...";

            var res = await _engine.PerformLoginForRuleAsync(idx);

            BtnPerformLogin.IsEnabled = true;
            BtnPerformLogin.Content = "Perform Login";

            if (res.ok)
            {
                MessageBox.Show($"Login succeeded (HTTP {res.status})", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Login failed: {res.error}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine?.Dispose();
            base.OnClosed(e);
        }
    }
}
