using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Plugins;
using Nop.Plugin.Payments.Assist.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;
using System.Threading.Tasks;
using Nop.Services.Common;

namespace Nop.Plugin.Payments.Assist
{
    /// <summary>
    /// Assist payment processor
    /// </summary>
    public class AssistPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly ICurrencyService _currencyService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly CurrencySettings _currencySettings;
        private readonly AssistPaymentSettings _assistPaymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IAddressService _addressService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ICountryService _countryService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private const string TEST_ASSIST_PAYMENT_URL = "https://test.paysecure.ru/";
        private const string PAYMENT_COMMAND = "pay/order.cfm";
        private const string ORDERSTATE_COMMEND = "orderstate/orderstate.cfm";

        #endregion

        #region Ctor

        public AssistPaymentProcessor(ICurrencyService currencyService,
            ISettingService settingService,
            IWebHelper webHelper,
            AssistPaymentSettings assistPaymentSettings,
            CurrencySettings currencySettings,
            ILocalizationService localizationService,
            IAddressService addressService,
            IStateProvinceService stateProvinceService,
            ICountryService countryService, 
            IHttpContextAccessor httpContextAccessor)
        {
            _currencyService = currencyService;
            _settingService = settingService;
            _webHelper = webHelper;
            _assistPaymentSettings = assistPaymentSettings;
            _currencySettings = currencySettings;
            _localizationService = localizationService;
            _addressService = addressService;
            _stateProvinceService = stateProvinceService;
            _countryService = countryService;
            _httpContextAccessor = httpContextAccessor;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Check payment status
        /// </summary>
        /// <param name="order">Order for check payment status</param>
        /// <returns>True if payment status is Approved, Felse - Otherwise</returns>
        public bool CheckPaymentStatus(Order order)
        {
            var searchFrom = order.CreatedOnUtc;

            //create and send post data
            var postData = new NameValueCollection
            {
                { "Merchant_ID", _assistPaymentSettings.MerchantId },
                { "Login", _assistPaymentSettings.Login },
                { "Password", _assistPaymentSettings.Password },
                { "OrderNumber", order.Id.ToString() },
                { "StartYear", searchFrom.Year.ToString() },
                { "StartMonth", searchFrom.Month.ToString() },
                { "StartDay", searchFrom.Day.ToString() },
                { "StartHour", "0" },
                { "StartMin", "0" },
                // response on XML format 
                { "Format", "3" }
            };

            byte[] data;
            using (var client = new WebClient())
            {
                data = client.UploadValues(GetUrl(ORDERSTATE_COMMEND), postData);
            }

            using (var ms = new MemoryStream(data))
            {
                using (var sr = new StreamReader(ms))
                {
                    var rez = sr.ReadToEnd();

                    if (!rez.Contains("?xml"))
                        return false;

                    try
                    {
                        var doc = XDocument.Parse(rez);
                        var orderElement = doc.Root?.Element("order") ?? new XElement("order");

                        var flag = string.Format(CultureInfo.InvariantCulture, "{0:0.00}", order.OrderTotal) == (orderElement.Element("orderamount") ?? new XElement("orderamount", "0.00")).Value;
                        flag = flag && (orderElement.Element("orderstate") ?? new XElement("orderstate")).Value == "Approved";

                        return flag;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public string GetUrl(string command)
        {
            var server = (_assistPaymentSettings.TestMode ? TEST_ASSIST_PAYMENT_URL : _assistPaymentSettings.GatewayUrl).TrimEnd('/');

            return $"{server}/{command}";
        }

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };

            return Task.FromResult(result);
        }

        public  Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            var warnings = new List<string>();

            return Task.FromResult<IList<string>>(warnings);
        }

        public  Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();

            return Task.FromResult(paymentInfo);
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentAssist/Configure";
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var post = new RemotePost(_httpContextAccessor,_webHelper)
            {
                FormName = "AssistPaymentForm",
                Url = GetUrl(PAYMENT_COMMAND),
                Method = "POST"
            };

            var billingAddress = await _addressService.GetAddressByIdAsync(postProcessPaymentRequest.Order.BillingAddressId);
            post.Add("Merchant_ID", _assistPaymentSettings.MerchantId);
            post.Add("Delay", _assistPaymentSettings.AuthorizeOnly ? "1" : "0");
            post.Add("OrderNumber", postProcessPaymentRequest.Order.Id.ToString());
            post.Add("OrderAmount", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", postProcessPaymentRequest.Order.OrderTotal));
            post.Add("OrderCurrency", (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId)).CurrencyCode);
            post.Add("URL_RETURN", $"{_webHelper.GetStoreLocation()}Plugins/PaymentAssist/Fail");
            post.Add("URL_RETURN_OK", $"{_webHelper.GetStoreLocation()}Plugins/PaymentAssist/Return");
            post.Add("FirstName", billingAddress.FirstName);
            post.Add("LastName", billingAddress.LastName);
            post.Add("Email", billingAddress.Email);
            post.Add("Address", billingAddress.Address1);
            post.Add("City", billingAddress.City);
            post.Add("Zip", billingAddress.ZipPostalCode);
            post.Add("Phone", billingAddress.PhoneNumber);

            var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);

            if (state != null)
                post.Add("State", state.Abbreviation);

            var country = await _countryService.GetCountryByIdAsync(billingAddress.CountryId ?? 0);

            if (country != null)
                post.Add("Country", country.ThreeLetterIsoCode);

            post.Post();
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public  Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public  Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(_assistPaymentSettings.AdditionalFee);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public  Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();

            result.AddError("Capture method not supported");

            return Task.FromResult(result);
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public  Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();

            result.AddError("Refund method not supported");

            return Task.FromResult(result);
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public  Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();

            result.AddError("Void method not supported");

            return Task.FromResult(result);
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public  Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();

            result.AddError("Recurring payment not supported");

            return Task.FromResult(result);
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();

            result.AddError("Recurring payment not supported");

            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public  Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //Assist is the redirection payment method
            //It also validates whether order is also paid (after redirection) so customers will not be able to pay twice
            
            //payment status should be Pending
            if (order.PaymentStatus != PaymentStatus.Pending)
                return Task.FromResult(false);

            //let's ensure that at least 1 minute passed after order is placed
            return Task.FromResult(!((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes < 1));
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentAssist";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Assist.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentAssist";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Assist.Controllers" }, { "area", null } };
        }

        public Type GetControllerType()
        {
            return typeof(PaymentAssistController);
        }

        public override  async Task InstallAsync()
        {
            var settings = new AssistPaymentSettings
            {
                GatewayUrl = TEST_ASSIST_PAYMENT_URL,
                MerchantId = "",
                AuthorizeOnly = false,
                TestMode = true,
                AdditionalFee = 0
            };

            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.RedirectionTip", "You will be redirected to Assist site to complete the order.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.GatewayUrl", "Gateway URL");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.GatewayUrl.Hint", "Enter gateway URL.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.MerchantId", "Merchant ID");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.MerchantId.Hint", "Enter your merchant identifier.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.AuthorizeOnly", "Authorize only");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.AuthorizeOnly.Hint", "Authorize only?");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.TestMode", "Test mode");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.TestMode.Hint", "Is test mode?");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.AdditionalFee", "Additional fee");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.PaymentMethodDescription", "You will be redirected to Assist site to complete the order.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.Password", "Password");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.Password.Hint", "Set the password.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.Login", "Login");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Assist.Login.Hint", "Set the login.");

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            //locales
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.RedirectionTip");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.GatewayUrl");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.GatewayUrl.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.MerchantId");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.MerchantId.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.AuthorizeOnly");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.AuthorizeOnly.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.TestMode");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.TestMode.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.AdditionalFee");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.AdditionalFee.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.PaymentMethodDescription");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.Password");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.Password.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.Login");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Assist.Login.Hint");

            await base.UninstallAsync();
        }

        public string GetPublicViewComponentName()
        {
            return "PaymentAssist";
        }

        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get
            {
                return RecurringPaymentType.NotSupported;
            }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get
            {
                return PaymentMethodType.Redirection;
            }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Assist.PaymentMethodDescription"); 
        }

        #endregion
    }
}
