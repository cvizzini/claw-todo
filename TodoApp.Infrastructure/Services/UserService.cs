using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Exceptions;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Services;

public class UserService(
    UserManager<AppUser> userManager,
    TodoDbContext db) : IUserService
{
    // ── Cross-tenant (Admin) ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserSummary>> GetAllAsync(int? tenantId = null, CancellationToken ct = default)
    {
        var query = userManager.Users.AsNoTracking().Include(u => u.Tenant).AsQueryable();
        if (tenantId.HasValue) query = query.Where(u => u.TenantId == tenantId);
        var users = await query.OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return await ToSummariesAsync(users);
    }

    public async Task<UserSummary?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var user = await userManager.Users.AsNoTracking()
            .Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;
        return await ToSummaryAsync(user);
    }

    public async Task<UserSummary> CreateAsync(AdminCreateUserRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            throw new ValidationException("Email and password are required.");

        var role = req.Role is "Administrator" or "TenantAdmin" or "User" ? req.Role : "User";

        if (role != "Administrator" && req.TenantId is null)
            throw new ValidationException("TenantId is required for User and TenantAdmin roles.");

        if (req.TenantId.HasValue && !await db.Tenants.AnyAsync(t => t.Id == req.TenantId && t.IsActive, ct))
            throw new ValidationException("Tenant not found or is inactive.");

        if (await userManager.FindByEmailAsync(req.Email) is not null)
            throw new ConflictException("Email already registered.");

        var user = new AppUser
        {
            UserName    = req.Email,
            Email       = req.Email,
            DisplayName = req.DisplayName ?? req.Email.Split('@')[0],
            CreatedAt   = DateTime.UtcNow,
            TenantId    = role == "Administrator" ? null : req.TenantId
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            throw new ValidationException(result.Errors.Select(e => e.Description));

        await userManager.AddToRoleAsync(user, role);
        var tenant = user.TenantId.HasValue ? await db.Tenants.FindAsync([user.TenantId], ct) : null;
        return new UserSummary(user.Id, user.Email!, user.DisplayName, role, user.CreatedAt, false, user.TenantId, tenant?.Name);
    }

    public async Task<UserSummary> UpdateAsync(string id, AdminUpdateUserRequest req, CancellationToken ct = default)
    {
        var user = await userManager.Users.Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User not found.");

        if (!string.IsNullOrWhiteSpace(req.DisplayName))
            user.DisplayName = req.DisplayName.Trim();

        if (req.TenantId.HasValue)
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == req.TenantId, ct))
                throw new ValidationException("Tenant not found.");
            user.TenantId = req.TenantId;
        }

        if (!string.IsNullOrEmpty(req.Role) && req.Role is "Administrator" or "TenantAdmin" or "User")
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            if (!currentRoles.Contains(req.Role))
            {
                await userManager.RemoveFromRolesAsync(user, currentRoles);
                await userManager.AddToRoleAsync(user, req.Role);
            }
            if (req.Role == "Administrator") user.TenantId = null;
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            throw new ValidationException(updateResult.Errors.Select(e => e.Description));

        return await ToSummaryAsync(user);
    }

    public async Task DeleteAsync(string id, string callerId, CancellationToken ct = default)
    {
        if (id == callerId) throw new ValidationException("You cannot delete your own account.");

        var user = await userManager.FindByIdAsync(id)
            ?? throw new NotFoundException("User not found.");

        await PurgeUserDataAsync(id, ct);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new ValidationException(result.Errors.Select(e => e.Description));
    }

    public async Task LockAsync(string id, string callerId, CancellationToken ct = default)
    {
        if (id == callerId) throw new ValidationException("You cannot lock your own account.");
        var user = await userManager.FindByIdAsync(id) ?? throw new NotFoundException("User not found.");
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
    }

    public async Task UnlockAsync(string id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id) ?? throw new NotFoundException("User not found.");
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
    }

    // ── Tenant-scoped ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserSummary>> GetByTenantAsync(int tenantId, CancellationToken ct = default)
    {
        var users = await userManager.Users.AsNoTracking()
            .Include(u => u.Tenant)
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);
        return await ToSummariesAsync(users);
    }

    public async Task<UserSummary?> GetByIdInTenantAsync(string id, int tenantId, CancellationToken ct = default)
    {
        var user = await userManager.Users.AsNoTracking()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);
        return user is null ? null : await ToSummaryAsync(user);
    }

    public async Task<UserSummary> CreateInTenantAsync(int tenantId, TenantCreateUserRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            throw new ValidationException("Email and password are required.");

        var role = req.Role is "TenantAdmin" or "User" ? req.Role : "User";

        if (await userManager.FindByEmailAsync(req.Email) is not null)
            throw new ConflictException("Email already registered.");

        var user = new AppUser
        {
            UserName    = req.Email,
            Email       = req.Email,
            DisplayName = req.DisplayName ?? req.Email.Split('@')[0],
            CreatedAt   = DateTime.UtcNow,
            TenantId    = tenantId
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            throw new ValidationException(result.Errors.Select(e => e.Description));

        await userManager.AddToRoleAsync(user, role);
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        return new UserSummary(user.Id, user.Email!, user.DisplayName, role, user.CreatedAt, false, tenantId, tenant?.Name);
    }

    public async Task<UserSummary> UpdateInTenantAsync(int tenantId, string id, TenantUpdateUserRequest req, string callerId, CancellationToken ct = default)
    {
        var user = await userManager.Users.Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct)
            ?? throw new NotFoundException("User not found.");

        if (!string.IsNullOrWhiteSpace(req.DisplayName))
            user.DisplayName = req.DisplayName.Trim();

        if (!string.IsNullOrEmpty(req.Role) && req.Role is "TenantAdmin" or "User")
        {
            if (id == callerId && req.Role == "User")
                throw new ValidationException("You cannot remove your own TenantAdmin role.");

            var currentRoles = await userManager.GetRolesAsync(user);
            if (!currentRoles.Contains(req.Role))
            {
                await userManager.RemoveFromRolesAsync(user, currentRoles);
                await userManager.AddToRoleAsync(user, req.Role);
            }
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            throw new ValidationException(updateResult.Errors.Select(e => e.Description));

        return await ToSummaryAsync(user);
    }

    public async Task DeleteInTenantAsync(int tenantId, string id, string callerId, CancellationToken ct = default)
    {
        if (id == callerId) throw new ValidationException("You cannot delete your own account.");

        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct)
            ?? throw new NotFoundException("User not found.");

        await PurgeUserDataAsync(id, ct);
        await userManager.DeleteAsync(user);
    }

    public async Task LockInTenantAsync(int tenantId, string id, string callerId, CancellationToken ct = default)
    {
        if (id == callerId) throw new ValidationException("You cannot lock your own account.");
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct)
            ?? throw new NotFoundException("User not found.");
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
    }

    public async Task UnlockInTenantAsync(int tenantId, string id, CancellationToken ct = default)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct)
            ?? throw new NotFoundException("User not found.");
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<UserSummary> ToSummaryAsync(AppUser user)
    {
        var roles    = await userManager.GetRolesAsync(user);
        var isLocked = await userManager.IsLockedOutAsync(user);
        return new UserSummary(
            user.Id, user.Email ?? "", user.DisplayName,
            roles.FirstOrDefault() ?? "User",
            user.CreatedAt, isLocked,
            user.TenantId, user.Tenant?.Name);
    }

    private async Task<List<UserSummary>> ToSummariesAsync(IEnumerable<AppUser> users)
    {
        var result = new List<UserSummary>();
        foreach (var u in users)
            result.Add(await ToSummaryAsync(u));
        return result;
    }

    private async Task PurgeUserDataAsync(string userId, CancellationToken ct)
    {
        var todos   = await db.Todos.IgnoreQueryFilters().Where(t => t.OwnerId == userId).ToListAsync(ct);
        var todoIds = todos.Select(t => t.Id).ToList();
        if (todoIds.Count > 0)
        {
            var logs = await db.AuditLogs.Where(a => todoIds.Contains(a.TodoId)).ToListAsync(ct);
            db.AuditLogs.RemoveRange(logs);
            db.Todos.RemoveRange(todos);
        }
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct);
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync(ct);
    }
}
