using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Assist.Components
{
    [ViewComponent(Name = "PaymentAssist")]
    public class PaymentAssistViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.Assist/Views/PaymentInfo.cshtml");
        }
    }
}
