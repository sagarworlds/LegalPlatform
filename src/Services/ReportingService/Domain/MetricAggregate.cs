using System;
using SharedKernel.Domain;

namespace ReportingService.Domain;

public class MetricAggregate : Entity<Guid>
{
    public Guid TenantId { get; set; }
    public string MetricName { get; set; } = string.Empty; 
    public int Value { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public MetricAggregate(Guid id, Guid tenantId, string metricName, int value) : base(id)
    {
        TenantId = tenantId;
        MetricName = metricName;
        Value = value;
        Timestamp = DateTime.UtcNow;
    }

#pragma warning disable CS8618
    protected MetricAggregate() { }
#pragma warning restore CS8618
}
