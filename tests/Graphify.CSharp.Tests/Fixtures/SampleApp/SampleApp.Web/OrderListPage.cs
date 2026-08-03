using SampleApp.Web.Ui;

namespace SampleApp.Web;

public sealed class OrderListPage : UiPageBase
{
    public void Page_Load()
    {
        Add(new UiLink
        {
            Id = "order-details-link",
            Label = "Open order"
        });
    }

    public void OpenOrder(string orderId)
    {
        Navigation.Redirect($"~/Orders/OrderDetails.aspx?id={orderId}");
    }
}
