using Auditing;
using Microsoft.Extensions.Options;

namespace ServiceEndpoints.Tests.Support;

public static class TestAudit
{
    /// <summary>
    /// The real <see cref="AuditPublisher"/> over a <see cref="FakeEventBus"/>, so tests assert on the integration event
    /// that would reach RabbitMQ (including the request details the publisher adds) rather than on a stub.
    /// </summary>
    public static IAuditPublisher For(FakeEventBus bus, string serviceName) =>
        new AuditPublisher(bus, Options.Create(new AuditPublisherOptions { ServiceName = serviceName }));
}
