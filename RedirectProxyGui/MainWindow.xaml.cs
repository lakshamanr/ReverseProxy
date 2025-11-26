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
            BtnAddRule.Click += BtnAddRule_Click;
            BtnEditRule.Click += BtnEditRule_Click;
            BtnDeleteRule.Click += BtnDeleteRule_Click;
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

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            RefreshRulesList();
            MessageBox.Show("Rules reloaded from rules.json", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RefreshRulesList()
        {
            RulesList.Items.Clear();
            foreach (var r in _engine.Rules)
                RulesList.Items.Add(r);
        }

        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            var editor = new RuleEditorWindow();
            editor.Owner = this;

            if (editor.ShowDialog() == true && editor.Saved)
            {
                var rules = LoadRulesFromFile();
                rules.Add(editor.EditedRule);
                SaveRulesToFile(rules);
                RefreshRulesList();

                MessageBox.Show("Rule added successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnEditRule_Click(object sender, RoutedEventArgs e)
        {
            if (!(RulesList.SelectedItem is Rule selectedRule))
            {
                MessageBox.Show("Please select a rule to edit", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editor = new RuleEditorWindow(selectedRule);
            editor.Owner = this;

            if (editor.ShowDialog() == true && editor.Saved)
            {
                var rules = LoadRulesFromFile();
                var index = FindRuleIndex(rules, selectedRule);

                if (index >= 0)
                {
                    rules[index] = editor.EditedRule;
                    SaveRulesToFile(rules);
                    RefreshRulesList();

                    MessageBox.Show("Rule updated successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Could not find rule to update", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (!(RulesList.SelectedItem is Rule selectedRule))
            {
                MessageBox.Show("Please select a rule to delete", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete this rule?\n\nMatch: {selectedRule.Match}\nTo: {selectedRule.To}",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var rules = LoadRulesFromFile();
                var index = FindRuleIndex(rules, selectedRule);

                if (index >= 0)
                {
                    rules.RemoveAt(index);
                    SaveRulesToFile(rules);
                    RefreshRulesList();

                    MessageBox.Show("Rule deleted successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Could not find rule to delete", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
            BtnPerformLogin.Content = "Perform Login for Selected";

            if (res.ok)
            {
                MessageBox.Show($"Login succeeded (HTTP {res.status})\n\nResponse:\n{res.body?.Substring(0, Math.Min(200, res.body.Length ?? 0))}",
                    "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Login failed: {res.error}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Rule> LoadRulesFromFile()
        {
            if (!File.Exists(_rulesFile))
                return new List<Rule>();

            try
            {
                var json = File.ReadAllText(_rulesFile);
                return JsonSerializer.Deserialize<List<Rule>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Rule>();
            }
            catch
            {
                return new List<Rule>();
            }
        }

        private void SaveRulesToFile(List<Rule> rules)
        {
            var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_rulesFile, json);
        }

        private int FindRuleIndex(List<Rule> rules, Rule rule)
        {
            // Find by matching Match and To fields
            return rules.FindIndex(r => r.Match == rule.Match && r.To == rule.To);
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine?.Dispose();
            base.OnClosed(e);
        }
    }
}
