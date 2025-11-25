// Models/Rule.cs
using System.Collections.Generic;

namespace RedirectProxyGui.Models
{
    public class SimpleHeader
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class CookiePair
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
    }

    public class LoginConfig
    {
        public string LoginUrl { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public string ContentType { get; set; } = "application/json";
        public string Body { get; set; } = string.Empty;
        public List<SimpleHeader> Headers { get; set; } = new();
        public List<CookiePair> CookiePairs { get; set; } = new();
    }

    public class Rule
    {
        public string Match { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public List<SimpleHeader> Headers { get; set; } = new();
        public LoginConfig? Login { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
