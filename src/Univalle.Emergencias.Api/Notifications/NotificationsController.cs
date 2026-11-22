
using Univalle.Emergencias.Api.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Notifications;

[ApiController]
[RequirePermission(PermissionCodes.ReceiveNotifications)]
[Route("api/v1/notifications")]
public sealed class NotificationsController(
    AppDbContext db,
    IIdentityContext identity,
    IServerClock clock) : ControllerBase
{
    // GET /api/v1/notifications/mine
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(
        CancellationToken cancellationToken)
    {
        var user = await identity.GetCurrentAsync(
            cancellationToken);

        if (user is null || !user.IsActive)
            return Unauthorized();

        var notifications = await db.NotificationEntries
            .AsNoTracking()
            .Where(x => x.RecipientUserId == user.UserId)
            .OrderByDescending(x => x.RegisteredAtUtc)
            .ThenByDescending(x => x.EventId)
            .Take(100)
            .Select(x => new
            {
                x.EventId,
                x.IncidentId,
                x.Type,
                x.RegisteredAtUtc,
                x.DeliveryStatus,
                x.DeliveredAtUtc,
                x.ReadAtUtc,
                IsRead = x.ReadAtUtc != null
            })
            .ToListAsync(cancellationToken);

        return Ok(notifications);
    }

    [HttpPatch("{eventId:guid}/delivered")]
    public async Task<IActionResult> ConfirmDelivery(Guid eventId, CancellationToken ct)
    {
        var user = await identity.GetCurrentAsync(ct);
        if (user is null || !user.IsActive) return Unauthorized();
        var mine = db.NotificationEntries.Where(x => x.EventId == eventId && x.RecipientUserId == user.UserId);
        var updated = await mine.Where(x => x.DeliveryStatus == "Pending")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.DeliveryStatus, "Delivered")
                .SetProperty(x => x.DeliveredAtUtc, clock.UtcNow), ct);
        if (updated == 0 && !await mine.AnyAsync(ct)) return NotFound();
        return NoContent();
    }

    // PATCH /api/v1/notifications/{eventId}/read
    [HttpPatch("{eventId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var user = await identity.GetCurrentAsync(
            cancellationToken);

        if (user is null || !user.IsActive)
            return Unauthorized();

        var mine = db.NotificationEntries.Where(x => x.EventId == eventId && x.RecipientUserId == user.UserId);
        var now = clock.UtcNow.ToUniversalTime();
        await mine.Where(x => x.ReadAtUtc == null).ExecuteUpdateAsync(set =>
            set.SetProperty(x => x.ReadAtUtc, now)
                .SetProperty(x => x.DeliveredAtUtc, x => x.DeliveredAtUtc ?? now)
                .SetProperty(x => x.DeliveryStatus, "Delivered"), cancellationToken);
        var notification = await mine.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (notification is null) return NotFound();

        return Ok(new
        {
            notification.EventId,
            notification.ReadAtUtc,
            IsRead = true
        });
    }
}
