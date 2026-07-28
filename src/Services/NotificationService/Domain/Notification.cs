using System;
using SharedKernel.Domain;

namespace NotificationService.Domain;

public class Notification : Entity<Guid>
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Channel { get; set; } = string.Empty; 
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; 
    public string Priority { get; set; } = "Normal"; 
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? SentDate { get; set; }
    public int RetryCount { get; set; } = 0;
    public bool IsRead { get; set; } = false;

    public Notification(Guid id, Guid tenantId, Guid userId, string channel, string subject, string message, string priority) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Channel = channel;
        Subject = subject;
        Message = message;
        Priority = priority;
        Status = "Pending";
        CreatedDate = DateTime.UtcNow;
        RetryCount = 0;
        IsRead = false;
    }

#pragma warning disable CS8618
    protected Notification() { }
#pragma warning restore CS8618
}
