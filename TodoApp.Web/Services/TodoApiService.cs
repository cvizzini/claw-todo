using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using TodoApp.Web.Models;

namespace TodoApp.Web.Services;

public class TodoApiService
{
    private readonly HttpClient _http;
    private readonly AuthService _auth;
    private readonly JsonSerializerOptions _json;

    public TodoApiService(HttpClient http, AuthService auth)
    {
        _http = http;
        _auth = auth;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private void SetAuthHeader()
    {
        if (_auth.CurrentUser is { Token: var token })
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<PagedResult<TodoItem>> GetTodosAsync(TodoQuery query)
    {
        SetAuthHeader();

        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (query.Completed.HasValue) qs["completed"] = query.Completed.Value.ToString().ToLower();
        if (!string.IsNullOrEmpty(query.Category))  qs["category"] = query.Category;
        if (query.Priority.HasValue)                 qs["priority"] = ((int)query.Priority.Value).ToString();
        if (!string.IsNullOrEmpty(query.Search))     qs["search"]   = query.Search;
        if (!string.IsNullOrEmpty(query.SortBy))     qs["sortBy"]   = query.SortBy;
        if (query.SortDesc)                          qs["sortDesc"]  = "true";
        qs["page"]     = query.Page.ToString();
        qs["pageSize"] = query.PageSize.ToString();

        var url = "/api/v1/todos?" + qs;
        return await _http.GetFromJsonAsync<PagedResult<TodoItem>>(url, _json) ?? new();
    }

    // Convenience overload for simple filtering (backward compat)
    public async Task<List<TodoItem>> GetTodosAsync(bool? completed = null, string? category = null)
    {
        var result = await GetTodosAsync(new TodoQuery
        {
            Completed = completed,
            Category  = category,
            PageSize  = 100
        });
        return result.Items;
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        SetAuthHeader();
        return await _http.GetFromJsonAsync<List<string>>("/api/v1/todos/categories", _json) ?? [];
    }

    public async Task<TodoStats?> GetStatsAsync()
    {
        SetAuthHeader();
        return await _http.GetFromJsonAsync<TodoStats>("/api/v1/todos/stats", _json);
    }

    public async Task<TodoItem?> CreateTodoAsync(CreateTodoRequest req)
    {
        SetAuthHeader();
        var res = await _http.PostAsJsonAsync("/api/v1/todos", req, _json);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<TodoItem>(_json);
    }

    public async Task<TodoItem?> UpdateTodoAsync(int id, UpdateTodoRequest req)
    {
        SetAuthHeader();
        var res = await _http.PutAsJsonAsync($"/api/v1/todos/{id}", req, _json);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<TodoItem>(_json);
    }

    public async Task<TodoItem?> ToggleTodoAsync(int id)
    {
        SetAuthHeader();
        var res = await _http.PatchAsync($"/api/v1/todos/{id}/toggle", null);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<TodoItem>(_json);
    }

    public async Task DeleteTodoAsync(int id)
    {
        SetAuthHeader();
        (await _http.DeleteAsync($"/api/v1/todos/{id}")).EnsureSuccessStatusCode();
    }

    public async Task<int> ClearCompletedAsync()
    {
        SetAuthHeader();
        var res = await _http.DeleteAsync("/api/v1/todos/completed");
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("deleted").GetInt32();
    }

    // ── Trash ─────────────────────────────────────────────────────────────────

    public async Task<List<TodoItem>> GetTrashAsync()
    {
        SetAuthHeader();
        return await _http.GetFromJsonAsync<List<TodoItem>>("/api/v1/todos/trash", _json) ?? [];
    }

    public async Task<TodoItem?> RestoreTodoAsync(int id)
    {
        SetAuthHeader();
        var res = await _http.PostAsync($"/api/v1/todos/{id}/restore", null);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<TodoItem>(_json);
    }

    public async Task PermanentDeleteAsync(int id)
    {
        SetAuthHeader();
        (await _http.DeleteAsync($"/api/v1/todos/{id}/permanent")).EnsureSuccessStatusCode();
    }

    public async Task<int> EmptyTrashAsync()
    {
        SetAuthHeader();
        var res = await _http.DeleteAsync("/api/v1/todos/trash");
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("deleted").GetInt32();
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    public async Task<List<AuditLogEntry>> GetAuditLogAsync(int todoId)
    {
        SetAuthHeader();
        return await _http.GetFromJsonAsync<List<AuditLogEntry>>($"/api/v1/todos/{todoId}/audit", _json) ?? [];
    }
}
