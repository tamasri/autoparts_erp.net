using System.Diagnostics.Metrics;

namespace AutoPartsERP.Infrastructure.Observability;

public sealed class ErpMetrics
{
    private readonly Counter<long> _invoicesPosted;
    private readonly Counter<long> _paymentsReceived;
    private readonly Histogram<double> _invoiceValue;
    private readonly Counter<long> _outboxProcessed;
    private readonly Counter<long> _outboxFailed;

    public ErpMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("AutoPartsERP");
        _invoicesPosted = meter.CreateCounter<long>("erp.invoices.posted");
        _paymentsReceived = meter.CreateCounter<long>("erp.payments.received");
        _invoiceValue = meter.CreateHistogram<double>("erp.invoice.value_syp");
        _outboxProcessed = meter.CreateCounter<long>("erp.outbox.processed");
        _outboxFailed = meter.CreateCounter<long>("erp.outbox.failed");
    }

    public void RecordInvoicePosted(decimal totalSyp)
    {
        _invoicesPosted.Add(1);
        _invoiceValue.Record((double)totalSyp);
    }

    public void RecordPaymentReceived() => _paymentsReceived.Add(1);

    public void RecordOutboxProcessed() => _outboxProcessed.Add(1);

    public void RecordOutboxFailed() => _outboxFailed.Add(1);
}
