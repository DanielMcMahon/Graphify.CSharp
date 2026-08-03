using SampleApp.Domain;
using SampleApp.Web.Security;
using SampleApp.Web.Ui;

namespace SampleApp.Web;

public partial class OrderDetailsPage : UiPageBase
{
    public void Page_Load(Order order)
    {
        if (UserContext.HasPermission("ViewInvoices"))
        {
            if (order.Invoice is not null)
            {
                var invoiceWell = new UiFragment
                {
                    Id = "invoice-well",
                    Label = "Invoice"
                };
                Add(invoiceWell);
                Add(new UiLink
                {
                    Id = "invoice-link",
                    Label = "Download invoice"
                });
            }
        }

        Add(new UiFileUpload
        {
            Id = "order-attachment-upload",
            Label = "Upload attachment"
        });
    }
}
