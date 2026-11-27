using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

const string UserName = "developer";
const string Password = "developer";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5088");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.Cookie.Name = "OnlineTestAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();

var app = builder.Build();
var store = new NoteStore(Path.Combine(app.Environment.ContentRootPath, "records.json"));

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Text("ok", "text/plain; charset=utf-8"));

app.MapGet("/login", (HttpContext http, IAntiforgery antiforgery) =>
{
    if (http.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/");

    return Html(LoginPage(antiforgery.GetAndStoreTokens(http), wrongPassword: false, username: ""));
});

app.MapPost("/login", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (!string.Equals(username, UserName, StringComparison.Ordinal) ||
        !string.Equals(password, Password, StringComparison.Ordinal))
    {
        return Html(LoginPage(antiforgery.GetAndStoreTokens(http), wrongPassword: true, username));
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, UserName)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapGet("/", (HttpContext http, IAntiforgery antiforgery) =>
{
    if (http.User.Identity?.IsAuthenticated != true)
        return Html(LoginPage(antiforgery.GetAndStoreTokens(http), wrongPassword: false, username: ""));

    var items = store.Read();
    var editId = http.Request.Query["edit"].ToString();
    var editing = items.FirstOrDefault(item => item.Id == editId);
    return Html(TablePage(
        antiforgery.GetAndStoreTokens(http),
        items,
        editing?.Name ?? "",
        editing?.Note ?? "",
        editing?.Id ?? "",
        nameError: null));
});

app.MapGet("/picture.png", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "picture.png");
    if (!File.Exists(path))
        path = Path.Combine(AppContext.BaseDirectory, "picture.png");

    return File.Exists(path)
        ? Results.File(path, "image/png")
        : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/save", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var id = form["id"].ToString();
    var name = Clip(form["name"].ToString().Trim(), 120);
    var note = Clip(form["note"].ToString().Trim(), 500);

    if (name.Length == 0)
    {
        return Html(TablePage(
            antiforgery.GetAndStoreTokens(http),
            store.Read(),
            name,
            note,
            id,
            "أدخل الاسم"));
    }

    store.Upsert(id, name, note);
    return Results.Redirect("/");
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/delete", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    store.Delete(form["id"].ToString());
    return Results.Redirect("/");
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization().DisableAntiforgery();

app.Run();

static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");

static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

static string Hidden(AntiforgeryTokenSet tokens) =>
    $"""<input type="hidden" name="{E(tokens.FormFieldName)}" value="{E(tokens.RequestToken)}">""";

static string LoginPage(AntiforgeryTokenSet tokens, bool wrongPassword, string username)
{
    var error = wrongPassword
        ? """<p class="error" role="alert">كلمة المرور غير صحيحة</p>"""
        : "";
    var passwordClass = wrongPassword ? "invalid" : "";

    return $"""
        <!DOCTYPE html>
        <html lang="ar" dir="rtl">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>تسجيل الدخول</title>
          {PageCss.Styles}
        </head>
        <body class="login-page">
          <main class="login-card">
            <p class="brand-title">الموقع شغال</p>
            <p class="brand-en">The site is online.</p>
            <h1>تسجيل الدخول</h1>
            <form class="transition" method="post" action="/login" accept-charset="utf-8">
              {Hidden(tokens)}
              <div class="field">
                <label for="username">اسم المستخدم</label>
                <input id="username" name="username" type="text" maxlength="64" autocomplete="username" value="{E(username)}">
              </div>
              <div class="field">
                <label for="password">كلمة المرور</label>
                <input id="password" class="{passwordClass}" name="password" type="password" maxlength="64" autocomplete="current-password">
                {error}
              </div>
              <button class="btn btn-primary btn-block" type="submit">دخول</button>
            </form>
          </main>
          {PageCss.Motion}
        </body>
        </html>
        """;
}

static string TablePage(
    AntiforgeryTokenSet tokens,
    IReadOnlyList<NoteItem> items,
    string name,
    string note,
    string editId,
    string? nameError)
{
    var token = Hidden(tokens);
    var rows = new StringBuilder();
    if (items.Count == 0)
    {
        rows.Append("""<tr><td colspan="3">لا توجد صفوف بعد</td></tr>""");
    }
    else
    {
        foreach (var item in items)
        {
            rows.Append($"""
                <tr>
                  <td>{E(item.Name)}</td>
                  <td>{E(item.Note)}</td>
                  <td class="actions">
                    <form method="get" action="/">
                      <input type="hidden" name="edit" value="{E(item.Id)}">
                      <button class="btn btn-quiet" type="submit">تعديل</button>
                    </form>
                    <form method="post" action="/delete">
                      {token}
                      <input type="hidden" name="id" value="{E(item.Id)}">
                      <button class="btn btn-danger" type="submit">حذف</button>
                    </form>
                  </td>
                </tr>
                """);
        }
    }

    var nameErrorHtml = string.IsNullOrEmpty(nameError)
        ? ""
        : $"""<p class="error">{E(nameError)}</p>""";

    return $"""
        <!DOCTYPE html>
        <html lang="ar" dir="rtl">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>السجلات</title>
          {PageCss.Styles}
        </head>
        <body class="app-page">
          <div class="app-shell">
            <header class="topbar">
              <div class="brand">
                <p class="brand-title">الموقع شغال</p>
                <p class="brand-en">The site is online.</p>
              </div>
              <form class="transition" method="post" action="/logout" accept-charset="utf-8">
                {token}
                <button class="btn btn-ghost" type="submit">تسجيل الخروج</button>
              </form>
            </header>
            <main class="content">
              <section class="card banner-card">
                <img class="banner" src="/picture.png" alt="">
              </section>
              <section class="card">
                <h1>السجلات</h1>
                <form method="post" action="/save" accept-charset="utf-8">
                  {token}
                  <input type="hidden" name="id" value="{E(editId)}">
                  <div class="field">
                    <label for="name">الاسم</label>
                    <input id="name" name="name" type="text" maxlength="120" value="{E(name)}">
                    {nameErrorHtml}
                  </div>
                  <div class="field">
                    <label for="note">الملاحظة</label>
                    <input id="note" name="note" type="text" maxlength="500" value="{E(note)}">
                  </div>
                  <button class="btn btn-primary" type="submit">حفظ</button>
                </form>
              </section>
              <section class="card">
                <div class="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>الاسم</th>
                        <th>الملاحظة</th>
                        <th>إجراءات</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows}
                    </tbody>
                  </table>
                </div>
              </section>
            </main>
          </div>
          {PageCss.Motion}
        </body>
        </html>
        """;
}

static class PageCss
{
    public const string Styles = """
    <style>
      * { box-sizing: border-box; }
      html { direction: rtl; }
      body {
        margin: 0;
        min-height: 100vh;
        color: #101828;
        background: #f2f4f7;
        font-family: "Segoe UI", Tahoma, sans-serif;
        font-size: 16px;
        line-height: 1.6;
        text-align: right;
      }
      .login-card, .app-shell { animation: enter 0.4s ease; transition: opacity 0.35s ease, transform 0.35s ease; }
      body.leaving .login-card, body.leaving .app-shell { opacity: 0; transform: translateY(8px); }
      @keyframes enter { from { opacity: 0; } to { opacity: 1; } }
      body.login-page {
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 24px 16px;
        background: #e8edf3;
      }
      .login-card {
        width: min(400px, 100%);
        background: #fff;
        border: 1px solid #e4e7ec;
        border-radius: 16px;
        padding: 32px 28px 28px;
        box-shadow: 0 16px 40px rgba(16, 24, 40, 0.08);
      }
      .brand-title { margin: 0; font-size: 15px; font-weight: 700; color: #175cd3; }
      .brand-en {
        margin: 2px 0 0;
        direction: ltr;
        unicode-bidi: isolate;
        text-align: right;
        font-size: 13px;
        color: #667085;
      }
      .login-card h1 { margin: 20px 0 18px; font-size: 28px; line-height: 1.3; font-weight: 700; }
      .topbar {
        display: flex;
        direction: rtl;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
        min-height: 64px;
        padding: 12px 24px;
        background: #fff;
        border-bottom: 1px solid #e4e7ec;
      }
      .brand { min-width: 0; }
      .topbar .brand-title { font-size: 18px; color: #101828; }
      .content {
        width: min(880px, calc(100% - 32px));
        margin: 24px auto 40px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .card {
        background: #fff;
        border: 1px solid #e4e7ec;
        border-radius: 12px;
        padding: 20px;
        box-shadow: 0 1px 2px rgba(16, 24, 40, 0.04);
      }
      .banner-card { padding: 8px; }
      .banner {
        display: block;
        width: 100%;
        height: 72px;
        object-fit: cover;
        border-radius: 8px;
      }
      h1 { margin: 0 0 16px; font-size: 20px; font-weight: 700; }
      .field { display: flex; flex-direction: column; align-items: stretch; gap: 6px; margin: 0 0 16px; }
      label { display: block; margin: 0; font-size: 14px; font-weight: 600; color: #344054; }
      input[type="text"], input[type="password"] {
        display: block;
        width: 100%;
        height: 44px;
        margin: 0;
        padding: 0 12px;
        font: inherit;
        color: #101828;
        background: #fff;
        border: 1px solid #d0d5dd;
        border-radius: 8px;
      }
      input:focus { outline: 2px solid #b2ddff; border-color: #84caff; }
      input.invalid { border-color: #f04438; background: #fffafa; }
      .btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        height: 40px;
        padding: 0 16px;
        border-radius: 8px;
        border: 1px solid transparent;
        font: inherit;
        font-weight: 600;
        line-height: 1;
        cursor: pointer;
        white-space: nowrap;
      }
      .btn-primary { background: #175cd3; color: #fff; }
      .btn-primary:hover { background: #1849a9; }
      .btn-block { width: 100%; height: 44px; }
      .btn-ghost { background: #fff; color: #344054; border-color: #d0d5dd; }
      .btn-quiet { background: #fff; color: #175cd3; border-color: #b2ddff; }
      .btn-danger { background: #fff; color: #b42318; border-color: #fecdca; }
      .error {
        margin: 0;
        padding: 8px 10px;
        color: #b42318;
        background: #fef3f2;
        border: 1px solid #fecdca;
        border-radius: 8px;
        font-weight: 600;
      }
      .table-wrap { overflow-x: auto; }
      table { width: 100%; border-collapse: collapse; }
      th, td { padding: 12px 10px; text-align: right; vertical-align: middle; border-bottom: 1px solid #eaecf0; }
      th { font-size: 13px; font-weight: 700; color: #667085; background: #f9fafb; }
      td.actions { white-space: nowrap; }
      td.actions form { display: inline-block; margin-inline-start: 8px; }
      .loading[hidden] { display: none !important; }
      .loading {
        position: fixed;
        inset: 0;
        z-index: 20;
        display: grid;
        place-items: center;
        background: rgba(242, 244, 247, 0.88);
      }
      .loading-card {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 14px 16px;
        background: #fff;
        border: 1px solid #e4e7ec;
        border-radius: 12px;
      }
      .loading-card p { margin: 0; }
      .spinner {
        width: 18px;
        height: 18px;
        border: 3px solid #e4e7ec;
        border-top-color: #175cd3;
        border-radius: 50%;
        animation: spin 0.7s linear infinite;
      }
      @keyframes spin { to { transform: rotate(360deg); } }
      @media (prefers-reduced-motion: reduce) {
        .login-card, .app-shell, .spinner { animation: none; transition: none; }
      }
    </style>
    """;

    public const string Motion = """
    <div class="loading" id="loading" hidden>
      <div class="loading-card">
        <span class="spinner" aria-hidden="true"></span>
        <p>لحظات...</p>
      </div>
    </div>
    <script>
      document.querySelectorAll("form.transition").forEach(function (form) {
        form.addEventListener("submit", function (event) {
          if (form.getAttribute("data-leaving") === "1") return;
          event.preventDefault();
          form.setAttribute("data-leaving", "1");
          var loading = document.getElementById("loading");
          if (loading) loading.hidden = false;
          document.body.classList.add("leaving");
          var delay = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 160 : 450;
          window.setTimeout(function () { form.submit(); }, delay);
        });
      });
    </script>
    """;
}

sealed class NoteItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
}

sealed class NoteStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public NoteStore(string path) => _path = path;

    public List<NoteItem> Read()
    {
        lock (_gate)
            return ReadUnlocked();
    }

    public void Upsert(string? id, string name, string note)
    {
        lock (_gate)
        {
            var items = ReadUnlocked();
            var existing = string.IsNullOrEmpty(id) ? null : items.FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                items.Add(new NoteItem
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Name = name,
                    Note = note
                });
            }
            else
            {
                existing.Name = name;
                existing.Note = note;
            }

            WriteUnlocked(items);
        }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var items = ReadUnlocked();
            items.RemoveAll(item => item.Id == id);
            WriteUnlocked(items);
        }
    }

    private List<NoteItem> ReadUnlocked()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
                return [];

            return JsonSerializer.Deserialize<List<NoteItem>>(text, _json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void WriteUnlocked(List<NoteItem> items)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, _json));
        File.Move(temp, _path, overwrite: true);
    }
}
