using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.Assist.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Assist.Controllers
{
    public class PaymentAssistController : BasePaymentController
    {
        #region Fields

        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ILocalizationService _localizationService;
        private readonly AssistPaymentSettings _assistPaymentSettings;
        private readonly IPermissionService _permissionService;
        private readonly INotificationService _notificationService;

        #endregion

        #region Ctor

        public PaymentAssistController(ISettingService settingService, 
            IPaymentService paymentService, 
            IOrderService orderService, 
            IOrderProcessingService orderProcessingService, 
            IWebHelper webHelper, 
            IWorkContext workContext, 
            ILocalizationService localizationService,
            AssistPaymentSettings assistPaymentSettings,
            IPermissionService permissionService,
            INotificationService notificationService)
        {
            _settingService = settingService;
            _paymentService = paymentService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _webHelper = webHelper;
            _workContext = workContext;
            _localizationService = localizationService;
            _assistPaymentSettings = assistPaymentSettings;
            _permissionService = permissionService;
            _notificationService = notificationService;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                MerchantId = _assistPaymentSettings.MerchantId,
                GatewayUrl = _assistPaymentSettings.GatewayUrl,
                AuthorizeOnly = _assistPaymentSettings.AuthorizeOnly,
                TestMode = _assistPaymentSettings.TestMode,
                AdditionalFee = _assistPaymentSettings.AdditionalFee,
                Login = _assistPaymentSettings.Login,
                Password = _assistPaymentSettings.Password
            };

            return View("~/Plugins/Payments.Assist/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //save settings
            _assistPaymentSettings.GatewayUrl = model.GatewayUrl;
            _assistPaymentSettings.MerchantId = model.MerchantId;
            _assistPaymentSettings.AuthorizeOnly = model.AuthorizeOnly;
            _assistPaymentSettings.TestMode = model.TestMode;
            _assistPaymentSettings.AdditionalFee = model.AdditionalFee;
            _assistPaymentSettings.Login = model.Login;
            _assistPaymentSettings.Password = model.Password;

            await _settingService.SaveSettingAsync(_assistPaymentSettings);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return RedirectToAction("Configure");
        }
        
        public async Task<IActionResult> Fail()
        {
            var order = await _orderService.GetOrderByIdAsync(_webHelper.QueryString<int>("ordernumber"));
            if (order == null || order.Deleted || (await _workContext.GetCurrentCustomerAsync()).Id != order.CustomerId)
                return RedirectToRoute("HomePage");

            return RedirectToRoute("OrderDetails", new { orderId = order.Id });
        }

        public async Task<IActionResult> Return()
        {
            var order = await _orderService.GetOrderByIdAsync(_webHelper.QueryString<int>("ordernumber"));
            if (order == null || order.Deleted || (await _workContext.GetCurrentCustomerAsync()).Id != order.CustomerId)
                return RedirectToRoute("HomePage");

            if (_assistPaymentSettings.AuthorizeOnly)
            {
                if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                {
                    await _orderProcessingService.MarkAsAuthorizedAsync(order);
                }
            }
            else
            {
                if (_orderProcessingService.CanMarkOrderAsPaid(order))
                {
                    await _orderProcessingService.MarkOrderAsPaidAsync(order);
                }
            }

            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }

        #endregion
    }
}