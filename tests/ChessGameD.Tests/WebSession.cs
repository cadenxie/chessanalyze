using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChessGameD.Tests;

/// <summary>
/// 测试会话:自己管 Cookie,不同会话之间互不影响。
///
/// 这样才能在同一个应用实例里模拟多个用户,验证「A 看不到 B 的棋局」。
/// 写操作会自动带上页面里的防伪 token(和真实前端一个走法)。
/// </summary>
public sealed class WebSession : IDisposable
{
    private static readonly Regex CsrfPattern = new(
        "<meta name=\"csrf-token\" content=\"(?<token>[^\"]+)\"",
        RegexOptions.Compiled);

    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);

    public WebSession(HttpClient client)
    {
        _client = client;
    }

    /// <summary>开一个新会话(不自动处理 Cookie、不自动跟随重定向)。</summary>
    public static WebSession Create(AppFactory factory)
        => new(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        }));

    public Task<HttpResponseMessage> GetAsync(string url)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public Task<HttpResponseMessage> DeleteAsync(string url)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));

    public Task<HttpResponseMessage> PostFormAsync(string url, IDictionary<string, string> fields)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields),
        });

    /// <summary>以 multipart 表单提交一段文本(模拟上传 PGN 文本)。</summary>
    public Task<HttpResponseMessage> PostTextFieldAsync(string url, string fieldName, string content)
    {
        var multi = new MultipartFormDataContent();
        multi.Add(new StringContent(content, Encoding.UTF8), fieldName);
        return SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = multi });
    }

    public Task<HttpResponseMessage> PostJsonAsync(string url, object payload)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        });

    /// <summary>当前会话带的 Cookie 名字(调试用)。</summary>
    public IReadOnlyCollection<string> CookieNames => _cookies.Keys;

    /// <summary>丢掉全部 Cookie(模拟「换个浏览器」)。</summary>
    public void ClearCookies() => _cookies.Clear();

    /// <summary>当前会话某个 Cookie 的值。</summary>
    public string? GetCookie(string name) => _cookies.TryGetValue(name, out var v) ? v : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        // 先取防伪 token:它跟防伪 Cookie 是配套的,
        // 取 token 的那个响应可能会下发新的防伪 Cookie,所以必须在挂 Cookie 之前取。
        if (!IsSafeMethod(request.Method))
        {
            var token = await GetCsrfTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("RequestVerificationToken", token);
            }
        }

        ApplyCookies(request);

        var response = await _client.SendAsync(request);
        CaptureCookies(response);
        return response;
    }

    /// <summary>取页面里的防伪 token(顺便把 antiforgery cookie 收进来)。</summary>
    private async Task<string?> GetCsrfTokenAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/Home/Index");
        ApplyCookies(request);

        var response = await _client.SendAsync(request);
        CaptureCookies(response);

        var html = await response.Content.ReadAsStringAsync();
        var match = CsrfPattern.Match(html);
        if (match.Success)
        {
            return match.Groups["token"].Value;
        }

        // 页面被重定向了(比如强制改密),跟着跳一次再找
        if (response.StatusCode == HttpStatusCode.Found
            && response.Headers.Location is { } location)
        {
            var redirect = new HttpRequestMessage(HttpMethod.Get, location.ToString());
            ApplyCookies(redirect);
            var redirected = await _client.SendAsync(redirect);
            CaptureCookies(redirected);
            var redirectedHtml = await redirected.Content.ReadAsStringAsync();
            var redirectMatch = CsrfPattern.Match(redirectedHtml);
            if (redirectMatch.Success)
            {
                return redirectMatch.Groups["token"].Value;
            }
        }

        return null;
    }

    private void ApplyCookies(HttpRequestMessage request)
    {
        if (_cookies.Count == 0)
        {
            return;
        }

        var header = string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        request.Headers.Add("Cookie", header);
    }

    private void CaptureCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var raw in values)
        {
            var pair = raw.Split(';')[0];
            var index = pair.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var name = pair[..index].Trim();
            var value = pair[(index + 1)..].Trim();
            if (string.IsNullOrEmpty(value))
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = value;
            }
        }
    }

    private static bool IsSafeMethod(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    public void Dispose() => _client.Dispose();
}
