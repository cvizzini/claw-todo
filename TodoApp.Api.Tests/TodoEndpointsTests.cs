using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace TodoApp.Api.Tests;

public class TodoEndpointsTests : TestBase
{
    public TodoEndpointsTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<string> AuthAsync()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        return token;
    }

    // Helper: extract items array from paged response
    private static JsonElement[] GetItems(JsonElement paged) =>
        paged.GetProperty("items").EnumerateArray().ToArray();

    // ── Unauthenticated access ────────────────────────────────────────────────

    [Fact]
    public async Task Todos_WithoutAuth_ReturnsUnauthorized()
    {
        ClearAuthHeader();
        var resp = await Client.GetAsync("/api/v1/todos");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateTodo_WithoutAuth_ReturnsUnauthorized()
    {
        ClearAuthHeader();
        var resp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Test", priority = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/todos ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodos_ForNewUser_ReturnsEmptyPagedResult()
    {
        await AuthAsync();
        var resp = await Client.GetAsync("/api/v1/todos");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        GetItems(body).Should().BeEmpty();
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
        body.GetProperty("totalPages").GetInt32().Should().Be(0);
    }

    // ── POST /api/v1/todos ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTodo_WithValidData_ReturnsCreated()
    {
        await AuthAsync();
        var resp = await Client.PostAsJsonAsync("/api/v1/todos", new
        {
            title    = "Buy milk",
            notes    = "Full fat",
            priority = 1,
            category = "Shopping",
            dueDate  = (DateTime?)null
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location.Should().NotBeNull();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Buy milk");
        body.GetProperty("notes").GetString().Should().Be("Full fat");
        body.GetProperty("category").GetString().Should().Be("Shopping");
        body.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
        body.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateTodo_WithEmptyTitle_ReturnsBadRequest()
    {
        await AuthAsync();
        var resp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "", priority = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTodo_WithWhitespaceTitle_ReturnsBadRequest()
    {
        await AuthAsync();
        var resp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "   ", priority = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/v1/todos/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task GetTodoById_WithValidId_ReturnsTodo()
    {
        var token = await RegisterAndLoginAsync(); // unique user per call
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Find me unique", priority = 0 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await Client.GetAsync($"/api/v1/todos/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(id);
        body.GetProperty("title").GetString().Should().Be("Find me unique");
    }

    [Fact]
    public async Task GetTodoById_WithInvalidId_ReturnsNotFound()
    {
        await AuthAsync();
        var resp = await Client.GetAsync("/api/v1/todos/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTodoById_BelongingToOtherUser_ReturnsNotFound()
    {
        var tokenA = await RegisterAndLoginAsync();
        SetAuthHeader(tokenA);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Secret", priority = 1 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var tokenB = await RegisterAndLoginAsync();
        SetAuthHeader(tokenB);
        var resp = await Client.GetAsync($"/api/v1/todos/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/v1/todos/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTodo_WithValidData_ReturnsUpdated()
    {
        await AuthAsync();
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Original", priority = 0 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await Client.PutAsJsonAsync($"/api/v1/todos/{id}", new
        {
            title       = "Updated Title",
            notes       = "New notes",
            isCompleted = true,
            priority    = 2,
            category    = "Work",
            dueDate     = (DateTime?)null
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Updated Title");
        body.GetProperty("isCompleted").GetBoolean().Should().BeTrue();
        body.GetProperty("priority").GetString().Should().Be("High");
    }

    [Fact]
    public async Task UpdateTodo_WithEmptyTitle_ReturnsBadRequest()
    {
        await AuthAsync();
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "To Update", priority = 0 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await Client.PutAsJsonAsync($"/api/v1/todos/{id}", new
        {
            title = "", isCompleted = false, priority = 0
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTodo_WithInvalidId_ReturnsNotFound()
    {
        await AuthAsync();
        var resp = await Client.PutAsJsonAsync("/api/v1/todos/999999", new
        {
            title = "Does not exist", isCompleted = false, priority = 0
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PATCH /api/v1/todos/{id}/toggle ──────────────────────────────────────

    [Fact]
    public async Task ToggleTodo_TogglesCompletionState()
    {
        await AuthAsync();
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Toggle me", priority = 1 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = created.GetProperty("id").GetInt32();
        var initial = created.GetProperty("isCompleted").GetBoolean();

        var resp = await Client.PatchAsync($"/api/v1/todos/{id}/toggle", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isCompleted").GetBoolean().Should().Be(!initial);

        var resp2 = await Client.PatchAsync($"/api/v1/todos/{id}/toggle", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        body2.GetProperty("isCompleted").GetBoolean().Should().Be(initial);
    }

    [Fact]
    public async Task ToggleTodo_WithInvalidId_ReturnsNotFound()
    {
        await AuthAsync();
        var resp = await Client.PatchAsync("/api/v1/todos/999999/toggle", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/v1/todos/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteTodo_WithValidId_ReturnsNoContent()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Delete me isolated", priority = 1 });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var resp = await Client.DeleteAsync($"/api/v1/todos/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await Client.GetAsync($"/api/v1/todos/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTodo_WithInvalidId_ReturnsNotFound()
    {
        await AuthAsync();
        var resp = await Client.DeleteAsync("/api/v1/todos/999999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/v1/todos/categories ─────────────────────────────────────────

    [Fact]
    public async Task GetCategories_ReturnsDistinctCategories()
    {
        await AuthAsync();
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "A", priority = 0, category = "CatWork" });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "B", priority = 0, category = "CatWork" });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "C", priority = 0, category = "CatPersonal" });

        var resp = await Client.GetAsync("/api/v1/todos/categories");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cats = await resp.Content.ReadFromJsonAsync<string[]>();
        cats.Should().NotBeNull();
        cats!.Should().Contain("CatWork");
        cats.Should().Contain("CatPersonal");
        cats.Should().OnlyHaveUniqueItems();
    }

    // ── GET /api/v1/todos/stats ───────────────────────────────────────────────

    [Fact]
    public async Task GetStats_ReturnsCorrectCounts()
    {
        await AuthAsync();
        var r1 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Stat1", priority = 0 });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Stat2", priority = 0 });
        var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await Client.PatchAsync($"/api/v1/todos/{id1}/toggle", null);

        var resp = await Client.GetAsync("/api/v1/todos/stats");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("completed").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("active").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodos_FilterByCompleted_ReturnsMatchingTodos()
    {
        await AuthAsync();
        var r1 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Completed Task Filter", priority = 0 });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Active Task Filter", priority = 0 });
        var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await Client.PatchAsync($"/api/v1/todos/{id1}/toggle", null);

        var resp = await Client.GetAsync("/api/v1/todos?completed=true");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = GetItems(body);
        items.Should().NotBeEmpty();
        items.All(t => t.GetProperty("isCompleted").GetBoolean()).Should().BeTrue();
    }

    [Fact]
    public async Task GetTodos_FilterByCategory_ReturnsMatchingTodos()
    {
        await AuthAsync();
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Cat Work Task", priority = 0, category = "FilterWork2" });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Cat Home Task", priority = 0, category = "FilterHome2" });

        var resp = await Client.GetAsync("/api/v1/todos?category=FilterWork2");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = GetItems(body);
        items.Should().NotBeEmpty();
        items.All(t => t.GetProperty("category").GetString() == "FilterWork2").Should().BeTrue();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodos_WithSearch_ReturnsMatchingTodos()
    {
        await AuthAsync();
        var uid = Guid.NewGuid().ToString("N")[..8];
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = $"Unique searchterm {uid}", priority = 0 });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Something completely different", priority = 0 });

        var resp = await Client.GetAsync($"/api/v1/todos?search={uid}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = GetItems(body);
        items.Should().HaveCount(1);
        items[0].GetProperty("title").GetString().Should().Contain(uid);
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodos_Pagination_ReturnsCorrectPage()
    {
        // Use isolated user so count is predictable
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);

        for (int i = 1; i <= 5; i++)
            await Client.PostAsJsonAsync("/api/v1/todos", new { title = $"Paged todo {i}", priority = 0 });

        var resp = await Client.GetAsync("/api/v1/todos?page=1&pageSize=2");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        GetItems(body).Should().HaveCount(2);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("totalCount").GetInt32().Should().Be(5);
        body.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        body.GetProperty("hasPreviousPage").GetBoolean().Should().BeFalse();
    }

    // ── DELETE /api/v1/todos/completed ────────────────────────────────────────

    [Fact]
    public async Task ClearCompleted_DeletesOnlyCompletedTodos()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);

        var r1 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Done CC1 isolated", priority = 0 });
        var r2 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Done CC2 isolated", priority = 0 });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Still Active CC isolated", priority = 0 });

        var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var id2 = (await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await Client.PatchAsync($"/api/v1/todos/{id1}/toggle", null);
        await Client.PatchAsync($"/api/v1/todos/{id2}/toggle", null);

        var resp = await Client.DeleteAsync("/api/v1/todos/completed");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("deleted").GetInt32().Should().Be(2);

        var getResp = await Client.GetAsync("/api/v1/todos?completed=false&pageSize=100");
        var remaining = GetItems(await getResp.Content.ReadFromJsonAsync<JsonElement>());
        remaining.Any(t => t.GetProperty("title").GetString() == "Still Active CC isolated").Should().BeTrue();
        remaining.Any(t => t.GetProperty("title").GetString() == "Done CC1 isolated").Should().BeFalse();
    }

    // ── Soft Delete / Trash ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTodo_IsSoftDelete_GetReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Soft delete me", priority = 0 });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Delete (soft)
        var del = await Client.DeleteAsync($"/api/v1/todos/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Normal GET should return 404 (global filter excludes it)
        var get = await Client.GetAsync($"/api/v1/todos/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTrash_ContainsSoftDeletedTodo()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Trash item", priority = 0 });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await Client.DeleteAsync($"/api/v1/todos/{id}");

        var trash = await Client.GetAsync("/api/v1/todos/trash");
        trash.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await trash.Content.ReadFromJsonAsync<JsonElement[]>();
        items.Should().NotBeNull();
        items!.Any(t => t.GetProperty("id").GetInt32() == id).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreTodo_MakesItAccessibleAgain()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Restore me", priority = 0 });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await Client.DeleteAsync($"/api/v1/todos/{id}");

        var restore = await Client.PostAsync($"/api/v1/todos/{id}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await Client.GetAsync($"/api/v1/todos/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PermanentDelete_HardDeletesFromTrash()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);
        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Permanent delete me", priority = 0 });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await Client.DeleteAsync($"/api/v1/todos/{id}");

        var perm = await Client.DeleteAsync($"/api/v1/todos/{id}/permanent");
        perm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Should no longer be in trash
        var trash = await Client.GetAsync("/api/v1/todos/trash");
        var items = await trash.Content.ReadFromJsonAsync<JsonElement[]>();
        items!.Any(t => t.GetProperty("id").GetInt32() == id).Should().BeFalse();

        // Restore should 404
        var restore = await Client.PostAsync($"/api/v1/todos/{id}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmptyTrash_RemovesAllTrashedItems()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);

        var r1 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Trash 1", priority = 0 });
        var r2 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Trash 2", priority = 0 });
        var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var id2 = (await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await Client.DeleteAsync($"/api/v1/todos/{id1}");
        await Client.DeleteAsync($"/api/v1/todos/{id2}");

        var empty = await Client.DeleteAsync("/api/v1/todos/trash");
        empty.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await empty.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("deleted").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var trash = await Client.GetAsync("/api/v1/todos/trash");
        var remaining = await trash.Content.ReadFromJsonAsync<JsonElement[]>();
        remaining!.Any(t => t.GetProperty("id").GetInt32() == id1 || t.GetProperty("id").GetInt32() == id2).Should().BeFalse();
    }

    [Fact]
    public async Task Stats_DoesNotCountSoftDeletedTodosInTotal()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);

        var r1 = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Will be deleted stats", priority = 0 });
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Still active stats", priority = 0 });
        var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Get initial stats
        var statsBefore = await (await Client.GetAsync("/api/v1/todos/stats")).Content.ReadFromJsonAsync<JsonElement>();
        var totalBefore = statsBefore.GetProperty("total").GetInt32();

        await Client.DeleteAsync($"/api/v1/todos/{id1}");

        var statsAfter = await (await Client.GetAsync("/api/v1/todos/stats")).Content.ReadFromJsonAsync<JsonElement>();
        statsAfter.GetProperty("total").GetInt32().Should().Be(totalBefore - 1);
        statsAfter.GetProperty("trashCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_RecordsCreateAndToggle()
    {
        var token = await RegisterAndLoginAsync();
        SetAuthHeader(token);

        var createResp = await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Audit me", priority = 0 });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await Client.PatchAsync($"/api/v1/todos/{id}/toggle", null);

        var auditResp = await Client.GetAsync($"/api/v1/todos/{id}/audit");
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await auditResp.Content.ReadFromJsonAsync<JsonElement[]>();
        logs.Should().NotBeNullOrEmpty();
        logs!.Any(l => l.GetProperty("action").GetString() == "Created").Should().BeTrue();
        logs!.Any(l => l.GetProperty("action").GetString() == "Completed").Should().BeTrue();
    }
}
