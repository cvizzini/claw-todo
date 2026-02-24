using NSubstitute;
using TodoApp.Application.DTOs;
using TodoApp.Application.Services;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Exceptions;
using TodoApp.Domain.Interfaces;
using FluentAssertions;

namespace TodoApp.Application.Tests;

/// <summary>
/// Pure unit tests for TodoService — no DB, no HTTP, no infrastructure.
/// All dependencies are mocked with NSubstitute.
/// </summary>
public class TodoServiceTests
{
    private readonly ITodoRepository    _todos;
    private readonly IAuditLogRepository _auditLogs;
    private readonly TodoService        _sut;
    private const string OwnerId = "user-123";

    public TodoServiceTests()
    {
        _todos     = Substitute.For<ITodoRepository>();
        _auditLogs = Substitute.For<IAuditLogRepository>();
        _sut       = new TodoService(_todos, _auditLogs);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsMappedResponse()
    {
        var req = new CreateTodoRequest("Buy milk", null, Priority.Low, null, null);
        _todos.AddAsync(Arg.Any<TodoItem>(), Arg.Any<CancellationToken>())
              .Returns(Task.CompletedTask);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.CreateAsync(OwnerId, req);

        result.Title.Should().Be("Buy milk");
        result.Priority.Should().Be(Priority.Low);
        result.IsCompleted.Should().BeFalse();
        await _todos.Received(1).AddAsync(Arg.Is<TodoItem>(t => t.OwnerId == OwnerId && t.Title == "Buy milk"), Arg.Any<CancellationToken>());
        await _todos.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _auditLogs.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Action == "Created"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_EmptyTitle_ThrowsValidationException()
    {
        var req = new CreateTodoRequest("   ", null, Priority.Medium, null, null);

        await _sut.Invoking(s => s.CreateAsync(OwnerId, req))
                  .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task CreateAsync_TrimsTitle()
    {
        var req = new CreateTodoRequest("  Trimmed  ", null, Priority.Medium, null, null);
        _todos.AddAsync(Arg.Any<TodoItem>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.CreateAsync(OwnerId, req);

        result.Title.Should().Be("Trimmed");
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingTodo_ReturnsMappedResponse()
    {
        var todo = MakeTodo(1, "Task A");
        _todos.GetByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);

        var result = await _sut.GetByIdAsync(1, OwnerId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Title.Should().Be("Task A");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        _todos.GetByIdAsync(99, OwnerId, Arg.Any<CancellationToken>()).Returns((TodoItem?)null);

        var result = await _sut.GetByIdAsync(99, OwnerId);

        result.Should().BeNull();
    }

    // ── ToggleAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleAsync_ActiveTodo_MarksCompleted()
    {
        var todo = MakeTodo(1, "Task", isCompleted: false);
        _todos.GetByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.ToggleAsync(1, OwnerId);

        result.IsCompleted.Should().BeTrue();
        await _auditLogs.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Completed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleAsync_CompletedTodo_MarksActive()
    {
        var todo = MakeTodo(1, "Task", isCompleted: true);
        _todos.GetByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.ToggleAsync(1, OwnerId);

        result.IsCompleted.Should().BeFalse();
        await _auditLogs.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Reopened"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleAsync_NotFound_ThrowsNotFoundException()
    {
        _todos.GetByIdAsync(99, OwnerId, Arg.Any<CancellationToken>()).Returns((TodoItem?)null);

        await _sut.Invoking(s => s.ToggleAsync(99, OwnerId))
                  .Should().ThrowAsync<NotFoundException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesFieldsAndLogs()
    {
        var todo = MakeTodo(1, "Old title");
        _todos.GetByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var req = new UpdateTodoRequest("New title", null, false, Priority.High, null, null);
        var result = await _sut.UpdateAsync(1, OwnerId, req);

        result.Title.Should().Be("New title");
        result.Priority.Should().Be(Priority.High);
        await _auditLogs.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Updated"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_EmptyTitle_ThrowsValidationException()
    {
        var req = new UpdateTodoRequest("", null, false, Priority.Medium, null, null);

        await _sut.Invoking(s => s.UpdateAsync(1, OwnerId, req))
                  .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsNotFoundException()
    {
        _todos.GetByIdAsync(99, OwnerId, Arg.Any<CancellationToken>()).Returns((TodoItem?)null);
        var req = new UpdateTodoRequest("Title", null, false, Priority.Medium, null, null);

        await _sut.Invoking(s => s.UpdateAsync(99, OwnerId, req))
                  .Should().ThrowAsync<NotFoundException>();
    }

    // ── SoftDeleteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteAsync_ExistingTodo_SetsIsDeletedAndLogs()
    {
        var todo = MakeTodo(1, "Task");
        _todos.GetByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.SoftDeleteAsync(1, OwnerId);

        todo.IsDeleted.Should().BeTrue();
        todo.DeletedAt.Should().NotBeNull();
        await _auditLogs.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Deleted"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftDeleteAsync_NotFound_ThrowsNotFoundException()
    {
        _todos.GetByIdAsync(99, OwnerId, Arg.Any<CancellationToken>()).Returns((TodoItem?)null);

        await _sut.Invoking(s => s.SoftDeleteAsync(99, OwnerId))
                  .Should().ThrowAsync<NotFoundException>();
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_DeletedTodo_ClearsIsDeletedAndLogs()
    {
        var todo = MakeTodo(1, "Task", isDeleted: true);
        _todos.GetDeletedByIdAsync(1, OwnerId, Arg.Any<CancellationToken>()).Returns(todo);
        _todos.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _auditLogs.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.RestoreAsync(1, OwnerId);

        todo.IsDeleted.Should().BeFalse();
        todo.DeletedAt.Should().BeNull();
        await _auditLogs.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Restored"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_NotInTrash_ThrowsNotFoundException()
    {
        _todos.GetDeletedByIdAsync(99, OwnerId, Arg.Any<CancellationToken>()).Returns((TodoItem?)null);

        await _sut.Invoking(s => s.RestoreAsync(99, OwnerId))
                  .Should().ThrowAsync<NotFoundException>();
    }

    // ── GetStatsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsMappedCounts()
    {
        _todos.GetStatsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns((10, 4, 2, 1));

        var result = await _sut.GetStatsAsync(OwnerId);

        result.Total.Should().Be(10);
        result.Completed.Should().Be(4);
        result.Active.Should().Be(6);
        result.Overdue.Should().Be(2);
        result.TrashCount.Should().Be(1);
    }

    // ── Pagination clamping ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0,   1)]
    [InlineData(-5,  1)]
    [InlineData(200, 100)]
    public async Task GetPagedAsync_ClampsBoundaries(int requestedPageSize, int expectedPageSize)
    {
        _todos.GetPagedAsync(OwnerId, null, null, null, null, null, false,
            1, expectedPageSize, Arg.Any<CancellationToken>())
              .Returns((new List<TodoItem>(), 0));

        await _sut.GetPagedAsync(OwnerId, null, null, null, null, null, false, 1, requestedPageSize);

        await _todos.Received(1).GetPagedAsync(OwnerId, null, null, null, null, null, false,
            1, expectedPageSize, Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TodoItem MakeTodo(int id, string title,
        bool isCompleted = false, bool isDeleted = false) => new()
    {
        Id          = id,
        Title       = title,
        OwnerId     = OwnerId,
        Priority    = Priority.Medium,
        IsCompleted = isCompleted,
        IsDeleted   = isDeleted,
        CreatedAt   = DateTime.UtcNow,
        UpdatedAt   = DateTime.UtcNow
    };
}
