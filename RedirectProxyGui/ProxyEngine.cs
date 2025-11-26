// ProxyEngine.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using RedirectProxyGui.Models;

namespace RedirectProxyGui
{
    public class InterceptLog
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string OriginalUrl { get; set; } = string.Empty;
        public string RedirectUrl { get; set; } = string.Empty;
        public List<KeyValuePair<string, string>> IncomingHeaders { get; set; } = new();
        public List<KeyValuePair<string, string>> OutgoingHeaders { get; set; } = new();
        public int RuleIndex { get; set; } = -1;
    }

    public class ProxyEngine : IDisposable
    {
        private readonly ProxyServer _proxy;
        private ExplicitProxyEndPoint? _endpoint;
        private readonly string _rulesFile;
        private List<Rule> _rules = new();
        private readonly Dictionary<int, CookieContainer> _cookieContainers = new();
        private readonly Dictionary<int, (string token, DateTime when)> _tokens = new();

        public event Action<InterceptLog>? OnIntercept;
        public event Action? RulesReloaded;

        public ProxyEngine(string rulesFile)
        {
            _rulesFile = rulesFile;
            _proxy = new ProxyServer();
            _proxy.BeforeRequest += OnRequest;
            _proxy.BeforeResponse += OnResponse;

            // Create and trust root certificate for HTTPS interception
            _proxy.CertificateManager.EnsureRootCertificate();
            _proxy.CertificateManager.TrustRootCertificate(true);

            // Accept all server certificates
            _proxy.ServerCertificateValidationCallback += (sender, e) =>
            {
                // Return true to accept all certificates
                return Task.FromResult(true);
            };

            LoadRules();
            WatchRulesFile();
        }

        public void Start(int port = 8000)
        {
            _endpoint = new ExplicitProxyEndPoint(IPAddress.Any, port, true);
            _proxy.AddEndPoint(_endpoint);
            _proxy.Start();
        }

        public void Stop()
        {
            _proxy.Stop();
            _proxy.BeforeRequest -= OnRequest;
            _proxy.BeforeResponse -= OnResponse;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        private async Task OnRequest(object sender, SessionEventArgs e)
        {
            try
            {
                var url = e.HttpClient.Request.Url;
                var idx = FindMatchingRuleIndex(url);
                if (idx == -1) return;

                var rule = _rules[idx];
                if (!rule.Enabled) return;

                // copy incoming headers
                var incoming = e.HttpClient.Request.Headers
                    .ToList()
                    .Select(h => new KeyValuePair<string, string>(h.Name, h.Value))
                    .ToList();

                // merge outgoing headers (start from incoming)
                var outgoing = new List<KeyValuePair<string, string>>(incoming);

                // token
                if (_tokens.ContainsKey(idx) && !string.IsNullOrEmpty(_tokens[idx].token))
                    Overwrite(outgoing, "Authorization", $"Bearer {_tokens[idx].token}");

                if (!string.IsNullOrEmpty(rule.ApiKey))
                    Overwrite(outgoing, "x-api-key", rule.ApiKey);

                if (rule.Headers != null)
                    foreach (var h in rule.Headers)
                        if (!string.IsNullOrEmpty(h?.Name))
                            Overwrite(outgoing, h.Name, h.Value ?? "");

                // build target url
                var remainder = url.Substring(rule.Match.Length);
                var target = rule.To + remainder;

                // prepare httpclient with cookie container
                if (!_cookieContainers.ContainsKey(idx))
                    _cookieContainers[idx] = new CookieContainer();

                var handler = new HttpClientHandler
                {
                    CookieContainer = _cookieContainers[idx],
                    UseCookies = true,
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                using var client = new HttpClient(handler);

                var method = new HttpMethod(e.HttpClient.Request.Method);
                var req = new HttpRequestMessage(method, target);

                if (e.HttpClient.Request.HasBody)
                {
                    var body = await e.GetRequestBody();
                    req.Content = new ByteArrayContent(body);

                    // Get Content-Type header
                    var ctHeader = e.HttpClient.Request.Headers.GetHeaders("Content-Type").FirstOrDefault();
                    if (ctHeader != null && !string.IsNullOrEmpty(ctHeader.Value))
                        req.Content.Headers.TryAddWithoutValidation("Content-Type", ctHeader.Value);
                }

                // set headers
                foreach (var kv in outgoing)
                {
                    if (string.Equals(kv.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.Equals(kv.Key, "Host", StringComparison.OrdinalIgnoreCase))
                    {
                        req.Headers.Host = new Uri(target).Host;
                        continue;
                    }

                    if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        req.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                // log before sending
                var log = new InterceptLog
                {
                    OriginalUrl = url,
                    RedirectUrl = target,
                    IncomingHeaders = incoming,
                    OutgoingHeaders = outgoing,
                    RuleIndex = idx
                };

                OnIntercept?.Invoke(log);

                // send to target
                using var resp = await client.SendAsync(req);

                // copy response status and body back to client
                var respBytes = await resp.Content.ReadAsByteArrayAsync();

                e.HttpClient.Response.StatusCode = (int)resp.StatusCode;
                e.HttpClient.Response.StatusDescription = resp.ReasonPhrase ?? string.Empty;

                // Copy response headers
                e.HttpClient.Response.Headers.Clear();

                foreach (var h in resp.Headers)
                    foreach (var v in h.Value)
                        e.HttpClient.Response.Headers.AddHeader(h.Key, v);

                foreach (var h in resp.Content.Headers)
                    foreach (var v in h.Value)
                        e.HttpClient.Response.Headers.AddHeader(h.Key, v);

                // Set response body
                e.SetResponseBody(respBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnRequest error: " + ex);
            }
        }

        private Task OnResponse(object s, SessionEventArgs e) => Task.CompletedTask;

        private void Overwrite(List<KeyValuePair<string, string>> list, string name, string value)
        {
            var i = list.FindIndex(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                list[i] = new KeyValuePair<string, string>(name, value);
            else
                list.Add(new KeyValuePair<string, string>(name, value));
        }

        private int FindMatchingRuleIndex(string url)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i];
                if (!r.Enabled) continue;
                if (!string.IsNullOrWhiteSpace(r.Match) &&
                    url.StartsWith(r.Match, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public async Task<(bool ok, int status, string? body, string? error)> PerformLoginForRuleAsync(int index)
        {
            if (index < 0 || index >= _rules.Count)
                return (false, 0, null, "invalid index");

            var rule = _rules[index];
            if (rule.Login == null || string.IsNullOrEmpty(rule.Login.LoginUrl))
                return (false, 0, null, "no login configured");

            if (!_cookieContainers.ContainsKey(index))
                _cookieContainers[index] = new CookieContainer();

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainers[index],
                UseCookies = true,
                AllowAutoRedirect = true
            };

            using var client = new HttpClient(handler);
            var login = rule.Login;
            var method = (login.Method ?? "POST").ToUpperInvariant();
            var req = new HttpRequestMessage(new HttpMethod(method), login.LoginUrl);

            if (login.Headers != null)
                foreach (var h in login.Headers)
                    if (!string.IsNullOrEmpty(h?.Name))
                        req.Headers.TryAddWithoutValidation(h.Name, h.Value ?? "");

            if (method != "GET" && !string.IsNullOrEmpty(login.Body))
                req.Content = new StringContent(login.Body, Encoding.UTF8, login.ContentType ?? "application/json");

            try
            {
                var resp = await client.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                // try parse JSON token
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("token", out var t) ||
                        doc.RootElement.TryGetProperty("access_token", out t))
                    {
                        var tokenValue = t.GetString();
                        if (tokenValue != null)
                            _tokens[index] = (tokenValue, DateTime.UtcNow);
                    }

                    if (doc.RootElement.TryGetProperty("cookies", out var cookies) &&
                        cookies.ValueKind == JsonValueKind.Object)
                    {
                        var uri = new Uri(login.LoginUrl);
                        foreach (var p in cookies.EnumerateObject())
                        {
                            var cookieValue = p.Value.GetString();
                            if (cookieValue != null)
                                _cookieContainers[index].Add(
                                    new Uri($"{uri.Scheme}://{uri.Host}"),
                                    new Cookie(p.Name, cookieValue));
                        }
                    }
                }
                catch { }

                // add cookiePairs
                if (login.CookiePairs != null)
                {
                    var uri = new Uri(login.LoginUrl);
                    foreach (var cp in login.CookiePairs)
                    {
                        _cookieContainers[index].Add(
                            new Uri($"{uri.Scheme}://{cp.Domain ?? uri.Host}"),
                            new Cookie(cp.Name, cp.Value ?? ""));
                    }
                }

                return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body, null);
            }
            catch (Exception ex)
            {
                return (false, 0, null, ex.ToString());
            }
        }

        private void LoadRules()
        {
            try
            {
                if (!File.Exists(_rulesFile))
                {
                    _rules = new();
                    return;
                }

                var json = File.ReadAllText(_rulesFile);
                _rules = JsonSerializer.Deserialize<List<Rule>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                RulesReloaded?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine("LoadRules error: " + ex);
                _rules = new();
            }
        }

        private void WatchRulesFile()
        {
            try
            {
                var fi = new FileInfo(_rulesFile);
                if (fi.DirectoryName == null) return;

                var w = new FileSystemWatcher(fi.DirectoryName, fi.Name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                w.Changed += (s, e) =>
                {
                    Thread.Sleep(200);
                    LoadRules();
                };

                w.EnableRaisingEvents = true;
            }
            catch { }
        }

        // expose rules for UI
        public IReadOnlyList<Rule> Rules => _rules;
    }
}
