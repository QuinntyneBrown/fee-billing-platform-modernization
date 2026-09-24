namespace FeeBilling.Invoicing
{
    public interface IInvoicePdfRenderer
    {
        byte[] Render(InvoiceDocument invoice);
    }
}
